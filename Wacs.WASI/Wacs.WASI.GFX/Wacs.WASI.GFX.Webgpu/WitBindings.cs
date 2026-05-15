// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.Core.Runtime;
using Wacs.WASI.GFX.HostBinding;
using Wacs.WASI.GFX.Types;
using IGpu = Wacs.WASI.GFX.Webgpu.Webgpu.IGpu;
using IGpuAdapter = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuAdapter;
using IGpuBuffer = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuBuffer;
using IGpuShaderModule = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuShaderModule;
using IGpuPipelineLayout = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuPipelineLayout;
using IGpuBindGroupLayout = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuBindGroupLayout;
using IGpuBindGroup = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuBindGroup;
using IGpuDevice = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuDevice;
using IGpuComputePipeline = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuComputePipeline;
using IGpuCommandEncoder = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuCommandEncoder;
using IGpuComputePassEncoder = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuComputePassEncoder;
using IGpuCommandBuffer = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuCommandBuffer;
using IGpuQueue = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuQueue;
using IGpuTexture = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuTexture;
using IGpuTextureView = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuTextureView;
using IGpuSampler = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuSampler;
using IGpuRenderPipeline = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuRenderPipeline;
using IGpuRenderBundle = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuRenderBundle;
using IWgslLanguageFeatures = Wacs.WASI.GFX.Webgpu.Webgpu.IWgslLanguageFeatures;
using GpuRequestAdapterOptions = Wacs.WASI.GFX.Webgpu.Webgpu.GpuRequestAdapterOptions;
using GpuDeviceDescriptor = Wacs.WASI.GFX.Webgpu.Webgpu.GpuDeviceDescriptor;
using GpuComputePassDescriptor = Wacs.WASI.GFX.Webgpu.Webgpu.GpuComputePassDescriptor;
using GpuCommandBufferDescriptor = Wacs.WASI.GFX.Webgpu.Webgpu.GpuCommandBufferDescriptor;

namespace Wacs.WASI.GFX.Webgpu
{
    /// <summary>
    /// Hand-written canonical-ABI host-function dispatcher for the
    /// <c>wasi:webgpu@0.0.1</c> imports. Same shape as
    /// <c>WACS.WASI.GFX.WitBindings</c> — one
    /// <c>runtime.BindHostFunction</c> call per WIT method,
    /// keyed on the wire-form <c>(module, entity)</c> pair.
    ///
    /// <para>v1 phase 3 session 3 wires the entry-point
    /// <c>get-gpu</c> free function plus the three
    /// <c>[method]gpu.*</c> imports and <c>[resource-drop]gpu</c>.
    /// Resource methods on gpu-adapter / gpu-device / etc. land in
    /// later sessions following the dependency order in the
    /// roadmap.</para>
    ///
    /// <para><b>Singleton semantics:</b> the wasi:webgpu spec's
    /// <c>gpu</c> resource is process-global. Every guest call to
    /// <c>get-gpu()</c> allocates a fresh wasm-side handle from
    /// <see cref="WasiWebgpuHost.Gpus"/>, but all handles point at
    /// the same <see cref="IGpu"/> instance the backend's
    /// <see cref="IGpuBackend.CreateGpu"/> minted. Dropping a
    /// handle releases the slot but never disposes the singleton
    /// — backend ownership controls that.</para>
    /// </summary>
    internal static class WitBindings
    {
        // wasi:webgpu pins @0.0.1; the wire-form module string is
        // constant. The cross-package wasi:io@0.2.x refs (only
        // visible at JS-flavored / pollable-returning methods,
        // which land in later sessions) ride the IoBindings
        // multi-version registration shipped in v1 phase 1i.
        internal const string Ns = "wasi:webgpu/webgpu@0.0.1";

        public static void Bind(WasmRuntime runtime, WasiWebgpuHost host)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (host.Backend == null)
                throw new WasiGfxException(
                    "wasi-webgpu WitBindings.Bind invoked without "
                    + "a backend on the host's configuration.");
            var alloc = new Wacs.WASI.GFX.HostBinding.Realloc(runtime);
            BindGpu(runtime, host);
            BindGpuAdapter(runtime, host);
            BindGpuDevice(runtime, host, alloc);
            BindGpuBuffer(runtime, host, alloc);
            BindLabeled(runtime, alloc,
                "gpu-shader-module", host.ShaderModules,
                h => ((IGpuShaderModule)h).Label(),
                (h, s) => ((IGpuShaderModule)h).SetLabel(s));
            BindLabeled(runtime, alloc,
                "gpu-pipeline-layout", host.PipelineLayouts,
                h => ((IGpuPipelineLayout)h).Label(),
                (h, s) => ((IGpuPipelineLayout)h).SetLabel(s));
            BindLabeled(runtime, alloc,
                "gpu-bind-group-layout", host.BindGroupLayouts,
                h => ((IGpuBindGroupLayout)h).Label(),
                (h, s) => ((IGpuBindGroupLayout)h).SetLabel(s));
            BindLabeled(runtime, alloc,
                "gpu-bind-group", host.BindGroups,
                h => ((IGpuBindGroup)h).Label(),
                (h, s) => ((IGpuBindGroup)h).SetLabel(s));
            BindGpuComputePipeline(runtime, host, alloc);
            BindGpuCommandEncoder(runtime, host, alloc);
            BindGpuComputePassEncoder(runtime, host, alloc);
            BindLabeled(runtime, alloc,
                "gpu-command-buffer", host.CommandBuffers,
                h => ((IGpuCommandBuffer)h).Label(),
                (h, s) => ((IGpuCommandBuffer)h).SetLabel(s));
            BindGpuQueue(runtime, host, alloc);
            BindGpuTexture(runtime, host, alloc);
            BindLabeled(runtime, alloc,
                "gpu-texture-view", host.TextureViews,
                h => ((IGpuTextureView)h).Label(),
                (h, s) => ((IGpuTextureView)h).SetLabel(s));
            BindLabeled(runtime, alloc,
                "gpu-sampler", host.Samplers,
                h => ((IGpuSampler)h).Label(),
                (h, s) => ((IGpuSampler)h).SetLabel(s));
            BindGpuRenderPipeline(runtime, host, alloc);
            BindLabeled(runtime, alloc,
                "gpu-render-bundle", host.RenderBundles,
                h => ((IGpuRenderBundle)h).Label(),
                (h, s) => ((IGpuRenderBundle)h).SetLabel(s));

            // v1 phase 3 session 9: graphics-context bridge.
            // [static]gpu-texture.from-graphics-buffer(buffer:
            //   abstract-buffer) -> own<gpu-texture>
            // Wire: (i32 abHandle) -> i32 texHandle. The wasm-
            // side handle lives in the wasi-gfx host's
            // AbstractBuffers table; the configured resolver
            // (set by WasiGfxSilkBindable) maps it. Without the
            // resolver this throws with a clear bridge-not-
            // wired message.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[static]gpu-texture.from-graphics-buffer"),
                (_, abHandle) =>
                {
                    if (host.AbstractBufferResolver == null)
                        throw new WasiGfxException(
                            "[static]gpu-texture.from-graphics-buffer "
                            + "called but no AbstractBufferResolver "
                            + "is configured on this WasiWebgpuHost. "
                            + "The graphics-context bridge requires a "
                            + "wasi-gfx sibling host; pair --wasi-gfx "
                            + "+ --wasi-webgpu in the CLI, or wire "
                            + "WasiWebgpuConfiguration"
                            + ".AbstractBufferResolver directly.");
                    var ab = host.AbstractBufferResolver(abHandle);
                    if (ab == null)
                        throw new WasiGfxException(
                            "[static]gpu-texture.from-graphics-buffer: "
                            + "abstract-buffer handle " + abHandle
                            + " is not registered in the wasi-gfx host.");
                    if (host.Backend == null)
                        throw new WasiGfxException(
                            "[static]gpu-texture.from-graphics-buffer "
                            + "called on a host with no IGpuBackend.");
                    var tex = host.Backend.FromAbstractBuffer(ab);
                    return host.Textures.Allocate(tex);
                });
        }

        // ----------------------------------------------------
        //   wasi:webgpu/webgpu@0.0.1 (top-level + gpu resource)
        //     get-gpu: func() -> gpu;
        //     resource gpu {
        //       request-adapter: func(options: option<gpu-request-adapter-options>) -> option<gpu-adapter>;
        //       get-preferred-canvas-format: func() -> gpu-texture-format;
        //       wgsl-language-features: func() -> wgsl-language-features;
        //     }
        // ----------------------------------------------------

        private static void BindGpu(WasmRuntime runtime, WasiWebgpuHost host)
        {
            // get-gpu() -> own<gpu>
            // No params; returns handle. Wire: Func<Ctx, i32>.
            // Singleton: the underlying IGpu is the same across
            // every get-gpu call but each call allocates a fresh
            // wasm-side handle so the resource-drop semantics
            // line up with WIT (own<gpu> handles are independent).
            runtime.BindHostFunction<Func<ExecContext, int>>(
                (Ns, "get-gpu"),
                _ =>
                {
                    var gpu = host.GetOrCreateGpu();
                    return host.Gpus.Allocate(gpu);
                });

            // [method]gpu.get-preferred-canvas-format(self) -> gpu-texture-format
            // gpu-texture-format is an enum — wire form is a single
            // i32. Wire: Func<Ctx, i32, i32>.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu.get-preferred-canvas-format"),
                (_, selfH) =>
                {
                    var gpu = (IGpu)host.Gpus.Get(selfH);
                    return (int)gpu.GetPreferredCanvasFormat();
                });

            // [method]gpu.wgsl-language-features(self) -> own<wgsl-language-features>
            // Returns a fresh resource handle each call. Wire:
            // Func<Ctx, i32, i32>.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu.wgsl-language-features"),
                (_, selfH) =>
                {
                    var gpu = (IGpu)host.Gpus.Get(selfH);
                    var features = gpu.WgslLanguageFeatures();
                    return host.WgslLanguageFeatures.Allocate(features);
                });

            // [resource-drop]gpu(self) — release the wasm-side
            // handle slot. The underlying IGpu singleton stays
            // alive; backend ownership manages its lifetime.
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]gpu"),
                (_, h) => host.Gpus.Drop(h));

            // [method]gpu.request-adapter(self, options: option<gpu-request-adapter-options>)
            //   -> option<gpu-adapter>
            //
            // option<gpu-request-adapter-options> flat-form (10 i32):
            //   opt_disc : i32
            //   record:
            //     option<string> feature-level         : 3 i32 (disc, ptr, len)
            //     option<enum> power-preference        : 2 i32 (disc, val)
            //     option<bool> force-fallback-adapter  : 2 i32 (disc, val)
            //     option<bool> xr-compatible           : 2 i32 (disc, val)
            //
            // Wire: 1 (self) + 10 (option<record>) + 1 (retArea) = 12 i32.
            // retArea layout: 8 bytes, align 4. disc@0 (u8 + 3 pad);
            // handle@4 (i32, only valid when disc=1).
            //
            // v1 phase 3 session 4 ships full decoding of the option<record>
            // into a CLR Option<GpuRequestAdapterOptions> via the canonical
            // ABI's flat-form rules. Strings come through via ExecContext
            // memory reads since option<string>'s disc=1 case carries a
            // (ptr, len) pair that points into linear memory.
            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, int, int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu.request-adapter"),
                (ctx, selfH, optDisc,
                    flDisc, flPtr, flLen,
                    ppDisc, ppVal,
                    ffDisc, ffVal,
                    xrDisc, xrVal,
                    retArea) =>
                {
                    var gpu = (IGpu)host.Gpus.Get(selfH);
                    Option<GpuRequestAdapterOptions> opts;
                    if (optDisc == 0)
                    {
                        opts = Option<GpuRequestAdapterOptions>.None;
                    }
                    else
                    {
                        var rec = new GpuRequestAdapterOptions
                        {
                            FeatureLevel = flDisc == 0
                                ? Option<string>.None
                                : Option<string>.Some(
                                    ReadUtf8(ctx, flPtr, flLen)),
                            PowerPreference = ppDisc == 0
                                ? Option<Webgpu.GpuPowerPreference>.None
                                : Option<Webgpu.GpuPowerPreference>.Some(
                                    (Webgpu.GpuPowerPreference)ppVal),
                            ForceFallbackAdapter = ffDisc == 0
                                ? Option<bool>.None
                                : Option<bool>.Some(ffVal != 0),
                            XrCompatible = xrDisc == 0
                                ? Option<bool>.None
                                : Option<bool>.Some(xrVal != 0),
                        };
                        opts = Option<GpuRequestAdapterOptions>.Some(rec);
                    }

                    var result = gpu.RequestAdapter(opts);
                    WriteOptionHandle(ctx, retArea, result.HasValue,
                        result.HasValue
                            ? host.Adapters.Allocate(result.Value)
                            : 0);
                });
        }

        // ----------------------------------------------------
        //   wasi:webgpu/webgpu@0.0.1 resource gpu-adapter
        //     features: func() -> gpu-supported-features;
        //     limits: func() -> gpu-supported-limits;
        //     info: func() -> gpu-adapter-info;
        //     is-fallback-adapter: func() -> bool;
        //     request-device: func(...) -> result<gpu-device, request-device-error>;
        // ----------------------------------------------------

        private static void BindGpuAdapter(WasmRuntime runtime, WasiWebgpuHost host)
        {
            // features / limits / info — all (self) -> own<handle>.
            // Wire: Func<Ctx, i32, i32>.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-adapter.features"),
                (_, selfH) =>
                {
                    var ad = (IGpuAdapter)host.Adapters.Get(selfH);
                    return host.SupportedFeatures.Allocate(ad.Features());
                });
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-adapter.limits"),
                (_, selfH) =>
                {
                    var ad = (IGpuAdapter)host.Adapters.Get(selfH);
                    return host.SupportedLimits.Allocate(ad.Limits());
                });
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-adapter.info"),
                (_, selfH) =>
                {
                    var ad = (IGpuAdapter)host.Adapters.Get(selfH);
                    return host.AdapterInfos.Allocate(ad.Info());
                });

            // is-fallback-adapter(self) -> bool. Wire: i32 result.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-adapter.is-fallback-adapter"),
                (_, selfH) =>
                {
                    var ad = (IGpuAdapter)host.Adapters.Get(selfH);
                    return ad.IsFallbackAdapter() ? 1 : 0;
                });

            // request-device(self, descriptor: option<gpu-device-descriptor>)
            //   -> result<gpu-device, request-device-error>
            //
            // The option<descriptor> flat-form has nested lists, options,
            // and a resource handle (record-option-gpu-size64) — substantial
            // canonical-ABI plumbing. Sessions 5-6 wire it alongside the
            // descriptor's nested resources (buffer / shader / pipeline-
            // layout). For session 4 the binding is registered with a
            // throw-stub body so validation pre-flight sees it and the
            // intent is obvious to a reader who hits the throw.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]gpu-adapter.request-device"),
                (_, _self, _retArea) =>
                {
                    throw new NotImplementedException(
                        "wasi:webgpu [method]gpu-adapter.request-device — "
                        + "the option<gpu-device-descriptor> flat-form "
                        + "lands in v1 phase 3 session 5 alongside the "
                        + "descriptor's nested resources.");
                });

            // [resource-drop]gpu-adapter
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]gpu-adapter"),
                (_, h) => host.Adapters.Drop(h));
        }

        // ----------------------------------------------------
        //   wasi:webgpu/webgpu@0.0.1 resource gpu-device
        //     Session 4 covers the query/lifecycle methods:
        //       features / limits / adapter-info / queue / lost
        //       destroy / label / set-label / [resource-drop]
        //     Session 5+ adds the create-* methods.
        // ----------------------------------------------------

        private static void BindGpuDevice(WasmRuntime runtime,
            WasiWebgpuHost host, Wacs.WASI.GFX.HostBinding.Realloc alloc)
        {
            // features / limits / adapter-info — handle returns.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-device.features"),
                (_, selfH) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    return host.SupportedFeatures.Allocate(dev.Features());
                });
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-device.limits"),
                (_, selfH) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    return host.SupportedLimits.Allocate(dev.Limits());
                });
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-device.adapter-info"),
                (_, selfH) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    return host.AdapterInfos.Allocate(dev.AdapterInfo());
                });

            // queue(self) -> own<gpu-queue>
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-device.queue"),
                (_, selfH) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    return host.Queues.Allocate(dev.Queue());
                });

            // lost(self) -> own<gpu-device-lost-info>
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-device.lost"),
                (_, selfH) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    return host.DeviceLostInfos.Allocate(dev.Lost());
                });

            // destroy(self) — void; signals the device is dropped.
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[method]gpu-device.destroy"),
                (_, selfH) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    dev.Destroy();
                });

            // label(self) -> string. retArea is 8 bytes: ptr@0 + len@4.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]gpu-device.label"),
                (ctx, selfH, retArea) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    WriteUtf8Allocated(ctx, alloc, retArea,
                        dev.Label() ?? string.Empty);
                });

            // set-label(self, label: string). Wire: (self, ptr, len).
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (Ns, "[method]gpu-device.set-label"),
                (ctx, selfH, ptr, len) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    dev.SetLabel(ReadUtf8(ctx, ptr, len));
                });

            // [resource-drop]gpu-device
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]gpu-device"),
                (_, h) => host.Devices.Drop(h));

            // create-buffer / -texture / -sampler / -bind-group-* /
            // -pipeline-* / -shader-module / -command-encoder /
            // -render-bundle-encoder / -query-set + the two async
            // variants land in sessions 5-7. push-error-scope /
            // pop-error-scope / onuncapturederror-subscribe /
            // connect-graphics-context land alongside the error +
            // graphics-context bridge sessions.
        }

        // ----------------------------------------------------
        //   wasi:webgpu/webgpu@0.0.1 resource gpu-buffer
        //     Session 5 covers the lifecycle / query methods:
        //       size / usage / map-state / destroy / label /
        //       set-label / [resource-drop]. The mapping methods
        //       (map-async, unmap, get-mapped-range-*) all return
        //       result<_, error> and land in session 6 alongside
        //       similar result-return work on other resources.
        // ----------------------------------------------------

        private static void BindGpuBuffer(WasmRuntime runtime,
            WasiWebgpuHost host, Wacs.WASI.GFX.HostBinding.Realloc alloc)
        {
            // size(self) -> u64. Wire: Func<Ctx, i32, i64>.
            runtime.BindHostFunction<Func<ExecContext, int, long>>(
                (Ns, "[method]gpu-buffer.size"),
                (_, selfH) =>
                {
                    var buf = (IGpuBuffer)host.Buffers.Get(selfH);
                    return unchecked((long)buf.Size());
                });

            // usage(self) -> u32. Wire: Func<Ctx, i32, i32>.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-buffer.usage"),
                (_, selfH) =>
                {
                    var buf = (IGpuBuffer)host.Buffers.Get(selfH);
                    return unchecked((int)buf.Usage());
                });

            // map-state(self) -> gpu-buffer-map-state. Enum → i32.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-buffer.map-state"),
                (_, selfH) =>
                {
                    var buf = (IGpuBuffer)host.Buffers.Get(selfH);
                    return (int)buf.MapState();
                });

            // destroy(self) — void.
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[method]gpu-buffer.destroy"),
                (_, selfH) =>
                {
                    var buf = (IGpuBuffer)host.Buffers.Get(selfH);
                    buf.Destroy();
                });

            // label / set-label — same wire shape as gpu-device.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]gpu-buffer.label"),
                (ctx, selfH, retArea) =>
                {
                    var buf = (IGpuBuffer)host.Buffers.Get(selfH);
                    WriteUtf8Allocated(ctx, alloc, retArea,
                        buf.Label() ?? string.Empty);
                });
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (Ns, "[method]gpu-buffer.set-label"),
                (ctx, selfH, ptr, len) =>
                {
                    var buf = (IGpuBuffer)host.Buffers.Get(selfH);
                    buf.SetLabel(ReadUtf8(ctx, ptr, len));
                });

            // [resource-drop]gpu-buffer
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]gpu-buffer"),
                (_, h) => host.Buffers.Drop(h));
        }

        // ----------------------------------------------------
        //   wasi:webgpu/webgpu@0.0.1 resource gpu-compute-pipeline
        //     label / set-label / get-bind-group-layout + drop
        // ----------------------------------------------------

        private static void BindGpuComputePipeline(WasmRuntime runtime,
            WasiWebgpuHost host, Wacs.WASI.GFX.HostBinding.Realloc alloc)
        {
            BindLabeled(runtime, alloc,
                "gpu-compute-pipeline", host.ComputePipelines,
                h => ((IGpuComputePipeline)h).Label(),
                (h, s) => ((IGpuComputePipeline)h).SetLabel(s));

            // get-bind-group-layout(self, index: u32) -> own<gpu-bind-group-layout>
            runtime.BindHostFunction<Func<ExecContext, int, int, int>>(
                (Ns, "[method]gpu-compute-pipeline.get-bind-group-layout"),
                (_, selfH, index) =>
                {
                    var p = (IGpuComputePipeline)host.ComputePipelines.Get(selfH);
                    var bgl = p.GetBindGroupLayout(unchecked((uint)index));
                    return host.BindGroupLayouts.Allocate(bgl);
                });
        }

        // ----------------------------------------------------
        //   wasi:webgpu/webgpu@0.0.1 resource gpu-command-encoder
        //     Session 6 covers the compute-path subset:
        //       begin-compute-pass / finish / copy-buffer-to-buffer
        //       clear-buffer / label / set-label / debug ops / drop
        //     copy-buffer-to-texture / copy-texture-* /
        //     resolve-query-set + begin-render-pass land in
        //     session 7 (render-path).
        // ----------------------------------------------------

        private static void BindGpuCommandEncoder(WasmRuntime runtime,
            WasiWebgpuHost host, Wacs.WASI.GFX.HostBinding.Realloc alloc)
        {
            // begin-compute-pass(self, descriptor: option<gpu-compute-pass-descriptor>)
            //   -> own<gpu-compute-pass-encoder>
            //
            // option<gpu-compute-pass-descriptor> flat-form (10 i32):
            //   opt_disc:i32
            //   record fields:
            //     timestamp-writes: option<gpu-compute-pass-timestamp-writes>
            //       opt_disc:i32, query-set:i32, beg_disc:i32, beg_val:i32,
            //       end_disc:i32, end_val:i32     = 6 i32
            //     label: option<string>           = 3 i32 (disc, ptr, len)
            //   record total = 9 i32; option = 10 i32
            //
            // Wire: (self, 10 i32 for option<record>) -> i32 result.
            // The full descriptor decode (timestamp-writes resource +
            // optional label) is non-trivial; v1 session 6 receives
            // the params and passes Option<...>.None to the impl,
            // matching what hello_compute typically uses (no
            // descriptor at all). Real-backend decoding lands when a
            // guest needs timestamp queries.
            runtime.BindHostFunction<Func<ExecContext,
                int, int, int, int, int, int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-command-encoder.begin-compute-pass"),
                (_, selfH,
                    _od, _twDisc, _twQs, _twBegD, _twBegV, _twEndD, _twEndV,
                    _lDisc, _lPtr, _lLen) =>
                {
                    var enc = (IGpuCommandEncoder)host.CommandEncoders.Get(selfH);
                    var pass = enc.BeginComputePass(
                        Option<GpuComputePassDescriptor>.None);
                    return host.ComputePassEncoders.Allocate(pass);
                });

            // finish(self, descriptor: option<gpu-command-buffer-descriptor>)
            //   -> own<gpu-command-buffer>
            // option<gpu-command-buffer-descriptor> flat-form: opt_disc +
            // record{label: option<string>} = 1 + 3 = 4 i32.
            // Wire: self + 4 (opt-record) = 5 i32 params + 1 i32 result.
            runtime.BindHostFunction<Func<ExecContext, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-command-encoder.finish"),
                (_, selfH, _od, _lDisc, _lPtr, _lLen) =>
                {
                    var enc = (IGpuCommandEncoder)host.CommandEncoders.Get(selfH);
                    var cb = enc.Finish(
                        Option<GpuCommandBufferDescriptor>.None);
                    return host.CommandBuffers.Allocate(cb);
                });

            // copy-buffer-to-buffer(self, src: borrow<gpu-buffer>,
            //   src-offset: u64, dst: borrow<gpu-buffer>,
            //   dst-offset: u64, size: u64)
            runtime.BindHostFunction<Action<ExecContext,
                int, int, long, int, long, long>>(
                (Ns, "[method]gpu-command-encoder.copy-buffer-to-buffer"),
                (_, selfH, srcH, srcOff, dstH, dstOff, size) =>
                {
                    var enc = (IGpuCommandEncoder)host.CommandEncoders.Get(selfH);
                    var src = (IGpuBuffer)host.Buffers.Get(srcH);
                    var dst = (IGpuBuffer)host.Buffers.Get(dstH);
                    enc.CopyBufferToBuffer(
                        src, unchecked((ulong)srcOff),
                        dst, unchecked((ulong)dstOff),
                        unchecked((ulong)size));
                });

            // clear-buffer(self, buffer: borrow<gpu-buffer>,
            //   offset: option<u64>, size: option<u64>)
            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, long, int, long>>(
                (Ns, "[method]gpu-command-encoder.clear-buffer"),
                (_, selfH, bufH, offDisc, offVal, sizDisc, sizVal) =>
                {
                    var enc = (IGpuCommandEncoder)host.CommandEncoders.Get(selfH);
                    var buf = (IGpuBuffer)host.Buffers.Get(bufH);
                    enc.ClearBuffer(buf,
                        offDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)offVal)),
                        sizDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)sizVal)));
                });

            BindDebugMarkers(runtime, alloc, "gpu-command-encoder",
                host.CommandEncoders,
                h => ((IGpuCommandEncoder)h).PushDebugGroup,
                h => ((IGpuCommandEncoder)h).PopDebugGroup,
                h => ((IGpuCommandEncoder)h).InsertDebugMarker);

            BindLabeled(runtime, alloc,
                "gpu-command-encoder", host.CommandEncoders,
                h => ((IGpuCommandEncoder)h).Label(),
                (h, s) => ((IGpuCommandEncoder)h).SetLabel(s));
        }

        // ----------------------------------------------------
        //   wasi:webgpu/webgpu@0.0.1 resource gpu-compute-pass-encoder
        //     Session 6 covers everything except set-bind-group
        //     (deferred to session 7's option<list>+result<_, error>
        //     batch).
        // ----------------------------------------------------

        private static void BindGpuComputePassEncoder(WasmRuntime runtime,
            WasiWebgpuHost host, Wacs.WASI.GFX.HostBinding.Realloc alloc)
        {
            // set-pipeline(self, pipeline: borrow<gpu-compute-pipeline>)
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]gpu-compute-pass-encoder.set-pipeline"),
                (_, selfH, pipeH) =>
                {
                    var pass = (IGpuComputePassEncoder)host.ComputePassEncoders.Get(selfH);
                    var pipe = (IGpuComputePipeline)host.ComputePipelines.Get(pipeH);
                    pass.SetPipeline(pipe);
                });

            // dispatch-workgroups(self, x: u32, y: option<u32>, z: option<u32>)
            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, int, int>>(
                (Ns, "[method]gpu-compute-pass-encoder.dispatch-workgroups"),
                (_, selfH, x, yDisc, yVal, zDisc, zVal) =>
                {
                    var pass = (IGpuComputePassEncoder)host.ComputePassEncoders.Get(selfH);
                    pass.DispatchWorkgroups(unchecked((uint)x),
                        yDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)yVal)),
                        zDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)zVal)));
                });

            // dispatch-workgroups-indirect(self, indirect-buffer: borrow<gpu-buffer>,
            //   indirect-offset: u64)
            runtime.BindHostFunction<Action<ExecContext, int, int, long>>(
                (Ns, "[method]gpu-compute-pass-encoder.dispatch-workgroups-indirect"),
                (_, selfH, bufH, off) =>
                {
                    var pass = (IGpuComputePassEncoder)host.ComputePassEncoders.Get(selfH);
                    var buf = (IGpuBuffer)host.Buffers.Get(bufH);
                    pass.DispatchWorkgroupsIndirect(buf,
                        unchecked((ulong)off));
                });

            // end(self) — void
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[method]gpu-compute-pass-encoder.end"),
                (_, selfH) =>
                {
                    var pass = (IGpuComputePassEncoder)host.ComputePassEncoders.Get(selfH);
                    pass.End();
                });

            BindDebugMarkers(runtime, alloc, "gpu-compute-pass-encoder",
                host.ComputePassEncoders,
                h => ((IGpuComputePassEncoder)h).PushDebugGroup,
                h => ((IGpuComputePassEncoder)h).PopDebugGroup,
                h => ((IGpuComputePassEncoder)h).InsertDebugMarker);

            BindLabeled(runtime, alloc,
                "gpu-compute-pass-encoder", host.ComputePassEncoders,
                h => ((IGpuComputePassEncoder)h).Label(),
                (h, s) => ((IGpuComputePassEncoder)h).SetLabel(s));
        }

        // ----------------------------------------------------
        //   wasi:webgpu/webgpu@0.0.1 resource gpu-queue
        //     submit / on-submitted-work-done / label / set-label
        //     + drop. write-buffer-with-copy / write-texture-with-copy
        //     land in session 7 (result<_, error> shape pair).
        // ----------------------------------------------------

        private static void BindGpuQueue(WasmRuntime runtime,
            WasiWebgpuHost host, Wacs.WASI.GFX.HostBinding.Realloc alloc)
        {
            // submit(self, command-buffers: list<borrow<gpu-command-buffer>>)
            // Wire: (self, listPtr:i32, listLen:i32). Each element is
            // 4 bytes (i32 handle).
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (Ns, "[method]gpu-queue.submit"),
                (ctx, selfH, listPtr, listLen) =>
                {
                    var queue = (IGpuQueue)host.Queues.Get(selfH);
                    var bufs = new IGpuCommandBuffer[listLen];
                    for (int i = 0; i < listLen; i++)
                    {
                        int handle = ctx.ReadI32LE(listPtr + i * 4);
                        bufs[i] = (IGpuCommandBuffer)host.CommandBuffers.Get(handle);
                    }
                    queue.Submit(bufs);
                });

            // on-submitted-work-done(self) — void.
            // Real backends signal completion via Pollable; v0.0.1
            // sync surface is just "block until done."
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[method]gpu-queue.on-submitted-work-done"),
                (_, selfH) =>
                {
                    var queue = (IGpuQueue)host.Queues.Get(selfH);
                    queue.OnSubmittedWorkDone();
                });

            BindLabeled(runtime, alloc,
                "gpu-queue", host.Queues,
                h => ((IGpuQueue)h).Label(),
                (h, s) => ((IGpuQueue)h).SetLabel(s));
        }

        // ----------------------------------------------------
        //   Shared `label / set-label / [resource-drop]` shape
        //   for the four resources whose only WIT surface this
        //   session covers is the label triple:
        //     gpu-shader-module, gpu-pipeline-layout,
        //     gpu-bind-group-layout, gpu-bind-group.
        //
        //   Cuts ~36 lines of identical boilerplate. Resources
        //   whose label/set-label sits alongside other methods
        //   (gpu-buffer / gpu-device) bind them inline.
        // ----------------------------------------------------

        // ----------------------------------------------------
        //   wasi:webgpu/webgpu@0.0.1 resource gpu-texture
        //     Session 7 covers query/lifecycle methods. The
        //     create-view path is deferred: its descriptor has
        //     9 option fields (20 i32 flat), pushing the host
        //     function's input arity beyond Func<T1..T16,TResult>
        //     and requiring an IFunctionInstance custom shape
        //     that pairs with the descriptor-decoding work in
        //     session 8. [static]from-graphics-buffer waits on
        //     the graphics-context bridge session.
        // ----------------------------------------------------

        private static void BindGpuTexture(WasmRuntime runtime,
            WasiWebgpuHost host, Wacs.WASI.GFX.HostBinding.Realloc alloc)
        {
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[method]gpu-texture.destroy"),
                (_, selfH) =>
                {
                    var t = (IGpuTexture)host.Textures.Get(selfH);
                    t.Destroy();
                });

            // u32-return query methods share a helper.
            BindTextureU32Query(runtime, host, "width",
                t => t.Width());
            BindTextureU32Query(runtime, host, "height",
                t => t.Height());
            BindTextureU32Query(runtime, host, "depth-or-array-layers",
                t => t.DepthOrArrayLayers());
            BindTextureU32Query(runtime, host, "mip-level-count",
                t => t.MipLevelCount());
            BindTextureU32Query(runtime, host, "sample-count",
                t => t.SampleCount());
            BindTextureU32Query(runtime, host, "usage",
                t => t.Usage());

            // dimension / format — enum returns lower to i32.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-texture.dimension"),
                (_, selfH) =>
                {
                    var t = (IGpuTexture)host.Textures.Get(selfH);
                    return (int)t.Dimension();
                });
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-texture.format"),
                (_, selfH) =>
                {
                    var t = (IGpuTexture)host.Textures.Get(selfH);
                    return (int)t.Format();
                });

            BindLabeled(runtime, alloc,
                "gpu-texture", host.Textures,
                h => ((IGpuTexture)h).Label(),
                (h, s) => ((IGpuTexture)h).SetLabel(s));
        }

        private static void BindTextureU32Query(WasmRuntime runtime,
            WasiWebgpuHost host, string methodName,
            Func<IGpuTexture, uint> query)
        {
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-texture." + methodName),
                (_, selfH) =>
                {
                    var t = (IGpuTexture)host.Textures.Get(selfH);
                    return unchecked((int)query(t));
                });
        }

        // ----------------------------------------------------
        //   wasi:webgpu/webgpu@0.0.1 resource gpu-render-pipeline
        //     Identical surface to gpu-compute-pipeline: label,
        //     set-label, get-bind-group-layout, drop.
        // ----------------------------------------------------

        private static void BindGpuRenderPipeline(WasmRuntime runtime,
            WasiWebgpuHost host, Wacs.WASI.GFX.HostBinding.Realloc alloc)
        {
            BindLabeled(runtime, alloc,
                "gpu-render-pipeline", host.RenderPipelines,
                h => ((IGpuRenderPipeline)h).Label(),
                (h, s) => ((IGpuRenderPipeline)h).SetLabel(s));

            runtime.BindHostFunction<Func<ExecContext, int, int, int>>(
                (Ns, "[method]gpu-render-pipeline.get-bind-group-layout"),
                (_, selfH, index) =>
                {
                    var p = (IGpuRenderPipeline)host.RenderPipelines.Get(selfH);
                    var bgl = p.GetBindGroupLayout(unchecked((uint)index));
                    return host.BindGroupLayouts.Allocate(bgl);
                });
        }

        // Shared `push-debug-group(label: string) / pop-debug-group() /
        // insert-debug-marker(label: string)` triple — shows up on
        // gpu-command-encoder, gpu-compute-pass-encoder, gpu-render-
        // pass-encoder. Push/insert take a string PARAM (decoded from
        // memory); pop is a no-arg void. Resolved through Func-returns
        // that grant access to the instance-bound delegates so we
        // don't need to re-resolve the resource in every callback.
        private static void BindDebugMarkers(WasmRuntime runtime,
            Wacs.WASI.GFX.HostBinding.Realloc alloc,
            string resourceName,
            Wacs.WASI.GFX.HostBinding.ResourceTable table,
            Func<object, Action<string>> pushFor,
            Func<object, Action> popFor,
            Func<object, Action<string>> insertFor)
        {
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (Ns, "[method]" + resourceName + ".push-debug-group"),
                (ctx, selfH, ptr, len) =>
                {
                    var inst = table.Get(selfH);
                    pushFor(inst)(ReadUtf8(ctx, ptr, len));
                });
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[method]" + resourceName + ".pop-debug-group"),
                (_, selfH) =>
                {
                    var inst = table.Get(selfH);
                    popFor(inst)();
                });
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (Ns, "[method]" + resourceName + ".insert-debug-marker"),
                (ctx, selfH, ptr, len) =>
                {
                    var inst = table.Get(selfH);
                    insertFor(inst)(ReadUtf8(ctx, ptr, len));
                });
        }

        private static void BindLabeled(WasmRuntime runtime,
            Wacs.WASI.GFX.HostBinding.Realloc alloc,
            string resourceName,
            Wacs.WASI.GFX.HostBinding.ResourceTable table,
            Func<object, string> getLabel,
            Action<object, string> setLabel)
        {
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]" + resourceName + ".label"),
                (ctx, selfH, retArea) =>
                {
                    var inst = table.Get(selfH);
                    WriteUtf8Allocated(ctx, alloc, retArea,
                        getLabel(inst) ?? string.Empty);
                });
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (Ns, "[method]" + resourceName + ".set-label"),
                (ctx, selfH, ptr, len) =>
                {
                    var inst = table.Get(selfH);
                    setLabel(inst, ReadUtf8(ctx, ptr, len));
                });
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]" + resourceName),
                (_, h) => table.Drop(h));
        }

        // ====================================================
        //   Canonical-ABI helpers (session 4)
        // ====================================================

        // Read a (ptr, len) UTF-8 pair from guest linear memory.
        private static string ReadUtf8(ExecContext ctx, int ptr, int len)
        {
            if (len <= 0) return string.Empty;
            var mem = ctx.Memory();
            return System.Text.Encoding.UTF8.GetString(mem, ptr, len);
        }

        // Write an option<own<R>> at retArea (8 bytes, align 4):
        //   disc@0 (u8 + 3 pad), handle@4 (i32 — only valid when disc=1).
        private static void WriteOptionHandle(ExecContext ctx,
            int retArea, bool isSome, int handle)
        {
            var mem = ctx.Memory();
            mem[retArea] = (byte)(isSome ? 1 : 0);
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            ctx.WriteI32LE(retArea + 4, isSome ? handle : 0);
        }

        // Encode `value` as UTF-8, allocate guest memory via
        // cabi_realloc, copy bytes, write the (ptr, len) pair into
        // the 8-byte retArea. Mirrors GFX's WriteU8List path —
        // canonical-ABI list<u8> / string share the retArea layout.
        private static void WriteUtf8Allocated(ExecContext ctx,
            Wacs.WASI.GFX.HostBinding.Realloc alloc, int retArea,
            string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            int ptr = bytes.Length == 0 ? 0
                : alloc.Allocate(1, bytes.Length);
            if (bytes.Length > 0)
            {
                var mem = ctx.Memory();
                for (int i = 0; i < bytes.Length; i++)
                    mem[ptr + i] = bytes[i];
            }
            ctx.WriteI32LE(retArea, ptr);
            ctx.WriteI32LE(retArea + 4, bytes.Length);
        }
    }
}
