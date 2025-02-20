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

// ReSharper disable InconsistentNaming
namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I8x16MinS = new(SimdCode.I8x16MinS, ExecuteI8x16MinS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        public static readonly NumericInst I8x16MinU = new(SimdCode.I8x16MinU, ExecuteI8x16MinU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        public static readonly NumericInst I8x16MaxS = new(SimdCode.I8x16MaxS, ExecuteI8x16MaxS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        public static readonly NumericInst I8x16MaxU = new(SimdCode.I8x16MaxU, ExecuteI8x16MaxU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        public static readonly NumericInst I16x8MinS = new(SimdCode.I16x8MinS, ExecuteI16x8MinS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        public static readonly NumericInst I16x8MinU = new(SimdCode.I16x8MinU, ExecuteI16x8MinU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        public static readonly NumericInst I16x8MaxS = new(SimdCode.I16x8MaxS, ExecuteI16x8MaxS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        public static readonly NumericInst I16x8MaxU = new(SimdCode.I16x8MaxU, ExecuteI16x8MaxU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        public static readonly NumericInst I32x4MinS = new(SimdCode.I32x4MinS, ExecuteI32x4MinS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        public static readonly NumericInst I32x4MinU = new(SimdCode.I32x4MinU, ExecuteI32x4MinU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        public static readonly NumericInst I32x4MaxS = new(SimdCode.I32x4MaxS, ExecuteI32x4MaxS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        public static readonly NumericInst I32x4MaxU = new(SimdCode.I32x4MaxU, ExecuteI32x4MaxU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        private static void ExecuteI8x16MinS(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                Math.Min(c1.I8x16_0, c2.I8x16_0),
                Math.Min(c1.I8x16_1, c2.I8x16_1),
                Math.Min(c1.I8x16_2, c2.I8x16_2),
                Math.Min(c1.I8x16_3, c2.I8x16_3),
                Math.Min(c1.I8x16_4, c2.I8x16_4),
                Math.Min(c1.I8x16_5, c2.I8x16_5),
                Math.Min(c1.I8x16_6, c2.I8x16_6),
                Math.Min(c1.I8x16_7, c2.I8x16_7),
                Math.Min(c1.I8x16_8, c2.I8x16_8),
                Math.Min(c1.I8x16_9, c2.I8x16_9),
                Math.Min(c1.I8x16_A, c2.I8x16_A),
                Math.Min(c1.I8x16_B, c2.I8x16_B),
                Math.Min(c1.I8x16_C, c2.I8x16_C),
                Math.Min(c1.I8x16_D, c2.I8x16_D),
                Math.Min(c1.I8x16_E, c2.I8x16_E),
                Math.Min(c1.I8x16_F, c2.I8x16_F));
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16MaxS(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                Math.Max(c1.I8x16_0, c2.I8x16_0),
                Math.Max(c1.I8x16_1, c2.I8x16_1),
                Math.Max(c1.I8x16_2, c2.I8x16_2),
                Math.Max(c1.I8x16_3, c2.I8x16_3),
                Math.Max(c1.I8x16_4, c2.I8x16_4),
                Math.Max(c1.I8x16_5, c2.I8x16_5),
                Math.Max(c1.I8x16_6, c2.I8x16_6),
                Math.Max(c1.I8x16_7, c2.I8x16_7),
                Math.Max(c1.I8x16_8, c2.I8x16_8),
                Math.Max(c1.I8x16_9, c2.I8x16_9),
                Math.Max(c1.I8x16_A, c2.I8x16_A),
                Math.Max(c1.I8x16_B, c2.I8x16_B),
                Math.Max(c1.I8x16_C, c2.I8x16_C),
                Math.Max(c1.I8x16_D, c2.I8x16_D),
                Math.Max(c1.I8x16_E, c2.I8x16_E),
                Math.Max(c1.I8x16_F, c2.I8x16_F));
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16MinU(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                Math.Min(c1.U8x16_0, c2.U8x16_0),
                Math.Min(c1.U8x16_1, c2.U8x16_1),
                Math.Min(c1.U8x16_2, c2.U8x16_2),
                Math.Min(c1.U8x16_3, c2.U8x16_3),
                Math.Min(c1.U8x16_4, c2.U8x16_4),
                Math.Min(c1.U8x16_5, c2.U8x16_5),
                Math.Min(c1.U8x16_6, c2.U8x16_6),
                Math.Min(c1.U8x16_7, c2.U8x16_7),
                Math.Min(c1.U8x16_8, c2.U8x16_8),
                Math.Min(c1.U8x16_9, c2.U8x16_9),
                Math.Min(c1.U8x16_A, c2.U8x16_A),
                Math.Min(c1.U8x16_B, c2.U8x16_B),
                Math.Min(c1.U8x16_C, c2.U8x16_C),
                Math.Min(c1.U8x16_D, c2.U8x16_D),
                Math.Min(c1.U8x16_E, c2.U8x16_E),
                Math.Min(c1.U8x16_F, c2.U8x16_F));
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16MaxU(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                Math.Max(c1.U8x16_0, c2.U8x16_0),
                Math.Max(c1.U8x16_1, c2.U8x16_1),
                Math.Max(c1.U8x16_2, c2.U8x16_2),
                Math.Max(c1.U8x16_3, c2.U8x16_3),
                Math.Max(c1.U8x16_4, c2.U8x16_4),
                Math.Max(c1.U8x16_5, c2.U8x16_5),
                Math.Max(c1.U8x16_6, c2.U8x16_6),
                Math.Max(c1.U8x16_7, c2.U8x16_7),
                Math.Max(c1.U8x16_8, c2.U8x16_8),
                Math.Max(c1.U8x16_9, c2.U8x16_9),
                Math.Max(c1.U8x16_A, c2.U8x16_A),
                Math.Max(c1.U8x16_B, c2.U8x16_B),
                Math.Max(c1.U8x16_C, c2.U8x16_C),
                Math.Max(c1.U8x16_D, c2.U8x16_D),
                Math.Max(c1.U8x16_E, c2.U8x16_E),
                Math.Max(c1.U8x16_F, c2.U8x16_F));
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8MinS(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                Math.Min(c1.I16x8_0, c2.I16x8_0),
                Math.Min(c1.I16x8_1, c2.I16x8_1),
                Math.Min(c1.I16x8_2, c2.I16x8_2),
                Math.Min(c1.I16x8_3, c2.I16x8_3),
                Math.Min(c1.I16x8_4, c2.I16x8_4),
                Math.Min(c1.I16x8_5, c2.I16x8_5),
                Math.Min(c1.I16x8_6, c2.I16x8_6),
                Math.Min(c1.I16x8_7, c2.I16x8_7));
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8MaxS(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                Math.Max(c1.I16x8_0, c2.I16x8_0),
                Math.Max(c1.I16x8_1, c2.I16x8_1),
                Math.Max(c1.I16x8_2, c2.I16x8_2),
                Math.Max(c1.I16x8_3, c2.I16x8_3),
                Math.Max(c1.I16x8_4, c2.I16x8_4),
                Math.Max(c1.I16x8_5, c2.I16x8_5),
                Math.Max(c1.I16x8_6, c2.I16x8_6),
                Math.Max(c1.I16x8_7, c2.I16x8_7));
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8MinU(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                Math.Min(c1.U16x8_0, c2.U16x8_0),
                Math.Min(c1.U16x8_1, c2.U16x8_1),
                Math.Min(c1.U16x8_2, c2.U16x8_2),
                Math.Min(c1.U16x8_3, c2.U16x8_3),
                Math.Min(c1.U16x8_4, c2.U16x8_4),
                Math.Min(c1.U16x8_5, c2.U16x8_5),
                Math.Min(c1.U16x8_6, c2.U16x8_6),
                Math.Min(c1.U16x8_7, c2.U16x8_7));
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8MaxU(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                Math.Max(c1.U16x8_0, c2.U16x8_0),
                Math.Max(c1.U16x8_1, c2.U16x8_1),
                Math.Max(c1.U16x8_2, c2.U16x8_2),
                Math.Max(c1.U16x8_3, c2.U16x8_3),
                Math.Max(c1.U16x8_4, c2.U16x8_4),
                Math.Max(c1.U16x8_5, c2.U16x8_5),
                Math.Max(c1.U16x8_6, c2.U16x8_6),
                Math.Max(c1.U16x8_7, c2.U16x8_7));
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4MinS(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                Math.Min(c1.I32x4_0, c2.I32x4_0),
                Math.Min(c1.I32x4_1, c2.I32x4_1),
                Math.Min(c1.I32x4_2, c2.I32x4_2),
                Math.Min(c1.I32x4_3, c2.I32x4_3));
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4MaxS(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                Math.Max(c1.I32x4_0, c2.I32x4_0),
                Math.Max(c1.I32x4_1, c2.I32x4_1),
                Math.Max(c1.I32x4_2, c2.I32x4_2),
                Math.Max(c1.I32x4_3, c2.I32x4_3));
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4MinU(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                Math.Min(c1.U32x4_0, c2.U32x4_0),
                Math.Min(c1.U32x4_1, c2.U32x4_1),
                Math.Min(c1.U32x4_2, c2.U32x4_2),
                Math.Min(c1.U32x4_3, c2.U32x4_3));
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4MaxU(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                Math.Max(c1.U32x4_0, c2.U32x4_0),
                Math.Max(c1.U32x4_1, c2.U32x4_1),
                Math.Max(c1.U32x4_2, c2.U32x4_2),
                Math.Max(c1.U32x4_3, c2.U32x4_3));
            context.OpStack.PushV128(c);
        }
    }
}