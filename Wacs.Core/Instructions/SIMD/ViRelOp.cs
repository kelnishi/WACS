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
using Wacs.Core.Types;

// ReSharper disable InconsistentNaming
namespace Wacs.Core.Instructions.Numeric
{
    // @Spec 4.4.3.13 txN.vrelop
    public partial class NumericInst
    {
        public static readonly NumericInst I8x16Eq = new(SimdCode.I8x16Eq, ExecuteI8x16Eq,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16Ne = new(SimdCode.I8x16Ne, ExecuteI8x16Ne,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16LtS = new(SimdCode.I8x16LtS, ExecuteI8x16LtS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16LtU = new(SimdCode.I8x16LtU, ExecuteI8x16LtU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16GtS = new(SimdCode.I8x16GtS, ExecuteI8x16GtS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16GtU = new(SimdCode.I8x16GtU, ExecuteI8x16GtU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16LeS = new(SimdCode.I8x16LeS, ExecuteI8x16LeS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16LeU = new(SimdCode.I8x16LeU, ExecuteI8x16LeU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16GeS = new(SimdCode.I8x16GeS, ExecuteI8x16GeS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16GeU = new(SimdCode.I8x16GeU, ExecuteI8x16GeU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8Eq = new(SimdCode.I16x8Eq, ExecuteI16x8Eq,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8Ne = new(SimdCode.I16x8Ne, ExecuteI16x8Ne,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8LtS = new(SimdCode.I16x8LtS, ExecuteI16x8LtS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8LtU = new(SimdCode.I16x8LtU, ExecuteI16x8LtU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8GtS = new(SimdCode.I16x8GtS, ExecuteI16x8GtS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8GtU = new(SimdCode.I16x8GtU, ExecuteI16x8GtU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8LeS = new(SimdCode.I16x8LeS, ExecuteI16x8LeS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8LeU = new(SimdCode.I16x8LeU, ExecuteI16x8LeU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8GeS = new(SimdCode.I16x8GeS, ExecuteI16x8GeS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8GeU = new(SimdCode.I16x8GeU, ExecuteI16x8GeU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4Eq = new(SimdCode.I32x4Eq, ExecuteI32x4Eq,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4Ne = new(SimdCode.I32x4Ne, ExecuteI32x4Ne,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4LtS = new(SimdCode.I32x4LtS, ExecuteI32x4LtS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4LtU = new(SimdCode.I32x4LtU, ExecuteI32x4LtU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4GtS = new(SimdCode.I32x4GtS, ExecuteI32x4GtS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4GtU = new(SimdCode.I32x4GtU, ExecuteI32x4GtU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4LeS = new(SimdCode.I32x4LeS, ExecuteI32x4LeS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4LeU = new(SimdCode.I32x4LeU, ExecuteI32x4LeU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4GeS = new(SimdCode.I32x4GeS, ExecuteI32x4GeS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4GeU = new(SimdCode.I32x4GeU, ExecuteI32x4GeU,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I64x2Eq = new(SimdCode.I64x2Eq, ExecuteI64x2Eq,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I64x2Ne = new(SimdCode.I64x2Ne, ExecuteI64x2Ne,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I64x2LtS = new(SimdCode.I64x2LtS, ExecuteI64x2LtS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I64x2GtS = new(SimdCode.I64x2GtS, ExecuteI64x2GtS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I64x2LeS = new(SimdCode.I64x2LeS, ExecuteI64x2LeS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I64x2GeS = new(SimdCode.I64x2GeS, ExecuteI64x2GeS,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        private static void ExecuteI8x16Eq(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.eq failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.eq failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.U8x16_0 == c2.U8x16_0 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_1 == c2.U8x16_1 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_2 == c2.U8x16_2 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_3 == c2.U8x16_3 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_4 == c2.U8x16_4 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_5 == c2.U8x16_5 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_6 == c2.U8x16_6 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_7 == c2.U8x16_7 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_8 == c2.U8x16_8 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_9 == c2.U8x16_9 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_A == c2.U8x16_A ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_B == c2.U8x16_B ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_C == c2.U8x16_C ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_D == c2.U8x16_D ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_E == c2.U8x16_E ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_F == c2.U8x16_F ? (sbyte)-1 : (sbyte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16Ne(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.ne failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.eq failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.U8x16_0 != c2.U8x16_0 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_1 != c2.U8x16_1 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_2 != c2.U8x16_2 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_3 != c2.U8x16_3 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_4 != c2.U8x16_4 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_5 != c2.U8x16_5 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_6 != c2.U8x16_6 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_7 != c2.U8x16_7 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_8 != c2.U8x16_8 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_9 != c2.U8x16_9 ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_A != c2.U8x16_A ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_B != c2.U8x16_B ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_C != c2.U8x16_C ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_D != c2.U8x16_D ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_E != c2.U8x16_E ? (sbyte)-1 : (sbyte)0,
                c1.U8x16_F != c2.U8x16_F ? (sbyte)-1 : (sbyte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16LtS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.lt_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.lt_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I8x16_0 < c2.I8x16_0 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_1 < c2.I8x16_1 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_2 < c2.I8x16_2 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_3 < c2.I8x16_3 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_4 < c2.I8x16_4 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_5 < c2.I8x16_5 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_6 < c2.I8x16_6 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_7 < c2.I8x16_7 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_8 < c2.I8x16_8 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_9 < c2.I8x16_9 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_A < c2.I8x16_A ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_B < c2.I8x16_B ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_C < c2.I8x16_C ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_D < c2.I8x16_D ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_E < c2.I8x16_E ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_F < c2.I8x16_F ? (sbyte)-1 : (sbyte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16LtU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.lt_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.lt_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.U8x16_0 < c2.U8x16_0) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_1 < c2.U8x16_1) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_2 < c2.U8x16_2) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_3 < c2.U8x16_3) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_4 < c2.U8x16_4) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_5 < c2.U8x16_5) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_6 < c2.U8x16_6) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_7 < c2.U8x16_7) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_8 < c2.U8x16_8) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_9 < c2.U8x16_9) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_A < c2.U8x16_A) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_B < c2.U8x16_B) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_C < c2.U8x16_C) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_D < c2.U8x16_D) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_E < c2.U8x16_E) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_F < c2.U8x16_F) ? (sbyte)-1 : (sbyte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16GtS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.gt_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.gt_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I8x16_0 > c2.I8x16_0 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_1 > c2.I8x16_1 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_2 > c2.I8x16_2 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_3 > c2.I8x16_3 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_4 > c2.I8x16_4 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_5 > c2.I8x16_5 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_6 > c2.I8x16_6 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_7 > c2.I8x16_7 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_8 > c2.I8x16_8 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_9 > c2.I8x16_9 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_A > c2.I8x16_A ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_B > c2.I8x16_B ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_C > c2.I8x16_C ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_D > c2.I8x16_D ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_E > c2.I8x16_E ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_F > c2.I8x16_F ? (sbyte)-1 : (sbyte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16GtU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.gt_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.gt_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.U8x16_0 > c2.U8x16_0) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_1 > c2.U8x16_1) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_2 > c2.U8x16_2) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_3 > c2.U8x16_3) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_4 > c2.U8x16_4) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_5 > c2.U8x16_5) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_6 > c2.U8x16_6) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_7 > c2.U8x16_7) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_8 > c2.U8x16_8) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_9 > c2.U8x16_9) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_A > c2.U8x16_A) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_B > c2.U8x16_B) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_C > c2.U8x16_C) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_D > c2.U8x16_D) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_E > c2.U8x16_E) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_F > c2.U8x16_F) ? (sbyte)-1 : (sbyte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16LeS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.le_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.le_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I8x16_0 <= c2.I8x16_0 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_1 <= c2.I8x16_1 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_2 <= c2.I8x16_2 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_3 <= c2.I8x16_3 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_4 <= c2.I8x16_4 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_5 <= c2.I8x16_5 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_6 <= c2.I8x16_6 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_7 <= c2.I8x16_7 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_8 <= c2.I8x16_8 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_9 <= c2.I8x16_9 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_A <= c2.I8x16_A ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_B <= c2.I8x16_B ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_C <= c2.I8x16_C ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_D <= c2.I8x16_D ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_E <= c2.I8x16_E ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_F <= c2.I8x16_F ? (sbyte)-1 : (sbyte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16LeU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.le_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.le_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.U8x16_0 <= c2.U8x16_0) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_1 <= c2.U8x16_1) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_2 <= c2.U8x16_2) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_3 <= c2.U8x16_3) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_4 <= c2.U8x16_4) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_5 <= c2.U8x16_5) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_6 <= c2.U8x16_6) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_7 <= c2.U8x16_7) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_8 <= c2.U8x16_8) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_9 <= c2.U8x16_9) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_A <= c2.U8x16_A) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_B <= c2.U8x16_B) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_C <= c2.U8x16_C) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_D <= c2.U8x16_D) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_E <= c2.U8x16_E) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_F <= c2.U8x16_F) ? (sbyte)-1 : (sbyte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16GeS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.ge_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.ge_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I8x16_0 >= c2.I8x16_0 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_1 >= c2.I8x16_1 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_2 >= c2.I8x16_2 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_3 >= c2.I8x16_3 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_4 >= c2.I8x16_4 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_5 >= c2.I8x16_5 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_6 >= c2.I8x16_6 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_7 >= c2.I8x16_7 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_8 >= c2.I8x16_8 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_9 >= c2.I8x16_9 ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_A >= c2.I8x16_A ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_B >= c2.I8x16_B ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_C >= c2.I8x16_C ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_D >= c2.I8x16_D ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_E >= c2.I8x16_E ? (sbyte)-1 : (sbyte)0,
                c1.I8x16_F >= c2.I8x16_F ? (sbyte)-1 : (sbyte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16GeU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.ge_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.ge_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.U8x16_0 >= c2.U8x16_0) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_1 >= c2.U8x16_1) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_2 >= c2.U8x16_2) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_3 >= c2.U8x16_3) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_4 >= c2.U8x16_4) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_5 >= c2.U8x16_5) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_6 >= c2.U8x16_6) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_7 >= c2.U8x16_7) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_8 >= c2.U8x16_8) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_9 >= c2.U8x16_9) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_A >= c2.U8x16_A) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_B >= c2.U8x16_B) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_C >= c2.U8x16_C) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_D >= c2.U8x16_D) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_E >= c2.U8x16_E) ? (sbyte)-1 : (sbyte)0,
                (c1.U8x16_F >= c2.U8x16_F) ? (sbyte)-1 : (sbyte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8Eq(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.eq failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.eq failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I16x8_0 == c2.I16x8_0 ? (short)-1 : (short)0,
                c1.I16x8_1 == c2.I16x8_1 ? (short)-1 : (short)0,
                c1.I16x8_2 == c2.I16x8_2 ? (short)-1 : (short)0,
                c1.I16x8_3 == c2.I16x8_3 ? (short)-1 : (short)0,
                c1.I16x8_4 == c2.I16x8_4 ? (short)-1 : (short)0,
                c1.I16x8_5 == c2.I16x8_5 ? (short)-1 : (short)0,
                c1.I16x8_6 == c2.I16x8_6 ? (short)-1 : (short)0,
                c1.I16x8_7 == c2.I16x8_7 ? (short)-1 : (short)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8Ne(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.ne failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.ne failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I16x8_0 != c2.I16x8_0 ? (short)-1 : (short)0,
                c1.I16x8_1 != c2.I16x8_1 ? (short)-1 : (short)0,
                c1.I16x8_2 != c2.I16x8_2 ? (short)-1 : (short)0,
                c1.I16x8_3 != c2.I16x8_3 ? (short)-1 : (short)0,
                c1.I16x8_4 != c2.I16x8_4 ? (short)-1 : (short)0,
                c1.I16x8_5 != c2.I16x8_5 ? (short)-1 : (short)0,
                c1.I16x8_6 != c2.I16x8_6 ? (short)-1 : (short)0,
                c1.I16x8_7 != c2.I16x8_7 ? (short)-1 : (short)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8LtS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.lt_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.lt_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I16x8_0 < c2.I16x8_0 ? (short)-1 : (short)0,
                c1.I16x8_1 < c2.I16x8_1 ? (short)-1 : (short)0,
                c1.I16x8_2 < c2.I16x8_2 ? (short)-1 : (short)0,
                c1.I16x8_3 < c2.I16x8_3 ? (short)-1 : (short)0,
                c1.I16x8_4 < c2.I16x8_4 ? (short)-1 : (short)0,
                c1.I16x8_5 < c2.I16x8_5 ? (short)-1 : (short)0,
                c1.I16x8_6 < c2.I16x8_6 ? (short)-1 : (short)0,
                c1.I16x8_7 < c2.I16x8_7 ? (short)-1 : (short)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8LtU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.lt_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.lt_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.U16x8_0 < c2.U16x8_0) ? (short)-1 : (short)0,
                (c1.U16x8_1 < c2.U16x8_1) ? (short)-1 : (short)0,
                (c1.U16x8_2 < c2.U16x8_2) ? (short)-1 : (short)0,
                (c1.U16x8_3 < c2.U16x8_3) ? (short)-1 : (short)0,
                (c1.U16x8_4 < c2.U16x8_4) ? (short)-1 : (short)0,
                (c1.U16x8_5 < c2.U16x8_5) ? (short)-1 : (short)0,
                (c1.U16x8_6 < c2.U16x8_6) ? (short)-1 : (short)0,
                (c1.U16x8_7 < c2.U16x8_7) ? (short)-1 : (short)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8GtS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.gt_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.gt_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I16x8_0 > c2.I16x8_0 ? (short)-1 : (short)0,
                c1.I16x8_1 > c2.I16x8_1 ? (short)-1 : (short)0,
                c1.I16x8_2 > c2.I16x8_2 ? (short)-1 : (short)0,
                c1.I16x8_3 > c2.I16x8_3 ? (short)-1 : (short)0,
                c1.I16x8_4 > c2.I16x8_4 ? (short)-1 : (short)0,
                c1.I16x8_5 > c2.I16x8_5 ? (short)-1 : (short)0,
                c1.I16x8_6 > c2.I16x8_6 ? (short)-1 : (short)0,
                c1.I16x8_7 > c2.I16x8_7 ? (short)-1 : (short)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8GtU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.gt_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.gt_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.U16x8_0 > c2.U16x8_0) ? (short)-1 : (short)0,
                (c1.U16x8_1 > c2.U16x8_1) ? (short)-1 : (short)0,
                (c1.U16x8_2 > c2.U16x8_2) ? (short)-1 : (short)0,
                (c1.U16x8_3 > c2.U16x8_3) ? (short)-1 : (short)0,
                (c1.U16x8_4 > c2.U16x8_4) ? (short)-1 : (short)0,
                (c1.U16x8_5 > c2.U16x8_5) ? (short)-1 : (short)0,
                (c1.U16x8_6 > c2.U16x8_6) ? (short)-1 : (short)0,
                (c1.U16x8_7 > c2.U16x8_7) ? (short)-1 : (short)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8LeS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.le_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.le_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I16x8_0 <= c2.I16x8_0 ? (short)-1 : (short)0,
                c1.I16x8_1 <= c2.I16x8_1 ? (short)-1 : (short)0,
                c1.I16x8_2 <= c2.I16x8_2 ? (short)-1 : (short)0,
                c1.I16x8_3 <= c2.I16x8_3 ? (short)-1 : (short)0,
                c1.I16x8_4 <= c2.I16x8_4 ? (short)-1 : (short)0,
                c1.I16x8_5 <= c2.I16x8_5 ? (short)-1 : (short)0,
                c1.I16x8_6 <= c2.I16x8_6 ? (short)-1 : (short)0,
                c1.I16x8_7 <= c2.I16x8_7 ? (short)-1 : (short)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8LeU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.le_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.le_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.U16x8_0 <= c2.U16x8_0) ? (short)-1 : (short)0,
                (c1.U16x8_1 <= c2.U16x8_1) ? (short)-1 : (short)0,
                (c1.U16x8_2 <= c2.U16x8_2) ? (short)-1 : (short)0,
                (c1.U16x8_3 <= c2.U16x8_3) ? (short)-1 : (short)0,
                (c1.U16x8_4 <= c2.U16x8_4) ? (short)-1 : (short)0,
                (c1.U16x8_5 <= c2.U16x8_5) ? (short)-1 : (short)0,
                (c1.U16x8_6 <= c2.U16x8_6) ? (short)-1 : (short)0,
                (c1.U16x8_7 <= c2.U16x8_7) ? (short)-1 : (short)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8GeS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.ge_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.ge_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I16x8_0 >= c2.I16x8_0 ? (short)-1 : (short)0,
                c1.I16x8_1 >= c2.I16x8_1 ? (short)-1 : (short)0,
                c1.I16x8_2 >= c2.I16x8_2 ? (short)-1 : (short)0,
                c1.I16x8_3 >= c2.I16x8_3 ? (short)-1 : (short)0,
                c1.I16x8_4 >= c2.I16x8_4 ? (short)-1 : (short)0,
                c1.I16x8_5 >= c2.I16x8_5 ? (short)-1 : (short)0,
                c1.I16x8_6 >= c2.I16x8_6 ? (short)-1 : (short)0,
                c1.I16x8_7 >= c2.I16x8_7 ? (short)-1 : (short)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI16x8GeU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.ge_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i16x8.ge_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.U16x8_0 >= c2.U16x8_0) ? (short)-1 : (short)0,
                (c1.U16x8_1 >= c2.U16x8_1) ? (short)-1 : (short)0,
                (c1.U16x8_2 >= c2.U16x8_2) ? (short)-1 : (short)0,
                (c1.U16x8_3 >= c2.U16x8_3) ? (short)-1 : (short)0,
                (c1.U16x8_4 >= c2.U16x8_4) ? (short)-1 : (short)0,
                (c1.U16x8_5 >= c2.U16x8_5) ? (short)-1 : (short)0,
                (c1.U16x8_6 >= c2.U16x8_6) ? (short)-1 : (short)0,
                (c1.U16x8_7 >= c2.U16x8_7) ? (short)-1 : (short)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4Eq(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.eq failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.eq failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I32x4_0 == c2.I32x4_0 ? (int)-1 : (int)0,
                c1.I32x4_1 == c2.I32x4_1 ? (int)-1 : (int)0,
                c1.I32x4_2 == c2.I32x4_2 ? (int)-1 : (int)0,
                c1.I32x4_3 == c2.I32x4_3 ? (int)-1 : (int)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4Ne(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.ne failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.ne failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I32x4_0 != c2.I32x4_0 ? (int)-1 : (int)0,
                c1.I32x4_1 != c2.I32x4_1 ? (int)-1 : (int)0,
                c1.I32x4_2 != c2.I32x4_2 ? (int)-1 : (int)0,
                c1.I32x4_3 != c2.I32x4_3 ? (int)-1 : (int)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4LtS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.lt_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.lt_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I32x4_0 < c2.I32x4_0 ? (int)-1 : (int)0,
                c1.I32x4_1 < c2.I32x4_1 ? (int)-1 : (int)0,
                c1.I32x4_2 < c2.I32x4_2 ? (int)-1 : (int)0,
                c1.I32x4_3 < c2.I32x4_3 ? (int)-1 : (int)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4LtU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.lt_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.lt_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.U32x4_0 < c2.U32x4_0) ? (int)-1 : (int)0,
                (c1.U32x4_1 < c2.U32x4_1) ? (int)-1 : (int)0,
                (c1.U32x4_2 < c2.U32x4_2) ? (int)-1 : (int)0,
                (c1.U32x4_3 < c2.U32x4_3) ? (int)-1 : (int)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4GtS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.gt_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.gt_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I32x4_0 > c2.I32x4_0 ? (int)-1 : (int)0,
                c1.I32x4_1 > c2.I32x4_1 ? (int)-1 : (int)0,
                c1.I32x4_2 > c2.I32x4_2 ? (int)-1 : (int)0,
                c1.I32x4_3 > c2.I32x4_3 ? (int)-1 : (int)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4GtU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.gt_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.gt_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.U32x4_0 > c2.U32x4_0) ? (int)-1 : (int)0,
                (c1.U32x4_1 > c2.U32x4_1) ? (int)-1 : (int)0,
                (c1.U32x4_2 > c2.U32x4_2) ? (int)-1 : (int)0,
                (c1.U32x4_3 > c2.U32x4_3) ? (int)-1 : (int)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4LeS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.le_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.le_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I32x4_0 <= c2.I32x4_0 ? (int)-1 : (int)0,
                c1.I32x4_1 <= c2.I32x4_1 ? (int)-1 : (int)0,
                c1.I32x4_2 <= c2.I32x4_2 ? (int)-1 : (int)0,
                c1.I32x4_3 <= c2.I32x4_3 ? (int)-1 : (int)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4LeU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.le_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.le_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.U32x4_0 <= c2.U32x4_0) ? (int)-1 : (int)0,
                (c1.U32x4_1 <= c2.U32x4_1) ? (int)-1 : (int)0,
                (c1.U32x4_2 <= c2.U32x4_2) ? (int)-1 : (int)0,
                (c1.U32x4_3 <= c2.U32x4_3) ? (int)-1 : (int)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4GeS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.ge_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.ge_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I32x4_0 >= c2.I32x4_0 ? (int)-1 : (int)0,
                c1.I32x4_1 >= c2.I32x4_1 ? (int)-1 : (int)0,
                c1.I32x4_2 >= c2.I32x4_2 ? (int)-1 : (int)0,
                c1.I32x4_3 >= c2.I32x4_3 ? (int)-1 : (int)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI32x4GeU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.ge_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i32x4.ge_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.U32x4_0 >= c2.U32x4_0) ? (int)-1 : (int)0,
                (c1.U32x4_1 >= c2.U32x4_1) ? (int)-1 : (int)0,
                (c1.U32x4_2 >= c2.U32x4_2) ? (int)-1 : (int)0,
                (c1.U32x4_3 >= c2.U32x4_3) ? (int)-1 : (int)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI64x2Eq(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i64x2.eq failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i64x2.eq failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I64x2_0 == c2.I64x2_0 ? (long)-1 : (long)0,
                c1.I64x2_1 == c2.I64x2_1 ? (long)-1 : (long)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI64x2Ne(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i64x2.ne failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i64x2.ne failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I64x2_0 != c2.I64x2_0 ? (long)-1 : (long)0,
                c1.I64x2_1 != c2.I64x2_1 ? (long)-1 : (long)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI64x2LtS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i64x2.lt_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i64x2.lt_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I64x2_0 < c2.I64x2_0 ? (long)-1 : (long)0,
                c1.I64x2_1 < c2.I64x2_1 ? (long)-1 : (long)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI64x2GtS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i64x2.gt_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i64x2.gt_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I64x2_0 > c2.I64x2_0 ? (long)-1 : (long)0,
                c1.I64x2_1 > c2.I64x2_1 ? (long)-1 : (long)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI64x2LeS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i64x2.le_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i64x2.le_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I64x2_0 <= c2.I64x2_0 ? (long)-1 : (long)0,
                c1.I64x2_1 <= c2.I64x2_1 ? (long)-1 : (long)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI64x2GeS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i64x2.ge_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i64x2.ge_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I64x2_0 >= c2.I64x2_0 ? (long)-1 : (long)0,
                c1.I64x2_1 >= c2.I64x2_1 ? (long)-1 : (long)0);
            context.OpStack.PushV128(c);
        }
    }
}