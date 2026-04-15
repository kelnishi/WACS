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

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for 0xFD prefix (SIMD) instructions.
    ///
    /// All SIMD ops dispatch through the interpreter's Execute methods via
    /// ExecContext.OpStack marshaling. This reuses the interpreter's spec-compliant
    /// scalar implementations as the reference.
    ///
    /// The instruction's StackDiff property determines how many values to pop/push.
    ///
    /// Future: intrinsics-based implementations can be added per-opcode by replacing
    /// specific cases with direct Vector128 operations, gated by transpiler options.
    /// </summary>
    internal static class SimdEmitter
    {
        internal static readonly List<InstructionBase> InstructionRegistry = new();
        private static readonly object _lock = new();

        private static int RegisterInstruction(InstructionBase inst)
        {
            lock (_lock)
            {
                int id = InstructionRegistry.Count;
                InstructionRegistry.Add(inst);
                return id;
            }
        }

        public static bool CanEmit(SimdCode op) => true;

        /// <summary>
        /// Emit a SIMD instruction by delegating to the interpreter.
        ///
        /// Uses StackDiff to determine marshaling:
        ///   StackDiff = outputCount - inputCount
        ///   Stores: outputCount = 0. Everything else: outputCount = 1.
        ///   inputCount = outputCount - StackDiff
        /// </summary>
        public static void Emit(ILGenerator il, InstructionBase inst, SimdCode op)
        {
            // Try direct helper path (bypasses interpreter, uses scalar/intrinsics)
            if (TryEmitDirect(il, inst, op))
                return;

            // Fallback: interpreter dispatch via OpStack marshaling
            EmitInterpreterDispatch(il, inst, op);
        }

        /// <summary>
        /// Direct helper calls for opcodes with dedicated SimdHelpers methods.
        /// Returns true if handled. Opcodes are added incrementally by chunk.
        /// </summary>
        private static bool TryEmitDirect(ILGenerator il, InstructionBase inst, SimdCode op)
        {
            switch (op)
            {
                // === Chunk 2: Bitwise ops ===
                case SimdCode.V128Not:
                    EmitUnaryV128(il, nameof(SimdHelpers.V128Not));
                    return true;
                case SimdCode.V128And:
                    EmitBinaryV128(il, nameof(SimdHelpers.V128And));
                    return true;
                case SimdCode.V128Or:
                    EmitBinaryV128(il, nameof(SimdHelpers.V128Or));
                    return true;
                case SimdCode.V128Xor:
                    EmitBinaryV128(il, nameof(SimdHelpers.V128Xor));
                    return true;
                case SimdCode.V128AndNot:
                    EmitBinaryV128(il, nameof(SimdHelpers.V128AndNot));
                    return true;
                case SimdCode.V128BitSelect:
                    EmitTernaryV128(il, nameof(SimdHelpers.V128BitSelect));
                    return true;
                case SimdCode.V128AnyTrue:
                    EmitV128ToI32(il, nameof(SimdHelpers.V128AnyTrue));
                    return true;

                // === Chunk 3: Integer arithmetic ===
                // add/sub per shape
                case SimdCode.I8x16Add: EmitBinaryV128(il, nameof(SimdHelpers.I8x16Add)); return true;
                case SimdCode.I8x16Sub: EmitBinaryV128(il, nameof(SimdHelpers.I8x16Sub)); return true;
                case SimdCode.I16x8Add: EmitBinaryV128(il, nameof(SimdHelpers.I16x8Add)); return true;
                case SimdCode.I16x8Sub: EmitBinaryV128(il, nameof(SimdHelpers.I16x8Sub)); return true;
                case SimdCode.I16x8Mul: EmitBinaryV128(il, nameof(SimdHelpers.I16x8Mul)); return true;
                case SimdCode.I32x4Add: EmitBinaryV128(il, nameof(SimdHelpers.I32x4Add)); return true;
                case SimdCode.I32x4Sub: EmitBinaryV128(il, nameof(SimdHelpers.I32x4Sub)); return true;
                case SimdCode.I32x4Mul: EmitBinaryV128(il, nameof(SimdHelpers.I32x4Mul)); return true;
                case SimdCode.I64x2Add: EmitBinaryV128(il, nameof(SimdHelpers.I64x2Add)); return true;
                case SimdCode.I64x2Sub: EmitBinaryV128(il, nameof(SimdHelpers.I64x2Sub)); return true;
                case SimdCode.I64x2Mul: EmitBinaryV128(il, nameof(SimdHelpers.I64x2Mul)); return true;
                // neg per shape
                case SimdCode.I8x16Neg: EmitUnaryV128(il, nameof(SimdHelpers.I8x16Neg)); return true;
                case SimdCode.I16x8Neg: EmitUnaryV128(il, nameof(SimdHelpers.I16x8Neg)); return true;
                case SimdCode.I32x4Neg: EmitUnaryV128(il, nameof(SimdHelpers.I32x4Neg)); return true;
                case SimdCode.I64x2Neg: EmitUnaryV128(il, nameof(SimdHelpers.I64x2Neg)); return true;

                default:
                    return false;
            }
        }

        // ================================================================
        // Emission templates for direct helper calls
        // ================================================================

        /// <summary>Unary V128 → V128</summary>
        private static void EmitUnaryV128(ILGenerator il, string helperName)
        {
            EmitUnboxV128(il);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName,
                new[] { typeof(V128) })!);
            EmitBoxV128(il);
        }

        /// <summary>Binary (V128, V128) → V128</summary>
        private static void EmitBinaryV128(ILGenerator il, string helperName)
        {
            // Stack: [Value(v1), Value(v2)] — v2 on top
            var v2 = il.DeclareLocal(typeof(V128));
            EmitUnboxV128(il); // unbox v2
            il.Emit(OpCodes.Stloc, v2);
            EmitUnboxV128(il); // unbox v1
            il.Emit(OpCodes.Ldloc, v2);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName,
                new[] { typeof(V128), typeof(V128) })!);
            EmitBoxV128(il);
        }

        /// <summary>Ternary (V128, V128, V128) → V128</summary>
        private static void EmitTernaryV128(ILGenerator il, string helperName)
        {
            // Stack: [Value(v1), Value(v2), Value(v3)]
            var v3 = il.DeclareLocal(typeof(V128));
            EmitUnboxV128(il);
            il.Emit(OpCodes.Stloc, v3);
            var v2 = il.DeclareLocal(typeof(V128));
            EmitUnboxV128(il);
            il.Emit(OpCodes.Stloc, v2);
            EmitUnboxV128(il);
            il.Emit(OpCodes.Ldloc, v2);
            il.Emit(OpCodes.Ldloc, v3);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName,
                new[] { typeof(V128), typeof(V128), typeof(V128) })!);
            EmitBoxV128(il);
        }

        /// <summary>V128 → i32 (test ops)</summary>
        private static void EmitV128ToI32(ILGenerator il, string helperName)
        {
            EmitUnboxV128(il);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName,
                new[] { typeof(V128) })!);
            // Result is i32 on CIL stack — matches WASM result type
        }

        private static void EmitUnboxV128(ILGenerator il)
        {
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(
                nameof(SimdHelpers.UnboxV128), BindingFlags.Public | BindingFlags.Static)!);
        }

        private static void EmitBoxV128(ILGenerator il)
        {
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(
                nameof(SimdHelpers.BoxV128), BindingFlags.Public | BindingFlags.Static)!);
        }

        // ================================================================
        // Interpreter fallback dispatch (for ops without direct helpers)
        // ================================================================

        private static void EmitInterpreterDispatch(ILGenerator il, InstructionBase inst, SimdCode op)
        {
            bool isStore = op == SimdCode.V128Store ||
                           op == SimdCode.V128Store8Lane ||
                           op == SimdCode.V128Store16Lane ||
                           op == SimdCode.V128Store32Lane ||
                           op == SimdCode.V128Store64Lane;
            int outputCount = isStore ? 0 : 1;
            int inputCount = outputCount - inst.StackDiff;
            if (inputCount < 0) inputCount = 0;

            int instId = RegisterInstruction(inst);

            var inputLocals = new LocalBuilder[inputCount];
            for (int i = inputCount - 1; i >= 0; i--)
            {
                inputLocals[i] = il.DeclareLocal(typeof(Value));
                il.Emit(OpCodes.Stloc, inputLocals[i]);
            }

            for (int i = 0; i < inputCount; i++)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, inputLocals[i]);
                il.Emit(OpCodes.Call, typeof(SimdDispatch).GetMethod(
                    nameof(SimdDispatch.PushValue), BindingFlags.Public | BindingFlags.Static)!);
            }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, instId);
            il.Emit(OpCodes.Call, typeof(SimdDispatch).GetMethod(
                nameof(SimdDispatch.ExecuteSimdOp), BindingFlags.Public | BindingFlags.Static)!);

            for (int i = 0; i < outputCount; i++)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, typeof(SimdDispatch).GetMethod(
                    nameof(SimdDispatch.PopValue), BindingFlags.Public | BindingFlags.Static)!);
            }
        }
    }

    /// <summary>
    /// Runtime dispatch for SIMD operations.
    /// Marshals between CIL stack (Value) and interpreter OpStack.
    /// Uses the interpreter's spec-compliant implementations as the reference.
    /// </summary>
    public static class SimdDispatch
    {
        public static void PushValue(TranspiledContext ctx, Value val)
        {
            if (ctx.ExecContext == null)
                throw new TrapException("SIMD ops require ExecContext");
            ctx.ExecContext.OpStack.PushValue(val);
        }

        public static Value PopValue(TranspiledContext ctx)
        {
            if (ctx.ExecContext == null)
                throw new TrapException("SIMD ops require ExecContext");
            return ctx.ExecContext.OpStack.PopAny();
        }

        public static void ExecuteSimdOp(TranspiledContext ctx, int instructionId)
        {
            if (ctx.ExecContext == null)
                throw new TrapException("SIMD ops require ExecContext");
            var inst = SimdEmitter.InstructionRegistry[instructionId];
            inst.Execute(ctx.ExecContext);
        }
    }
}
