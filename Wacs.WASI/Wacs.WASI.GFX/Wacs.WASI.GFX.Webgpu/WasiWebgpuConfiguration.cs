// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

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
        /// host. v1 phase 3c sets this to the same
        /// <c>SilkGfxBackend</c> instance that v0 provides for
        /// the CPU IBackend path — one backend, two SPIs.
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

        public static WasiWebgpuConfiguration DefaultConfiguration()
        {
            return new WasiWebgpuConfiguration();
        }
    }
}
