// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Microsoft.Extensions.DependencyInjection;
using Wacs.WASI.Preview2.DependencyInjection;

[assembly: WasiScopeBootstrap(typeof(
    Wacs.WASI.GFX.DependencyInjection.GfxScopeBootstrap))]

namespace Wacs.WASI.GFX.DependencyInjection
{
    /// <summary>
    /// Self-registration hook for the wasi-gfx DI surface — found
    /// by <see cref="WasiPreview2RuntimeScope"/> via the assembly-
    /// level <c>[WasiScopeBootstrap]</c> attribute. Replaces the
    /// earlier hardcoded <c>ReflectivelyAddWasiGfx</c> path in
    /// Preview2.DI.
    ///
    /// <para>The backend (SilkGfxBackend) is wired into
    /// WasiGfxCurrent (AsyncLocal-scoped) by
    /// WasiGfxSilkBindable.BindToRuntime, not via a DI configure
    /// callback — so the gfx Configuration can stay at its
    /// DefaultConfiguration() defaults here. The DI registration's
    /// only job is to make the composite WasiPreview2GfxBundle
    /// resolvable; the bundle's GfxBackend forwarder picks up the
    /// current handle at first use.</para>
    /// </summary>
    public sealed class GfxScopeBootstrap : IWasiScopeBootstrap
    {
        public void Apply(IServiceCollection services)
        {
            services.AddWasiGfx(configure: null);
            services.AddWasiPreview2GfxBundle();
        }
    }
}
