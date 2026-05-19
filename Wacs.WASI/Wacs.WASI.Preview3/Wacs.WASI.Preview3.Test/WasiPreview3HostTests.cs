// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview3.Cli;
using Wacs.WASI.Preview3.DependencyInjection;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Shape-level coverage for the v0
    /// <see cref="WasiPreview3Host"/> surface. The end-to-end
    /// wit-component-bound test awaits fixture availability per
    /// the Phase 3 closeout; this layer pins the configurable
    /// composite + DI integration.
    /// </summary>
    public class WasiPreview3HostTests
    {
        [Fact]
        public void Host_default_construct_provides_console_backed_stdio()
        {
            var host = new WasiPreview3Host();
            Assert.NotNull(host.Stdin);
            Assert.NotNull(host.Stdout);
            Assert.NotNull(host.Stderr);
        }

        [Fact]
        public void Host_custom_builder_threads_overrides()
        {
            using var mem = new MemoryStream();
            var custom = new StreamBackedSink(mem);
            var host = new WasiPreview3Host(new WasiPreview3HostBuilder
            {
                Stdout = custom,
            });
            Assert.Same(custom, host.Stdout);
        }

        [Fact]
        public void BindToRuntime_is_currently_noop_pending_fixture()
        {
            // Per the class doc comment: wire-level binding is
            // Slice J. Today's BindToRuntime is intentionally
            // empty — just verifies no exceptions.
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime); // doesn't throw
        }

        [Fact]
        public void UseWasiPreview3_returns_configured_host()
        {
            var runtime = new WasmRuntime();
            var captured = false;
            var host = runtime.UseWasiPreview3(b => { captured = true; });
            Assert.True(captured);
            Assert.NotNull(host);
        }

        // Note: an end-to-end dispatcher-buffer → sink test
        // lives in StreamBridgeTests (the lower layer
        // StreamBackedSink delegates to). StreamBackedSink
        // itself uses Task.Delay polling under parallel xunit
        // execution which makes a direct integration test
        // timing-fragile — folded into StreamBridge tests
        // for stability. The wire-level integration (via the
        // canon-async binder once a real .component.wasm
        // fixture exists) is Slice J.

        // ---- DI wiring ------------------------------------------------

        [Fact]
        public void AddWacsWasiPreview3_registers_stdio_singletons()
        {
            var services = new ServiceCollection();
            services.AddWacsWasiPreview3();
            using var provider = services.BuildServiceProvider();

            var host = provider.GetRequiredService<WasiPreview3Host>();
            var stdout = provider.GetRequiredService<IStdout>();
            Assert.Same(host.Stdout, stdout);
        }

        [Fact]
        public void AddWacsWasiPreview3_honors_builder_override()
        {
            using var mem = new MemoryStream();
            var custom = new StreamBackedSink(mem);

            var services = new ServiceCollection();
            services.AddWacsWasiPreview3(b => { b.Stdout = custom; });
            using var provider = services.BuildServiceProvider();

            var stdout = provider.GetRequiredService<IStdout>();
            Assert.Same(custom, stdout);
        }
    }
}
