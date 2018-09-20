﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Codecs.Http
{
    using System;

    partial class HttpMethod : IEquatable<HttpMethod>
    {
        const byte CByte = (byte)'C';
        const byte DByte = (byte)'D';
        const byte GByte = (byte)'G';
        const byte HByte = (byte)'H';
        const byte OByte = (byte)'O';
        const byte PByte = (byte)'P';
        const byte UByte = (byte)'U';
        const byte AByte = (byte)'A';
        const byte TByte = (byte)'T';

        public bool Equals(HttpMethod other)
        {
            if (ReferenceEquals(this, other)) { return true; }
            return other != null && this.name.Equals(other.name);
        }
    }
}