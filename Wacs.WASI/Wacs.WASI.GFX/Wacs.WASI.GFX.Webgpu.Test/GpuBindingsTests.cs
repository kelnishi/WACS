// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.GFX.Types;
using Wacs.WASI.GFX.Webgpu;
using Xunit;

namespace Wacs.WASI.GFX.Webgpu.Test
{
    /// <summary>
    /// Session 3 acceptance tests for the wasi:webgpu host
    /// scaffolding. Covers configuration / BindToRuntime
    /// lifecycle / disposal / singleton semantics. Per-binding
    /// invocation tests land alongside the transpiler fixture
    /// tests in a later session — we'd need either a wasm
    /// component that imports the bindings or the runtime's
    /// internal entity-bindings table to assert the dispatch
    /// shape directly, both deferred to the parity-fixture
    /// session.
    /// </summary>
    public class GpuBindingsTests
    {
        [Fact]
        public void DefaultConfiguration_HasNoBackend()
        {
            var cfg = WasiWebgpuConfiguration.DefaultConfiguration();
            Assert.Null(cfg.Backend);
            Assert.Null(cfg.SharedResources);
        }

        [Fact]
        public void BindToRuntime_WithoutBackend_Throws()
        {
            var runtime = new WasmRuntime();
            using var host = new WasiWebgpuHost();
            var ex = Assert.Throws<WasiGfxException>(() =>
                host.BindToRuntime(runtime));
            Assert.Contains("no backend configured", ex.Message);
        }

        [Fact]
        public void BindToRuntime_WithBackend_Succeeds()
        {
            var runtime = new WasmRuntime();
            var stub = new StubGpuBackend();
            using var host = runtime.UseWasiWebgpu(b => b.WithBackend(stub));
            Assert.Same(stub, host.Backend);
        }

        [Fact]
        public void UseWasiWebgpu_NoBuilderArgs_ThrowsAtBind()
        {
            // Empty configure callback → no backend set →
            // BindToRuntime throws inside UseWasiWebgpu.
            var runtime = new WasmRuntime();
            Assert.Throws<WasiGfxException>(() =>
                runtime.UseWasiWebgpu());
        }

        [Fact]
        public void Builder_WithBackend_NullThrows()
        {
            var b = new WasiWebgpuConfigurationBuilder();
            Assert.Throws<ArgumentNullException>(() =>
                b.WithBackend(null!));
        }

        [Fact]
        public void GetOrCreateGpu_AllocatesSingleton()
        {
            var runtime = new WasmRuntime();
            var stub = new StubGpuBackend();
            using var host = runtime.UseWasiWebgpu(b => b.WithBackend(stub));

            // First call allocates; subsequent calls return the
            // same instance. The wasi:webgpu spec's `gpu` resource
            // is process-global; the wasm-side gets a fresh handle
            // for each get-gpu invocation but the underlying CLR
            // singleton stays put.
            var gpu1 = host.GetOrCreateGpu();
            var gpu2 = host.GetOrCreateGpu();
            Assert.NotNull(gpu1);
            Assert.Same(gpu1, gpu2);
            Assert.Equal(1, stub.CreateGpuCalls);
        }

        [Fact]
        public void GetOrCreateGpu_WithoutBackend_Throws()
        {
            using var host = new WasiWebgpuHost();
            var ex = Assert.Throws<WasiGfxException>(() =>
                host.GetOrCreateGpu());
            Assert.Contains("no IGpuBackend configured", ex.Message);
        }

        [Fact]
        public void Host_DisposeDisposesBackend()
        {
            var runtime = new WasmRuntime();
            var stub = new StubGpuBackend();
            var host = runtime.UseWasiWebgpu(b => b.WithBackend(stub));
            Assert.False(stub.Disposed);
            host.Dispose();
            Assert.True(stub.Disposed);
        }

        [Fact]
        public void Host_ConstructsCleanly_WithoutBackend()
        {
            // Constructing without a backend is allowed — the
            // failure surfaces only on BindToRuntime / get-gpu.
            // Verifies the scaffolding doesn't eagerly touch the
            // backend in the ctor.
            using var host = new WasiWebgpuHost();
            Assert.Null(host.Backend);
        }
    }
}
