// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Threading;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// Single-threaded concurrency policy. Valid for Unity IL2CPP, WebGL,
    /// and any host that has not opted into multi-threaded wasm execution.
    /// <para>
    /// Semantics follow the threads-proposal wait/notify spec as applied
    /// to a single-thread host:
    /// <list type="bullet">
    ///   <item>Wait with mismatched current value → <c>not-equal</c> (2).</item>
    ///   <item>Wait with matching value and finite timeout → blocks the
    ///   calling thread via <see cref="Thread.Sleep(int)"/> for the
    ///   timeout, returns <c>timed-out</c> (1). Since nobody else can
    ///   notify, it is impossible to return <c>ok</c> under this policy.</item>
    ///   <item>Wait with matching value and infinite timeout → traps.
    ///   The wait would be a permanent deadlock; matches wasmtime / v8
    ///   behavior when the main thread calls wait on non-shared memory.</item>
    ///   <item>Notify → always 0 (no one to wake).</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class NotSupportedPolicy : IConcurrencyPolicy
    {
        public ConcurrencyPolicyMode Mode => ConcurrencyPolicyMode.NotSupported;

        public int Wait32(MemoryInstance mem, long addr, int expected, long timeoutNs)
        {
            int current = mem.AtomicLoadInt32((int)addr);
            if (current != expected) return 2; // not-equal
            return BlockOrTrap(timeoutNs);
        }

        public int Wait64(MemoryInstance mem, long addr, long expected, long timeoutNs)
        {
            long current = mem.AtomicLoadInt64((int)addr);
            if (current != expected) return 2;
            return BlockOrTrap(timeoutNs);
        }

        public int Notify(MemoryInstance mem, long addr, int maxWaiters) => 0;

        private static int BlockOrTrap(long timeoutNs)
        {
            if (timeoutNs < 0)
                throw new TrapException(
                    "atomic.wait would deadlock (ConcurrencyPolicy=NotSupported with infinite timeout)");
            // Round up to at least 1 ms, clamp to int.MaxValue.
            long ms = timeoutNs / 1_000_000;
            if (ms <= 0) ms = 0;
            else if (ms > int.MaxValue) ms = int.MaxValue;
            if (ms > 0) Thread.Sleep((int)ms);
            return 1; // timed-out
        }
    }
}
