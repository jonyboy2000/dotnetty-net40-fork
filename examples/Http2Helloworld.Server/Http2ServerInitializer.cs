﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Http2Helloworld.Server
{
    using System;
    using System.Collections.Generic;
    using System.Net.Security;
    using System.Security.Cryptography.X509Certificates;
    using DotNetty.Codecs.Http;
    using DotNetty.Codecs.Http2;
    using DotNetty.Common.Utilities;
    using DotNetty.Handlers.Tls;
    using DotNetty.Transport.Channels;
    using Microsoft.Extensions.Logging;

    public class Http2ServerInitializer : ChannelInitializer<IChannel>
    {
        static readonly ILogger s_logger = TraceLogger.GetLogger<Http2ServerInitializer>();

        static readonly HttpServerUpgradeCodecFactory UpgradeCodecFactory = new HttpServerUpgradeCodecFactory();

        readonly X509Certificate2 tlsCertificate;
        readonly int maxHttpContentLength;

        public Http2ServerInitializer(X509Certificate2 tlsCertificate)
            : this(tlsCertificate, 16 * 1024)
        {
        }

        public Http2ServerInitializer(X509Certificate2 tlsCertificate, int maxHttpContentLength)
        {
            if (maxHttpContentLength < 0)
            {
                throw new ArgumentException("maxHttpContentLength (expected >= 0): " + maxHttpContentLength);
            }
            this.tlsCertificate = tlsCertificate;
            this.maxHttpContentLength = maxHttpContentLength;
        }

        protected override void InitChannel(IChannel channel)
        {
            if (tlsCertificate != null)
            {
                this.ConfigureSsl(channel);
            }
            else
            {
                this.ConfigureClearText(channel);
            }
        }

        /**
         * Configure the pipeline for TLS NPN negotiation to HTTP/2.
         */
        void ConfigureSsl(IChannel ch)
        {
            ch.Pipeline.AddLast(new TlsHandler(new ServerTlsSettings(this.tlsCertificate)
#if NETCOREAPP_2_0_GREATER
                {
                    ApplicationProtocols = new List<SslApplicationProtocol>(new[]
                        {
                            SslApplicationProtocol.Http2,
                            SslApplicationProtocol.Http11
                        })
                }
#endif
                ));
#if NETCOREAPP_2_0_GREATER
            ch.Pipeline.AddLast(new Http2OrHttpHandler());
#else
            this.ConfigureClearText(ch);
#endif

        }

        void ConfigureClearText(IChannel ch)
        {
            IChannelPipeline p = ch.Pipeline;
            HttpServerCodec sourceCodec = new HttpServerCodec();
            HttpServerUpgradeHandler upgradeHandler = new HttpServerUpgradeHandler(sourceCodec, UpgradeCodecFactory);
            CleartextHttp2ServerUpgradeHandler cleartextHttp2ServerUpgradeHandler =
                   new CleartextHttp2ServerUpgradeHandler(sourceCodec, upgradeHandler,
                                                          new HelloWorldHttp2HandlerBuilder().Build());

            p.AddLast(cleartextHttp2ServerUpgradeHandler);
            p.AddLast(new HttpMessageHandler(this.maxHttpContentLength));

            p.AddLast(new UserEventLogger());
        }

        sealed class HttpServerUpgradeCodecFactory : HttpServerUpgradeHandler.IUpgradeCodecFactory
        {
            public HttpServerUpgradeHandler.IUpgradeCodec NewUpgradeCodec(ICharSequence protocol)
            {
                if (AsciiString.ContentEquals(Http2CodecUtil.HttpUpgradeProtocolName, protocol))
                {
                    return new Http2ServerUpgradeCodec(new HelloWorldHttp2HandlerBuilder().Build());
                }
                else
                {
                    return null;
                }
            }
        }

        sealed class HttpMessageHandler : SimpleChannelInboundHandler2<IHttpMessage>
        {
            readonly int maxHttpContentLength;

            public HttpMessageHandler(int maxHttpContentLength) => this.maxHttpContentLength = maxHttpContentLength;

            protected override void ChannelRead0(IChannelHandlerContext ctx, IHttpMessage msg)
            {
                // If this handler is hit then no upgrade has been attempted and the client is just talking HTTP.
                s_logger.LogInformation("Directly talking: " + msg.ProtocolVersion + " (no upgrade was attempted)");
                IChannelPipeline pipeline = ctx.Pipeline;
                IChannelHandlerContext thisCtx = pipeline.Context(this);
                pipeline.AddAfter(thisCtx.Name, null, new HelloWorldHttp1Handler("Direct. No Upgrade Attempted."));
                pipeline.Replace(this, null, new HttpObjectAggregator(this.maxHttpContentLength));
                ctx.FireChannelRead(ReferenceCountUtil.Retain(msg));
            }
        }

        /// <summary>
        /// Class that logs any User Events triggered on this channel.
        /// </summary>
        sealed class UserEventLogger : ChannelHandlerAdapter
        {
            public override void UserEventTriggered(IChannelHandlerContext context, object evt)
            {
                s_logger.LogInformation("User Event Triggered: " + evt);
                context.FireUserEventTriggered(evt);
            }
        }
    }
}
