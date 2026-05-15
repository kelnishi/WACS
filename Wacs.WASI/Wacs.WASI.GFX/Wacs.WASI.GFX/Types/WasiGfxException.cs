// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.GFX.Types
{
    /// <summary>
    /// Runtime failures inside a wasi-gfx backend. The binding
    /// layer catches this and lowers it to the right
    /// canonical-ABI representation for the call site (most
    /// wasi-gfx methods today don't return result&lt;_, _&gt;
    /// — failures trap the guest. The proposal is still
    /// Phase 2, error-modeling will tighten as it advances.)
    /// </summary>
    public sealed class WasiGfxException : Exception
    {
        public WasiGfxException(string message) : base(message) { }
        public WasiGfxException(string message, Exception inner)
            : base(message, inner) { }
    }
}
