// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Compilation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;

namespace Wacs.Core.Instructions.SIMD
{
    /// <summary>
    /// [OpHandler] entries for the shape ops — splat (broadcast scalar to all lanes),
    /// extract_lane (pull one lane as a scalar), replace_lane (write one lane from a
    /// scalar). Extract/replace take a 1-byte lane-index immediate.
    /// </summary>
    internal static class VLaneHandlers
    {
        // ── Splat ────────────────────────────────────────────────────────────────
        // Broadcast a scalar argument to every lane of the result v128.

        [OpHandler(SimdCode.I8x16Splat)]
        private static V128 I8x16Splat(uint v)
        {
            byte b = (byte)v;
            return new V128(b, b, b, b, b, b, b, b, b, b, b, b, b, b, b, b);
        }

        [OpHandler(SimdCode.I16x8Splat)]
        private static V128 I16x8Splat(uint v)
        {
            ushort s = (ushort)v;
            return new V128(s, s, s, s, s, s, s, s);
        }

        [OpHandler(SimdCode.I32x4Splat)]
        private static V128 I32x4Splat(uint v) => new V128(v, v, v, v);

        [OpHandler(SimdCode.I64x2Splat)]
        private static V128 I64x2Splat(ulong v) => new V128(v, v);

        [OpHandler(SimdCode.F32x4Splat)]
        private static V128 F32x4Splat(float v) => new V128(v, v, v, v);

        [OpHandler(SimdCode.F64x2Splat)]
        private static V128 F64x2Splat(double v) => new V128(v, v);

        // ── ExtractLane ───────────────────────────────────────────────────────────
        // Pull one lane as a scalar. The [Imm] byte is the lane index (0..N-1).

        [OpHandler(SimdCode.I8x16ExtractLaneS)]
        private static int I8x16ExtractLaneS([Imm] byte lane, V128 v) => v[(sbyte)lane];

        [OpHandler(SimdCode.I8x16ExtractLaneU)]
        private static int I8x16ExtractLaneU([Imm] byte lane, V128 v) => v[(byte)lane];

        [OpHandler(SimdCode.I16x8ExtractLaneS)]
        private static int I16x8ExtractLaneS([Imm] byte lane, V128 v) => v[(short)lane];

        [OpHandler(SimdCode.I16x8ExtractLaneU)]
        private static int I16x8ExtractLaneU([Imm] byte lane, V128 v) => v[(ushort)lane];

        [OpHandler(SimdCode.I32x4ExtractLane)]
        private static int I32x4ExtractLane([Imm] byte lane, V128 v) => v[(int)lane];

        [OpHandler(SimdCode.I64x2ExtractLane)]
        private static long I64x2ExtractLane([Imm] byte lane, V128 v) => v[(long)lane];

        [OpHandler(SimdCode.F32x4ExtractLane)]
        private static float F32x4ExtractLane([Imm] byte lane, V128 v) => v[(float)lane];

        [OpHandler(SimdCode.F64x2ExtractLane)]
        private static double F64x2ExtractLane([Imm] byte lane, V128 v) => v[(double)lane];

        // ── ReplaceLane ───────────────────────────────────────────────────────────
        // Replace one lane from a scalar. Stack order: v128 (bottom), scalar (top).
        // Immediate: lane index.

        [OpHandler(SimdCode.I8x16ReplaceLane)]
        private static V128 I8x16ReplaceLane([Imm] byte lane, V128 v, uint scalar)
        {
            MV128 m = v;
            m[(byte)lane] = (byte)scalar;
            return m;
        }

        [OpHandler(SimdCode.I16x8ReplaceLane)]
        private static V128 I16x8ReplaceLane([Imm] byte lane, V128 v, uint scalar)
        {
            MV128 m = v;
            m[(ushort)lane] = (ushort)scalar;
            return m;
        }

        [OpHandler(SimdCode.I32x4ReplaceLane)]
        private static V128 I32x4ReplaceLane([Imm] byte lane, V128 v, int scalar)
        {
            MV128 m = v;
            m[(int)lane] = scalar;
            return m;
        }

        [OpHandler(SimdCode.I64x2ReplaceLane)]
        private static V128 I64x2ReplaceLane([Imm] byte lane, V128 v, long scalar)
        {
            MV128 m = v;
            m[(long)lane] = scalar;
            return m;
        }

        [OpHandler(SimdCode.F32x4ReplaceLane)]
        private static V128 F32x4ReplaceLane([Imm] byte lane, V128 v, float scalar)
        {
            MV128 m = v;
            m[(float)lane] = scalar;
            return m;
        }

        [OpHandler(SimdCode.F64x2ReplaceLane)]
        private static V128 F64x2ReplaceLane([Imm] byte lane, V128 v, double scalar)
        {
            MV128 m = v;
            m[(double)lane] = scalar;
            return m;
        }
    }
}
