﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Codecs.Compression
{
    using System.Threading.Tasks;
    using DotNetty.Buffers;
    using DotNetty.Common.Concurrency;

    public abstract class ZlibEncoder : MessageToByteEncoder2<IByteBuffer>
    {
        public abstract bool IsClosed { get; }

        /**
         * Close this {@link ZlibEncoder} and so finish the encoding.
         *
         * The returned {@link ChannelFuture} will be notified once the
         * operation completes.
         */
        public abstract Task CloseAsync();

        public abstract Task CloseAsync(IPromise promise);
    }
}
