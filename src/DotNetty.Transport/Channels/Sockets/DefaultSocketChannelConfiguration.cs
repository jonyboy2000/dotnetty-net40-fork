﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Transport.Channels.Sockets
{
    using System;
    using System.Collections.Generic;
    using System.Net.Sockets;
    using System.Threading;

    /// <summary>
    /// The default <see cref="ISocketChannelConfiguration"/> implementation.
    /// </summary>
    public class DefaultSocketChannelConfiguration : DefaultChannelConfiguration, ISocketChannelConfiguration
    {
        protected readonly Socket Socket;
        int allowHalfClosure;

        public DefaultSocketChannelConfiguration(ISocketChannel channel, Socket socket)
            : base(channel)
        {
            if (null == socket) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.socket); }
            this.Socket = socket;

            // Enable TCP_NODELAY by default if possible.
            try
            {
                this.TcpNoDelay = true;
            }
            catch
            {
            }
        }

        public override T GetOption<T>(ChannelOption<T> option)
        {
            if (ChannelOption.SoRcvbuf.Equals(option))
            {
                return (T)(object)this.ReceiveBufferSize;
            }
            if (ChannelOption.SoSndbuf.Equals(option))
            {
                return (T)(object)this.SendBufferSize;
            }
            if (ChannelOption.TcpNodelay.Equals(option))
            {
                return (T)(object)this.TcpNoDelay;
            }
            if (ChannelOption.SoKeepalive.Equals(option))
            {
                return (T)(object)this.KeepAlive;
            }
            if (ChannelOption.SoReuseaddr.Equals(option))
            {
                return (T)(object)this.ReuseAddress;
            }
            if (ChannelOption.SoLinger.Equals(option))
            {
                return (T)(object)this.Linger;
            }
            //if (ChannelOption.IP_TOS.Equals(option))
            //{
            //    return (T)(object)this.TrafficClass;
            //}
            if (ChannelOption.AllowHalfClosure.Equals(option))
            {
                return (T)(object)this.AllowHalfClosure;
            }

            return base.GetOption(option);
        }

        public override bool SetOption<T>(ChannelOption<T> option, T value)
        {
            if (base.SetOption(option, value))
            {
                return true;
            }

            if (ChannelOption.SoRcvbuf.Equals(option))
            {
                this.ReceiveBufferSize = (int)(object)value;
            }
            else if (ChannelOption.SoSndbuf.Equals(option))
            {
                this.SendBufferSize = (int)(object)value;
            }
            else if (ChannelOption.TcpNodelay.Equals(option))
            {
                this.TcpNoDelay = (bool)(object)value;
            }
            else if (ChannelOption.SoKeepalive.Equals(option))
            {
                this.KeepAlive = (bool)(object)value;
            }
            else if (ChannelOption.SoReuseaddr.Equals(option))
            {
                this.ReuseAddress = (bool)(object)value;
            }
            else if (ChannelOption.SoLinger.Equals(option))
            {
                this.Linger = (int)(object)value;
            }
            //else if (option == IP_TOS)
            //{
            //    setTrafficClass((Integer)value);
            //}
            else if (ChannelOption.AllowHalfClosure.Equals(option))
            {
                this.AllowHalfClosure = (bool)(object)value;
            }
            else
            {
                return false;
            }

            return true;
        }

        public bool AllowHalfClosure
        {
            get { return Constants.True == Volatile.Read(ref this.allowHalfClosure); }
            set { Interlocked.Exchange(ref this.allowHalfClosure, value ? Constants.True : Constants.False); }
        }

        public int ReceiveBufferSize
        {
            get
            {
                try
                {
                    return this.Socket.ReceiveBufferSize;
                }
                catch (ObjectDisposedException ex)
                {
                    return ThrowHelper.ThrowChannelException_Get_Int(ex);
                }
                catch (SocketException ex)
                {
                    return ThrowHelper.ThrowChannelException_Get_Int(ex);
                }
            }
            set
            {
                try
                {
                    this.Socket.ReceiveBufferSize = value;
                }
                catch (ObjectDisposedException ex)
                {
                    ThrowHelper.ThrowChannelException_Set(ex);
                }
                catch (SocketException ex)
                {
                    ThrowHelper.ThrowChannelException_Set(ex);
                }
            }
        }

        public virtual int SendBufferSize
        {
            get
            {
                try
                {
                    return this.Socket.SendBufferSize;
                }
                catch (ObjectDisposedException ex)
                {
                    return ThrowHelper.ThrowChannelException_Get_Int(ex);
                }
                catch (SocketException ex)
                {
                    return ThrowHelper.ThrowChannelException_Get_Int(ex);
                }
            }
            set
            {
                try
                {
                    this.Socket.SendBufferSize = value;
                }
                catch (ObjectDisposedException ex)
                {
                    ThrowHelper.ThrowChannelException_Set(ex);
                }
                catch (SocketException ex)
                {
                    ThrowHelper.ThrowChannelException_Set(ex);
                }
            }
        }

        public int Linger
        {
            get
            {
                try
                {
                    LingerOption lingerState = this.Socket.LingerState;
                    return lingerState.Enabled ? lingerState.LingerTime : -1;
                }
                catch (ObjectDisposedException ex)
                {
                    return ThrowHelper.ThrowChannelException_Get_Int(ex);
                }
                catch (SocketException ex)
                {
                    return ThrowHelper.ThrowChannelException_Get_Int(ex);
                }
            }
            set
            {
                try
                {
                    if (value < 0)
                    {
                        this.Socket.LingerState = new LingerOption(false, 0);
                    }
                    else
                    {
                        if (s_lingerCache.TryGetValue(value, out var lingerOption))
                        {
                            this.Socket.LingerState = lingerOption;
                        }
                        else
                        {
                            this.Socket.LingerState = new LingerOption(true, value);
                        }
                    }
                }
                catch (ObjectDisposedException ex)
                {
                    ThrowHelper.ThrowChannelException_Set(ex);
                }
                catch (SocketException ex)
                {
                    ThrowHelper.ThrowChannelException_Set(ex);
                }
            }
        }

        public bool KeepAlive
        {
            get
            {
                try
                {
                    return (int)this.Socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive) != 0;
                }
                catch (ObjectDisposedException ex)
                {
                    return ThrowHelper.ThrowChannelException_Get_Bool(ex);
                }
                catch (SocketException ex)
                {
                    return ThrowHelper.ThrowChannelException_Get_Bool(ex);
                }
            }
            set
            {
                try
                {
                    this.Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, value ? 1 : 0);
                }
                catch (ObjectDisposedException ex)
                {
                    ThrowHelper.ThrowChannelException_Set(ex);
                }
                catch (SocketException ex)
                {
                    ThrowHelper.ThrowChannelException_Set(ex);
                }
            }
        }

        public bool ReuseAddress
        {
            get
            {
                try
                {
                    return (int)this.Socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress) != 0;
                }
                catch (ObjectDisposedException ex)
                {
                    return ThrowHelper.ThrowChannelException_Get_Bool(ex);
                }
                catch (SocketException ex)
                {
                    return ThrowHelper.ThrowChannelException_Get_Bool(ex);
                }
            }
            set
            {
                try
                {
                    this.Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, value ? 1 : 0);
                }
                catch (ObjectDisposedException ex)
                {
                    ThrowHelper.ThrowChannelException_Set(ex);
                }
                catch (SocketException ex)
                {
                    ThrowHelper.ThrowChannelException_Set(ex);
                }
            }
        }

        public bool TcpNoDelay
        {
            get
            {
                try
                {
                    return this.Socket.NoDelay;
                }
                catch (ObjectDisposedException ex)
                {
                    return ThrowHelper.ThrowChannelException_Get_Bool(ex);
                }
                catch (SocketException ex)
                {
                    return ThrowHelper.ThrowChannelException_Get_Bool(ex);
                }
            }
            set
            {
                try
                {
                    this.Socket.NoDelay = value;
                }
                catch (ObjectDisposedException ex)
                {
                    ThrowHelper.ThrowChannelException_Set(ex);
                }
                catch (SocketException ex)
                {
                    ThrowHelper.ThrowChannelException_Set(ex);
                }
            }
        }

        private static readonly Dictionary<int, LingerOption> s_lingerCache;
        static DefaultSocketChannelConfiguration()
        {
            s_lingerCache = new Dictionary<int, LingerOption>(11);
            for(var idx = 0; idx <= 10; idx++)
            {
                s_lingerCache.Add(idx, new LingerOption(true, idx));
            }
        }
    }
}