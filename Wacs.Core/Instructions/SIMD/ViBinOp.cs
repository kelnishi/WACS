using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        // Vector Integer (VI) Binary Operations

        public static readonly NumericInst I8x16Add = new(SimdCode.I8x16Add, ExecuteI8x16Add,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16Sub = new(SimdCode.I8x16Sub, ExecuteI8x16Sub,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8Add = new(SimdCode.I16x8Add, ExecuteI16x8Add,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8Sub = new(SimdCode.I16x8Sub, ExecuteI16x8Sub,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4Add = new(SimdCode.I32x4Add, ExecuteI32x4Add,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4Sub = new(SimdCode.I32x4Sub, ExecuteI32x4Sub,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I64x2Add = new(SimdCode.I64x2Add, ExecuteI64x2Add,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I64x2Sub = new(SimdCode.I64x2Sub, ExecuteI64x2Sub,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        // Execute methods
        private static void ExecuteI8x16Add(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)(v1.U8x16_0 + v2.U8x16_0),
                (byte)(v1.U8x16_1 + v2.U8x16_1),
                (byte)(v1.U8x16_2 + v2.U8x16_2),
                (byte)(v1.U8x16_3 + v2.U8x16_3),
                (byte)(v1.U8x16_4 + v2.U8x16_4),
                (byte)(v1.U8x16_5 + v2.U8x16_5),
                (byte)(v1.U8x16_6 + v2.U8x16_6),
                (byte)(v1.U8x16_7 + v2.U8x16_7),
                (byte)(v1.U8x16_8 + v2.U8x16_8),
                (byte)(v1.U8x16_9 + v2.U8x16_9),
                (byte)(v1.U8x16_A + v2.U8x16_A),
                (byte)(v1.U8x16_B + v2.U8x16_B),
                (byte)(v1.U8x16_C + v2.U8x16_C),
                (byte)(v1.U8x16_D + v2.U8x16_D),
                (byte)(v1.U8x16_E + v2.U8x16_E),
                (byte)(v1.U8x16_F + v2.U8x16_F)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16Sub(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)(v1.U8x16_0 - v2.U8x16_0),
                (byte)(v1.U8x16_1 - v2.U8x16_1),
                (byte)(v1.U8x16_2 - v2.U8x16_2),
                (byte)(v1.U8x16_3 - v2.U8x16_3),
                (byte)(v1.U8x16_4 - v2.U8x16_4),
                (byte)(v1.U8x16_5 - v2.U8x16_5),
                (byte)(v1.U8x16_6 - v2.U8x16_6),
                (byte)(v1.U8x16_7 - v2.U8x16_7),
                (byte)(v1.U8x16_8 - v2.U8x16_8),
                (byte)(v1.U8x16_9 - v2.U8x16_9),
                (byte)(v1.U8x16_A - v2.U8x16_A),
                (byte)(v1.U8x16_B - v2.U8x16_B),
                (byte)(v1.U8x16_C - v2.U8x16_C),
                (byte)(v1.U8x16_D - v2.U8x16_D),
                (byte)(v1.U8x16_E - v2.U8x16_E),
                (byte)(v1.U8x16_F - v2.U8x16_F)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8Add(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)(v1.I16x8_0 + v2.I16x8_0),
                (short)(v1.I16x8_1 + v2.I16x8_1),
                (short)(v1.I16x8_2 + v2.I16x8_2),
                (short)(v1.I16x8_3 + v2.I16x8_3),
                (short)(v1.I16x8_4 + v2.I16x8_4),
                (short)(v1.I16x8_5 + v2.I16x8_5),
                (short)(v1.I16x8_6 + v2.I16x8_6),
                (short)(v1.I16x8_7 + v2.I16x8_7)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8Sub(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)(v1.I16x8_0 - v2.I16x8_0),
                (short)(v1.I16x8_1 - v2.I16x8_1),
                (short)(v1.I16x8_2 - v2.I16x8_2),
                (short)(v1.I16x8_3 - v2.I16x8_3),
                (short)(v1.I16x8_4 - v2.I16x8_4),
                (short)(v1.I16x8_5 - v2.I16x8_5),
                (short)(v1.I16x8_6 - v2.I16x8_6),
                (short)(v1.I16x8_7 - v2.I16x8_7)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4Add(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                v1.I32x4_0 + v2.I32x4_0,
                v1.I32x4_1 + v2.I32x4_1,
                v1.I32x4_2 + v2.I32x4_2,
                v1.I32x4_3 + v2.I32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4Sub(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                v1.I32x4_0 - v2.I32x4_0,
                v1.I32x4_1 - v2.I32x4_1,
                v1.I32x4_2 - v2.I32x4_2,
                v1.I32x4_3 - v2.I32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2Add(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                v1.I64x2_0 + v2.I64x2_0,
                v1.I64x2_1 + v2.I64x2_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2Sub(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                v1.I64x2_0 - v2.I64x2_0,
                v1.I64x2_1 - v2.I64x2_1
            );
            context.OpStack.PushV128(result);
        }
    }
}