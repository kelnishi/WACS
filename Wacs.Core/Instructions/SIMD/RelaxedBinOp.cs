// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

// ReSharper disable InconsistentNaming
namespace Wacs.Core.Instructions.Numeric
{
    // @Spec https://github.com/WebAssembly/relaxed-simd/blob/main/proposals/relaxed-simd/Overview.md
    public partial class NumericInst
    {
        public static readonly NumericInst I8x16RelaxedSwizzle = new(SimdCode.I8x16RelaxedSwizzle, ExecuteI8x16RelaxedSwizzle, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4RelaxedMin = new(SimdCode.F32x4RelaxedMin, ExecuteF32x4RelaxedMin, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4RelaxedMax = new(SimdCode.F32x4RelaxedMax, ExecuteF32x4RelaxedMax, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2RelaxedMin = new(SimdCode.F64x2RelaxedMin, ExecuteF64x2RelaxedMin, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2RelaxedMax = new(SimdCode.F64x2RelaxedMax, ExecuteF64x2RelaxedMax, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8RelaxedQ15MulrS = new(SimdCode.I16x8RelaxedQ15MulrS, ExecuteI16x8RelaxedQ15MulrS, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8RelaxedDotI8x16I7x16S = new (SimdCode.I16x8RelaxedDotI8x16I7x16S, ExecuteI16x8RelaxedDotI8x16I7x16S, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        private static void ExecuteI8x16RelaxedSwizzle(ExecContext context)
        {
            V128 s = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (byte i = 0; i < 16; ++i)
            {
                if (s[i] < 16)
                    result[i] = a[s[i]];
                else if (s[i] < 128)
#if RELAXED_SIMD_ALT
                    result[i] = 0;
#else
                    result[i] = a[(byte)(s[i] % 16)];
#endif
                else
                    result[i] = 0;
            }
            
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4RelaxedMin(ExecContext context)
        {
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (float i = 0.0f; i < 4.0f; i += 1.0f)
            {
                if (float.IsNaN(a[i]) || float.IsNaN(b[i]))
#if RELAXED_SIMD_ALT
                    result[i] = b[i];
#else
                    result[i] = a[i];
#endif
                else if ((a[i] == -0.0f && b[i] == 0.0f) || (a[i] == 0.0f && b[i] == -0.0f))
#if RELAXED_SIMD_ALT
                    result[i] = b[i];
#else
                    result[i] = a[i];
#endif
                else
                    result[i] = Math.Min(a[i], b[i]);
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4RelaxedMax(ExecContext context)
        {
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (float i = 0.0f; i < 4.0f; i += 1.0f)
            {
                if (float.IsNaN(a[i]) || float.IsNaN(b[i]))
#if RELAXED_SIMD_ALT
                    result[i] = b[i];
#else
                    result[i] = a[i];
#endif
                else if ((a[i] == -0.0f && b[i] == 0.0f) || (a[i] == 0.0f && b[i] == -0.0f))
#if RELAXED_SIMD_ALT
                    result[i] = a[i];
#else
                    result[i] = b[i];
#endif
                else
                    result[i] = Math.Max(a[i], b[i]);
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2RelaxedMin(ExecContext context)
        {
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (double i = 0.0; i < 2.0; i += 1.0)
            {
                if (double.IsNaN(a[i]) || double.IsNaN(b[i]))
#if RELAXED_SIMD_ALT
                    result[i] = b[i];
#else
                    result[i] = a[i];
#endif
                else if ((a[i] == -0.0 && b[i] == 0.0) || (a[i] == 0.0 && b[i] == -0.0))
#if RELAXED_SIMD_ALT
                    result[i] = b[i];
#else
                    result[i] = a[i];
#endif
                else
                    result[i] = Math.Min(a[i], b[i]);
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2RelaxedMax(ExecContext context)
        {
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (double i = 0.0; i < 2.0; i += 1.0)
            {
                if (double.IsNaN(a[i]) || double.IsNaN(b[i]))
#if RELAXED_SIMD_ALT
                    result[i] = b[i];
#else
                    result[i] = a[i];
#endif
                else if ((a[i] == -0.0 && b[i] == 0.0) || (a[i] == 0.0 && b[i] == -0.0))
#if RELAXED_SIMD_ALT
                    result[i] = a[i];
#else
                    result[i] = b[i];
#endif
                else
                    result[i] = Math.Max(a[i], b[i]);
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8RelaxedQ15MulrS(ExecContext context)
        {
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (short i = 0; i < 8; i += 1)
            {
                if (a[i] == short.MinValue && b[i] == short.MinValue)
#if RELAXED_SIMD_ALT
                    result[i] = short.MinValue;
#else
                    result[i] = short.MaxValue;
#endif
                else
                {
                    result[i] = (short)((a[i] * b[i] + 0x4000) >> 15);
                }
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8RelaxedDotI8x16I7x16S(ExecContext context)
        {
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();
            MV128 intermediate = new MV128();
            for (sbyte i = 0; i < 8; i += 1)
            {
                if ((b[i] & 0x80) != 0)
                {
                    int lhs = a[i];
#if RELAXED_SIMD_ALT
                    int rhs = b[(byte)i];
#else
                    int rhs = b[i];
#endif
                    intermediate[i] = (sbyte)(lhs * rhs);
                }
                else
                {
                    intermediate[i] = (sbyte)(a[i] * b[i]);
                }
            }

            for (short i = 0; i < 8; i += 1)
            {
#if RELAXED_SIMD_ALT
                result[i] = (short)(byte)(intermediate[(sbyte)(i * 2)] + intermediate[(sbyte)(i * 2 + 1)]);
#else
                result[i] = (short)(intermediate[(sbyte)(i * 2)] + intermediate[(sbyte)(i * 2 + 1)]);
#endif
            }
            context.OpStack.PushV128(result);
        }
    }
}