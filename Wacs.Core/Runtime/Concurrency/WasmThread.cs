// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// Handle to a wasm-level thread spawned via <see cref="IWasmThreadHost.Spawn"/>.
    /// Task-based completion lets callers <c>await</c> the thread, compose with
    /// other asynchronous work, and observe traps/cancellation uniformly.
    ///
    /// <para>The underlying execution runs to completion on a dedicated host
    /// <see cref="System.Threading.Thread"/> (no <see cref="SynchronizationContext"/>
    /// capture, no thread-pool contention with other async work). Layer 1 only wires
    /// the sync path; future layers can add async <c>atomic.wait</c> yielding on top
    /// without changing this surface — the ExecContext is movable across host
    /// threads (all state on the managed heap, nothing stack-allocated).</para>
    ///
    /// <para>Forward-compatible with both wasi-threads (thin adapter owns positive-i32
    /// tid allocation and <c>wasi_thread_start</c> wiring) and shared-everything's
    /// future <c>thread.spawn</c> instruction (handlers dispatch to the same
    /// <see cref="IWasmThreadHost"/>).</para>
    /// </summary>
    public sealed class WasmThread
    {
        private readonly TaskCompletionSource<TrapException?> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile string? _trapReason;

        public int HostId { get; }

        /// <summary>
        /// Completes when the thread exits. The result is <c>null</c> for a clean
        /// return, a <see cref="TrapException"/> for a wasm-level trap (including
        /// traps requested via <see cref="RequestTrap"/> or CancellationToken).
        /// A non-trap host exception surfaces as a faulted task.
        /// </summary>
        public Task<TrapException?> Completion => _tcs.Task;

        internal WasmThread(int hostId)
        {
            HostId = hostId;
        }

        /// <summary>
        /// Request cooperative termination. The running thread will trap at the next
        /// instruction boundary check (Layer 1f wires the invoke loop to observe
        /// this). Safe to call from any thread.
        /// </summary>
        public void RequestTrap(string reason)
        {
            _trapReason = reason ?? "WasmThread.RequestTrap";
        }

        /// <summary>Non-null when <see cref="RequestTrap"/> or a CancellationToken has fired.</summary>
        internal string? TrapReason => _trapReason;

        internal void Complete(TrapException? trap) => _tcs.TrySetResult(trap);
        internal void Fault(Exception exc) => _tcs.TrySetException(exc);
    }
}
