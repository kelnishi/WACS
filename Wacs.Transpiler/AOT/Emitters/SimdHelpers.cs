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
        // Chunk 2: Bitwise ops — Scalar reference implementations
        // Mirror: Wacs.Core/Instructions/SIMD/VvUnOp.cs, VvBinOp.cs, VvTernOp.cs, VvTestOp.cs
        // ================================================================

        // v128.not: ~v
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128Not(V128 v) => ~v;

        // v128.and: a & b
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128And(V128 a, V128 b) => a & b;

        // v128.or: a | b
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128Or(V128 a, V128 b) => a | b;

        // v128.xor: a ^ b
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128Xor(V128 a, V128 b) => a ^ b;

        // v128.andnot: a & ~b
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128AndNot(V128 a, V128 b) => a & ~b;

        // v128.bitselect: (v1 & v3) | (v2 & ~v3)
        // Spec: select bits from v1 where v3 is 1, v2 where v3 is 0
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 V128BitSelect(V128 v1, V128 v2, V128 v3)
            => (v1 & v3) | (v2 & ~v3);

        // v128.any_true: 1 if any bit is set, 0 otherwise
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int V128AnyTrue(V128 v)
            => (v.U64x2_0 != 0UL || v.U64x2_1 != 0UL) ? 1 : 0;

        // ================================================================
        // Chunk 3: Integer arithmetic — Scalar reference implementations
        // ================================================================

        // --- add/sub per shape ---
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

        // --- neg per shape ---
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

        // ================================================================
        // Chunk 2: Bitwise ops — Intrinsics implementations
        // These produce identical results to the scalar versions.
        // Gated by transpiler options (future — currently unused).
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

            // ================================================================
            // Chunk 3: Integer arithmetic — Intrinsics
            //
            // CLR coverage:
            // - add/sub: Vector128 operator +/- for all shapes ✓
            // - mul i16x8/i32x4: Vector128 operator * ✓
            // - mul i64x2: NO direct CLR intrinsic — requires emulation
            //   (Sse41.MultiplyLow for low 32 bits + manual high bits)
            // - neg: Vector128.Negate ✓
            // - i8x16 mul: NO direct CLR intrinsic — requires widening to i16, mul, narrow
            // ================================================================

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
            // Must use scalar fallback or emulate via 32-bit multiply + shift.
            public static V128 I64x2Mul(V128 a, V128 b)
            {
                // Fallback to scalar — Vector128<long> * is not available in .NET 8
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
