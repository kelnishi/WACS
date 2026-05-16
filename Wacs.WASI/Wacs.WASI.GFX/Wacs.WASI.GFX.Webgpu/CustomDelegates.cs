// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Runtime;

namespace Wacs.WASI.GFX.Webgpu
{
    /// <summary>
    /// Custom delegate types for wasi:webgpu host functions with
    /// arity above .NET's built-in <see cref="System.Action"/> /
    /// <see cref="System.Func{TResult}"/> ceiling (16 generic
    /// parameters). The runtime's
    /// <c>WasmRuntime.BindHostFunction&lt;TDelegate&gt;</c>
    /// constraint accepts any <c>Delegate</c> subtype, so a
    /// purpose-built delegate slots in cleanly.
    ///
    /// <para>v1 phase 3 result&lt;_, error&gt; / custom-arity
    /// follow-up: gpu-texture.create-view's
    /// <c>option&lt;gpu-texture-view-descriptor&gt;</c> has 9
    /// option fields (8 option&lt;enum/u32&gt; + 1
    /// option&lt;string&gt;), flattening to 20 i32. Plus the
    /// self handle and the i32 return, that's a 22-parameter
    /// callable. Future gpu-device.create-buffer descriptors with
    /// i64 fields use shapes defined alongside their binding.
    /// </para>
    /// </summary>
    internal static class CustomDelegates
    {
        // create-view shape: self + 20 i32 (option<record> flat-form)
        // + i32 return. ExecContext + 21 int → int.
        internal delegate int CreateView(ExecContext ctx,
            int a01, int a02, int a03, int a04, int a05, int a06, int a07,
            int a08, int a09, int a10, int a11, int a12, int a13, int a14,
            int a15, int a16, int a17, int a18, int a19, int a20, int a21);

        // request-device shape: self + 13 i32 (option<gpu-device-descriptor>)
        // + retArea — 15 i32 inputs, no result (aggregate retArea write).
        // ExecContext + 15 int.
        internal delegate void RequestDevice(ExecContext ctx,
            int a01, int a02, int a03, int a04, int a05, int a06, int a07,
            int a08, int a09, int a10, int a11, int a12, int a13, int a14,
            int a15);

        // create-texture shape: self + gpu-texture-descriptor flat (19 i32)
        // + i32 return handle.
        //   gpu-extent3-d flat = u32 + 2 opt<u32> = 5 i32
        //   + opt<u32> mip-level-count = 2 i32
        //   + opt<u32> sample-count   = 2 i32
        //   + opt<enum> dimension     = 2 i32
        //   + enum format             = 1 i32
        //   + u32 usage               = 1 i32
        //   + opt<list<enum>> view-formats = 3 i32 (disc, ptr, len)
        //   + opt<string> label       = 3 i32
        //   total record = 19 i32
        // ExecContext + 20 int → int.
        internal delegate int CreateTexture(ExecContext ctx,
            int a01, int a02, int a03, int a04, int a05, int a06, int a07,
            int a08, int a09, int a10, int a11, int a12, int a13, int a14,
            int a15, int a16, int a17, int a18, int a19, int a20);

        // create-sampler shape: self + option<gpu-sampler-descriptor>
        // (24 flat slots, mixed i32/f32) + i32 return handle.
        //   opt-disc                                              i32
        //   6 × opt<enum>      (2 i32 each)                       = 12 i32
        //   opt<f32> lod-min-clamp = disc:i32 + val:f32           = i32, f32
        //   opt<f32> lod-max-clamp = disc:i32 + val:f32           = i32, f32
        //   opt<enum> compare      (2 i32)                         = 2 i32
        //   opt<u16>  max-anisotropy (2 i32)                       = 2 i32
        //   opt<string> label      (3 i32)                         = 3 i32
        //   record-flat = 23 i32 + 2 f32 = 25 slots; option<that> = 26
        // ExecContext + (self + 25 mixed) + return = 27 type params.
        internal delegate int CreateSampler(ExecContext ctx,
            int self,
            int optDisc,
            int aud, int auv, int avd, int avv, int awd, int aww,
            int mfd, int mfv, int mnd, int mnv, int mid, int miv,
            int lminD, float lminV, int lmaxD, float lmaxV,
            int cmpD, int cmpV, int maD, int maV,
            int lblD, int lblPtr, int lblLen);

        // create-compute-pipeline-async shape: self + gpu-compute-pipeline-
        // descriptor flat (11 i32) + retArea i32.
        //   programmable-stage record flat:
        //     module: borrow                              1 i32
        //     entry-point: opt<string>                    3 i32
        //     constants: opt<own>                         2 i32
        //     = 6 i32
        //   layout-mode variant{specific(borrow)|auto}:   2 i32 (disc + max payload)
        //   label: opt<string>                            3 i32
        //   record total = 11 i32
        // 0 results. ExecContext + 13 int.
        internal delegate void CreateComputePipelineAsync(ExecContext ctx,
            int a01, int a02, int a03, int a04, int a05, int a06, int a07,
            int a08, int a09, int a10, int a11, int a12, int a13);
    }
}
