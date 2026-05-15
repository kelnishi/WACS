// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.DependencyInjection;

namespace Wacs.WASI.GFX.DependencyInjection
{
    /// <summary>
    /// Composite bundle exposing both <c>WasiPreview2Bundle</c>
    /// and <see cref="WasiGfxBundle"/> through a single CLR
    /// object. Used when a component imports both
    /// <c>wasi:cli/*</c> (Preview 2) AND any of the wasi-gfx
    /// packages. The module ctor still takes a single
    /// <c>object hostBundle</c> slot; this composite satisfies
    /// every binding both packages contribute through one slot.
    ///
    /// <para>v1 phase 1 deferred item 1h: the constructor +
    /// forwarding-properties body lands via the
    /// <c>CompositeBundleGenerator</c> source-generator, driven
    /// by the <see cref="WacsCompositeBundleGenAttribute"/> on
    /// this partial class. Was ~90 lines of hand-written
    /// boilerplate in v0; reflective consumers still see the
    /// same surface (the generator stamps
    /// <c>[WacsCompositeBundle("wasi-gfx", Priority = 10)]</c>
    /// on the generated half).</para>
    /// </summary>
    [WacsCompositeBundleGen(
        typeof(WasiPreview2Bundle), typeof(WasiGfxBundle),
        Family = "wasi-gfx", Priority = 10,
        BaseAccessor = "Preview2", SiblingAccessor = "Gfx")]
    public sealed partial class WasiPreview2GfxBundle
    {
    }
}
