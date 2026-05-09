// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;

namespace Wacs.WASI.Threads
{
    /// <summary>
    /// Ergonomic one-liner to wire wasi-threads onto a
    /// <see cref="WasmRuntime"/>. Mirrors the shape of
    /// <c>runtime.UseWasiPreview2(...)</c> /
    /// <c>runtime.UseWasiNN(...)</c> so the host-binding wire-up
    /// reads consistently across the WASI host family.
    ///
    /// <para>Usage (wire before module instantiation so the
    /// <c>wasi:thread-spawn</c> import resolves):</para>
    /// <code>
    ///     var runtime = new WasmRuntime();
    ///     runtime.UseWasiThreads();
    ///     var inst = runtime.InstantiateModule(module);
    /// </code>
    /// </summary>
    public static class WasiThreadsRuntimeExtensions
    {
        /// <summary>
        /// Construct and bind a fresh <see cref="WasiThreads"/>.
        /// Returns the binding so the caller can hold its lifetime
        /// (the spawn handler keeps a reference to the runtime
        /// for the duration of any spawned thread's execution).
        /// </summary>
        public static WasiThreads UseWasiThreads(this WasmRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            var threads = new WasiThreads();
            threads.BindToRuntime(runtime);
            return threads;
        }
    }
}
