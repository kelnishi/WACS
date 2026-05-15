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

namespace Wacs.WASI.GFX.Webgpu
{
    /// <summary>
    /// Hand-written canonical-ABI host-function dispatcher for the
    /// <c>wasi:webgpu@0.0.1</c> imports. Same shape as
    /// <c>WACS.WASI.GFX.WitBindings</c> — one
    /// <c>runtime.BindHostFunction</c> call per WIT method,
    /// keyed on the wire-form <c>(module, entity)</c> pair.
    ///
    /// <para>v1 phase 3 session 2 ships the entry-point stub only:
    /// <see cref="Bind"/> exists but registers no bindings yet.
    /// The interpreter path therefore reports "missing handler"
    /// for any wasi:webgpu import; the transpiler-direct-link
    /// path is gated behind
    /// <see cref="Wacs.Transpiler.AOT.Component.DirectLinkedImportEmit.CanEmitDirect"/>
    /// which falls back to the legacy delegate dispatch on
    /// missing handlers. Sessions 3-6 fill in the per-resource
    /// bindings incrementally.</para>
    /// </summary>
    internal static class WitBindings
    {
        // wasi:webgpu spans 9 shape-stable wasi:io point releases
        // (the IoBindings multi-version registration covers them);
        // wasi:webgpu itself only pins @0.0.1 so the module string
        // is constant.
        internal const string Ns = "wasi:webgpu/webgpu@0.0.1";

        public static void Bind(WasmRuntime runtime, WasiWebgpuHost host)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (host.Backend == null)
                throw new WasiGfxException(
                    "wasi-webgpu WitBindings.Bind invoked without "
                    + "a backend on the host's configuration.");
            // No per-resource bindings yet — session 3 onwards.
            // Drop methods land alongside the first session's
            // resource (gpu), then expand outward.
        }
    }
}
