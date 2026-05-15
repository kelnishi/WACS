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
    }
}
