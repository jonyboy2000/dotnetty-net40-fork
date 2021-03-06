﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Codecs.Http2
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Settings for one endpoint in an HTTP/2 connection. Each of the values are optional as defined in
    /// the spec for the SETTINGS frame. Permits storage of arbitrary key/value pairs but provides helper
    /// methods for standard settings.
    /// </summary>
    public class Http2Settings : Dictionary<char, long>
    {
        /// <summary>
        /// Default capacity based on the number of standard settings from the HTTP/2 spec, adjusted so that adding all of
        /// the standard settings will not cause the map capacity to change.
        /// </summary>
        const long FALSE = 0L;
        const long True = 1L;

        public Http2Settings()
            : this(Http2CodecUtil.NumStandardSettings)
        {
        }


        public Http2Settings(int initialCapacity)
            : base(initialCapacity)
        {
        }

        /// <summary>
        /// Adds the given setting key/value pair. For standard settings defined by the HTTP/2 spec, performs
        /// validation on the values.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">if verification for a standard HTTP/2 setting fails.</exception>
        public long Put(char key, long value)
        {
            VerifyStandardSetting(key, value);
            return this[key] = value;
        }

        /// <summary>
        /// Gets the <see cref="Http2CodecUtil.SettingsHeaderTableSize"/> value. If unavailable, returns <c>null</c>.
        /// </summary>
        public long? HeaderTableSize()
        {
            return this.TryGetValue(Http2CodecUtil.SettingsHeaderTableSize, out var val) ? val : default(long?);
        }

        /// <summary>
        /// Sets the <see cref="Http2CodecUtil.SettingsHeaderTableSize"/> value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">if verification of the setting fails.</exception>
        public Http2Settings HeaderTableSize(long value)
        {
            this.Put(Http2CodecUtil.SettingsHeaderTableSize, value);
            return this;
        }

        /// <summary>
        /// Gets the <see cref="Http2CodecUtil.SettingsEnablePush"/> value. If unavailable, returns <c>null</c>.
        /// </summary>
        /// <returns></returns>
        public bool? PushEnabled()
        {
            return this.TryGetValue(Http2CodecUtil.SettingsEnablePush, out var val) ? val == True : default(bool?);
        }

        /// <summary>
        /// Sets the <see cref="Http2CodecUtil.SettingsEnablePush"/> value.
        /// </summary>
        /// <param name="enabled"></param>
        /// <returns></returns>
        public Http2Settings PushEnabled(bool enabled)
        {
            this.Put(Http2CodecUtil.SettingsEnablePush, enabled ? True : FALSE);
            return this;
        }

        /// <summary>
        /// Gets the <see cref="Http2CodecUtil.SettingsMaxConcurrentStreams"/> value. If unavailable, returns <c>null</c>.
        /// </summary>
        /// <returns></returns>
        public long? MaxConcurrentStreams()
        {
            return this.TryGetValue(Http2CodecUtil.SettingsMaxConcurrentStreams, out var val) ? val : default(long?);
        }

        /// <summary>
        /// Sets the <see cref="Http2CodecUtil.SettingsMaxConcurrentStreams"/> value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">if verification of the setting fails.</exception>
        public Http2Settings MaxConcurrentStreams(long value)
        {
            this.Put(Http2CodecUtil.SettingsMaxConcurrentStreams, value);
            return this;
        }

        /// <summary>
        /// Gets the <see cref="Http2CodecUtil.SettingsInitialWindowSize"/> value. If unavailable, returns <c>null</c>.
        /// </summary>
        /// <returns></returns>
        public int? InitialWindowSize()
        {
            return GetIntValue(Http2CodecUtil.SettingsInitialWindowSize);
        }

        /// <summary>
        /// Sets the <see cref="Http2CodecUtil.SettingsInitialWindowSize"/> value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">if verification of the setting fails.</exception>
        public Http2Settings InitialWindowSize(int value)
        {
            this.Put(Http2CodecUtil.SettingsInitialWindowSize, value);
            return this;
        }

        /// <summary>
        /// Gets the <see cref="Http2CodecUtil.SettingsMaxFrameSize"/> value. If unavailable, returns <c>null</c>.
        /// </summary>
        /// <returns></returns>
        public int? MaxFrameSize()
        {
            return GetIntValue(Http2CodecUtil.SettingsMaxFrameSize);
        }

        /// <summary>
        /// Sets the <see cref="Http2CodecUtil.SettingsMaxFrameSize"/> value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">if verification of the setting fails.</exception>
        public Http2Settings MaxFrameSize(int value)
        {
            this.Put(Http2CodecUtil.SettingsMaxFrameSize, value);
            return this;
        }

        /// <summary>
        /// Gets the <see cref="Http2CodecUtil.SettingsMaxHeaderListSize"/> value. If unavailable, returns <c>null</c>.
        /// </summary>
        /// <returns></returns>
        public long? MaxHeaderListSize()
        {
            return this.TryGetValue(Http2CodecUtil.SettingsMaxHeaderListSize, out var val) ? val : default(long?);
        }

        /// <summary>
        /// Sets the <see cref="Http2CodecUtil.SettingsMaxHeaderListSize"/>  value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">if verification of the setting fails.</exception>
        public Http2Settings MaxHeaderListSize(long value)
        {
            this.Put(Http2CodecUtil.SettingsMaxHeaderListSize, value);
            return this;
        }

        /// <summary>
        /// Clears and then copies the given settings into this object.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public Http2Settings CopyFrom(Http2Settings settings)
        {
            this.Clear();
            foreach (KeyValuePair<char, long> pair in settings)
            {
                this[pair.Key] = pair.Value;
            }
            return this;
        }

        /// <summary>
        /// A helper method that returns <see cref="int"/> on the return of {@link #get(char)}, if present. Note that
        /// if the range of the value exceeds <see cref="int.MaxValue"/>, the {@link #get(char)} method should
        /// be used instead to avoid truncation of the value.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public int? GetIntValue(char key)
        {
            return this.TryGetValue(key, out var val) ? (int)val : default(int?);
        }

        static void VerifyStandardSetting(int key, long value)
        {
            switch (key)
            {
                case Http2CodecUtil.SettingsHeaderTableSize:
                    if (value < Http2CodecUtil.MinHeaderTableSize || value > Http2CodecUtil.MaxHeaderTableSize)
                    {
                        ThrowHelper.ThrowArgumentException_InvalidHeaderTableSize(value);
                    }
                    break;
                case Http2CodecUtil.SettingsEnablePush:
                    if (value != 0L && value != 1L)
                    {
                        ThrowHelper.ThrowArgumentException_InvalidEnablePush(value);
                    }
                    break;
                case Http2CodecUtil.SettingsMaxConcurrentStreams:
                    if (value < Http2CodecUtil.MinConcurrentStreams || value > Http2CodecUtil.MaxConcurrentStreams)
                    {
                        ThrowHelper.ThrowArgumentException_InvalidConcurrentStreams(value);
                    }
                    break;
                case Http2CodecUtil.SettingsInitialWindowSize:
                    if (value < Http2CodecUtil.MinInitialWindowSize || value > Http2CodecUtil.MaxInitialWindowSize)
                    {
                        ThrowHelper.ThrowArgumentException_InvalidInitialWindowSize(value);
                    }
                    break;
                case Http2CodecUtil.SettingsMaxFrameSize:
                    if (!Http2CodecUtil.IsMaxFrameSizeValid(value))
                    {
                        ThrowHelper.ThrowArgumentException_InvalidMaxFrameSize(value);
                    }
                    break;
                case Http2CodecUtil.SettingsMaxHeaderListSize:
                    if (value < Http2CodecUtil.MinHeaderListSize || value > Http2CodecUtil.MaxHeaderListSize)
                    {
                        ThrowHelper.ThrowArgumentException_InvalidHeaderListSize(value);
                    }
                    break;
                default:
                    // Non-standard HTTP/2 setting - don't do validation.
                    break;
            }
        }


        protected string KeyToString(char key)
        {
            switch (key)
            {
                case Http2CodecUtil.SettingsHeaderTableSize:
                    return "HEADER_TABLE_SIZE";
                case Http2CodecUtil.SettingsEnablePush:
                    return "ENABLE_PUSH";
                case Http2CodecUtil.SettingsMaxConcurrentStreams:
                    return "Http2CodecUtil.MAX_CONCURRENT_STREAMS";
                case Http2CodecUtil.SettingsInitialWindowSize:
                    return "INITIAL_WINDOW_SIZE";
                case Http2CodecUtil.SettingsMaxFrameSize:
                    return "Http2CodecUtil.MAX_FRAME_SIZE";
                case Http2CodecUtil.SettingsMaxHeaderListSize:
                    return "Http2CodecUtil.MAX_HEADER_LIST_SIZE";
                default:
                    // Unknown keys.
                    return key.ToString();
            }
        }

        public static Http2Settings DefaultSettings()
        {
            return new Http2Settings().MaxHeaderListSize(Http2CodecUtil.DefaultHeaderListSize);
        }
    }
}