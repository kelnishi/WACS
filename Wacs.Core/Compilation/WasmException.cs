// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// Propagates a WASM exception through managed C# stack frames on the switch runtime.
    /// Throw handlers raise one; <see cref="SwitchRuntime.Run"/> catches at each frame,
    /// consults the current function's <see cref="HandlerEntry"/> table, and either
    /// resumes at a matching handler or rethrows to let the Call handler unwind one frame.
    /// </summary>
    public sealed class WasmException : Exception
    {
        public readonly ExnInstance Exn;

        public WasmException(ExnInstance exn) : base("wasm exception")
        {
            Exn = exn;
        }
    }
}
