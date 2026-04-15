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

using System.Reflection.Emit;
using Wacs.Core.Instructions;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for WebAssembly variable and parametric instructions.
    ///
    /// WASM locals map to CIL locals + method parameters:
    /// - Parameters 0..N-1 are CIL args 1..N (arg 0 is ThinContext)
    /// - Locals N..M are CIL locals 0..M-N
    ///
    /// local.get → ldarg / ldloc
    /// local.set → starg / stloc
    /// local.tee → dup + starg / stloc
    /// drop → pop
    /// select → conditional via branch
    /// </summary>
    internal static class VariableEmitter
    {
        public static bool CanEmit(WasmOpCode op)
        {
            return op == WasmOpCode.LocalGet
                || op == WasmOpCode.LocalSet
                || op == WasmOpCode.LocalTee
                || op == WasmOpCode.Drop
                || op == WasmOpCode.Select
                || op == WasmOpCode.SelectT;
        }

        /// <summary>
        /// Emit CIL for a variable/parametric instruction.
        /// </summary>
        /// <param name="il">IL generator</param>
        /// <param name="inst">The instruction instance (for extracting index)</param>
        /// <param name="op">The opcode</param>
        /// <param name="paramCount">Number of WASM function parameters</param>
        /// <param name="locals">Array of CIL local builders (for WASM locals, not params)</param>
        public static void Emit(
            ILGenerator il,
            InstructionBase inst,
            WasmOpCode op,
            int paramCount,
            LocalBuilder[] locals)
        {
            switch (op)
            {
                case WasmOpCode.LocalGet:
                {
                    int idx = ((InstLocalGet)inst).GetIndex();
                    EmitLocalGet(il, idx, paramCount);
                    break;
                }
                case WasmOpCode.LocalSet:
                {
                    int idx = ((InstLocalSet)inst).GetIndex();
                    EmitLocalSet(il, idx, paramCount);
                    break;
                }
                case WasmOpCode.LocalTee:
                {
                    int idx = ((InstLocalTee)inst).GetIndex();
                    il.Emit(OpCodes.Dup);
                    EmitLocalSet(il, idx, paramCount);
                    break;
                }
                case WasmOpCode.Drop:
                    il.Emit(OpCodes.Pop);
                    break;
                case WasmOpCode.Select:
                case WasmOpCode.SelectT:
                    EmitSelect(il);
                    break;
                default:
                    throw new TranspilerException($"VariableEmitter: unhandled opcode {op}");
            }
        }

        /// <summary>
        /// WASM local index 0..paramCount-1 → CIL arg 1..paramCount (arg 0 = ThinContext)
        /// WASM local index paramCount..N → CIL local 0..N-paramCount
        /// </summary>
        private static void EmitLocalGet(ILGenerator il, int wasmIdx, int paramCount)
        {
            if (wasmIdx < paramCount)
            {
                // WASM param → CIL arg (offset by 1 for ThinContext)
                il.Emit(OpCodes.Ldarg, wasmIdx + 1);
            }
            else
            {
                // WASM local → CIL local
                il.Emit(OpCodes.Ldloc, wasmIdx - paramCount);
            }
        }

        private static void EmitLocalSet(ILGenerator il, int wasmIdx, int paramCount)
        {
            if (wasmIdx < paramCount)
            {
                il.Emit(OpCodes.Starg, wasmIdx + 1);
            }
            else
            {
                il.Emit(OpCodes.Stloc, wasmIdx - paramCount);
            }
        }

        /// <summary>
        /// select: [val1 val2 cond] → [val1 if cond!=0, else val2]
        /// CIL: store val2 to temp, branch on cond, load val1 or val2
        /// </summary>
        private static void EmitSelect(ILGenerator il)
        {
            // Stack: val1 val2 cond
            // Strategy: use a branch
            var lblTrue = il.DefineLabel();
            var lblEnd = il.DefineLabel();

            // Store cond in a temp
            var condLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, condLocal);

            // Store val2 in a temp
            var val2Local = il.DeclareLocal(typeof(long)); // wide enough for any numeric
            il.Emit(OpCodes.Stloc, val2Local);

            // Stack now has: val1
            // Check cond
            il.Emit(OpCodes.Ldloc, condLocal);
            il.Emit(OpCodes.Brtrue, lblTrue);

            // cond == 0: discard val1, use val2
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc, val2Local);
            il.Emit(OpCodes.Br, lblEnd);

            // cond != 0: keep val1 (already on stack)
            il.MarkLabel(lblTrue);

            il.MarkLabel(lblEnd);
        }
    }
}
