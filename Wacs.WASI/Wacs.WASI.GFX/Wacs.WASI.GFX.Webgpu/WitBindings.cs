// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.GFX.HostBinding;
using Wacs.WASI.GFX.Types;
using IGpu = Wacs.WASI.GFX.Webgpu.Webgpu.IGpu;
using IWgslLanguageFeatures = Wacs.WASI.GFX.Webgpu.Webgpu.IWgslLanguageFeatures;

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
            BindGpu(runtime, host);
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

            // request-adapter + the option<record> param + option<handle>
            // return wiring lands in session 4 alongside the gpu-adapter
            // resource (session 4's natural pairing). Stubbed handler
            // for now so callers reach a clear NotImplementedException
            // instead of "missing host function" hangs.
            // [method]gpu.request-adapter(self, options: option<gpu-request-adapter-options>)
            //   -> option<gpu-adapter>
            // Wire (aggregate retArea):
            //   (self:i32,
            //    opt_disc:i32,
            //      power_pref_disc:i32, power_pref_val:i32,
            //      force_fallback:i32, xr_compatible:i32,
            //    retArea:i32) -> 0 results
            // retArea layout: 8 bytes (disc i32 @0, handle i32 @4)
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int, int, int, int>>(
                (Ns, "[method]gpu.request-adapter"),
                (_, _self, _od, _ppd, _ppv, _ff, _xr, _ra) =>
                {
                    throw new NotImplementedException(
                        "wasi:webgpu [method]gpu.request-adapter — "
                        + "lands in v1 phase 3 session 4 alongside "
                        + "gpu-adapter binding.");
                });
        }
    }
}
