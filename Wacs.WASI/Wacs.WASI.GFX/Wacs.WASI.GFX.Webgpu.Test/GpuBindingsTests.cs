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

        // ---- Session 6: compute path stubs ------------------

        [Fact]
        public void StubComputePipeline_GetBindGroupLayout_Returns()
        {
            var p = new StubGpuComputePipeline();
            Assert.NotNull(p.GetBindGroupLayout(0));
            Assert.NotNull(p.GetBindGroupLayout(7));
        }

        [Fact]
        public void StubCommandEncoder_BeginComputePass_ReturnsPass()
        {
            var enc = new StubGpuCommandEncoder();
            var pass = enc.BeginComputePass(
                Option<Wacs.WASI.GFX.Webgpu.Webgpu.GpuComputePassDescriptor>.None);
            Assert.NotNull(pass);
            Assert.IsType<StubGpuComputePassEncoder>(pass);
        }

        [Fact]
        public void StubCommandEncoder_Finish_ReturnsCommandBuffer()
        {
            var enc = new StubGpuCommandEncoder();
            var cb = enc.Finish(
                Option<Wacs.WASI.GFX.Webgpu.Webgpu.GpuCommandBufferDescriptor>.None);
            Assert.NotNull(cb);
            Assert.IsType<StubGpuCommandBuffer>(cb);
        }

        [Fact]
        public void StubComputePass_RecordsLifecycle()
        {
            var pass = new StubGpuComputePassEncoder();
            var pipe = new StubGpuComputePipeline();
            pass.SetPipeline(pipe);
            Assert.Same(pipe, pass.LastPipeline);

            pass.DispatchWorkgroups(64,
                Option<uint>.Some(8), Option<uint>.None);
            Assert.NotNull(pass.LastDispatch);
            Assert.Equal(64u, pass.LastDispatch!.Value.x);
            Assert.True(pass.LastDispatch.Value.y.HasValue);
            Assert.Equal(8u, pass.LastDispatch.Value.y.Value);
            Assert.False(pass.LastDispatch.Value.z.HasValue);

            Assert.False(pass.Ended);
            pass.End();
            Assert.True(pass.Ended);
        }

        [Fact]
        public void StubQueue_SubmitCapturesBuffers()
        {
            var q = new StubGpuQueue();
            var cb1 = new StubGpuCommandBuffer();
            var cb2 = new StubGpuCommandBuffer();
            q.Submit(new Wacs.WASI.GFX.Webgpu.Webgpu.IGpuCommandBuffer[] { cb1, cb2 });
            Assert.Single(q.Submissions);
            Assert.Equal(2, q.Submissions[0].Length);

            Assert.Equal(0, q.OnSubmittedCalls);
            q.OnSubmittedWorkDone();
            q.OnSubmittedWorkDone();
            Assert.Equal(2, q.OnSubmittedCalls);
        }

        // ---- Session 7: render-path stubs -------------------

        [Fact]
        public void StubGpuTexture_DefaultQueries()
        {
            var t = new StubGpuTexture();
            Assert.Equal(0u, t.Width());
            Assert.Equal(0u, t.Height());
            Assert.Equal(1u, t.DepthOrArrayLayers());
            Assert.Equal(1u, t.MipLevelCount());
            Assert.Equal(1u, t.SampleCount());
            Assert.Equal(0u, t.Usage());
            Assert.Equal(
                Wacs.WASI.GFX.Webgpu.Webgpu.GpuTextureDimension.D2,
                t.Dimension());
            Assert.Equal(
                Wacs.WASI.GFX.Webgpu.Webgpu.GpuTextureFormat.Rgba8unorm,
                t.Format());
        }

        [Fact]
        public void StubGpuTexture_DestroyAndLabel()
        {
            var t = new StubGpuTexture();
            Assert.False(t.Destroyed);
            t.Destroy();
            Assert.True(t.Destroyed);

            Assert.Equal("stub-texture", t.Label());
            t.SetLabel("color-target");
            Assert.Equal("color-target", t.Label());
        }

        [Fact]
        public void StubGpuTexture_CreateView_ReturnsView()
        {
            var t = new StubGpuTexture();
            var v = t.CreateView(
                Option<Wacs.WASI.GFX.Webgpu.Webgpu.GpuTextureViewDescriptor>.None);
            Assert.NotNull(v);
            Assert.IsType<StubGpuTextureView>(v);
        }

        [Fact]
        public void RenderPathLabeled_DefaultsAndRoundtrip()
        {
            var v = new StubGpuTextureView();
            Assert.Equal("stub-texture-view", v.Label());
            v.SetLabel("view"); Assert.Equal("view", v.Label());

            var s = new StubGpuSampler();
            Assert.Equal("stub-sampler", s.Label());
            s.SetLabel("smp"); Assert.Equal("smp", s.Label());

            var rp = new StubGpuRenderPipeline();
            Assert.Equal("stub-render-pipeline", rp.Label());
            rp.SetLabel("rp"); Assert.Equal("rp", rp.Label());
            // get-bind-group-layout returns a fresh BGL.
            Assert.NotNull(rp.GetBindGroupLayout(0));

            var rb = new StubGpuRenderBundle();
            Assert.Equal("stub-render-bundle", rb.Label());
            rb.SetLabel("rb"); Assert.Equal("rb", rb.Label());
        }

        // ---- Session 9: graphics-context bridge ------------

        [Fact]
        public void Backend_FromAbstractBuffer_RejectsCpuKind()
        {
            // The bridge MUST reject ICpuAbstractBuffer inputs
            // — that lift belongs on the frame-buffer device,
            // not on gpu-texture.from-graphics-buffer.
            var stub = new StubGpuBackend();
            var cpuBuf = new TestCpuAbstractBuffer();
            var ex = Assert.Throws<Wacs.WASI.GFX.Types.WasiGfxException>(
                () => stub.FromAbstractBuffer(cpuBuf));
            Assert.Contains("IGpuAbstractBuffer", ex.Message);
        }

        [Fact]
        public void Backend_FromAbstractBuffer_AcceptsGpuKind()
        {
            var stub = new StubGpuBackend();
            var gpuBuf = new StubGpuAbstractBuffer();
            var tex = stub.FromAbstractBuffer(gpuBuf);
            Assert.NotNull(tex);
            Assert.Equal(1, stub.FromAbstractBufferCalls);
        }

        [Fact]
        public void Builder_WithAbstractBufferResolver_Plumbs()
        {
            // Wire a custom resolver through the fluent
            // builder and verify it lands on the host's
            // configuration (visible through the internal
            // accessor).
            var runtime = new WasmRuntime();
            var stub = new StubGpuBackend();
            int captured = -1;
            using var host = runtime.UseWasiWebgpu(b => b
                .WithBackend(stub)
                .WithAbstractBufferResolver(h =>
                {
                    captured = h;
                    return new StubGpuAbstractBuffer();
                }));
            Assert.NotNull(host.AbstractBufferResolver);
            var ab = host.AbstractBufferResolver!(42);
            Assert.Equal(42, captured);
            Assert.IsType<StubGpuAbstractBuffer>(ab);
        }

        // ---- Render-pass + bundle-encoder lifecycle ---------

        [Fact]
        public void StubRenderPassEncoder_CapturesStateSetters()
        {
            var pass = new StubGpuRenderPassEncoder();
            pass.SetViewport(0f, 0f, 800f, 600f, 0f, 1f);
            Assert.NotNull(pass.LastViewport);
            Assert.Equal(800f, pass.LastViewport!.Value.w);
            Assert.Equal(1f, pass.LastViewport.Value.maxD);

            pass.SetScissorRect(10u, 20u, 200u, 100u);
            Assert.Equal((10u, 20u, 200u, 100u), pass.LastScissor);

            pass.SetStencilReference(7u);
            Assert.Equal(7u, pass.LastStencil);

            pass.SetBlendConstant(new Wacs.WASI.GFX.Webgpu.Webgpu.GpuColor
            {
                R = 0.5, G = 0.25, B = 0.125, A = 1.0,
            });
            Assert.NotNull(pass.LastBlend);
            Assert.Equal(0.5, pass.LastBlend!.R);
        }

        [Fact]
        public void StubRenderPassEncoder_DrawAndDrawIndexed()
        {
            var pass = new StubGpuRenderPassEncoder();
            pass.Draw(3u,
                Option<uint>.Some(2u),
                Option<uint>.None,
                Option<uint>.None);
            Assert.NotNull(pass.LastDraw);
            Assert.Equal(3u, pass.LastDraw!.Value.vc);
            Assert.True(pass.LastDraw.Value.ic.HasValue);
            Assert.Equal(2u, pass.LastDraw.Value.ic.Value);

            pass.DrawIndexed(6u,
                Option<uint>.None,
                Option<uint>.None,
                Option<int>.Some(-4),
                Option<uint>.Some(1u));
            Assert.NotNull(pass.LastDrawIndexed);
            Assert.True(pass.LastDrawIndexed!.Value.baseV.HasValue);
            Assert.Equal(-4, pass.LastDrawIndexed.Value.baseV.Value);
        }

        [Fact]
        public void StubRenderPassEncoder_OcclusionAndExecuteBundles()
        {
            var pass = new StubGpuRenderPassEncoder();
            pass.BeginOcclusionQuery(5u);
            Assert.Equal(5u, pass.LastOcclusionQuery);
            Assert.False(pass.OcclusionEnded);
            pass.EndOcclusionQuery();
            Assert.True(pass.OcclusionEnded);

            var b1 = new StubGpuRenderBundle();
            pass.ExecuteBundles(
                new Wacs.WASI.GFX.Webgpu.Webgpu.IGpuRenderBundle[] { b1 });
            Assert.NotNull(pass.LastBundles);
            Assert.Single(pass.LastBundles!);

            Assert.False(pass.Ended);
            pass.End();
            Assert.True(pass.Ended);
        }

        [Fact]
        public void StubRenderBundleEncoder_FinishReturnsBundle()
        {
            var enc = new StubGpuRenderBundleEncoder();
            var bundle = enc.Finish(
                Option<Wacs.WASI.GFX.Webgpu.Webgpu.GpuRenderBundleDescriptor>.None);
            Assert.NotNull(bundle);
            Assert.IsType<StubGpuRenderBundle>(bundle);
        }

        [Fact]
        public void Session6_ComputePath_EndToEnd_ChainCompiles()
        {
            // Sanity: the whole compute path stub chain can be
            // constructed and walked without throwing. This is
            // the shape hello_compute will exercise once the
            // Silk backend lands in session 8.
            var stub = new StubGpuBackend();
            var dev = new StubGpuDevice();
            var enc = new StubGpuCommandEncoder();
            var pass = enc.BeginComputePass(
                Option<Wacs.WASI.GFX.Webgpu.Webgpu.GpuComputePassDescriptor>.None);
            pass.SetPipeline(new StubGpuComputePipeline());
            pass.DispatchWorkgroups(1u,
                Option<uint>.None, Option<uint>.None);
            pass.End();
            var cb = enc.Finish(
                Option<Wacs.WASI.GFX.Webgpu.Webgpu.GpuCommandBufferDescriptor>.None);
            dev.Queue().Submit(
                new Wacs.WASI.GFX.Webgpu.Webgpu.IGpuCommandBuffer[] { cb });
            // No assertions on intermediate values — this is a
            // shape-compiles smoke test.
        }
    }
}
