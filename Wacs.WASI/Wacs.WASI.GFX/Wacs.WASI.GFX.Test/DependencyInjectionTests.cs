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
            // The composite's forwarding properties come from
            // CompositeBundleGenerator. The Backend property
            // forwards from WasiGfxBundle.Backend (no `Gfx`
            // prefix); the direct .Gfx accessor still exposes
            // the underlying bundle for callers that want to
            // disambiguate.
            Assert.IsType<StubBackend>(composite.Backend);
            Assert.IsType<StubBackend>(composite.Gfx.Backend);
        }

        // ---- scoped backend factory ----------

        [Fact]
        public void Context_BundleCtor_PullsBackendFromConfiguration()
        {
            // Two separate runtimes in one AppDomain — each gets
            // its own bundle pointing at a distinct backend. The
            // v0 WasiGfxAmbient process-global static would have
            // made this impossible; the bundle ctor threads the
            // backend through so each impl sees only its owner's
            // backend (with WasiGfxCurrent's AsyncLocal scope
            // providing the same isolation for the [static]
            // factory case).
            var cfg1 = WasiGfxConfiguration.DefaultConfiguration();
            var stub1 = new StubBackend();
            cfg1.Backend = stub1;
            var bundle1 = new WasiGfxBundle(cfg1);

            var cfg2 = WasiGfxConfiguration.DefaultConfiguration();
            var stub2 = new StubBackend();
            cfg2.Backend = stub2;
            var bundle2 = new WasiGfxBundle(cfg2);

            // Construct two Context impls bound to different
            // bundles; Create() routes through the configured
            // backend, not the ambient.
            using var ctx1 = new Context(bundle1);
            using var ctx2 = new Context(bundle2);
            ctx1.Create();
            ctx2.Create();

            Assert.Equal(1, stub1.ContextsCreated);
            Assert.Equal(1, stub2.ContextsCreated);
        }

        [Fact]
        public void Context_BundleCtor_NullArgThrows()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new Context(null!));
        }
    }
}
