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
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime;
using Wacs.Core.Types.Defs;
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
        /// <param name="paramTypes">CIL types for WASM params seen on the
        /// internal stack (i.e., object for GC refs, Value for funcref/externref/v128).</param>
        /// <param name="paramShadowLocals">Shadow locals (doc 2 §15) for GC-ref params;
        /// null entries indicate the param uses the direct CIL arg slot.</param>
        /// <param name="cv">CIL stack validator (optional)</param>
        public static void Emit(
            ILGenerator il,
            InstructionBase inst,
            WasmOpCode op,
            int paramCount,
            LocalBuilder[] locals,
            Type[]? paramTypes = null,
            LocalBuilder?[]? paramShadowLocals = null,
            CilValidator? cv = null)
        {
            switch (op)
            {
                case WasmOpCode.LocalGet:
                {
                    int idx = ((InstLocalGet)inst).GetIndex();
                    EmitLocalGet(il, idx, paramCount, paramShadowLocals);
                    if (cv != null)
                    {
                        var clrType = idx < paramCount
                            ? (paramTypes != null && idx < paramTypes.Length ? paramTypes[idx] : typeof(object))
                            : locals[idx - paramCount].LocalType;
                        cv.Push(clrType);
                    }
                    break;
                }
                case WasmOpCode.LocalSet:
                {
                    int idx = ((InstLocalSet)inst).GetIndex();
                    if (cv != null)
                    {
                        var clrType = idx < paramCount
                            ? (paramTypes != null && idx < paramTypes.Length ? paramTypes[idx] : typeof(object))
                            : locals[idx - paramCount].LocalType;
                        cv.Pop(clrType, "local.set");
                    }
                    EmitLocalSet(il, idx, paramCount, paramShadowLocals);
                    break;
                }
                case WasmOpCode.LocalTee:
                {
                    int idx = ((InstLocalTee)inst).GetIndex();
                    il.Emit(OpCodes.Dup);
                    EmitLocalSet(il, idx, paramCount, paramShadowLocals);
                    break;
                }
                case WasmOpCode.Drop:
                    cv?.Pop(context: "drop");
                    il.Emit(OpCodes.Pop);
                    break;
                case WasmOpCode.Select:
                case WasmOpCode.SelectT:
                {
                    // Select must match both operand types. In the split-stack
                    // model GC refs flow as object (doc 2 §1), so a ref select
                    // may see either Value (funcref/externref/v128) or object
                    // (GC ref) on the CIL stack. Dispatch on the peeked type
                    // of the second operand (the first pop is the condition).
                    cv?.Pop(typeof(int), "select.cond");
                    // Pop both operand values from the validator up-front so
                    // the stack tracking matches the actual CIL stack (select
                    // consumes 3, produces 1). Then push the result type.
                    var val2Type = cv?.Peek() ?? typeof(Value);
                    cv?.Pop(context: "select.val2");
                    var val1Type = cv?.Peek() ?? val2Type;
                    cv?.Pop(context: "select.val1");
                    // Prefer the more specific known type for the result.
                    var resultType = val1Type == typeof(object) ? val2Type : val1Type;
                    if (resultType == typeof(object))
                    {
                        // GC ref select → object-based helper.
                        il.Emit(OpCodes.Call, typeof(SelectHelpers).GetMethod(
                            nameof(SelectHelpers.SelectObject),
                            BindingFlags.Public | BindingFlags.Static)!);
                    }
                    else
                    {
                        var sel = inst as InstSelect;
                        if (sel?.Types.Length > 0 && sel.Types[0].IsRefType())
                        {
                            // Funcref / externref / v128 select — Value helper.
                            il.Emit(OpCodes.Call, typeof(SelectHelpers).GetMethod(
                                nameof(SelectHelpers.SelectValue),
                                BindingFlags.Public | BindingFlags.Static)!);
                        }
                        else
                        {
                            // Scalar select. Use the actual operand type —
                            // both operands share the same type by WASM
                            // validation.
                            EmitSelect(il, resultType);
                        }
                    }
                    cv?.Push(resultType);
                    break;
                }
                default:
                    throw new TranspilerException($"VariableEmitter: unhandled opcode {op}");
            }
        }

        /// <summary>
        /// WASM local index 0..paramCount-1 → CIL arg 1..paramCount (arg 0 = ThinContext),
        /// unless a shadow local exists for the index (GC-ref params routed through
        /// object-typed shadow locals per doc 2 §15).
        /// WASM local index paramCount..N → CIL local 0..N-paramCount.
        /// </summary>
        private static void EmitLocalGet(ILGenerator il, int wasmIdx, int paramCount,
            LocalBuilder?[]? paramShadowLocals)
        {
            if (wasmIdx < paramCount)
            {
                if (paramShadowLocals != null && paramShadowLocals[wasmIdx] != null)
                {
                    il.Emit(OpCodes.Ldloc, paramShadowLocals[wasmIdx]!);
                }
                else
                {
                    il.Emit(OpCodes.Ldarg, wasmIdx + 1);
                }
            }
            else
            {
                il.Emit(OpCodes.Ldloc, wasmIdx - paramCount);
            }
        }

        private static void EmitLocalSet(ILGenerator il, int wasmIdx, int paramCount,
            LocalBuilder?[]? paramShadowLocals)
        {
            if (wasmIdx < paramCount)
            {
                if (paramShadowLocals != null && paramShadowLocals[wasmIdx] != null)
                {
                    il.Emit(OpCodes.Stloc, paramShadowLocals[wasmIdx]!);
                }
                else
                {
                    il.Emit(OpCodes.Starg, wasmIdx + 1);
                }
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
        private static void EmitSelect(ILGenerator il, Type val2Type)
        {
            // Stack: val1 val2 cond
            // Strategy: use a branch
            var lblTrue = il.DefineLabel();
            var lblEnd = il.DefineLabel();

            // Store cond in a temp
            var condLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, condLocal);

            // Store val2 in a temp (type must match the CIL stack type)
            var val2Local = il.DeclareLocal(val2Type);
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

    /// <summary>
    /// Runtime helpers for select with Value structs.
    /// Value contains a managed reference field (IGcRef? GcRef) which
    /// causes CIL verification issues when the struct is on the
    /// evaluation stack at branch merge points. Using a method call
    /// avoids the merge entirely.
    /// </summary>
    public static class SelectHelpers
    {
        /// <summary>select for Value: [val1, val2, cond] → val1 if cond!=0, else val2</summary>
        public static Value SelectValue(Value val1, Value val2, int cond)
            => cond != 0 ? val1 : val2;

        /// <summary>select for object (GC refs, doc 2 §1):
        /// [val1, val2, cond] → val1 if cond!=0, else val2.</summary>
        public static object? SelectObject(object? val1, object? val2, int cond)
            => cond != 0 ? val1 : val2;
    }
}
