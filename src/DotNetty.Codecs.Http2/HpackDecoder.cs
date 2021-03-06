﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Codecs.Http2
{
    using System;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using DotNetty.Buffers;
    using DotNetty.Common.Utilities;

    sealed class HpackDecoder
    {
        internal static readonly Http2Exception DecodeULE128DecompressionException =
            Http2Exception.ConnectionError(Http2Error.CompressionError, "HPACK - decompression failure");

        internal static readonly Http2Exception DecodeULE128ToLongDecompressionException =
            Http2Exception.ConnectionError(Http2Error.CompressionError, "HPACK - long overflow");

        internal static readonly Http2Exception DecodeULE128ToIntDecompressionException =
            Http2Exception.ConnectionError(Http2Error.CompressionError, "HPACK - int overflow");

        internal static readonly Http2Exception DecodeIllegalIndexValue =
            Http2Exception.ConnectionError(Http2Error.CompressionError, "HPACK - illegal index value");

        internal static readonly Http2Exception IndexHeaderIllegalIndexValue =
            Http2Exception.ConnectionError(Http2Error.CompressionError, "HPACK - illegal index value");

        internal static readonly Http2Exception ReadNameIllegalIndexValue =
            Http2Exception.ConnectionError(Http2Error.CompressionError, "HPACK - illegal index value");

        internal static readonly Http2Exception InvalidMaxDynamicTableSize =
            Http2Exception.ConnectionError(Http2Error.CompressionError, "HPACK - invalid max dynamic table size");

        internal static readonly Http2Exception MaxDynamicTableSizeChangeRequired =
            Http2Exception.ConnectionError(Http2Error.CompressionError, "HPACK - max dynamic table size change required");

        const byte ReadHeaderRepresentation = 0;

        const byte ReadMaxDynamicTableSize = 1;

        const byte ReadIndexedHeader = 2;

        const byte ReadIndexedHeaderName = 3;

        const byte ReadLiteralHeaderNameLengthPrefix = 4;

        const byte ReadLiteralHeaderNameLength = 5;

        const byte ReadLiteralHeaderName = 6;

        const byte ReadLiteralHeaderValueLengthPrefix = 7;

        const byte ReadLiteralHeaderValueLength = 8;

        const byte ReadLiteralHeaderValue = 9;

        readonly HpackDynamicTable hpackDynamicTable;

        readonly HpackHuffmanDecoder hpackHuffmanDecoder;

        long maxHeaderListSize;
        long maxDynamicTableSize;
        long encoderMaxDynamicTableSize;
        bool maxDynamicTableSizeChangeRequired;

        /// <summary>
        /// Create a new instance.
        /// </summary>
        /// <param name="maxHeaderListSize">maxHeaderListSize This is the only setting that can be configured before notifying the peer.
        /// This is because <a href="https://tools.ietf.org/html/rfc7540#section-6.5.1">SETTINGS_Http2CodecUtil.MAX_HEADER_LIST_SIZE</a>
        /// allows a lower than advertised limit from being enforced, and the default limit is unlimited
        /// (which is dangerous).</param>
        /// <param name="initialHuffmanDecodeCapacity">Size of an intermediate buffer used during huffman decode.</param>
        internal HpackDecoder(long maxHeaderListSize, int initialHuffmanDecodeCapacity)
            : this(maxHeaderListSize, initialHuffmanDecodeCapacity, Http2CodecUtil.DefaultHeaderTableSize)
        {

        }

        /// <summary>
        /// Exposed Used for testing only! Default values used in the initial settings frame are overridden intentionally
        /// for testing but violate the RFC if used outside the scope of testing.
        /// </summary>
        /// <param name="maxHeaderListSize"></param>
        /// <param name="initialHuffmanDecodeCapacity"></param>
        /// <param name="maxHeaderTableSize"></param>
        internal HpackDecoder(long maxHeaderListSize, int initialHuffmanDecodeCapacity, int maxHeaderTableSize)
        {
            if (maxHeaderListSize <= 0) { ThrowHelper.ThrowArgumentException_Positive(maxHeaderListSize, ExceptionArgument.maxHeaderListSize); }
            this.maxHeaderListSize = maxHeaderListSize;

            this.maxDynamicTableSize = this.encoderMaxDynamicTableSize = maxHeaderTableSize;
            this.maxDynamicTableSizeChangeRequired = false;
            this.hpackDynamicTable = new HpackDynamicTable(maxHeaderTableSize);
            this.hpackHuffmanDecoder = new HpackHuffmanDecoder(initialHuffmanDecodeCapacity);
        }

        /// <summary>
        /// Decode the header block into header fields.
        /// <para>This method assumes the entire header block is contained in <paramref name="input"/>.</para>
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="input"></param>
        /// <param name="headers"></param>
        /// <param name="validateHeaders"></param>
        public void Decode(int streamId, IByteBuffer input, IHttp2Headers headers, bool validateHeaders)
        {
            var sink = new Http2HeadersSink(streamId, headers, this.maxHeaderListSize, validateHeaders);
            this.Decode(input, sink);

            // Now that we've read all of our headers we can perform the validation steps. We must
            // delay throwing until this point to prevent dynamic table corruption.
            sink.Finish();
        }

        public void Decode(IByteBuffer input, ISink sink)
        {
            int index = 0;
            int nameLength = 0;
            int valueLength = 0;
            byte state = ReadHeaderRepresentation;
            bool huffmanEncoded = false;
            ICharSequence name = null;

            HpackUtil.IndexType indexType = HpackUtil.IndexType.None;
            while (input.IsReadable())
            {
                switch (state)
                {
                    case ReadHeaderRepresentation:
                        byte b = input.ReadByte();
                        if (this.maxDynamicTableSizeChangeRequired && (b & 0xE0) != 0x20)
                        {
                            // HpackEncoder MUST signal maximum dynamic table size change
                            ThrowHelper.ThrowHttp2Exception_MaxDynamicTableSizeChangeRequired();
                        }

                        if (b > 127)
                        {
                            // Indexed Header Field
                            index = b & 0x7F;
                            switch (index)
                            {
                                case 0:
                                    ThrowHelper.ThrowHttp2Exception_DecodeIllegalIndexValue();
                                    break;
                                case 0x7F:
                                    state = ReadIndexedHeader;
                                    break;
                                default:
                                    HpackHeaderField idxHeader = this.GetIndexedHeader(index);
                                    sink.AppendToHeaderList(idxHeader.name, idxHeader.value);
                                    break;
                            }
                        }
                        else if ((b & 0x40) == 0x40)
                        {
                            // Literal Header Field with Incremental Indexing
                            indexType = HpackUtil.IndexType.Incremental;
                            index = b & 0x3F;
                            switch (index)
                            {
                                case 0:
                                    state = ReadLiteralHeaderNameLengthPrefix;
                                    break;
                                case 0x3F:
                                    state = ReadIndexedHeaderName;
                                    break;
                                default:
                                    // Index was stored as the prefix
                                    name = this.ReadName(index);
                                    nameLength = name.Count;
                                    state = ReadLiteralHeaderValueLengthPrefix;
                                    break;
                            }
                        }
                        else if ((b & 0x20) == 0x20)
                        {
                            // Dynamic Table Size Update
                            index = b & 0x1F;
                            if (index == 0x1F)
                            {
                                state = ReadMaxDynamicTableSize;
                            }
                            else
                            {
                                this.SetDynamicTableSize(index);
                                state = ReadHeaderRepresentation;
                            }
                        }
                        else
                        {
                            // Literal Header Field without Indexing / never Indexed
                            indexType = ((b & 0x10) == 0x10) ? HpackUtil.IndexType.Never : HpackUtil.IndexType.None;
                            index = b & 0x0F;
                            switch (index)
                            {
                                case 0:
                                    state = ReadLiteralHeaderNameLengthPrefix;
                                    break;
                                case 0x0F:
                                    state = ReadIndexedHeaderName;
                                    break;
                                default:
                                    // Index was stored as the prefix
                                    name = this.ReadName(index);
                                    nameLength = name.Count;
                                    state = ReadLiteralHeaderValueLengthPrefix;
                                    break;
                            }
                        }

                        break;

                    case ReadMaxDynamicTableSize:
                        this.SetDynamicTableSize(DecodeULE128(input, (long)index));
                        state = ReadHeaderRepresentation;
                        break;

                    case ReadIndexedHeader:
                        HpackHeaderField indexedHeader = this.GetIndexedHeader(DecodeULE128(input, index));
                        sink.AppendToHeaderList(indexedHeader.name, indexedHeader.value);
                        state = ReadHeaderRepresentation;
                        break;

                    case ReadIndexedHeaderName:
                        // Header Name matches an entry in the Header Table
                        name = this.ReadName(DecodeULE128(input, index));
                        nameLength = name.Count;
                        state = ReadLiteralHeaderValueLengthPrefix;
                        break;

                    case ReadLiteralHeaderNameLengthPrefix:
                        b = input.ReadByte();
                        huffmanEncoded = (b & 0x80) == 0x80;
                        index = b & 0x7F;
                        if (index == 0x7f)
                        {
                            state = ReadLiteralHeaderNameLength;
                        }
                        else
                        {
                            nameLength = index;
                            state = ReadLiteralHeaderName;
                        }

                        break;

                    case ReadLiteralHeaderNameLength:
                        // Header Name is a Literal String
                        nameLength = DecodeULE128(input, index);
                        state = ReadLiteralHeaderName;
                        break;

                    case ReadLiteralHeaderName:
                        // Wait until entire name is readable
                        if (input.ReadableBytes < nameLength)
                        {
                            ThrowHelper.ThrowArgumentException_NotEnoughData(input);
                        }

                        name = this.ReadStringLiteral(input, nameLength, huffmanEncoded);

                        state = ReadLiteralHeaderValueLengthPrefix;
                        break;

                    case ReadLiteralHeaderValueLengthPrefix:
                        b = input.ReadByte();
                        huffmanEncoded = (b & 0x80) == 0x80;
                        index = b & 0x7F;
                        switch (index)
                        {
                            case 0x7f:
                                state = ReadLiteralHeaderValueLength;
                                break;
                            case 0:
                                this.InsertHeader(sink, name, AsciiString.Empty, indexType);
                                state = ReadHeaderRepresentation;
                                break;
                            default:
                                valueLength = index;
                                state = ReadLiteralHeaderValue;
                                break;
                        }

                        break;

                    case ReadLiteralHeaderValueLength:
                        // Header Value is a Literal String
                        valueLength = DecodeULE128(input, index);
                        state = ReadLiteralHeaderValue;
                        break;

                    case ReadLiteralHeaderValue:
                        // Wait until entire value is readable
                        if (input.ReadableBytes < valueLength)
                        {
                            ThrowHelper.ThrowArgumentException_NotEnoughData(input);
                        }

                        ICharSequence value = this.ReadStringLiteral(input, valueLength, huffmanEncoded);
                        this.InsertHeader(sink, name, value, indexType);
                        state = ReadHeaderRepresentation;
                        break;

                    default:
                        ThrowHelper.ThrowException_ShouldNotReachHere(state);
                        break;
                }
            }

            if (state != ReadHeaderRepresentation)
            {
                ThrowHelper.ThrowConnectionError_IncompleteHeaderBlockFragment();
            }
        }

        /// <summary>
        /// Set the maximum table size. If this is below the maximum size of the dynamic table used by
        /// the encoder, the beginning of the next header block MUST signal this change.
        /// </summary>
        /// <param name="maxHeaderTableSize"></param>
        public void SetMaxHeaderTableSize(long maxHeaderTableSize)
        {
            if (maxHeaderTableSize < Http2CodecUtil.MinHeaderTableSize || maxHeaderTableSize > Http2CodecUtil.MaxHeaderTableSize)
            {
                ThrowHelper.ThrowConnectionError_SetMaxHeaderTableSize(maxHeaderTableSize);
            }

            this.maxDynamicTableSize = maxHeaderTableSize;
            if (this.maxDynamicTableSize < this.encoderMaxDynamicTableSize)
            {
                // decoder requires less space than encoder
                // encoder MUST signal this change
                this.maxDynamicTableSizeChangeRequired = true;
                this.hpackDynamicTable.SetCapacity(this.maxDynamicTableSize);
            }
        }

        [Obsolete("=> SetMaxHeaderListSize(long maxHeaderListSize)")]
        public void SetMaxHeaderListSize(long maxHeaderListSize, long maxHeaderListSizeGoAway)
        {
            this.SetMaxHeaderListSize(maxHeaderListSize);
        }

        public void SetMaxHeaderListSize(long maxHeaderListSize)
        {
            if (maxHeaderListSize < Http2CodecUtil.MinHeaderListSize || maxHeaderListSize > Http2CodecUtil.MaxHeaderListSize)
            {
                ThrowHelper.ThrowConnectionError_SetMaxHeaderListSize(maxHeaderListSize);
            }
            this.maxHeaderListSize = maxHeaderListSize;
        }

        public long GetMaxHeaderListSize()
        {
            return this.maxHeaderListSize;
        }

        /// <summary>
        /// Return the maximum table size. This is the maximum size allowed by both the encoder and the
        /// decoder.
        /// </summary>
        /// <returns></returns>
        public long GetMaxHeaderTableSize()
        {
            return this.hpackDynamicTable.Capacity();
        }

        /// <summary>
        /// Return the number of header fields input the dynamic table. Exposed for testing.
        /// </summary>
        /// <returns></returns>
        internal int Length()
        {
            return this.hpackDynamicTable.Length();
        }

        /// <summary>
        /// Return the size of the dynamic table. Exposed for testing.
        /// </summary>
        /// <returns></returns>
        internal long Size()
        {
            return this.hpackDynamicTable.Size();
        }

        /// <summary>
        /// Return the header field at the given index. Exposed for testing.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        internal HpackHeaderField GetHeaderField(int index)
        {
            return this.hpackDynamicTable.GetEntry(index + 1);
        }

        private void SetDynamicTableSize(long dynamicTableSize)
        {
            if (dynamicTableSize > this.maxDynamicTableSize)
            {
                ThrowHelper.ThrowHttp2Exception_InvalidMaxDynamicTableSize();
            }

            this.encoderMaxDynamicTableSize = dynamicTableSize;
            this.maxDynamicTableSizeChangeRequired = false;
            this.hpackDynamicTable.SetCapacity(dynamicTableSize);
        }

        internal static HeaderType Validate(int streamId, ICharSequence name, HeaderType? previousHeaderType)
        {
            if (PseudoHeaderName.HasPseudoHeaderFormat(name))
            {
                if (previousHeaderType == HeaderType.RegularHeader)
                {
                    ThrowHelper.ThrowStreamError_AfterRegularHeader(streamId, name);
                }

                var pseudoHeader = PseudoHeaderName.GetPseudoHeader(name);
                if (pseudoHeader == null)
                {
                    ThrowHelper.ThrowStreamError_InvalidPseudoHeader(streamId, name);
                }

                HeaderType currentHeaderType = pseudoHeader.IsRequestOnly ?
                        HeaderType.RequestPseudoHeader : HeaderType.ResponsePseudoHeader;
                if (previousHeaderType.HasValue && currentHeaderType != previousHeaderType.Value)
                {
                    ThrowHelper.ThrowStreamError_MixOfRequest(streamId);
                }

                return currentHeaderType;
            }

            return HeaderType.RegularHeader;
        }

        private ICharSequence ReadName(int index)
        {
            if (index <= HpackStaticTable.Length)
            {
                HpackHeaderField hpackHeaderField = HpackStaticTable.GetEntry(index);
                return hpackHeaderField.name;
            }

            if (index - HpackStaticTable.Length <= this.hpackDynamicTable.Length())
            {
                HpackHeaderField hpackHeaderField = this.hpackDynamicTable.GetEntry(index - HpackStaticTable.Length);
                return hpackHeaderField.name;
            }

            ThrowHelper.ThrowHttp2Exception_ReadNameIllegalIndexValue(); return null;
        }

        private HpackHeaderField GetIndexedHeader(int index)
        {
            if (index <= HpackStaticTable.Length)
            {
                return HpackStaticTable.GetEntry(index);
            }
            if (index - HpackStaticTable.Length <= this.hpackDynamicTable.Length())
            {
                return this.hpackDynamicTable.GetEntry(index - HpackStaticTable.Length);
            }
            ThrowHelper.ThrowHttp2Exception_IndexHeaderIllegalIndexValue(); return null;
        }

        private void InsertHeader(ISink sink, ICharSequence name, ICharSequence value, HpackUtil.IndexType indexType)
        {
            sink.AppendToHeaderList(name, value);

            switch (indexType)
            {
                case HpackUtil.IndexType.None:
                case HpackUtil.IndexType.Never:
                    break;

                case HpackUtil.IndexType.Incremental:
                    this.hpackDynamicTable.Add(new HpackHeaderField(name, value));
                    break;

                default:
                    ThrowHelper.ThrowException_ShouldNotReachHere();
                    break;
            }
        }

        private ICharSequence ReadStringLiteral(IByteBuffer input, int length, bool huffmanEncoded)
        {
            if (huffmanEncoded)
            {
                return this.hpackHuffmanDecoder.Decode(input, length);
            }

            byte[] buf = new byte[length];
            input.ReadBytes(buf);
            return new AsciiString(buf, false);
        }

        /// <summary>
        /// Unsigned Little Endian Base 128 Variable-Length Integer Encoding
        /// <para>Visible for testing only!</para>
        /// </summary>
        /// <param name="input"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        internal static int DecodeULE128(IByteBuffer input, int result)
        {
            int readerIndex = input.ReaderIndex;
            long v = DecodeULE128(input, (long)result);
            if (v > int.MaxValue)
            {
                // the maximum value that can be represented by a signed 32 bit number is:
                // [0x1,0x7f] + 0x7f + (0x7f << 7) + (0x7f << 14) + (0x7f << 21) + (0x6 << 28)
                // OR
                // 0x0 + 0x7f + (0x7f << 7) + (0x7f << 14) + (0x7f << 21) + (0x7 << 28)
                // we should reset the readerIndex if we overflowed the int type.
                input.SetReaderIndex(readerIndex);
                ThrowHelper.ThrowHttp2Exception_DecodeULE128ToIntDecompression();
            }

            return (int)v;
        }

        /// <summary>
        /// Unsigned Little Endian Base 128 Variable-Length Integer Encoding
        /// <para>Visible for testing only!</para>
        /// </summary>
        /// <param name="input"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        internal static long DecodeULE128(IByteBuffer input, long result)
        {
            Debug.Assert(result <= 0x7f && result >= 0);
            bool resultStartedAtZero = result == 0;
            int writerIndex = input.WriterIndex;
            for (int readerIndex = input.ReaderIndex, shift = 0; readerIndex < writerIndex; ++readerIndex, shift += 7)
            {
                byte b = input.GetByte(readerIndex);
                if (shift == 56 && ((b & 0x80) != 0 || b == 0x7F && !resultStartedAtZero))
                {
                    // the maximum value that can be represented by a signed 64 bit number is:
                    // [0x01L, 0x7fL] + 0x7fL + (0x7fL << 7) + (0x7fL << 14) + (0x7fL << 21) + (0x7fL << 28) + (0x7fL << 35)
                    // + (0x7fL << 42) + (0x7fL << 49) + (0x7eL << 56)
                    // OR
                    // 0x0L + 0x7fL + (0x7fL << 7) + (0x7fL << 14) + (0x7fL << 21) + (0x7fL << 28) + (0x7fL << 35) +
                    // (0x7fL << 42) + (0x7fL << 49) + (0x7fL << 56)
                    // this means any more shifts will result in overflow so we should break out and throw an error.
                    ThrowHelper.ThrowHttp2Exception_DecodeULE128ToLongDecompression();
                }

                if ((b & 0x80) == 0)
                {
                    input.SetReaderIndex(readerIndex + 1);
                    return result + ((b & 0x7FL) << shift);
                }

                result += (b & 0x7FL) << shift;
            }

            return ThrowHelper.ThrowHttp2Exception_DecodeULE128Decompression();
        }
    }

    /// <summary>
    /// HTTP/2 header types.
    /// </summary>
    internal enum HeaderType
    {
        RegularHeader,
        RequestPseudoHeader,
        ResponsePseudoHeader
    }

    interface ISink
    {
        void AppendToHeaderList(ICharSequence name, ICharSequence value);
        void Finish();
    }

    sealed class Http2HeadersSink : ISink
    {
        private readonly IHttp2Headers headers;
        private readonly long maxHeaderListSize;
        private readonly int streamId;
        private readonly bool validate;
        private long headersLength;
        private bool exceededMaxLength;
        private HeaderType? previousType;
        private Http2Exception validationException;

        public Http2HeadersSink(int streamId, IHttp2Headers headers, long maxHeaderListSize, bool validate)
        {
            this.headers = headers;
            this.maxHeaderListSize = maxHeaderListSize;
            this.streamId = streamId;
            this.validate = validate;
        }
        public void Finish()
        {
            if (exceededMaxLength)
            {
                Http2CodecUtil.HeaderListSizeExceeded(this.streamId, this.maxHeaderListSize, true);
            }
            else if (validationException != null)
            {
                ThrowValidationException();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowValidationException()
        {
            throw validationException;
        }

        public void AppendToHeaderList(ICharSequence name, ICharSequence value)
        {
            this.headersLength += HpackHeaderField.SizeOf(name, value);
            this.exceededMaxLength |= this.headersLength > this.maxHeaderListSize;

            if (this.exceededMaxLength || this.validationException != null)
            {
                // We don't store the header since we've already failed validation requirements.
                return;
            }

            if (this.validate)
            {
                try
                {
                    this.previousType = HpackDecoder.Validate(this.streamId, name, this.previousType);
                }
                catch (Http2Exception ex)
                {
                    this.validationException = ex;
                    return;
                }
            }

            this.headers.Add(name, value);
        }
    }
}