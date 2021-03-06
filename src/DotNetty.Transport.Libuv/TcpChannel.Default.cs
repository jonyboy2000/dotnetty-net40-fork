﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Transport.Libuv
{
    using DotNetty.Transport.Channels;
    using DotNetty.Transport.Channels.Sockets;
    using DotNetty.Transport.Libuv.Native;

    public sealed class TcpChannel : TcpChannel<TcpChannel>
    {
        public TcpChannel() : base() { }

        internal TcpChannel(IChannel parent, Tcp tcp) : base(parent, tcp) { }
    }

    partial class TcpChannel<TChannel> : NativeChannel<TChannel, TcpChannel<TChannel>.TcpChannelUnsafe>, ISocketChannel
        where TChannel : TcpChannel<TChannel>
    {
    }
}
