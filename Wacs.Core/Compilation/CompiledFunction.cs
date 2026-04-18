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

        /// <summary>
        /// Exception-handler sidecar table. Empty for functions without try_table. On
        /// throw, the runtime scans this table for an entry whose pc-range covers the
        /// current pc; the innermost match (scanned reverse-order) wins.
        /// </summary>
        public readonly HandlerEntry[] HandlerTable;

        public CompiledFunction(byte[] bytecode, int localsCount, FunctionType signature,
                                HandlerEntry[] handlerTable)
        {
            Bytecode = bytecode;
            LocalsCount = localsCount;
            Signature = signature;
            HandlerTable = handlerTable;
        }
    }
}
