// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Transpiler.AOT.Emitters;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Per-function IL code generator. Walks the instruction sequence
    /// and emits CIL via ILGenerator.
    ///
    /// The emitter grows incrementally — each phase adds support for more
    /// instruction categories. Functions containing unsupported instructions
    /// fall back to the interpreter.
    /// </summary>
    public class FunctionCodegen
    {
        private readonly MethodBuilder _method;
        private readonly FunctionInstance _funcInst;
        private readonly MethodBuilder[] _siblingMethods;
        private readonly int _paramCount;

        public FunctionCodegen(
            MethodBuilder method,
            FunctionInstance funcInst,
            MethodBuilder[] siblingMethods)
        {
            _method = method;
            _funcInst = funcInst;
            _siblingMethods = siblingMethods;
            _paramCount = funcInst.Type.ParameterTypes.Arity;
        }

        /// <summary>
        /// Attempt to emit IL for this function.
        /// Returns true if all instructions were successfully emitted.
        /// Returns false if any instruction is unsupported, signaling fallback.
        /// </summary>
        public bool TryEmit()
        {
            // First pass: check if we can handle every instruction
            if (!CanEmitAllInstructions())
                return false;

            // Second pass: emit IL
            var il = _method.GetILGenerator();

            // Declare CIL locals for WASM locals (parameters are CIL args, not locals)
            var wasmLocals = _funcInst.Locals;
            var locals = new LocalBuilder[wasmLocals.Length];
            for (int i = 0; i < wasmLocals.Length; i++)
            {
                locals[i] = il.DeclareLocal(ModuleTranspiler.MapValType(wasmLocals[i]));
            }

            // Emit instructions
            foreach (var inst in _funcInst.Body.Instructions)
            {
                EmitInstruction(il, inst, locals);
            }

            return true;
        }

        private void EmitInstruction(ILGenerator il, InstructionBase inst, LocalBuilder[] locals)
        {
            // The ByteCode.x00 gives us the OpCode for single-byte instructions
            var op = inst.Op.x00;

            if (NumericEmitter.CanEmit(op))
            {
                NumericEmitter.Emit(il, inst, op);
                return;
            }

            if (VariableEmitter.CanEmit(op))
            {
                VariableEmitter.Emit(il, inst, op, _paramCount, locals);
                return;
            }

            switch (op)
            {
                case WasmOpCode.Nop:
                    il.Emit(OpCodes.Nop);
                    break;

                case WasmOpCode.End:
                    // Function end — emit ret
                    // For blocks/loops/ifs, End is handled by the control flow emitter (Phase 2)
                    // For the function-level End, we need ret
                    if (inst is InstEnd endInst && endInst.FunctionEnd)
                    {
                        il.Emit(OpCodes.Ret);
                    }
                    break;

                case WasmOpCode.Return:
                    il.Emit(OpCodes.Ret);
                    break;

                default:
                    throw new TranspilerException(
                        $"FunctionCodegen: no emitter for opcode {inst.Op.GetMnemonic()} (0x{(byte)op:X2})");
            }
        }

        /// <summary>
        /// Check if we have emitters for every instruction in this function.
        /// </summary>
        private bool CanEmitAllInstructions()
        {
            foreach (var inst in _funcInst.Body.Instructions)
            {
                if (!HasEmitter(inst.Op))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Check if we have an IL emitter for the given bytecode.
        /// </summary>
        private bool HasEmitter(ByteCode bc)
        {
            // Multi-byte opcodes (FB/FC/FD/FE prefix) — not yet supported
            var op = bc.x00;
            if (op == WasmOpCode.FB || op == WasmOpCode.FC || op == WasmOpCode.FD || op == WasmOpCode.FE)
                return false;

            // Numeric instructions (0x41-0xC4)
            if (NumericEmitter.CanEmit(op))
                return true;

            // Variable/parametric instructions
            if (VariableEmitter.CanEmit(op))
                return true;

            // Control flow basics
            if (op == WasmOpCode.Nop || op == WasmOpCode.End || op == WasmOpCode.Return)
                return true;

            return false;
        }
    }
}
