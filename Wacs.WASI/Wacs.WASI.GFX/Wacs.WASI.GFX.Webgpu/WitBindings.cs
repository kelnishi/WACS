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
using IGpuDevice = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuDevice;
using IWgslLanguageFeatures = Wacs.WASI.GFX.Webgpu.Webgpu.IWgslLanguageFeatures;
using GpuRequestAdapterOptions = Wacs.WASI.GFX.Webgpu.Webgpu.GpuRequestAdapterOptions;
using GpuDeviceDescriptor = Wacs.WASI.GFX.Webgpu.Webgpu.GpuDeviceDescriptor;

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
