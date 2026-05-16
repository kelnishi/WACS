// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.GFX
{
    /// <summary>
    /// Embedder-supplied configuration for the wasi-gfx host.
    /// Mirrors the shape of <c>WasiNNConfiguration</c> — a POCO
    /// with a static <see cref="DefaultConfiguration"/> factory.
    /// Configurations pass into <c>WasiGfxHost</c>'s ctor by
    /// value; mutating after construction has no effect.
    ///
    /// <para>One <see cref="Backend"/> per host — wasi-gfx has
    /// no encoding axis the way wasi-nn does. A host without a
    /// backend rejects every <c>graphics-context.context</c>
    /// constructor with <see cref="Types.WasiGfxException"/>.</para>
    /// </summary>
    public sealed class WasiGfxConfiguration
    {
        /// <summary>
        /// The single backend that handles surface, frame-buffer,
        /// and graphics-context for this host. One backend covers
        /// every package the host serves; a separate webgpu host
        /// holds the matching GPU backend.
        /// </summary>
        public IBackend? Backend { get; set; }

        /// <summary>
        /// Shared resource context — the same instance Preview2's
        /// <c>WasiPreview2Host</c> uses. wasi-gfx's surface
        /// <c>subscribe-*</c> methods mint pollables into
        /// <c>SharedResources.Table&lt;Wacs.WASI.Preview2.Io.Pollable&gt;()</c>
        /// so that Preview2's <c>wasi:io/poll@0.2.0.poll</c>
        /// binding can resolve those handles. REQUIRED for any
        /// real-world embedding — the runtime cannot service
        /// surface pollables without it.
        /// </summary>
        public Wacs.WASI.Preview2.HostBinding.ResourceContext? SharedResources { get; set; }

        /// <summary>
        /// Standard-defaults factory. Returns a configuration
        /// with no backend wired — the embedder fills in
        /// <see cref="Backend"/> before constructing the host.
        /// </summary>
        public static WasiGfxConfiguration DefaultConfiguration()
        {
            return new WasiGfxConfiguration();
        }
    }
}
