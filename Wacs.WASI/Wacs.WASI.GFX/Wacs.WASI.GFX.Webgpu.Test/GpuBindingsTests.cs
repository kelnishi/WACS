// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
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

        // ---- Session 4: gpu-adapter + gpu-device stubs --------
        //
        // Wire-form binding invocation lands once parity fixtures
        // arrive (session 10) — there's no public host-function-
        // lookup API on WasmRuntime today. Session 4's coverage
        // is the SPI side: stub backend → IGpu → adapter/device
        // chain works correctly so the WitBindings dispatch on
        // top has a working target.

        [Fact]
        public void StubGpu_AlwaysReturnAdapter_YieldsAdapter()
        {
            // The session-4 stub gpu exposes a flag to toggle
            // request-adapter's behavior. None mode is the
            // session-3 baseline; Some mode is what later wire-
            // form tests use.
            var stub = new StubGpuBackend();
            var gpu = stub.CreateGpu();
            var stubGpu = (StubGpu)gpu;
            stubGpu.AlwaysReturnAdapter = true;

            var result = gpu.RequestAdapter(
                Option<Wacs.WASI.GFX.Webgpu.Webgpu.GpuRequestAdapterOptions>.None);
            Assert.True(result.HasValue);
            Assert.IsType<StubGpuAdapter>(result.Value);
        }

        [Fact]
        public void StubGpu_RequestAdapterDefault_ReturnsNone()
        {
            var stub = new StubGpuBackend();
            var gpu = stub.CreateGpu();
            var result = gpu.RequestAdapter(
                Option<Wacs.WASI.GFX.Webgpu.Webgpu.GpuRequestAdapterOptions>.None);
            Assert.False(result.HasValue);
        }

        [Fact]
        public void StubGpuAdapter_IsFallbackAdapter_False()
        {
            var ad = new StubGpuAdapter();
            Assert.False(ad.IsFallbackAdapter());
            // Features / Limits / Info are all handle returns;
            // ensure they at least don't throw.
            Assert.NotNull(ad.Features());
            Assert.NotNull(ad.Limits());
            Assert.NotNull(ad.Info());
        }

        [Fact]
        public void StubGpuDevice_LabelRoundtrip()
        {
            var dev = new StubGpuDevice();
            Assert.Equal("stub-device", dev.Label());
            dev.SetLabel("renamed");
            Assert.Equal("renamed", dev.Label());
        }

        [Fact]
        public void StubGpuAdapter_RequestDevice_ReturnsOk()
        {
            var ad = new StubGpuAdapter();
            var result = ad.RequestDevice(
                Option<Wacs.WASI.GFX.Webgpu.Webgpu.GpuDeviceDescriptor>.None);
            Assert.True(result.IsOk);
            Assert.IsType<StubGpuDevice>(result.Ok);
        }

        // ---- Session 5: buffer/shader/pipeline-layout/bind-group ----

        [Fact]
        public void StubGpuBuffer_DefaultStateMatchesUnmapped()
        {
            var buf = new StubGpuBuffer();
            Assert.Equal(0ul, buf.Size());
            Assert.Equal(0u, buf.Usage());
            Assert.Equal(Wacs.WASI.GFX.Webgpu.Webgpu.GpuBufferMapState.Unmapped,
                buf.MapState());
            Assert.False(buf.Destroyed);
            buf.Destroy();
            Assert.True(buf.Destroyed);
        }

        [Fact]
        public void StubGpuBuffer_LabelRoundtrip()
        {
            var buf = new StubGpuBuffer();
            Assert.Equal("stub-buffer", buf.Label());
            buf.SetLabel("vertex-buffer");
            Assert.Equal("vertex-buffer", buf.Label());
        }

        [Fact]
        public void LabeledResources_DefaultsAndRoundtrip()
        {
            // The four label-only resources share the same
            // StubGpuFoo<T>.Label/SetLabel shape; verify each
            // initializes to a sensible default and roundtrips.
            var sm = new StubGpuShaderModule();
            Assert.Equal("stub-shader", sm.Label());
            sm.SetLabel("vert"); Assert.Equal("vert", sm.Label());

            var pl = new StubGpuPipelineLayout();
            Assert.Equal("stub-pipeline-layout", pl.Label());
            pl.SetLabel("pl"); Assert.Equal("pl", pl.Label());

            var bgl = new StubGpuBindGroupLayout();
            Assert.Equal("stub-bind-group-layout", bgl.Label());
            bgl.SetLabel("bgl"); Assert.Equal("bgl", bgl.Label());

            var bg = new StubGpuBindGroup();
            Assert.Equal("stub-bind-group", bg.Label());
            bg.SetLabel("bg"); Assert.Equal("bg", bg.Label());
        }
    }
}
