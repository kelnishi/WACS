// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.

using System;
using System.Threading;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace Wacs.WASI.Threads
{
    /// <summary>
    /// wasi-threads host-import adapter. Binds the
    /// <c>wasi:thread-spawn</c> import onto the core
    /// <see cref="Wacs.Core.Runtime.Concurrency.IWasmThreadHost"/> primitive so
    /// wasm modules with shared memory can spawn worker threads that share
    /// the module's linear memory, tables, and globals.
    ///
    /// <para>Usage (wire before module instantiation so the import resolves):</para>
    /// <code>
    ///     var runtime = new WasmRuntime();
    ///     new WasiThreads().BindToRuntime(runtime);
    ///     var inst = runtime.InstantiateModule(module);
    /// </code>
    ///
    /// <para>Spec: <a href="https://github.com/WebAssembly/wasi-threads">github.com/WebAssembly/wasi-threads</a>.
    /// Module contract:</para>
    /// <list type="bullet">
    /// <item>Declares or imports shared memory (no spec-level check here — the
    /// first atomic op traps if memory isn't shared).</item>
    /// <item>Exports <c>wasi_thread_start (param i32 i32)</c> — the per-thread
    /// entry point that the host invokes with <c>(tid, start_arg)</c>.</item>
    /// <item>Imports <c>(wasi) (thread-spawn) (param i32) (result i32)</c> —
    /// call with <c>start_arg</c>; get back a positive tid, or a negative value
    /// on failure.</item>
    /// </list>
    ///
    /// <para>A future shared-everything-threads <c>thread.spawn</c> *instruction*
    /// would dispatch through the same
    /// <see cref="Wacs.Core.Runtime.Concurrency.IWasmThreadHost"/> primitive —
    /// the wasm-host-import surface (this class) and the wasm-instruction
    /// surface remain two clients of one core, by design.</para>
    /// </summary>
    public sealed class WasiThreads : IBindable
    {
        private const string ModuleName = "wasi";
        private const string SpawnFunctionName = "thread-spawn";

        /// <summary>
        /// Name of the exported entry function the spec requires on any module
        /// that calls <c>thread-spawn</c>. Signature: <c>(param i32 i32)</c> —
        /// <c>(tid, start_arg)</c>. Missing export → spawn returns -1.
        /// </summary>
        public const string ThreadStartExport = "wasi_thread_start";

        private WasmRuntime? _runtime;

        /// <summary>
        /// Monotonic source of positive-i32 thread ids. Starts at 0, first
        /// <see cref="Interlocked.Increment(ref int)"/> yields tid 1. No
        /// recycling — if a process spawns &gt;int.MaxValue threads over its
        /// lifetime, subsequent spawns return -1. Recycling can be added
        /// as an additive change when a real workload needs it.
        /// </summary>
        private int _nextTid;

        public void BindToRuntime(WasmRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            _runtime = runtime;
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (ModuleName, SpawnFunctionName), ThreadSpawn);
        }

        /// <summary>
        /// Handler for <c>wasi:thread-spawn</c>. Returns a positive tid on
        /// success, or -1 on any failure (no <c>wasi_thread_start</c> export,
        /// runtime not bound, tid counter exhausted, spawn machinery threw).
        /// </summary>
        private int ThreadSpawn(ExecContext ctx, int startArg)
        {
            var runtime = _runtime;
            if (runtime == null) return -1;

            // Resolve wasi_thread_start on the module that called thread-spawn.
            // Using ctx.Frame.Module avoids an explicit RegisterModule step —
            // the calling module's own exports are the source of truth.
            var entry = FindThreadStart(ctx.Frame.Module);
            if (entry == null) return -1;

            int tid = Interlocked.Increment(ref _nextTid);
            if (tid <= 0) return -1; // exhaustion / wraparound

            try
            {
                runtime.ThreadHost.Spawn(
                    entry.Value,
                    new[] { (Value)tid, (Value)startArg });
            }
            catch
            {
                return -1;
            }

            return tid;
        }

        private static FuncAddr? FindThreadStart(ModuleInstance module)
        {
            foreach (var export in module.Exports)
            {
                if (export.Name == ThreadStartExport
                    && export.Value is ExternalValue.Function f)
                {
                    return f.Address;
                }
            }
            return null;
        }
    }
}
