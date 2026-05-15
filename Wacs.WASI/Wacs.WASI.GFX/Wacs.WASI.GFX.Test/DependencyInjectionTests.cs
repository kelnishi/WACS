// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Microsoft.Extensions.DependencyInjection;
using Wacs.WASI.GFX;
using Wacs.WASI.GFX.DependencyInjection;
using Wacs.WASI.Preview2.DependencyInjection;
using Xunit;

namespace Wacs.WASI.GFX.Test
{
    public class DependencyInjectionTests
    {
        [Fact]
        public void AddWasiGfx_RegistersBundle()
        {
            var services = new ServiceCollection();
            services.AddWasiGfx(b => b.WithBackend(new StubBackend()));
            var sp = services.BuildServiceProvider();
            var bundle = sp.GetRequiredService<WasiGfxBundle>();
            Assert.NotNull(bundle.Backend);
            Assert.IsType<StubBackend>(bundle.Backend);
        }

        [Fact]
        public void AddWasiGfx_NoBackend_RegistersEmptyBundle()
        {
            var services = new ServiceCollection();
            services.AddWasiGfx();
            var sp = services.BuildServiceProvider();
            var bundle = sp.GetRequiredService<WasiGfxBundle>();
            Assert.Null(bundle.Backend);
        }

        [Fact]
        public void AddWasiPreview2GfxBundle_RegistersComposite()
        {
            var services = new ServiceCollection();
            services
                .AddWasiPreview2()
                .AddWasiGfx(b => b.WithBackend(new StubBackend()))
                .AddWasiPreview2GfxBundle();
            var sp = services.BuildServiceProvider();
            var composite = sp.GetRequiredService<WasiPreview2GfxBundle>();
            Assert.NotNull(composite);
            Assert.NotNull(composite.Preview2);
            Assert.NotNull(composite.Gfx);
            // v1 phase 1 1h: the composite's forwarding
            // properties come from CompositeBundleGenerator now.
            // The Backend property forwards from
            // WasiGfxBundle.Backend (no `Gfx` prefix); the
            // direct .Gfx accessor still exposes the underlying
            // bundle for callers that want to disambiguate.
            Assert.IsType<StubBackend>(composite.Backend);
            Assert.IsType<StubBackend>(composite.Gfx.Backend);
        }
    }
}
