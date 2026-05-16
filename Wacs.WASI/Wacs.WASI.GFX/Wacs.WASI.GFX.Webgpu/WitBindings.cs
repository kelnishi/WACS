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
using IGpuRenderPassEncoder = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuRenderPassEncoder;
using IGpuRenderBundleEncoder = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuRenderBundleEncoder;
using GpuColor = Wacs.WASI.GFX.Webgpu.Webgpu.GpuColor;
using GpuIndexFormat = Wacs.WASI.GFX.Webgpu.Webgpu.GpuIndexFormat;
using GpuRenderBundleDescriptor = Wacs.WASI.GFX.Webgpu.Webgpu.GpuRenderBundleDescriptor;
using UnmapErrorKind = Wacs.WASI.GFX.Webgpu.Webgpu.UnmapErrorKind;
using MapAsyncErrorKind = Wacs.WASI.GFX.Webgpu.Webgpu.MapAsyncErrorKind;
using WriteBufferErrorKind = Wacs.WASI.GFX.Webgpu.Webgpu.WriteBufferErrorKind;
using SetBindGroupErrorKind = Wacs.WASI.GFX.Webgpu.Webgpu.SetBindGroupErrorKind;
using GetMappedRangeErrorKind = Wacs.WASI.GFX.Webgpu.Webgpu.GetMappedRangeErrorKind;
using RequestDeviceErrorKind = Wacs.WASI.GFX.Webgpu.Webgpu.RequestDeviceErrorKind;
using CreateQuerySetErrorKind = Wacs.WASI.GFX.Webgpu.Webgpu.CreateQuerySetErrorKind;
using IGpuQuerySet = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuQuerySet;
using GpuQuerySetDescriptor = Wacs.WASI.GFX.Webgpu.Webgpu.GpuQuerySetDescriptor;
using GpuQueryType = Wacs.WASI.GFX.Webgpu.Webgpu.GpuQueryType;
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
            BindGpuAdapter(runtime, host, alloc);
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
            BindGpuRenderPassEncoder(runtime, host, alloc);
            BindGpuRenderBundleEncoder(runtime, host, alloc);

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

            // Passive resource-drops for the keyed-record
            // helpers in the WIT. wit-bindgen generates Drop
            // impls for these even when the guest never
            // constructs an instance — the binding has to be
            // registered or the import resolver rejects the
            // module. Since no instances exist, the drop is a
            // no-op; a non-zero handle here would indicate a
            // host that minted one which the binding never
            // gave back, and would point at a future wiring
            // gap rather than something this drop should
            // recover from.
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]record-option-gpu-size64"),
                (_, _) => { });
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]record-gpu-pipeline-constant-value"),
                (_, _) => { });
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

        private static void BindGpuAdapter(WasmRuntime runtime,
            WasiWebgpuHost host,
            Wacs.WASI.GFX.HostBinding.Realloc alloc)
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
            // option<gpu-device-descriptor> flat-form (13 i32):
            //   opt_disc:i32
            //   record:
            //     required-features: option<list<gpu-feature-name>>  3 i32
            //     required-limits:   option<own<record-option-gpu-size64>> 2
            //     default-queue:     option<gpu-queue-descriptor{label:opt<string>}> 4
            //     label:             option<string>                  3 i32
            //
            // Wire: 1 (self) + 13 (option<record>) + 1 (retArea) = 15 i32.
            // retArea: 16 bytes, align 4 (handle Ok or kind+message Err).
            //
            // Descriptor decoding deferred — impl receives
            // Option<GpuDeviceDescriptor>.None; nested-list +
            // option<own<R>> + nested-record decode pairs with the
            // create-* methods that share the same shape complexity.
            runtime.BindHostFunction<CustomDelegates.RequestDevice>(
                (Ns, "[method]gpu-adapter.request-device"),
                (ctx, selfH, _od,
                    _rfDisc, _rfPtr, _rfLen,
                    _rlDisc, _rlVal,
                    _dqDisc, _dqLDisc, _dqLPtr, _dqLLen,
                    _lblDisc, _lblPtr, _lblLen,
                    retArea) =>
                {
                    var ad = (IGpuAdapter)host.Adapters.Get(selfH);
                    var result = ad.RequestDevice(
                        Option<GpuDeviceDescriptor>.None);
                    if (result.IsOk)
                    {
                        var handle = host.Devices.Allocate(result.Ok);
                        WriteResultHandleErrRecord(ctx, alloc, retArea,
                            isOk: true, handle: handle,
                            kindDisc: 0, message: "");
                    }
                    else
                    {
                        WriteResultHandleErrRecord(ctx, alloc, retArea,
                            isOk: false, handle: 0,
                            kindDisc: RequestDeviceErrorKindDisc(result.Err!.Kind),
                            message: result.Err.Message);
                    }
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
            // Wire the cross-helper closure for the
            // option<own<gpu-error>> retArea writer so it can
            // allocate handles into this host's Errors table
            // without knowing about WasiWebgpuHost directly.
            _allocateGpuError = err => host.Errors.Allocate(err);

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

            // ===== create-* methods (descriptor decoding deferred) =====
            // All create-* methods receive their flat-form params and
            // pass a default-constructed descriptor (or Option.None for
            // option<descriptor>) to the impl. Real backends (wgpu-
            // native) need full descriptor decode; ships alongside the
            // dispatcher.

            // create-buffer(self, gpu-buffer-descriptor{u64,u32,opt<bool>,opt<string>})
            //   -> own<gpu-buffer>
            // Flat: u64 size + u32 usage + 2 opt<bool> + 3 opt<string> = 1 i64 + 6 i32
            runtime.BindHostFunction<Func<ExecContext,
                int, long, int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-device.create-buffer"),
                (ctx, selfH, sizeI64, usageI32, mDisc, mVal,
                    lblDisc, lblPtr, lblLen) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    var descriptor = new Webgpu.GpuBufferDescriptor
                    {
                        Size = unchecked((ulong)sizeI64),
                        Usage = unchecked((uint)usageI32),
                        MappedAtCreation = mDisc == 0
                            ? Option<bool>.None
                            : Option<bool>.Some(mVal != 0),
                        Label = lblDisc == 0
                            ? Option<string>.None
                            : Option<string>.Some(
                                ReadUtf8(ctx, lblPtr, lblLen)),
                    };
                    var b = dev.CreateBuffer(descriptor);
                    return host.Buffers.Allocate(b);
                });

            // create-bind-group-layout(self, descriptor{list, opt<string>})
            //   -> own<bg-layout>. Flat: 2 i32 list + 3 i32 opt<string> = 5 i32.
            // entries list: each element is a 112-byte gpu-bind-group-
            // layout-entry record in guest memory; see
            // ReadBindGroupLayoutEntry for the byte layout.
            runtime.BindHostFunction<Func<ExecContext,
                int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-device.create-bind-group-layout"),
                (ctx, selfH, entPtr, entLen,
                    lblDisc, lblPtr, lblLen) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    var entries = new Webgpu.GpuBindGroupLayoutEntry[entLen];
                    for (int i = 0; i < entLen; i++)
                        entries[i] = ReadBindGroupLayoutEntry(
                            ctx, entPtr + i * 56);
                    var descriptor = new Webgpu.GpuBindGroupLayoutDescriptor
                    {
                        Entries = entries,
                        Label = lblDisc == 0
                            ? Option<string>.None
                            : Option<string>.Some(ReadUtf8(ctx, lblPtr, lblLen)),
                    };
                    var bgl = dev.CreateBindGroupLayout(descriptor);
                    return host.BindGroupLayouts.Allocate(bgl);
                });

            // create-pipeline-layout(self, descriptor{list, opt<string>})
            //   -> own<pipeline-layout>.
            // bind-group-layouts list: list<option<borrow<gpu-bind-group-
            // layout>>>. Each element is opt<u32 handle>: 4-byte disc +
            // 4-byte payload = 8 bytes, align 4.
            runtime.BindHostFunction<Func<ExecContext,
                int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-device.create-pipeline-layout"),
                (ctx, selfH, bglPtr, bglLen,
                    lblDisc, lblPtr, lblLen) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    var bgls = new Option<IGpuBindGroupLayout>[bglLen];
                    for (int i = 0; i < bglLen; i++)
                    {
                        int elemPtr = bglPtr + i * 8;
                        int disc = ctx.Memory()[elemPtr];
                        if (disc == 0)
                            bgls[i] = Option<IGpuBindGroupLayout>.None;
                        else
                        {
                            int handle = ctx.ReadI32LE(elemPtr + 4);
                            var inst = (IGpuBindGroupLayout)
                                host.BindGroupLayouts.Get(handle);
                            bgls[i] = Option<IGpuBindGroupLayout>.Some(inst);
                        }
                    }
                    var descriptor = new Webgpu.GpuPipelineLayoutDescriptor
                    {
                        BindGroupLayouts = bgls,
                        Label = lblDisc == 0
                            ? Option<string>.None
                            : Option<string>.Some(ReadUtf8(ctx, lblPtr, lblLen)),
                    };
                    var pl = dev.CreatePipelineLayout(descriptor);
                    return host.PipelineLayouts.Allocate(pl);
                });

            // create-bind-group(self, descriptor{borrow, list, opt<string>})
            //   -> own<bind-group>. Flat: 1 i32 borrow + 2 i32 list + 3 i32 = 6 i32.
            // entries list: each element is a 56-byte gpu-bind-group-entry
            // record. See ReadBindGroupEntry for the byte layout.
            runtime.BindHostFunction<Func<ExecContext,
                int, int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-device.create-bind-group"),
                (ctx, selfH, layH, entPtr, entLen,
                    lblDisc, lblPtr, lblLen) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    var layout = (IGpuBindGroupLayout)
                        host.BindGroupLayouts.Get(layH);
                    var entries = new Webgpu.GpuBindGroupEntry[entLen];
                    for (int i = 0; i < entLen; i++)
                        entries[i] = ReadBindGroupEntry(
                            ctx, entPtr + i * 56, host);
                    var descriptor = new Webgpu.GpuBindGroupDescriptor
                    {
                        Layout = layout,
                        Entries = entries,
                        Label = lblDisc == 0
                            ? Option<string>.None
                            : Option<string>.Some(ReadUtf8(ctx, lblPtr, lblLen)),
                    };
                    var bg = dev.CreateBindGroup(descriptor);
                    return host.BindGroups.Allocate(bg);
                });

            // create-shader-module(self, descriptor{string, opt<list>, opt<string>})
            //   -> own<shader-module>. Flat: 2 i32 string + 3 i32 opt<list> + 3 i32 opt<string> = 8 i32.
            runtime.BindHostFunction<Func<ExecContext,
                int, int, int, int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-device.create-shader-module"),
                (ctx, selfH, codePtr, codeLen,
                    _hintsDisc, _hintsPtr, _hintsLen,
                    lblDisc, lblPtr, lblLen) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    var descriptor = new Webgpu.GpuShaderModuleDescriptor
                    {
                        Code = ReadUtf8(ctx, codePtr, codeLen),
                        // compilation-hints decoding deferred — the
                        // wgpu-native binding does its own WGSL
                        // parsing and ignores hints today.
                        CompilationHints = Option<Webgpu.GpuShaderModuleCompilationHint[]>.None,
                        Label = lblDisc == 0
                            ? Option<string>.None
                            : Option<string>.Some(
                                ReadUtf8(ctx, lblPtr, lblLen)),
                    };
                    var sm = dev.CreateShaderModule(descriptor);
                    return host.ShaderModules.Allocate(sm);
                });

            // create-compute-pipeline(self, descriptor{programmable-stage, layout-mode,
            //   opt<string>}). Flat: 6 i32 (programmable-stage) + 2 i32 (layout-mode
            //   variant) + 3 i32 (opt<string>) = 11 i32.
            runtime.BindHostFunction<Func<ExecContext,
                int, int, int, int, int, int, int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-device.create-compute-pipeline"),
                (ctx, selfH, modH, epDisc, epPtr, epLen, cDisc, cVal,
                    layDisc, layVal, lblDisc, lblPtr, lblLen) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    var module = (IGpuShaderModule)host.ShaderModules.Get(modH);
                    var stage = new Webgpu.GpuProgrammableStage
                    {
                        Module = module,
                        EntryPoint = epDisc == 0
                            ? Option<string>.None
                            : Option<string>.Some(ReadUtf8(ctx, epPtr, epLen)),
                        // constants resource decode deferred; hello_compute
                        // doesn't use override constants.
                        Constants = Option<Webgpu.IRecordGpuPipelineConstantValue>.None,
                    };
                    // layout-mode variant: disc 0 = specific(borrow<pipeline-
                    // layout>); disc 1 = auto.
                    Webgpu.GpuLayoutMode layoutMode =
                        layDisc == 0
                        ? new Webgpu.GpuLayoutMode.GpuLayoutModeSpecific(
                            (IGpuPipelineLayout)host.PipelineLayouts.Get(layVal))
                        : new Webgpu.GpuLayoutMode.GpuLayoutModeAuto();
                    var descriptor = new Webgpu.GpuComputePipelineDescriptor
                    {
                        Compute = stage,
                        Layout = layoutMode,
                        Label = lblDisc == 0
                            ? Option<string>.None
                            : Option<string>.Some(ReadUtf8(ctx, lblPtr, lblLen)),
                    };
                    var cp = dev.CreateComputePipeline(descriptor);
                    return host.ComputePipelines.Allocate(cp);
                });

            // create-command-encoder(self, opt<descriptor{label:opt<string>}>)
            //   -> own<command-encoder>. Flat: 1 + 3 = 4 i32.
            runtime.BindHostFunction<Func<ExecContext,
                int, int, int, int, int, int>>(
                (Ns, "[method]gpu-device.create-command-encoder"),
                (ctx, selfH, oDisc, lblDisc, lblPtr, lblLen) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    var descriptor = oDisc == 0
                        ? Option<Webgpu.GpuCommandEncoderDescriptor>.None
                        : Option<Webgpu.GpuCommandEncoderDescriptor>.Some(
                            new Webgpu.GpuCommandEncoderDescriptor
                            {
                                Label = lblDisc == 0
                                    ? Option<string>.None
                                    : Option<string>.Some(
                                        ReadUtf8(ctx, lblPtr, lblLen)),
                            });
                    var enc = dev.CreateCommandEncoder(descriptor);
                    return host.CommandEncoders.Allocate(enc);
                });

            // create-render-bundle-encoder(self, descriptor) -> own<render-bundle-encoder>.
            // Descriptor: opt<bool>(2) + opt<bool>(2) + list<opt<format>>(2) +
            // opt<format>(2) + opt<u32>(2) + opt<string>(3) = 13 i32.
            runtime.BindHostFunction<Func<ExecContext,
                int, int, int, int, int, int, int, int, int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-device.create-render-bundle-encoder"),
                (_, selfH,
                    _drDisc, _drVal, _srDisc, _srVal,
                    _cfPtr, _cfLen, _dsfDisc, _dsfVal,
                    _scDisc, _scVal, _lblDisc, _lblPtr, _lblLen) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    var enc = dev.CreateRenderBundleEncoder(
                        new Webgpu.GpuRenderBundleEncoderDescriptor());
                    return host.RenderBundleEncoders.Allocate(enc);
                });

            // create-query-set(self, descriptor{type:enum, count:u32, opt<string>})
            //   -> result<own<query-set>, create-query-set-error>
            // Flat: 1 + 1 + 3 = 5 i32; self + 5 + retArea = 7 i32, 0 results.
            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-device.create-query-set"),
                (ctx, selfH, _type, _count,
                    _lblDisc, _lblPtr, _lblLen, retArea) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    var result = dev.CreateQuerySet(
                        new Webgpu.GpuQuerySetDescriptor());
                    if (result.IsOk)
                    {
                        var h = host.QuerySets.Allocate(result.Ok);
                        WriteResultHandleErrRecord(ctx, alloc, retArea,
                            isOk: true, handle: h,
                            kindDisc: 0, message: "");
                    }
                    else
                    {
                        WriteResultHandleErrRecord(ctx, alloc, retArea,
                            isOk: false, handle: 0,
                            kindDisc: CreateQuerySetErrorKindDisc(result.Err!.Kind),
                            message: result.Err.Message);
                    }
                });

            // ===== Misc gpu-device methods =====

            // push-error-scope(self, filter: gpu-error-filter)
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]gpu-device.push-error-scope"),
                (_, selfH, filter) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    dev.PushErrorScope((Webgpu.GpuErrorFilter)filter);
                });

            // pop-error-scope(self)
            //   -> result<option<own<gpu-error>>, pop-error-scope-error>
            // retArea: 16 bytes, align 4.
            //   outer disc:u8 @0 + 3 pad
            //   payload @4:
            //     Ok (option<own>):  inner disc:u8 @4 + 3 pad + handle @8
            //     Err (record):      kind:u8 @4 + 3 pad + msg.ptr @8 + msg.len @12
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]gpu-device.pop-error-scope"),
                (ctx, selfH, retArea) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    var result = dev.PopErrorScope();
                    WriteResultOptionHandleErrRecord(ctx, alloc, retArea,
                        result);
                });

            // onuncapturederror-subscribe(self) -> own<pollable>
            // The returned pollable lives in the shared Preview2
            // ResourceContext (wasi:io's IPollable resource), not
            // a webgpu-local table. Bind through SharedResources;
            // when no SharedResources is configured the call
            // surfaces as a clear bridge-missing error.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]gpu-device.onuncapturederror-subscribe"),
                (_, selfH) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    if (host.SharedResources == null)
                        throw new Wacs.WASI.GFX.Types.WasiGfxException(
                            "onuncapturederror-subscribe needs "
                            + "WasiWebgpuConfiguration.SharedResources "
                            + "to mint a pollable handle.");
                    var p = dev.OnuncapturederrorSubscribe();
                    var table = host.SharedResources
                        .Table<Wacs.WASI.Preview2.Io.Pollable>();
                    return table.Allocate((Wacs.WASI.Preview2.Io.Pollable)p);
                });

            // connect-graphics-context(self, borrow<context>) -> ()
            // The context handle lives in the wasi-gfx host's
            // Contexts table. The bridge is the
            // GraphicsContextResolver closure the embedder wires
            // when both hosts share a process.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]gpu-device.connect-graphics-context"),
                (_, selfH, ctxH) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    if (host.GraphicsContextResolver == null)
                        throw new Wacs.WASI.GFX.Types.WasiGfxException(
                            "[method]gpu-device.connect-graphics-context "
                            + "called but no GraphicsContextResolver "
                            + "is configured; impossible to reach a "
                            + "wasi:graphics-context.context handle.");
                    var ctxObj = host.GraphicsContextResolver(ctxH);
                    if (ctxObj == null)
                        throw new Wacs.WASI.GFX.Types.WasiGfxException(
                            "[method]gpu-device.connect-graphics-context: "
                            + "GraphicsContextResolver returned null for "
                            + "handle " + ctxH + ".");
                    dev.ConnectGraphicsContext(ctxObj);
                });

            // create-texture(self, gpu-texture-descriptor) -> own<gpu-texture>
            // Descriptor flat (19 i32) — see CustomDelegates.CreateTexture
            // for the field-by-field layout.
            runtime.BindHostFunction<CustomDelegates.CreateTexture>(
                (Ns, "[method]gpu-device.create-texture"),
                (ctx, selfH,
                    w, hDisc, hVal, dDisc, dVal,
                    mipDisc, mipVal, smpDisc, smpVal,
                    dimDisc, dimVal, fmt, usage,
                    vfDisc, vfPtr, vfLen, lblDisc, lblPtr, lblLen) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    var size = new Webgpu.GpuExtent3D
                    {
                        Width = unchecked((uint)w),
                        Height = hDisc == 0
                            ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)hVal)),
                        DepthOrArrayLayers = dDisc == 0
                            ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)dVal)),
                    };
                    // view-formats: opt<list<gpu-texture-format>>. The
                    // enum encodes as u8 in memory; read each byte.
                    Option<Webgpu.GpuTextureFormat[]> viewFormats;
                    if (vfDisc == 0)
                        viewFormats = Option<Webgpu.GpuTextureFormat[]>.None;
                    else
                    {
                        var arr = new Webgpu.GpuTextureFormat[vfLen];
                        var mem = ctx.Memory();
                        for (int i = 0; i < vfLen; i++)
                            arr[i] = (Webgpu.GpuTextureFormat)mem[vfPtr + i];
                        viewFormats = Option<Webgpu.GpuTextureFormat[]>.Some(arr);
                    }
                    var descriptor = new Webgpu.GpuTextureDescriptor
                    {
                        Size = size,
                        MipLevelCount = mipDisc == 0
                            ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)mipVal)),
                        SampleCount = smpDisc == 0
                            ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)smpVal)),
                        Dimension = dimDisc == 0
                            ? Option<Webgpu.GpuTextureDimension>.None
                            : Option<Webgpu.GpuTextureDimension>.Some(
                                (Webgpu.GpuTextureDimension)dimVal),
                        Format = (Webgpu.GpuTextureFormat)fmt,
                        Usage = unchecked((uint)usage),
                        ViewFormats = viewFormats,
                        Label = lblDisc == 0
                            ? Option<string>.None
                            : Option<string>.Some(ReadUtf8(ctx, lblPtr, lblLen)),
                    };
                    var t = dev.CreateTexture(descriptor);
                    return host.Textures.Allocate(t);
                });

            // create-sampler(self, option<gpu-sampler-descriptor>)
            //   -> own<gpu-sampler>
            // Flat: self + outer-disc + 9 opt<enum/u16> + 2 opt<f32> +
            //       opt<string> = 1 + 1 + 18 + 4 + 3 = 27 with mixed
            //       i32/f32 — see CustomDelegates.CreateSampler.
            runtime.BindHostFunction<CustomDelegates.CreateSampler>(
                (Ns, "[method]gpu-device.create-sampler"),
                (ctx, selfH, optDisc,
                    aud, auv, avd, avv, awd, aww,
                    mfd, mfv, mnd, mnv, mid, miv,
                    lminD, lminV, lmaxD, lmaxV,
                    cmpD, cmpV, maD, maV,
                    lblD, lblPtr, lblLen) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    Option<Webgpu.GpuSamplerDescriptor> descriptor;
                    if (optDisc == 0)
                    {
                        descriptor = Option<Webgpu.GpuSamplerDescriptor>.None;
                    }
                    else
                    {
                        var d = new Webgpu.GpuSamplerDescriptor
                        {
                            AddressModeU = aud == 0
                                ? Option<Webgpu.GpuAddressMode>.None
                                : Option<Webgpu.GpuAddressMode>.Some(
                                    (Webgpu.GpuAddressMode)auv),
                            AddressModeV = avd == 0
                                ? Option<Webgpu.GpuAddressMode>.None
                                : Option<Webgpu.GpuAddressMode>.Some(
                                    (Webgpu.GpuAddressMode)avv),
                            AddressModeW = awd == 0
                                ? Option<Webgpu.GpuAddressMode>.None
                                : Option<Webgpu.GpuAddressMode>.Some(
                                    (Webgpu.GpuAddressMode)aww),
                            MagFilter = mfd == 0
                                ? Option<Webgpu.GpuFilterMode>.None
                                : Option<Webgpu.GpuFilterMode>.Some(
                                    (Webgpu.GpuFilterMode)mfv),
                            MinFilter = mnd == 0
                                ? Option<Webgpu.GpuFilterMode>.None
                                : Option<Webgpu.GpuFilterMode>.Some(
                                    (Webgpu.GpuFilterMode)mnv),
                            MipmapFilter = mid == 0
                                ? Option<Webgpu.GpuMipmapFilterMode>.None
                                : Option<Webgpu.GpuMipmapFilterMode>.Some(
                                    (Webgpu.GpuMipmapFilterMode)miv),
                            LodMinClamp = lminD == 0
                                ? Option<float>.None
                                : Option<float>.Some(lminV),
                            LodMaxClamp = lmaxD == 0
                                ? Option<float>.None
                                : Option<float>.Some(lmaxV),
                            Compare = cmpD == 0
                                ? Option<Webgpu.GpuCompareFunction>.None
                                : Option<Webgpu.GpuCompareFunction>.Some(
                                    (Webgpu.GpuCompareFunction)cmpV),
                            MaxAnisotropy = maD == 0
                                ? Option<ushort>.None
                                : Option<ushort>.Some(unchecked((ushort)maV)),
                            Label = lblD == 0
                                ? Option<string>.None
                                : Option<string>.Some(
                                    ReadUtf8(ctx, lblPtr, lblLen)),
                        };
                        descriptor = Option<Webgpu.GpuSamplerDescriptor>.Some(d);
                    }
                    var s = dev.CreateSampler(descriptor);
                    return host.Samplers.Allocate(s);
                });

            // create-compute-pipeline-async(self, gpu-compute-pipeline-
            //   descriptor) -> result<own<gpu-compute-pipeline>,
            //                          create-pipeline-error>
            // Descriptor flat = 11 i32 (same as sync create-compute-
            // pipeline). retArea is 20 bytes (see
            // WriteResultHandleErrPipelineRecord layout).
            runtime.BindHostFunction<CustomDelegates.CreateComputePipelineAsync>(
                (Ns, "[method]gpu-device.create-compute-pipeline-async"),
                (ctx, selfH,
                    _modH, _epDisc, _epPtr, _epLen, _cDisc, _cVal,
                    _layDisc, _layVal, _lblDisc, _lblPtr, _lblLen,
                    retArea) =>
                {
                    var dev = (IGpuDevice)host.Devices.Get(selfH);
                    var result = dev.CreateComputePipelineAsync(
                        new Webgpu.GpuComputePipelineDescriptor());
                    if (result.IsOk)
                    {
                        var h = host.ComputePipelines.Allocate(result.Ok);
                        WriteResultHandleErrPipelineRecord(ctx, alloc, retArea,
                            isOk: true, handle: h,
                            reason: 0, message: "");
                    }
                    else
                    {
                        WriteResultHandleErrPipelineRecord(ctx, alloc, retArea,
                            isOk: false, handle: 0,
                            reason: CreatePipelineErrorReasonDisc(
                                result.Err!.Kind),
                            message: result.Err.Message);
                    }
                });

            // create-render-pipeline / create-render-pipeline-async —
            // the gpu-render-pipeline-descriptor record nests vertex/
            // primitive/depth-stencil/multisample/fragment sub-records
            // for an unflattened arity > 60 i32, beyond what a single
            // custom delegate captures cleanly. Defer until a backend
            // (wgpu-native) actually consumes these — at that point
            // the host code that decodes the descriptor lives in the
            // backend and this binding is mostly retArea-write plumbing.
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

            // get-mapped-range-get-with-copy(self, opt<u64> offset,
            //   opt<u64> size) -> result<list<u8>, get-mapped-range-error>
            // Wire: self + 2 opt<u64> (4 args, 2 i32 + 2 i64) + retArea
            // = 6 args total. retArea: 16 bytes, align 4. Ok writes
            // (ptr, len) at +4/+8; Err writes kind+msg.
            runtime.BindHostFunction<Action<ExecContext,
                int, int, long, int, long, int>>(
                (Ns, "[method]gpu-buffer.get-mapped-range-get-with-copy"),
                (ctx, selfH, offDisc, offVal, sizDisc, sizVal, retArea) =>
                {
                    var buf = (IGpuBuffer)host.Buffers.Get(selfH);
                    var result = buf.GetMappedRangeGetWithCopy(
                        offDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)offVal)),
                        sizDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)sizVal)));
                    WriteResultByteArrayErrRecord(ctx, alloc, retArea,
                        result);
                });

            // get-mapped-range-set-with-copy(self, list<u8>,
            //   opt<u64> off, opt<u64> size)
            //   -> result<_, get-mapped-range-error>
            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, long, int, long, int>>(
                (Ns, "[method]gpu-buffer.get-mapped-range-set-with-copy"),
                (ctx, selfH, dataPtr, dataLen,
                    offDisc, offVal, sizDisc, sizVal, retArea) =>
                {
                    var buf = (IGpuBuffer)host.Buffers.Get(selfH);
                    var data = ReadByteArray(ctx, dataPtr, dataLen);
                    var result = buf.GetMappedRangeSetWithCopy(data,
                        offDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)offVal)),
                        sizDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)sizVal)));
                    WriteResultUnitErrRecord(ctx, alloc, retArea,
                        result.IsOk,
                        result.IsErr
                            ? GetMappedRangeErrorKindDisc(result.Err!.Kind)
                            : 0,
                        result.IsErr ? result.Err!.Message : "");
                });

            // unmap(self) -> result<_, unmap-error>
            // Wire: (self, retArea). 0 results. retArea = 16 bytes
            // (disc:u8 @0 + 3 pad + payload @4..15).
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]gpu-buffer.unmap"),
                (ctx, selfH, retArea) =>
                {
                    var buf = (IGpuBuffer)host.Buffers.Get(selfH);
                    var result = buf.Unmap();
                    WriteResultUnitErrRecord(ctx, alloc, retArea,
                        result.IsOk,
                        result.IsErr
                            ? UnmapErrorKindDisc(result.Err!.Kind)
                            : 0,
                        result.IsErr ? result.Err!.Message : "");
                });

            // map-async(self, mode: gpu-map-mode-flags, opt<u64> offset,
            //   opt<u64> size) -> result<_, map-async-error>
            // Wire: (self, mode:i32, offDisc:i32, offVal:i64,
            //        sizDisc:i32, sizVal:i64, retArea:i32). 0 results.
            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, long, int, long, int>>(
                (Ns, "[method]gpu-buffer.map-async"),
                (ctx, selfH, mode, offDisc, offVal, sizDisc, sizVal, retArea) =>
                {
                    var buf = (IGpuBuffer)host.Buffers.Get(selfH);
                    var result = buf.MapAsync(
                        unchecked((uint)mode),
                        offDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)offVal)),
                        sizDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)sizVal)));
                    WriteResultUnitErrRecord(ctx, alloc, retArea,
                        result.IsOk,
                        result.IsErr
                            ? MapAsyncErrorKindDisc(result.Err!.Kind)
                            : 0,
                        result.IsErr ? result.Err!.Message : "");
                });
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

            BindSetBindGroup(runtime, alloc,
                "gpu-compute-pass-encoder",
                host.ComputePassEncoders, host.BindGroups,
                (instance, args) =>
                    ((IGpuComputePassEncoder)instance).SetBindGroup(
                        args.index, args.bindGroup, args.dynamicOffsets,
                        args.dynamicOffsetsStart, args.dynamicOffsetsLength));
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

            // write-buffer-with-copy(self, borrow<buf>, u64 bufOff,
            //   list<u8> data, opt<u64> dataOff, opt<u64> size)
            //   -> result<_, write-buffer-error>
            // Wire: (self, buf:i32, bufOff:i64, dataPtr:i32, dataLen:i32,
            //        doDisc:i32, doVal:i64, sizDisc:i32, sizVal:i64,
            //        retArea:i32) — 6 i32 + 3 i64 = 10 args total.
            runtime.BindHostFunction<Action<ExecContext,
                int, int, long, int, int, int, long, int, long, int>>(
                (Ns, "[method]gpu-queue.write-buffer-with-copy"),
                (ctx, selfH, bufH, bufOff, dataPtr, dataLen,
                    doDisc, doVal, sizDisc, sizVal, retArea) =>
                {
                    var queue = (IGpuQueue)host.Queues.Get(selfH);
                    var buf = (IGpuBuffer)host.Buffers.Get(bufH);
                    var data = ReadByteArray(ctx, dataPtr, dataLen);
                    var result = queue.WriteBufferWithCopy(
                        buf, unchecked((ulong)bufOff), data,
                        doDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)doVal)),
                        sizDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)sizVal)));
                    WriteResultUnitErrRecord(ctx, alloc, retArea,
                        result.IsOk,
                        result.IsErr
                            ? WriteBufferErrorKindDisc(result.Err!.Kind)
                            : 0,
                        result.IsErr ? result.Err!.Message : "");
                });
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

            // create-view(self, opt<gpu-texture-view-descriptor>)
            //   -> own<gpu-texture-view>
            //
            // option<descriptor> flat-form: 1 opt-disc + 9 option
            // fields (8 option<enum/u32> = 2 i32 each, 1 option<string>
            // = 3 i32) = 1 + 16 + 3 = 20 i32. Wire: self + 20 +
            // i32 result = 22-arity callable, beyond
            // Func<T1..T16,TResult>. Bound via the custom
            // CreateView delegate in CustomDelegates.
            //
            // v1 follow-up: descriptor fields ignored; impl
            // receives Option<GpuTextureViewDescriptor>.None. Real
            // backends pulling format/dimension/aspect/mip/array
            // ranges need the full decode — pairs with the Silk
            // wgpu dispatcher when it lands.
            runtime.BindHostFunction<CustomDelegates.CreateView>(
                (Ns, "[method]gpu-texture.create-view"),
                (_, selfH,
                    _od,
                    _fmtD, _fmtV,
                    _dimD, _dimV,
                    _usD, _usV,
                    _aspD, _aspV,
                    _mipBD, _mipBV,
                    _mipCD, _mipCV,
                    _arrBD, _arrBV,
                    _arrCD, _arrCV,
                    _lblD, _lblPtr, _lblLen) =>
                {
                    var tex = (IGpuTexture)host.Textures.Get(selfH);
                    var view = tex.CreateView(
                        Option<Webgpu.GpuTextureViewDescriptor>.None);
                    return host.TextureViews.Allocate(view);
                });
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

        // ----------------------------------------------------
        //   wasi:webgpu/webgpu@0.0.1 resource gpu-render-pass-encoder
        //     Session: render-pass-encoder lifecycle. Covers the
        //     state-setting and draw methods. set-bind-group
        //     (option<list<u32>> + result<_, error>) deferred to
        //     the result<_, error> batch.
        // ----------------------------------------------------

        private static void BindGpuRenderPassEncoder(WasmRuntime runtime,
            WasiWebgpuHost host, Wacs.WASI.GFX.HostBinding.Realloc alloc)
        {
            // set-viewport(self, x, y, w, h, minDepth, maxDepth: f32×6)
            runtime.BindHostFunction<Action<ExecContext,
                int, float, float, float, float, float, float>>(
                (Ns, "[method]gpu-render-pass-encoder.set-viewport"),
                (_, selfH, x, y, w, h, minD, maxD) =>
                    ((IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH))
                    .SetViewport(x, y, w, h, minD, maxD));

            // set-scissor-rect(self, x, y, w, h: u32×4)
            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, int>>(
                (Ns, "[method]gpu-render-pass-encoder.set-scissor-rect"),
                (_, selfH, x, y, w, h) =>
                    ((IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH))
                    .SetScissorRect(unchecked((uint)x),
                        unchecked((uint)y),
                        unchecked((uint)w),
                        unchecked((uint)h)));

            // set-blend-constant(self, gpu-color{r,g,b,a: f64}) →
            // 4 f64 flat-form params.
            runtime.BindHostFunction<Action<ExecContext,
                int, double, double, double, double>>(
                (Ns, "[method]gpu-render-pass-encoder.set-blend-constant"),
                (_, selfH, r, g, b, a) =>
                {
                    var enc = (IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH);
                    enc.SetBlendConstant(new GpuColor
                    {
                        R = r, G = g, B = b, A = a,
                    });
                });

            // set-stencil-reference(self, u32)
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]gpu-render-pass-encoder.set-stencil-reference"),
                (_, selfH, reference) =>
                    ((IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH))
                    .SetStencilReference(unchecked((uint)reference)));

            // begin-occlusion-query(self, u32)
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]gpu-render-pass-encoder.begin-occlusion-query"),
                (_, selfH, queryIndex) =>
                    ((IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH))
                    .BeginOcclusionQuery(unchecked((uint)queryIndex)));

            // end-occlusion-query(self) — void
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[method]gpu-render-pass-encoder.end-occlusion-query"),
                (_, selfH) =>
                    ((IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH))
                    .EndOcclusionQuery());

            // execute-bundles(self, list<borrow<gpu-render-bundle>>)
            // Wire: (self, listPtr:i32, listLen:i32). 4-byte handles.
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (Ns, "[method]gpu-render-pass-encoder.execute-bundles"),
                (ctx, selfH, listPtr, listLen) =>
                {
                    var enc = (IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH);
                    var bundles = new IGpuRenderBundle[listLen];
                    for (int i = 0; i < listLen; i++)
                    {
                        int h = ctx.ReadI32LE(listPtr + i * 4);
                        bundles[i] = (IGpuRenderBundle)host.RenderBundles.Get(h);
                    }
                    enc.ExecuteBundles(bundles);
                });

            // end(self)
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[method]gpu-render-pass-encoder.end"),
                (_, selfH) =>
                    ((IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH))
                    .End());

            // set-pipeline(self, borrow<gpu-render-pipeline>)
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]gpu-render-pass-encoder.set-pipeline"),
                (_, selfH, pipeH) =>
                {
                    var enc = (IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH);
                    var pipe = (IGpuRenderPipeline)host.RenderPipelines.Get(pipeH);
                    enc.SetPipeline(pipe);
                });

            // set-index-buffer(self, buf, fmt, opt<u64> off, opt<u64> size)
            // Wire: (self, buf:i32, fmt:i32,
            //        offDisc:i32, offVal:i64, sizDisc:i32, sizVal:i64)
            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, long, int, long>>(
                (Ns, "[method]gpu-render-pass-encoder.set-index-buffer"),
                (_, selfH, bufH, fmt, offDisc, offVal, sizDisc, sizVal) =>
                {
                    var enc = (IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH);
                    var buf = (IGpuBuffer)host.Buffers.Get(bufH);
                    enc.SetIndexBuffer(buf,
                        (GpuIndexFormat)fmt,
                        offDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)offVal)),
                        sizDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)sizVal)));
                });

            // set-vertex-buffer(self, slot, opt<buf>, opt<u64> off, opt<u64> size)
            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, int, long, int, long>>(
                (Ns, "[method]gpu-render-pass-encoder.set-vertex-buffer"),
                (_, selfH, slot, bufDisc, bufVal,
                    offDisc, offVal, sizDisc, sizVal) =>
                {
                    var enc = (IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH);
                    enc.SetVertexBuffer(unchecked((uint)slot),
                        bufDisc == 0 ? Option<IGpuBuffer>.None
                            : Option<IGpuBuffer>.Some(
                                (IGpuBuffer)host.Buffers.Get(bufVal)),
                        offDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)offVal)),
                        sizDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)sizVal)));
                });

            // draw(self, vertex-count, opt<instance>, opt<first-vertex>,
            //      opt<first-instance>)
            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-render-pass-encoder.draw"),
                (_, selfH, vc,
                    instDisc, instVal, firstVDisc, firstVVal,
                    firstIDisc, firstIVal) =>
                {
                    var enc = (IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH);
                    enc.Draw(unchecked((uint)vc),
                        instDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)instVal)),
                        firstVDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)firstVVal)),
                        firstIDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)firstIVal)));
                });

            // draw-indexed(self, index-count, opt<inst>, opt<first-index>,
            //              opt<base-vertex:s32>, opt<first-instance>)
            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-render-pass-encoder.draw-indexed"),
                (_, selfH, ic,
                    instDisc, instVal, firstIDisc, firstIVal,
                    baseVDisc, baseVVal, firstInDisc, firstInVal) =>
                {
                    var enc = (IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH);
                    enc.DrawIndexed(unchecked((uint)ic),
                        instDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)instVal)),
                        firstIDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)firstIVal)),
                        baseVDisc == 0 ? Option<int>.None
                            : Option<int>.Some(baseVVal),
                        firstInDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)firstInVal)));
                });

            // draw-indirect(self, borrow<buf>, u64 offset)
            runtime.BindHostFunction<Action<ExecContext, int, int, long>>(
                (Ns, "[method]gpu-render-pass-encoder.draw-indirect"),
                (_, selfH, bufH, off) =>
                {
                    var enc = (IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH);
                    var buf = (IGpuBuffer)host.Buffers.Get(bufH);
                    enc.DrawIndirect(buf, unchecked((ulong)off));
                });

            // draw-indexed-indirect(self, borrow<buf>, u64 offset)
            runtime.BindHostFunction<Action<ExecContext, int, int, long>>(
                (Ns, "[method]gpu-render-pass-encoder.draw-indexed-indirect"),
                (_, selfH, bufH, off) =>
                {
                    var enc = (IGpuRenderPassEncoder)host.RenderPassEncoders
                        .Get(selfH);
                    var buf = (IGpuBuffer)host.Buffers.Get(bufH);
                    enc.DrawIndexedIndirect(buf, unchecked((ulong)off));
                });

            BindDebugMarkers(runtime, alloc, "gpu-render-pass-encoder",
                host.RenderPassEncoders,
                h => ((IGpuRenderPassEncoder)h).PushDebugGroup,
                h => ((IGpuRenderPassEncoder)h).PopDebugGroup,
                h => ((IGpuRenderPassEncoder)h).InsertDebugMarker);

            BindLabeled(runtime, alloc,
                "gpu-render-pass-encoder", host.RenderPassEncoders,
                h => ((IGpuRenderPassEncoder)h).Label(),
                (h, s) => ((IGpuRenderPassEncoder)h).SetLabel(s));

            BindSetBindGroup(runtime, alloc,
                "gpu-render-pass-encoder",
                host.RenderPassEncoders, host.BindGroups,
                (instance, args) =>
                    ((IGpuRenderPassEncoder)instance).SetBindGroup(
                        args.index, args.bindGroup, args.dynamicOffsets,
                        args.dynamicOffsetsStart, args.dynamicOffsetsLength));
        }

        // ----------------------------------------------------
        //   wasi:webgpu/webgpu@0.0.1 resource gpu-render-bundle-encoder
        //     finish(opt<descriptor>) + the shared
        //     set-pipeline/set-buffer/draw* surface from
        //     render-pass-encoder. set-bind-group deferred
        //     (result<_, error> batch).
        // ----------------------------------------------------

        private static void BindGpuRenderBundleEncoder(WasmRuntime runtime,
            WasiWebgpuHost host, Wacs.WASI.GFX.HostBinding.Realloc alloc)
        {
            // finish(self, opt<gpu-render-bundle-descriptor{label:opt<string>}>)
            //   -> own<gpu-render-bundle>
            // opt<descriptor> = 1 + 3 = 4 i32 params; self + 4 = 5 i32 in;
            // i32 result. Descriptor ignored (label only); session-shortcut.
            runtime.BindHostFunction<Func<ExecContext,
                int, int, int, int, int, int>>(
                (Ns, "[method]gpu-render-bundle-encoder.finish"),
                (_, selfH, _od, _lDisc, _lPtr, _lLen) =>
                {
                    var enc = (IGpuRenderBundleEncoder)host.RenderBundleEncoders
                        .Get(selfH);
                    var bundle = enc.Finish(
                        Option<GpuRenderBundleDescriptor>.None);
                    return host.RenderBundles.Allocate(bundle);
                });

            // set-pipeline / set-index-buffer / set-vertex-buffer /
            // draw / draw-indexed / draw-indirect / draw-indexed-indirect
            // — identical wire form to gpu-render-pass-encoder. Routes
            // through host.RenderBundleEncoders + the bundle-encoder
            // IGpuRenderBundleEncoder methods.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]gpu-render-bundle-encoder.set-pipeline"),
                (_, selfH, pipeH) =>
                {
                    var enc = (IGpuRenderBundleEncoder)host.RenderBundleEncoders
                        .Get(selfH);
                    var pipe = (IGpuRenderPipeline)host.RenderPipelines.Get(pipeH);
                    enc.SetPipeline(pipe);
                });

            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, long, int, long>>(
                (Ns, "[method]gpu-render-bundle-encoder.set-index-buffer"),
                (_, selfH, bufH, fmt, offDisc, offVal, sizDisc, sizVal) =>
                {
                    var enc = (IGpuRenderBundleEncoder)host.RenderBundleEncoders
                        .Get(selfH);
                    var buf = (IGpuBuffer)host.Buffers.Get(bufH);
                    enc.SetIndexBuffer(buf,
                        (GpuIndexFormat)fmt,
                        offDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)offVal)),
                        sizDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)sizVal)));
                });

            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, int, long, int, long>>(
                (Ns, "[method]gpu-render-bundle-encoder.set-vertex-buffer"),
                (_, selfH, slot, bufDisc, bufVal,
                    offDisc, offVal, sizDisc, sizVal) =>
                {
                    var enc = (IGpuRenderBundleEncoder)host.RenderBundleEncoders
                        .Get(selfH);
                    enc.SetVertexBuffer(unchecked((uint)slot),
                        bufDisc == 0 ? Option<IGpuBuffer>.None
                            : Option<IGpuBuffer>.Some(
                                (IGpuBuffer)host.Buffers.Get(bufVal)),
                        offDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)offVal)),
                        sizDisc == 0 ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)sizVal)));
                });

            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-render-bundle-encoder.draw"),
                (_, selfH, vc,
                    instDisc, instVal, firstVDisc, firstVVal,
                    firstIDisc, firstIVal) =>
                {
                    var enc = (IGpuRenderBundleEncoder)host.RenderBundleEncoders
                        .Get(selfH);
                    enc.Draw(unchecked((uint)vc),
                        instDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)instVal)),
                        firstVDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)firstVVal)),
                        firstIDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)firstIVal)));
                });

            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu-render-bundle-encoder.draw-indexed"),
                (_, selfH, ic,
                    instDisc, instVal, firstIDisc, firstIVal,
                    baseVDisc, baseVVal, firstInDisc, firstInVal) =>
                {
                    var enc = (IGpuRenderBundleEncoder)host.RenderBundleEncoders
                        .Get(selfH);
                    enc.DrawIndexed(unchecked((uint)ic),
                        instDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)instVal)),
                        firstIDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)firstIVal)),
                        baseVDisc == 0 ? Option<int>.None
                            : Option<int>.Some(baseVVal),
                        firstInDisc == 0 ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)firstInVal)));
                });

            runtime.BindHostFunction<Action<ExecContext, int, int, long>>(
                (Ns, "[method]gpu-render-bundle-encoder.draw-indirect"),
                (_, selfH, bufH, off) =>
                {
                    var enc = (IGpuRenderBundleEncoder)host.RenderBundleEncoders
                        .Get(selfH);
                    var buf = (IGpuBuffer)host.Buffers.Get(bufH);
                    enc.DrawIndirect(buf, unchecked((ulong)off));
                });

            runtime.BindHostFunction<Action<ExecContext, int, int, long>>(
                (Ns, "[method]gpu-render-bundle-encoder.draw-indexed-indirect"),
                (_, selfH, bufH, off) =>
                {
                    var enc = (IGpuRenderBundleEncoder)host.RenderBundleEncoders
                        .Get(selfH);
                    var buf = (IGpuBuffer)host.Buffers.Get(bufH);
                    enc.DrawIndexedIndirect(buf, unchecked((ulong)off));
                });

            BindDebugMarkers(runtime, alloc, "gpu-render-bundle-encoder",
                host.RenderBundleEncoders,
                h => ((IGpuRenderBundleEncoder)h).PushDebugGroup,
                h => ((IGpuRenderBundleEncoder)h).PopDebugGroup,
                h => ((IGpuRenderBundleEncoder)h).InsertDebugMarker);

            BindLabeled(runtime, alloc,
                "gpu-render-bundle-encoder", host.RenderBundleEncoders,
                h => ((IGpuRenderBundleEncoder)h).Label(),
                (h, s) => ((IGpuRenderBundleEncoder)h).SetLabel(s));

            BindSetBindGroup(runtime, alloc,
                "gpu-render-bundle-encoder",
                host.RenderBundleEncoders, host.BindGroups,
                (instance, args) =>
                    ((IGpuRenderBundleEncoder)instance).SetBindGroup(
                        args.index, args.bindGroup, args.dynamicOffsets,
                        args.dynamicOffsetsStart, args.dynamicOffsetsLength));
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

        // Shared `set-bind-group(index, opt<borrow<bg>>,
        // opt<list<u32>> dynamic-offsets, opt<u64>, opt<u32>) ->
        // result<_, set-bind-group-error>` across the three
        // encoders (compute-pass, render-pass, render-bundle).
        // Identical wire form; only the impl callback differs.
        //
        // Wire (Action arity 13, all i32 except offset-start i64):
        //   self, index, bgDisc, bgVal, listDisc, listPtr, listLen,
        //   offDisc, offVal(i64), lenDisc, lenVal, retArea
        internal struct BindGroupArgs
        {
            public uint index;
            public Option<IGpuBindGroup> bindGroup;
            public Option<uint[]> dynamicOffsets;
            public Option<ulong> dynamicOffsetsStart;
            public Option<uint> dynamicOffsetsLength;
        }

        private static void BindSetBindGroup(WasmRuntime runtime,
            Wacs.WASI.GFX.HostBinding.Realloc alloc,
            string resourceName,
            Wacs.WASI.GFX.HostBinding.ResourceTable encoderTable,
            Wacs.WASI.GFX.HostBinding.ResourceTable bindGroupTable,
            Func<object, BindGroupArgs,
                Result<Unit, Webgpu.SetBindGroupError>> invoke)
        {
            runtime.BindHostFunction<Action<ExecContext,
                int, int, int, int, int, int, int, int, long, int, int, int>>(
                (Ns, "[method]" + resourceName + ".set-bind-group"),
                (ctx, selfH, index,
                    bgDisc, bgVal,
                    listDisc, listPtr, listLen,
                    offDisc, offVal,
                    lenDisc, lenVal,
                    retArea) =>
                {
                    var enc = encoderTable.Get(selfH);
                    var args = new BindGroupArgs
                    {
                        index = unchecked((uint)index),
                        bindGroup = bgDisc == 0
                            ? Option<IGpuBindGroup>.None
                            : Option<IGpuBindGroup>.Some(
                                (IGpuBindGroup)bindGroupTable.Get(bgVal)),
                        dynamicOffsets = listDisc == 0
                            ? Option<uint[]>.None
                            : Option<uint[]>.Some(
                                ReadU32List(ctx, listPtr, listLen)),
                        dynamicOffsetsStart = offDisc == 0
                            ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)offVal)),
                        dynamicOffsetsLength = lenDisc == 0
                            ? Option<uint>.None
                            : Option<uint>.Some(unchecked((uint)lenVal)),
                    };
                    Result<Unit, Webgpu.SetBindGroupError> result;
                    try { result = invoke(enc, args); }
                    catch (NotImplementedException)
                    {
                        // Backend hasn't wired set-bind-group yet
                        // — surface as a synthetic Range arm so
                        // the guest sees a non-trap failure rather
                        // than a thrown exception escaping the
                        // host function. Once a real backend lands,
                        // the impl returns Ok/Err directly.
                        result = Result<Unit, Webgpu.SetBindGroupError>
                            .FromErr(new Webgpu.SetBindGroupError
                            {
                                Kind = new SetBindGroupErrorKind
                                    .SetBindGroupErrorKindRangeError(),
                                Message = "host backend hasn't "
                                    + "implemented set-bind-group",
                            });
                    }
                    WriteResultUnitErrRecord(ctx, alloc, retArea,
                        result.IsOk,
                        result.IsErr
                            ? SetBindGroupErrorKindDisc(result.Err!.Kind)
                            : 0,
                        result.IsErr ? result.Err!.Message : "");
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
            var (ptr, len) = WriteUtf8Bytes(ctx, alloc, value);
            ctx.WriteI32LE(retArea, ptr);
            ctx.WriteI32LE(retArea + 4, len);
        }

        // Encode `value` as UTF-8 + allocate guest memory; return
        // the (ptr, len) pair. Caller writes the pair wherever it
        // belongs in retArea — useful when the (ptr, len) lands
        // inside a larger record payload, not at the retArea root.
        private static (int ptr, int len) WriteUtf8Bytes(
            ExecContext ctx,
            Wacs.WASI.GFX.HostBinding.Realloc alloc, string value)
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
            return (ptr, bytes.Length);
        }

        // ====================================================
        //   result<unit, error-record> retArea writer
        //   ----------------------------------------------------
        //   Every wasi:webgpu `result<_, foo-error>` has the same
        //   payload shape — the error is a record
        //   `{ kind: variant-no-payload, message: string }`.
        //   Storage layout at retArea (16 bytes, align 4):
        //     disc:u8 @0 + 3 pad
        //     payload @4:
        //       (if disc=0 / Ok) unused, zero
        //       (if disc=1 / Err) error-record @4:
        //         kind:u8 @4 + 3 pad
        //         msg.ptr:i32 @8
        //         msg.len:i32 @12
        // ====================================================

        // result<list<u8>, error-record> retArea (16 bytes, align 4):
        //   disc:u8 @0 + 3 pad
        //   payload @4:
        //     if Ok:  list<u8> ptr:i32 @4 + len:i32 @8 (4 bytes pad)
        //     if Err: kind:u8 @4 + 3 pad + msg.ptr @8 + msg.len @12
        // Same total size as the Unit/handle variants (max payload
        // = 12 bytes).
        private static void WriteResultByteArrayErrRecord(ExecContext ctx,
            Wacs.WASI.GFX.HostBinding.Realloc alloc, int retArea,
            Result<byte[], Webgpu.GetMappedRangeError> result)
        {
            var mem = ctx.Memory();
            if (result.IsOk)
            {
                mem[retArea] = 0;
                mem[retArea + 1] = 0;
                mem[retArea + 2] = 0;
                mem[retArea + 3] = 0;
                var bytes = result.Ok ?? Array.Empty<byte>();
                int ptr = bytes.Length == 0 ? 0
                    : alloc.Allocate(1, bytes.Length);
                if (bytes.Length > 0)
                {
                    for (int i = 0; i < bytes.Length; i++)
                        mem[ptr + i] = bytes[i];
                }
                ctx.WriteI32LE(retArea + 4, ptr);
                ctx.WriteI32LE(retArea + 8, bytes.Length);
                mem[retArea + 12] = 0;
                mem[retArea + 13] = 0;
                mem[retArea + 14] = 0;
                mem[retArea + 15] = 0;
                return;
            }
            mem[retArea] = 1;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            mem[retArea + 4] = (byte)GetMappedRangeErrorKindDisc(
                result.Err!.Kind);
            mem[retArea + 5] = 0;
            mem[retArea + 6] = 0;
            mem[retArea + 7] = 0;
            var (msgPtr, msgLen) = WriteUtf8Bytes(ctx, alloc,
                result.Err.Message);
            ctx.WriteI32LE(retArea + 8, msgPtr);
            ctx.WriteI32LE(retArea + 12, msgLen);
        }

        // result<option<own<R>>, error-record> retArea
        // (16 bytes, align 4). Used by pop-error-scope.
        //   outer disc:u8 @0 + 3 pad
        //   payload @4:
        //     if outer=Ok:
        //       inner option<own<R>>:
        //         inner disc:u8 @4 + 3 pad
        //         handle:i32 @8 (only valid when inner=Some)
        //     if outer=Err:
        //       kind:u8 @4 + 3 pad + msg.ptr @8 + msg.len @12
        private static void WriteResultOptionHandleErrRecord(
            ExecContext ctx,
            Wacs.WASI.GFX.HostBinding.Realloc alloc, int retArea,
            Result<Option<Webgpu.IGpuError>,
                Webgpu.PopErrorScopeError> result)
        {
            var mem = ctx.Memory();
            if (result.IsOk)
            {
                mem[retArea] = 0;
                mem[retArea + 1] = 0;
                mem[retArea + 2] = 0;
                mem[retArea + 3] = 0;
                var inner = result.Ok;
                if (inner.HasValue)
                {
                    mem[retArea + 4] = 1; // Some
                    mem[retArea + 5] = 0;
                    mem[retArea + 6] = 0;
                    mem[retArea + 7] = 0;
                    // Stub side: no Errors table yet; nest the
                    // error handle into a future host.Errors slot.
                    // For now there's no impl producing IGpuError,
                    // so this branch is reachable only by future
                    // backends.
                    // Allocate via reflection-friendly path:
                    ctx.WriteI32LE(retArea + 8,
                        AllocateGpuErrorHandle(
                            (object)(inner.Value)));
                }
                else
                {
                    mem[retArea + 4] = 0;
                    for (int i = 5; i < 16; i++) mem[retArea + i] = 0;
                }
                return;
            }
            mem[retArea] = 1;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            mem[retArea + 4] = (byte)PopErrorScopeErrorKindDisc(
                result.Err!.Kind);
            mem[retArea + 5] = 0;
            mem[retArea + 6] = 0;
            mem[retArea + 7] = 0;
            var (msgPtr, msgLen) = WriteUtf8Bytes(ctx, alloc,
                result.Err.Message);
            ctx.WriteI32LE(retArea + 8, msgPtr);
            ctx.WriteI32LE(retArea + 12, msgLen);
        }

        // Bridge for IGpuError → host.Errors table. Closure-
        // captured during Bind so the writer above can stay
        // independent of the host instance.
        private static System.Func<object, int>? _allocateGpuError;
        private static int AllocateGpuErrorHandle(object gpuError)
        {
            if (_allocateGpuError == null)
                throw new System.InvalidOperationException(
                    "AllocateGpuErrorHandle invoked before the "
                    + "BindGpuDevice plumbing set the closure. "
                    + "This indicates the binding registration "
                    + "order changed unexpectedly.");
            return _allocateGpuError(gpuError);
        }

        private static int PopErrorScopeErrorKindDisc(
            Webgpu.PopErrorScopeErrorKind k)
            => k switch
            {
                Webgpu.PopErrorScopeErrorKind
                    .PopErrorScopeErrorKindOperationError => 0,
                _ => 0,
            };

        private static void WriteResultUnitErrRecord(ExecContext ctx,
            Wacs.WASI.GFX.HostBinding.Realloc alloc, int retArea,
            bool isOk, int kindDisc, string message)
        {
            var mem = ctx.Memory();
            if (isOk)
            {
                // Ok arm. Zero the 16-byte retArea — the disc
                // byte at @0 marks Ok; payload bytes are
                // undefined per canon-ABI but zero is the
                // deterministic choice consumers can rely on.
                for (int i = 0; i < 16; i++) mem[retArea + i] = 0;
                return;
            }
            // Err arm.
            mem[retArea] = 1;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            mem[retArea + 4] = (byte)kindDisc;
            mem[retArea + 5] = 0;
            mem[retArea + 6] = 0;
            mem[retArea + 7] = 0;
            var (msgPtr, msgLen) = WriteUtf8Bytes(ctx, alloc, message);
            ctx.WriteI32LE(retArea + 8, msgPtr);
            ctx.WriteI32LE(retArea + 12, msgLen);
        }

        // result<handle, error-record> retArea (16 bytes, align 4):
        //   disc:u8 @0 + 3 pad
        //   payload @4: max(handle=4, error-record=12) = 12 bytes
        //     if Ok:  handle:i32 @4 (4 bytes used, 8 trailing pad)
        //     if Err: kind:u8 @4 + 3 pad + msg.ptr @8 + msg.len @12
        // Same shape as the Unit variant — Ok arm just writes a
        // handle at +4 instead of zeros.
        private static void WriteResultHandleErrRecord(ExecContext ctx,
            Wacs.WASI.GFX.HostBinding.Realloc alloc, int retArea,
            bool isOk, int handle, int kindDisc, string message)
        {
            var mem = ctx.Memory();
            mem[retArea] = (byte)(isOk ? 0 : 1);
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            if (isOk)
            {
                ctx.WriteI32LE(retArea + 4, handle);
                // Zero trailing 8 bytes of the 12-byte payload
                // area for determinism.
                for (int i = 8; i < 16; i++) mem[retArea + i] = 0;
                return;
            }
            mem[retArea + 4] = (byte)kindDisc;
            mem[retArea + 5] = 0;
            mem[retArea + 6] = 0;
            mem[retArea + 7] = 0;
            var (msgPtr, msgLen) = WriteUtf8Bytes(ctx, alloc, message);
            ctx.WriteI32LE(retArea + 8, msgPtr);
            ctx.WriteI32LE(retArea + 12, msgLen);
        }

        // Per-error-type kind→disc mappings. The WIT variants
        // have only no-payload cases, so the discriminant matches
        // declaration order. Each error type's case set differs;
        // the source-gen represents the variant as a base class
        // with one sealed subclass per case, so we identify cases
        // via type checks.

        private static int UnmapErrorKindDisc(UnmapErrorKind k)
            => k switch
            {
                Webgpu.UnmapErrorKind.UnmapErrorKindAbortError => 0,
                _ => 0,
            };

        private static int MapAsyncErrorKindDisc(MapAsyncErrorKind k)
            => k switch
            {
                Webgpu.MapAsyncErrorKind.MapAsyncErrorKindOperationError => 0,
                Webgpu.MapAsyncErrorKind.MapAsyncErrorKindRangeError => 1,
                Webgpu.MapAsyncErrorKind.MapAsyncErrorKindAbortError => 2,
                _ => 0,
            };

        private static int WriteBufferErrorKindDisc(WriteBufferErrorKind k)
            => k switch
            {
                Webgpu.WriteBufferErrorKind.WriteBufferErrorKindOperationError => 0,
                _ => 0,
            };

        private static int SetBindGroupErrorKindDisc(SetBindGroupErrorKind k)
            => k switch
            {
                Webgpu.SetBindGroupErrorKind.SetBindGroupErrorKindRangeError => 0,
                _ => 0,
            };

        private static int GetMappedRangeErrorKindDisc(GetMappedRangeErrorKind k)
            => k switch
            {
                Webgpu.GetMappedRangeErrorKind.GetMappedRangeErrorKindOperationError => 0,
                Webgpu.GetMappedRangeErrorKind.GetMappedRangeErrorKindRangeError => 1,
                Webgpu.GetMappedRangeErrorKind.GetMappedRangeErrorKindTypeError => 2,
                _ => 0,
            };

        private static int RequestDeviceErrorKindDisc(RequestDeviceErrorKind k)
            => k switch
            {
                Webgpu.RequestDeviceErrorKind.RequestDeviceErrorKindTypeError => 0,
                Webgpu.RequestDeviceErrorKind.RequestDeviceErrorKindOperationError => 1,
                _ => 0,
            };

        private static int CreateQuerySetErrorKindDisc(CreateQuerySetErrorKind k)
            => k switch
            {
                Webgpu.CreateQuerySetErrorKind.CreateQuerySetErrorKindTypeError => 0,
                _ => 0,
            };

        // create-pipeline-error has a payload-bearing variant kind
        // (gpu-pipeline-error(gpu-pipeline-error-reason)) — return
        // the inner enum value directly so the helper can write both
        // the case-disc (always 0 — one case) and the payload at the
        // appropriate retArea slots.
        private static int CreatePipelineErrorReasonDisc(
            Webgpu.CreatePipelineErrorKind k)
        {
            if (k is Webgpu.CreatePipelineErrorKind.CreatePipelineErrorKindGpuPipelineError g)
                return (int)g.Value;
            return 0;
        }

        // result<own<pipeline>, create-pipeline-error> retArea layout
        // (align 4, size 20):
        //   @0  outer disc:u8 + 3 pad
        //   Ok arm:
        //     @4  handle:i32
        //     @8..@19 zero
        //   Err arm (record{kind:variant{case0(enum)}, message:string}):
        //     @4  kind.case-disc:u8 + 3 pad  (always 0; 1 case)
        //     @8  kind.payload(enum):u32
        //     @12 message.ptr:i32
        //     @16 message.len:i32
        private static void WriteResultHandleErrPipelineRecord(
            ExecContext ctx,
            Wacs.WASI.GFX.HostBinding.Realloc alloc,
            int retArea,
            bool isOk, int handle, int reason, string message)
        {
            var mem = ctx.Memory();
            mem[retArea] = (byte)(isOk ? 0 : 1);
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            if (isOk)
            {
                ctx.WriteI32LE(retArea + 4, handle);
                for (int i = 8; i < 20; i++) mem[retArea + i] = 0;
                return;
            }
            // Single variant case → case-disc is always 0.
            mem[retArea + 4] = 0;
            mem[retArea + 5] = 0;
            mem[retArea + 6] = 0;
            mem[retArea + 7] = 0;
            ctx.WriteI32LE(retArea + 8, reason);
            var (msgPtr, msgLen) = WriteUtf8Bytes(ctx, alloc, message);
            ctx.WriteI32LE(retArea + 12, msgPtr);
            ctx.WriteI32LE(retArea + 16, msgLen);
        }

        // Decode a list<u8> from guest memory into a host byte[].
        // The wire shape for list<u8> params is (ptr, len) pairs
        // already on the stack; this helper just resolves the
        // pointer and copies. Used by queue.write-buffer-with-
        // copy + buffer.get-mapped-range-set-with-copy where the
        // host side wants the bytes for either inspection or
        // forwarding.
        private static byte[] ReadByteArray(ExecContext ctx, int ptr, int len)
        {
            if (len <= 0) return Array.Empty<byte>();
            var mem = ctx.Memory();
            var arr = new byte[len];
            for (int i = 0; i < len; i++) arr[i] = mem[ptr + i];
            return arr;
        }

        // Decode a list<u32> at (ptr, count). 4 bytes per element.
        // Used by set-bind-group's dynamic-offsets parameter.
        private static uint[] ReadU32List(ExecContext ctx, int ptr, int count)
        {
            if (count <= 0) return Array.Empty<uint>();
            var arr = new uint[count];
            for (int i = 0; i < count; i++)
                arr[i] = unchecked((uint)ctx.ReadI32LE(ptr + i * 4));
            return arr;
        }

        // ============================================================
        //   Canonical-ABI in-memory decoders for list<record>
        //   parameters that the binding receives as (ptr, len).
        //   The byte layouts are derived from the WIT records and
        //   the Component Model canonical-ABI alignment rules:
        //     align(record) = max(align of fields, 1)
        //     size(record)  = aligned-up(sum of fields with padding)
        //     option<T>: align = max(1, align(T));
        //                size  = align + size(T)
        //     variant   : align = max align of payloads;
        //                 size  = align + max size of payloads
        // ============================================================

        // gpu-bind-group-layout-entry record layout (size=56, align=8).
        // All gpu-* enums in wasi:webgpu have ≤256 cases so they
        // encode as u8 (canonical-ABI discriminant_type rule):
        //   @0..3   binding             u32
        //   @4..7   visibility          u32 (shader-stage-flags)
        //   @8..39  buffer              option<buffer-binding-layout>
        //     @8        opt-disc:u8 (+ 7 pad to align 8)
        //     @16..39   bbl payload (size 24, align 8):
        //       @16     type.disc           (opt<enum-u8>)
        //       @17     type.val
        //       @18     has-dyn-off.disc    (opt<bool>)
        //       @19     has-dyn-off.val
        //       @24     min-binding-size.disc (opt<u64>; pad to align 8)
        //       @32     min-binding-size.val:u64
        //   @40..42 sampler             option<sampler-binding-layout>
        //     @40 opt-disc:u8 (sbl align 1)
        //     @41 sbl.type.disc, @42 sbl.type.val
        //   @43..49 texture             option<texture-binding-layout>
        //     @43 opt-disc:u8
        //     @44 sampleType.disc, @45 sampleType.val
        //     @46 viewDim.disc,    @47 viewDim.val
        //     @48 multisampled.disc, @49 multisampled.val
        //   @50..55 storage-texture    option<storage-texture-binding-layout>
        //     @50 opt-disc:u8
        //     @51 access.disc, @52 access.val
        //     @53 format.val (u8 enum, required, no disc)
        //     @54 viewDim.disc, @55 viewDim.val
        private static Webgpu.GpuBindGroupLayoutEntry
            ReadBindGroupLayoutEntry(ExecContext ctx, int ptr)
        {
            var mem = ctx.Memory();
            var entry = new Webgpu.GpuBindGroupLayoutEntry
            {
                Binding = unchecked((uint)ctx.ReadI32LE(ptr + 0)),
                Visibility = unchecked((uint)ctx.ReadI32LE(ptr + 4)),
                Buffer = Option<Webgpu.GpuBufferBindingLayout>.None,
                Sampler = Option<Webgpu.GpuSamplerBindingLayout>.None,
                Texture = Option<Webgpu.GpuTextureBindingLayout>.None,
                StorageTexture = Option<Webgpu.GpuStorageTextureBindingLayout>.None,
            };
            if (mem[ptr + 8] != 0)
            {
                var bbl = new Webgpu.GpuBufferBindingLayout
                {
                    Type = mem[ptr + 16] == 0
                        ? Option<Webgpu.GpuBufferBindingType>.None
                        : Option<Webgpu.GpuBufferBindingType>.Some(
                            (Webgpu.GpuBufferBindingType)mem[ptr + 17]),
                    HasDynamicOffset = mem[ptr + 18] == 0
                        ? Option<bool>.None
                        : Option<bool>.Some(mem[ptr + 19] != 0),
                    MinBindingSize = mem[ptr + 24] == 0
                        ? Option<ulong>.None
                        : Option<ulong>.Some(unchecked((ulong)ctx.ReadI64LE(ptr + 32))),
                };
                entry.Buffer = Option<Webgpu.GpuBufferBindingLayout>.Some(bbl);
            }
            if (mem[ptr + 40] != 0)
            {
                var sbl = new Webgpu.GpuSamplerBindingLayout
                {
                    Type = mem[ptr + 41] == 0
                        ? Option<Webgpu.GpuSamplerBindingType>.None
                        : Option<Webgpu.GpuSamplerBindingType>.Some(
                            (Webgpu.GpuSamplerBindingType)mem[ptr + 42]),
                };
                entry.Sampler = Option<Webgpu.GpuSamplerBindingLayout>.Some(sbl);
            }
            if (mem[ptr + 43] != 0)
            {
                var tbl = new Webgpu.GpuTextureBindingLayout
                {
                    SampleType = mem[ptr + 44] == 0
                        ? Option<Webgpu.GpuTextureSampleType>.None
                        : Option<Webgpu.GpuTextureSampleType>.Some(
                            (Webgpu.GpuTextureSampleType)mem[ptr + 45]),
                    ViewDimension = mem[ptr + 46] == 0
                        ? Option<Webgpu.GpuTextureViewDimension>.None
                        : Option<Webgpu.GpuTextureViewDimension>.Some(
                            (Webgpu.GpuTextureViewDimension)mem[ptr + 47]),
                    Multisampled = mem[ptr + 48] == 0
                        ? Option<bool>.None
                        : Option<bool>.Some(mem[ptr + 49] != 0),
                };
                entry.Texture = Option<Webgpu.GpuTextureBindingLayout>.Some(tbl);
            }
            if (mem[ptr + 50] != 0)
            {
                var stbl = new Webgpu.GpuStorageTextureBindingLayout
                {
                    Access = mem[ptr + 51] == 0
                        ? Option<Webgpu.GpuStorageTextureAccess>.None
                        : Option<Webgpu.GpuStorageTextureAccess>.Some(
                            (Webgpu.GpuStorageTextureAccess)mem[ptr + 52]),
                    Format = (Webgpu.GpuTextureFormat)mem[ptr + 53],
                    ViewDimension = mem[ptr + 54] == 0
                        ? Option<Webgpu.GpuTextureViewDimension>.None
                        : Option<Webgpu.GpuTextureViewDimension>.Some(
                            (Webgpu.GpuTextureViewDimension)mem[ptr + 55]),
                };
                entry.StorageTexture = Option<Webgpu.GpuStorageTextureBindingLayout>.Some(stbl);
            }
            return entry;
        }

        // gpu-bind-group-entry record layout (size=56, align=8):
        //   @0..3   : binding   u32
        //   @4..7   : (pad to align 8 for the variant)
        //   @8..55  : resource  variant (align 8, size 48):
        //     @8        disc:u8 (+ 7 pad)
        //     @16..55   payload (40 bytes max — gpu-buffer-binding):
        //       disc 0 (gpu-buffer-binding record, size 40, align 8):
        //         @16..19  buffer u32 handle
        //         @20..23  (pad)
        //         @24..39  offset option<u64>  (disc@24, val@32)
        //         @40..55  size   option<u64>  (disc@40, val@48)
        //       disc 1 (gpu-sampler borrow): @16..19 = u32 handle
        //       disc 2 (gpu-texture-view borrow): @16..19 = u32 handle
        private static Webgpu.GpuBindGroupEntry ReadBindGroupEntry(
            ExecContext ctx, int ptr, WasiWebgpuHost host)
        {
            var mem = ctx.Memory();
            var binding = unchecked((uint)ctx.ReadI32LE(ptr + 0));
            int disc = mem[ptr + 8];
            Webgpu.GpuBindingResource resource;
            switch (disc)
            {
                case 0:
                    var bufH = ctx.ReadI32LE(ptr + 16);
                    var offDisc = mem[ptr + 24];
                    var sizeDisc = mem[ptr + 40];
                    var bb = new Webgpu.GpuBufferBinding
                    {
                        Buffer = (IGpuBuffer)host.Buffers.Get(bufH),
                        Offset = offDisc == 0
                            ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)ctx.ReadI64LE(ptr + 32))),
                        Size = sizeDisc == 0
                            ? Option<ulong>.None
                            : Option<ulong>.Some(unchecked((ulong)ctx.ReadI64LE(ptr + 48))),
                    };
                    resource = new Webgpu.GpuBindingResource
                        .GpuBindingResourceGpuBufferBinding(bb);
                    break;
                case 1:
                    var sampH = ctx.ReadI32LE(ptr + 16);
                    resource = new Webgpu.GpuBindingResource
                        .GpuBindingResourceGpuSampler(
                            (IGpuSampler)host.Samplers.Get(sampH));
                    break;
                case 2:
                    var tvH = ctx.ReadI32LE(ptr + 16);
                    resource = new Webgpu.GpuBindingResource
                        .GpuBindingResourceGpuTextureView(
                            (IGpuTextureView)host.TextureViews.Get(tvH));
                    break;
                default:
                    throw new Wacs.WASI.GFX.Types.WasiGfxException(
                        "ReadBindGroupEntry: unexpected variant disc="
                        + disc + " (entry binding=" + binding + ").");
            }
            return new Webgpu.GpuBindGroupEntry
            {
                Binding = binding,
                Resource = resource,
            };
        }
    }
}
