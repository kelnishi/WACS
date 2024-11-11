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
                (sbyte)Math.Clamp((int)v1.U8x16_0 + (int)v2.U8x16_0, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_1 + (int)v2.U8x16_1, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_2 + (int)v2.U8x16_2, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_3 + (int)v2.U8x16_3, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_4 + (int)v2.U8x16_4, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_5 + (int)v2.U8x16_5, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_6 + (int)v2.U8x16_6, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_7 + (int)v2.U8x16_7, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_8 + (int)v2.U8x16_8, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_9 + (int)v2.U8x16_9, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_A + (int)v2.U8x16_A, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_B + (int)v2.U8x16_B, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_C + (int)v2.U8x16_C, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_D + (int)v2.U8x16_D, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_E + (int)v2.U8x16_E, sbyte.MinValue, sbyte.MaxValue),
                (sbyte)Math.Clamp((int)v1.U8x16_F + (int)v2.U8x16_F, sbyte.MinValue, sbyte.MaxValue)
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
                (short)Math.Clamp((int)v1.U16x8_0 + (int)v2.U16x8_0, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.U16x8_1 + (int)v2.U16x8_1, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.U16x8_2 + (int)v2.U16x8_2, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.U16x8_3 + (int)v2.U16x8_3, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.U16x8_4 + (int)v2.U16x8_4, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.U16x8_5 + (int)v2.U16x8_5, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.U16x8_6 + (int)v2.U16x8_6, short.MinValue, short.MaxValue),
                (short)Math.Clamp((int)v1.U16x8_7 + (int)v2.U16x8_7, short.MinValue, short.MaxValue)
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
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_0 - v2.U8x16_0)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_1 - v2.U8x16_1)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_2 - v2.U8x16_2)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_3 - v2.U8x16_3)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_4 - v2.U8x16_4)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_5 - v2.U8x16_5)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_6 - v2.U8x16_6)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_7 - v2.U8x16_7)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_8 - v2.U8x16_8)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_9 - v2.U8x16_9)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_A - v2.U8x16_A)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_B - v2.U8x16_B)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_C - v2.U8x16_C)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_D - v2.U8x16_D)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_E - v2.U8x16_E)),
                (sbyte)Math.Min(sbyte.MaxValue, Math.Max(sbyte.MinValue, v1.U8x16_F - v2.U8x16_F))
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16SubSatU(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)Math.Max(0, v1.U8x16_0 - v2.U8x16_0),
                (byte)Math.Max(0, v1.U8x16_1 - v2.U8x16_1),
                (byte)Math.Max(0, v1.U8x16_2 - v2.U8x16_2),
                (byte)Math.Max(0, v1.U8x16_3 - v2.U8x16_3),
                (byte)Math.Max(0, v1.U8x16_4 - v2.U8x16_4),
                (byte)Math.Max(0, v1.U8x16_5 - v2.U8x16_5),
                (byte)Math.Max(0, v1.U8x16_6 - v2.U8x16_6),
                (byte)Math.Max(0, v1.U8x16_7 - v2.U8x16_7),
                (byte)Math.Max(0, v1.U8x16_8 - v2.U8x16_8),
                (byte)Math.Max(0, v1.U8x16_9 - v2.U8x16_9),
                (byte)Math.Max(0, v1.U8x16_A - v2.U8x16_A),
                (byte)Math.Max(0, v1.U8x16_B - v2.U8x16_B),
                (byte)Math.Max(0, v1.U8x16_C - v2.U8x16_C),
                (byte)Math.Max(0, v1.U8x16_D - v2.U8x16_D),
                (byte)Math.Max(0, v1.U8x16_E - v2.U8x16_E),
                (byte)Math.Max(0, v1.U8x16_F - v2.U8x16_F)
            );
            context.OpStack.PushV128(result);
        }


        private static void ExecuteI16x8SubSatS(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)Math.Min(short.MaxValue, Math.Max(short.MinValue, v1.I16x8_0 - v2.I16x8_0)),
                (short)Math.Min(short.MaxValue, Math.Max(short.MinValue, v1.I16x8_1 - v2.I16x8_1)),
                (short)Math.Min(short.MaxValue, Math.Max(short.MinValue, v1.I16x8_2 - v2.I16x8_2)),
                (short)Math.Min(short.MaxValue, Math.Max(short.MinValue, v1.I16x8_3 - v2.I16x8_3)),
                (short)Math.Min(short.MaxValue, Math.Max(short.MinValue, v1.I16x8_4 - v2.I16x8_4)),
                (short)Math.Min(short.MaxValue, Math.Max(short.MinValue, v1.I16x8_5 - v2.I16x8_5)),
                (short)Math.Min(short.MaxValue, Math.Max(short.MinValue, v1.I16x8_6 - v2.I16x8_6)),
                (short)Math.Min(short.MaxValue, Math.Max(short.MinValue, v1.I16x8_7 - v2.I16x8_7))
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8SubSatU(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)Math.Max(0, v1.I16x8_0 - v2.I16x8_0),
                (short)Math.Max(0, v1.I16x8_1 - v2.I16x8_1),
                (short)Math.Max(0, v1.I16x8_2 - v2.I16x8_2),
                (short)Math.Max(0, v1.I16x8_3 - v2.I16x8_3),
                (short)Math.Max(0, v1.I16x8_4 - v2.I16x8_4),
                (short)Math.Max(0, v1.I16x8_5 - v2.I16x8_5),
                (short)Math.Max(0, v1.I16x8_6 - v2.I16x8_6),
                (short)Math.Max(0, v1.I16x8_7 - v2.I16x8_7)
            );
            context.OpStack.PushV128(result);
        }
    }
}