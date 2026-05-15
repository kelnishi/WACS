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
    /// <para>v0 surface: just the configured
    /// <see cref="WasiGfxConfiguration"/> + the configured
    /// <see cref="IBackend"/>. The per-resource concrete
    /// classes that <c>HostPackageResolver</c> would direct-link
    /// against (mirroring WASI.NN's <c>Tensor</c>,
    /// <c>Graph</c>, <c>GraphExecutionContext</c>,
    /// <c>Error</c>) are a v1 follow-up — the wasi-gfx
    /// resources (context, surface, frame-buffer device +
    /// buffer, abstract-buffer) all use <c>constructor(...)</c>
    /// rather than free-function factories, so the binding
    /// shape is different. v0 ships the interpreter path
    /// via <c>WitBindings.cs</c>; transpiler-direct-link wiring
    /// lands when a wasi-gfx component fixture exercises it.</para>
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
