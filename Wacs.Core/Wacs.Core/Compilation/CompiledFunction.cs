// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Runtime;
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

        /// <summary>Parameter count — cached from <see cref="Signature"/> so the Call hot
        /// path doesn't dereference <c>Signature.ParameterTypes.Arity</c> per invocation.</summary>
        public readonly int ParamCount;

        /// <summary>
        /// Pre-initialized locals buffer. Slots <c>[0..ParamCount)</c> are zero (overwritten
        /// with popped args at call time); slots <c>[ParamCount..LocalsCount)</c> hold the
        /// per-type default <c>Value</c> for each declared local. Call-site initialization
        /// becomes a single <c>Array.Copy(DefaultLocalsTemplate, 0, rented, 0, LocalsCount)</c>
        /// plus the arg-pop loop — no per-slot <c>new Value(ValType)</c> construction.
        /// Null when <c>LocalsCount == 0</c>.
        /// </summary>
        public readonly Value[]? DefaultLocalsTemplate;

        /// <summary>Function signature — used by the invocation glue to marshal args/results.</summary>
        public readonly FunctionType Signature;

        /// <summary>
        /// Exception-handler sidecar table. Empty for functions without try_table. On
        /// throw, the runtime scans this table for an entry whose pc-range covers the
        /// current pc; the innermost match (scanned reverse-order) wins.
        /// </summary>
        public readonly HandlerEntry[] HandlerTable;

        public CompiledFunction(byte[] bytecode, int localsCount, FunctionType signature,
                                HandlerEntry[] handlerTable,
                                Value[]? defaultLocalsTemplate = null)
        {
            Bytecode = bytecode;
            LocalsCount = localsCount;
            ParamCount = signature?.ParameterTypes.Arity ?? 0;
            Signature = signature;
            HandlerTable = handlerTable;
            DefaultLocalsTemplate = defaultLocalsTemplate;
        }
    }
}
