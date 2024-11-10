using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

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

        private static void ExecuteI8x16Eq(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.eq failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.eq failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.U8x16_0 == c2.U8x16_0 ? (byte)1 : (byte)0,
                c1.U8x16_1 == c2.U8x16_1 ? (byte)1 : (byte)0,
                c1.U8x16_2 == c2.U8x16_2 ? (byte)1 : (byte)0,
                c1.U8x16_3 == c2.U8x16_3 ? (byte)1 : (byte)0,
                c1.U8x16_4 == c2.U8x16_4 ? (byte)1 : (byte)0,
                c1.U8x16_5 == c2.U8x16_5 ? (byte)1 : (byte)0,
                c1.U8x16_6 == c2.U8x16_6 ? (byte)1 : (byte)0,
                c1.U8x16_7 == c2.U8x16_7 ? (byte)1 : (byte)0,
                c1.U8x16_8 == c2.U8x16_8 ? (byte)1 : (byte)0,
                c1.U8x16_9 == c2.U8x16_9 ? (byte)1 : (byte)0,
                c1.U8x16_A == c2.U8x16_A ? (byte)1 : (byte)0,
                c1.U8x16_B == c2.U8x16_B ? (byte)1 : (byte)0,
                c1.U8x16_C == c2.U8x16_C ? (byte)1 : (byte)0,
                c1.U8x16_D == c2.U8x16_D ? (byte)1 : (byte)0,
                c1.U8x16_E == c2.U8x16_E ? (byte)1 : (byte)0,
                c1.U8x16_F == c2.U8x16_F ? (byte)1 : (byte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16Ne(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.ne failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.eq failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.U8x16_0 != c2.U8x16_0 ? (byte)1 : (byte)0,
                c1.U8x16_1 != c2.U8x16_1 ? (byte)1 : (byte)0,
                c1.U8x16_2 != c2.U8x16_2 ? (byte)1 : (byte)0,
                c1.U8x16_3 != c2.U8x16_3 ? (byte)1 : (byte)0,
                c1.U8x16_4 != c2.U8x16_4 ? (byte)1 : (byte)0,
                c1.U8x16_5 != c2.U8x16_5 ? (byte)1 : (byte)0,
                c1.U8x16_6 != c2.U8x16_6 ? (byte)1 : (byte)0,
                c1.U8x16_7 != c2.U8x16_7 ? (byte)1 : (byte)0,
                c1.U8x16_8 != c2.U8x16_8 ? (byte)1 : (byte)0,
                c1.U8x16_9 != c2.U8x16_9 ? (byte)1 : (byte)0,
                c1.U8x16_A != c2.U8x16_A ? (byte)1 : (byte)0,
                c1.U8x16_B != c2.U8x16_B ? (byte)1 : (byte)0,
                c1.U8x16_C != c2.U8x16_C ? (byte)1 : (byte)0,
                c1.U8x16_D != c2.U8x16_D ? (byte)1 : (byte)0,
                c1.U8x16_E != c2.U8x16_E ? (byte)1 : (byte)0,
                c1.U8x16_F != c2.U8x16_F ? (byte)1 : (byte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16LtS(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.lt_s failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.lt_s failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                c1.I8x16_0 < c2.I8x16_0 ? (byte)1 : (byte)0,
                c1.I8x16_1 < c2.I8x16_1 ? (byte)1 : (byte)0,
                c1.I8x16_2 < c2.I8x16_2 ? (byte)1 : (byte)0,
                c1.I8x16_3 < c2.I8x16_3 ? (byte)1 : (byte)0,
                c1.I8x16_4 < c2.I8x16_4 ? (byte)1 : (byte)0,
                c1.I8x16_5 < c2.I8x16_5 ? (byte)1 : (byte)0,
                c1.I8x16_6 < c2.I8x16_6 ? (byte)1 : (byte)0,
                c1.I8x16_7 < c2.I8x16_7 ? (byte)1 : (byte)0,
                c1.I8x16_8 < c2.I8x16_8 ? (byte)1 : (byte)0,
                c1.I8x16_9 < c2.I8x16_9 ? (byte)1 : (byte)0,
                c1.I8x16_A < c2.I8x16_A ? (byte)1 : (byte)0,
                c1.I8x16_B < c2.I8x16_B ? (byte)1 : (byte)0,
                c1.I8x16_C < c2.I8x16_C ? (byte)1 : (byte)0,
                c1.I8x16_D < c2.I8x16_D ? (byte)1 : (byte)0,
                c1.I8x16_E < c2.I8x16_E ? (byte)1 : (byte)0,
                c1.I8x16_F < c2.I8x16_F ? (byte)1 : (byte)0);
            context.OpStack.PushV128(c);
        }

        private static void ExecuteI8x16LtU(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.lt_u failed. Wrong type on stack.");
            V128 c2 = context.OpStack.PopV128();
            context.Assert(context.OpStack.Peek().IsV128, $"Instruction i8x16.lt_u failed. Wrong type on stack.");
            V128 c1 = context.OpStack.PopV128();
            V128 c = new V128(
                (c1.I8x16_0 < c2.I8x16_0) ? (byte)1 : (byte)0,
                (c1.I8x16_1 < c2.I8x16_1) ? (byte)1 : (byte)0,
                (c1.I8x16_2 < c2.I8x16_2) ? (byte)1 : (byte)0,
                (c1.I8x16_3 < c2.I8x16_3) ? (byte)1 : (byte)0,
                (c1.I8x16_4 < c2.I8x16_4) ? (byte)1 : (byte)0,
                (c1.I8x16_5 < c2.I8x16_5) ? (byte)1 : (byte)0,
                (c1.I8x16_6 < c2.I8x16_6) ? (byte)1 : (byte)0,
                (c1.I8x16_7 < c2.I8x16_7) ? (byte)1 : (byte)0,
                (c1.I8x16_8 < c2.I8x16_8) ? (byte)1 : (byte)0,
                (c1.I8x16_9 < c2.I8x16_9) ? (byte)1 : (byte)0,
                (c1.I8x16_A < c2.I8x16_A) ? (byte)1 : (byte)0,
                (c1.I8x16_B < c2.I8x16_B) ? (byte)1 : (byte)0,
                (c1.I8x16_C < c2.I8x16_C) ? (byte)1 : (byte)0,
                (c1.I8x16_D < c2.I8x16_D) ? (byte)1 : (byte)0,
                (c1.I8x16_E < c2.I8x16_E) ? (byte)1 : (byte)0,
                (c1.I8x16_F < c2.I8x16_F) ? (byte)1 : (byte)0);
            context.OpStack.PushV128(c);
        }
    }
}