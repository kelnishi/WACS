// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Compilation;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;

namespace Wacs.Core.Instructions.SIMD
{
    /// <summary>
    /// [OpHandler] entries that delegate to the polymorphic Wacs.Core.Instructions.Numeric.NumericInst.ExecuteX
    /// implementations. Every handler here is a one-line call to the already-tested
    /// polymorphic body — same pop/push semantics via ctx.OpStack. This keeps the
    /// switch runtime close to feature-complete for SIMD without re-implementing
    /// 60+ lane-expansion bodies; correctness parity is automatic.
    /// </summary>
    internal static class VDelegateHandlers
    {
        [OpHandler(SimdCode.F32x4ConvertI32x4S)]
        private static void F32x4ConvertI32x4S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF32x4ConvertI32x4S(ctx);

        [OpHandler(SimdCode.F32x4ConvertI32x4U)]
        private static void F32x4ConvertI32x4U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF32x4ConvertI32x4U(ctx);

        [OpHandler(SimdCode.F32x4DemoteF64x2Zero)]
        private static void F32x4DemoteF64x2Zero(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF32x4DemoteF64x2Zero(ctx);

        [OpHandler(SimdCode.F32x4Max)]
        private static void F32x4Max(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF32x4Max(ctx);

        [OpHandler(SimdCode.F32x4Min)]
        private static void F32x4Min(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF32x4Min(ctx);

        [OpHandler(SimdCode.F32x4PMax)]
        private static void F32x4PMax(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF32x4PMax(ctx);

        [OpHandler(SimdCode.F32x4PMin)]
        private static void F32x4PMin(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF32x4PMin(ctx);

        [OpHandler(SimdCode.F64x2ConvertLowI32x4S)]
        private static void F64x2ConvertLowI32x4S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF64x2ConvertLowI32x4S(ctx);

        [OpHandler(SimdCode.F64x2ConvertLowI32x4U)]
        private static void F64x2ConvertLowI32x4U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF64x2ConvertLowI32x4U(ctx);

        [OpHandler(SimdCode.F64x2Max)]
        private static void F64x2Max(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF64x2Max(ctx);

        [OpHandler(SimdCode.F64x2Min)]
        private static void F64x2Min(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF64x2Min(ctx);

        [OpHandler(SimdCode.F64x2PMax)]
        private static void F64x2PMax(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF64x2PMax(ctx);

        [OpHandler(SimdCode.F64x2PMin)]
        private static void F64x2PMin(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF64x2PMin(ctx);

        [OpHandler(SimdCode.F64x2PromoteLowF32x4)]
        private static void F64x2PromoteLowF32x4(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteF64x2PromoteLowF32x4(ctx);

        [OpHandler(SimdCode.I16x8AddSatS)]
        private static void I16x8AddSatS(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8AddSatS(ctx);

        [OpHandler(SimdCode.I16x8AddSatU)]
        private static void I16x8AddSatU(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8AddSatU(ctx);

        [OpHandler(SimdCode.I16x8AvgrU)]
        private static void I16x8AvgrU(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8AvgrU(ctx);

        [OpHandler(SimdCode.I16x8ExtAddPairwiseI8x16S)]
        private static void I16x8ExtAddPairwiseI8x16S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8ExtAddPairwiseI8x16S(ctx);

        [OpHandler(SimdCode.I16x8ExtAddPairwiseI8x16U)]
        private static void I16x8ExtAddPairwiseI8x16U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8ExtAddPairwiseI8x16U(ctx);

        [OpHandler(SimdCode.I16x8ExtMulHighI8x16S)]
        private static void I16x8ExtMulHighI8x16S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8ExtMulHighI8x16S(ctx);

        [OpHandler(SimdCode.I16x8ExtMulHighI8x16U)]
        private static void I16x8ExtMulHighI8x16U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8ExtMulHighI8x16U(ctx);

        [OpHandler(SimdCode.I16x8ExtendHighI8x16S)]
        private static void I16x8ExtendHighI8x16S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8ExtendHighI8x16S(ctx);

        [OpHandler(SimdCode.I16x8ExtendHighI8x16U)]
        private static void I16x8ExtendHighI8x16U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8ExtendHighI8x16U(ctx);

        [OpHandler(SimdCode.I16x8MaxS)]
        private static void I16x8MaxS(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8MaxS(ctx);

        [OpHandler(SimdCode.I16x8MaxU)]
        private static void I16x8MaxU(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8MaxU(ctx);

        [OpHandler(SimdCode.I16x8MinS)]
        private static void I16x8MinS(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8MinS(ctx);

        [OpHandler(SimdCode.I16x8MinU)]
        private static void I16x8MinU(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8MinU(ctx);

        [OpHandler(SimdCode.I16x8Mul)]
        private static void I16x8Mul(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8Mul(ctx);

        [OpHandler(SimdCode.I16x8NarrowI32x4S)]
        private static void I16x8NarrowI32x4S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8NarrowI32x4S(ctx);

        [OpHandler(SimdCode.I16x8NarrowI32x4U)]
        private static void I16x8NarrowI32x4U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8NarrowI32x4U(ctx);

        [OpHandler(SimdCode.I16x8Q15MulRSatS)]
        private static void I16x8Q15MulRSatS(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8Q15MulRSatS(ctx);

        [OpHandler(SimdCode.I16x8SubSatS)]
        private static void I16x8SubSatS(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8SubSatS(ctx);

        [OpHandler(SimdCode.I16x8SubSatU)]
        private static void I16x8SubSatU(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI16x8SubSatU(ctx);

        [OpHandler(SimdCode.I32x4DotI16x8S)]
        private static void I32x4DotI16x8S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4DotI16x8S(ctx);

        [OpHandler(SimdCode.I32x4ExtAddPairwiseI16x8S)]
        private static void I32x4ExtAddPairwiseI16x8S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4ExtAddPairwiseI16x8S(ctx);

        [OpHandler(SimdCode.I32x4ExtAddPairwiseI16x8U)]
        private static void I32x4ExtAddPairwiseI16x8U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4ExtAddPairwiseI16x8U(ctx);

        [OpHandler(SimdCode.I32x4ExtMulHighI16x8S)]
        private static void I32x4ExtMulHighI16x8S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4ExtMulHighI16x8S(ctx);

        [OpHandler(SimdCode.I32x4ExtMulHighI16x8U)]
        private static void I32x4ExtMulHighI16x8U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4ExtMulHighI16x8U(ctx);

        [OpHandler(SimdCode.I32x4ExtendHighI16x8S)]
        private static void I32x4ExtendHighI16x8S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4ExtendHighI16x8S(ctx);

        [OpHandler(SimdCode.I32x4ExtendHighI16x8U)]
        private static void I32x4ExtendHighI16x8U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4ExtendHighI16x8U(ctx);

        [OpHandler(SimdCode.I32x4ExtendLowI16x8S)]
        private static void I32x4ExtendLowI16x8S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4ExtendLowI16x8S(ctx);

        [OpHandler(SimdCode.I32x4ExtendLowI16x8U)]
        private static void I32x4ExtendLowI16x8U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4ExtendLowI16x8U(ctx);

        [OpHandler(SimdCode.I32x4MaxS)]
        private static void I32x4MaxS(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4MaxS(ctx);

        [OpHandler(SimdCode.I32x4MaxU)]
        private static void I32x4MaxU(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4MaxU(ctx);

        [OpHandler(SimdCode.I32x4MinS)]
        private static void I32x4MinS(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4MinS(ctx);

        [OpHandler(SimdCode.I32x4MinU)]
        private static void I32x4MinU(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4MinU(ctx);

        [OpHandler(SimdCode.I32x4TruncSatF32x4S)]
        private static void I32x4TruncSatF32x4S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4TruncSatF32x4S(ctx);

        [OpHandler(SimdCode.I32x4TruncSatF32x4U)]
        private static void I32x4TruncSatF32x4U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4TruncSatF32x4U(ctx);

        [OpHandler(SimdCode.I32x4TruncSatF64x2SZero)]
        private static void I32x4TruncSatF64x2SZero(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4TruncSatF64x2SZero(ctx);

        [OpHandler(SimdCode.I32x4TruncSatF64x2UZero)]
        private static void I32x4TruncSatF64x2UZero(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI32x4TruncSatF64x2UZero(ctx);

        [OpHandler(SimdCode.I64x2ExtendHighI32x4S)]
        private static void I64x2ExtendHighI32x4S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI64x2ExtendHighI32x4S(ctx);

        [OpHandler(SimdCode.I64x2ExtendHighI32x4U)]
        private static void I64x2ExtendHighI32x4U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI64x2ExtendHighI32x4U(ctx);

        [OpHandler(SimdCode.I64x2ExtendLowI32x4S)]
        private static void I64x2ExtendLowI32x4S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI64x2ExtendLowI32x4S(ctx);

        [OpHandler(SimdCode.I64x2ExtendLowI32x4U)]
        private static void I64x2ExtendLowI32x4U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI64x2ExtendLowI32x4U(ctx);

        [OpHandler(SimdCode.I64x2Mul)]
        private static void I64x2Mul(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI64x2Mul(ctx);

        [OpHandler(SimdCode.I8x16AddSatS)]
        private static void I8x16AddSatS(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI8x16AddSatS(ctx);

        [OpHandler(SimdCode.I8x16AddSatU)]
        private static void I8x16AddSatU(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI8x16AddSatU(ctx);

        [OpHandler(SimdCode.I8x16AvgrU)]
        private static void I8x16AvgrU(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI8x16AvgrU(ctx);

        [OpHandler(SimdCode.I8x16MaxS)]
        private static void I8x16MaxS(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI8x16MaxS(ctx);

        [OpHandler(SimdCode.I8x16MaxU)]
        private static void I8x16MaxU(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI8x16MaxU(ctx);

        [OpHandler(SimdCode.I8x16MinS)]
        private static void I8x16MinS(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI8x16MinS(ctx);

        [OpHandler(SimdCode.I8x16MinU)]
        private static void I8x16MinU(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI8x16MinU(ctx);

        [OpHandler(SimdCode.I8x16NarrowI16x8S)]
        private static void I8x16NarrowI16x8S(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI8x16NarrowI16x8S(ctx);

        [OpHandler(SimdCode.I8x16NarrowI16x8U)]
        private static void I8x16NarrowI16x8U(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI8x16NarrowI16x8U(ctx);

        [OpHandler(SimdCode.I8x16SubSatS)]
        private static void I8x16SubSatS(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI8x16SubSatS(ctx);

        [OpHandler(SimdCode.I8x16SubSatU)]
        private static void I8x16SubSatU(ExecContext ctx) => Wacs.Core.Instructions.Numeric.NumericInst.ExecuteI8x16SubSatU(ctx);

    }
}
