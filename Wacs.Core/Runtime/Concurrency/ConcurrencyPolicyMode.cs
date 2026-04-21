// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// High-level classification of the active <see cref="IConcurrencyPolicy"/>.
    /// The threads proposal does not standardize how hosts spawn wasm threads;
    /// it only standardizes shared memory + atomic ops + wait/notify. This mode
    /// lets callers introspect whether the runtime is configured to actually
    /// block on <c>memory.atomic.wait*</c> and wake via <c>memory.atomic.notify</c>
    /// (<see cref="HostDefined"/>), or to run single-threaded with
    /// immediate-return wait semantics (<see cref="NotSupported"/>).
    /// </summary>
    public enum ConcurrencyPolicyMode : byte
    {
        /// <summary>
        /// Single-threaded runtime. <c>atomic.wait</c> with a finite timeout
        /// returns <c>timed-out</c> after the timeout elapses; with an
        /// infinite timeout (&lt; 0) traps, since there is no one to notify.
        /// <c>notify</c> is a no-op returning 0.
        /// </summary>
        NotSupported,

        /// <summary>
        /// Host has opted in to concurrent wasm execution. The runtime
        /// maintains a wait-queue keyed on <c>(MemoryInstance, address)</c>
        /// and blocks waiters until a matching <c>notify</c> fires or the
        /// timeout elapses.
        /// </summary>
        HostDefined,
    }
}
