// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Threading;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// Core primitive for spawning wasm-level threads. Both wasi-threads (a thin host
    /// import adapter) and shared-everything's future <c>thread.spawn</c> instruction
    /// dispatch to one of these — the host-import layer and the instruction layer
    /// remain two clients of the same core, which is what makes the transition from
    /// wasi-threads to shared-everything purely additive.
    ///
    /// <para>Layer 1d introduces the interface; the default <see cref="ThreadBasedHost"/>
    /// creates a real <see cref="System.Threading.Thread"/> per spawn. Shared state
    /// (linear memory, tables, globals) is shared by reference through the runtime's
    /// <see cref="SharedRuntimeState"/>; each spawned thread gets its own ExecContext
    /// via the runtime's ThreadLocal (Layer 1c).</para>
    /// </summary>
    public interface IWasmThreadHost
    {
        /// <summary>
        /// Spawn a wasm function on a new host thread. The caller passes
        /// <paramref name="args"/> as a ReadOnlySpan; the implementation must copy
        /// the values into its own storage before the call returns — the underlying
        /// span must not outlive this method.
        ///
        /// <para>The <paramref name="ct"/> token lets the host cancel the spawned
        /// thread cooperatively; the running thread observes the token at call
        /// boundaries and traps with <c>TrapException("cancelled")</c> on
        /// cancellation (Layer 1f wires this into the invoke loop).</para>
        /// </summary>
        WasmThread Spawn(FuncAddr entry, ReadOnlySpan<Value> args, CancellationToken ct = default);
    }
}
