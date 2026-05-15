// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

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
    /// <para>Wires Preview2 alongside wasi-gfx: surface
    /// pollables live in Preview2's <see cref="ResourceContext"/>
    /// so that the guest's <c>wasi:io/poll@0.2.0.poll</c>
    /// (bound by Preview2's <c>IoBindings</c>) finds them. The
    /// two hosts share one <c>ResourceContext</c> across all
    /// resource types, not just pollables.</para>
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
        public WasiPreview2Host? Preview2Host { get; private set; }
        public ResourceContext? SharedResources { get; private set; }

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

            // Wire Preview2 first so its WitBindings handlers
            // for wasi:io/poll are registered against the shared
            // ResourceContext; then wire wasi-gfx pointing at the
            // same context. Order matters: wasi-gfx's surface
            // .subscribe-* mints pollables INTO Preview2's table.
            SharedResources = new ResourceContext();
            Preview2Host = new WasiPreview2Host(new WasiPreview2HostBuilder
            {
                SharedResources = SharedResources,
                // Bind the top-level wasi:io/poll.poll function —
                // wasi-gfx surfaces need it. PollSource is the
                // stock implementation: walks pollables, blocks on
                // their AsTask() futures when none are ready.
                Poll = new PollSource(),
            });
            Preview2Host.BindToRuntime(runtime);

            Host = runtime.UseWasiGfx(b => b
                .WithBackend(Backend)
                .WithSharedResources(SharedResources));
        }

        public void Dispose()
        {
            Host?.Dispose();
            Preview2Host = null;
            SharedResources = null;
            Backend = null;
            Host = null;
        }
    }
}
