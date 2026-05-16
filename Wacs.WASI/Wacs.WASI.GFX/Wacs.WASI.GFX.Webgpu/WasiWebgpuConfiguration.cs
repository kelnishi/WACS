// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.GFX.Webgpu
{
    /// <summary>
    /// Embedder-supplied configuration for the wasi-webgpu host.
    /// Mirrors the shape of <see cref="WasiGfxConfiguration"/> —
    /// a POCO with a static <see cref="DefaultConfiguration"/>
    /// factory. One <see cref="Backend"/> per host; the backend
    /// covers every gpu-* resource. A host without a backend
    /// rejects <c>gpu()</c> construction with a clear error.
    /// </summary>
    public sealed class WasiWebgpuConfiguration
    {
        /// <summary>
        /// The single backend driving webgpu resources for this
        /// host. The combined CLI path points this at the same
        /// <c>SilkGfxBackend</c> instance that serves the CPU
        /// IBackend host — one backend, two SPIs.
        /// </summary>
        public IGpuBackend? Backend { get; set; }

        /// <summary>
        /// Shared resource context — the same instance Preview2's
        /// <c>WasiPreview2Host</c> and <c>WasiGfxHost</c> use.
        /// Webgpu doesn't currently mint pollables itself
        /// (request-adapter is sync in the wit), but future async
        /// flows (gpu-device.lost / queue.on-submitted-work-done)
        /// will route pollables through this context so
        /// Preview2's <c>wasi:io/poll.poll</c> binding can resolve
        /// them.
        /// </summary>
        public Wacs.WASI.Preview2.HostBinding.ResourceContext? SharedResources { get; set; }

        /// <summary>
        /// Cross-host bridge for the
        /// <c>[static]gpu-texture.from-graphics-buffer</c>
        /// import: maps an <c>abstract-buffer</c> wasm handle
        /// to its host-side <see cref="IAbstractBuffer"/>
        /// instance. The handle lives in <c>WasiGfxHost</c>'s
        /// AbstractBuffers table (a sibling host); the webgpu
        /// dispatcher doesn't see that table directly. Null in
        /// pure-webgpu embedders (no graphics-context bridge);
        /// <c>WasiGfxSilkBindable</c> wires it to point at the
        /// CPU host's lookup so the bridge works in the
        /// combined CLI path.
        /// </summary>
        public Func<int, IAbstractBuffer?>? AbstractBufferResolver { get; set; }

        /// <summary>
        /// Cross-host bridge for
        /// <c>[method]gpu-device.connect-graphics-context</c>:
        /// resolves a wasi-gfx-side <c>context</c> handle to a
        /// <c>wasi:graphics-context.context</c> instance the
        /// webgpu device's <c>IGpuDevice.ConnectGraphicsContext</c>
        /// can consume. Parallels <see cref="AbstractBufferResolver"/>
        /// but for the Contexts table on the sibling host. The
        /// embedder wiring (Silk bindable) is responsible for
        /// adapting the SPI-side stored object to this
        /// wit-generated interface — the interpreter Contexts table
        /// stores <c>IGraphicsContext</c> SPI instances directly,
        /// whereas the DI/transpiler path stores
        /// <c>DependencyInjection.Context</c> wrappers that already
        /// implement <see cref="Wacs.WASI.GFX.GraphicsContext.IContext"/>.
        /// </summary>
        public Func<int, Wacs.WASI.GFX.GraphicsContext.IContext?>? GraphicsContextResolver { get; set; }

        public static WasiWebgpuConfiguration DefaultConfiguration()
        {
            return new WasiWebgpuConfiguration();
        }
    }
}
