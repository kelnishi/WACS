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
    /// [OpHandler] entries for the shape-wise relational v128 ops. A result lane is
    /// all-ones (0xFF…) when the comparison is true, all-zeros when false — matching
    /// the spec's per-lane boolean mask convention.
    ///
    /// Each family (eq/ne/lt/gt/le/ge × signed/unsigned × 8/16/32/64 × f32/f64) is
    /// one constructor-sized call: build a V128 from per-lane expressions using the
    /// polymorphic V128's typed indexers and tuple constructors.
    /// </summary>
    internal static class VRelHandlers
    {
        // ── i8x16 (16 lanes of sbyte / byte) ──────────────────────────────────────

        [OpHandler(SimdCode.I8x16Eq)]
        private static V128 I8x16Eq(V128 a, V128 b) => new V128(
            (sbyte)(a.I8x16_0 == b.I8x16_0 ? -1 : 0), (sbyte)(a.I8x16_1 == b.I8x16_1 ? -1 : 0),
            (sbyte)(a.I8x16_2 == b.I8x16_2 ? -1 : 0), (sbyte)(a.I8x16_3 == b.I8x16_3 ? -1 : 0),
            (sbyte)(a.I8x16_4 == b.I8x16_4 ? -1 : 0), (sbyte)(a.I8x16_5 == b.I8x16_5 ? -1 : 0),
            (sbyte)(a.I8x16_6 == b.I8x16_6 ? -1 : 0), (sbyte)(a.I8x16_7 == b.I8x16_7 ? -1 : 0),
            (sbyte)(a.I8x16_8 == b.I8x16_8 ? -1 : 0), (sbyte)(a.I8x16_9 == b.I8x16_9 ? -1 : 0),
            (sbyte)(a.I8x16_A == b.I8x16_A ? -1 : 0), (sbyte)(a.I8x16_B == b.I8x16_B ? -1 : 0),
            (sbyte)(a.I8x16_C == b.I8x16_C ? -1 : 0), (sbyte)(a.I8x16_D == b.I8x16_D ? -1 : 0),
            (sbyte)(a.I8x16_E == b.I8x16_E ? -1 : 0), (sbyte)(a.I8x16_F == b.I8x16_F ? -1 : 0));

        [OpHandler(SimdCode.I8x16Ne)]
        private static V128 I8x16Ne(V128 a, V128 b) => new V128(
            (sbyte)(a.I8x16_0 != b.I8x16_0 ? -1 : 0), (sbyte)(a.I8x16_1 != b.I8x16_1 ? -1 : 0),
            (sbyte)(a.I8x16_2 != b.I8x16_2 ? -1 : 0), (sbyte)(a.I8x16_3 != b.I8x16_3 ? -1 : 0),
            (sbyte)(a.I8x16_4 != b.I8x16_4 ? -1 : 0), (sbyte)(a.I8x16_5 != b.I8x16_5 ? -1 : 0),
            (sbyte)(a.I8x16_6 != b.I8x16_6 ? -1 : 0), (sbyte)(a.I8x16_7 != b.I8x16_7 ? -1 : 0),
            (sbyte)(a.I8x16_8 != b.I8x16_8 ? -1 : 0), (sbyte)(a.I8x16_9 != b.I8x16_9 ? -1 : 0),
            (sbyte)(a.I8x16_A != b.I8x16_A ? -1 : 0), (sbyte)(a.I8x16_B != b.I8x16_B ? -1 : 0),
            (sbyte)(a.I8x16_C != b.I8x16_C ? -1 : 0), (sbyte)(a.I8x16_D != b.I8x16_D ? -1 : 0),
            (sbyte)(a.I8x16_E != b.I8x16_E ? -1 : 0), (sbyte)(a.I8x16_F != b.I8x16_F ? -1 : 0));

        [OpHandler(SimdCode.I8x16LtS)]
        private static V128 I8x16LtS(V128 a, V128 b) => new V128(
            (sbyte)(a.I8x16_0 < b.I8x16_0 ? -1 : 0), (sbyte)(a.I8x16_1 < b.I8x16_1 ? -1 : 0),
            (sbyte)(a.I8x16_2 < b.I8x16_2 ? -1 : 0), (sbyte)(a.I8x16_3 < b.I8x16_3 ? -1 : 0),
            (sbyte)(a.I8x16_4 < b.I8x16_4 ? -1 : 0), (sbyte)(a.I8x16_5 < b.I8x16_5 ? -1 : 0),
            (sbyte)(a.I8x16_6 < b.I8x16_6 ? -1 : 0), (sbyte)(a.I8x16_7 < b.I8x16_7 ? -1 : 0),
            (sbyte)(a.I8x16_8 < b.I8x16_8 ? -1 : 0), (sbyte)(a.I8x16_9 < b.I8x16_9 ? -1 : 0),
            (sbyte)(a.I8x16_A < b.I8x16_A ? -1 : 0), (sbyte)(a.I8x16_B < b.I8x16_B ? -1 : 0),
            (sbyte)(a.I8x16_C < b.I8x16_C ? -1 : 0), (sbyte)(a.I8x16_D < b.I8x16_D ? -1 : 0),
            (sbyte)(a.I8x16_E < b.I8x16_E ? -1 : 0), (sbyte)(a.I8x16_F < b.I8x16_F ? -1 : 0));

        [OpHandler(SimdCode.I8x16LtU)]
        private static V128 I8x16LtU(V128 a, V128 b) => new V128(
            (sbyte)(a.U8x16_0 < b.U8x16_0 ? -1 : 0), (sbyte)(a.U8x16_1 < b.U8x16_1 ? -1 : 0),
            (sbyte)(a.U8x16_2 < b.U8x16_2 ? -1 : 0), (sbyte)(a.U8x16_3 < b.U8x16_3 ? -1 : 0),
            (sbyte)(a.U8x16_4 < b.U8x16_4 ? -1 : 0), (sbyte)(a.U8x16_5 < b.U8x16_5 ? -1 : 0),
            (sbyte)(a.U8x16_6 < b.U8x16_6 ? -1 : 0), (sbyte)(a.U8x16_7 < b.U8x16_7 ? -1 : 0),
            (sbyte)(a.U8x16_8 < b.U8x16_8 ? -1 : 0), (sbyte)(a.U8x16_9 < b.U8x16_9 ? -1 : 0),
            (sbyte)(a.U8x16_A < b.U8x16_A ? -1 : 0), (sbyte)(a.U8x16_B < b.U8x16_B ? -1 : 0),
            (sbyte)(a.U8x16_C < b.U8x16_C ? -1 : 0), (sbyte)(a.U8x16_D < b.U8x16_D ? -1 : 0),
            (sbyte)(a.U8x16_E < b.U8x16_E ? -1 : 0), (sbyte)(a.U8x16_F < b.U8x16_F ? -1 : 0));

        [OpHandler(SimdCode.I8x16GtS)]
        private static V128 I8x16GtS(V128 a, V128 b) => new V128(
            (sbyte)(a.I8x16_0 > b.I8x16_0 ? -1 : 0), (sbyte)(a.I8x16_1 > b.I8x16_1 ? -1 : 0),
            (sbyte)(a.I8x16_2 > b.I8x16_2 ? -1 : 0), (sbyte)(a.I8x16_3 > b.I8x16_3 ? -1 : 0),
            (sbyte)(a.I8x16_4 > b.I8x16_4 ? -1 : 0), (sbyte)(a.I8x16_5 > b.I8x16_5 ? -1 : 0),
            (sbyte)(a.I8x16_6 > b.I8x16_6 ? -1 : 0), (sbyte)(a.I8x16_7 > b.I8x16_7 ? -1 : 0),
            (sbyte)(a.I8x16_8 > b.I8x16_8 ? -1 : 0), (sbyte)(a.I8x16_9 > b.I8x16_9 ? -1 : 0),
            (sbyte)(a.I8x16_A > b.I8x16_A ? -1 : 0), (sbyte)(a.I8x16_B > b.I8x16_B ? -1 : 0),
            (sbyte)(a.I8x16_C > b.I8x16_C ? -1 : 0), (sbyte)(a.I8x16_D > b.I8x16_D ? -1 : 0),
            (sbyte)(a.I8x16_E > b.I8x16_E ? -1 : 0), (sbyte)(a.I8x16_F > b.I8x16_F ? -1 : 0));

        [OpHandler(SimdCode.I8x16GtU)]
        private static V128 I8x16GtU(V128 a, V128 b) => new V128(
            (sbyte)(a.U8x16_0 > b.U8x16_0 ? -1 : 0), (sbyte)(a.U8x16_1 > b.U8x16_1 ? -1 : 0),
            (sbyte)(a.U8x16_2 > b.U8x16_2 ? -1 : 0), (sbyte)(a.U8x16_3 > b.U8x16_3 ? -1 : 0),
            (sbyte)(a.U8x16_4 > b.U8x16_4 ? -1 : 0), (sbyte)(a.U8x16_5 > b.U8x16_5 ? -1 : 0),
            (sbyte)(a.U8x16_6 > b.U8x16_6 ? -1 : 0), (sbyte)(a.U8x16_7 > b.U8x16_7 ? -1 : 0),
            (sbyte)(a.U8x16_8 > b.U8x16_8 ? -1 : 0), (sbyte)(a.U8x16_9 > b.U8x16_9 ? -1 : 0),
            (sbyte)(a.U8x16_A > b.U8x16_A ? -1 : 0), (sbyte)(a.U8x16_B > b.U8x16_B ? -1 : 0),
            (sbyte)(a.U8x16_C > b.U8x16_C ? -1 : 0), (sbyte)(a.U8x16_D > b.U8x16_D ? -1 : 0),
            (sbyte)(a.U8x16_E > b.U8x16_E ? -1 : 0), (sbyte)(a.U8x16_F > b.U8x16_F ? -1 : 0));

        [OpHandler(SimdCode.I8x16LeS)]
        private static V128 I8x16LeS(V128 a, V128 b) => new V128(
            (sbyte)(a.I8x16_0 <= b.I8x16_0 ? -1 : 0), (sbyte)(a.I8x16_1 <= b.I8x16_1 ? -1 : 0),
            (sbyte)(a.I8x16_2 <= b.I8x16_2 ? -1 : 0), (sbyte)(a.I8x16_3 <= b.I8x16_3 ? -1 : 0),
            (sbyte)(a.I8x16_4 <= b.I8x16_4 ? -1 : 0), (sbyte)(a.I8x16_5 <= b.I8x16_5 ? -1 : 0),
            (sbyte)(a.I8x16_6 <= b.I8x16_6 ? -1 : 0), (sbyte)(a.I8x16_7 <= b.I8x16_7 ? -1 : 0),
            (sbyte)(a.I8x16_8 <= b.I8x16_8 ? -1 : 0), (sbyte)(a.I8x16_9 <= b.I8x16_9 ? -1 : 0),
            (sbyte)(a.I8x16_A <= b.I8x16_A ? -1 : 0), (sbyte)(a.I8x16_B <= b.I8x16_B ? -1 : 0),
            (sbyte)(a.I8x16_C <= b.I8x16_C ? -1 : 0), (sbyte)(a.I8x16_D <= b.I8x16_D ? -1 : 0),
            (sbyte)(a.I8x16_E <= b.I8x16_E ? -1 : 0), (sbyte)(a.I8x16_F <= b.I8x16_F ? -1 : 0));

        [OpHandler(SimdCode.I8x16LeU)]
        private static V128 I8x16LeU(V128 a, V128 b) => new V128(
            (sbyte)(a.U8x16_0 <= b.U8x16_0 ? -1 : 0), (sbyte)(a.U8x16_1 <= b.U8x16_1 ? -1 : 0),
            (sbyte)(a.U8x16_2 <= b.U8x16_2 ? -1 : 0), (sbyte)(a.U8x16_3 <= b.U8x16_3 ? -1 : 0),
            (sbyte)(a.U8x16_4 <= b.U8x16_4 ? -1 : 0), (sbyte)(a.U8x16_5 <= b.U8x16_5 ? -1 : 0),
            (sbyte)(a.U8x16_6 <= b.U8x16_6 ? -1 : 0), (sbyte)(a.U8x16_7 <= b.U8x16_7 ? -1 : 0),
            (sbyte)(a.U8x16_8 <= b.U8x16_8 ? -1 : 0), (sbyte)(a.U8x16_9 <= b.U8x16_9 ? -1 : 0),
            (sbyte)(a.U8x16_A <= b.U8x16_A ? -1 : 0), (sbyte)(a.U8x16_B <= b.U8x16_B ? -1 : 0),
            (sbyte)(a.U8x16_C <= b.U8x16_C ? -1 : 0), (sbyte)(a.U8x16_D <= b.U8x16_D ? -1 : 0),
            (sbyte)(a.U8x16_E <= b.U8x16_E ? -1 : 0), (sbyte)(a.U8x16_F <= b.U8x16_F ? -1 : 0));

        [OpHandler(SimdCode.I8x16GeS)]
        private static V128 I8x16GeS(V128 a, V128 b) => new V128(
            (sbyte)(a.I8x16_0 >= b.I8x16_0 ? -1 : 0), (sbyte)(a.I8x16_1 >= b.I8x16_1 ? -1 : 0),
            (sbyte)(a.I8x16_2 >= b.I8x16_2 ? -1 : 0), (sbyte)(a.I8x16_3 >= b.I8x16_3 ? -1 : 0),
            (sbyte)(a.I8x16_4 >= b.I8x16_4 ? -1 : 0), (sbyte)(a.I8x16_5 >= b.I8x16_5 ? -1 : 0),
            (sbyte)(a.I8x16_6 >= b.I8x16_6 ? -1 : 0), (sbyte)(a.I8x16_7 >= b.I8x16_7 ? -1 : 0),
            (sbyte)(a.I8x16_8 >= b.I8x16_8 ? -1 : 0), (sbyte)(a.I8x16_9 >= b.I8x16_9 ? -1 : 0),
            (sbyte)(a.I8x16_A >= b.I8x16_A ? -1 : 0), (sbyte)(a.I8x16_B >= b.I8x16_B ? -1 : 0),
            (sbyte)(a.I8x16_C >= b.I8x16_C ? -1 : 0), (sbyte)(a.I8x16_D >= b.I8x16_D ? -1 : 0),
            (sbyte)(a.I8x16_E >= b.I8x16_E ? -1 : 0), (sbyte)(a.I8x16_F >= b.I8x16_F ? -1 : 0));

        [OpHandler(SimdCode.I8x16GeU)]
        private static V128 I8x16GeU(V128 a, V128 b) => new V128(
            (sbyte)(a.U8x16_0 >= b.U8x16_0 ? -1 : 0), (sbyte)(a.U8x16_1 >= b.U8x16_1 ? -1 : 0),
            (sbyte)(a.U8x16_2 >= b.U8x16_2 ? -1 : 0), (sbyte)(a.U8x16_3 >= b.U8x16_3 ? -1 : 0),
            (sbyte)(a.U8x16_4 >= b.U8x16_4 ? -1 : 0), (sbyte)(a.U8x16_5 >= b.U8x16_5 ? -1 : 0),
            (sbyte)(a.U8x16_6 >= b.U8x16_6 ? -1 : 0), (sbyte)(a.U8x16_7 >= b.U8x16_7 ? -1 : 0),
            (sbyte)(a.U8x16_8 >= b.U8x16_8 ? -1 : 0), (sbyte)(a.U8x16_9 >= b.U8x16_9 ? -1 : 0),
            (sbyte)(a.U8x16_A >= b.U8x16_A ? -1 : 0), (sbyte)(a.U8x16_B >= b.U8x16_B ? -1 : 0),
            (sbyte)(a.U8x16_C >= b.U8x16_C ? -1 : 0), (sbyte)(a.U8x16_D >= b.U8x16_D ? -1 : 0),
            (sbyte)(a.U8x16_E >= b.U8x16_E ? -1 : 0), (sbyte)(a.U8x16_F >= b.U8x16_F ? -1 : 0));

        // ── i16x8 (8 lanes of short / ushort) ──────────────────────────────────

        [OpHandler(SimdCode.I16x8Eq)]
        private static V128 I16x8Eq(V128 a, V128 b) => new V128(
            (short)(a.I16x8_0 == b.I16x8_0 ? -1 : 0), (short)(a.I16x8_1 == b.I16x8_1 ? -1 : 0),
            (short)(a.I16x8_2 == b.I16x8_2 ? -1 : 0), (short)(a.I16x8_3 == b.I16x8_3 ? -1 : 0),
            (short)(a.I16x8_4 == b.I16x8_4 ? -1 : 0), (short)(a.I16x8_5 == b.I16x8_5 ? -1 : 0),
            (short)(a.I16x8_6 == b.I16x8_6 ? -1 : 0), (short)(a.I16x8_7 == b.I16x8_7 ? -1 : 0));

        [OpHandler(SimdCode.I16x8Ne)]
        private static V128 I16x8Ne(V128 a, V128 b) => new V128(
            (short)(a.I16x8_0 != b.I16x8_0 ? -1 : 0), (short)(a.I16x8_1 != b.I16x8_1 ? -1 : 0),
            (short)(a.I16x8_2 != b.I16x8_2 ? -1 : 0), (short)(a.I16x8_3 != b.I16x8_3 ? -1 : 0),
            (short)(a.I16x8_4 != b.I16x8_4 ? -1 : 0), (short)(a.I16x8_5 != b.I16x8_5 ? -1 : 0),
            (short)(a.I16x8_6 != b.I16x8_6 ? -1 : 0), (short)(a.I16x8_7 != b.I16x8_7 ? -1 : 0));

        [OpHandler(SimdCode.I16x8LtS)]
        private static V128 I16x8LtS(V128 a, V128 b) => new V128(
            (short)(a.I16x8_0 < b.I16x8_0 ? -1 : 0), (short)(a.I16x8_1 < b.I16x8_1 ? -1 : 0),
            (short)(a.I16x8_2 < b.I16x8_2 ? -1 : 0), (short)(a.I16x8_3 < b.I16x8_3 ? -1 : 0),
            (short)(a.I16x8_4 < b.I16x8_4 ? -1 : 0), (short)(a.I16x8_5 < b.I16x8_5 ? -1 : 0),
            (short)(a.I16x8_6 < b.I16x8_6 ? -1 : 0), (short)(a.I16x8_7 < b.I16x8_7 ? -1 : 0));

        [OpHandler(SimdCode.I16x8LtU)]
        private static V128 I16x8LtU(V128 a, V128 b) => new V128(
            (short)(a.U16x8_0 < b.U16x8_0 ? -1 : 0), (short)(a.U16x8_1 < b.U16x8_1 ? -1 : 0),
            (short)(a.U16x8_2 < b.U16x8_2 ? -1 : 0), (short)(a.U16x8_3 < b.U16x8_3 ? -1 : 0),
            (short)(a.U16x8_4 < b.U16x8_4 ? -1 : 0), (short)(a.U16x8_5 < b.U16x8_5 ? -1 : 0),
            (short)(a.U16x8_6 < b.U16x8_6 ? -1 : 0), (short)(a.U16x8_7 < b.U16x8_7 ? -1 : 0));

        [OpHandler(SimdCode.I16x8GtS)]
        private static V128 I16x8GtS(V128 a, V128 b) => new V128(
            (short)(a.I16x8_0 > b.I16x8_0 ? -1 : 0), (short)(a.I16x8_1 > b.I16x8_1 ? -1 : 0),
            (short)(a.I16x8_2 > b.I16x8_2 ? -1 : 0), (short)(a.I16x8_3 > b.I16x8_3 ? -1 : 0),
            (short)(a.I16x8_4 > b.I16x8_4 ? -1 : 0), (short)(a.I16x8_5 > b.I16x8_5 ? -1 : 0),
            (short)(a.I16x8_6 > b.I16x8_6 ? -1 : 0), (short)(a.I16x8_7 > b.I16x8_7 ? -1 : 0));

        [OpHandler(SimdCode.I16x8GtU)]
        private static V128 I16x8GtU(V128 a, V128 b) => new V128(
            (short)(a.U16x8_0 > b.U16x8_0 ? -1 : 0), (short)(a.U16x8_1 > b.U16x8_1 ? -1 : 0),
            (short)(a.U16x8_2 > b.U16x8_2 ? -1 : 0), (short)(a.U16x8_3 > b.U16x8_3 ? -1 : 0),
            (short)(a.U16x8_4 > b.U16x8_4 ? -1 : 0), (short)(a.U16x8_5 > b.U16x8_5 ? -1 : 0),
            (short)(a.U16x8_6 > b.U16x8_6 ? -1 : 0), (short)(a.U16x8_7 > b.U16x8_7 ? -1 : 0));

        [OpHandler(SimdCode.I16x8LeS)]
        private static V128 I16x8LeS(V128 a, V128 b) => new V128(
            (short)(a.I16x8_0 <= b.I16x8_0 ? -1 : 0), (short)(a.I16x8_1 <= b.I16x8_1 ? -1 : 0),
            (short)(a.I16x8_2 <= b.I16x8_2 ? -1 : 0), (short)(a.I16x8_3 <= b.I16x8_3 ? -1 : 0),
            (short)(a.I16x8_4 <= b.I16x8_4 ? -1 : 0), (short)(a.I16x8_5 <= b.I16x8_5 ? -1 : 0),
            (short)(a.I16x8_6 <= b.I16x8_6 ? -1 : 0), (short)(a.I16x8_7 <= b.I16x8_7 ? -1 : 0));

        [OpHandler(SimdCode.I16x8LeU)]
        private static V128 I16x8LeU(V128 a, V128 b) => new V128(
            (short)(a.U16x8_0 <= b.U16x8_0 ? -1 : 0), (short)(a.U16x8_1 <= b.U16x8_1 ? -1 : 0),
            (short)(a.U16x8_2 <= b.U16x8_2 ? -1 : 0), (short)(a.U16x8_3 <= b.U16x8_3 ? -1 : 0),
            (short)(a.U16x8_4 <= b.U16x8_4 ? -1 : 0), (short)(a.U16x8_5 <= b.U16x8_5 ? -1 : 0),
            (short)(a.U16x8_6 <= b.U16x8_6 ? -1 : 0), (short)(a.U16x8_7 <= b.U16x8_7 ? -1 : 0));

        [OpHandler(SimdCode.I16x8GeS)]
        private static V128 I16x8GeS(V128 a, V128 b) => new V128(
            (short)(a.I16x8_0 >= b.I16x8_0 ? -1 : 0), (short)(a.I16x8_1 >= b.I16x8_1 ? -1 : 0),
            (short)(a.I16x8_2 >= b.I16x8_2 ? -1 : 0), (short)(a.I16x8_3 >= b.I16x8_3 ? -1 : 0),
            (short)(a.I16x8_4 >= b.I16x8_4 ? -1 : 0), (short)(a.I16x8_5 >= b.I16x8_5 ? -1 : 0),
            (short)(a.I16x8_6 >= b.I16x8_6 ? -1 : 0), (short)(a.I16x8_7 >= b.I16x8_7 ? -1 : 0));

        [OpHandler(SimdCode.I16x8GeU)]
        private static V128 I16x8GeU(V128 a, V128 b) => new V128(
            (short)(a.U16x8_0 >= b.U16x8_0 ? -1 : 0), (short)(a.U16x8_1 >= b.U16x8_1 ? -1 : 0),
            (short)(a.U16x8_2 >= b.U16x8_2 ? -1 : 0), (short)(a.U16x8_3 >= b.U16x8_3 ? -1 : 0),
            (short)(a.U16x8_4 >= b.U16x8_4 ? -1 : 0), (short)(a.U16x8_5 >= b.U16x8_5 ? -1 : 0),
            (short)(a.U16x8_6 >= b.U16x8_6 ? -1 : 0), (short)(a.U16x8_7 >= b.U16x8_7 ? -1 : 0));

        // ── i32x4 (4 lanes of int / uint) ──────────────────────────────────────

        [OpHandler(SimdCode.I32x4Eq)]
        private static V128 I32x4Eq(V128 a, V128 b) => new V128(
            a.I32x4_0 == b.I32x4_0 ? -1 : 0, a.I32x4_1 == b.I32x4_1 ? -1 : 0,
            a.I32x4_2 == b.I32x4_2 ? -1 : 0, a.I32x4_3 == b.I32x4_3 ? -1 : 0);

        [OpHandler(SimdCode.I32x4Ne)]
        private static V128 I32x4Ne(V128 a, V128 b) => new V128(
            a.I32x4_0 != b.I32x4_0 ? -1 : 0, a.I32x4_1 != b.I32x4_1 ? -1 : 0,
            a.I32x4_2 != b.I32x4_2 ? -1 : 0, a.I32x4_3 != b.I32x4_3 ? -1 : 0);

        [OpHandler(SimdCode.I32x4LtS)]
        private static V128 I32x4LtS(V128 a, V128 b) => new V128(
            a.I32x4_0 < b.I32x4_0 ? -1 : 0, a.I32x4_1 < b.I32x4_1 ? -1 : 0,
            a.I32x4_2 < b.I32x4_2 ? -1 : 0, a.I32x4_3 < b.I32x4_3 ? -1 : 0);

        [OpHandler(SimdCode.I32x4LtU)]
        private static V128 I32x4LtU(V128 a, V128 b) => new V128(
            (int)(a.U32x4_0 < b.U32x4_0 ? -1 : 0), (int)(a.U32x4_1 < b.U32x4_1 ? -1 : 0),
            (int)(a.U32x4_2 < b.U32x4_2 ? -1 : 0), (int)(a.U32x4_3 < b.U32x4_3 ? -1 : 0));

        [OpHandler(SimdCode.I32x4GtS)]
        private static V128 I32x4GtS(V128 a, V128 b) => new V128(
            a.I32x4_0 > b.I32x4_0 ? -1 : 0, a.I32x4_1 > b.I32x4_1 ? -1 : 0,
            a.I32x4_2 > b.I32x4_2 ? -1 : 0, a.I32x4_3 > b.I32x4_3 ? -1 : 0);

        [OpHandler(SimdCode.I32x4GtU)]
        private static V128 I32x4GtU(V128 a, V128 b) => new V128(
            (int)(a.U32x4_0 > b.U32x4_0 ? -1 : 0), (int)(a.U32x4_1 > b.U32x4_1 ? -1 : 0),
            (int)(a.U32x4_2 > b.U32x4_2 ? -1 : 0), (int)(a.U32x4_3 > b.U32x4_3 ? -1 : 0));

        [OpHandler(SimdCode.I32x4LeS)]
        private static V128 I32x4LeS(V128 a, V128 b) => new V128(
            a.I32x4_0 <= b.I32x4_0 ? -1 : 0, a.I32x4_1 <= b.I32x4_1 ? -1 : 0,
            a.I32x4_2 <= b.I32x4_2 ? -1 : 0, a.I32x4_3 <= b.I32x4_3 ? -1 : 0);

        [OpHandler(SimdCode.I32x4LeU)]
        private static V128 I32x4LeU(V128 a, V128 b) => new V128(
            (int)(a.U32x4_0 <= b.U32x4_0 ? -1 : 0), (int)(a.U32x4_1 <= b.U32x4_1 ? -1 : 0),
            (int)(a.U32x4_2 <= b.U32x4_2 ? -1 : 0), (int)(a.U32x4_3 <= b.U32x4_3 ? -1 : 0));

        [OpHandler(SimdCode.I32x4GeS)]
        private static V128 I32x4GeS(V128 a, V128 b) => new V128(
            a.I32x4_0 >= b.I32x4_0 ? -1 : 0, a.I32x4_1 >= b.I32x4_1 ? -1 : 0,
            a.I32x4_2 >= b.I32x4_2 ? -1 : 0, a.I32x4_3 >= b.I32x4_3 ? -1 : 0);

        [OpHandler(SimdCode.I32x4GeU)]
        private static V128 I32x4GeU(V128 a, V128 b) => new V128(
            (int)(a.U32x4_0 >= b.U32x4_0 ? -1 : 0), (int)(a.U32x4_1 >= b.U32x4_1 ? -1 : 0),
            (int)(a.U32x4_2 >= b.U32x4_2 ? -1 : 0), (int)(a.U32x4_3 >= b.U32x4_3 ? -1 : 0));

        // ── i64x2 (2 lanes of long — spec only defines signed relational ops) ──

        [OpHandler(SimdCode.I64x2Eq)]
        private static V128 I64x2Eq(V128 a, V128 b) => new V128(
            a.I64x2_0 == b.I64x2_0 ? -1L : 0L, a.I64x2_1 == b.I64x2_1 ? -1L : 0L);

        [OpHandler(SimdCode.I64x2Ne)]
        private static V128 I64x2Ne(V128 a, V128 b) => new V128(
            a.I64x2_0 != b.I64x2_0 ? -1L : 0L, a.I64x2_1 != b.I64x2_1 ? -1L : 0L);

        [OpHandler(SimdCode.I64x2LtS)]
        private static V128 I64x2LtS(V128 a, V128 b) => new V128(
            a.I64x2_0 < b.I64x2_0 ? -1L : 0L, a.I64x2_1 < b.I64x2_1 ? -1L : 0L);

        [OpHandler(SimdCode.I64x2GtS)]
        private static V128 I64x2GtS(V128 a, V128 b) => new V128(
            a.I64x2_0 > b.I64x2_0 ? -1L : 0L, a.I64x2_1 > b.I64x2_1 ? -1L : 0L);

        [OpHandler(SimdCode.I64x2LeS)]
        private static V128 I64x2LeS(V128 a, V128 b) => new V128(
            a.I64x2_0 <= b.I64x2_0 ? -1L : 0L, a.I64x2_1 <= b.I64x2_1 ? -1L : 0L);

        [OpHandler(SimdCode.I64x2GeS)]
        private static V128 I64x2GeS(V128 a, V128 b) => new V128(
            a.I64x2_0 >= b.I64x2_0 ? -1L : 0L, a.I64x2_1 >= b.I64x2_1 ? -1L : 0L);

        // ── f32x4 (4 lanes of float) ──────────────────────────────────────────

        [OpHandler(SimdCode.F32x4Eq)]
        private static V128 F32x4Eq(V128 a, V128 b) => new V128(
            a.F32x4_0 == b.F32x4_0 ? -1 : 0, a.F32x4_1 == b.F32x4_1 ? -1 : 0,
            a.F32x4_2 == b.F32x4_2 ? -1 : 0, a.F32x4_3 == b.F32x4_3 ? -1 : 0);

        [OpHandler(SimdCode.F32x4Ne)]
        private static V128 F32x4Ne(V128 a, V128 b) => new V128(
            a.F32x4_0 != b.F32x4_0 ? -1 : 0, a.F32x4_1 != b.F32x4_1 ? -1 : 0,
            a.F32x4_2 != b.F32x4_2 ? -1 : 0, a.F32x4_3 != b.F32x4_3 ? -1 : 0);

        [OpHandler(SimdCode.F32x4Lt)]
        private static V128 F32x4Lt(V128 a, V128 b) => new V128(
            a.F32x4_0 < b.F32x4_0 ? -1 : 0, a.F32x4_1 < b.F32x4_1 ? -1 : 0,
            a.F32x4_2 < b.F32x4_2 ? -1 : 0, a.F32x4_3 < b.F32x4_3 ? -1 : 0);

        [OpHandler(SimdCode.F32x4Gt)]
        private static V128 F32x4Gt(V128 a, V128 b) => new V128(
            a.F32x4_0 > b.F32x4_0 ? -1 : 0, a.F32x4_1 > b.F32x4_1 ? -1 : 0,
            a.F32x4_2 > b.F32x4_2 ? -1 : 0, a.F32x4_3 > b.F32x4_3 ? -1 : 0);

        [OpHandler(SimdCode.F32x4Le)]
        private static V128 F32x4Le(V128 a, V128 b) => new V128(
            a.F32x4_0 <= b.F32x4_0 ? -1 : 0, a.F32x4_1 <= b.F32x4_1 ? -1 : 0,
            a.F32x4_2 <= b.F32x4_2 ? -1 : 0, a.F32x4_3 <= b.F32x4_3 ? -1 : 0);

        [OpHandler(SimdCode.F32x4Ge)]
        private static V128 F32x4Ge(V128 a, V128 b) => new V128(
            a.F32x4_0 >= b.F32x4_0 ? -1 : 0, a.F32x4_1 >= b.F32x4_1 ? -1 : 0,
            a.F32x4_2 >= b.F32x4_2 ? -1 : 0, a.F32x4_3 >= b.F32x4_3 ? -1 : 0);

        // ── f64x2 (2 lanes of double) ─────────────────────────────────────────

        [OpHandler(SimdCode.F64x2Eq)]
        private static V128 F64x2Eq(V128 a, V128 b) => new V128(
            a.F64x2_0 == b.F64x2_0 ? -1L : 0L, a.F64x2_1 == b.F64x2_1 ? -1L : 0L);

        [OpHandler(SimdCode.F64x2Ne)]
        private static V128 F64x2Ne(V128 a, V128 b) => new V128(
            a.F64x2_0 != b.F64x2_0 ? -1L : 0L, a.F64x2_1 != b.F64x2_1 ? -1L : 0L);

        [OpHandler(SimdCode.F64x2Lt)]
        private static V128 F64x2Lt(V128 a, V128 b) => new V128(
            a.F64x2_0 < b.F64x2_0 ? -1L : 0L, a.F64x2_1 < b.F64x2_1 ? -1L : 0L);

        [OpHandler(SimdCode.F64x2Gt)]
        private static V128 F64x2Gt(V128 a, V128 b) => new V128(
            a.F64x2_0 > b.F64x2_0 ? -1L : 0L, a.F64x2_1 > b.F64x2_1 ? -1L : 0L);

        [OpHandler(SimdCode.F64x2Le)]
        private static V128 F64x2Le(V128 a, V128 b) => new V128(
            a.F64x2_0 <= b.F64x2_0 ? -1L : 0L, a.F64x2_1 <= b.F64x2_1 ? -1L : 0L);

        [OpHandler(SimdCode.F64x2Ge)]
        private static V128 F64x2Ge(V128 a, V128 b) => new V128(
            a.F64x2_0 >= b.F64x2_0 ? -1L : 0L, a.F64x2_1 >= b.F64x2_1 ? -1L : 0L);
    }
}
