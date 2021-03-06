﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Codecs
{
    using System;
    using DotNetty.Buffers;
    using DotNetty.Common.Concurrency;
    using DotNetty.Common.Utilities;
    using DotNetty.Transport.Channels;

    public abstract class MessageToByteEncoder<T> : ChannelHandlerAdapter
    {
        public virtual bool AcceptOutboundMessage(object message) => message is T;

        public override void Write(IChannelHandlerContext context, object message, IPromise promise)
        {
            if (null == context) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.context); }

            IByteBuffer buffer = null;
            try
            {
                if (this.AcceptOutboundMessage(message))
                {
                    buffer = this.AllocateBuffer(context);
                    var input = (T)message;
                    try
                    {
                        this.Encode(context, input, buffer);
                    }
                    finally
                    {
                        ReferenceCountUtil.Release(input);
                    }

                    if (buffer.IsReadable())
                    {
                        context.WriteAsync(buffer, promise);
                    }
                    else
                    {
                        buffer.Release();
                        context.WriteAsync(Unpooled.Empty, promise);
                    }

                    buffer = null;
                }
                else
                {
                    context.WriteAsync(message, promise);
                }
            }
            catch (EncoderException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ThrowHelper.ThrowEncoderException(ex);
            }
            finally
            {
                buffer?.Release();
            }
        }

        protected virtual IByteBuffer AllocateBuffer(IChannelHandlerContext context)
        {
            if (null == context) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.context); }

            return context.Allocator.Buffer();
        }

        protected abstract void Encode(IChannelHandlerContext context, T message, IByteBuffer output);
    }
}
