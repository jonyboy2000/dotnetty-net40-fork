﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Common.Concurrency
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
//#if NET_4_5_GREATER
//    using System.Threading;
//#endif

    public interface IPromise
    {
        Task Task { get; }

        bool IsVoid { get; }

        bool IsCompleted { get; }

        bool IsSuccess { get; }

        bool IsFaulted { get; }

        bool IsCanceled { get; }

        bool TryComplete();

        void Complete();

        bool TrySetException(Exception exception);

        bool TrySetException(IEnumerable<Exception> exceptions);

        void SetException(Exception exception);

        void SetException(IEnumerable<Exception> exceptions);

//#if NET_4_5_GREATER
//        bool TrySetCanceled(CancellationToken cancellationToken);
//#endif

        bool TrySetCanceled();

        void SetCanceled();

        bool SetUncancellable();

        IPromise Unvoid();
    }
}