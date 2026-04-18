// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Types;

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// The annotated-bytecode form of a WASM function body, produced once at instantiation
    /// time by <see cref="BytecodeCompiler"/> and then executed by <see cref="SwitchRuntime"/>
    /// with zero per-opcode allocation.
    ///
    /// The stream inlines every immediate at a fixed width (no LEB128 at runtime) and —
    /// once control flow is fully wired — pre-resolves branch targets to absolute stream
    /// offsets. See the plan file for the full format spec.
    /// </summary>
    public sealed class CompiledFunction
    {
        /// <summary>The annotated opcode + immediates stream.</summary>
        public readonly byte[] Bytecode;

        /// <summary>Total number of locals the frame must provision (params + declared locals).</summary>
        public readonly int LocalsCount;

        /// <summary>Function signature — used by the invocation glue to marshal args/results.</summary>
        public readonly FunctionType Signature;

        public CompiledFunction(byte[] bytecode, int localsCount, FunctionType signature)
        {
            Bytecode = bytecode;
            LocalsCount = localsCount;
            Signature = signature;
        }
    }
}
