﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable 420

namespace DotNetty.Buffers
{
    using System.Threading;
    using DotNetty.Common;

    public abstract class AbstractReferenceCountedByteBuffer : AbstractByteBuffer
    {
        int referenceCount = 1;

        protected AbstractReferenceCountedByteBuffer(int maxCapacity)
            : base(maxCapacity)
        {
        }

        public override int ReferenceCount => Volatile.Read(ref this.referenceCount);

        //An unsafe operation intended for use by a subclass that sets the reference count of the buffer directly
        protected internal void SetReferenceCount(int value) => Interlocked.Exchange(ref this.referenceCount, value);

        public override IReferenceCounted Retain() => this.Retain0(1);

        public override IReferenceCounted Retain(int increment)
        {
            if (increment <= 0) { ThrowHelper.ThrowArgumentException_Positive(increment, ExceptionArgument.increment); }

            return this.Retain0(increment);
        }

        IReferenceCounted Retain0(int increment)
        {
            int refCnt = Volatile.Read(ref this.referenceCount);
            int oldRefCnt;
            do
            {
                oldRefCnt = refCnt;
                int nextCnt = refCnt + increment;

                // Ensure we not resurrect (which means the refCnt was 0) and also that we encountered an overflow.
                if (nextCnt <= increment) { ThrowHelper.ThrowIllegalReferenceCountException(refCnt, increment); }

                refCnt = Interlocked.CompareExchange(ref this.referenceCount, nextCnt, refCnt);
            } while (refCnt != oldRefCnt);

            return this;
        }

        public override IReferenceCounted Touch() => this;

        public override IReferenceCounted Touch(object hint) => this;

        public override bool Release() => this.Release0(1);

        public override bool Release(int decrement)
        {
            if (decrement <= 0) { ThrowHelper.ThrowArgumentException_Positive(decrement, ExceptionArgument.decrement); }

            return this.Release0(decrement);
        }

        bool Release0(int decrement)
        {
            int refCnt = Volatile.Read(ref this.referenceCount);
            int oldRefCnt;
            do
            {
                oldRefCnt = refCnt;

                if (refCnt < decrement) { ThrowHelper.ThrowIllegalReferenceCountException(refCnt, -decrement); }

                refCnt = Interlocked.CompareExchange(ref this.referenceCount, refCnt - decrement, refCnt);
            } while (refCnt != oldRefCnt);

            if (refCnt == decrement)
            {
                this.Deallocate();
                return true;
            }
            return false;
        }

        protected internal abstract void Deallocate();
    }
}