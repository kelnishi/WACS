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
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Wacs.Core.Runtime;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Static helper methods for SIMD operations called from transpiled IL.
    ///
    /// Each operation has two implementations:
    /// 1. Scalar: uses V128 operator overloads, matches interpreter exactly
    /// 2. Intrinsics: uses System.Runtime.Intrinsics.Vector128, hardware-accelerated
    ///
    /// The scalar implementation is the spec-compliant reference.
    /// The intrinsics implementation is the performance target.
    /// Selection is gated by transpiler options (future).
    ///
    /// V128 boxing/unboxing:
    ///   V128 values on the CIL stack are stored in Value (via VecRef/IGcRef).
    ///   UnboxV128 extracts the raw V128 struct.
    ///   BoxV128 wraps it back into Value.
    /// </summary>
    public static class SimdHelpers
    {
        // ================================================================
        // Boxing / Unboxing
        // ================================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 UnboxV128(Value v) => ((VecRef)v.GcRef!).V128;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Value BoxV128(V128 v) => new Value(v);

        // ================================================================
        // VConst — v128.const
        // ================================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128Const(V128 value) => value;

        // ================================================================
        // Bitwise ops — Scalar reference implementations
        // Mirror: Wacs.Core/Instructions/SIMD/VvUnOp.cs, VvBinOp.cs, VvTernOp.cs, VvTestOp.cs
        // ================================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128Not(V128 v) => ~v;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128And(V128 a, V128 b) => a & b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128Or(V128 a, V128 b) => a | b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128Xor(V128 a, V128 b) => a ^ b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128AndNot(V128 a, V128 b) => a & ~b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128BitSelect(V128 v1, V128 v2, V128 v3)
            => (v1 & v3) | (v2 & ~v3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int V128AnyTrue(V128 v)
            => (v.U64x2_0 != 0UL || v.U64x2_1 != 0UL) ? 1 : 0;

        // ================================================================
        // Integer arithmetic — add/sub/mul per shape
        // Mirror: Wacs.Core/Instructions/SIMD/ViBinOp.cs
        // ================================================================

        public static V128 I8x16Add(V128 a, V128 b) => new V128(
            (byte)(a.U8x16_0+b.U8x16_0), (byte)(a.U8x16_1+b.U8x16_1),
            (byte)(a.U8x16_2+b.U8x16_2), (byte)(a.U8x16_3+b.U8x16_3),
            (byte)(a.U8x16_4+b.U8x16_4), (byte)(a.U8x16_5+b.U8x16_5),
            (byte)(a.U8x16_6+b.U8x16_6), (byte)(a.U8x16_7+b.U8x16_7),
            (byte)(a.U8x16_8+b.U8x16_8), (byte)(a.U8x16_9+b.U8x16_9),
            (byte)(a.U8x16_A+b.U8x16_A), (byte)(a.U8x16_B+b.U8x16_B),
            (byte)(a.U8x16_C+b.U8x16_C), (byte)(a.U8x16_D+b.U8x16_D),
            (byte)(a.U8x16_E+b.U8x16_E), (byte)(a.U8x16_F+b.U8x16_F));

        public static V128 I8x16Sub(V128 a, V128 b) => new V128(
            (byte)(a.U8x16_0-b.U8x16_0), (byte)(a.U8x16_1-b.U8x16_1),
            (byte)(a.U8x16_2-b.U8x16_2), (byte)(a.U8x16_3-b.U8x16_3),
            (byte)(a.U8x16_4-b.U8x16_4), (byte)(a.U8x16_5-b.U8x16_5),
            (byte)(a.U8x16_6-b.U8x16_6), (byte)(a.U8x16_7-b.U8x16_7),
            (byte)(a.U8x16_8-b.U8x16_8), (byte)(a.U8x16_9-b.U8x16_9),
            (byte)(a.U8x16_A-b.U8x16_A), (byte)(a.U8x16_B-b.U8x16_B),
            (byte)(a.U8x16_C-b.U8x16_C), (byte)(a.U8x16_D-b.U8x16_D),
            (byte)(a.U8x16_E-b.U8x16_E), (byte)(a.U8x16_F-b.U8x16_F));

        public static V128 I16x8Add(V128 a, V128 b) => new V128(
            (short)(a.I16x8_0+b.I16x8_0), (short)(a.I16x8_1+b.I16x8_1),
            (short)(a.I16x8_2+b.I16x8_2), (short)(a.I16x8_3+b.I16x8_3),
            (short)(a.I16x8_4+b.I16x8_4), (short)(a.I16x8_5+b.I16x8_5),
            (short)(a.I16x8_6+b.I16x8_6), (short)(a.I16x8_7+b.I16x8_7));

        public static V128 I16x8Sub(V128 a, V128 b) => new V128(
            (short)(a.I16x8_0-b.I16x8_0), (short)(a.I16x8_1-b.I16x8_1),
            (short)(a.I16x8_2-b.I16x8_2), (short)(a.I16x8_3-b.I16x8_3),
            (short)(a.I16x8_4-b.I16x8_4), (short)(a.I16x8_5-b.I16x8_5),
            (short)(a.I16x8_6-b.I16x8_6), (short)(a.I16x8_7-b.I16x8_7));

        public static V128 I16x8Mul(V128 a, V128 b) => new V128(
            (short)(a.I16x8_0*b.I16x8_0), (short)(a.I16x8_1*b.I16x8_1),
            (short)(a.I16x8_2*b.I16x8_2), (short)(a.I16x8_3*b.I16x8_3),
            (short)(a.I16x8_4*b.I16x8_4), (short)(a.I16x8_5*b.I16x8_5),
            (short)(a.I16x8_6*b.I16x8_6), (short)(a.I16x8_7*b.I16x8_7));

        public static V128 I32x4Add(V128 a, V128 b) => new V128(
            a.I32x4_0+b.I32x4_0, a.I32x4_1+b.I32x4_1,
            a.I32x4_2+b.I32x4_2, a.I32x4_3+b.I32x4_3);

        public static V128 I32x4Sub(V128 a, V128 b) => new V128(
            a.I32x4_0-b.I32x4_0, a.I32x4_1-b.I32x4_1,
            a.I32x4_2-b.I32x4_2, a.I32x4_3-b.I32x4_3);

        public static V128 I32x4Mul(V128 a, V128 b) => new V128(
            a.I32x4_0*b.I32x4_0, a.I32x4_1*b.I32x4_1,
            a.I32x4_2*b.I32x4_2, a.I32x4_3*b.I32x4_3);

        public static V128 I64x2Add(V128 a, V128 b) =>
            new V128(a.I64x2_0+b.I64x2_0, a.I64x2_1+b.I64x2_1);

        public static V128 I64x2Sub(V128 a, V128 b) =>
            new V128(a.I64x2_0-b.I64x2_0, a.I64x2_1-b.I64x2_1);

        public static V128 I64x2Mul(V128 a, V128 b) =>
            new V128(a.I64x2_0*b.I64x2_0, a.I64x2_1*b.I64x2_1);

        // ================================================================
        // Integer unary — abs, neg, popcnt
        // Mirror: Wacs.Core/Instructions/SIMD/ViUnOp.cs
        // ================================================================

        public static V128 I8x16Abs(V128 v) => new V128(
            v.I8x16_0 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_0),
            v.I8x16_1 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_1),
            v.I8x16_2 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_2),
            v.I8x16_3 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_3),
            v.I8x16_4 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_4),
            v.I8x16_5 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_5),
            v.I8x16_6 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_6),
            v.I8x16_7 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_7),
            v.I8x16_8 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_8),
            v.I8x16_9 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_9),
            v.I8x16_A == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_A),
            v.I8x16_B == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_B),
            v.I8x16_C == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_C),
            v.I8x16_D == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_D),
            v.I8x16_E == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_E),
            v.I8x16_F == sbyte.MinValue ? sbyte.MinValue : Math.Abs(v.I8x16_F));

        public static V128 I16x8Abs(V128 v) => new V128(
            v.I16x8_0 == short.MinValue ? short.MinValue : Math.Abs(v.I16x8_0),
            v.I16x8_1 == short.MinValue ? short.MinValue : Math.Abs(v.I16x8_1),
            v.I16x8_2 == short.MinValue ? short.MinValue : Math.Abs(v.I16x8_2),
            v.I16x8_3 == short.MinValue ? short.MinValue : Math.Abs(v.I16x8_3),
            v.I16x8_4 == short.MinValue ? short.MinValue : Math.Abs(v.I16x8_4),
            v.I16x8_5 == short.MinValue ? short.MinValue : Math.Abs(v.I16x8_5),
            v.I16x8_6 == short.MinValue ? short.MinValue : Math.Abs(v.I16x8_6),
            v.I16x8_7 == short.MinValue ? short.MinValue : Math.Abs(v.I16x8_7));

        public static V128 I32x4Abs(V128 v) => new V128(
            v.I32x4_0 == int.MinValue ? int.MinValue : Math.Abs(v.I32x4_0),
            v.I32x4_1 == int.MinValue ? int.MinValue : Math.Abs(v.I32x4_1),
            v.I32x4_2 == int.MinValue ? int.MinValue : Math.Abs(v.I32x4_2),
            v.I32x4_3 == int.MinValue ? int.MinValue : Math.Abs(v.I32x4_3));

        public static V128 I64x2Abs(V128 v) => new V128(
            v.I64x2_0 == long.MinValue ? long.MinValue : Math.Abs(v.I64x2_0),
            v.I64x2_1 == long.MinValue ? long.MinValue : Math.Abs(v.I64x2_1));

        public static V128 I8x16Neg(V128 v) => new V128(
            (byte)(0-v.U8x16_0), (byte)(0-v.U8x16_1), (byte)(0-v.U8x16_2), (byte)(0-v.U8x16_3),
            (byte)(0-v.U8x16_4), (byte)(0-v.U8x16_5), (byte)(0-v.U8x16_6), (byte)(0-v.U8x16_7),
            (byte)(0-v.U8x16_8), (byte)(0-v.U8x16_9), (byte)(0-v.U8x16_A), (byte)(0-v.U8x16_B),
            (byte)(0-v.U8x16_C), (byte)(0-v.U8x16_D), (byte)(0-v.U8x16_E), (byte)(0-v.U8x16_F));

        public static V128 I16x8Neg(V128 v) => new V128(
            (short)(-v.I16x8_0), (short)(-v.I16x8_1), (short)(-v.I16x8_2), (short)(-v.I16x8_3),
            (short)(-v.I16x8_4), (short)(-v.I16x8_5), (short)(-v.I16x8_6), (short)(-v.I16x8_7));

        public static V128 I32x4Neg(V128 v) =>
            new V128(-v.I32x4_0, -v.I32x4_1, -v.I32x4_2, -v.I32x4_3);

        public static V128 I64x2Neg(V128 v) =>
            new V128(-v.I64x2_0, -v.I64x2_1);

        private static byte PopCount(sbyte value)
        {
            byte count = 0;
            for (int i = 0; i < 8; i++)
                count += (byte)((value >> i) & 1);
            return count;
        }

        public static V128 I8x16Popcnt(V128 v) => new V128(
            PopCount(v.I8x16_0), PopCount(v.I8x16_1), PopCount(v.I8x16_2), PopCount(v.I8x16_3),
            PopCount(v.I8x16_4), PopCount(v.I8x16_5), PopCount(v.I8x16_6), PopCount(v.I8x16_7),
            PopCount(v.I8x16_8), PopCount(v.I8x16_9), PopCount(v.I8x16_A), PopCount(v.I8x16_B),
            PopCount(v.I8x16_C), PopCount(v.I8x16_D), PopCount(v.I8x16_E), PopCount(v.I8x16_F));

        // ================================================================
        // Integer test — all_true, bitmask
        // Mirror: Wacs.Core/Instructions/SIMD/ViTestOp.cs, ViInjectOp.cs
        // ================================================================

        public static int I8x16AllTrue(V128 c) =>
            (c[(byte)0] != 0 && c[(byte)1] != 0 && c[(byte)2] != 0 && c[(byte)3] != 0 &&
             c[(byte)4] != 0 && c[(byte)5] != 0 && c[(byte)6] != 0 && c[(byte)7] != 0 &&
             c[(byte)8] != 0 && c[(byte)9] != 0 && c[(byte)0xA] != 0 && c[(byte)0xB] != 0 &&
             c[(byte)0xC] != 0 && c[(byte)0xD] != 0 && c[(byte)0xE] != 0 && c[(byte)0xF] != 0) ? 1 : 0;

        public static int I16x8AllTrue(V128 c) =>
            (c[(short)0] != 0 && c[(short)1] != 0 && c[(short)2] != 0 && c[(short)3] != 0 &&
             c[(short)4] != 0 && c[(short)5] != 0 && c[(short)6] != 0 && c[(short)7] != 0) ? 1 : 0;

        public static int I32x4AllTrue(V128 c) =>
            (c[(int)0] != 0 && c[(int)1] != 0 && c[(int)2] != 0 && c[(int)3] != 0) ? 1 : 0;

        public static int I64x2AllTrue(V128 c) =>
            (c[(long)0] != 0 && c[(long)1] != 0) ? 1 : 0;

        public static int I8x16Bitmask(V128 c) =>
            ((c[(byte)0x0] & 0x80) >> 7) | ((c[(byte)0x1] & 0x80) >> 6)
            | ((c[(byte)0x2] & 0x80) >> 5) | ((c[(byte)0x3] & 0x80) >> 4)
            | ((c[(byte)0x4] & 0x80) >> 3) | ((c[(byte)0x5] & 0x80) >> 2)
            | ((c[(byte)0x6] & 0x80) >> 1) | ((c[(byte)0x7] & 0x80) >> 0)
            | ((c[(byte)0x8] & 0x80) << 1) | ((c[(byte)0x9] & 0x80) << 2)
            | ((c[(byte)0xA] & 0x80) << 3) | ((c[(byte)0xB] & 0x80) << 4)
            | ((c[(byte)0xC] & 0x80) << 5) | ((c[(byte)0xD] & 0x80) << 6)
            | ((c[(byte)0xE] & 0x80) << 7) | ((c[(byte)0xF] & 0x80) << 8);

        public static int I16x8Bitmask(V128 c) =>
            ((c[(ushort)0x0] & 0x8000) >> 0xF) | ((c[(ushort)0x1] & 0x8000) >> 0xE)
            | ((c[(ushort)0x2] & 0x8000) >> 0xD) | ((c[(ushort)0x3] & 0x8000) >> 0xC)
            | ((c[(ushort)0x4] & 0x8000) >> 0xB) | ((c[(ushort)0x5] & 0x8000) >> 0xA)
            | ((c[(ushort)0x6] & 0x8000) >> 0x9) | ((c[(ushort)0x7] & 0x8000) >> 0x8);

        public static int I32x4Bitmask(V128 c) =>
            (int)(((c[(uint)0x0] & 0x8000_0000) >> 31)
                | ((c[(uint)0x1] & 0x8000_0000) >> 30)
                | ((c[(uint)0x2] & 0x8000_0000) >> 29)
                | ((c[(uint)0x3] & 0x8000_0000) >> 28));

        public static int I64x2Bitmask(V128 c) =>
            (c.I64x2_0 < 0 ? 0b1 : 0) | (c.I64x2_1 < 0 ? 0b10 : 0);

        // ================================================================
        // Splat — scalar to V128
        // Mirror: Wacs.Core/Instructions/SIMD/ViInjectOp.cs, VfInjectOp.cs
        // ================================================================

        public static V128 I8x16Splat(int v)
        {
            byte b = (byte)(uint)v;
            return new V128(b, b, b, b, b, b, b, b, b, b, b, b, b, b, b, b);
        }

        public static V128 I16x8Splat(int v)
        {
            ushort s = (ushort)(uint)v;
            return new V128(s, s, s, s, s, s, s, s);
        }

        public static V128 I32x4Splat(int v)
        {
            uint u = (uint)v;
            return new V128(u, u, u, u);
        }

        public static V128 I64x2Splat(long v)
        {
            ulong u = (ulong)v;
            return new V128(u, u);
        }

        public static V128 F32x4Splat(float v) => new V128(v, v, v, v);

        public static V128 F64x2Splat(double v) => new V128(v, v);

        // ================================================================
        // Extract lane — V128 + lane index to scalar
        // Mirror: Wacs.Core/Instructions/SIMD/VLaneOp.cs
        // ================================================================

        public static int I8x16ExtractLaneS(V128 c, byte lane) => (int)c[(sbyte)lane];
        public static int I8x16ExtractLaneU(V128 c, byte lane) => (int)(uint)c[(byte)lane];
        public static int I16x8ExtractLaneS(V128 c, byte lane) => (int)c[(short)lane];
        public static int I16x8ExtractLaneU(V128 c, byte lane) => (int)(uint)c[(ushort)lane];
        public static int I32x4ExtractLane(V128 c, byte lane) => (int)c[(int)lane];
        public static long I64x2ExtractLane(V128 c, byte lane) => (long)c[(long)lane];
        public static float F32x4ExtractLane(V128 c, byte lane) => (float)c[(float)lane];
        public static double F64x2ExtractLane(V128 c, byte lane) => (double)c[(double)lane];

        // ================================================================
        // Replace lane — V128 + lane index + scalar to V128
        // Mirror: Wacs.Core/Instructions/SIMD/VLaneOp.cs
        // ================================================================

        public static V128 I8x16ReplaceLane(V128 c, byte lane, int v)
        {
            MV128 m = (V128)c;
            m[(byte)lane] = (byte)(uint)v;
            return m;
        }

        public static V128 I16x8ReplaceLane(V128 c, byte lane, int v)
        {
            MV128 m = (V128)c;
            m[(ushort)lane] = (ushort)(uint)v;
            return m;
        }

        public static V128 I32x4ReplaceLane(V128 c, byte lane, int v)
        {
            MV128 m = (V128)c;
            m[(int)lane] = v;
            return m;
        }

        public static V128 I64x2ReplaceLane(V128 c, byte lane, long v)
        {
            MV128 m = (V128)c;
            m[(long)lane] = v;
            return m;
        }

        public static V128 F32x4ReplaceLane(V128 c, byte lane, float v)
        {
            MV128 m = (V128)c;
            m[(float)lane] = v;
            return m;
        }

        public static V128 F64x2ReplaceLane(V128 c, byte lane, double v)
        {
            MV128 m = (V128)c;
            m[(double)lane] = v;
            return m;
        }

        // ================================================================
        // Shuffle and Swizzle
        // Mirror: Wacs.Core/Instructions/SIMD/ViShuffleOp.cs, ViBinOp.cs
        // ================================================================

        public static V128 I8x16Shuffle(V128 a, V128 b, V128 lanes)
        {
            MV128 result = new();
            for (byte i = 0; i < 16; ++i)
            {
                byte laneIndex = lanes[i];
                result[i] = laneIndex < 16 ? a[laneIndex] : b[(byte)(laneIndex - 16)];
            }
            return result;
        }

        public static V128 I8x16Swizzle(V128 a, V128 b)
        {
            MV128 result = new();
            for (byte i = 0; i < 16; ++i)
            {
                byte index = b[i];
                result[i] = index >= 16 ? (byte)0 : a[index];
            }
            return result;
        }

        // ================================================================
        // Integer relational ops
        // Mirror: Wacs.Core/Instructions/SIMD/ViRelOp.cs
        // ================================================================

        // --- i8x16 ---
        public static V128 I8x16Eq(V128 a, V128 b) => new V128(
            a.U8x16_0==b.U8x16_0?(sbyte)-1:(sbyte)0, a.U8x16_1==b.U8x16_1?(sbyte)-1:(sbyte)0,
            a.U8x16_2==b.U8x16_2?(sbyte)-1:(sbyte)0, a.U8x16_3==b.U8x16_3?(sbyte)-1:(sbyte)0,
            a.U8x16_4==b.U8x16_4?(sbyte)-1:(sbyte)0, a.U8x16_5==b.U8x16_5?(sbyte)-1:(sbyte)0,
            a.U8x16_6==b.U8x16_6?(sbyte)-1:(sbyte)0, a.U8x16_7==b.U8x16_7?(sbyte)-1:(sbyte)0,
            a.U8x16_8==b.U8x16_8?(sbyte)-1:(sbyte)0, a.U8x16_9==b.U8x16_9?(sbyte)-1:(sbyte)0,
            a.U8x16_A==b.U8x16_A?(sbyte)-1:(sbyte)0, a.U8x16_B==b.U8x16_B?(sbyte)-1:(sbyte)0,
            a.U8x16_C==b.U8x16_C?(sbyte)-1:(sbyte)0, a.U8x16_D==b.U8x16_D?(sbyte)-1:(sbyte)0,
            a.U8x16_E==b.U8x16_E?(sbyte)-1:(sbyte)0, a.U8x16_F==b.U8x16_F?(sbyte)-1:(sbyte)0);

        public static V128 I8x16Ne(V128 a, V128 b) => new V128(
            a.U8x16_0!=b.U8x16_0?(sbyte)-1:(sbyte)0, a.U8x16_1!=b.U8x16_1?(sbyte)-1:(sbyte)0,
            a.U8x16_2!=b.U8x16_2?(sbyte)-1:(sbyte)0, a.U8x16_3!=b.U8x16_3?(sbyte)-1:(sbyte)0,
            a.U8x16_4!=b.U8x16_4?(sbyte)-1:(sbyte)0, a.U8x16_5!=b.U8x16_5?(sbyte)-1:(sbyte)0,
            a.U8x16_6!=b.U8x16_6?(sbyte)-1:(sbyte)0, a.U8x16_7!=b.U8x16_7?(sbyte)-1:(sbyte)0,
            a.U8x16_8!=b.U8x16_8?(sbyte)-1:(sbyte)0, a.U8x16_9!=b.U8x16_9?(sbyte)-1:(sbyte)0,
            a.U8x16_A!=b.U8x16_A?(sbyte)-1:(sbyte)0, a.U8x16_B!=b.U8x16_B?(sbyte)-1:(sbyte)0,
            a.U8x16_C!=b.U8x16_C?(sbyte)-1:(sbyte)0, a.U8x16_D!=b.U8x16_D?(sbyte)-1:(sbyte)0,
            a.U8x16_E!=b.U8x16_E?(sbyte)-1:(sbyte)0, a.U8x16_F!=b.U8x16_F?(sbyte)-1:(sbyte)0);

        public static V128 I8x16LtS(V128 a, V128 b) => new V128(
            a.I8x16_0<b.I8x16_0?(sbyte)-1:(sbyte)0, a.I8x16_1<b.I8x16_1?(sbyte)-1:(sbyte)0,
            a.I8x16_2<b.I8x16_2?(sbyte)-1:(sbyte)0, a.I8x16_3<b.I8x16_3?(sbyte)-1:(sbyte)0,
            a.I8x16_4<b.I8x16_4?(sbyte)-1:(sbyte)0, a.I8x16_5<b.I8x16_5?(sbyte)-1:(sbyte)0,
            a.I8x16_6<b.I8x16_6?(sbyte)-1:(sbyte)0, a.I8x16_7<b.I8x16_7?(sbyte)-1:(sbyte)0,
            a.I8x16_8<b.I8x16_8?(sbyte)-1:(sbyte)0, a.I8x16_9<b.I8x16_9?(sbyte)-1:(sbyte)0,
            a.I8x16_A<b.I8x16_A?(sbyte)-1:(sbyte)0, a.I8x16_B<b.I8x16_B?(sbyte)-1:(sbyte)0,
            a.I8x16_C<b.I8x16_C?(sbyte)-1:(sbyte)0, a.I8x16_D<b.I8x16_D?(sbyte)-1:(sbyte)0,
            a.I8x16_E<b.I8x16_E?(sbyte)-1:(sbyte)0, a.I8x16_F<b.I8x16_F?(sbyte)-1:(sbyte)0);

        public static V128 I8x16LtU(V128 a, V128 b) => new V128(
            a.U8x16_0<b.U8x16_0?(sbyte)-1:(sbyte)0, a.U8x16_1<b.U8x16_1?(sbyte)-1:(sbyte)0,
            a.U8x16_2<b.U8x16_2?(sbyte)-1:(sbyte)0, a.U8x16_3<b.U8x16_3?(sbyte)-1:(sbyte)0,
            a.U8x16_4<b.U8x16_4?(sbyte)-1:(sbyte)0, a.U8x16_5<b.U8x16_5?(sbyte)-1:(sbyte)0,
            a.U8x16_6<b.U8x16_6?(sbyte)-1:(sbyte)0, a.U8x16_7<b.U8x16_7?(sbyte)-1:(sbyte)0,
            a.U8x16_8<b.U8x16_8?(sbyte)-1:(sbyte)0, a.U8x16_9<b.U8x16_9?(sbyte)-1:(sbyte)0,
            a.U8x16_A<b.U8x16_A?(sbyte)-1:(sbyte)0, a.U8x16_B<b.U8x16_B?(sbyte)-1:(sbyte)0,
            a.U8x16_C<b.U8x16_C?(sbyte)-1:(sbyte)0, a.U8x16_D<b.U8x16_D?(sbyte)-1:(sbyte)0,
            a.U8x16_E<b.U8x16_E?(sbyte)-1:(sbyte)0, a.U8x16_F<b.U8x16_F?(sbyte)-1:(sbyte)0);

        public static V128 I8x16GtS(V128 a, V128 b) => new V128(
            a.I8x16_0>b.I8x16_0?(sbyte)-1:(sbyte)0, a.I8x16_1>b.I8x16_1?(sbyte)-1:(sbyte)0,
            a.I8x16_2>b.I8x16_2?(sbyte)-1:(sbyte)0, a.I8x16_3>b.I8x16_3?(sbyte)-1:(sbyte)0,
            a.I8x16_4>b.I8x16_4?(sbyte)-1:(sbyte)0, a.I8x16_5>b.I8x16_5?(sbyte)-1:(sbyte)0,
            a.I8x16_6>b.I8x16_6?(sbyte)-1:(sbyte)0, a.I8x16_7>b.I8x16_7?(sbyte)-1:(sbyte)0,
            a.I8x16_8>b.I8x16_8?(sbyte)-1:(sbyte)0, a.I8x16_9>b.I8x16_9?(sbyte)-1:(sbyte)0,
            a.I8x16_A>b.I8x16_A?(sbyte)-1:(sbyte)0, a.I8x16_B>b.I8x16_B?(sbyte)-1:(sbyte)0,
            a.I8x16_C>b.I8x16_C?(sbyte)-1:(sbyte)0, a.I8x16_D>b.I8x16_D?(sbyte)-1:(sbyte)0,
            a.I8x16_E>b.I8x16_E?(sbyte)-1:(sbyte)0, a.I8x16_F>b.I8x16_F?(sbyte)-1:(sbyte)0);

        public static V128 I8x16GtU(V128 a, V128 b) => new V128(
            a.U8x16_0>b.U8x16_0?(sbyte)-1:(sbyte)0, a.U8x16_1>b.U8x16_1?(sbyte)-1:(sbyte)0,
            a.U8x16_2>b.U8x16_2?(sbyte)-1:(sbyte)0, a.U8x16_3>b.U8x16_3?(sbyte)-1:(sbyte)0,
            a.U8x16_4>b.U8x16_4?(sbyte)-1:(sbyte)0, a.U8x16_5>b.U8x16_5?(sbyte)-1:(sbyte)0,
            a.U8x16_6>b.U8x16_6?(sbyte)-1:(sbyte)0, a.U8x16_7>b.U8x16_7?(sbyte)-1:(sbyte)0,
            a.U8x16_8>b.U8x16_8?(sbyte)-1:(sbyte)0, a.U8x16_9>b.U8x16_9?(sbyte)-1:(sbyte)0,
            a.U8x16_A>b.U8x16_A?(sbyte)-1:(sbyte)0, a.U8x16_B>b.U8x16_B?(sbyte)-1:(sbyte)0,
            a.U8x16_C>b.U8x16_C?(sbyte)-1:(sbyte)0, a.U8x16_D>b.U8x16_D?(sbyte)-1:(sbyte)0,
            a.U8x16_E>b.U8x16_E?(sbyte)-1:(sbyte)0, a.U8x16_F>b.U8x16_F?(sbyte)-1:(sbyte)0);

        public static V128 I8x16LeS(V128 a, V128 b) => new V128(
            a.I8x16_0<=b.I8x16_0?(sbyte)-1:(sbyte)0, a.I8x16_1<=b.I8x16_1?(sbyte)-1:(sbyte)0,
            a.I8x16_2<=b.I8x16_2?(sbyte)-1:(sbyte)0, a.I8x16_3<=b.I8x16_3?(sbyte)-1:(sbyte)0,
            a.I8x16_4<=b.I8x16_4?(sbyte)-1:(sbyte)0, a.I8x16_5<=b.I8x16_5?(sbyte)-1:(sbyte)0,
            a.I8x16_6<=b.I8x16_6?(sbyte)-1:(sbyte)0, a.I8x16_7<=b.I8x16_7?(sbyte)-1:(sbyte)0,
            a.I8x16_8<=b.I8x16_8?(sbyte)-1:(sbyte)0, a.I8x16_9<=b.I8x16_9?(sbyte)-1:(sbyte)0,
            a.I8x16_A<=b.I8x16_A?(sbyte)-1:(sbyte)0, a.I8x16_B<=b.I8x16_B?(sbyte)-1:(sbyte)0,
            a.I8x16_C<=b.I8x16_C?(sbyte)-1:(sbyte)0, a.I8x16_D<=b.I8x16_D?(sbyte)-1:(sbyte)0,
            a.I8x16_E<=b.I8x16_E?(sbyte)-1:(sbyte)0, a.I8x16_F<=b.I8x16_F?(sbyte)-1:(sbyte)0);

        public static V128 I8x16LeU(V128 a, V128 b) => new V128(
            a.U8x16_0<=b.U8x16_0?(sbyte)-1:(sbyte)0, a.U8x16_1<=b.U8x16_1?(sbyte)-1:(sbyte)0,
            a.U8x16_2<=b.U8x16_2?(sbyte)-1:(sbyte)0, a.U8x16_3<=b.U8x16_3?(sbyte)-1:(sbyte)0,
            a.U8x16_4<=b.U8x16_4?(sbyte)-1:(sbyte)0, a.U8x16_5<=b.U8x16_5?(sbyte)-1:(sbyte)0,
            a.U8x16_6<=b.U8x16_6?(sbyte)-1:(sbyte)0, a.U8x16_7<=b.U8x16_7?(sbyte)-1:(sbyte)0,
            a.U8x16_8<=b.U8x16_8?(sbyte)-1:(sbyte)0, a.U8x16_9<=b.U8x16_9?(sbyte)-1:(sbyte)0,
            a.U8x16_A<=b.U8x16_A?(sbyte)-1:(sbyte)0, a.U8x16_B<=b.U8x16_B?(sbyte)-1:(sbyte)0,
            a.U8x16_C<=b.U8x16_C?(sbyte)-1:(sbyte)0, a.U8x16_D<=b.U8x16_D?(sbyte)-1:(sbyte)0,
            a.U8x16_E<=b.U8x16_E?(sbyte)-1:(sbyte)0, a.U8x16_F<=b.U8x16_F?(sbyte)-1:(sbyte)0);

        public static V128 I8x16GeS(V128 a, V128 b) => new V128(
            a.I8x16_0>=b.I8x16_0?(sbyte)-1:(sbyte)0, a.I8x16_1>=b.I8x16_1?(sbyte)-1:(sbyte)0,
            a.I8x16_2>=b.I8x16_2?(sbyte)-1:(sbyte)0, a.I8x16_3>=b.I8x16_3?(sbyte)-1:(sbyte)0,
            a.I8x16_4>=b.I8x16_4?(sbyte)-1:(sbyte)0, a.I8x16_5>=b.I8x16_5?(sbyte)-1:(sbyte)0,
            a.I8x16_6>=b.I8x16_6?(sbyte)-1:(sbyte)0, a.I8x16_7>=b.I8x16_7?(sbyte)-1:(sbyte)0,
            a.I8x16_8>=b.I8x16_8?(sbyte)-1:(sbyte)0, a.I8x16_9>=b.I8x16_9?(sbyte)-1:(sbyte)0,
            a.I8x16_A>=b.I8x16_A?(sbyte)-1:(sbyte)0, a.I8x16_B>=b.I8x16_B?(sbyte)-1:(sbyte)0,
            a.I8x16_C>=b.I8x16_C?(sbyte)-1:(sbyte)0, a.I8x16_D>=b.I8x16_D?(sbyte)-1:(sbyte)0,
            a.I8x16_E>=b.I8x16_E?(sbyte)-1:(sbyte)0, a.I8x16_F>=b.I8x16_F?(sbyte)-1:(sbyte)0);

        public static V128 I8x16GeU(V128 a, V128 b) => new V128(
            a.U8x16_0>=b.U8x16_0?(sbyte)-1:(sbyte)0, a.U8x16_1>=b.U8x16_1?(sbyte)-1:(sbyte)0,
            a.U8x16_2>=b.U8x16_2?(sbyte)-1:(sbyte)0, a.U8x16_3>=b.U8x16_3?(sbyte)-1:(sbyte)0,
            a.U8x16_4>=b.U8x16_4?(sbyte)-1:(sbyte)0, a.U8x16_5>=b.U8x16_5?(sbyte)-1:(sbyte)0,
            a.U8x16_6>=b.U8x16_6?(sbyte)-1:(sbyte)0, a.U8x16_7>=b.U8x16_7?(sbyte)-1:(sbyte)0,
            a.U8x16_8>=b.U8x16_8?(sbyte)-1:(sbyte)0, a.U8x16_9>=b.U8x16_9?(sbyte)-1:(sbyte)0,
            a.U8x16_A>=b.U8x16_A?(sbyte)-1:(sbyte)0, a.U8x16_B>=b.U8x16_B?(sbyte)-1:(sbyte)0,
            a.U8x16_C>=b.U8x16_C?(sbyte)-1:(sbyte)0, a.U8x16_D>=b.U8x16_D?(sbyte)-1:(sbyte)0,
            a.U8x16_E>=b.U8x16_E?(sbyte)-1:(sbyte)0, a.U8x16_F>=b.U8x16_F?(sbyte)-1:(sbyte)0);

        // --- i16x8 ---
        public static V128 I16x8Eq(V128 a, V128 b) => new V128(
            a.I16x8_0==b.I16x8_0?(short)-1:(short)0, a.I16x8_1==b.I16x8_1?(short)-1:(short)0,
            a.I16x8_2==b.I16x8_2?(short)-1:(short)0, a.I16x8_3==b.I16x8_3?(short)-1:(short)0,
            a.I16x8_4==b.I16x8_4?(short)-1:(short)0, a.I16x8_5==b.I16x8_5?(short)-1:(short)0,
            a.I16x8_6==b.I16x8_6?(short)-1:(short)0, a.I16x8_7==b.I16x8_7?(short)-1:(short)0);

        public static V128 I16x8Ne(V128 a, V128 b) => new V128(
            a.I16x8_0!=b.I16x8_0?(short)-1:(short)0, a.I16x8_1!=b.I16x8_1?(short)-1:(short)0,
            a.I16x8_2!=b.I16x8_2?(short)-1:(short)0, a.I16x8_3!=b.I16x8_3?(short)-1:(short)0,
            a.I16x8_4!=b.I16x8_4?(short)-1:(short)0, a.I16x8_5!=b.I16x8_5?(short)-1:(short)0,
            a.I16x8_6!=b.I16x8_6?(short)-1:(short)0, a.I16x8_7!=b.I16x8_7?(short)-1:(short)0);

        public static V128 I16x8LtS(V128 a, V128 b) => new V128(
            a.I16x8_0<b.I16x8_0?(short)-1:(short)0, a.I16x8_1<b.I16x8_1?(short)-1:(short)0,
            a.I16x8_2<b.I16x8_2?(short)-1:(short)0, a.I16x8_3<b.I16x8_3?(short)-1:(short)0,
            a.I16x8_4<b.I16x8_4?(short)-1:(short)0, a.I16x8_5<b.I16x8_5?(short)-1:(short)0,
            a.I16x8_6<b.I16x8_6?(short)-1:(short)0, a.I16x8_7<b.I16x8_7?(short)-1:(short)0);

        public static V128 I16x8LtU(V128 a, V128 b) => new V128(
            a.U16x8_0<b.U16x8_0?(short)-1:(short)0, a.U16x8_1<b.U16x8_1?(short)-1:(short)0,
            a.U16x8_2<b.U16x8_2?(short)-1:(short)0, a.U16x8_3<b.U16x8_3?(short)-1:(short)0,
            a.U16x8_4<b.U16x8_4?(short)-1:(short)0, a.U16x8_5<b.U16x8_5?(short)-1:(short)0,
            a.U16x8_6<b.U16x8_6?(short)-1:(short)0, a.U16x8_7<b.U16x8_7?(short)-1:(short)0);

        public static V128 I16x8GtS(V128 a, V128 b) => new V128(
            a.I16x8_0>b.I16x8_0?(short)-1:(short)0, a.I16x8_1>b.I16x8_1?(short)-1:(short)0,
            a.I16x8_2>b.I16x8_2?(short)-1:(short)0, a.I16x8_3>b.I16x8_3?(short)-1:(short)0,
            a.I16x8_4>b.I16x8_4?(short)-1:(short)0, a.I16x8_5>b.I16x8_5?(short)-1:(short)0,
            a.I16x8_6>b.I16x8_6?(short)-1:(short)0, a.I16x8_7>b.I16x8_7?(short)-1:(short)0);

        public static V128 I16x8GtU(V128 a, V128 b) => new V128(
            a.U16x8_0>b.U16x8_0?(short)-1:(short)0, a.U16x8_1>b.U16x8_1?(short)-1:(short)0,
            a.U16x8_2>b.U16x8_2?(short)-1:(short)0, a.U16x8_3>b.U16x8_3?(short)-1:(short)0,
            a.U16x8_4>b.U16x8_4?(short)-1:(short)0, a.U16x8_5>b.U16x8_5?(short)-1:(short)0,
            a.U16x8_6>b.U16x8_6?(short)-1:(short)0, a.U16x8_7>b.U16x8_7?(short)-1:(short)0);

        public static V128 I16x8LeS(V128 a, V128 b) => new V128(
            a.I16x8_0<=b.I16x8_0?(short)-1:(short)0, a.I16x8_1<=b.I16x8_1?(short)-1:(short)0,
            a.I16x8_2<=b.I16x8_2?(short)-1:(short)0, a.I16x8_3<=b.I16x8_3?(short)-1:(short)0,
            a.I16x8_4<=b.I16x8_4?(short)-1:(short)0, a.I16x8_5<=b.I16x8_5?(short)-1:(short)0,
            a.I16x8_6<=b.I16x8_6?(short)-1:(short)0, a.I16x8_7<=b.I16x8_7?(short)-1:(short)0);

        public static V128 I16x8LeU(V128 a, V128 b) => new V128(
            a.U16x8_0<=b.U16x8_0?(short)-1:(short)0, a.U16x8_1<=b.U16x8_1?(short)-1:(short)0,
            a.U16x8_2<=b.U16x8_2?(short)-1:(short)0, a.U16x8_3<=b.U16x8_3?(short)-1:(short)0,
            a.U16x8_4<=b.U16x8_4?(short)-1:(short)0, a.U16x8_5<=b.U16x8_5?(short)-1:(short)0,
            a.U16x8_6<=b.U16x8_6?(short)-1:(short)0, a.U16x8_7<=b.U16x8_7?(short)-1:(short)0);

        public static V128 I16x8GeS(V128 a, V128 b) => new V128(
            a.I16x8_0>=b.I16x8_0?(short)-1:(short)0, a.I16x8_1>=b.I16x8_1?(short)-1:(short)0,
            a.I16x8_2>=b.I16x8_2?(short)-1:(short)0, a.I16x8_3>=b.I16x8_3?(short)-1:(short)0,
            a.I16x8_4>=b.I16x8_4?(short)-1:(short)0, a.I16x8_5>=b.I16x8_5?(short)-1:(short)0,
            a.I16x8_6>=b.I16x8_6?(short)-1:(short)0, a.I16x8_7>=b.I16x8_7?(short)-1:(short)0);

        public static V128 I16x8GeU(V128 a, V128 b) => new V128(
            a.U16x8_0>=b.U16x8_0?(short)-1:(short)0, a.U16x8_1>=b.U16x8_1?(short)-1:(short)0,
            a.U16x8_2>=b.U16x8_2?(short)-1:(short)0, a.U16x8_3>=b.U16x8_3?(short)-1:(short)0,
            a.U16x8_4>=b.U16x8_4?(short)-1:(short)0, a.U16x8_5>=b.U16x8_5?(short)-1:(short)0,
            a.U16x8_6>=b.U16x8_6?(short)-1:(short)0, a.U16x8_7>=b.U16x8_7?(short)-1:(short)0);

        // --- i32x4 ---
        public static V128 I32x4Eq(V128 a, V128 b) => new V128(
            a.I32x4_0==b.I32x4_0?-1:0, a.I32x4_1==b.I32x4_1?-1:0,
            a.I32x4_2==b.I32x4_2?-1:0, a.I32x4_3==b.I32x4_3?-1:0);

        public static V128 I32x4Ne(V128 a, V128 b) => new V128(
            a.I32x4_0!=b.I32x4_0?-1:0, a.I32x4_1!=b.I32x4_1?-1:0,
            a.I32x4_2!=b.I32x4_2?-1:0, a.I32x4_3!=b.I32x4_3?-1:0);

        public static V128 I32x4LtS(V128 a, V128 b) => new V128(
            a.I32x4_0<b.I32x4_0?-1:0, a.I32x4_1<b.I32x4_1?-1:0,
            a.I32x4_2<b.I32x4_2?-1:0, a.I32x4_3<b.I32x4_3?-1:0);

        public static V128 I32x4LtU(V128 a, V128 b) => new V128(
            a.U32x4_0<b.U32x4_0?-1:0, a.U32x4_1<b.U32x4_1?-1:0,
            a.U32x4_2<b.U32x4_2?-1:0, a.U32x4_3<b.U32x4_3?-1:0);

        public static V128 I32x4GtS(V128 a, V128 b) => new V128(
            a.I32x4_0>b.I32x4_0?-1:0, a.I32x4_1>b.I32x4_1?-1:0,
            a.I32x4_2>b.I32x4_2?-1:0, a.I32x4_3>b.I32x4_3?-1:0);

        public static V128 I32x4GtU(V128 a, V128 b) => new V128(
            a.U32x4_0>b.U32x4_0?-1:0, a.U32x4_1>b.U32x4_1?-1:0,
            a.U32x4_2>b.U32x4_2?-1:0, a.U32x4_3>b.U32x4_3?-1:0);

        public static V128 I32x4LeS(V128 a, V128 b) => new V128(
            a.I32x4_0<=b.I32x4_0?-1:0, a.I32x4_1<=b.I32x4_1?-1:0,
            a.I32x4_2<=b.I32x4_2?-1:0, a.I32x4_3<=b.I32x4_3?-1:0);

        public static V128 I32x4LeU(V128 a, V128 b) => new V128(
            a.U32x4_0<=b.U32x4_0?-1:0, a.U32x4_1<=b.U32x4_1?-1:0,
            a.U32x4_2<=b.U32x4_2?-1:0, a.U32x4_3<=b.U32x4_3?-1:0);

        public static V128 I32x4GeS(V128 a, V128 b) => new V128(
            a.I32x4_0>=b.I32x4_0?-1:0, a.I32x4_1>=b.I32x4_1?-1:0,
            a.I32x4_2>=b.I32x4_2?-1:0, a.I32x4_3>=b.I32x4_3?-1:0);

        public static V128 I32x4GeU(V128 a, V128 b) => new V128(
            a.U32x4_0>=b.U32x4_0?-1:0, a.U32x4_1>=b.U32x4_1?-1:0,
            a.U32x4_2>=b.U32x4_2?-1:0, a.U32x4_3>=b.U32x4_3?-1:0);

        // --- i64x2 ---
        public static V128 I64x2Eq(V128 a, V128 b) => new V128(
            a.I64x2_0==b.I64x2_0?(long)-1:(long)0, a.I64x2_1==b.I64x2_1?(long)-1:(long)0);

        public static V128 I64x2Ne(V128 a, V128 b) => new V128(
            a.I64x2_0!=b.I64x2_0?(long)-1:(long)0, a.I64x2_1!=b.I64x2_1?(long)-1:(long)0);

        public static V128 I64x2LtS(V128 a, V128 b) => new V128(
            a.I64x2_0<b.I64x2_0?(long)-1:(long)0, a.I64x2_1<b.I64x2_1?(long)-1:(long)0);

        public static V128 I64x2GtS(V128 a, V128 b) => new V128(
            a.I64x2_0>b.I64x2_0?(long)-1:(long)0, a.I64x2_1>b.I64x2_1?(long)-1:(long)0);

        public static V128 I64x2LeS(V128 a, V128 b) => new V128(
            a.I64x2_0<=b.I64x2_0?(long)-1:(long)0, a.I64x2_1<=b.I64x2_1?(long)-1:(long)0);

        public static V128 I64x2GeS(V128 a, V128 b) => new V128(
            a.I64x2_0>=b.I64x2_0?(long)-1:(long)0, a.I64x2_1>=b.I64x2_1?(long)-1:(long)0);

        // ================================================================
        // Saturating integer arithmetic
        // Mirror: Wacs.Core/Instructions/SIMD/ViSatBinOp.cs
        // ================================================================

        public static V128 I8x16AddSatS(V128 a, V128 b) => new V128(
            (sbyte)Math.Clamp((int)a.I8x16_0+(int)b.I8x16_0, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_1+(int)b.I8x16_1, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_2+(int)b.I8x16_2, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_3+(int)b.I8x16_3, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_4+(int)b.I8x16_4, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_5+(int)b.I8x16_5, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_6+(int)b.I8x16_6, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_7+(int)b.I8x16_7, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_8+(int)b.I8x16_8, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_9+(int)b.I8x16_9, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_A+(int)b.I8x16_A, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_B+(int)b.I8x16_B, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_C+(int)b.I8x16_C, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_D+(int)b.I8x16_D, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_E+(int)b.I8x16_E, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_F+(int)b.I8x16_F, sbyte.MinValue, sbyte.MaxValue));

        public static V128 I8x16AddSatU(V128 a, V128 b) => new V128(
            (byte)Math.Clamp((int)a.U8x16_0+(int)b.U8x16_0, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_1+(int)b.U8x16_1, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_2+(int)b.U8x16_2, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_3+(int)b.U8x16_3, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_4+(int)b.U8x16_4, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_5+(int)b.U8x16_5, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_6+(int)b.U8x16_6, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_7+(int)b.U8x16_7, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_8+(int)b.U8x16_8, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_9+(int)b.U8x16_9, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_A+(int)b.U8x16_A, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_B+(int)b.U8x16_B, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_C+(int)b.U8x16_C, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_D+(int)b.U8x16_D, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_E+(int)b.U8x16_E, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_F+(int)b.U8x16_F, byte.MinValue, byte.MaxValue));

        public static V128 I8x16SubSatS(V128 a, V128 b) => new V128(
            (sbyte)Math.Clamp((int)a.I8x16_0-(int)b.I8x16_0, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_1-(int)b.I8x16_1, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_2-(int)b.I8x16_2, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_3-(int)b.I8x16_3, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_4-(int)b.I8x16_4, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_5-(int)b.I8x16_5, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_6-(int)b.I8x16_6, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_7-(int)b.I8x16_7, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_8-(int)b.I8x16_8, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_9-(int)b.I8x16_9, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_A-(int)b.I8x16_A, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_B-(int)b.I8x16_B, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_C-(int)b.I8x16_C, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_D-(int)b.I8x16_D, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_E-(int)b.I8x16_E, sbyte.MinValue, sbyte.MaxValue),
            (sbyte)Math.Clamp((int)a.I8x16_F-(int)b.I8x16_F, sbyte.MinValue, sbyte.MaxValue));

        public static V128 I8x16SubSatU(V128 a, V128 b) => new V128(
            (byte)Math.Clamp((int)a.U8x16_0-(int)b.U8x16_0, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_1-(int)b.U8x16_1, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_2-(int)b.U8x16_2, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_3-(int)b.U8x16_3, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_4-(int)b.U8x16_4, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_5-(int)b.U8x16_5, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_6-(int)b.U8x16_6, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_7-(int)b.U8x16_7, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_8-(int)b.U8x16_8, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_9-(int)b.U8x16_9, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_A-(int)b.U8x16_A, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_B-(int)b.U8x16_B, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_C-(int)b.U8x16_C, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_D-(int)b.U8x16_D, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_E-(int)b.U8x16_E, byte.MinValue, byte.MaxValue),
            (byte)Math.Clamp((int)a.U8x16_F-(int)b.U8x16_F, byte.MinValue, byte.MaxValue));

        public static V128 I16x8AddSatS(V128 a, V128 b) => new V128(
            (short)Math.Clamp((int)a.I16x8_0+(int)b.I16x8_0, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_1+(int)b.I16x8_1, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_2+(int)b.I16x8_2, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_3+(int)b.I16x8_3, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_4+(int)b.I16x8_4, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_5+(int)b.I16x8_5, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_6+(int)b.I16x8_6, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_7+(int)b.I16x8_7, short.MinValue, short.MaxValue));

        public static V128 I16x8AddSatU(V128 a, V128 b) => new V128(
            (ushort)Math.Clamp((int)a.U16x8_0+(int)b.U16x8_0, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_1+(int)b.U16x8_1, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_2+(int)b.U16x8_2, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_3+(int)b.U16x8_3, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_4+(int)b.U16x8_4, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_5+(int)b.U16x8_5, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_6+(int)b.U16x8_6, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_7+(int)b.U16x8_7, ushort.MinValue, ushort.MaxValue));

        public static V128 I16x8SubSatS(V128 a, V128 b) => new V128(
            (short)Math.Clamp((int)a.I16x8_0-(int)b.I16x8_0, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_1-(int)b.I16x8_1, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_2-(int)b.I16x8_2, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_3-(int)b.I16x8_3, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_4-(int)b.I16x8_4, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_5-(int)b.I16x8_5, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_6-(int)b.I16x8_6, short.MinValue, short.MaxValue),
            (short)Math.Clamp((int)a.I16x8_7-(int)b.I16x8_7, short.MinValue, short.MaxValue));

        public static V128 I16x8SubSatU(V128 a, V128 b) => new V128(
            (ushort)Math.Clamp((int)a.U16x8_0-(int)b.U16x8_0, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_1-(int)b.U16x8_1, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_2-(int)b.U16x8_2, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_3-(int)b.U16x8_3, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_4-(int)b.U16x8_4, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_5-(int)b.U16x8_5, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_6-(int)b.U16x8_6, ushort.MinValue, ushort.MaxValue),
            (ushort)Math.Clamp((int)a.U16x8_7-(int)b.U16x8_7, ushort.MinValue, ushort.MaxValue));

        // ================================================================
        // Integer min/max
        // Mirror: Wacs.Core/Instructions/SIMD/ViMinMaxOp.cs
        // ================================================================

        public static V128 I8x16MinS(V128 a, V128 b) => new V128(Math.Min(a.I8x16_0,b.I8x16_0),Math.Min(a.I8x16_1,b.I8x16_1),Math.Min(a.I8x16_2,b.I8x16_2),Math.Min(a.I8x16_3,b.I8x16_3),Math.Min(a.I8x16_4,b.I8x16_4),Math.Min(a.I8x16_5,b.I8x16_5),Math.Min(a.I8x16_6,b.I8x16_6),Math.Min(a.I8x16_7,b.I8x16_7),Math.Min(a.I8x16_8,b.I8x16_8),Math.Min(a.I8x16_9,b.I8x16_9),Math.Min(a.I8x16_A,b.I8x16_A),Math.Min(a.I8x16_B,b.I8x16_B),Math.Min(a.I8x16_C,b.I8x16_C),Math.Min(a.I8x16_D,b.I8x16_D),Math.Min(a.I8x16_E,b.I8x16_E),Math.Min(a.I8x16_F,b.I8x16_F));
        public static V128 I8x16MinU(V128 a, V128 b) => new V128(Math.Min(a.U8x16_0,b.U8x16_0),Math.Min(a.U8x16_1,b.U8x16_1),Math.Min(a.U8x16_2,b.U8x16_2),Math.Min(a.U8x16_3,b.U8x16_3),Math.Min(a.U8x16_4,b.U8x16_4),Math.Min(a.U8x16_5,b.U8x16_5),Math.Min(a.U8x16_6,b.U8x16_6),Math.Min(a.U8x16_7,b.U8x16_7),Math.Min(a.U8x16_8,b.U8x16_8),Math.Min(a.U8x16_9,b.U8x16_9),Math.Min(a.U8x16_A,b.U8x16_A),Math.Min(a.U8x16_B,b.U8x16_B),Math.Min(a.U8x16_C,b.U8x16_C),Math.Min(a.U8x16_D,b.U8x16_D),Math.Min(a.U8x16_E,b.U8x16_E),Math.Min(a.U8x16_F,b.U8x16_F));
        public static V128 I8x16MaxS(V128 a, V128 b) => new V128(Math.Max(a.I8x16_0,b.I8x16_0),Math.Max(a.I8x16_1,b.I8x16_1),Math.Max(a.I8x16_2,b.I8x16_2),Math.Max(a.I8x16_3,b.I8x16_3),Math.Max(a.I8x16_4,b.I8x16_4),Math.Max(a.I8x16_5,b.I8x16_5),Math.Max(a.I8x16_6,b.I8x16_6),Math.Max(a.I8x16_7,b.I8x16_7),Math.Max(a.I8x16_8,b.I8x16_8),Math.Max(a.I8x16_9,b.I8x16_9),Math.Max(a.I8x16_A,b.I8x16_A),Math.Max(a.I8x16_B,b.I8x16_B),Math.Max(a.I8x16_C,b.I8x16_C),Math.Max(a.I8x16_D,b.I8x16_D),Math.Max(a.I8x16_E,b.I8x16_E),Math.Max(a.I8x16_F,b.I8x16_F));
        public static V128 I8x16MaxU(V128 a, V128 b) => new V128(Math.Max(a.U8x16_0,b.U8x16_0),Math.Max(a.U8x16_1,b.U8x16_1),Math.Max(a.U8x16_2,b.U8x16_2),Math.Max(a.U8x16_3,b.U8x16_3),Math.Max(a.U8x16_4,b.U8x16_4),Math.Max(a.U8x16_5,b.U8x16_5),Math.Max(a.U8x16_6,b.U8x16_6),Math.Max(a.U8x16_7,b.U8x16_7),Math.Max(a.U8x16_8,b.U8x16_8),Math.Max(a.U8x16_9,b.U8x16_9),Math.Max(a.U8x16_A,b.U8x16_A),Math.Max(a.U8x16_B,b.U8x16_B),Math.Max(a.U8x16_C,b.U8x16_C),Math.Max(a.U8x16_D,b.U8x16_D),Math.Max(a.U8x16_E,b.U8x16_E),Math.Max(a.U8x16_F,b.U8x16_F));

        public static V128 I16x8MinS(V128 a, V128 b) => new V128(Math.Min(a.I16x8_0,b.I16x8_0),Math.Min(a.I16x8_1,b.I16x8_1),Math.Min(a.I16x8_2,b.I16x8_2),Math.Min(a.I16x8_3,b.I16x8_3),Math.Min(a.I16x8_4,b.I16x8_4),Math.Min(a.I16x8_5,b.I16x8_5),Math.Min(a.I16x8_6,b.I16x8_6),Math.Min(a.I16x8_7,b.I16x8_7));
        public static V128 I16x8MinU(V128 a, V128 b) => new V128(Math.Min(a.U16x8_0,b.U16x8_0),Math.Min(a.U16x8_1,b.U16x8_1),Math.Min(a.U16x8_2,b.U16x8_2),Math.Min(a.U16x8_3,b.U16x8_3),Math.Min(a.U16x8_4,b.U16x8_4),Math.Min(a.U16x8_5,b.U16x8_5),Math.Min(a.U16x8_6,b.U16x8_6),Math.Min(a.U16x8_7,b.U16x8_7));
        public static V128 I16x8MaxS(V128 a, V128 b) => new V128(Math.Max(a.I16x8_0,b.I16x8_0),Math.Max(a.I16x8_1,b.I16x8_1),Math.Max(a.I16x8_2,b.I16x8_2),Math.Max(a.I16x8_3,b.I16x8_3),Math.Max(a.I16x8_4,b.I16x8_4),Math.Max(a.I16x8_5,b.I16x8_5),Math.Max(a.I16x8_6,b.I16x8_6),Math.Max(a.I16x8_7,b.I16x8_7));
        public static V128 I16x8MaxU(V128 a, V128 b) => new V128(Math.Max(a.U16x8_0,b.U16x8_0),Math.Max(a.U16x8_1,b.U16x8_1),Math.Max(a.U16x8_2,b.U16x8_2),Math.Max(a.U16x8_3,b.U16x8_3),Math.Max(a.U16x8_4,b.U16x8_4),Math.Max(a.U16x8_5,b.U16x8_5),Math.Max(a.U16x8_6,b.U16x8_6),Math.Max(a.U16x8_7,b.U16x8_7));

        public static V128 I32x4MinS(V128 a, V128 b) => new V128(Math.Min(a.I32x4_0,b.I32x4_0),Math.Min(a.I32x4_1,b.I32x4_1),Math.Min(a.I32x4_2,b.I32x4_2),Math.Min(a.I32x4_3,b.I32x4_3));
        public static V128 I32x4MinU(V128 a, V128 b) => new V128(Math.Min(a.U32x4_0,b.U32x4_0),Math.Min(a.U32x4_1,b.U32x4_1),Math.Min(a.U32x4_2,b.U32x4_2),Math.Min(a.U32x4_3,b.U32x4_3));
        public static V128 I32x4MaxS(V128 a, V128 b) => new V128(Math.Max(a.I32x4_0,b.I32x4_0),Math.Max(a.I32x4_1,b.I32x4_1),Math.Max(a.I32x4_2,b.I32x4_2),Math.Max(a.I32x4_3,b.I32x4_3));
        public static V128 I32x4MaxU(V128 a, V128 b) => new V128(Math.Max(a.U32x4_0,b.U32x4_0),Math.Max(a.U32x4_1,b.U32x4_1),Math.Max(a.U32x4_2,b.U32x4_2),Math.Max(a.U32x4_3,b.U32x4_3));

        // ================================================================
        // Average (round-up)
        // Mirror: Wacs.Core/Instructions/SIMD/ViBinOp.cs
        // ================================================================

        public static V128 I8x16AvgrU(V128 a, V128 b) => new V128(
            (byte)((a.U8x16_0+b.U8x16_0+1)>>1), (byte)((a.U8x16_1+b.U8x16_1+1)>>1),
            (byte)((a.U8x16_2+b.U8x16_2+1)>>1), (byte)((a.U8x16_3+b.U8x16_3+1)>>1),
            (byte)((a.U8x16_4+b.U8x16_4+1)>>1), (byte)((a.U8x16_5+b.U8x16_5+1)>>1),
            (byte)((a.U8x16_6+b.U8x16_6+1)>>1), (byte)((a.U8x16_7+b.U8x16_7+1)>>1),
            (byte)((a.U8x16_8+b.U8x16_8+1)>>1), (byte)((a.U8x16_9+b.U8x16_9+1)>>1),
            (byte)((a.U8x16_A+b.U8x16_A+1)>>1), (byte)((a.U8x16_B+b.U8x16_B+1)>>1),
            (byte)((a.U8x16_C+b.U8x16_C+1)>>1), (byte)((a.U8x16_D+b.U8x16_D+1)>>1),
            (byte)((a.U8x16_E+b.U8x16_E+1)>>1), (byte)((a.U8x16_F+b.U8x16_F+1)>>1));

        public static V128 I16x8AvgrU(V128 a, V128 b) => new V128(
            (ushort)((a.U16x8_0+b.U16x8_0+1)>>1), (ushort)((a.U16x8_1+b.U16x8_1+1)>>1),
            (ushort)((a.U16x8_2+b.U16x8_2+1)>>1), (ushort)((a.U16x8_3+b.U16x8_3+1)>>1),
            (ushort)((a.U16x8_4+b.U16x8_4+1)>>1), (ushort)((a.U16x8_5+b.U16x8_5+1)>>1),
            (ushort)((a.U16x8_6+b.U16x8_6+1)>>1), (ushort)((a.U16x8_7+b.U16x8_7+1)>>1));

        // ================================================================
        // Shift ops
        // Mirror: Wacs.Core/Instructions/SIMD/ViShiftOp.cs
        // ================================================================

        public static V128 I8x16Shl(V128 v, int s) { s%=8; return new V128((byte)(v.U8x16_0<<s),(byte)(v.U8x16_1<<s),(byte)(v.U8x16_2<<s),(byte)(v.U8x16_3<<s),(byte)(v.U8x16_4<<s),(byte)(v.U8x16_5<<s),(byte)(v.U8x16_6<<s),(byte)(v.U8x16_7<<s),(byte)(v.U8x16_8<<s),(byte)(v.U8x16_9<<s),(byte)(v.U8x16_A<<s),(byte)(v.U8x16_B<<s),(byte)(v.U8x16_C<<s),(byte)(v.U8x16_D<<s),(byte)(v.U8x16_E<<s),(byte)(v.U8x16_F<<s)); }
        public static V128 I8x16ShrS(V128 v, int s) { s%=8; return new V128((sbyte)(v.I8x16_0>>s),(sbyte)(v.I8x16_1>>s),(sbyte)(v.I8x16_2>>s),(sbyte)(v.I8x16_3>>s),(sbyte)(v.I8x16_4>>s),(sbyte)(v.I8x16_5>>s),(sbyte)(v.I8x16_6>>s),(sbyte)(v.I8x16_7>>s),(sbyte)(v.I8x16_8>>s),(sbyte)(v.I8x16_9>>s),(sbyte)(v.I8x16_A>>s),(sbyte)(v.I8x16_B>>s),(sbyte)(v.I8x16_C>>s),(sbyte)(v.I8x16_D>>s),(sbyte)(v.I8x16_E>>s),(sbyte)(v.I8x16_F>>s)); }
        public static V128 I8x16ShrU(V128 v, int s) { s%=8; return new V128((byte)(v.U8x16_0>>s),(byte)(v.U8x16_1>>s),(byte)(v.U8x16_2>>s),(byte)(v.U8x16_3>>s),(byte)(v.U8x16_4>>s),(byte)(v.U8x16_5>>s),(byte)(v.U8x16_6>>s),(byte)(v.U8x16_7>>s),(byte)(v.U8x16_8>>s),(byte)(v.U8x16_9>>s),(byte)(v.U8x16_A>>s),(byte)(v.U8x16_B>>s),(byte)(v.U8x16_C>>s),(byte)(v.U8x16_D>>s),(byte)(v.U8x16_E>>s),(byte)(v.U8x16_F>>s)); }

        public static V128 I16x8Shl(V128 v, int s) { s%=16; return new V128((ushort)(v.U16x8_0<<s),(ushort)(v.U16x8_1<<s),(ushort)(v.U16x8_2<<s),(ushort)(v.U16x8_3<<s),(ushort)(v.U16x8_4<<s),(ushort)(v.U16x8_5<<s),(ushort)(v.U16x8_6<<s),(ushort)(v.U16x8_7<<s)); }
        public static V128 I16x8ShrS(V128 v, int s) { s%=16; return new V128((short)(v.I16x8_0>>s),(short)(v.I16x8_1>>s),(short)(v.I16x8_2>>s),(short)(v.I16x8_3>>s),(short)(v.I16x8_4>>s),(short)(v.I16x8_5>>s),(short)(v.I16x8_6>>s),(short)(v.I16x8_7>>s)); }
        public static V128 I16x8ShrU(V128 v, int s) { s%=16; return new V128((ushort)(v.U16x8_0>>s),(ushort)(v.U16x8_1>>s),(ushort)(v.U16x8_2>>s),(ushort)(v.U16x8_3>>s),(ushort)(v.U16x8_4>>s),(ushort)(v.U16x8_5>>s),(ushort)(v.U16x8_6>>s),(ushort)(v.U16x8_7>>s)); }

        public static V128 I32x4Shl(V128 v, int s) => new V128((uint)(v.U32x4_0<<s),(uint)(v.U32x4_1<<s),(uint)(v.U32x4_2<<s),(uint)(v.U32x4_3<<s));
        public static V128 I32x4ShrS(V128 v, int s) => new V128(v.I32x4_0>>s, v.I32x4_1>>s, v.I32x4_2>>s, v.I32x4_3>>s);
        public static V128 I32x4ShrU(V128 v, int s) => new V128((uint)(v.U32x4_0>>s),(uint)(v.U32x4_1>>s),(uint)(v.U32x4_2>>s),(uint)(v.U32x4_3>>s));

        public static V128 I64x2Shl(V128 v, int s) => new V128((ulong)(v.U64x2_0<<s),(ulong)(v.U64x2_1<<s));
        public static V128 I64x2ShrS(V128 v, int s) => new V128(v.I64x2_0>>s, v.I64x2_1>>s);
        public static V128 I64x2ShrU(V128 v, int s) => new V128((ulong)(v.U64x2_0>>s),(ulong)(v.U64x2_1>>s));

        // ================================================================
        // Extended multiply, dot, q15mulr, extadd_pairwise
        // Mirror: Wacs.Core/Instructions/SIMD/ViBinOp.cs
        // ================================================================

        public static V128 I16x8ExtAddPairwiseI8x16S(V128 v) => new V128(
            (short)((short)v.I8x16_0+(short)v.I8x16_1), (short)((short)v.I8x16_2+(short)v.I8x16_3),
            (short)((short)v.I8x16_4+(short)v.I8x16_5), (short)((short)v.I8x16_6+(short)v.I8x16_7),
            (short)((short)v.I8x16_8+(short)v.I8x16_9), (short)((short)v.I8x16_A+(short)v.I8x16_B),
            (short)((short)v.I8x16_C+(short)v.I8x16_D), (short)((short)v.I8x16_E+(short)v.I8x16_F));

        public static V128 I16x8ExtAddPairwiseI8x16U(V128 v) => new V128(
            (ushort)((short)v.U8x16_0+(short)v.U8x16_1), (ushort)((short)v.U8x16_2+(short)v.U8x16_3),
            (ushort)((short)v.U8x16_4+(short)v.U8x16_5), (ushort)((short)v.U8x16_6+(short)v.U8x16_7),
            (ushort)((short)v.U8x16_8+(short)v.U8x16_9), (ushort)((short)v.U8x16_A+(short)v.U8x16_B),
            (ushort)((short)v.U8x16_C+(short)v.U8x16_D), (ushort)((short)v.U8x16_E+(short)v.U8x16_F));

        public static V128 I32x4ExtAddPairwiseI16x8S(V128 v) => new V128(
            (int)v.I16x8_0+(int)v.I16x8_1, (int)v.I16x8_2+(int)v.I16x8_3,
            (int)v.I16x8_4+(int)v.I16x8_5, (int)v.I16x8_6+(int)v.I16x8_7);

        public static V128 I32x4ExtAddPairwiseI16x8U(V128 v) => new V128(
            (uint)v.U16x8_0+(uint)v.U16x8_1, (uint)v.U16x8_2+(uint)v.U16x8_3,
            (uint)v.U16x8_4+(uint)v.U16x8_5, (uint)v.U16x8_6+(uint)v.U16x8_7);

        public static V128 I16x8ExtMulLowI8x16S(V128 a, V128 b) => new V128(
            (short)((short)a.I8x16_0*(short)b.I8x16_0), (short)((short)a.I8x16_1*(short)b.I8x16_1),
            (short)((short)a.I8x16_2*(short)b.I8x16_2), (short)((short)a.I8x16_3*(short)b.I8x16_3),
            (short)((short)a.I8x16_4*(short)b.I8x16_4), (short)((short)a.I8x16_5*(short)b.I8x16_5),
            (short)((short)a.I8x16_6*(short)b.I8x16_6), (short)((short)a.I8x16_7*(short)b.I8x16_7));

        public static V128 I16x8ExtMulHighI8x16S(V128 a, V128 b) => new V128(
            (short)((short)a.I8x16_8*(short)b.I8x16_8), (short)((short)a.I8x16_9*(short)b.I8x16_9),
            (short)((short)a.I8x16_A*(short)b.I8x16_A), (short)((short)a.I8x16_B*(short)b.I8x16_B),
            (short)((short)a.I8x16_C*(short)b.I8x16_C), (short)((short)a.I8x16_D*(short)b.I8x16_D),
            (short)((short)a.I8x16_E*(short)b.I8x16_E), (short)((short)a.I8x16_F*(short)b.I8x16_F));

        public static V128 I16x8ExtMulLowI8x16U(V128 a, V128 b) => new V128(
            (ushort)((ushort)a.U8x16_0*(ushort)b.U8x16_0), (ushort)((ushort)a.U8x16_1*(ushort)b.U8x16_1),
            (ushort)((ushort)a.U8x16_2*(ushort)b.U8x16_2), (ushort)((ushort)a.U8x16_3*(ushort)b.U8x16_3),
            (ushort)((ushort)a.U8x16_4*(ushort)b.U8x16_4), (ushort)((ushort)a.U8x16_5*(ushort)b.U8x16_5),
            (ushort)((ushort)a.U8x16_6*(ushort)b.U8x16_6), (ushort)((ushort)a.U8x16_7*(ushort)b.U8x16_7));

        public static V128 I16x8ExtMulHighI8x16U(V128 a, V128 b) => new V128(
            (ushort)((ushort)a.U8x16_8*(ushort)b.U8x16_8), (ushort)((ushort)a.U8x16_9*(ushort)b.U8x16_9),
            (ushort)((ushort)a.U8x16_A*(ushort)b.U8x16_A), (ushort)((ushort)a.U8x16_B*(ushort)b.U8x16_B),
            (ushort)((ushort)a.U8x16_C*(ushort)b.U8x16_C), (ushort)((ushort)a.U8x16_D*(ushort)b.U8x16_D),
            (ushort)((ushort)a.U8x16_E*(ushort)b.U8x16_E), (ushort)((ushort)a.U8x16_F*(ushort)b.U8x16_F));

        public static V128 I32x4ExtMulLowI16x8S(V128 a, V128 b) => new V128(
            (int)a.I16x8_0*(int)b.I16x8_0, (int)a.I16x8_1*(int)b.I16x8_1,
            (int)a.I16x8_2*(int)b.I16x8_2, (int)a.I16x8_3*(int)b.I16x8_3);

        public static V128 I32x4ExtMulHighI16x8S(V128 a, V128 b) => new V128(
            (int)a.I16x8_4*(int)b.I16x8_4, (int)a.I16x8_5*(int)b.I16x8_5,
            (int)a.I16x8_6*(int)b.I16x8_6, (int)a.I16x8_7*(int)b.I16x8_7);

        public static V128 I32x4ExtMulLowI16x8U(V128 a, V128 b) => new V128(
            (uint)a.U16x8_0*(uint)b.U16x8_0, (uint)a.U16x8_1*(uint)b.U16x8_1,
            (uint)a.U16x8_2*(uint)b.U16x8_2, (uint)a.U16x8_3*(uint)b.U16x8_3);

        public static V128 I32x4ExtMulHighI16x8U(V128 a, V128 b) => new V128(
            (uint)a.U16x8_4*(uint)b.U16x8_4, (uint)a.U16x8_5*(uint)b.U16x8_5,
            (uint)a.U16x8_6*(uint)b.U16x8_6, (uint)a.U16x8_7*(uint)b.U16x8_7);

        public static V128 I64x2ExtMulLowI32x4S(V128 a, V128 b) => new V128(
            (long)a.I32x4_0*(long)b.I32x4_0, (long)a.I32x4_1*(long)b.I32x4_1);

        public static V128 I64x2ExtMulHighI32x4S(V128 a, V128 b) => new V128(
            (long)a.I32x4_2*(long)b.I32x4_2, (long)a.I32x4_3*(long)b.I32x4_3);

        public static V128 I64x2ExtMulLowI32x4U(V128 a, V128 b) => new V128(
            (ulong)a.U32x4_0*(ulong)b.U32x4_0, (ulong)a.U32x4_1*(ulong)b.U32x4_1);

        public static V128 I64x2ExtMulHighI32x4U(V128 a, V128 b) => new V128(
            (ulong)a.U32x4_2*(ulong)b.U32x4_2, (ulong)a.U32x4_3*(ulong)b.U32x4_3);

        public static V128 I32x4DotI16x8S(V128 a, V128 b) => new V128(
            (int)a.I16x8_0*(int)b.I16x8_0+(int)a.I16x8_1*(int)b.I16x8_1,
            (int)a.I16x8_2*(int)b.I16x8_2+(int)a.I16x8_3*(int)b.I16x8_3,
            (int)a.I16x8_4*(int)b.I16x8_4+(int)a.I16x8_5*(int)b.I16x8_5,
            (int)a.I16x8_6*(int)b.I16x8_6+(int)a.I16x8_7*(int)b.I16x8_7);

        private static short Q15MulRSat(short a, short b) =>
            (short)Math.Clamp((a * b + 16384) >> 15, short.MinValue, short.MaxValue);

        public static V128 I16x8Q15MulRSatS(V128 a, V128 b) => new V128(
            Q15MulRSat(a.I16x8_0, b.I16x8_0), Q15MulRSat(a.I16x8_1, b.I16x8_1),
            Q15MulRSat(a.I16x8_2, b.I16x8_2), Q15MulRSat(a.I16x8_3, b.I16x8_3),
            Q15MulRSat(a.I16x8_4, b.I16x8_4), Q15MulRSat(a.I16x8_5, b.I16x8_5),
            Q15MulRSat(a.I16x8_6, b.I16x8_6), Q15MulRSat(a.I16x8_7, b.I16x8_7));

        // ================================================================
        // Integer conversions — extend, narrow, trunc_sat
        // Mirror: Wacs.Core/Instructions/SIMD/ViConvert.cs
        // ================================================================

        public static V128 I16x8ExtendLowI8x16S(V128 v) => new V128((short)v.I8x16_0,(short)v.I8x16_1,(short)v.I8x16_2,(short)v.I8x16_3,(short)v.I8x16_4,(short)v.I8x16_5,(short)v.I8x16_6,(short)v.I8x16_7);
        public static V128 I16x8ExtendHighI8x16S(V128 v) => new V128((short)v.I8x16_8,(short)v.I8x16_9,(short)v.I8x16_A,(short)v.I8x16_B,(short)v.I8x16_C,(short)v.I8x16_D,(short)v.I8x16_E,(short)v.I8x16_F);
        public static V128 I16x8ExtendLowI8x16U(V128 v) => new V128((ushort)v.U8x16_0,(ushort)v.U8x16_1,(ushort)v.U8x16_2,(ushort)v.U8x16_3,(ushort)v.U8x16_4,(ushort)v.U8x16_5,(ushort)v.U8x16_6,(ushort)v.U8x16_7);
        public static V128 I16x8ExtendHighI8x16U(V128 v) => new V128((ushort)v.U8x16_8,(ushort)v.U8x16_9,(ushort)v.U8x16_A,(ushort)v.U8x16_B,(ushort)v.U8x16_C,(ushort)v.U8x16_D,(ushort)v.U8x16_E,(ushort)v.U8x16_F);

        public static V128 I32x4ExtendLowI16x8S(V128 v) => new V128((int)v.I16x8_0,(int)v.I16x8_1,(int)v.I16x8_2,(int)v.I16x8_3);
        public static V128 I32x4ExtendHighI16x8S(V128 v) => new V128((int)v.I16x8_4,(int)v.I16x8_5,(int)v.I16x8_6,(int)v.I16x8_7);
        public static V128 I32x4ExtendLowI16x8U(V128 v) => new V128((uint)v.U16x8_0,(uint)v.U16x8_1,(uint)v.U16x8_2,(uint)v.U16x8_3);
        public static V128 I32x4ExtendHighI16x8U(V128 v) => new V128((uint)v.U16x8_4,(uint)v.U16x8_5,(uint)v.U16x8_6,(uint)v.U16x8_7);

        public static V128 I64x2ExtendLowI32x4S(V128 v) => new V128((long)v.I32x4_0,(long)v.I32x4_1);
        public static V128 I64x2ExtendHighI32x4S(V128 v) => new V128((long)v.I32x4_2,(long)v.I32x4_3);
        public static V128 I64x2ExtendLowI32x4U(V128 v) => new V128((ulong)v.U32x4_0,(ulong)v.U32x4_1);
        public static V128 I64x2ExtendHighI32x4U(V128 v) => new V128((ulong)v.U32x4_2,(ulong)v.U32x4_3);

        public static V128 I8x16NarrowI16x8S(V128 a, V128 b) => new V128(
            (sbyte)Math.Min(Math.Max(a.I16x8_0, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(a.I16x8_1, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(a.I16x8_2, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(a.I16x8_3, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(a.I16x8_4, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(a.I16x8_5, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(a.I16x8_6, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(a.I16x8_7, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(b.I16x8_0, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(b.I16x8_1, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(b.I16x8_2, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(b.I16x8_3, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(b.I16x8_4, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(b.I16x8_5, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(b.I16x8_6, sbyte.MinValue), sbyte.MaxValue),
            (sbyte)Math.Min(Math.Max(b.I16x8_7, sbyte.MinValue), sbyte.MaxValue));

        public static V128 I8x16NarrowI16x8U(V128 a, V128 b) => new V128(
            (byte)Math.Min(Math.Max(a.I16x8_0, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(a.I16x8_1, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(a.I16x8_2, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(a.I16x8_3, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(a.I16x8_4, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(a.I16x8_5, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(a.I16x8_6, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(a.I16x8_7, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(b.I16x8_0, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(b.I16x8_1, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(b.I16x8_2, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(b.I16x8_3, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(b.I16x8_4, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(b.I16x8_5, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(b.I16x8_6, byte.MinValue), byte.MaxValue),
            (byte)Math.Min(Math.Max(b.I16x8_7, byte.MinValue), byte.MaxValue));

        public static V128 I16x8NarrowI32x4S(V128 a, V128 b) => new V128(
            (short)Math.Min(Math.Max(a.I32x4_0, short.MinValue), short.MaxValue),
            (short)Math.Min(Math.Max(a.I32x4_1, short.MinValue), short.MaxValue),
            (short)Math.Min(Math.Max(a.I32x4_2, short.MinValue), short.MaxValue),
            (short)Math.Min(Math.Max(a.I32x4_3, short.MinValue), short.MaxValue),
            (short)Math.Min(Math.Max(b.I32x4_0, short.MinValue), short.MaxValue),
            (short)Math.Min(Math.Max(b.I32x4_1, short.MinValue), short.MaxValue),
            (short)Math.Min(Math.Max(b.I32x4_2, short.MinValue), short.MaxValue),
            (short)Math.Min(Math.Max(b.I32x4_3, short.MinValue), short.MaxValue));

        public static V128 I16x8NarrowI32x4U(V128 a, V128 b) => new V128(
            (ushort)Math.Min(Math.Max(a.I32x4_0, ushort.MinValue), ushort.MaxValue),
            (ushort)Math.Min(Math.Max(a.I32x4_1, ushort.MinValue), ushort.MaxValue),
            (ushort)Math.Min(Math.Max(a.I32x4_2, ushort.MinValue), ushort.MaxValue),
            (ushort)Math.Min(Math.Max(a.I32x4_3, ushort.MinValue), ushort.MaxValue),
            (ushort)Math.Min(Math.Max(b.I32x4_0, ushort.MinValue), ushort.MaxValue),
            (ushort)Math.Min(Math.Max(b.I32x4_1, ushort.MinValue), ushort.MaxValue),
            (ushort)Math.Min(Math.Max(b.I32x4_2, ushort.MinValue), ushort.MaxValue),
            (ushort)Math.Min(Math.Max(b.I32x4_3, ushort.MinValue), ushort.MaxValue));

        // TruncSat delegates to existing SatConversion helpers
        private static int TruncSatF32S(float v) { if (float.IsNaN(v)) return 0; if (float.IsPositiveInfinity(v)) return int.MaxValue; if (float.IsNegativeInfinity(v)) return int.MinValue; double d = v; if (d < int.MinValue) return int.MinValue; if (d > int.MaxValue) return int.MaxValue; return (int)Math.Truncate(d); }
        private static uint TruncSatF32U(float v) { if (float.IsNaN(v)) return 0; if (float.IsPositiveInfinity(v)) return uint.MaxValue; if (float.IsNegativeInfinity(v)) return 0; double d = v; if (d <= 0) return 0; if (d >= uint.MaxValue) return uint.MaxValue; return (uint)Math.Truncate(d); }
        private static int TruncSatF64S(double v) { if (double.IsNaN(v)) return 0; if (double.IsPositiveInfinity(v)) return int.MaxValue; if (double.IsNegativeInfinity(v)) return int.MinValue; if (v < int.MinValue) return int.MinValue; if (v > int.MaxValue) return int.MaxValue; return (int)Math.Truncate(v); }
        private static uint TruncSatF64U(double v) { if (double.IsNaN(v)) return 0; if (double.IsPositiveInfinity(v)) return uint.MaxValue; if (double.IsNegativeInfinity(v)) return 0; if (v < 0) return 0; if (v > uint.MaxValue) return uint.MaxValue; return (uint)Math.Truncate(v); }

        public static V128 I32x4TruncSatF32x4S(V128 c) => new V128(TruncSatF32S(c.F32x4_0),TruncSatF32S(c.F32x4_1),TruncSatF32S(c.F32x4_2),TruncSatF32S(c.F32x4_3));
        public static V128 I32x4TruncSatF32x4U(V128 c) => new V128(TruncSatF32U(c.F32x4_0),TruncSatF32U(c.F32x4_1),TruncSatF32U(c.F32x4_2),TruncSatF32U(c.F32x4_3));
        public static V128 I32x4TruncSatF64x2SZero(V128 c) => new V128(TruncSatF64S(c.F64x2_0),TruncSatF64S(c.F64x2_1),0,0);
        public static V128 I32x4TruncSatF64x2UZero(V128 c) => new V128(TruncSatF64U(c.F64x2_0),TruncSatF64U(c.F64x2_1),0,0);

        // ================================================================
        // Float binary ops
        // Mirror: Wacs.Core/Instructions/SIMD/VfBinOp.cs
        // ================================================================

        public static V128 F32x4Add(V128 a, V128 b) => new V128(a.F32x4_0+b.F32x4_0, a.F32x4_1+b.F32x4_1, a.F32x4_2+b.F32x4_2, a.F32x4_3+b.F32x4_3);
        public static V128 F32x4Sub(V128 a, V128 b) => new V128(a.F32x4_0-b.F32x4_0, a.F32x4_1-b.F32x4_1, a.F32x4_2-b.F32x4_2, a.F32x4_3-b.F32x4_3);
        public static V128 F32x4Mul(V128 a, V128 b) => new V128(a.F32x4_0*b.F32x4_0, a.F32x4_1*b.F32x4_1, a.F32x4_2*b.F32x4_2, a.F32x4_3*b.F32x4_3);
        public static V128 F32x4Div(V128 a, V128 b) => new V128(a.F32x4_0/b.F32x4_0, a.F32x4_1/b.F32x4_1, a.F32x4_2/b.F32x4_2, a.F32x4_3/b.F32x4_3);
        public static V128 F32x4Min(V128 a, V128 b) => new V128(Math.Min(a.F32x4_0,b.F32x4_0),Math.Min(a.F32x4_1,b.F32x4_1),Math.Min(a.F32x4_2,b.F32x4_2),Math.Min(a.F32x4_3,b.F32x4_3));
        public static V128 F32x4Max(V128 a, V128 b) => new V128(Math.Max(a.F32x4_0,b.F32x4_0),Math.Max(a.F32x4_1,b.F32x4_1),Math.Max(a.F32x4_2,b.F32x4_2),Math.Max(a.F32x4_3,b.F32x4_3));

        private static float PseudoMinF32(float a, float b) { if (float.IsNaN(a)) return float.NaN; if (float.IsNaN(b)) return a; return a < b ? a : b; }
        private static float PseudoMaxF32(float a, float b) { if (float.IsNaN(a)) return float.NaN; if (float.IsNaN(b)) return a; return a > b ? a : b; }
        public static V128 F32x4PMin(V128 a, V128 b) => new V128(PseudoMinF32(a.F32x4_0,b.F32x4_0),PseudoMinF32(a.F32x4_1,b.F32x4_1),PseudoMinF32(a.F32x4_2,b.F32x4_2),PseudoMinF32(a.F32x4_3,b.F32x4_3));
        public static V128 F32x4PMax(V128 a, V128 b) => new V128(PseudoMaxF32(a.F32x4_0,b.F32x4_0),PseudoMaxF32(a.F32x4_1,b.F32x4_1),PseudoMaxF32(a.F32x4_2,b.F32x4_2),PseudoMaxF32(a.F32x4_3,b.F32x4_3));

        public static V128 F64x2Add(V128 a, V128 b) => new V128(a.F64x2_0+b.F64x2_0, a.F64x2_1+b.F64x2_1);
        public static V128 F64x2Sub(V128 a, V128 b) => new V128(a.F64x2_0-b.F64x2_0, a.F64x2_1-b.F64x2_1);
        public static V128 F64x2Mul(V128 a, V128 b) => new V128(a.F64x2_0*b.F64x2_0, a.F64x2_1*b.F64x2_1);
        public static V128 F64x2Div(V128 a, V128 b) => new V128(a.F64x2_0/b.F64x2_0, a.F64x2_1/b.F64x2_1);
        public static V128 F64x2Min(V128 a, V128 b) => new V128(Math.Min(a.F64x2_0,b.F64x2_0),Math.Min(a.F64x2_1,b.F64x2_1));
        public static V128 F64x2Max(V128 a, V128 b) => new V128(Math.Max(a.F64x2_0,b.F64x2_0),Math.Max(a.F64x2_1,b.F64x2_1));

        private static double PseudoMinF64(double a, double b) { if (double.IsNaN(a)) return double.NaN; if (double.IsNaN(b)) return a; return a < b ? a : b; }
        private static double PseudoMaxF64(double a, double b) { if (double.IsNaN(a)) return double.NaN; if (double.IsNaN(b)) return a; return a > b ? a : b; }
        public static V128 F64x2PMin(V128 a, V128 b) => new V128(PseudoMinF64(a.F64x2_0,b.F64x2_0),PseudoMinF64(a.F64x2_1,b.F64x2_1));
        public static V128 F64x2PMax(V128 a, V128 b) => new V128(PseudoMaxF64(a.F64x2_0,b.F64x2_0),PseudoMaxF64(a.F64x2_1,b.F64x2_1));

        // ================================================================
        // Float unary ops
        // Mirror: Wacs.Core/Instructions/SIMD/VfUnOp.cs
        // ================================================================

        public static V128 F32x4Abs(V128 v) => new V128(Math.Abs(v.F32x4_0),Math.Abs(v.F32x4_1),Math.Abs(v.F32x4_2),Math.Abs(v.F32x4_3));
        public static V128 F32x4Neg(V128 v) => new V128(-v.F32x4_0,-v.F32x4_1,-v.F32x4_2,-v.F32x4_3);
        public static V128 F32x4Sqrt(V128 v) => new V128((float)Math.Sqrt(v.F32x4_0),(float)Math.Sqrt(v.F32x4_1),(float)Math.Sqrt(v.F32x4_2),(float)Math.Sqrt(v.F32x4_3));
        public static V128 F32x4Ceil(V128 v) => new V128((float)Math.Ceiling(v.F32x4_0),(float)Math.Ceiling(v.F32x4_1),(float)Math.Ceiling(v.F32x4_2),(float)Math.Ceiling(v.F32x4_3));
        public static V128 F32x4Floor(V128 v) => new V128((float)Math.Floor(v.F32x4_0),(float)Math.Floor(v.F32x4_1),(float)Math.Floor(v.F32x4_2),(float)Math.Floor(v.F32x4_3));
        public static V128 F32x4Trunc(V128 v) => new V128((float)Math.Truncate(v.F32x4_0),(float)Math.Truncate(v.F32x4_1),(float)Math.Truncate(v.F32x4_2),(float)Math.Truncate(v.F32x4_3));
        public static V128 F32x4Nearest(V128 v) => new V128((float)Math.Round(v.F32x4_0),(float)Math.Round(v.F32x4_1),(float)Math.Round(v.F32x4_2),(float)Math.Round(v.F32x4_3));

        public static V128 F64x2Abs(V128 v) => new V128(Math.Abs(v.F64x2_0),Math.Abs(v.F64x2_1));
        public static V128 F64x2Neg(V128 v) => new V128(-v.F64x2_0,-v.F64x2_1);
        public static V128 F64x2Sqrt(V128 v) => new V128(Math.Sqrt(v.F64x2_0),Math.Sqrt(v.F64x2_1));
        public static V128 F64x2Ceil(V128 v) => new V128(Math.Ceiling(v.F64x2_0),Math.Ceiling(v.F64x2_1));
        public static V128 F64x2Floor(V128 v) => new V128(Math.Floor(v.F64x2_0),Math.Floor(v.F64x2_1));
        public static V128 F64x2Trunc(V128 v) => new V128(Math.Truncate(v.F64x2_0),Math.Truncate(v.F64x2_1));
        public static V128 F64x2Nearest(V128 v) => new V128(Math.Round(v.F64x2_0),Math.Round(v.F64x2_1));

        // ================================================================
        // Float relational ops
        // Mirror: Wacs.Core/Instructions/SIMD/VfRelOp.cs
        // ================================================================

        public static V128 F32x4Eq(V128 a, V128 b) => new V128(a.F32x4_0==b.F32x4_0?-1:0, a.F32x4_1==b.F32x4_1?-1:0, a.F32x4_2==b.F32x4_2?-1:0, a.F32x4_3==b.F32x4_3?-1:0);
        public static V128 F32x4Ne(V128 a, V128 b) => new V128(a.F32x4_0!=b.F32x4_0?-1:0, a.F32x4_1!=b.F32x4_1?-1:0, a.F32x4_2!=b.F32x4_2?-1:0, a.F32x4_3!=b.F32x4_3?-1:0);
        public static V128 F32x4Lt(V128 a, V128 b) => new V128(a.F32x4_0<b.F32x4_0?-1:0, a.F32x4_1<b.F32x4_1?-1:0, a.F32x4_2<b.F32x4_2?-1:0, a.F32x4_3<b.F32x4_3?-1:0);
        public static V128 F32x4Gt(V128 a, V128 b) => new V128(a.F32x4_0>b.F32x4_0?-1:0, a.F32x4_1>b.F32x4_1?-1:0, a.F32x4_2>b.F32x4_2?-1:0, a.F32x4_3>b.F32x4_3?-1:0);
        public static V128 F32x4Le(V128 a, V128 b) => new V128(a.F32x4_0<=b.F32x4_0?-1:0, a.F32x4_1<=b.F32x4_1?-1:0, a.F32x4_2<=b.F32x4_2?-1:0, a.F32x4_3<=b.F32x4_3?-1:0);
        public static V128 F32x4Ge(V128 a, V128 b) => new V128(a.F32x4_0>=b.F32x4_0?-1:0, a.F32x4_1>=b.F32x4_1?-1:0, a.F32x4_2>=b.F32x4_2?-1:0, a.F32x4_3>=b.F32x4_3?-1:0);

        public static V128 F64x2Eq(V128 a, V128 b) => new V128(a.F64x2_0==b.F64x2_0?-1:0, a.F64x2_1==b.F64x2_1?-1:0);
        public static V128 F64x2Ne(V128 a, V128 b) => new V128(a.F64x2_0!=b.F64x2_0?-1:0, a.F64x2_1!=b.F64x2_1?-1:0);
        public static V128 F64x2Lt(V128 a, V128 b) => new V128(a.F64x2_0<b.F64x2_0?-1:0, a.F64x2_1<b.F64x2_1?-1:0);
        public static V128 F64x2Gt(V128 a, V128 b) => new V128(a.F64x2_0>b.F64x2_0?-1:0, a.F64x2_1>b.F64x2_1?-1:0);
        public static V128 F64x2Le(V128 a, V128 b) => new V128(a.F64x2_0<=b.F64x2_0?-1:0, a.F64x2_1<=b.F64x2_1?-1:0);
        public static V128 F64x2Ge(V128 a, V128 b) => new V128(a.F64x2_0>=b.F64x2_0?-1:0, a.F64x2_1>=b.F64x2_1?-1:0);

        // ================================================================
        // Float conversions — convert, promote, demote
        // Mirror: Wacs.Core/Instructions/SIMD/VfConvert.cs
        // ================================================================

        public static V128 F32x4ConvertI32x4S(V128 v) => new V128((float)v.I32x4_0,(float)v.I32x4_1,(float)v.I32x4_2,(float)v.I32x4_3);
        public static V128 F32x4ConvertI32x4U(V128 v) => new V128((float)v.U32x4_0,(float)v.U32x4_1,(float)v.U32x4_2,(float)v.U32x4_3);
        public static V128 F64x2ConvertLowI32x4S(V128 v) => new V128((double)v.I32x4_0,(double)v.I32x4_1);
        public static V128 F64x2ConvertLowI32x4U(V128 v) => new V128((double)v.U32x4_0,(double)v.U32x4_1);
        public static V128 F32x4DemoteF64x2Zero(V128 v) => new V128((float)v.F64x2_0,(float)v.F64x2_1,0.0f,0.0f);
        public static V128 F64x2PromoteLowF32x4(V128 v) => new V128((double)v.F32x4_0,(double)v.F32x4_1);

        // ================================================================
        // Relaxed SIMD — unary (trunc)
        // Mirror: Wacs.Core/Instructions/SIMD/RelaxedUnOp.cs
        // Uses non-ALT path (default interpreter behavior)
        // ================================================================

        public static V128 I32x4RelaxedTruncF32x4S(V128 a)
        {
            MV128 result = new MV128();
            for (int i = 0; i < 4; i++)
            {
                float f = (float)i;
                if (float.IsNaN(a[f])) { result[i] = int.MinValue; continue; }
                float r = (float)Math.Truncate(a[f]);
                if (r < int.MinValue) result[i] = int.MinValue;
                else if (r > int.MaxValue) result[i] = int.MaxValue;
                else result[i] = (int)r;
            }
            return result;
        }

        public static V128 I32x4RelaxedTruncF32x4U(V128 a)
        {
            MV128 result = new MV128();
            for (uint i = 0; i < 4; i++)
            {
                float f = (float)i;
                if (float.IsNaN(a[f])) { result[i] = uint.MaxValue; continue; }
                float r = (float)Math.Truncate(a[f]);
                if (r < uint.MinValue) result[i] = uint.MaxValue;
                else if (r > int.MaxValue) result[i] = uint.MaxValue;
                else result[i] = (uint)r;
            }
            return result;
        }

        public static V128 I32x4RelaxedTruncF64x2SZero(V128 a)
        {
            MV128 result = new MV128();
            for (int i = 0; i < 2; i++)
            {
                double d = (double)i;
                if (double.IsNaN(a[d])) { result[i] = int.MinValue; continue; }
                double r = Math.Truncate(a[d]);
                if (r < int.MinValue) result[i] = int.MinValue;
                else if (r > int.MaxValue) result[i] = int.MaxValue;
                else result[i] = (int)r;
            }
            return result;
        }

        public static V128 I32x4RelaxedTruncF64x2UZero(V128 a)
        {
            MV128 result = new MV128();
            for (uint i = 0; i < 2; i++)
            {
                double d = (double)i;
                if (double.IsNaN(a[d])) { result[i] = uint.MaxValue; continue; }
                double r = Math.Truncate(a[d]);
                if (r < uint.MinValue) result[i] = uint.MaxValue;
                else if (r > uint.MaxValue) result[i] = uint.MaxValue;
                else result[i] = (uint)r;
            }
            return result;
        }

        // ================================================================
        // Relaxed SIMD — binary
        // Mirror: Wacs.Core/Instructions/SIMD/RelaxedBinOp.cs
        // ================================================================

        public static V128 I8x16RelaxedSwizzle(V128 a, V128 s)
        {
            MV128 result = new MV128();
            for (byte i = 0; i < 16; ++i)
            {
                if (s[i] < 16) result[i] = a[s[i]];
                else if (s[i] < 128) result[i] = a[(byte)(s[i] % 16)];
                else result[i] = 0;
            }
            return result;
        }

        public static V128 F32x4RelaxedMin(V128 a, V128 b)
        {
            MV128 result = new MV128();
            for (float i = 0.0f; i < 4.0f; i += 1.0f)
            {
                if (float.IsNaN(a[i]) || float.IsNaN(b[i])) result[i] = a[i];
                else if ((a[i] == -0.0f && b[i] == 0.0f) || (a[i] == 0.0f && b[i] == -0.0f)) result[i] = a[i];
                else result[i] = Math.Min(a[i], b[i]);
            }
            return result;
        }

        public static V128 F32x4RelaxedMax(V128 a, V128 b)
        {
            MV128 result = new MV128();
            for (float i = 0.0f; i < 4.0f; i += 1.0f)
            {
                if (float.IsNaN(a[i]) || float.IsNaN(b[i])) result[i] = a[i];
                else if ((a[i] == -0.0f && b[i] == 0.0f) || (a[i] == 0.0f && b[i] == -0.0f)) result[i] = b[i];
                else result[i] = Math.Max(a[i], b[i]);
            }
            return result;
        }

        public static V128 F64x2RelaxedMin(V128 a, V128 b)
        {
            MV128 result = new MV128();
            for (double i = 0.0; i < 2.0; i += 1.0)
            {
                if (double.IsNaN(a[i]) || double.IsNaN(b[i])) result[i] = a[i];
                else if ((a[i] == -0.0 && b[i] == 0.0) || (a[i] == 0.0 && b[i] == -0.0)) result[i] = a[i];
                else result[i] = Math.Min(a[i], b[i]);
            }
            return result;
        }

        public static V128 F64x2RelaxedMax(V128 a, V128 b)
        {
            MV128 result = new MV128();
            for (double i = 0.0; i < 2.0; i += 1.0)
            {
                if (double.IsNaN(a[i]) || double.IsNaN(b[i])) result[i] = a[i];
                else if ((a[i] == -0.0 && b[i] == 0.0) || (a[i] == 0.0 && b[i] == -0.0)) result[i] = b[i];
                else result[i] = Math.Max(a[i], b[i]);
            }
            return result;
        }

        public static V128 I16x8RelaxedQ15MulrS(V128 a, V128 b)
        {
            MV128 result = new MV128();
            for (short i = 0; i < 8; i += 1)
            {
                if (a[i] == short.MinValue && b[i] == short.MinValue)
                    result[i] = short.MaxValue;
                else
                    result[i] = (short)((a[i] * b[i] + 0x4000) >> 15);
            }
            return result;
        }

        public static V128 I16x8RelaxedDotI8x16I7x16S(V128 a, V128 b)
        {
            MV128 left = new MV128();
            MV128 right = new MV128();
            for (sbyte i = 0; i < 16; i += 1)
            {
                int lhs = a[i];
                int rhs = (b[i] & 0x80) != 0 ? b[i] : b[i];
                if ((i & 1) == 0) left[(short)(i >> 1)] = (short)(lhs * rhs);
                else right[(short)(i >> 1)] = (short)(lhs * rhs);
            }
            MV128 result = new MV128();
            for (short i = 0; i < 8; i += 1)
                result[i] = (short)(left[i] + right[i]);
            return result;
        }

        // ================================================================
        // Relaxed SIMD — ternary
        // Mirror: Wacs.Core/Instructions/SIMD/RelaxedTernOp.cs
        // ================================================================

        public static V128 I8x16RelaxedLaneselect(V128 a, V128 b, V128 m) => V128BitSelect(a, b, m);
        public static V128 I16x8RelaxedLaneselect(V128 a, V128 b, V128 m) => V128BitSelect(a, b, m);
        public static V128 I32x4RelaxedLaneselect(V128 a, V128 b, V128 m) => V128BitSelect(a, b, m);
        public static V128 I64x2RelaxedLaneselect(V128 a, V128 b, V128 m) => V128BitSelect(a, b, m);

        public static V128 F32x4RelaxedMAdd(V128 a, V128 b, V128 c)
        {
            MV128 result = new MV128();
            for (float i = 0.0f; i < 4.0f; i += 1.0f)
                result[i] = a[i] * b[i] + c[i];
            return result;
        }

        public static V128 F32x4RelaxedNMAdd(V128 a, V128 b, V128 c)
        {
            MV128 result = new MV128();
            for (float i = 0.0f; i < 4.0f; i += 1.0f)
                result[i] = -(a[i] * b[i]) + c[i];
            return result;
        }

        public static V128 F64x2RelaxedMAdd(V128 a, V128 b, V128 c)
        {
            MV128 result = new MV128();
            for (double i = 0.0; i < 2.0; i += 1.0)
                result[i] = a[i] * b[i] + c[i];
            return result;
        }

        public static V128 F64x2RelaxedNMAdd(V128 a, V128 b, V128 c)
        {
            MV128 result = new MV128();
            for (double i = 0.0; i < 2.0; i += 1.0)
                result[i] = -(a[i] * b[i]) + c[i];
            return result;
        }

        public static V128 I32x4RelaxedDotI8x16I7x16AddS(V128 a, V128 b, V128 c)
        {
            MV128 intermediate = new MV128();
            for (sbyte i = 0; i < 8; i += 1)
            {
                int lhs = a[i];
                int rhs = (b[i] & 0x80) != 0 ? b[i] : b[i];
                intermediate[i] = (sbyte)(lhs * rhs);
            }
            MV128 tmp = new MV128();
            for (short i = 0; i < 8; i += 1)
                tmp[i] = (short)(intermediate[(sbyte)(i * 2)] + intermediate[(sbyte)(i * 2 + 1)]);
            MV128 result = new MV128();
            for (int i = 0; i < 4; i += 1)
                result[i] = tmp[(short)(i * 2)] + tmp[(short)(i * 2 + 1)] + c[i];
            return result;
        }

        // ================================================================
        // Intrinsics implementations (kept from original)
        // ================================================================

        public static class Intrinsics
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 V128Not(V128 v)
            {
                var vec = Unsafe.As<V128, Vector128<byte>>(ref v);
                var result = ~vec;
                return Unsafe.As<Vector128<byte>, V128>(ref result);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 V128And(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<byte>>(ref a);
                var vb = Unsafe.As<V128, Vector128<byte>>(ref b);
                var result = va & vb;
                return Unsafe.As<Vector128<byte>, V128>(ref result);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 V128Or(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<byte>>(ref a);
                var vb = Unsafe.As<V128, Vector128<byte>>(ref b);
                var result = va | vb;
                return Unsafe.As<Vector128<byte>, V128>(ref result);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 V128Xor(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<byte>>(ref a);
                var vb = Unsafe.As<V128, Vector128<byte>>(ref b);
                var result = va ^ vb;
                return Unsafe.As<Vector128<byte>, V128>(ref result);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 V128AndNot(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<byte>>(ref a);
                var vb = Unsafe.As<V128, Vector128<byte>>(ref b);
                var result = Vector128.AndNot(va, vb);
                return Unsafe.As<Vector128<byte>, V128>(ref result);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 V128BitSelect(V128 v1, V128 v2, V128 v3)
            {
                var a = Unsafe.As<V128, Vector128<byte>>(ref v1);
                var b = Unsafe.As<V128, Vector128<byte>>(ref v2);
                var c = Unsafe.As<V128, Vector128<byte>>(ref v3);
                var result = Vector128.ConditionalSelect(c, a, b);
                return Unsafe.As<Vector128<byte>, V128>(ref result);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int V128AnyTrue(V128 v)
            {
                var vec = Unsafe.As<V128, Vector128<byte>>(ref v);
                return vec != Vector128<byte>.Zero ? 1 : 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I8x16Add(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<byte>>(ref a);
                var vb = Unsafe.As<V128, Vector128<byte>>(ref b);
                var r = va + vb;
                return Unsafe.As<Vector128<byte>, V128>(ref r);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I8x16Sub(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<byte>>(ref a);
                var vb = Unsafe.As<V128, Vector128<byte>>(ref b);
                var r = va - vb;
                return Unsafe.As<Vector128<byte>, V128>(ref r);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I16x8Add(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<short>>(ref a);
                var vb = Unsafe.As<V128, Vector128<short>>(ref b);
                var r = va + vb;
                return Unsafe.As<Vector128<short>, V128>(ref r);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I16x8Sub(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<short>>(ref a);
                var vb = Unsafe.As<V128, Vector128<short>>(ref b);
                var r = va - vb;
                return Unsafe.As<Vector128<short>, V128>(ref r);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I16x8Mul(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<short>>(ref a);
                var vb = Unsafe.As<V128, Vector128<short>>(ref b);
                var r = va * vb;
                return Unsafe.As<Vector128<short>, V128>(ref r);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I32x4Add(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<int>>(ref a);
                var vb = Unsafe.As<V128, Vector128<int>>(ref b);
                var r = va + vb;
                return Unsafe.As<Vector128<int>, V128>(ref r);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I32x4Sub(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<int>>(ref a);
                var vb = Unsafe.As<V128, Vector128<int>>(ref b);
                var r = va - vb;
                return Unsafe.As<Vector128<int>, V128>(ref r);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I32x4Mul(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<int>>(ref a);
                var vb = Unsafe.As<V128, Vector128<int>>(ref b);
                var r = va * vb;
                return Unsafe.As<Vector128<int>, V128>(ref r);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I64x2Add(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<long>>(ref a);
                var vb = Unsafe.As<V128, Vector128<long>>(ref b);
                var r = va + vb;
                return Unsafe.As<Vector128<long>, V128>(ref r);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I64x2Sub(V128 a, V128 b)
            {
                var va = Unsafe.As<V128, Vector128<long>>(ref a);
                var vb = Unsafe.As<V128, Vector128<long>>(ref b);
                var r = va - vb;
                return Unsafe.As<Vector128<long>, V128>(ref r);
            }

            // i64x2.mul: NO direct Vector128 operator * for long.
            public static V128 I64x2Mul(V128 a, V128 b)
            {
                return SimdHelpers.I64x2Mul(a, b);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I8x16Neg(V128 v)
            {
                var vec = Unsafe.As<V128, Vector128<byte>>(ref v);
                var r = Vector128<byte>.Zero - vec;
                return Unsafe.As<Vector128<byte>, V128>(ref r);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I16x8Neg(V128 v)
            {
                var vec = Unsafe.As<V128, Vector128<short>>(ref v);
                var r = Vector128.Negate(vec);
                return Unsafe.As<Vector128<short>, V128>(ref r);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I32x4Neg(V128 v)
            {
                var vec = Unsafe.As<V128, Vector128<int>>(ref v);
                var r = Vector128.Negate(vec);
                return Unsafe.As<Vector128<int>, V128>(ref r);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static V128 I64x2Neg(V128 v)
            {
                var vec = Unsafe.As<V128, Vector128<long>>(ref v);
                var r = Vector128.Negate(vec);
                return Unsafe.As<Vector128<long>, V128>(ref r);
            }
        }
    }
}
