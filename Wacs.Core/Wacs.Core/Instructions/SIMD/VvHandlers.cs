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
    /// [OpHandler] entries for the cross-shape bitwise-logical v128 ops (v128.and,
    /// v128.or, v128.xor, v128.not, v128.andnot) and the ternary bitselect. These map
    /// 1:1 to V128 struct operators — the polymorphic ExecuteX bodies are literally
    /// the same lines wrapped in Pop/Push.
    /// </summary>
    internal static class VvHandlers
    {
        [OpHandler(SimdCode.V128And)]
        private static V128 V128And(V128 a, V128 b) => a & b;

        [OpHandler(SimdCode.V128AndNot)]
        private static V128 V128AndNot(V128 a, V128 b) => a & ~b;

        [OpHandler(SimdCode.V128Or)]
        private static V128 V128Or(V128 a, V128 b) => a | b;

        [OpHandler(SimdCode.V128Xor)]
        private static V128 V128Xor(V128 a, V128 b) => a ^ b;

        [OpHandler(SimdCode.V128Not)]
        private static V128 V128Not(V128 a) => ~a;

        // v128.bitselect: (a, b, mask) → (a & mask) | (b & ~mask). Mask is top of stack.
        [OpHandler(SimdCode.V128BitSelect)]
        private static V128 V128Bitselect(V128 a, V128 b, V128 mask)
            => (a & mask) | (b & ~mask);

        // v128.any_true: push 1 if any bit is set. OR-fold the 64-bit halves.
        [OpHandler(SimdCode.V128AnyTrue)]
        private static int V128AnyTrue(V128 v) => (v.I64x2_0 | v.I64x2_1) != 0 ? 1 : 0;

        // ── all_true per shape — every lane non-zero. ────────────────────────────

        [OpHandler(SimdCode.I8x16AllTrue)]
        private static int I8x16AllTrue(V128 v) =>
            v.U8x16_0 != 0 && v.U8x16_1 != 0 && v.U8x16_2 != 0 && v.U8x16_3 != 0
         && v.U8x16_4 != 0 && v.U8x16_5 != 0 && v.U8x16_6 != 0 && v.U8x16_7 != 0
         && v.U8x16_8 != 0 && v.U8x16_9 != 0 && v.U8x16_A != 0 && v.U8x16_B != 0
         && v.U8x16_C != 0 && v.U8x16_D != 0 && v.U8x16_E != 0 && v.U8x16_F != 0
                ? 1 : 0;

        [OpHandler(SimdCode.I16x8AllTrue)]
        private static int I16x8AllTrue(V128 v) =>
            v.I16x8_0 != 0 && v.I16x8_1 != 0 && v.I16x8_2 != 0 && v.I16x8_3 != 0
         && v.I16x8_4 != 0 && v.I16x8_5 != 0 && v.I16x8_6 != 0 && v.I16x8_7 != 0
                ? 1 : 0;

        [OpHandler(SimdCode.I32x4AllTrue)]
        private static int I32x4AllTrue(V128 v) =>
            v.I32x4_0 != 0 && v.I32x4_1 != 0 && v.I32x4_2 != 0 && v.I32x4_3 != 0
                ? 1 : 0;

        [OpHandler(SimdCode.I64x2AllTrue)]
        private static int I64x2AllTrue(V128 v) =>
            v.I64x2_0 != 0 && v.I64x2_1 != 0 ? 1 : 0;

        // ── bitmask — one bit per lane, = (signed-negative ? 1 : 0). ─────────────

        [OpHandler(SimdCode.I8x16Bitmask)]
        private static int I8x16Bitmask(V128 v)
        {
            int mask = 0;
            if (v.I8x16_0 < 0) mask |= 1 << 0;
            if (v.I8x16_1 < 0) mask |= 1 << 1;
            if (v.I8x16_2 < 0) mask |= 1 << 2;
            if (v.I8x16_3 < 0) mask |= 1 << 3;
            if (v.I8x16_4 < 0) mask |= 1 << 4;
            if (v.I8x16_5 < 0) mask |= 1 << 5;
            if (v.I8x16_6 < 0) mask |= 1 << 6;
            if (v.I8x16_7 < 0) mask |= 1 << 7;
            if (v.I8x16_8 < 0) mask |= 1 << 8;
            if (v.I8x16_9 < 0) mask |= 1 << 9;
            if (v.I8x16_A < 0) mask |= 1 << 10;
            if (v.I8x16_B < 0) mask |= 1 << 11;
            if (v.I8x16_C < 0) mask |= 1 << 12;
            if (v.I8x16_D < 0) mask |= 1 << 13;
            if (v.I8x16_E < 0) mask |= 1 << 14;
            if (v.I8x16_F < 0) mask |= 1 << 15;
            return mask;
        }

        [OpHandler(SimdCode.I16x8Bitmask)]
        private static int I16x8Bitmask(V128 v)
        {
            int mask = 0;
            if (v.I16x8_0 < 0) mask |= 1 << 0;
            if (v.I16x8_1 < 0) mask |= 1 << 1;
            if (v.I16x8_2 < 0) mask |= 1 << 2;
            if (v.I16x8_3 < 0) mask |= 1 << 3;
            if (v.I16x8_4 < 0) mask |= 1 << 4;
            if (v.I16x8_5 < 0) mask |= 1 << 5;
            if (v.I16x8_6 < 0) mask |= 1 << 6;
            if (v.I16x8_7 < 0) mask |= 1 << 7;
            return mask;
        }

        [OpHandler(SimdCode.I32x4Bitmask)]
        private static int I32x4Bitmask(V128 v)
        {
            int mask = 0;
            if (v.I32x4_0 < 0) mask |= 1 << 0;
            if (v.I32x4_1 < 0) mask |= 1 << 1;
            if (v.I32x4_2 < 0) mask |= 1 << 2;
            if (v.I32x4_3 < 0) mask |= 1 << 3;
            return mask;
        }

        [OpHandler(SimdCode.I64x2Bitmask)]
        private static int I64x2Bitmask(V128 v)
        {
            int mask = 0;
            if (v.I64x2_0 < 0) mask |= 1 << 0;
            if (v.I64x2_1 < 0) mask |= 1 << 1;
            return mask;
        }
    }
}
