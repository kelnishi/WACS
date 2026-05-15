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
        /// and graphics-context for this host. v1 will add a
        /// separate slot for webgpu when that package lands;
        /// for v0, one backend covers everything it implements.
        /// </summary>
        public IBackend? Backend { get; set; }

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
