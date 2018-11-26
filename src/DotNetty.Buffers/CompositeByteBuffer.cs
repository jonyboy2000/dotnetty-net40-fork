﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// ReSharper disable ConvertToAutoProperty
namespace DotNetty.Buffers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using DotNetty.Common;
    using DotNetty.Common.Internal;
    using DotNetty.Common.Utilities;

    public partial class CompositeByteBuffer : AbstractReferenceCountedByteBuffer, IEnumerable<IByteBuffer>
    {
        static readonly IList<IByteBuffer> EmptyList = new ReadOnlyCollection<IByteBuffer>(new IByteBuffer[0]);

        class ComponentEntry
        {
            public readonly IByteBuffer Buffer;
            public int Adjustment;
            public int Offset;
            public int EndOffset;
            internal IByteBuffer slice;

            public ComponentEntry(IByteBuffer buffer, int srcOffset, int offset, int len, IByteBuffer slice)
            {
                this.Buffer = buffer;
                this.Offset = offset;
                this.EndOffset = offset + len;
                this.Adjustment = srcOffset - offset;
                this.slice = slice;
            }

            public int Idx(int index)
            {
                return index + this.Adjustment;
            }

            public int Length()
            {
                return this.EndOffset - this.Offset;
            }

            public void Reposition(int newOffset)
            {
                int move = newOffset - this.Offset;
                this.EndOffset += move;
                this.Adjustment -= move;
                this.Offset = newOffset;
            }

            // copy then release
            public void TransferTo(IByteBuffer dst)
            {
                dst.WriteBytes(this.Buffer, this.Idx(this.Offset), this.Length());
                this.FreeIfNecessary();
            }

            public IByteBuffer Slice()
            {
                return this.slice != null ? this.slice : (this.slice = this.Buffer.Slice(this.Idx(this.Offset), this.Length()));
            }

            public IByteBuffer Duplicate()
            {
                return this.Buffer.Duplicate().SetIndex(this.Idx(this.Offset), this.Idx(this.EndOffset));
            }

            public void FreeIfNecessary()
            {
                // Release the slice if present since it may have a different
                // refcount to the unwrapped buf if it is a PooledSlicedByteBuf
                IByteBuffer buffer = this.slice;
                if (buffer != null)
                {
                    buffer.Release();
                }
                else
                {
                    this.Buffer.Release();
                }
                // null out in either case since it could be racy
                this.slice = null;
            }
        }

        static readonly ArraySegment<byte> EmptyNioBuffer = Unpooled.Empty.GetIoBuffer();

        readonly IByteBufferAllocator allocator;
        readonly bool direct;
        readonly int maxNumComponents;

        int componentCount;
        ComponentEntry[] components;

        bool freed;

        public CompositeByteBuffer(IByteBufferAllocator allocator, bool direct, int maxNumComponents)
            : base(AbstractByteBufferAllocator.DefaultMaxCapacity)
        {
            if (null == allocator) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.allocator); }
            if (maxNumComponents < 2) { ThrowHelper.ThrowArgumentException_CheckMaxNumComponents(maxNumComponents); }

            this.allocator = allocator;
            this.direct = direct;
            this.maxNumComponents = maxNumComponents;
            this.components = NewList(maxNumComponents);
        }

        public CompositeByteBuffer(IByteBufferAllocator allocator, bool direct, int maxNumComponents, params IByteBuffer[] buffers)
            : this(allocator, direct, maxNumComponents, buffers, 0, buffers.Length)
        {
        }

        internal CompositeByteBuffer(IByteBufferAllocator allocator, bool direct, int maxNumComponents, IByteBuffer[] buffers, int offset, int length)
            : base(AbstractByteBufferAllocator.DefaultMaxCapacity)
        {
            if (null == allocator) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.allocator); }
            if (maxNumComponents < 2) { ThrowHelper.ThrowArgumentException_CheckMaxNumComponents(maxNumComponents); }

            this.allocator = allocator;
            this.direct = direct;
            this.maxNumComponents = maxNumComponents;
            this.components = NewList(maxNumComponents);

            this.AddComponents0(false, 0, buffers, offset, length);
            this.ConsolidateIfNeeded();
            this.SetIndex0(0, GetCapacity(this.components));
        }

        public CompositeByteBuffer(
            IByteBufferAllocator allocator, bool direct, int maxNumComponents, IEnumerable<IByteBuffer> buffers)
            : base(AbstractByteBufferAllocator.DefaultMaxCapacity)
        {
            if (null == allocator) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.allocator); }
            if (maxNumComponents < 2) { ThrowHelper.ThrowArgumentException_CheckMaxNumComponents(maxNumComponents); }

            this.allocator = allocator;
            this.direct = direct;
            this.maxNumComponents = maxNumComponents;
            this.components = NewList(maxNumComponents);

            this.AddComponents0(false, 0, buffers);
            this.ConsolidateIfNeeded();
            this.SetIndex0(0, GetCapacity(this.components));
        }

        static List<ComponentEntry> NewList(int maxNumComponents) =>
            new List<ComponentEntry>(Math.Min(AbstractByteBufferAllocator.DefaultMaxComponents, maxNumComponents));

        // Special constructor used by WrappedCompositeByteBuf
        internal CompositeByteBuffer(IByteBufferAllocator allocator) : base(int.MaxValue)
        {
            this.allocator = allocator;
            this.direct = false;
            this.maxNumComponents = 0;
            this.components = new List<ComponentEntry>(0);
        }

        /// <summary>
        ///     Add the given {@link IByteBuffer}.
        ///     Be aware that this method does not increase the {@code writerIndex} of the {@link CompositeByteBuffer}.
        ///     If you need to have it increased you need to handle it by your own.
        ///     @param buffer the {@link IByteBuffer} to add
        /// </summary>
        public virtual CompositeByteBuffer AddComponent(IByteBuffer buffer) => this.AddComponent(false, buffer);

        /// <summary>
        ///     Add the given {@link IByteBuffer}s.
        ///     Be aware that this method does not increase the {@code writerIndex} of the {@link CompositeByteBuffer}.
        ///     If you need to have it increased you need to handle it by your own.
        ///     @param buffers the {@link IByteBuffer}s to add
        /// </summary>
        public virtual CompositeByteBuffer AddComponents(params IByteBuffer[] buffers) => this.AddComponents(false, buffers);

        /// <summary>
        ///     Add the given {@link IByteBuffer}s.
        ///     Be aware that this method does not increase the {@code writerIndex} of the {@link CompositeByteBuffer}.
        ///     If you need to have it increased you need to handle it by your own.
        ///     @param buffers the {@link IByteBuffer}s to add
        /// </summary>
        public virtual CompositeByteBuffer AddComponents(IEnumerable<IByteBuffer> buffers) => this.AddComponents(false, buffers);

        /// <summary>
        ///     Add the given {@link IByteBuffer} on the specific index.
        ///     Be aware that this method does not increase the {@code writerIndex} of the {@link CompositeByteBuffer}.
        ///     If you need to have it increased you need to handle it by your own.
        ///     @param cIndex the index on which the {@link IByteBuffer} will be added
        ///     @param buffer the {@link IByteBuffer} to add
        /// </summary>
        public virtual CompositeByteBuffer AddComponent(int cIndex, IByteBuffer buffer) => this.AddComponent(false, cIndex, buffer);

        public virtual CompositeByteBuffer AddComponent(bool increaseWriterIndex, IByteBuffer buffer)
        {
            if (null == buffer) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.buffer); }
            this.AddComponent0(increaseWriterIndex, this.components.Count, buffer);
            this.ConsolidateIfNeeded();
            return this;
        }

        public virtual CompositeByteBuffer AddComponents(bool increaseWriterIndex, params IByteBuffer[] buffers)
        {
            this.AddComponents0(increaseWriterIndex, this.components.Count, buffers, 0, buffers.Length);
            this.ConsolidateIfNeeded();
            return this;
        }

        public virtual CompositeByteBuffer AddComponents(bool increaseWriterIndex, IEnumerable<IByteBuffer> buffers)
        {
            this.AddComponents0(increaseWriterIndex, this.components.Count, buffers);
            this.ConsolidateIfNeeded();
            return this;
        }

        public virtual CompositeByteBuffer AddComponent(bool increaseWriterIndex, int cIndex, IByteBuffer buffer)
        {
            if (null == buffer) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.buffer); }
            this.AddComponent0(increaseWriterIndex, cIndex, buffer);
            this.ConsolidateIfNeeded();
            return this;
        }

        int AddComponent0(bool increaseWriterIndex, int cIndex, IByteBuffer buffer)
        {
            bool wasAdded = false;
            try
            {
                this.CheckComponentIndex(cIndex);

                int readableBytes = buffer.ReadableBytes;

                // No need to consolidate - just add a component to the list.
                var c = new ComponentEntry(buffer.Slice());
                if (cIndex == this.components.Count)
                {
                    this.components.Add(c);
                    wasAdded = true;
                    if (cIndex == 0)
                    {
                        c.EndOffset = readableBytes;
                    }
                    else
                    {
                        ComponentEntry prev = this.components[cIndex - 1];
                        c.Offset = prev.EndOffset;
                        c.EndOffset = c.Offset + readableBytes;
                    }
                }
                else
                {
                    this.components.Insert(cIndex, c);
                    wasAdded = true;
                    if (readableBytes != 0)
                    {
                        this.UpdateComponentOffsets(cIndex);
                    }
                }
                if (increaseWriterIndex)
                {
                    this.SetWriterIndex(this.WriterIndex + buffer.ReadableBytes);
                }
                return cIndex;
            }
            finally
            {
                if (!wasAdded)
                {
                    buffer.Release();
                }
            }
        }

        /// <summary>
        ///     Add the given {@link IByteBuffer}s on the specific index
        ///     Be aware that this method does not increase the {@code writerIndex} of the {@link CompositeByteBuffer}.
        ///     If you need to have it increased you need to handle it by your own.
        ///     @param cIndex the index on which the {@link IByteBuffer} will be added.
        ///     @param buffers the {@link IByteBuffer}s to add
        /// </summary>
        public virtual CompositeByteBuffer AddComponents(int cIndex, params IByteBuffer[] buffers)
        {
            this.AddComponents0(false, cIndex, buffers, 0, buffers.Length);
            this.ConsolidateIfNeeded();
            return this;
        }

        int AddComponents0(bool increaseWriterIndex, int cIndex, IByteBuffer[] buffers, int offset, int len)
        {
            if (null == buffers) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.buffers); }
            int i = offset;
            try
            {
                this.CheckComponentIndex(cIndex);

                // No need for consolidation
                while (i < len)
                {
                    // Increment i now to prepare for the next iteration and prevent a duplicate release (addComponent0
                    // will release if an exception occurs, and we also release in the finally block here).
                    IByteBuffer b = buffers[i++];
                    if (b == null)
                    {
                        break;
                    }
                    cIndex = this.AddComponent0(increaseWriterIndex, cIndex, b) + 1;
                    int size = this.components.Count;
                    if (cIndex > size)
                    {
                        cIndex = size;
                    }
                }

                return cIndex;
            }
            finally
            {
                for (; i < len; ++i)
                {
                    IByteBuffer b = buffers[i];
                    if (b != null)
                    {
                        try
                        {
                            b.Release();
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Add the given {@link ByteBuf}s on the specific index
        ///     Be aware that this method does not increase the {@code writerIndex} of the {@link CompositeByteBuffer}.
        ///     If you need to have it increased you need to handle it by your own.
        ///     @param cIndex the index on which the {@link IByteBuffer} will be added.
        ///     @param buffers the {@link IByteBuffer}s to add
        /// </summary>
        public virtual CompositeByteBuffer AddComponents(int cIndex, IEnumerable<IByteBuffer> buffers)
        {
            this.AddComponents0(false, cIndex, buffers);
            this.ConsolidateIfNeeded();
            return this;
        }

        int AddComponents0(bool increaseIndex, int cIndex, IEnumerable<IByteBuffer> buffers)
        {
            if (null == buffers) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.buffers); }

            if (buffers is IByteBuffer buffer)
            {
                // If buffers also implements ByteBuf (e.g. CompositeByteBuf), it has to go to addComponent(ByteBuf).
                return this.AddComponent0(increaseIndex, cIndex, buffer);
            }

            IByteBuffer[] array = buffers.ToArray();
            return this.AddComponents0(increaseIndex, cIndex, array, 0, array.Length);
        }

        /// <summary>
        ///     This should only be called as last operation from a method as this may adjust the underlying
        ///     array of components and so affect the index etc.
        /// </summary>
        void ConsolidateIfNeeded()
        {
            // Consolidate if the number of components will exceed the allowed maximum by the current
            // operation.
            int numComponents = this.components.Count;
            if (numComponents > this.MaxNumComponents)
            {
                int capacity = this.components[numComponents - 1].EndOffset;

                IByteBuffer consolidated = this.AllocateBuffer(capacity);

                // We're not using foreach to avoid creating an iterator.
                for (int i = 0; i < numComponents; i++)
                {
                    ComponentEntry c1 = this.components[i];
                    IByteBuffer b = c1.Buffer;
                    consolidated.WriteBytes(b);
                    c1.FreeIfNecessary();
                }
                var c = new ComponentEntry(consolidated);
                c.EndOffset = c.Length;
                this.components.Clear();
                this.components.Add(c);
            }
        }

        [MethodImpl(InlineMethod.Value)]
        void CheckComponentIndex(int cIndex)
        {
            this.EnsureAccessible();
            if (cIndex < 0 || cIndex > this.components.Count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException_Index(cIndex, this.components.Count);
            }
        }

        [MethodImpl(InlineMethod.Value)]
        void CheckComponentIndex(int cIndex, int numComponents)
        {
            this.EnsureAccessible();
            if (cIndex < 0 || cIndex + numComponents > this.components.Count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException_Index(cIndex, numComponents, this.components.Count);
            }
        }

        void UpdateComponentOffsets(int cIndex)
        {
            int size = this.components.Count;
            if (size <= cIndex)
            {
                return;
            }

            ComponentEntry c = this.components[cIndex];
            if (cIndex == 0)
            {
                c.Offset = 0;
                c.EndOffset = c.Length;
                cIndex++;
            }

            for (int i = cIndex; i < size; i++)
            {
                ComponentEntry prev = this.components[i - 1];
                ComponentEntry cur = this.components[i];
                cur.Offset = prev.EndOffset;
                cur.EndOffset = cur.Offset + cur.Length;
            }
        }

        /// <summary>
        ///     Remove the {@link IByteBuffer} from the given index.
        ///     @param cIndex the index on from which the {@link IByteBuffer} will be remove
        /// </summary>
        public virtual CompositeByteBuffer RemoveComponent(int cIndex)
        {
            this.CheckComponentIndex(cIndex);
            ComponentEntry comp = this.components[cIndex];
            this.components.RemoveAt(cIndex);
            comp.FreeIfNecessary();
            if (comp.Length > 0)
            {
                // Only need to call updateComponentOffsets if the length was > 0
                this.UpdateComponentOffsets(cIndex);
            }
            return this;
        }

        /// <summary>
        ///     Remove the number of {@link IByteBuffer}s starting from the given index.
        ///     @param cIndex the index on which the {@link IByteBuffer}s will be started to removed
        ///     @param numComponents the number of components to remove
        /// </summary>
        public virtual CompositeByteBuffer RemoveComponents(int cIndex, int numComponents)
        {
            this.CheckComponentIndex(cIndex, numComponents);

            if (numComponents == 0)
            {
                return this;
            }
            bool needsUpdate = false;
            for (int i = cIndex + numComponents; i >= cIndex; i--)
            {
                ComponentEntry c = this.components[i];
                needsUpdate |= c.Length > 0;
                c.FreeIfNecessary();
                this.components.RemoveAt(i);
            }

            if (needsUpdate)
            {
                // Only need to call updateComponentOffsets if the length was > 0
                this.UpdateComponentOffsets(cIndex);
            }
            return this;
        }

        public virtual IEnumerator<IByteBuffer> GetEnumerator()
        {
            this.EnsureAccessible();

            var size = this.NumComponents;
            for (var idx = 0; idx < size; idx++)
            {
                yield return this.components[idx].Slice();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        /// <summary>
        ///     Same with {@link #slice(int, int)} except that this method returns a list.
        /// </summary>
        public virtual IList<IByteBuffer> Decompose(int offset, int length)
        {
            this.CheckIndex(offset, length);
            if (length == 0)
            {
                return EmptyList;
            }

            int componentId = this.ToComponentIndex(offset);
            int bytesToSlice = length;
            // The first component
            var firstC = this.components[componentId];
            int firstBufOffset = offset - firstC.Offset;

            var slice = firstC.Buffer.Slice(firstBufOffset + firstC.Buffer.ReaderIndex,
                                            Math.Min(firstC.Length - firstBufOffset, bytesToSlice));
            bytesToSlice -= slice.ReadableBytes;

            if (bytesToSlice == 0)
            {
                return new List<IByteBuffer> { slice };
            }

            var sliceList = new List<IByteBuffer>(this.components.Count - componentId);
            sliceList.Add(slice);

            // Add all the slices until there is nothing more left and then return the List.
            do
            {
                var component = this.components[++componentId];
                slice = component.Buffer.Slice(component.Buffer.ReaderIndex, Math.Min(component.Length, bytesToSlice));
                bytesToSlice -= slice.ReadableBytes;
                sliceList.Add(slice);
            } while (bytesToSlice > 0);

            return sliceList;
        }

        public override int IoBufferCount
        {
            get
            {
                switch (this.components.Count)
                {
                    case 0:
                        return 1;
                    case 1:
                        return this.components[0].Buffer.IoBufferCount;
                    default:
                        int count = 0;
                        int componentsCount = this.components.Count;
                        for (int i = 0; i < componentsCount; i++)
                        {
                            ComponentEntry c = this.components[i];
                            count += c.Buffer.IoBufferCount;
                        }
                        return count;
                }
            }
        }

        public override ArraySegment<byte> GetIoBuffer(int index, int length)
        {
            this.CheckIndex(index, length);

            switch (this.components.Count)
            {
                case 0:
                    return EmptyNioBuffer;
                case 1:
                    IByteBuffer buf = this.components[0].Buffer;
                    if (buf.IoBufferCount == 1)
                    {
                        return this.components[0].Buffer.GetIoBuffer(index, length);
                    }
                    break;
            }

            var merged = new byte[length];
            ArraySegment<byte>[] buffers = this.GetIoBuffers(index, length);

            int offset = 0;
            foreach (ArraySegment<byte> buf in buffers)
            {
                Debug.Assert(merged.Length - offset >= buf.Count);

                PlatformDependent.CopyMemory(buf.Array, buf.Offset, merged, offset, buf.Count);
                offset += buf.Count;
            }

            return new ArraySegment<byte>(merged);
        }

        public override ArraySegment<byte>[] GetIoBuffers(int index, int length)
        {
            this.CheckIndex(index, length);
            if (length == 0)
            {
                return new[] { EmptyNioBuffer };
            }

            var buffers = new List<ArraySegment<byte>>(this.components.Count);
            int i = this.ToComponentIndex(index);
            while (length > 0)
            {
                ComponentEntry c = this.components[i];
                IByteBuffer s = c.Buffer;
                int adjustment = c.Offset;
                int localLength = Math.Min(length, s.Capacity - (index - adjustment));
                switch (s.IoBufferCount)
                {
                    case 0:
                        throw new NotSupportedException();
                    case 1:
                        buffers.Add(s.GetIoBuffer(index - adjustment, localLength));
                        break;
                    default:
                        buffers.AddRange(s.GetIoBuffers(index - adjustment, localLength));
                        break;
                }

                index += localLength;
                length -= localLength;
                i++;
            }

            return buffers.ToArray();
        }


        public override bool IsDirect
        {
            get
            {
                int size = this.components.Count;
                if (size == 0)
                {
                    return false;
                }
                for (int i = 0; i < size; i++)
                {
                    if (!this.components[i].Buffer.IsDirect)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public override bool HasArray
        {
            get
            {
                switch (this.components.Count)
                {
                    case 0:
                        return true;
                    case 1:
                        return this.components[0].Buffer.HasArray;
                    default:
                        return false;
                }
            }
        }

        public override byte[] Array
        {
            get
            {
                switch (this.components.Count)
                {
                    case 0:
                        return ArrayExtensions.ZeroBytes;
                    case 1:
                        return this.components[0].Buffer.Array;
                    default:
                        throw new NotSupportedException();
                }
            }
        }

        public override int ArrayOffset
        {
            get
            {
                switch (this.components.Count)
                {
                    case 0:
                        return 0;
                    case 1:
                        return this.components[0].Buffer.ArrayOffset;
                    default:
                        throw new NotSupportedException();
                }
            }
        }

        public override bool HasMemoryAddress
        {
            get
            {
                switch (this.components.Count)
                {
                    case 1:
                        return this.components[0].Buffer.HasMemoryAddress;
                    default:
                        return false;
                }
            }
        }

        public override ref byte GetPinnableMemoryAddress()
        {
            switch (this.components.Count)
            {
                case 1:
                    return ref this.components[0].Buffer.GetPinnableMemoryAddress();
                default:
                    throw new NotSupportedException();
            }
        }

        public override IntPtr AddressOfPinnedMemory()
        {
            switch (this.components.Count)
            {
                case 1:
                    return this.components[0].Buffer.AddressOfPinnedMemory();
                default:
                    throw new NotSupportedException();
            }
        }

        public override int Capacity => GetCapacity(this.components);

        static int GetCapacity(List<ComponentEntry> components)
        {
            int numComponents = components.Count;
            if (numComponents == 0)
            {
                return 0;
            }

            return components[numComponents - 1].EndOffset;
        }

        public override IByteBuffer AdjustCapacity(int newCapacity)
        {
            this.CheckNewCapacity(newCapacity);

            int oldCapacity = this.Capacity;
            if (newCapacity > oldCapacity)
            {
                int paddingLength = newCapacity - oldCapacity;
                IByteBuffer padding;
                int nComponents = this.components.Count;
                if (nComponents < this.MaxNumComponents)
                {
                    padding = this.AllocateBuffer(paddingLength);
                    padding.SetIndex(0, paddingLength);
                    this.AddComponent0(false, this.components.Count, padding);
                }
                else
                {
                    padding = this.AllocateBuffer(paddingLength);
                    padding.SetIndex(0, paddingLength);
                    // FIXME: No need to create a padding buffer and consolidate.
                    // Just create a big single buffer and put the current content there.
                    this.AddComponent0(false, this.components.Count, padding);
                    this.ConsolidateIfNeeded();
                }
            }
            else if (newCapacity < oldCapacity)
            {
                int bytesToTrim = oldCapacity - newCapacity;
                for (int i = this.components.Count - 1; i >= 0; i--)
                {
                    ComponentEntry c = this.components[i];
                    if (bytesToTrim >= c.Length)
                    {
                        bytesToTrim -= c.Length;
                        this.components.RemoveAt(i);
                        continue;
                    }

                    // Replace the last component with the trimmed slice.
                    var newC = new ComponentEntry(c.Buffer.Slice(0, c.Length - bytesToTrim));
                    newC.Offset = c.Offset;
                    newC.EndOffset = newC.Offset + newC.Length;
                    this.components[i] = newC;
                    break;
                }

                if (this.ReaderIndex > newCapacity)
                {
                    this.SetIndex(newCapacity, newCapacity);
                }
                else if (this.WriterIndex > newCapacity)
                {
                    this.SetWriterIndex(newCapacity);
                }
            }
            return this;
        }

        public override IByteBufferAllocator Allocator => this.allocator;

        /// <summary>
        ///     Return the current number of {@link IByteBuffer}'s that are composed in this instance
        /// </summary>
        public virtual int NumComponents => this.components.Count;

        /// <summary>
        ///     Return the max number of {@link IByteBuffer}'s that are composed in this instance
        /// </summary>
        public virtual int MaxNumComponents => this.maxNumComponents;

        /// <summary>
        ///     Return the index for the given offset
        /// </summary>
        public virtual int ToComponentIndex(int offset)
        {
            this.CheckIndex(offset);

            for (int low = 0, high = this.components.Count; low <= high;)
            {
                int mid = (low + high).RightUShift(1);
                ComponentEntry c = this.components[mid];
                if (offset >= c.EndOffset)
                {
                    low = mid + 1;
                }
                else if (offset < c.Offset)
                {
                    high = mid - 1;
                }
                else
                {
                    return mid;
                }
            }

            return ThrowHelper.ThrowGetException_ShouldNotReachHere<int>();
        }

        public virtual int ToByteIndex(int cIndex)
        {
            this.CheckComponentIndex(cIndex);
            return this.components[cIndex].Offset;
        }

        protected internal override byte _GetByte(int index)
        {
            ComponentEntry c = this.FindComponent(index);
            return c.Buffer.GetByte(index - c.Offset);
        }

        protected internal override short _GetShort(int index)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 2 <= c.EndOffset)
            {
                return c.Buffer.GetShort(index - c.Offset);
            }

            return (short)(this._GetByte(index) << 8 | this._GetByte(index + 1));
        }

        protected internal override short _GetShortLE(int index)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 2 <= c.EndOffset)
            {
                return c.Buffer.GetShortLE(index - c.Offset);
            }

            return (short)(this._GetByte(index) << 8 | this._GetByte(index + 1));
        }

        protected internal override int _GetUnsignedMedium(int index)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 3 <= c.EndOffset)
            {
                return c.Buffer.GetUnsignedMedium(index - c.Offset);
            }

            return (this._GetShort(index) & 0xffff) << 8 | this._GetByte(index + 2);
        }

        protected internal override int _GetUnsignedMediumLE(int index)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 3 <= c.EndOffset)
            {
                return c.Buffer.GetUnsignedMediumLE(index - c.Offset);
            }

            return (this._GetShortLE(index) & 0xffff) << 8 | this._GetByte(index + 2);
        }

        protected internal override int _GetInt(int index)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 4 <= c.EndOffset)
            {
                return c.Buffer.GetInt(index - c.Offset);
            }

            return this._GetShort(index) << 16 | (ushort)this._GetShort(index + 2);
        }

        protected internal override int _GetIntLE(int index)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 4 <= c.EndOffset)
            {
                return c.Buffer.GetIntLE(index - c.Offset);
            }

            return (this._GetShortLE(index) << 16 | (ushort)this._GetShortLE(index + 2));
        }

        protected internal override long _GetLong(int index)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 8 <= c.EndOffset)
            {
                return c.Buffer.GetLong(index - c.Offset);
            }

            return (long)this._GetInt(index) << 32 | (uint)this._GetInt(index + 4);
        }

        protected internal override long _GetLongLE(int index)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 8 <= c.EndOffset)
            {
                return c.Buffer.GetLongLE(index - c.Offset);
            }

            return (this._GetIntLE(index) << 32 | this._GetIntLE(index + 4));
        }

        public override IByteBuffer GetBytes(int index, byte[] dst, int dstIndex, int length)
        {
            this.CheckDstIndex(index, length, dstIndex, dst.Length);
            if (length == 0)
            {
                return this;
            }

            int i = this.ToComponentIndex(index);
            while (length > 0)
            {
                ComponentEntry c = this.components[i];
                IByteBuffer s = c.Buffer;
                int adjustment = c.Offset;
                int localLength = Math.Min(length, s.Capacity - (index - adjustment));
                s.GetBytes(index - adjustment, dst, dstIndex, localLength);
                index += localLength;
                dstIndex += localLength;
                length -= localLength;
                i++;
            }
            return this;
        }

        public override IByteBuffer GetBytes(int index, Stream destination, int length)
        {
            this.CheckIndex(index, length);
            if (length == 0)
            {
                return this;
            }

            int i = this.ToComponentIndex(index);
            while (length > 0)
            {
                ComponentEntry c = this.components[i];
                IByteBuffer s = c.Buffer;
                int adjustment = c.Offset;
                int localLength = Math.Min(length, s.Capacity - (index - adjustment));
                s.GetBytes(index - adjustment, destination, localLength);
                index += localLength;
                length -= localLength;
                i++;
            }
            return this;
        }

        public override IByteBuffer GetBytes(int index, IByteBuffer dst, int dstIndex, int length)
        {
            this.CheckDstIndex(index, length, dstIndex, dst.Capacity);
            if (length == 0)
            {
                return this;
            }

            int i = this.ToComponentIndex(index);
            while (length > 0)
            {
                ComponentEntry c = this.components[i];
                IByteBuffer s = c.Buffer;
                int adjustment = c.Offset;
                int localLength = Math.Min(length, s.Capacity - (index - adjustment));
                s.GetBytes(index - adjustment, dst, dstIndex, localLength);
                index += localLength;
                dstIndex += localLength;
                length -= localLength;
                i++;
            }
            return this;
        }

        protected internal override void _SetByte(int index, int value)
        {
            ComponentEntry c = this.FindComponent(index);
            c.Buffer.SetByte(index - c.Offset, value);
        }

        protected internal override void _SetShort(int index, int value)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 2 <= c.EndOffset)
            {
                c.Buffer.SetShort(index - c.Offset, value);
            }
            else
            {
                this._SetByte(index, (byte)((uint)value >> 8));
                this._SetByte(index + 1, (byte)value);
            }
        }

        protected internal override void _SetShortLE(int index, int value)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 2 <= c.EndOffset)
            {
                c.Buffer.SetShortLE(index - c.Offset, value);
            }
            else
            {
                this._SetByte(index, (byte)(value.RightUShift(8)));
                this._SetByte(index + 1, (byte)value);
            }
        }

        protected internal override void _SetMedium(int index, int value)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 3 <= c.EndOffset)
            {
                c.Buffer.SetMedium(index - c.Offset, value);
            }
            else
            {
                this._SetShort(index, (short)(value >> 8));
                this._SetByte(index + 2, (byte)value);
            }
        }

        protected internal override void _SetMediumLE(int index, int value)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 3 <= c.EndOffset)
            {
                c.Buffer.SetMediumLE(index - c.Offset, value);
            }
            else
            {
                this._SetShortLE(index, (short)(value >> 8));
                this._SetByte(index + 2, (byte)value);
            }
        }

        protected internal override void _SetInt(int index, int value)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 4 <= c.EndOffset)
            {
                c.Buffer.SetInt(index - c.Offset, value);
            }
            else
            {
                this._SetShort(index, (short)((uint)value >> 16));
                this._SetShort(index + 2, (short)value);
            }
        }

        protected internal override void _SetIntLE(int index, int value)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 4 <= c.EndOffset)
            {
                c.Buffer.SetIntLE(index - c.Offset, value);
            }
            else
            {
                this._SetShortLE(index, (short)value.RightUShift(16));
                this._SetShortLE(index + 2, (short)value);
            }
        }

        protected internal override void _SetLong(int index, long value)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 8 <= c.EndOffset)
            {
                c.Buffer.SetLong(index - c.Offset, value);
            }
            else
            {
                this._SetInt(index, (int)((ulong)value >> 32));
                this._SetInt(index + 4, (int)value);
            }
        }

        protected internal override void _SetLongLE(int index, long value)
        {
            ComponentEntry c = this.FindComponent(index);
            if (index + 8 <= c.EndOffset)
            {
                c.Buffer.SetLongLE(index - c.Offset, value);
            }
            else
            {
                this._SetIntLE(index, (int)value.RightUShift(32));
                this._SetIntLE(index + 4, (int)value);
            }
        }

        public override IByteBuffer SetBytes(int index, byte[] src, int srcIndex, int length)
        {
            this.CheckSrcIndex(index, length, srcIndex, src.Length);
            if (length == 0)
            {
                return this;
            }

            int i = this.ToComponentIndex(index);
            while (length > 0)
            {
                ComponentEntry c = this.components[i];
                IByteBuffer s = c.Buffer;
                int adjustment = c.Offset;
                int localLength = Math.Min(length, s.Capacity - (index - adjustment));
                s.SetBytes(index - adjustment, src, srcIndex, localLength);
                index += localLength;
                srcIndex += localLength;
                length -= localLength;
                i++;
            }
            return this;
        }

        public override async Task<int> SetBytesAsync(int index, Stream src, int length, CancellationToken cancellationToken)
        {
            this.CheckIndex(index, length);
            if (length == 0)
            {
                return 0;
                //return src.Read(EmptyArrays.EMPTY_BYTES);
            }

            int i = this.ToComponentIndex(index);
            int readBytes = 0;

            do
            {
                ComponentEntry c = this.components[i];
                IByteBuffer s = c.Buffer;
                int adjustment = c.Offset;
                int localLength = Math.Min(length, s.Capacity - (index - adjustment));
                if (localLength == 0)
                {
                    // Skip empty buffer
                    i++;
                    continue;
                }
                int localReadBytes = await s.SetBytesAsync(index - adjustment, src, localLength, cancellationToken);
                if (localReadBytes < 0)
                {
                    if (readBytes == 0)
                    {
                        return -1;
                    }
                    else
                    {
                        break;
                    }
                }

                if (localReadBytes == localLength)
                {
                    index += localLength;
                    length -= localLength;
                    readBytes += localLength;
                    i++;
                }
                else
                {
                    index += localReadBytes;
                    length -= localReadBytes;
                    readBytes += localReadBytes;
                }
            }
            while (length > 0);

            return readBytes;
        }

        public override IByteBuffer SetBytes(int index, IByteBuffer src, int srcIndex, int length)
        {
            this.CheckSrcIndex(index, length, srcIndex, src.Capacity);
            if (length == 0)
            {
                return this;
            }

            int i = this.ToComponentIndex(index);
            while (length > 0)
            {
                ComponentEntry c = this.components[i];
                IByteBuffer s = c.Buffer;
                int adjustment = c.Offset;
                int localLength = Math.Min(length, s.Capacity - (index - adjustment));
                s.SetBytes(index - adjustment, src, srcIndex, localLength);
                index += localLength;
                srcIndex += localLength;
                length -= localLength;
                i++;
            }
            return this;
        }

        public override IByteBuffer SetZero(int index, int length)
        {
            this.CheckIndex(index, length);
            if (length == 0)
            {
                return this;
            }

            int i = this.ToComponentIndex(index);
            while (length > 0)
            {
                ComponentEntry c = this.components[i];
                IByteBuffer s = c.Buffer;
                int adjustment = c.Offset;
                int localLength = Math.Min(length, s.Capacity - (index - adjustment));
                s.SetZero(index - adjustment, localLength);
                index += localLength;
                length -= localLength;
                i++;
            }
            return this;
        }

        public override IByteBuffer Copy(int index, int length)
        {
            this.CheckIndex(index, length);
            IByteBuffer dst = this.AllocateBuffer(length);
            if (length != 0)
            {
                this.CopyTo(index, length, this.ToComponentIndex(index), dst);
            }
            return dst;
        }

        void CopyTo(int index, int length, int componentId, IByteBuffer dst)
        {
            int dstIndex = 0;
            int i = componentId;

            while (length > 0)
            {
                ComponentEntry c = this.components[i];
                IByteBuffer s = c.Buffer;
                int adjustment = c.Offset;
                int localLength = Math.Min(length, s.Capacity - (index - adjustment));
                s.GetBytes(index - adjustment, dst, dstIndex, localLength);
                index += localLength;
                dstIndex += localLength;
                length -= localLength;
                i++;
            }

            dst.SetWriterIndex(dst.Capacity);
        }

        /// <summary>
        ///     Return the {@link IByteBuffer} on the specified index
        ///     @param cIndex the index for which the {@link IByteBuffer} should be returned
        ///     @return buffer the {@link IByteBuffer} on the specified index
        /// </summary>
        public virtual IByteBuffer this[int cIndex] => this.InternalComponent(cIndex).Duplicate();

        /// <summary>
        ///     Return the {@link IByteBuffer} on the specified index
        ///     @param offset the offset for which the {@link IByteBuffer} should be returned
        ///     @return the {@link IByteBuffer} on the specified index
        /// </summary>
        public virtual IByteBuffer ComponentAtOffset(int offset) => this.InternalComponentAtOffset(offset).Duplicate();

        /// <summary>
        ///     Return the internal {@link IByteBuffer} on the specified index. Note that updating the indexes of the returned
        ///     buffer will lead to an undefined behavior of this buffer.
        ///     @param cIndex the index for which the {@link IByteBuffer} should be returned
        /// </summary>
        public virtual IByteBuffer InternalComponent(int cIndex)
        {
            this.CheckComponentIndex(cIndex);
            return this.components[cIndex].Buffer;
        }

        /// <summary>
        ///     Return the internal {@link IByteBuffer} on the specified offset. Note that updating the indexes of the returned
        ///     buffer will lead to an undefined behavior of this buffer.
        ///     @param offset the offset for which the {@link IByteBuffer} should be returned
        /// </summary>
        public virtual IByteBuffer InternalComponentAtOffset(int offset) => this.FindComponent(offset).Buffer;

        ComponentEntry FindComponent(int offset)
        {
            this.CheckIndex(offset);

            for (int low = 0, high = this.components.Count; low <= high;)
            {
                int mid = (low + high).RightUShift(1);
                ComponentEntry c = this.components[mid];
                if (offset >= c.EndOffset)
                {
                    low = mid + 1;
                }
                else if (offset < c.Offset)
                {
                    high = mid - 1;
                }
                else
                {
                    Debug.Assert(c.Length != 0);
                    return c;
                }
            }

            return ThrowHelper.ThrowGetException_ShouldNotReachHere<ComponentEntry>();
        }

        /// <summary>
        ///     Consolidate the composed {@link IByteBuffer}s
        /// </summary>
        public virtual CompositeByteBuffer Consolidate()
        {
            this.EnsureAccessible();
            int numComponents = this.NumComponents;
            if (numComponents <= 1)
            {
                return this;
            }

            ComponentEntry last = this.components[numComponents - 1];
            int capacity = last.EndOffset;
            IByteBuffer consolidated = this.AllocateBuffer(capacity);

            for (int i = 0; i < numComponents; i++)
            {
                ComponentEntry c = this.components[i];
                IByteBuffer b = c.Buffer;
                consolidated.WriteBytes(b);
                c.FreeIfNecessary();
            }

            this.components.Clear();
            this.components.Add(new ComponentEntry(consolidated));
            this.UpdateComponentOffsets(0);
            return this;
        }

        /// <summary>
        ///     Consolidate the composed {@link IByteBuffer}s
        ///     @param cIndex the index on which to start to compose
        ///     @param numComponents the number of components to compose
        /// </summary>
        public virtual CompositeByteBuffer Consolidate(int cIndex, int numComponents)
        {
            this.CheckComponentIndex(cIndex, numComponents);
            if (numComponents <= 1)
            {
                return this;
            }

            int endCIndex = cIndex + numComponents;
            ComponentEntry last = this.components[endCIndex - 1];
            int capacity = last.EndOffset - this.components[cIndex].Offset;
            IByteBuffer consolidated = this.AllocateBuffer(capacity);

            for (int i = cIndex; i < endCIndex; i++)
            {
                ComponentEntry c = this.components[i];
                IByteBuffer b = c.Buffer;
                consolidated.WriteBytes(b);
                c.FreeIfNecessary();
            }

            this.components.RemoveRange(cIndex, numComponents);
            this.components.Insert(cIndex, new ComponentEntry(consolidated));
            this.UpdateComponentOffsets(cIndex);
            return this;
        }

        /// <summary>
        ///     Discard all {@link IByteBuffer}s which are read.
        /// </summary>
        public virtual CompositeByteBuffer DiscardReadComponents()
        {
            this.EnsureAccessible();
            int readerIndex = this.ReaderIndex;
            if (readerIndex == 0)
            {
                return this;
            }

            // Discard everything if (readerIndex = writerIndex = capacity).
            int writerIndex = this.WriterIndex;
            if (readerIndex == writerIndex && writerIndex == this.Capacity)
            {
                foreach (ComponentEntry c in this.components)
                {
                    c.FreeIfNecessary();
                }
                this.components.Clear();
                this.SetIndex(0, 0);
                this.AdjustMarkers(readerIndex);
                return this;
            }

            // Remove read components.
            int firstComponentId = this.ToComponentIndex(readerIndex);
            for (int i = 0; i < firstComponentId; i++)
            {
                this.components[i].FreeIfNecessary();
            }
            this.components.RemoveRange(0, firstComponentId);

            // Update indexes and markers.
            ComponentEntry first = this.components[0];
            int offset = first.Offset;
            this.UpdateComponentOffsets(0);
            this.SetIndex(readerIndex - offset, writerIndex - offset);
            this.AdjustMarkers(offset);
            return this;
        }

        public override IByteBuffer DiscardReadBytes()
        {
            this.EnsureAccessible();
            int readerIndex = this.ReaderIndex;
            if (readerIndex == 0)
            {
                return this;
            }

            // Discard everything if (readerIndex = writerIndex = capacity).
            int writerIndex = this.WriterIndex;
            if (readerIndex == writerIndex && writerIndex == this.Capacity)
            {
                foreach (ComponentEntry c1 in this.components)
                {
                    c1.FreeIfNecessary();
                }
                this.components.Clear();
                this.SetIndex(0, 0);
                this.AdjustMarkers(readerIndex);
                return this;
            }

            // Remove read components.
            int firstComponentId = this.ToComponentIndex(readerIndex);
            for (int i = 0; i < firstComponentId; i++)
            {
                this.components[i].FreeIfNecessary();
            }
            this.components.RemoveRange(0, firstComponentId);

            // Remove or replace the first readable component with a new slice.
            ComponentEntry c = this.components[0];
            int adjustment = readerIndex - c.Offset;
            if (adjustment == c.Length)
            {
                // new slice would be empty, so remove instead
                this.components.RemoveAt(0);
            }
            else
            {
                var newC = new ComponentEntry(c.Buffer.Slice(adjustment, c.Length - adjustment));
                this.components[0] = newC;
            }

            // Update indexes and markers.
            this.UpdateComponentOffsets(0);
            this.SetIndex(0, writerIndex - readerIndex);
            this.AdjustMarkers(readerIndex);
            return this;
        }

        IByteBuffer AllocateBuffer(int capacity) =>
            this.direct ? this.Allocator.DirectBuffer(capacity) : this.Allocator.HeapBuffer(capacity);

        public override string ToString()
        {
            string result = base.ToString();
            result = result.Substring(0, result.Length - 1);
            return $"{result}, components={this.componentCount})";
        }

        public override IReferenceCounted Touch() => this;

        public override IReferenceCounted Touch(object hint) => this;

        public override IByteBuffer DiscardSomeReadBytes() => this.DiscardReadComponents();

        protected internal override void Deallocate()
        {
            if (this.freed)
            {
                return;
            }

            this.freed = true;
            int size = this.componentCount;
            // We're not using foreach to avoid creating an iterator.
            // see https://github.com/netty/netty/issues/2642
            for (int i = 0; i < size; i++)
            {
                this.components[i].FreeIfNecessary();
            }
        }

        public override IByteBuffer Unwrap() => null;

        // Component array manipulation - range checking omitted

        void ClearComps()
        {
            this.RemoveCompRange(0, this.componentCount);
        }

        void RemoveComp(int i)
        {
            this.RemoveCompRange(i, i + 1);
        }

        void RemoveCompRange(int from, int to)
        {
            if (from >= to) { return; }

            int size = this.componentCount;
            Debug.Assert(from >= 0 && to <= size);
            if (to < size)
            {
                System.Array.Copy(this.components, to, this.components, from, size - to);
            }
            int newSize = size - to + from;
            for (int i = newSize; i < size; i++)
            {
                components[i] = null;
            }
            this.componentCount = newSize;
        }

        void AddComp(int i, ComponentEntry c)
        {
            this.ShiftComps(i, 1);
            this.components[i] = c;
        }

        void ShiftComps(int i, int count)
        {
            int size = this.componentCount, newSize = size + count;
            Debug.Assert(i >= 0 && i <= size && count > 0);
            if (newSize > this.components.Length)
            {
                // grow the array
                int newArrSize = Math.Max(size + (size >> 1), newSize);
                ComponentEntry[] newArr;
                newArr = new ComponentEntry[newArrSize];
                if (i == size)
                {
                    System.Array.Copy(this.components, 0, newArr, 0, Math.Min(this.components.Length, newArrSize));
                }
                else
                {
                    if (i > 0)
                    {
                        System.Array.Copy(this.components, 0, newArr, 0, i);
                    }
                    if (i < size)
                    {
                        System.Array.Copy(this.components, i, newArr, i + count, size - i);
                    }
                }
                this.components = newArr;
            }
            else if (i < size)
            {
                System.Array.Copy(this.components, i, this.components, i + count, size - i);
            }
            this.componentCount = newSize;
        }
    }
}