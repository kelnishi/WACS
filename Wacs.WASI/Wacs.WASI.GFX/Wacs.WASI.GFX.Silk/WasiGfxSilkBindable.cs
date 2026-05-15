// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Runtime;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Parameterless <see cref="IBindable"/> adapter for the
    /// CLI's <c>--wasi-gfx</c> / <c>--bind</c> path. The CLI
    /// resolves bindings by assembly name, activates every
    /// public parameterless-ctor <see cref="IBindable"/> type
    /// it finds, and calls <see cref="BindToRuntime"/>.
    /// Mirrors <c>WasiNNOnnxBindable</c> in shape.
    /// </summary>
    public sealed class WasiGfxSilkBindable : IBindable
    {
        public void BindToRuntime(WasmRuntime runtime)
        {
            runtime.UseWasiGfx(b => b.WithBackend(new SilkGfxBackend()));
        }
    }
}
