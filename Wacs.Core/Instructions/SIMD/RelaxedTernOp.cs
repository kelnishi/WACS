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

using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I8x16RelaxedLaneselect = new(SimdCode.I8x16RelaxedLaneselect, ExecuteI8x16RelaxedLaneselect, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, pop3: ValType.V128, push: ValType.V128), -2);
        public static readonly NumericInst I16x8RelaxedLaneselect = new(SimdCode.I16x8RelaxedLaneselect, ExecuteI16x8RelaxedLaneselect, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, pop3: ValType.V128, push: ValType.V128), -2);
        public static readonly NumericInst I32x4RelaxedLaneselect = new(SimdCode.I32x4RelaxedLaneselect, ExecuteI32x4RelaxedLaneselect, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, pop3: ValType.V128, push: ValType.V128), -2);
        public static readonly NumericInst I64x2RelaxedLaneselect = new(SimdCode.I64x2RelaxedLaneselect, ExecuteI64x2RelaxedLaneselect, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, pop3: ValType.V128, push: ValType.V128), -2);

        public static readonly NumericInst F32x4RelaxedMAdd  = new(SimdCode.F32x4RelaxedMAdd , ExecuteF32x4RelaxedMAdd , ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, pop3: ValType.V128, push: ValType.V128), -2);
        public static readonly NumericInst F32x4RelaxedNMAdd = new(SimdCode.F32x4RelaxedNMAdd, ExecuteF32x4RelaxedNMAdd, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, pop3: ValType.V128, push: ValType.V128), -2);
        public static readonly NumericInst F64x2RelaxedMAdd  = new(SimdCode.F64x2RelaxedMAdd , ExecuteF64x2RelaxedMAdd , ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, pop3: ValType.V128, push: ValType.V128), -2);
        public static readonly NumericInst F64x2RelaxedNMAdd = new(SimdCode.F64x2RelaxedNMAdd, ExecuteF64x2RelaxedNMAdd, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, pop3: ValType.V128, push: ValType.V128), -2);

        public static readonly NumericInst I32x4RelaxedDotI8x16I7x16AddS = new (SimdCode.I32x4RelaxedDotI8x16I7x16AddS, ExecuteI32x4RelaxedDotI8x16I7x16AddS, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, pop3: ValType.V128, push: ValType.V128), -2);

        private static byte BitSelect(byte i1, byte i2, byte i3)
        {
            int j1 = i1 & i3;
            int j3 = ~i3;
            int j2 = i2 & j3;
            int result = j1 | j2;
            return (byte)(result & 0xFF);
        }

        private static ushort BitSelect(ushort i1, ushort i2, ushort i3)
        {
            int j1 = i1 & i3;
            int j3 = ~i3;
            int j2 = i2 & j3;
            int result = j1 | j2;
            return (ushort)(result & 0xFFFF);
        }

        private static uint BitSelect(uint i1, uint i2, uint i3)
        {
            uint j1 = i1 & i3;
            uint j3 = ~i3;
            uint j2 = i2 & j3;
            uint result = j1 | j2;
            return result;
        }

        private static ulong BitSelect(ulong i1, ulong i2, ulong i3)
        {
            ulong j1 = i1 & i3;
            ulong j3 = ~i3;
            ulong j2 = i2 & j3;
            ulong result = j1 | j2;
            return result;
        }

        private static void ExecuteI8x16RelaxedLaneselect(ExecContext context)
        {
            V128 m = context.OpStack.PopV128();
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (byte i = 0; i < 8; ++i)
            {
                byte mask = m[i];
                if (mask == 0xFF)
                    result[i] = a[i];
                else if (mask == 0)
                    result[i] = b[i];
                else if ((mask & 0x80) != 0)
#if RELAXED_SIMD_ALT
                    result[i] = BitSelect(a[i],b[i], mask);
#else
                    result[i] = a[i];
#endif
                else
#if RELAXED_SIMD_ALT
                    result[i] = BitSelect(a[i],b[i], mask);
#else
                    result[i] = b[i];
#endif
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8RelaxedLaneselect(ExecContext context)
        {
            V128 m = context.OpStack.PopV128();
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (ushort i = 0; i < 8; ++i)
            {
                ushort mask = m[i];
                if (mask == 0xFFFF)
                    result[i] = a[i];
                else if (mask == 0)
                    result[i] = b[i];
                else if ((mask & 0x8000) != 0)
#if RELAXED_SIMD_ALT
                    result[i] = BitSelect(a[i],b[i], mask);
#else
                    result[i] = a[i];
#endif
                else
#if RELAXED_SIMD_ALT
                    result[i] = BitSelect(a[i],b[i], mask);
#else
                    result[i] = b[i];
#endif
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4RelaxedLaneselect(ExecContext context)
        {
            V128 m = context.OpStack.PopV128();
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (uint i = 0; i < 4; ++i)
            {
                uint mask = m[i];
                if (mask == 0xFFFF_FFFF)
                    result[i] = a[i];
                else if (mask == 0)
                    result[i] = b[i];
                else if ((mask & 0x8000_0000) != 0)
#if RELAXED_SIMD_ALT
                    result[i] = BitSelect(a[i],b[i], mask);
#else
                    result[i] = a[i];
#endif
                else
#if RELAXED_SIMD_ALT
                    result[i] = BitSelect(a[i],b[i], mask);
#else
                    result[i] = b[i];
#endif
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2RelaxedLaneselect(ExecContext context)
        {
            V128 m = context.OpStack.PopV128();
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (ulong i = 0; i < 2; ++i)
            {
                ulong mask = m[i];
                if (mask == 0xFFFF_FFFF_FFFF_FFFF)
                    result[i] = a[i];
                else if (mask == 0)
                    result[i] = b[i];
                else if ((mask & 0x8000_0000_0000_0000) != 0)
#if RELAXED_SIMD_ALT
                    result[i] = BitSelect(a[i],b[i], mask);
#else
                    result[i] = a[i];
#endif
                else
#if RELAXED_SIMD_ALT
                    result[i] = BitSelect(a[i],b[i], mask);
#else
                    result[i] = b[i];
#endif
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4RelaxedMAdd(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (float i = 0.0f; i < 4.0f; i += 1.0f)
            {
#if RELAXED_SIMD_ALT
                result[i] = (float)(((double)a[i] * (double)b[i]) + (double)c[i]);
#else
                result[i] = a[i] * b[i] + c[i];
#endif
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4RelaxedNMAdd(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (float i = 0.0f; i < 4.0f; i += 1.0f)
            {
#if RELAXED_SIMD_ALT
                result[i] = (float)(-((double)a[i] * (double)b[i]) + (double)c[i]);
#else
                result[i] = -(a[i] * b[i]) + c[i];
#endif
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2RelaxedMAdd(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (double i = 0.0; i < 2.0; i += 1.0)
            {
                result[i] = a[i] * b[i] + c[i];
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2RelaxedNMAdd(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new MV128();

            for (double i = 0.0; i < 2.0; i += 1.0)
            {
                result[i] = -(a[i] * b[i]) + c[i];
            }

            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4RelaxedDotI8x16I7x16AddS(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 intermediate = new MV128();
            MV128 tmp = new MV128();
            MV128 result = new MV128();
            
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
                tmp[i] = (short)(byte)(intermediate[(sbyte)(i * 2)] + intermediate[(sbyte)(i * 2 + 1)]);
#else
                tmp[i] = (short)(intermediate[(sbyte)(i * 2)] + intermediate[(sbyte)(i * 2 + 1)]);
#endif
            }

            for (int i = 0; i < 4; i += 1)
            {
                result[i] = tmp[(short)(i * 2)] + tmp[(short)(i * 2 + 1)] + c[i];
            }
            
            context.OpStack.PushV128(result);
        }
    }
}