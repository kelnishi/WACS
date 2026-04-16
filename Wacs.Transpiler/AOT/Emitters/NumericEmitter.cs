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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for WebAssembly numeric instructions.
    /// Most WASM numeric ops map directly to CIL opcodes since both are stack machines.
    ///
    /// Categories:
    /// - Constants: i32.const → ldc.i4, i64.const → ldc.i8, f32.const → ldc.r4, f64.const → ldc.r8
    /// - Arithmetic: add/sub/mul/div → add/sub/mul/div (with signed/unsigned variants)
    /// - Comparisons: eq/ne/lt/gt/le/ge → ceq/clt/cgt (with inversions for ne/le/ge)
    /// - Unary: clz/ctz/popcnt → BitOperations calls; abs/neg/sqrt → Math calls
    /// - Conversions: wrap/extend/trunc/convert/reinterpret → conv.* family
    /// </summary>
    internal static class NumericEmitter
    {
        /// <summary>
        /// Returns true if this opcode is a numeric instruction we can emit.
        /// </summary>
        public static bool CanEmit(WasmOpCode op)
        {
            byte b = (byte)op;
            // Constants: 0x41-0x44
            // Test/Compare/Arithmetic: 0x45-0xA6
            // Conversions: 0xA7-0xBF
            // Sign extension: 0xC0-0xC4
            return b >= 0x41 && b <= 0xC4;
        }

        /// <summary>
        /// Emit CIL for a numeric instruction.
        /// Assumes operands are already on the CIL evaluation stack.
        /// </summary>
        public static void Emit(ILGenerator il, InstructionBase inst, WasmOpCode op)
        {
            switch (op)
            {
                // === Constants ===
                case WasmOpCode.I32Const:
                    il.Emit(OpCodes.Ldc_I4, ((InstI32Const)inst).Value);
                    break;
                case WasmOpCode.I64Const:
                    il.Emit(OpCodes.Ldc_I8, ((InstI64Const)inst).FetchImmediate(null!));
                    break;
                case WasmOpCode.F32Const:
                {
                    float fval = ((InstF32Const)inst).FetchImmediate(null!);
                    if (float.IsNaN(fval))
                    {
                        // Load NaN via memory to preserve sNaN bit patterns.
                        // On ARM64, BitConverter.Int32BitsToSingle gets JIT-inlined
                        // through FPU instructions that canonicalize sNaN to qNaN.
                        int fbits = BitConverter.SingleToInt32Bits(fval);
                        var tmp = il.DeclareLocal(typeof(int));
                        il.Emit(OpCodes.Ldc_I4, fbits);
                        il.Emit(OpCodes.Stloc, tmp);
                        il.Emit(OpCodes.Ldloca, tmp);
                        il.Emit(OpCodes.Ldind_R4);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldc_R4, fval);
                    }
                    break;
                }
                case WasmOpCode.F64Const:
                {
                    double dval = ((InstF64Const)inst).FetchImmediate(null!);
                    if (double.IsNaN(dval))
                    {
                        long dbits = BitConverter.DoubleToInt64Bits(dval);
                        var tmp = il.DeclareLocal(typeof(long));
                        il.Emit(OpCodes.Ldc_I8, dbits);
                        il.Emit(OpCodes.Stloc, tmp);
                        il.Emit(OpCodes.Ldloca, tmp);
                        il.Emit(OpCodes.Ldind_R8);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldc_R8, dval);
                    }
                    break;
                }

                // === i32 Test ===
                case WasmOpCode.I32Eqz:
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;

                // === i32 Comparisons ===
                case WasmOpCode.I32Eq:
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.I32Ne:
                    il.Emit(OpCodes.Ceq);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.I32LtS:
                    il.Emit(OpCodes.Clt);
                    break;
                case WasmOpCode.I32LtU:
                    il.Emit(OpCodes.Clt_Un);
                    break;
                case WasmOpCode.I32GtS:
                    il.Emit(OpCodes.Cgt);
                    break;
                case WasmOpCode.I32GtU:
                    il.Emit(OpCodes.Cgt_Un);
                    break;
                case WasmOpCode.I32LeS:
                    il.Emit(OpCodes.Cgt);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.I32LeU:
                    il.Emit(OpCodes.Cgt_Un);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.I32GeS:
                    il.Emit(OpCodes.Clt);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.I32GeU:
                    il.Emit(OpCodes.Clt_Un);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;

                // === i64 Test ===
                case WasmOpCode.I64Eqz:
                    il.Emit(OpCodes.Ldc_I8, 0L);
                    il.Emit(OpCodes.Ceq);
                    break;

                // === i64 Comparisons ===
                case WasmOpCode.I64Eq:
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.I64Ne:
                    il.Emit(OpCodes.Ceq);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.I64LtS:
                    il.Emit(OpCodes.Clt);
                    break;
                case WasmOpCode.I64LtU:
                    il.Emit(OpCodes.Clt_Un);
                    break;
                case WasmOpCode.I64GtS:
                    il.Emit(OpCodes.Cgt);
                    break;
                case WasmOpCode.I64GtU:
                    il.Emit(OpCodes.Cgt_Un);
                    break;
                case WasmOpCode.I64LeS:
                    il.Emit(OpCodes.Cgt);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.I64LeU:
                    il.Emit(OpCodes.Cgt_Un);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.I64GeS:
                    il.Emit(OpCodes.Clt);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.I64GeU:
                    il.Emit(OpCodes.Clt_Un);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;

                // === f32 Comparisons ===
                case WasmOpCode.F32Eq:
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.F32Ne:
                    // NaN != NaN is true in WASM. CIL: ceq returns 0 for NaN, invert gives 1. Correct.
                    il.Emit(OpCodes.Ceq);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.F32Lt:
                    il.Emit(OpCodes.Clt);
                    break;
                case WasmOpCode.F32Gt:
                    il.Emit(OpCodes.Cgt);
                    break;
                case WasmOpCode.F32Le:
                    // !(a > b) — but NaN handling: cgt_un returns 1 for unordered, invert gives 0. Correct.
                    il.Emit(OpCodes.Cgt_Un);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.F32Ge:
                    il.Emit(OpCodes.Clt_Un);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;

                // === f64 Comparisons ===
                case WasmOpCode.F64Eq:
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.F64Ne:
                    il.Emit(OpCodes.Ceq);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.F64Lt:
                    il.Emit(OpCodes.Clt);
                    break;
                case WasmOpCode.F64Gt:
                    il.Emit(OpCodes.Cgt);
                    break;
                case WasmOpCode.F64Le:
                    il.Emit(OpCodes.Cgt_Un);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;
                case WasmOpCode.F64Ge:
                    il.Emit(OpCodes.Clt_Un);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);
                    break;

                // === i32 Arithmetic ===
                case WasmOpCode.I32Add: il.Emit(OpCodes.Add); break;
                case WasmOpCode.I32Sub: il.Emit(OpCodes.Sub); break;
                case WasmOpCode.I32Mul: il.Emit(OpCodes.Mul); break;
                case WasmOpCode.I32DivS: il.Emit(OpCodes.Div); break;
                case WasmOpCode.I32DivU: il.Emit(OpCodes.Div_Un); break;
                case WasmOpCode.I32RemS:
                    il.Emit(OpCodes.Call, typeof(RemHelpers).GetMethod(nameof(RemHelpers.I32RemS))!);
                    break;
                case WasmOpCode.I32RemU: il.Emit(OpCodes.Rem_Un); break;
                case WasmOpCode.I32And: il.Emit(OpCodes.And); break;
                case WasmOpCode.I32Or:  il.Emit(OpCodes.Or); break;
                case WasmOpCode.I32Xor: il.Emit(OpCodes.Xor); break;
                case WasmOpCode.I32Shl: il.Emit(OpCodes.Shl); break;
                case WasmOpCode.I32ShrS: il.Emit(OpCodes.Shr); break;
                case WasmOpCode.I32ShrU: il.Emit(OpCodes.Shr_Un); break;

                // i32 unary — need BCL calls
                case WasmOpCode.I32Clz:
                    il.Emit(OpCodes.Call, typeof(System.Numerics.BitOperations).GetMethod("LeadingZeroCount", new[] { typeof(uint) })!);
                    break;
                case WasmOpCode.I32Ctz:
                    il.Emit(OpCodes.Call, typeof(System.Numerics.BitOperations).GetMethod("TrailingZeroCount", new[] { typeof(int) })!);
                    break;
                case WasmOpCode.I32Popcnt:
                    il.Emit(OpCodes.Call, typeof(System.Numerics.BitOperations).GetMethod("PopCount", new[] { typeof(uint) })!);
                    break;

                // i32 rotate — need BCL calls
                case WasmOpCode.I32Rotl:
                    il.Emit(OpCodes.Call, typeof(System.Numerics.BitOperations).GetMethod("RotateLeft", new[] { typeof(uint), typeof(int) })!);
                    break;
                case WasmOpCode.I32Rotr:
                    il.Emit(OpCodes.Call, typeof(System.Numerics.BitOperations).GetMethod("RotateRight", new[] { typeof(uint), typeof(int) })!);
                    break;

                // === i64 Arithmetic ===
                case WasmOpCode.I64Add: il.Emit(OpCodes.Add); break;
                case WasmOpCode.I64Sub: il.Emit(OpCodes.Sub); break;
                case WasmOpCode.I64Mul: il.Emit(OpCodes.Mul); break;
                case WasmOpCode.I64DivS: il.Emit(OpCodes.Div); break;
                case WasmOpCode.I64DivU: il.Emit(OpCodes.Div_Un); break;
                case WasmOpCode.I64RemS:
                    il.Emit(OpCodes.Call, typeof(RemHelpers).GetMethod(nameof(RemHelpers.I64RemS))!);
                    break;
                case WasmOpCode.I64RemU: il.Emit(OpCodes.Rem_Un); break;
                case WasmOpCode.I64And: il.Emit(OpCodes.And); break;
                case WasmOpCode.I64Or:  il.Emit(OpCodes.Or); break;
                case WasmOpCode.I64Xor: il.Emit(OpCodes.Xor); break;
                case WasmOpCode.I64Shl:
                    il.Emit(OpCodes.Conv_I4); // CIL shl expects i4 shift amount
                    il.Emit(OpCodes.Shl);
                    break;
                case WasmOpCode.I64ShrS:
                    il.Emit(OpCodes.Conv_I4);
                    il.Emit(OpCodes.Shr);
                    break;
                case WasmOpCode.I64ShrU:
                    il.Emit(OpCodes.Conv_I4);
                    il.Emit(OpCodes.Shr_Un);
                    break;

                // i64 unary
                case WasmOpCode.I64Clz:
                    il.Emit(OpCodes.Call, typeof(System.Numerics.BitOperations).GetMethod("LeadingZeroCount", new[] { typeof(ulong) })!);
                    il.Emit(OpCodes.Conv_I8);
                    break;
                case WasmOpCode.I64Ctz:
                    il.Emit(OpCodes.Call, typeof(System.Numerics.BitOperations).GetMethod("TrailingZeroCount", new[] { typeof(long) })!);
                    il.Emit(OpCodes.Conv_I8);
                    break;
                case WasmOpCode.I64Popcnt:
                    il.Emit(OpCodes.Call, typeof(System.Numerics.BitOperations).GetMethod("PopCount", new[] { typeof(ulong) })!);
                    il.Emit(OpCodes.Conv_I8);
                    break;

                // i64 rotate
                case WasmOpCode.I64Rotl:
                    il.Emit(OpCodes.Conv_I4); // shift amount
                    il.Emit(OpCodes.Call, typeof(System.Numerics.BitOperations).GetMethod("RotateLeft", new[] { typeof(ulong), typeof(int) })!);
                    break;
                case WasmOpCode.I64Rotr:
                    il.Emit(OpCodes.Conv_I4);
                    il.Emit(OpCodes.Call, typeof(System.Numerics.BitOperations).GetMethod("RotateRight", new[] { typeof(ulong), typeof(int) })!);
                    break;

                // === f32 Arithmetic ===
                case WasmOpCode.F32Add: il.Emit(OpCodes.Add); break;
                case WasmOpCode.F32Sub: il.Emit(OpCodes.Sub); break;
                case WasmOpCode.F32Mul: il.Emit(OpCodes.Mul); break;
                case WasmOpCode.F32Div: il.Emit(OpCodes.Div); break;

                // f32 unary
                case WasmOpCode.F32Abs:
                    il.Emit(OpCodes.Call, typeof(MathF).GetMethod("Abs", new[] { typeof(float) })!);
                    break;
                case WasmOpCode.F32Neg:
                    il.Emit(OpCodes.Neg);
                    break;
                case WasmOpCode.F32Sqrt:
                    il.Emit(OpCodes.Call, typeof(MathF).GetMethod("Sqrt", new[] { typeof(float) })!);
                    break;
                case WasmOpCode.F32Ceil:
                    il.Emit(OpCodes.Call, typeof(MathF).GetMethod("Ceiling", new[] { typeof(float) })!);
                    break;
                case WasmOpCode.F32Floor:
                    il.Emit(OpCodes.Call, typeof(MathF).GetMethod("Floor", new[] { typeof(float) })!);
                    break;
                case WasmOpCode.F32Trunc:
                    il.Emit(OpCodes.Call, typeof(MathF).GetMethod("Truncate", new[] { typeof(float) })!);
                    break;
                case WasmOpCode.F32Nearest:
                    il.Emit(OpCodes.Ldc_I4, (int)MidpointRounding.ToEven);
                    il.Emit(OpCodes.Call, typeof(MathF).GetMethod("Round", new[] { typeof(float), typeof(MidpointRounding) })
                        ?? throw new TranspilerException("MathF.Round not found"));
                    break;
                case WasmOpCode.F32Min:
                    il.Emit(OpCodes.Call, typeof(MathF).GetMethod("Min", new[] { typeof(float), typeof(float) })!);
                    break;
                case WasmOpCode.F32Max:
                    il.Emit(OpCodes.Call, typeof(MathF).GetMethod("Max", new[] { typeof(float), typeof(float) })!);
                    break;
                case WasmOpCode.F32Copysign:
                    il.Emit(OpCodes.Call, typeof(MathF).GetMethod("CopySign", new[] { typeof(float), typeof(float) })!);
                    break;

                // === f64 Arithmetic ===
                case WasmOpCode.F64Add: il.Emit(OpCodes.Add); break;
                case WasmOpCode.F64Sub: il.Emit(OpCodes.Sub); break;
                case WasmOpCode.F64Mul: il.Emit(OpCodes.Mul); break;
                case WasmOpCode.F64Div: il.Emit(OpCodes.Div); break;

                // f64 unary
                case WasmOpCode.F64Abs:
                    il.Emit(OpCodes.Call, typeof(Math).GetMethod("Abs", new[] { typeof(double) })!);
                    break;
                case WasmOpCode.F64Neg:
                    il.Emit(OpCodes.Neg);
                    break;
                case WasmOpCode.F64Sqrt:
                    il.Emit(OpCodes.Call, typeof(Math).GetMethod("Sqrt", new[] { typeof(double) })!);
                    break;
                case WasmOpCode.F64Ceil:
                    il.Emit(OpCodes.Call, typeof(Math).GetMethod("Ceiling", new[] { typeof(double) })!);
                    break;
                case WasmOpCode.F64Floor:
                    il.Emit(OpCodes.Call, typeof(Math).GetMethod("Floor", new[] { typeof(double) })!);
                    break;
                case WasmOpCode.F64Trunc:
                    il.Emit(OpCodes.Call, typeof(Math).GetMethod("Truncate", new[] { typeof(double) })!);
                    break;
                case WasmOpCode.F64Nearest:
                    il.Emit(OpCodes.Ldc_I4, (int)MidpointRounding.ToEven);
                    il.Emit(OpCodes.Call, typeof(Math).GetMethod("Round", new[] { typeof(double), typeof(MidpointRounding) })
                        ?? throw new TranspilerException("Math.Round not found"));
                    break;
                case WasmOpCode.F64Min:
                    il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", new[] { typeof(double), typeof(double) })!);
                    break;
                case WasmOpCode.F64Max:
                    il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", new[] { typeof(double), typeof(double) })!);
                    break;
                case WasmOpCode.F64Copysign:
                    il.Emit(OpCodes.Call, typeof(Math).GetMethod("CopySign", new[] { typeof(double), typeof(double) })
                        ?? throw new TranspilerException("Math.CopySign not found"));
                    break;

                // === Conversions ===
                // Trapping float-to-int conversions: WASM traps on NaN or out-of-range.
                // CIL conv.ovf.* throws OverflowException which TranspiledFunction wraps as TrapException.
                // For unsigned targets, we convert via the larger unsigned type then narrow.
                case WasmOpCode.I32WrapI64:        il.Emit(OpCodes.Conv_I4); break;
                case WasmOpCode.I32TruncF32S:
                    il.Emit(OpCodes.Call, typeof(TruncHelpers).GetMethod(nameof(TruncHelpers.I32TruncF32S))!);
                    break;
                case WasmOpCode.I32TruncF32U:
                    il.Emit(OpCodes.Call, typeof(TruncHelpers).GetMethod(nameof(TruncHelpers.I32TruncF32U))!);
                    break;
                case WasmOpCode.I32TruncF64S:
                    il.Emit(OpCodes.Call, typeof(TruncHelpers).GetMethod(nameof(TruncHelpers.I32TruncF64S))!);
                    break;
                case WasmOpCode.I32TruncF64U:
                    il.Emit(OpCodes.Call, typeof(TruncHelpers).GetMethod(nameof(TruncHelpers.I32TruncF64U))!);
                    break;
                case WasmOpCode.I64ExtendI32S:     il.Emit(OpCodes.Conv_I8); break;
                case WasmOpCode.I64ExtendI32U:     il.Emit(OpCodes.Conv_U8); break;
                case WasmOpCode.I64TruncF32S:
                    il.Emit(OpCodes.Call, typeof(TruncHelpers).GetMethod(nameof(TruncHelpers.I64TruncF32S))!);
                    break;
                case WasmOpCode.I64TruncF32U:
                    il.Emit(OpCodes.Call, typeof(TruncHelpers).GetMethod(nameof(TruncHelpers.I64TruncF32U))!);
                    break;
                case WasmOpCode.I64TruncF64S:
                    il.Emit(OpCodes.Call, typeof(TruncHelpers).GetMethod(nameof(TruncHelpers.I64TruncF64S))!);
                    break;
                case WasmOpCode.I64TruncF64U:
                    il.Emit(OpCodes.Call, typeof(TruncHelpers).GetMethod(nameof(TruncHelpers.I64TruncF64U))!);
                    break;
                case WasmOpCode.F32ConvertI32S:    il.Emit(OpCodes.Conv_R4); break;
                case WasmOpCode.F32ConvertI32U:    il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Conv_R4); break;
                case WasmOpCode.F32ConvertI64S:    il.Emit(OpCodes.Conv_R4); break;
                case WasmOpCode.F32ConvertI64U:    il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Conv_R4); break;
                case WasmOpCode.F32DemoteF64:
                    il.Emit(OpCodes.Conv_R4);
                    // ARM64 conv.r4 may not set the quiet NaN bit.
                    // Spec requires arithmetic NaN (quiet bit set) for NaN inputs.
                    il.Emit(OpCodes.Call, typeof(NanHelpers).GetMethod(
                        nameof(NanHelpers.CanonicalizeF32))!);
                    break;
                case WasmOpCode.F64ConvertI32S:    il.Emit(OpCodes.Conv_R8); break;
                case WasmOpCode.F64ConvertI32U:    il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Conv_R8); break;
                case WasmOpCode.F64ConvertI64S:    il.Emit(OpCodes.Conv_R8); break;
                case WasmOpCode.F64ConvertI64U:    il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Conv_R8); break;
                case WasmOpCode.F64PromoteF32:
                    il.Emit(OpCodes.Conv_R8);
                    il.Emit(OpCodes.Call, typeof(NanHelpers).GetMethod(
                        nameof(NanHelpers.CanonicalizeF64))!);
                    break;

                // Reinterpret — no-op in CIL (same bit pattern, different type interpretation)
                // But CIL verification requires explicit conversion for type safety
                case WasmOpCode.I32ReinterpretF32:
                {
                    // Spill float to memory, reload as int — preserves sNaN bits.
                    // BitConverter calls get JIT-inlined through FPU on ARM64,
                    // which canonicalizes sNaN to qNaN.
                    var fl = il.DeclareLocal(typeof(float));
                    il.Emit(OpCodes.Stloc, fl);
                    il.Emit(OpCodes.Ldloca, fl);
                    il.Emit(OpCodes.Ldind_I4);
                    break;
                }
                case WasmOpCode.I64ReinterpretF64:
                {
                    var dl = il.DeclareLocal(typeof(double));
                    il.Emit(OpCodes.Stloc, dl);
                    il.Emit(OpCodes.Ldloca, dl);
                    il.Emit(OpCodes.Ldind_I8);
                    break;
                }
                case WasmOpCode.F32ReinterpretI32:
                {
                    var il32 = il.DeclareLocal(typeof(int));
                    il.Emit(OpCodes.Stloc, il32);
                    il.Emit(OpCodes.Ldloca, il32);
                    il.Emit(OpCodes.Ldind_R4);
                    break;
                }
                case WasmOpCode.F64ReinterpretI64:
                {
                    var il64 = il.DeclareLocal(typeof(long));
                    il.Emit(OpCodes.Stloc, il64);
                    il.Emit(OpCodes.Ldloca, il64);
                    il.Emit(OpCodes.Ldind_R8);
                    break;
                }

                // === Sign Extension ===
                case WasmOpCode.I32Extend8S:
                    il.Emit(OpCodes.Conv_I1);
                    il.Emit(OpCodes.Conv_I4);
                    break;
                case WasmOpCode.I32Extend16S:
                    il.Emit(OpCodes.Conv_I2);
                    il.Emit(OpCodes.Conv_I4);
                    break;
                case WasmOpCode.I64Extend8S:
                    il.Emit(OpCodes.Conv_I1);
                    il.Emit(OpCodes.Conv_I8);
                    break;
                case WasmOpCode.I64Extend16S:
                    il.Emit(OpCodes.Conv_I2);
                    il.Emit(OpCodes.Conv_I8);
                    break;
                case WasmOpCode.I64Extend32S:
                    il.Emit(OpCodes.Conv_I4);
                    il.Emit(OpCodes.Conv_I8);
                    break;

                default:
                    throw new TranspilerException($"NumericEmitter: unhandled opcode {op}");
            }
        }
    }

    /// <summary>
    /// WASM trapping float-to-int conversions.
    /// CLR conv.i4/conv.u4 silently wraps on overflow. WASM requires trapping
    /// on NaN or out-of-range values. These helpers implement the spec-mandated behavior.
    /// </summary>
    public static class TruncHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int I32TruncF32S(float v)
        {
            if (float.IsNaN(v)) throw new Wacs.Core.Runtime.Types.TrapException("invalid conversion to integer");
            if (v >= 2147483648.0f || v < -2147483648.0f) throw new Wacs.Core.Runtime.Types.TrapException("integer overflow");
            return (int)v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int I32TruncF32U(float v)
        {
            if (float.IsNaN(v)) throw new Wacs.Core.Runtime.Types.TrapException("invalid conversion to integer");
            if (v >= 4294967296.0f || v <= -1.0f) throw new Wacs.Core.Runtime.Types.TrapException("integer overflow");
            return (int)(uint)v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int I32TruncF64S(double v)
        {
            if (double.IsNaN(v)) throw new Wacs.Core.Runtime.Types.TrapException("invalid conversion to integer");
            if (v >= 2147483648.0 || v <= -2147483649.0) throw new Wacs.Core.Runtime.Types.TrapException("integer overflow");
            return (int)v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int I32TruncF64U(double v)
        {
            if (double.IsNaN(v)) throw new Wacs.Core.Runtime.Types.TrapException("invalid conversion to integer");
            if (v >= 4294967296.0 || v <= -1.0) throw new Wacs.Core.Runtime.Types.TrapException("integer overflow");
            return (int)(uint)v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long I64TruncF32S(float v)
        {
            if (float.IsNaN(v)) throw new Wacs.Core.Runtime.Types.TrapException("invalid conversion to integer");
            if (v >= 9223372036854775808.0f || v < -9223372036854775808.0f) throw new Wacs.Core.Runtime.Types.TrapException("integer overflow");
            return (long)v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long I64TruncF32U(float v)
        {
            if (float.IsNaN(v)) throw new Wacs.Core.Runtime.Types.TrapException("invalid conversion to integer");
            if (v >= 18446744073709551616.0f || v <= -1.0f) throw new Wacs.Core.Runtime.Types.TrapException("integer overflow");
            return (long)(ulong)v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long I64TruncF64S(double v)
        {
            if (double.IsNaN(v)) throw new Wacs.Core.Runtime.Types.TrapException("invalid conversion to integer");
            if (v >= 9223372036854775808.0 || v < -9223372036854775808.0) throw new Wacs.Core.Runtime.Types.TrapException("integer overflow");
            return (long)v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long I64TruncF64U(double v)
        {
            if (double.IsNaN(v)) throw new Wacs.Core.Runtime.Types.TrapException("invalid conversion to integer");
            if (v >= 18446744073709551616.0 || v <= -1.0) throw new Wacs.Core.Runtime.Types.TrapException("integer overflow");
            return (long)(ulong)v;
        }
    }

    /// <summary>
    /// WASM remainder operations.
    /// CLR throws OverflowException for INT_MIN % -1, but WASM spec requires result 0.
    /// </summary>
    public static class RemHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int I32RemS(int a, int b)
        {
            if (b == 0) throw new Wacs.Core.Runtime.Types.TrapException("integer divide by zero");
            if (b == -1) return 0; // INT_MIN % -1 = 0 per spec
            return a % b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long I64RemS(long a, long b)
        {
            if (b == 0) throw new Wacs.Core.Runtime.Types.TrapException("integer divide by zero");
            if (b == -1) return 0; // LONG_MIN % -1 = 0 per spec
            return a % b;
        }
    }

    /// <summary>
    /// NaN canonicalization helpers.
    /// The WASM spec requires arithmetic operations (including promote/demote)
    /// to produce "arithmetic NaN" — NaN with the quiet bit set.
    /// On ARM64, CIL conv.r4/conv.r8 may preserve sNaN (quiet bit clear)
    /// instead of canonicalizing. These helpers ensure the quiet bit is set.
    /// </summary>
    public static class NanHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CanonicalizeF32(float v)
        {
            if (!float.IsNaN(v)) return v;
            // Set quiet bit (bit 22) via memory reinterpret to avoid FPU canonicalization
            var span = MemoryMarshal.CreateSpan(ref v, 1);
            var bits = MemoryMarshal.Cast<float, int>(span);
            bits[0] |= 0x00400000; // set quiet NaN bit
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double CanonicalizeF64(double v)
        {
            if (!double.IsNaN(v)) return v;
            var span = MemoryMarshal.CreateSpan(ref v, 1);
            var bits = MemoryMarshal.Cast<double, long>(span);
            bits[0] |= 0x0008000000000000L; // set quiet NaN bit
            return v;
        }
    }
}
