// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.GFX.DependencyInjection
{
    /// <summary>
    /// Aggregate over the wasi-gfx host surface for the
    /// transpiler-direct-link path. Symmetric with
    /// <c>WasiPreview2Bundle</c> and <c>WasiNNBundle</c>.
    ///
    /// <para>Holds the configured
    /// <see cref="WasiGfxConfiguration"/> + the configured
    /// <see cref="IBackend"/>. The per-resource concrete
    /// classes <c>HostPackageResolver</c> direct-links against
    /// (<c>Context</c>, <c>AbstractBuffer</c>, <c>Surface</c>,
    /// <c>Device</c>, <c>Buffer</c>) live alongside this
    /// bundle and are discovered by the resolver via
    /// <c>TryFindResourceImpl</c>. They use a parameterless
    /// ctor + <c>Create()</c> shape (the SourceGen-resource
    /// convention) and pull the backend from
    /// <see cref="WasiGfxAmbient"/> at <c>Create()</c> time —
    /// the bundle's role is to carry that backend through DI
    /// so the ambient is set before any wasm guest resource
    /// is minted.</para>
    /// </summary>
    public sealed class WasiGfxBundle
    {
        public WasiGfxConfiguration Configuration { get; }
        public IBackend? Backend => Configuration.Backend;

        public WasiGfxBundle(WasiGfxConfiguration configuration)
        {
            Configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
        }
    }
}
