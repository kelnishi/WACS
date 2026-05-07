// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Threading;
using System.Threading.Tasks;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// Backend for <c>memory.atomic.wait32</c>, <c>memory.atomic.wait64</c>,
    /// and <c>memory.atomic.notify</c> from the WebAssembly threads proposal.
    /// The runtime dispatches every wait/notify op through a single
    /// <see cref="IConcurrencyPolicy"/> instance attached to
    /// <see cref="RuntimeAttributes.ConcurrencyPolicy"/>.
    /// <para>
    /// Return-value conventions match the threads-proposal spec:
    /// <list type="bullet">
    ///   <item><c>Wait32</c>/<c>Wait64</c>: 0 = ok (notified), 1 = timed-out, 2 = not-equal.</item>
    ///   <item><c>Notify</c>: count of waiters actually woken.</item>
    /// </list>
    /// </para>
    /// </summary>
    public interface IConcurrencyPolicy
    {
        ConcurrencyPolicyMode Mode { get; }

        /// <summary>
        /// Wait on a 32-bit value at <paramref name="addr"/> in
        /// <paramref name="mem"/>. Returns 0 (ok), 1 (timed-out), or 2
        /// (not-equal — current value did not match <paramref name="expected"/>).
        /// </summary>
        /// <param name="mem">Target memory; must be a shared memory.</param>
        /// <param name="addr">Effective address (post-offset); must be
        /// 4-byte aligned.</param>
        /// <param name="expected">Value to compare against the current cell.</param>
        /// <param name="timeoutNs">Timeout in nanoseconds. Negative means
        /// infinite.</param>
        int Wait32(MemoryInstance mem, long addr, int expected, long timeoutNs);

        /// <summary>
        /// Wait on a 64-bit value at <paramref name="addr"/>. Returns 0
        /// (ok), 1 (timed-out), or 2 (not-equal).
        /// </summary>
        int Wait64(MemoryInstance mem, long addr, long expected, long timeoutNs);

        /// <summary>
        /// Notify up to <paramref name="maxWaiters"/> waiters at
        /// <paramref name="addr"/>. Returns the number of waiters actually
        /// woken.
        /// </summary>
        int Notify(MemoryInstance mem, long addr, int maxWaiters);

        // ----- Async surface (Layer 1e) -----
        //
        // Default-implemented async variants wrap the synchronous ones on the
        // calling thread. They exist so a future policy implementation can
        // override them with a truly-yielding async wait — letting a wasm thread
        // blocked on atomic.wait release its host thread back to the pool and
        // resume on a different thread when notified. ExecContext is movable
        // across host threads (nothing stack-allocated), so this is a real option.
        //
        // Layer 1 ships only the surface; no async implementation is wired yet.
        // Introducing the interface now keeps later additions purely additive —
        // existing implementations (NotSupportedPolicy, HostDefinedPolicy) get
        // the default wrapper for free.

        /// <summary>
        /// Async counterpart to <see cref="Wait32"/>. Default implementation
        /// calls the synchronous version on the calling thread. Override for a
        /// truly-yielding implementation.
        /// </summary>
        ValueTask<int> Wait32Async(MemoryInstance mem, long addr, int expected,
            long timeoutNs, CancellationToken ct = default)
            => new ValueTask<int>(Wait32(mem, addr, expected, timeoutNs));

        /// <summary>
        /// Async counterpart to <see cref="Wait64"/>. Default implementation
        /// calls the synchronous version on the calling thread.
        /// </summary>
        ValueTask<int> Wait64Async(MemoryInstance mem, long addr, long expected,
            long timeoutNs, CancellationToken ct = default)
            => new ValueTask<int>(Wait64(mem, addr, expected, timeoutNs));

        /// <summary>
        /// Async counterpart to <see cref="Notify"/>. Default implementation
        /// calls the synchronous version on the calling thread.
        /// </summary>
        ValueTask<int> NotifyAsync(MemoryInstance mem, long addr, int maxWaiters,
            CancellationToken ct = default)
            => new ValueTask<int>(Notify(mem, addr, maxWaiters));
    }
}
