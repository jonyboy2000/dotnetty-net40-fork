﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Codecs.Http.WebSockets
{
    using DotNetty.Buffers;

    public class BinaryWebSocketFrame : WebSocketFrame
    {
        public BinaryWebSocketFrame() 
            : base(true, 0, Opcode.Binary, ArrayPooled.Buffer(0))
        {
        }

        public BinaryWebSocketFrame(IByteBuffer binaryData)
            : base(true, 0, Opcode.Binary, binaryData)
        {
        }

        public BinaryWebSocketFrame(bool finalFragment, IByteBuffer binaryData)
            : base(finalFragment, 0, Opcode.Binary, binaryData)
        {
        }

        public BinaryWebSocketFrame(bool finalFragment, int rsv, IByteBuffer binaryData)
            : base(finalFragment, rsv, Opcode.Binary, binaryData)
        {
        }

        public override IByteBufferHolder Replace(IByteBuffer content) => new BinaryWebSocketFrame(this.IsFinalFragment, this.Rsv, content);
    }
}
