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
using Wacs.Core.Instructions.SIMD;
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
        public static void Emit(ILGenerator il, InstructionBase inst, SimdCode op,
            TranspilerOptions options, DiagnosticCollector diagnostics, string? functionName = null)
        {
            if (options.Simd == SimdStrategy.InterpreterDispatch)
            {
                EmitInterpreterDispatch(il, inst, op);
                return;
            }

            // Try direct helper path (scalar or intrinsics)
            if (TryEmitDirect(il, inst, op))
                return;

            // Fallback to interpreter for ops without direct helpers
            diagnostics.Warning(
                $"SIMD op falls back to interpreter dispatch (no direct helper yet)",
                functionName, inst.Op.GetMnemonic());
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
                // === Bitwise ops ===
                case SimdCode.V128Not: EmitUnaryV128(il, nameof(SimdHelpers.V128Not)); return true;
                case SimdCode.V128And: EmitBinaryV128(il, nameof(SimdHelpers.V128And)); return true;
                case SimdCode.V128Or: EmitBinaryV128(il, nameof(SimdHelpers.V128Or)); return true;
                case SimdCode.V128Xor: EmitBinaryV128(il, nameof(SimdHelpers.V128Xor)); return true;
                case SimdCode.V128AndNot: EmitBinaryV128(il, nameof(SimdHelpers.V128AndNot)); return true;
                case SimdCode.V128BitSelect: EmitTernaryV128(il, nameof(SimdHelpers.V128BitSelect)); return true;
                case SimdCode.V128AnyTrue: EmitV128ToI32(il, nameof(SimdHelpers.V128AnyTrue)); return true;

                // === Integer add/sub/mul ===
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

                // === Integer abs/neg/popcnt ===
                case SimdCode.I8x16Abs: EmitUnaryV128(il, nameof(SimdHelpers.I8x16Abs)); return true;
                case SimdCode.I16x8Abs: EmitUnaryV128(il, nameof(SimdHelpers.I16x8Abs)); return true;
                case SimdCode.I32x4Abs: EmitUnaryV128(il, nameof(SimdHelpers.I32x4Abs)); return true;
                case SimdCode.I64x2Abs: EmitUnaryV128(il, nameof(SimdHelpers.I64x2Abs)); return true;
                case SimdCode.I8x16Neg: EmitUnaryV128(il, nameof(SimdHelpers.I8x16Neg)); return true;
                case SimdCode.I16x8Neg: EmitUnaryV128(il, nameof(SimdHelpers.I16x8Neg)); return true;
                case SimdCode.I32x4Neg: EmitUnaryV128(il, nameof(SimdHelpers.I32x4Neg)); return true;
                case SimdCode.I64x2Neg: EmitUnaryV128(il, nameof(SimdHelpers.I64x2Neg)); return true;
                case SimdCode.I8x16Popcnt: EmitUnaryV128(il, nameof(SimdHelpers.I8x16Popcnt)); return true;

                // === Integer test/bitmask ===
                case SimdCode.I8x16AllTrue: EmitV128ToI32(il, nameof(SimdHelpers.I8x16AllTrue)); return true;
                case SimdCode.I16x8AllTrue: EmitV128ToI32(il, nameof(SimdHelpers.I16x8AllTrue)); return true;
                case SimdCode.I32x4AllTrue: EmitV128ToI32(il, nameof(SimdHelpers.I32x4AllTrue)); return true;
                case SimdCode.I64x2AllTrue: EmitV128ToI32(il, nameof(SimdHelpers.I64x2AllTrue)); return true;
                case SimdCode.I8x16Bitmask: EmitV128ToI32(il, nameof(SimdHelpers.I8x16Bitmask)); return true;
                case SimdCode.I16x8Bitmask: EmitV128ToI32(il, nameof(SimdHelpers.I16x8Bitmask)); return true;
                case SimdCode.I32x4Bitmask: EmitV128ToI32(il, nameof(SimdHelpers.I32x4Bitmask)); return true;
                case SimdCode.I64x2Bitmask: EmitV128ToI32(il, nameof(SimdHelpers.I64x2Bitmask)); return true;

                // === Integer relational ops ===
                case SimdCode.I8x16Eq: EmitBinaryV128(il, nameof(SimdHelpers.I8x16Eq)); return true;
                case SimdCode.I8x16Ne: EmitBinaryV128(il, nameof(SimdHelpers.I8x16Ne)); return true;
                case SimdCode.I8x16LtS: EmitBinaryV128(il, nameof(SimdHelpers.I8x16LtS)); return true;
                case SimdCode.I8x16LtU: EmitBinaryV128(il, nameof(SimdHelpers.I8x16LtU)); return true;
                case SimdCode.I8x16GtS: EmitBinaryV128(il, nameof(SimdHelpers.I8x16GtS)); return true;
                case SimdCode.I8x16GtU: EmitBinaryV128(il, nameof(SimdHelpers.I8x16GtU)); return true;
                case SimdCode.I8x16LeS: EmitBinaryV128(il, nameof(SimdHelpers.I8x16LeS)); return true;
                case SimdCode.I8x16LeU: EmitBinaryV128(il, nameof(SimdHelpers.I8x16LeU)); return true;
                case SimdCode.I8x16GeS: EmitBinaryV128(il, nameof(SimdHelpers.I8x16GeS)); return true;
                case SimdCode.I8x16GeU: EmitBinaryV128(il, nameof(SimdHelpers.I8x16GeU)); return true;
                case SimdCode.I16x8Eq: EmitBinaryV128(il, nameof(SimdHelpers.I16x8Eq)); return true;
                case SimdCode.I16x8Ne: EmitBinaryV128(il, nameof(SimdHelpers.I16x8Ne)); return true;
                case SimdCode.I16x8LtS: EmitBinaryV128(il, nameof(SimdHelpers.I16x8LtS)); return true;
                case SimdCode.I16x8LtU: EmitBinaryV128(il, nameof(SimdHelpers.I16x8LtU)); return true;
                case SimdCode.I16x8GtS: EmitBinaryV128(il, nameof(SimdHelpers.I16x8GtS)); return true;
                case SimdCode.I16x8GtU: EmitBinaryV128(il, nameof(SimdHelpers.I16x8GtU)); return true;
                case SimdCode.I16x8LeS: EmitBinaryV128(il, nameof(SimdHelpers.I16x8LeS)); return true;
                case SimdCode.I16x8LeU: EmitBinaryV128(il, nameof(SimdHelpers.I16x8LeU)); return true;
                case SimdCode.I16x8GeS: EmitBinaryV128(il, nameof(SimdHelpers.I16x8GeS)); return true;
                case SimdCode.I16x8GeU: EmitBinaryV128(il, nameof(SimdHelpers.I16x8GeU)); return true;
                case SimdCode.I32x4Eq: EmitBinaryV128(il, nameof(SimdHelpers.I32x4Eq)); return true;
                case SimdCode.I32x4Ne: EmitBinaryV128(il, nameof(SimdHelpers.I32x4Ne)); return true;
                case SimdCode.I32x4LtS: EmitBinaryV128(il, nameof(SimdHelpers.I32x4LtS)); return true;
                case SimdCode.I32x4LtU: EmitBinaryV128(il, nameof(SimdHelpers.I32x4LtU)); return true;
                case SimdCode.I32x4GtS: EmitBinaryV128(il, nameof(SimdHelpers.I32x4GtS)); return true;
                case SimdCode.I32x4GtU: EmitBinaryV128(il, nameof(SimdHelpers.I32x4GtU)); return true;
                case SimdCode.I32x4LeS: EmitBinaryV128(il, nameof(SimdHelpers.I32x4LeS)); return true;
                case SimdCode.I32x4LeU: EmitBinaryV128(il, nameof(SimdHelpers.I32x4LeU)); return true;
                case SimdCode.I32x4GeS: EmitBinaryV128(il, nameof(SimdHelpers.I32x4GeS)); return true;
                case SimdCode.I32x4GeU: EmitBinaryV128(il, nameof(SimdHelpers.I32x4GeU)); return true;
                case SimdCode.I64x2Eq: EmitBinaryV128(il, nameof(SimdHelpers.I64x2Eq)); return true;
                case SimdCode.I64x2Ne: EmitBinaryV128(il, nameof(SimdHelpers.I64x2Ne)); return true;
                case SimdCode.I64x2LtS: EmitBinaryV128(il, nameof(SimdHelpers.I64x2LtS)); return true;
                case SimdCode.I64x2GtS: EmitBinaryV128(il, nameof(SimdHelpers.I64x2GtS)); return true;
                case SimdCode.I64x2LeS: EmitBinaryV128(il, nameof(SimdHelpers.I64x2LeS)); return true;
                case SimdCode.I64x2GeS: EmitBinaryV128(il, nameof(SimdHelpers.I64x2GeS)); return true;

                // === Saturating arithmetic ===
                case SimdCode.I8x16AddSatS: EmitBinaryV128(il, nameof(SimdHelpers.I8x16AddSatS)); return true;
                case SimdCode.I8x16AddSatU: EmitBinaryV128(il, nameof(SimdHelpers.I8x16AddSatU)); return true;
                case SimdCode.I8x16SubSatS: EmitBinaryV128(il, nameof(SimdHelpers.I8x16SubSatS)); return true;
                case SimdCode.I8x16SubSatU: EmitBinaryV128(il, nameof(SimdHelpers.I8x16SubSatU)); return true;
                case SimdCode.I16x8AddSatS: EmitBinaryV128(il, nameof(SimdHelpers.I16x8AddSatS)); return true;
                case SimdCode.I16x8AddSatU: EmitBinaryV128(il, nameof(SimdHelpers.I16x8AddSatU)); return true;
                case SimdCode.I16x8SubSatS: EmitBinaryV128(il, nameof(SimdHelpers.I16x8SubSatS)); return true;
                case SimdCode.I16x8SubSatU: EmitBinaryV128(il, nameof(SimdHelpers.I16x8SubSatU)); return true;

                // === Integer min/max/avgr ===
                case SimdCode.I8x16MinS: EmitBinaryV128(il, nameof(SimdHelpers.I8x16MinS)); return true;
                case SimdCode.I8x16MinU: EmitBinaryV128(il, nameof(SimdHelpers.I8x16MinU)); return true;
                case SimdCode.I8x16MaxS: EmitBinaryV128(il, nameof(SimdHelpers.I8x16MaxS)); return true;
                case SimdCode.I8x16MaxU: EmitBinaryV128(il, nameof(SimdHelpers.I8x16MaxU)); return true;
                case SimdCode.I16x8MinS: EmitBinaryV128(il, nameof(SimdHelpers.I16x8MinS)); return true;
                case SimdCode.I16x8MinU: EmitBinaryV128(il, nameof(SimdHelpers.I16x8MinU)); return true;
                case SimdCode.I16x8MaxS: EmitBinaryV128(il, nameof(SimdHelpers.I16x8MaxS)); return true;
                case SimdCode.I16x8MaxU: EmitBinaryV128(il, nameof(SimdHelpers.I16x8MaxU)); return true;
                case SimdCode.I32x4MinS: EmitBinaryV128(il, nameof(SimdHelpers.I32x4MinS)); return true;
                case SimdCode.I32x4MinU: EmitBinaryV128(il, nameof(SimdHelpers.I32x4MinU)); return true;
                case SimdCode.I32x4MaxS: EmitBinaryV128(il, nameof(SimdHelpers.I32x4MaxS)); return true;
                case SimdCode.I32x4MaxU: EmitBinaryV128(il, nameof(SimdHelpers.I32x4MaxU)); return true;
                case SimdCode.I8x16AvgrU: EmitBinaryV128(il, nameof(SimdHelpers.I8x16AvgrU)); return true;
                case SimdCode.I16x8AvgrU: EmitBinaryV128(il, nameof(SimdHelpers.I16x8AvgrU)); return true;

                // === Shift ops (V128 + i32 -> V128) ===
                case SimdCode.I8x16Shl: EmitShiftV128(il, nameof(SimdHelpers.I8x16Shl)); return true;
                case SimdCode.I8x16ShrS: EmitShiftV128(il, nameof(SimdHelpers.I8x16ShrS)); return true;
                case SimdCode.I8x16ShrU: EmitShiftV128(il, nameof(SimdHelpers.I8x16ShrU)); return true;
                case SimdCode.I16x8Shl: EmitShiftV128(il, nameof(SimdHelpers.I16x8Shl)); return true;
                case SimdCode.I16x8ShrS: EmitShiftV128(il, nameof(SimdHelpers.I16x8ShrS)); return true;
                case SimdCode.I16x8ShrU: EmitShiftV128(il, nameof(SimdHelpers.I16x8ShrU)); return true;
                case SimdCode.I32x4Shl: EmitShiftV128(il, nameof(SimdHelpers.I32x4Shl)); return true;
                case SimdCode.I32x4ShrS: EmitShiftV128(il, nameof(SimdHelpers.I32x4ShrS)); return true;
                case SimdCode.I32x4ShrU: EmitShiftV128(il, nameof(SimdHelpers.I32x4ShrU)); return true;
                case SimdCode.I64x2Shl: EmitShiftV128(il, nameof(SimdHelpers.I64x2Shl)); return true;
                case SimdCode.I64x2ShrS: EmitShiftV128(il, nameof(SimdHelpers.I64x2ShrS)); return true;
                case SimdCode.I64x2ShrU: EmitShiftV128(il, nameof(SimdHelpers.I64x2ShrU)); return true;

                // === Swizzle, dot, q15mulr, extmul ===
                case SimdCode.I8x16Swizzle: EmitBinaryV128(il, nameof(SimdHelpers.I8x16Swizzle)); return true;
                case SimdCode.I32x4DotI16x8S: EmitBinaryV128(il, nameof(SimdHelpers.I32x4DotI16x8S)); return true;
                case SimdCode.I16x8Q15MulRSatS: EmitBinaryV128(il, nameof(SimdHelpers.I16x8Q15MulRSatS)); return true;

                // === Extended multiply ===
                case SimdCode.I16x8ExtMulLowI8x16S: EmitBinaryV128(il, nameof(SimdHelpers.I16x8ExtMulLowI8x16S)); return true;
                case SimdCode.I16x8ExtMulHighI8x16S: EmitBinaryV128(il, nameof(SimdHelpers.I16x8ExtMulHighI8x16S)); return true;
                case SimdCode.I16x8ExtMulLowI8x16U: EmitBinaryV128(il, nameof(SimdHelpers.I16x8ExtMulLowI8x16U)); return true;
                case SimdCode.I16x8ExtMulHighI8x16U: EmitBinaryV128(il, nameof(SimdHelpers.I16x8ExtMulHighI8x16U)); return true;
                case SimdCode.I32x4ExtMulLowI16x8S: EmitBinaryV128(il, nameof(SimdHelpers.I32x4ExtMulLowI16x8S)); return true;
                case SimdCode.I32x4ExtMulHighI16x8S: EmitBinaryV128(il, nameof(SimdHelpers.I32x4ExtMulHighI16x8S)); return true;
                case SimdCode.I32x4ExtMulLowI16x8U: EmitBinaryV128(il, nameof(SimdHelpers.I32x4ExtMulLowI16x8U)); return true;
                case SimdCode.I32x4ExtMulHighI16x8U: EmitBinaryV128(il, nameof(SimdHelpers.I32x4ExtMulHighI16x8U)); return true;
                case SimdCode.I64x2ExtMulLowI32x4S: EmitBinaryV128(il, nameof(SimdHelpers.I64x2ExtMulLowI32x4S)); return true;
                case SimdCode.I64x2ExtMulHighI32x4S: EmitBinaryV128(il, nameof(SimdHelpers.I64x2ExtMulHighI32x4S)); return true;
                case SimdCode.I64x2ExtMulLowI32x4U: EmitBinaryV128(il, nameof(SimdHelpers.I64x2ExtMulLowI32x4U)); return true;
                case SimdCode.I64x2ExtMulHighI32x4U: EmitBinaryV128(il, nameof(SimdHelpers.I64x2ExtMulHighI32x4U)); return true;

                // === ExtAdd pairwise (unary) ===
                case SimdCode.I16x8ExtAddPairwiseI8x16S: EmitUnaryV128(il, nameof(SimdHelpers.I16x8ExtAddPairwiseI8x16S)); return true;
                case SimdCode.I16x8ExtAddPairwiseI8x16U: EmitUnaryV128(il, nameof(SimdHelpers.I16x8ExtAddPairwiseI8x16U)); return true;
                case SimdCode.I32x4ExtAddPairwiseI16x8S: EmitUnaryV128(il, nameof(SimdHelpers.I32x4ExtAddPairwiseI16x8S)); return true;
                case SimdCode.I32x4ExtAddPairwiseI16x8U: EmitUnaryV128(il, nameof(SimdHelpers.I32x4ExtAddPairwiseI16x8U)); return true;

                // === Narrow (binary) ===
                case SimdCode.I8x16NarrowI16x8S: EmitBinaryV128(il, nameof(SimdHelpers.I8x16NarrowI16x8S)); return true;
                case SimdCode.I8x16NarrowI16x8U: EmitBinaryV128(il, nameof(SimdHelpers.I8x16NarrowI16x8U)); return true;
                case SimdCode.I16x8NarrowI32x4S: EmitBinaryV128(il, nameof(SimdHelpers.I16x8NarrowI32x4S)); return true;
                case SimdCode.I16x8NarrowI32x4U: EmitBinaryV128(il, nameof(SimdHelpers.I16x8NarrowI32x4U)); return true;

                // === Extend (unary) ===
                case SimdCode.I16x8ExtendLowI8x16S: EmitUnaryV128(il, nameof(SimdHelpers.I16x8ExtendLowI8x16S)); return true;
                case SimdCode.I16x8ExtendHighI8x16S: EmitUnaryV128(il, nameof(SimdHelpers.I16x8ExtendHighI8x16S)); return true;
                case SimdCode.I16x8ExtendLowI8x16U: EmitUnaryV128(il, nameof(SimdHelpers.I16x8ExtendLowI8x16U)); return true;
                case SimdCode.I16x8ExtendHighI8x16U: EmitUnaryV128(il, nameof(SimdHelpers.I16x8ExtendHighI8x16U)); return true;
                case SimdCode.I32x4ExtendLowI16x8S: EmitUnaryV128(il, nameof(SimdHelpers.I32x4ExtendLowI16x8S)); return true;
                case SimdCode.I32x4ExtendHighI16x8S: EmitUnaryV128(il, nameof(SimdHelpers.I32x4ExtendHighI16x8S)); return true;
                case SimdCode.I32x4ExtendLowI16x8U: EmitUnaryV128(il, nameof(SimdHelpers.I32x4ExtendLowI16x8U)); return true;
                case SimdCode.I32x4ExtendHighI16x8U: EmitUnaryV128(il, nameof(SimdHelpers.I32x4ExtendHighI16x8U)); return true;
                case SimdCode.I64x2ExtendLowI32x4S: EmitUnaryV128(il, nameof(SimdHelpers.I64x2ExtendLowI32x4S)); return true;
                case SimdCode.I64x2ExtendHighI32x4S: EmitUnaryV128(il, nameof(SimdHelpers.I64x2ExtendHighI32x4S)); return true;
                case SimdCode.I64x2ExtendLowI32x4U: EmitUnaryV128(il, nameof(SimdHelpers.I64x2ExtendLowI32x4U)); return true;
                case SimdCode.I64x2ExtendHighI32x4U: EmitUnaryV128(il, nameof(SimdHelpers.I64x2ExtendHighI32x4U)); return true;

                // === TruncSat (unary) ===
                case SimdCode.I32x4TruncSatF32x4S: EmitUnaryV128(il, nameof(SimdHelpers.I32x4TruncSatF32x4S)); return true;
                case SimdCode.I32x4TruncSatF32x4U: EmitUnaryV128(il, nameof(SimdHelpers.I32x4TruncSatF32x4U)); return true;
                case SimdCode.I32x4TruncSatF64x2SZero: EmitUnaryV128(il, nameof(SimdHelpers.I32x4TruncSatF64x2SZero)); return true;
                case SimdCode.I32x4TruncSatF64x2UZero: EmitUnaryV128(il, nameof(SimdHelpers.I32x4TruncSatF64x2UZero)); return true;

                // === Float binary ops ===
                case SimdCode.F32x4Add: EmitBinaryV128(il, nameof(SimdHelpers.F32x4Add)); return true;
                case SimdCode.F32x4Sub: EmitBinaryV128(il, nameof(SimdHelpers.F32x4Sub)); return true;
                case SimdCode.F32x4Mul: EmitBinaryV128(il, nameof(SimdHelpers.F32x4Mul)); return true;
                case SimdCode.F32x4Div: EmitBinaryV128(il, nameof(SimdHelpers.F32x4Div)); return true;
                case SimdCode.F32x4Min: EmitBinaryV128(il, nameof(SimdHelpers.F32x4Min)); return true;
                case SimdCode.F32x4Max: EmitBinaryV128(il, nameof(SimdHelpers.F32x4Max)); return true;
                case SimdCode.F32x4PMin: EmitBinaryV128(il, nameof(SimdHelpers.F32x4PMin)); return true;
                case SimdCode.F32x4PMax: EmitBinaryV128(il, nameof(SimdHelpers.F32x4PMax)); return true;
                case SimdCode.F64x2Add: EmitBinaryV128(il, nameof(SimdHelpers.F64x2Add)); return true;
                case SimdCode.F64x2Sub: EmitBinaryV128(il, nameof(SimdHelpers.F64x2Sub)); return true;
                case SimdCode.F64x2Mul: EmitBinaryV128(il, nameof(SimdHelpers.F64x2Mul)); return true;
                case SimdCode.F64x2Div: EmitBinaryV128(il, nameof(SimdHelpers.F64x2Div)); return true;
                case SimdCode.F64x2Min: EmitBinaryV128(il, nameof(SimdHelpers.F64x2Min)); return true;
                case SimdCode.F64x2Max: EmitBinaryV128(il, nameof(SimdHelpers.F64x2Max)); return true;
                case SimdCode.F64x2PMin: EmitBinaryV128(il, nameof(SimdHelpers.F64x2PMin)); return true;
                case SimdCode.F64x2PMax: EmitBinaryV128(il, nameof(SimdHelpers.F64x2PMax)); return true;

                // === Float unary ops ===
                case SimdCode.F32x4Abs: EmitUnaryV128(il, nameof(SimdHelpers.F32x4Abs)); return true;
                case SimdCode.F32x4Neg: EmitUnaryV128(il, nameof(SimdHelpers.F32x4Neg)); return true;
                case SimdCode.F32x4Sqrt: EmitUnaryV128(il, nameof(SimdHelpers.F32x4Sqrt)); return true;
                case SimdCode.F32x4Ceil: EmitUnaryV128(il, nameof(SimdHelpers.F32x4Ceil)); return true;
                case SimdCode.F32x4Floor: EmitUnaryV128(il, nameof(SimdHelpers.F32x4Floor)); return true;
                case SimdCode.F32x4Trunc: EmitUnaryV128(il, nameof(SimdHelpers.F32x4Trunc)); return true;
                case SimdCode.F32x4Nearest: EmitUnaryV128(il, nameof(SimdHelpers.F32x4Nearest)); return true;
                case SimdCode.F64x2Abs: EmitUnaryV128(il, nameof(SimdHelpers.F64x2Abs)); return true;
                case SimdCode.F64x2Neg: EmitUnaryV128(il, nameof(SimdHelpers.F64x2Neg)); return true;
                case SimdCode.F64x2Sqrt: EmitUnaryV128(il, nameof(SimdHelpers.F64x2Sqrt)); return true;
                case SimdCode.F64x2Ceil: EmitUnaryV128(il, nameof(SimdHelpers.F64x2Ceil)); return true;
                case SimdCode.F64x2Floor: EmitUnaryV128(il, nameof(SimdHelpers.F64x2Floor)); return true;
                case SimdCode.F64x2Trunc: EmitUnaryV128(il, nameof(SimdHelpers.F64x2Trunc)); return true;
                case SimdCode.F64x2Nearest: EmitUnaryV128(il, nameof(SimdHelpers.F64x2Nearest)); return true;

                // === Float relational ops ===
                case SimdCode.F32x4Eq: EmitBinaryV128(il, nameof(SimdHelpers.F32x4Eq)); return true;
                case SimdCode.F32x4Ne: EmitBinaryV128(il, nameof(SimdHelpers.F32x4Ne)); return true;
                case SimdCode.F32x4Lt: EmitBinaryV128(il, nameof(SimdHelpers.F32x4Lt)); return true;
                case SimdCode.F32x4Gt: EmitBinaryV128(il, nameof(SimdHelpers.F32x4Gt)); return true;
                case SimdCode.F32x4Le: EmitBinaryV128(il, nameof(SimdHelpers.F32x4Le)); return true;
                case SimdCode.F32x4Ge: EmitBinaryV128(il, nameof(SimdHelpers.F32x4Ge)); return true;
                case SimdCode.F64x2Eq: EmitBinaryV128(il, nameof(SimdHelpers.F64x2Eq)); return true;
                case SimdCode.F64x2Ne: EmitBinaryV128(il, nameof(SimdHelpers.F64x2Ne)); return true;
                case SimdCode.F64x2Lt: EmitBinaryV128(il, nameof(SimdHelpers.F64x2Lt)); return true;
                case SimdCode.F64x2Gt: EmitBinaryV128(il, nameof(SimdHelpers.F64x2Gt)); return true;
                case SimdCode.F64x2Le: EmitBinaryV128(il, nameof(SimdHelpers.F64x2Le)); return true;
                case SimdCode.F64x2Ge: EmitBinaryV128(il, nameof(SimdHelpers.F64x2Ge)); return true;

                // === Float convert/promote/demote ===
                case SimdCode.F32x4ConvertI32x4S: EmitUnaryV128(il, nameof(SimdHelpers.F32x4ConvertI32x4S)); return true;
                case SimdCode.F32x4ConvertI32x4U: EmitUnaryV128(il, nameof(SimdHelpers.F32x4ConvertI32x4U)); return true;
                case SimdCode.F64x2ConvertLowI32x4S: EmitUnaryV128(il, nameof(SimdHelpers.F64x2ConvertLowI32x4S)); return true;
                case SimdCode.F64x2ConvertLowI32x4U: EmitUnaryV128(il, nameof(SimdHelpers.F64x2ConvertLowI32x4U)); return true;
                case SimdCode.F32x4DemoteF64x2Zero: EmitUnaryV128(il, nameof(SimdHelpers.F32x4DemoteF64x2Zero)); return true;
                case SimdCode.F64x2PromoteLowF32x4: EmitUnaryV128(il, nameof(SimdHelpers.F64x2PromoteLowF32x4)); return true;

                // === Relaxed SIMD — unary ===
                case SimdCode.I32x4RelaxedTruncF32x4S: EmitUnaryV128(il, nameof(SimdHelpers.I32x4RelaxedTruncF32x4S)); return true;
                case SimdCode.I32x4RelaxedTruncF32x4U: EmitUnaryV128(il, nameof(SimdHelpers.I32x4RelaxedTruncF32x4U)); return true;
                case SimdCode.I32x4RelaxedTruncF64x2SZero: EmitUnaryV128(il, nameof(SimdHelpers.I32x4RelaxedTruncF64x2SZero)); return true;
                case SimdCode.I32x4RelaxedTruncF64x2UZero: EmitUnaryV128(il, nameof(SimdHelpers.I32x4RelaxedTruncF64x2UZero)); return true;

                // === Relaxed SIMD — binary ===
                case SimdCode.I8x16RelaxedSwizzle: EmitBinaryV128(il, nameof(SimdHelpers.I8x16RelaxedSwizzle)); return true;
                case SimdCode.F32x4RelaxedMin: EmitBinaryV128(il, nameof(SimdHelpers.F32x4RelaxedMin)); return true;
                case SimdCode.F32x4RelaxedMax: EmitBinaryV128(il, nameof(SimdHelpers.F32x4RelaxedMax)); return true;
                case SimdCode.F64x2RelaxedMin: EmitBinaryV128(il, nameof(SimdHelpers.F64x2RelaxedMin)); return true;
                case SimdCode.F64x2RelaxedMax: EmitBinaryV128(il, nameof(SimdHelpers.F64x2RelaxedMax)); return true;
                case SimdCode.I16x8RelaxedQ15MulrS: EmitBinaryV128(il, nameof(SimdHelpers.I16x8RelaxedQ15MulrS)); return true;
                case SimdCode.I16x8RelaxedDotI8x16I7x16S: EmitBinaryV128(il, nameof(SimdHelpers.I16x8RelaxedDotI8x16I7x16S)); return true;

                // === Relaxed SIMD — ternary ===
                case SimdCode.I8x16RelaxedLaneselect: EmitTernaryV128(il, nameof(SimdHelpers.I8x16RelaxedLaneselect)); return true;
                case SimdCode.I16x8RelaxedLaneselect: EmitTernaryV128(il, nameof(SimdHelpers.I16x8RelaxedLaneselect)); return true;
                case SimdCode.I32x4RelaxedLaneselect: EmitTernaryV128(il, nameof(SimdHelpers.I32x4RelaxedLaneselect)); return true;
                case SimdCode.I64x2RelaxedLaneselect: EmitTernaryV128(il, nameof(SimdHelpers.I64x2RelaxedLaneselect)); return true;
                case SimdCode.F32x4RelaxedMAdd: EmitTernaryV128(il, nameof(SimdHelpers.F32x4RelaxedMAdd)); return true;
                case SimdCode.F32x4RelaxedNMAdd: EmitTernaryV128(il, nameof(SimdHelpers.F32x4RelaxedNMAdd)); return true;
                case SimdCode.F64x2RelaxedMAdd: EmitTernaryV128(il, nameof(SimdHelpers.F64x2RelaxedMAdd)); return true;
                case SimdCode.F64x2RelaxedNMAdd: EmitTernaryV128(il, nameof(SimdHelpers.F64x2RelaxedNMAdd)); return true;
                case SimdCode.I32x4RelaxedDotI8x16I7x16AddS: EmitTernaryV128(il, nameof(SimdHelpers.I32x4RelaxedDotI8x16I7x16AddS)); return true;

                // === Splat ops: scalar → V128 ===
                case SimdCode.I8x16Splat: EmitSplatI32(il, nameof(SimdHelpers.I8x16Splat)); return true;
                case SimdCode.I16x8Splat: EmitSplatI32(il, nameof(SimdHelpers.I16x8Splat)); return true;
                case SimdCode.I32x4Splat: EmitSplatI32(il, nameof(SimdHelpers.I32x4Splat)); return true;
                case SimdCode.I64x2Splat: EmitSplatI64(il, nameof(SimdHelpers.I64x2Splat)); return true;
                case SimdCode.F32x4Splat: EmitSplatF32(il, nameof(SimdHelpers.F32x4Splat)); return true;
                case SimdCode.F64x2Splat: EmitSplatF64(il, nameof(SimdHelpers.F64x2Splat)); return true;

                // === Extract lane: V128 → scalar (lane index from instruction) ===
                case SimdCode.I8x16ExtractLaneS: EmitExtractLaneI32(il, inst, nameof(SimdHelpers.I8x16ExtractLaneS)); return true;
                case SimdCode.I8x16ExtractLaneU: EmitExtractLaneI32(il, inst, nameof(SimdHelpers.I8x16ExtractLaneU)); return true;
                case SimdCode.I16x8ExtractLaneS: EmitExtractLaneI32(il, inst, nameof(SimdHelpers.I16x8ExtractLaneS)); return true;
                case SimdCode.I16x8ExtractLaneU: EmitExtractLaneI32(il, inst, nameof(SimdHelpers.I16x8ExtractLaneU)); return true;
                case SimdCode.I32x4ExtractLane: EmitExtractLaneI32(il, inst, nameof(SimdHelpers.I32x4ExtractLane)); return true;
                case SimdCode.I64x2ExtractLane: EmitExtractLaneI64(il, inst, nameof(SimdHelpers.I64x2ExtractLane)); return true;
                case SimdCode.F32x4ExtractLane: EmitExtractLaneF32(il, inst, nameof(SimdHelpers.F32x4ExtractLane)); return true;
                case SimdCode.F64x2ExtractLane: EmitExtractLaneF64(il, inst, nameof(SimdHelpers.F64x2ExtractLane)); return true;

                // === Replace lane: V128 + scalar → V128 ===
                case SimdCode.I8x16ReplaceLane: EmitReplaceLaneI32(il, inst, nameof(SimdHelpers.I8x16ReplaceLane)); return true;
                case SimdCode.I16x8ReplaceLane: EmitReplaceLaneI32(il, inst, nameof(SimdHelpers.I16x8ReplaceLane)); return true;
                case SimdCode.I32x4ReplaceLane: EmitReplaceLaneI32(il, inst, nameof(SimdHelpers.I32x4ReplaceLane)); return true;
                case SimdCode.I64x2ReplaceLane: EmitReplaceLaneI64(il, inst, nameof(SimdHelpers.I64x2ReplaceLane)); return true;
                case SimdCode.F32x4ReplaceLane: EmitReplaceLaneF32(il, inst, nameof(SimdHelpers.F32x4ReplaceLane)); return true;
                case SimdCode.F64x2ReplaceLane: EmitReplaceLaneF64(il, inst, nameof(SimdHelpers.F64x2ReplaceLane)); return true;

                // === Shuffle: V128 + V128 → V128 with lane indices ===
                case SimdCode.I8x16Shuffle: EmitShuffle(il, inst); return true;

                // === V128.const: immediate → V128 ===
                case SimdCode.V128Const: EmitV128Const(il, inst); return true;

                // === V128 memory loads: addr → V128 ===
                case SimdCode.V128Load:
                case SimdCode.V128Load8x8S: case SimdCode.V128Load8x8U:
                case SimdCode.V128Load16x4S: case SimdCode.V128Load16x4U:
                case SimdCode.V128Load32x2S: case SimdCode.V128Load32x2U:
                case SimdCode.V128Load8Splat: case SimdCode.V128Load16Splat:
                case SimdCode.V128Load32Splat: case SimdCode.V128Load64Splat:
                case SimdCode.V128Load32Zero: case SimdCode.V128Load64Zero:
                    EmitSimdLoad(il, inst, op);
                    return true;

                // === V128 lane loads: V128 + addr → V128 ===
                // Note: InstMemoryLoadZero incorrectly uses Lane opcodes internally
                case SimdCode.V128Load8Lane: case SimdCode.V128Load16Lane:
                case SimdCode.V128Load32Lane: case SimdCode.V128Load64Lane:
                    if (inst is Wacs.Core.Instructions.SIMD.InstMemoryLoadZero)
                        EmitSimdLoad(il, inst, op);
                    else
                        EmitSimdLoadLane(il, inst, op);
                    return true;

                // === V128 store: addr + V128 → void ===
                case SimdCode.V128Store:
                    EmitSimdStore(il, inst);
                    return true;

                // === V128 lane stores: addr + V128 → void ===
                case SimdCode.V128Store8Lane: case SimdCode.V128Store16Lane:
                case SimdCode.V128Store32Lane: case SimdCode.V128Store64Lane:
                    EmitSimdStoreLane(il, inst, op);
                    return true;

                // === Prototype relaxed (duplicate encodings) ===
                case SimdCode.Prototype_I8x16RelaxedSwizzle: EmitBinaryV128(il, nameof(SimdHelpers.I8x16RelaxedSwizzle)); return true;
                case SimdCode.Prototype_I8x16RelaxedLaneselect: EmitTernaryV128(il, nameof(SimdHelpers.I8x16RelaxedLaneselect)); return true;
                case SimdCode.Prototype_I16x8RelaxedLaneselect: EmitTernaryV128(il, nameof(SimdHelpers.I16x8RelaxedLaneselect)); return true;
                case SimdCode.Prototype_I32x4RelaxedLaneselect: EmitTernaryV128(il, nameof(SimdHelpers.I32x4RelaxedLaneselect)); return true;
                case SimdCode.Prototype_I64x2RelaxedLaneselect: EmitTernaryV128(il, nameof(SimdHelpers.I64x2RelaxedLaneselect)); return true;
                case SimdCode.Prototype_F32x4RelaxedMin: EmitBinaryV128(il, nameof(SimdHelpers.F32x4RelaxedMin)); return true;
                case SimdCode.Prototype_F32x4RelaxedMax: EmitBinaryV128(il, nameof(SimdHelpers.F32x4RelaxedMax)); return true;
                case SimdCode.Prototype_F64x2RelaxedMin: EmitBinaryV128(il, nameof(SimdHelpers.F64x2RelaxedMin)); return true;
                case SimdCode.Prototype_F64x2RelaxedMax: EmitBinaryV128(il, nameof(SimdHelpers.F64x2RelaxedMax)); return true;
                case SimdCode.Prototype_F32x4RelaxedMAdd: EmitTernaryV128(il, nameof(SimdHelpers.F32x4RelaxedMAdd)); return true;
                case SimdCode.Prototype_F32x4RelaxedNMAdd: EmitTernaryV128(il, nameof(SimdHelpers.F32x4RelaxedNMAdd)); return true;
                case SimdCode.Prototype_F64x2RelaxedMAdd: EmitTernaryV128(il, nameof(SimdHelpers.F64x2RelaxedMAdd)); return true;
                case SimdCode.Prototype_F64x2RelaxedNMAdd: EmitTernaryV128(il, nameof(SimdHelpers.F64x2RelaxedNMAdd)); return true;
                case SimdCode.Prototype_I32x4RelaxedTruncF32x4S: EmitUnaryV128(il, nameof(SimdHelpers.I32x4RelaxedTruncF32x4S)); return true;
                case SimdCode.Prototype_I32x4RelaxedTruncF32x4U: EmitUnaryV128(il, nameof(SimdHelpers.I32x4RelaxedTruncF32x4U)); return true;
                case SimdCode.Prototype_I32x4RelaxedTruncF64x2SZero: EmitUnaryV128(il, nameof(SimdHelpers.I32x4RelaxedTruncF64x2SZero)); return true;
                case SimdCode.Prototype_I32x4RelaxedTruncF64x2UZero: EmitUnaryV128(il, nameof(SimdHelpers.I32x4RelaxedTruncF64x2UZero)); return true;

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

        /// <summary>Shift (V128, i32) → V128. Stack: [Value(v128), i32] — i32 on top</summary>
        private static void EmitShiftV128(ILGenerator il, string helperName)
        {
            // Stack: [Value(v), i32_shift] — shift amount on top (already raw i32)
            var shift = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, shift);
            EmitUnboxV128(il); // unbox v
            il.Emit(OpCodes.Ldloc, shift);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName,
                new[] { typeof(V128), typeof(int) })!);
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

        // === Splat: scalar on CIL stack → V128 boxed to Value ===
        private static void EmitSplatI32(ILGenerator il, string helperName)
        {
            // Stack has: [int] → call helper(int) → V128 → box
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName, new[] { typeof(int) })!);
            EmitBoxV128(il);
        }

        private static void EmitSplatI64(ILGenerator il, string helperName)
        {
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName, new[] { typeof(long) })!);
            EmitBoxV128(il);
        }

        private static void EmitSplatF32(ILGenerator il, string helperName)
        {
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName, new[] { typeof(float) })!);
            EmitBoxV128(il);
        }

        private static void EmitSplatF64(ILGenerator il, string helperName)
        {
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName, new[] { typeof(double) })!);
            EmitBoxV128(il);
        }

        // === Extract lane: V128 → scalar, lane from instruction immediate ===
        private static void EmitExtractLaneI32(ILGenerator il, InstructionBase inst, string helperName)
        {
            byte lane = ((Wacs.Core.Instructions.Numeric.InstLaneOp)inst).LaneIndex;
            EmitUnboxV128(il);
            il.Emit(OpCodes.Ldc_I4, (int)lane);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName, new[] { typeof(V128), typeof(byte) })!);
            // Result is int on CIL stack
        }

        private static void EmitExtractLaneI64(ILGenerator il, InstructionBase inst, string helperName)
        {
            byte lane = ((Wacs.Core.Instructions.Numeric.InstLaneOp)inst).LaneIndex;
            EmitUnboxV128(il);
            il.Emit(OpCodes.Ldc_I4, (int)lane);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName, new[] { typeof(V128), typeof(byte) })!);
        }

        private static void EmitExtractLaneF32(ILGenerator il, InstructionBase inst, string helperName)
        {
            byte lane = ((Wacs.Core.Instructions.Numeric.InstLaneOp)inst).LaneIndex;
            EmitUnboxV128(il);
            il.Emit(OpCodes.Ldc_I4, (int)lane);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName, new[] { typeof(V128), typeof(byte) })!);
        }

        private static void EmitExtractLaneF64(ILGenerator il, InstructionBase inst, string helperName)
        {
            byte lane = ((Wacs.Core.Instructions.Numeric.InstLaneOp)inst).LaneIndex;
            EmitUnboxV128(il);
            il.Emit(OpCodes.Ldc_I4, (int)lane);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName, new[] { typeof(V128), typeof(byte) })!);
        }

        // === Replace lane: V128 + scalar → V128, lane from instruction ===
        private static void EmitReplaceLaneI32(ILGenerator il, InstructionBase inst, string helperName)
        {
            byte lane = ((Wacs.Core.Instructions.Numeric.InstLaneOp)inst).LaneIndex;
            // Stack: [Value(v128), int]
            var scalar = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, scalar);
            EmitUnboxV128(il);
            il.Emit(OpCodes.Ldc_I4, (int)lane);
            il.Emit(OpCodes.Ldloc, scalar);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName, new[] { typeof(V128), typeof(byte), typeof(int) })!);
            EmitBoxV128(il);
        }

        private static void EmitReplaceLaneI64(ILGenerator il, InstructionBase inst, string helperName)
        {
            byte lane = ((Wacs.Core.Instructions.Numeric.InstLaneOp)inst).LaneIndex;
            var scalar = il.DeclareLocal(typeof(long));
            il.Emit(OpCodes.Stloc, scalar);
            EmitUnboxV128(il);
            il.Emit(OpCodes.Ldc_I4, (int)lane);
            il.Emit(OpCodes.Ldloc, scalar);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName, new[] { typeof(V128), typeof(byte), typeof(long) })!);
            EmitBoxV128(il);
        }

        private static void EmitReplaceLaneF32(ILGenerator il, InstructionBase inst, string helperName)
        {
            byte lane = ((Wacs.Core.Instructions.Numeric.InstLaneOp)inst).LaneIndex;
            var scalar = il.DeclareLocal(typeof(float));
            il.Emit(OpCodes.Stloc, scalar);
            EmitUnboxV128(il);
            il.Emit(OpCodes.Ldc_I4, (int)lane);
            il.Emit(OpCodes.Ldloc, scalar);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName, new[] { typeof(V128), typeof(byte), typeof(float) })!);
            EmitBoxV128(il);
        }

        private static void EmitReplaceLaneF64(ILGenerator il, InstructionBase inst, string helperName)
        {
            byte lane = ((Wacs.Core.Instructions.Numeric.InstLaneOp)inst).LaneIndex;
            var scalar = il.DeclareLocal(typeof(double));
            il.Emit(OpCodes.Stloc, scalar);
            EmitUnboxV128(il);
            il.Emit(OpCodes.Ldc_I4, (int)lane);
            il.Emit(OpCodes.Ldloc, scalar);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(helperName, new[] { typeof(V128), typeof(byte), typeof(double) })!);
            EmitBoxV128(il);
        }

        // === Shuffle: V128 + V128 → V128 with lane indices from instruction ===
        private static void EmitShuffle(ILGenerator il, InstructionBase inst)
        {
            var lanes = ((Wacs.Core.Instructions.InstShuffleOp)inst).LaneIndices;
            // Stack: [Value(v1), Value(v2)]
            var v2 = il.DeclareLocal(typeof(V128));
            EmitUnboxV128(il);
            il.Emit(OpCodes.Stloc, v2);
            EmitUnboxV128(il);
            il.Emit(OpCodes.Ldloc, v2);
            // Pass lanes as a V128 (the lane indices are stored as a V128 in the instruction)
            // We register the lanes value and load it at runtime
            int lanesId = RegisterInstruction(inst); // reuse registry to stash the instruction
            il.Emit(OpCodes.Ldarg_0); // ctx (unused but needed for registry access)
            il.Emit(OpCodes.Ldc_I4, lanesId);
            il.Emit(OpCodes.Call, typeof(SimdDispatch).GetMethod(
                nameof(SimdDispatch.GetShuffleLanes), BindingFlags.Public | BindingFlags.Static)!);
            il.Emit(OpCodes.Call, typeof(SimdHelpers).GetMethod(
                nameof(SimdHelpers.I8x16Shuffle), new[] { typeof(V128), typeof(V128), typeof(V128) })!);
            EmitBoxV128(il);
        }

        // === V128.const: push immediate value ===
        private static void EmitV128Const(ILGenerator il, InstructionBase inst)
        {
            var constInst = (Wacs.Core.Instructions.Simd.InstV128Const)inst;
            var value = constInst.Value;
            // Register the value and load at runtime (V128 can't be embedded as IL constant)
            int constId = RegisterInstruction(inst);
            il.Emit(OpCodes.Ldc_I4, constId);
            il.Emit(OpCodes.Call, typeof(SimdDispatch).GetMethod(
                nameof(SimdDispatch.GetV128Const), BindingFlags.Public | BindingFlags.Static)!);
            EmitBoxV128(il);
        }

        private static readonly FieldInfo MemoriesField =
            typeof(ThinContext).GetField(nameof(ThinContext.Memories))!;
        private static readonly FieldInfo MemoryDataField =
            MemoryEmitter.MemoryDataField;

        // === SIMD memory load: [addr (i32)] → V128 boxed as Value ===
        private static void EmitSimdLoad(ILGenerator il, InstructionBase inst, SimdCode op)
        {
            // Get MemArg and determine helper name based on instruction type
            long memOffset;
            int memIndex;
            string helperName;

            if (inst is Wacs.Core.Instructions.SIMD.InstMemoryLoadZero z)
            {
                // LoadZero incorrectly reports Lane opcodes — determine helper from bit width
                memOffset = z.MemOffset; memIndex = z.MemIndex;
                helperName = z.LoadWidth switch
                {
                    4 => nameof(MemoryHelpers.LoadV128_32Zero),
                    8 => nameof(MemoryHelpers.LoadV128_64Zero),
                    _ => throw new TranspilerException($"Unknown LoadZero width: {z.LoadWidth}")
                };
            }
            else
            {
                helperName = op switch
                {
                    SimdCode.V128Load => nameof(MemoryHelpers.LoadV128),
                    SimdCode.V128Load8x8S => nameof(MemoryHelpers.LoadV128_8x8S),
                    SimdCode.V128Load8x8U => nameof(MemoryHelpers.LoadV128_8x8U),
                    SimdCode.V128Load16x4S => nameof(MemoryHelpers.LoadV128_16x4S),
                    SimdCode.V128Load16x4U => nameof(MemoryHelpers.LoadV128_16x4U),
                    SimdCode.V128Load32x2S => nameof(MemoryHelpers.LoadV128_32x2S),
                    SimdCode.V128Load32x2U => nameof(MemoryHelpers.LoadV128_32x2U),
                    SimdCode.V128Load8Splat => nameof(MemoryHelpers.LoadV128_8Splat),
                    SimdCode.V128Load16Splat => nameof(MemoryHelpers.LoadV128_16Splat),
                    SimdCode.V128Load32Splat => nameof(MemoryHelpers.LoadV128_32Splat),
                    SimdCode.V128Load64Splat => nameof(MemoryHelpers.LoadV128_64Splat),
                    SimdCode.V128Load32Zero => nameof(MemoryHelpers.LoadV128_32Zero),
                    SimdCode.V128Load64Zero => nameof(MemoryHelpers.LoadV128_64Zero),
                    _ => throw new TranspilerException($"Unknown SIMD load: {op}")
                };

                if (inst is InstMemoryLoad ml) { memOffset = ml.MemOffset; memIndex = ml.MemIndex; }
                else if (inst is Wacs.Core.Instructions.SIMD.InstMemoryLoadMxN mxn) { memOffset = mxn.MemOffset; memIndex = mxn.MemIndex; }
                else if (inst is Wacs.Core.Instructions.SIMD.InstMemoryLoadSplat spl) { memOffset = spl.MemOffset; memIndex = spl.MemIndex; }
                else throw new TranspilerException($"Cannot get MemArg from {inst.GetType().Name}");
            }

            // Stack: [addr (i32)]
            // Pattern: MemoryHelpers.LoadXxx(byte[] mem, int addr, long offset) → V128
            var addrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, addrLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4, memIndex);
            il.Emit(OpCodes.Ldelem_Ref);              // MemoryInstance
            il.Emit(OpCodes.Ldfld, MemoryDataField);  // byte[] Data
            il.Emit(OpCodes.Ldloc, addrLocal);
            il.Emit(OpCodes.Ldc_I8, memOffset);
            il.Emit(OpCodes.Call, typeof(MemoryHelpers).GetMethod(helperName,
                BindingFlags.Public | BindingFlags.Static)!);
            EmitBoxV128(il);
        }

        // Lane loads: [Value(v128), addr (i32)] → V128
        private static void EmitSimdLoadLane(ILGenerator il, InstructionBase inst, SimdCode op)
        {
            var laneInst = (Wacs.Core.Instructions.SIMD.InstMemoryLoadLane)inst;
            string helperName = op switch
            {
                SimdCode.V128Load8Lane => nameof(MemoryHelpers.LoadV128_8Lane),
                SimdCode.V128Load16Lane => nameof(MemoryHelpers.LoadV128_16Lane),
                SimdCode.V128Load32Lane => nameof(MemoryHelpers.LoadV128_32Lane),
                SimdCode.V128Load64Lane => nameof(MemoryHelpers.LoadV128_64Lane),
                _ => throw new TranspilerException($"Unknown SIMD lane load: {op}")
            };

            // WASM spec: v128.loadN_lane : [i32 addr, v128 vec] → [v128]
            // Stack (bottom→top): [addr, Value(v128)]. v128 is on top.
            var vecLocal = il.DeclareLocal(typeof(V128));
            EmitUnboxV128(il);
            il.Emit(OpCodes.Stloc, vecLocal);
            var addrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, addrLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4, laneInst.MemIndex);
            il.Emit(OpCodes.Ldelem_Ref);              // MemoryInstance
            il.Emit(OpCodes.Ldfld, MemoryDataField);  // byte[] Data
            il.Emit(OpCodes.Ldloc, addrLocal);
            il.Emit(OpCodes.Ldc_I8, laneInst.MemOffset);
            il.Emit(OpCodes.Ldloc, vecLocal);
            il.Emit(OpCodes.Ldc_I4, (int)laneInst.LaneIndex);
            il.Emit(OpCodes.Call, typeof(MemoryHelpers).GetMethod(helperName,
                BindingFlags.Public | BindingFlags.Static)!);
            EmitBoxV128(il);
        }

        // V128 store: [addr (i32), Value(v128)] → void
        private static void EmitSimdStore(ILGenerator il, InstructionBase inst)
        {
            var storeInst = (InstMemoryStore)inst;

            // Stack: [addr (i32), Value(v128)]
            var vecLocal = il.DeclareLocal(typeof(V128));
            EmitUnboxV128(il);
            il.Emit(OpCodes.Stloc, vecLocal);
            var addrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, addrLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4, storeInst.MemIndex);
            il.Emit(OpCodes.Ldelem_Ref);              // MemoryInstance
            il.Emit(OpCodes.Ldfld, MemoryDataField);  // byte[] Data
            il.Emit(OpCodes.Ldloc, addrLocal);
            il.Emit(OpCodes.Ldc_I8, storeInst.MemOffset);
            il.Emit(OpCodes.Ldloc, vecLocal);
            il.Emit(OpCodes.Call, typeof(MemoryHelpers).GetMethod(
                nameof(MemoryHelpers.StoreV128), BindingFlags.Public | BindingFlags.Static)!);
        }

        // Lane stores: [addr (i32), Value(v128)] → void
        private static void EmitSimdStoreLane(ILGenerator il, InstructionBase inst, SimdCode op)
        {
            var laneInst = (Wacs.Core.Instructions.SIMD.InstMemoryStoreLane)inst;
            string helperName = op switch
            {
                SimdCode.V128Store8Lane => nameof(MemoryHelpers.StoreV128_8Lane),
                SimdCode.V128Store16Lane => nameof(MemoryHelpers.StoreV128_16Lane),
                SimdCode.V128Store32Lane => nameof(MemoryHelpers.StoreV128_32Lane),
                SimdCode.V128Store64Lane => nameof(MemoryHelpers.StoreV128_64Lane),
                _ => throw new TranspilerException($"Unknown SIMD lane store: {op}")
            };

            // Stack: [addr (i32), Value(v128)]
            var vecLocal = il.DeclareLocal(typeof(V128));
            EmitUnboxV128(il);
            il.Emit(OpCodes.Stloc, vecLocal);
            var addrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, addrLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4, laneInst.MemIndex);
            il.Emit(OpCodes.Ldelem_Ref);              // MemoryInstance
            il.Emit(OpCodes.Ldfld, MemoryDataField);  // byte[] Data
            il.Emit(OpCodes.Ldloc, addrLocal);
            il.Emit(OpCodes.Ldc_I8, laneInst.MemOffset);
            il.Emit(OpCodes.Ldloc, vecLocal);
            il.Emit(OpCodes.Ldc_I4, (int)laneInst.LaneIndex);
            il.Emit(OpCodes.Call, typeof(MemoryHelpers).GetMethod(helperName,
                BindingFlags.Public | BindingFlags.Static)!);
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
        public static void PushValue(ThinContext ctx, Value val)
        {
            if (ctx.ExecContext == null)
                throw new TrapException("SIMD ops require ExecContext");
            ctx.ExecContext.OpStack.PushValue(val);
        }

        public static Value PopValue(ThinContext ctx)
        {
            if (ctx.ExecContext == null)
                throw new TrapException("SIMD ops require ExecContext");
            return ctx.ExecContext.OpStack.PopAny();
        }

        public static V128 GetShuffleLanes(ThinContext ctx, int instructionId)
        {
            var inst = SimdEmitter.InstructionRegistry[instructionId];
            return ((Wacs.Core.Instructions.InstShuffleOp)inst).LaneIndices;
        }

        public static V128 GetV128Const(int instructionId)
        {
            var inst = SimdEmitter.InstructionRegistry[instructionId];
            return ((Wacs.Core.Instructions.Simd.InstV128Const)inst).Value;
        }

        public static void ExecuteSimdOp(ThinContext ctx, int instructionId)
        {
            if (ctx.ExecContext == null)
                throw new TrapException("SIMD ops require ExecContext");
            var inst = SimdEmitter.InstructionRegistry[instructionId];
            inst.Execute(ctx.ExecContext);
        }
    }
}
