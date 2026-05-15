// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Parameterless <see cref="IBindable"/> adapter for the
    /// CLI's <c>--wasi-gfx</c> / <c>--bind</c> path. The CLI
    /// resolves bindings by assembly name, activates every
    /// public parameterless-ctor <see cref="IBindable"/> type
    /// it finds, and calls <see cref="BindToRuntime"/>.
    /// Mirrors <c>WasiNNOnnxBindable</c> in shape.
    ///
    /// <para>Exposes the constructed <see cref="Backend"/> and
    /// <see cref="Host"/> after <see cref="BindToRuntime"/> so
    /// the CLI's <c>--windowed</c> path can find the SDL event
    /// pump entry point to drive from the main thread.</para>
    /// </summary>
    public sealed class WasiGfxSilkBindable : IBindable, IDisposable
    {
        public SilkGfxBackend? Backend { get; private set; }
        public WasiGfxHost? Host { get; private set; }

        /// <summary>
        /// Optional pre-constructed backend the CLI's
        /// <c>--windowed</c> path injects so the main thread can
        /// own the backend's <c>RunMainLoop</c> while the wasm
        /// guest binds on a worker. Reflection-discoverable so
        /// <c>Wacs.Console</c> doesn't need a hard ref to this
        /// assembly. When non-null at <see cref="BindToRuntime"/>
        /// time, the bindable uses this instance and clears the
        /// preset; otherwise it constructs a fresh backend per
        /// bind (the standalone-embedder path).
        /// </summary>
        public static SilkGfxBackend? PresetBackend { get; set; }

        public void BindToRuntime(WasmRuntime runtime)
        {
            Backend = PresetBackend ?? new SilkGfxBackend();
            // Preset is single-use — if a second bind runs on the
            // same process, it gets a fresh backend rather than
            // re-using the windowed-mode one.
            PresetBackend = null;
            Host = runtime.UseWasiGfx(b => b.WithBackend(Backend));
        }

        public void Dispose()
        {
            Host?.Dispose();
            Backend = null;
            Host = null;
        }
    }
}
