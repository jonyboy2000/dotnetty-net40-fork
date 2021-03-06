﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Codecs.Http.WebSockets.Extensions
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using DotNetty.Common.Concurrency;
    using DotNetty.Common.Utilities;
    using DotNetty.Transport.Channels;

    public class WebSocketClientExtensionHandler : ChannelHandlerAdapter
    {
        readonly List<IWebSocketClientExtensionHandshaker> extensionHandshakers;

        public WebSocketClientExtensionHandler(params IWebSocketClientExtensionHandshaker[] extensionHandshakers)
        {
            if (null == extensionHandshakers || extensionHandshakers.Length <= 0) { ThrowHelper.ThrowArgumentNullException(ExceptionArgument.extensionHandshakers); }
            this.extensionHandshakers = new List<IWebSocketClientExtensionHandshaker>(extensionHandshakers);
        }

        public override void Write(IChannelHandlerContext ctx, object msg, IPromise promise)
        {
            if (msg is IHttpRequest request && WebSocketExtensionUtil.IsWebsocketUpgrade(request.Headers))
            {
                string headerValue = null;
                if (request.Headers.TryGet(HttpHeaderNames.SecWebsocketExtensions, out ICharSequence value))
                {
                    headerValue = value.ToString();
                }

                foreach (IWebSocketClientExtensionHandshaker extensionHandshaker in this.extensionHandshakers)
                {
                    WebSocketExtensionData extensionData = extensionHandshaker.NewRequestData();
                    headerValue = WebSocketExtensionUtil.AppendExtension(headerValue,
                        extensionData.Name, extensionData.Parameters);
                }

                request.Headers.Set(HttpHeaderNames.SecWebsocketExtensions, headerValue);
            }

            base.Write(ctx, msg, promise);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttpResponse response
                && WebSocketExtensionUtil.IsWebsocketUpgrade(response.Headers))
            {
                string extensionsHeader = null;
                if (response.Headers.TryGet(HttpHeaderNames.SecWebsocketExtensions, out ICharSequence value))
                {
                    extensionsHeader = value.ToString();
                }

                var pipeline = ctx.Pipeline;
                if (extensionsHeader != null)
                {
                    List<WebSocketExtensionData> extensions =
                        WebSocketExtensionUtil.ExtractExtensions(extensionsHeader);
                    var validExtensions = new List<IWebSocketClientExtension>(extensions.Count);
                    int rsv = 0;

                    foreach (WebSocketExtensionData extensionData in extensions)
                    {
                        IWebSocketClientExtension validExtension = null;
                        foreach (IWebSocketClientExtensionHandshaker extensionHandshaker in this.extensionHandshakers)
                        {
                            validExtension = extensionHandshaker.HandshakeExtension(extensionData);
                            if (validExtension != null)
                            {
                                break;
                            }
                        }

                        if (validExtension != null && (validExtension.Rsv & rsv) == 0)
                        {
                            rsv = rsv | validExtension.Rsv;
                            validExtensions.Add(validExtension);
                        }
                        else
                        {
                            ThrowHelper.ThrowCodecException_InvalidWSExHandshake(extensionsHeader);
                        }
                    }

                    foreach (IWebSocketClientExtension validExtension in validExtensions)
                    {
                        WebSocketExtensionDecoder decoder = validExtension.NewExtensionDecoder();
                        WebSocketExtensionEncoder encoder = validExtension.NewExtensionEncoder();
                        pipeline.AddAfter(ctx.Name, decoder.GetType().Name, decoder);
                        pipeline.AddAfter(ctx.Name, encoder.GetType().Name, encoder);
                    }
                }

                pipeline.Remove(ctx.Name);
            }

            base.ChannelRead(ctx, msg);
        }
    }
}
