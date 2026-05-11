// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// A single frame in a snapshot of the WASM-side call stack at the
    /// moment a trap / uncaught exception fires. Captured cheaply at
    /// throw time and rehydrated to a human-readable trace on demand.
    ///
    /// <para><see cref="FuncAddr"/> is the store-wide function address
    /// (use <c>store[addr]</c> to get the
    /// <see cref="FunctionInstance"/>, then <c>.Id</c> for a name).
    /// <see cref="Instruction"/> is the instruction that was executing
    /// in this frame when the trap fired — populated directly for the
    /// throwing frame (top of the chain) and resolved lazily for
    /// caller frames in <see cref="Wacs.Core.Runtime.Exceptions.WasmStackTrace"/>
    /// (Pass C).</para>
    ///
    /// <para><see cref="ResumeContinuationAddress"/> is the linker-
    /// assigned PC the caller frame will return to. Combined with the
    /// caller's bytecode, the formatter can recover the call-site
    /// instruction lazily.</para>
    /// </summary>
    public readonly struct WasmStackFrame
    {
        /// <summary>Store-wide function address for this frame.</summary>
        public readonly uint FuncAddr;

        /// <summary>
        /// The instruction that was executing in this frame at trap
        /// time. Non-null for the throwing frame (top of the chain);
        /// null for caller frames in the cheap-capture path (the
        /// formatter resolves them lazily from
        /// <see cref="ResumeContinuationAddress"/>).
        /// </summary>
        public readonly InstructionBase? Instruction;

        /// <summary>
        /// Linker PC the caller will return to once this frame
        /// finishes. Useful when <see cref="Instruction"/> is null —
        /// the formatter looks one step back from this address in
        /// the caller's function body to recover the call site.
        /// </summary>
        public readonly int ResumeContinuationAddress;

        public WasmStackFrame(
            uint funcAddr,
            InstructionBase? instruction,
            int resumeContinuationAddress)
        {
            FuncAddr = funcAddr;
            Instruction = instruction;
            ResumeContinuationAddress = resumeContinuationAddress;
        }

        public override string ToString() =>
            Instruction != null
                ? $"WasmStackFrame(func={FuncAddr}, inst={Instruction.Op.GetMnemonic()})"
                : $"WasmStackFrame(func={FuncAddr}, resume@{ResumeContinuationAddress})";
    }
}
