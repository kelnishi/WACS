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
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I32x4RelaxedTruncF32x4S = new(SimdCode.I32x4RelaxedTruncF32x4S, ExecuteI32x4RelaxedTruncF32x4S, ValidateOperands(pop: ValType.V128, push: ValType.V128), 0);
        public static readonly NumericInst I32x4RelaxedTruncF32x4U = new(SimdCode.I32x4RelaxedTruncF32x4U, ExecuteI32x4RelaxedTruncF32x4S, ValidateOperands(pop: ValType.V128, push: ValType.V128), 0);
        public static readonly NumericInst I32x4RelaxedTruncF64x2SZero = new (SimdCode.I32x4RelaxedTruncF64x2SZero, ExecuteI32x4RelaxedTruncF64x2SZero, ValidateOperands(pop: ValType.V128, push: ValType.V128), 0);
        public static readonly NumericInst I32x4RelaxedTruncF64x2UZero = new (SimdCode.I32x4RelaxedTruncF64x2UZero, ExecuteI32x4RelaxedTruncF64x2UZero, ValidateOperands(pop: ValType.V128, push: ValType.V128), 0);

        private static void ExecuteI32x4RelaxedTruncF32x4S(ExecContext context)
        {
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (int i = 0; i < 4; i++)
            {
                float f = (float)i;
                if (float.IsNaN(a[f]))
                {
#if RELAXED_SIMD_ALT
                    result[i] = 0;
#else
                    result[i] = int.MinValue;
#endif
                    continue;
                }

                float r = (float)Math.Truncate(a[f]);
                if (r < int.MinValue)
                    result[i] = int.MinValue;
                else if (r > int.MaxValue)
#if RELAXED_SIMD_ALT
                    result[i] = int.MinValue;
#else
                    result[i] = int.MaxValue;
#endif
                else
                {
                    result[i] = (int)r;
                }
            }
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4RelaxedTruncF32x4U(ExecContext context)
        {
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (uint i = 0; i < 4; i ++)
            {
                float f = (float)i;
                if (float.IsNaN(a[f]))
                {
#if RELAXED_SIMD_ALT
                    result[i] = 0;
#else
                    result[i] = uint.MaxValue;
#endif
                    continue;
                }

                float r = (float)Math.Truncate(a[f]);
                if (r < uint.MinValue)
#if RELAXED_SIMD_ALT
                    result[i] = uint.MinValue;
#else
                    result[i] = uint.MaxValue;
#endif
                else if (r > int.MaxValue)
                    result[i] = uint.MaxValue;
                else
                {
                    result[i] = (uint)r;
                }
            }
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4RelaxedTruncF64x2SZero(ExecContext context)
        {
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (int i = 0; i < 2; i++)
            {
                double d = (double)i;
                if (double.IsNaN(a[d]))
#if RELAXED_SIMD_ALT
                    result[i] = 0;
#else
                    result[i] = int.MinValue;
#endif
                double r = Math.Truncate(a[d]);
                if (r < int.MinValue)
                    result[i] = int.MinValue;
                else if (r > int.MaxValue)
#if RELAXED_SIMD_ALT
                    result[i] = int.MinValue;
#else
                    result[i] = int.MaxValue;
#endif
                else
                {
                    result[i] = (int)r;
                }
            }
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4RelaxedTruncF64x2UZero(ExecContext context)
        {
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (uint i = 0; i < 2; i++)
            {
                double d = (double)i;
                if (double.IsNaN(a[d]))
#if RELAXED_SIMD_ALT
                    result[i] = 0;
#else
                    result[i] = uint.MaxValue;
#endif
                double r = Math.Truncate(a[d]);
                if (r < uint.MinValue)
#if RELAXED_SIMD_ALT
                    result[i] = uint.MinValue;
#else
                    result[i] = uint.MaxValue;
#endif
                else if (r > uint.MaxValue)
                    result[i] = uint.MaxValue;
                else
                {
                    result[i] = (uint)r;
                }
            }
            context.OpStack.PushV128(result);
        }
    }
}