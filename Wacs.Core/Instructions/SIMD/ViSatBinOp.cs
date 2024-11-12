using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I8x16AddSatS = new(SimdCode.I8x16AddSatS, ExecuteI8x16AddSatS, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I8x16AddSatU = new(SimdCode.I8x16AddSatU, ExecuteI8x16AddSatU, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8AddSatS = new(SimdCode.I16x8AddSatS, ExecuteI16x8AddSatS, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8AddSatU = new(SimdCode.I16x8AddSatU, ExecuteI16x8AddSatU, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16SubSatS = new(SimdCode.I8x16SubSatS, ExecuteI8x16SubSatS, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I8x16SubSatU = new(SimdCode.I8x16SubSatU, ExecuteI8x16SubSatU, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8SubSatS = new(SimdCode.I16x8SubSatS, ExecuteI16x8SubSatS, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8SubSatU = new(SimdCode.I16x8SubSatU, ExecuteI16x8SubSatU, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        //q15mulr_sat_s

        private static void ExecuteI8x16AddSatS(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (sbyte)Math.Clamp((int)v1.I8x16_0 + (int)v2.I8x16_0, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_1 + (int)v2.I8x16_1, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_2 + (int)v2.I8x16_2, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_3 + (int)v2.I8x16_3, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_4 + (int)v2.I8x16_4, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_5 + (int)v2.I8x16_5, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_6 + (int)v2.I8x16_6, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_7 + (int)v2.I8x16_7, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_8 + (int)v2.I8x16_8, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_9 + (int)v2.I8x16_9, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_A + (int)v2.I8x16_A, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_B + (int)v2.I8x16_B, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_C + (int)v2.I8x16_C, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_D + (int)v2.I8x16_D, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_E + (int)v2.I8x16_E, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_F + (int)v2.I8x16_F, sbyte.MinValue, sbyte.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16AddSatU(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)Math.Clamp((int)v1.U8x16_0 + (int)v2.U8x16_0, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_1 + (int)v2.U8x16_1, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_2 + (int)v2.U8x16_2, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_3 + (int)v2.U8x16_3, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_4 + (int)v2.U8x16_4, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_5 + (int)v2.U8x16_5, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_6 + (int)v2.U8x16_6, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_7 + (int)v2.U8x16_7, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_8 + (int)v2.U8x16_8, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_9 + (int)v2.U8x16_9, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_A + (int)v2.U8x16_A, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_B + (int)v2.U8x16_B, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_C + (int)v2.U8x16_C, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_D + (int)v2.U8x16_D, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_E + (int)v2.U8x16_E, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_F + (int)v2.U8x16_F, byte.MinValue, byte.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8AddSatS(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)Math.Clamp((int)v1.I16x8_0 + (int)v2.I16x8_0, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_1 + (int)v2.I16x8_1, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_2 + (int)v2.I16x8_2, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_3 + (int)v2.I16x8_3, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_4 + (int)v2.I16x8_4, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_5 + (int)v2.I16x8_5, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_6 + (int)v2.I16x8_6, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_7 + (int)v2.I16x8_7, short.MinValue, short.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8AddSatU(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)Math.Clamp((int)v1.U16x8_0 + (int)v2.U16x8_0, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_1 + (int)v2.U16x8_1, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_2 + (int)v2.U16x8_2, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_3 + (int)v2.U16x8_3, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_4 + (int)v2.U16x8_4, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_5 + (int)v2.U16x8_5, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_6 + (int)v2.U16x8_6, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_7 + (int)v2.U16x8_7, ushort.MinValue, ushort.MaxValue)
            );
            context.OpStack.PushV128(result);
        }


        private static void ExecuteI8x16SubSatS(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (sbyte)Math.Clamp((int)v1.I8x16_0 - (int)v2.I8x16_0, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_1 - (int)v2.I8x16_1, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_2 - (int)v2.I8x16_2, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_3 - (int)v2.I8x16_3, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_4 - (int)v2.I8x16_4, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_5 - (int)v2.I8x16_5, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_6 - (int)v2.I8x16_6, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_7 - (int)v2.I8x16_7, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_8 - (int)v2.I8x16_8, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_9 - (int)v2.I8x16_9, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_A - (int)v2.I8x16_A, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_B - (int)v2.I8x16_B, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_C - (int)v2.I8x16_C, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_D - (int)v2.I8x16_D, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_E - (int)v2.I8x16_E, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.I8x16_F - (int)v2.I8x16_F, sbyte.MinValue, sbyte.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16SubSatU(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)Math.Clamp((int)v1.U8x16_0 - (int)v2.U8x16_0, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_1 - (int)v2.U8x16_1, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_2 - (int)v2.U8x16_2, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_3 - (int)v2.U8x16_3, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_4 - (int)v2.U8x16_4, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_5 - (int)v2.U8x16_5, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_6 - (int)v2.U8x16_6, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_7 - (int)v2.U8x16_7, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_8 - (int)v2.U8x16_8, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_9 - (int)v2.U8x16_9, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_A - (int)v2.U8x16_A, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_B - (int)v2.U8x16_B, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_C - (int)v2.U8x16_C, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_D - (int)v2.U8x16_D, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_E - (int)v2.U8x16_E, byte.MinValue, byte.MaxValue),
                (byte)Math.Clamp((int)v1.U8x16_F - (int)v2.U8x16_F, byte.MinValue, byte.MaxValue)
            );
            context.OpStack.PushV128(result);
        }


        private static void ExecuteI16x8SubSatS(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)Math.Clamp((int)v1.I16x8_0 - (int)v2.I16x8_0, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_1 - (int)v2.I16x8_1, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_2 - (int)v2.I16x8_2, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_3 - (int)v2.I16x8_3, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_4 - (int)v2.I16x8_4, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_5 - (int)v2.I16x8_5, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_6 - (int)v2.I16x8_6, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.I16x8_7 - (int)v2.I16x8_7, short.MinValue, short.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8SubSatU(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)Math.Clamp((int)v1.U16x8_0 - (int)v2.U16x8_0, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_1 - (int)v2.U16x8_1, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_2 - (int)v2.U16x8_2, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_3 - (int)v2.U16x8_3, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_4 - (int)v2.U16x8_4, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_5 - (int)v2.U16x8_5, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_6 - (int)v2.U16x8_6, ushort.MinValue, ushort.MaxValue),
                (ushort)Math.Clamp((int)v1.U16x8_7 - (int)v2.U16x8_7, ushort.MinValue, ushort.MaxValue)
            );
            context.OpStack.PushV128(result);
        }
    }
}