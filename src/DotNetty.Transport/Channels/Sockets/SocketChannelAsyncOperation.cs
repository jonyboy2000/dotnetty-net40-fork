﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Transport.Channels.Sockets
{
    using System.Net.Sockets;
    using DotNetty.Common.Utilities;

    public partial class SocketChannelAsyncOperation<TChannel, TUnsafe> : SocketAsyncEventArgs
    {
        public SocketChannelAsyncOperation(TChannel channel)
            : this(channel, true)
        {
        }

        public SocketChannelAsyncOperation(TChannel channel, bool setEmptyBuffer)
        {
            if (null == channel) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.channel); }

            this.Channel = channel;
            this.Completed += AbstractSocketChannel<TChannel, TUnsafe>.IoCompletedCallback;
            if (setEmptyBuffer)
            {
                this.SetBuffer(ArrayExtensions.ZeroBytes, 0, 0);
            }
        }

        public void Validate()
        {
            SocketError socketError = this.SocketError;
            if (socketError != SocketError.Success)
            {
                ThrowHelper.ThrowSocketException(socketError);
            }
        }

        public TChannel Channel { get; private set; }
    }
}