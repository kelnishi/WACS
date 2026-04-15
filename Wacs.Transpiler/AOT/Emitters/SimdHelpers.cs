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
        }
    }
}
