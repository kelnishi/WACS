using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I8x16Abs     = new (SimdCode.I8x16Abs     , ExecuteI8x16Abs    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8Abs     = new (SimdCode.I16x8Abs     , ExecuteI16x8Abs    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4Abs     = new (SimdCode.I32x4Abs     , ExecuteI32x4Abs    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2Abs     = new (SimdCode.I64x2Abs     , ExecuteI64x2Abs    , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16Neg     = new (SimdCode.I8x16Neg     , ExecuteI8x16Neg    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8Neg     = new (SimdCode.I16x8Neg     , ExecuteI16x8Neg    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4Neg     = new (SimdCode.I32x4Neg     , ExecuteI32x4Neg    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2Neg     = new (SimdCode.I64x2Neg     , ExecuteI64x2Neg    , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16Popcnt  = new (SimdCode.I8x16Popcnt  , ExecuteI8x16Popcnt , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        private static void ExecuteI8x16Abs(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                val.I8x16_0 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_0),
                val.I8x16_1 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_1),
                val.I8x16_2 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_2),
                val.I8x16_3 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_3),
                val.I8x16_4 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_4),
                val.I8x16_5 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_5),
                val.I8x16_6 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_6),
                val.I8x16_7 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_7),
                val.I8x16_8 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_8),
                val.I8x16_9 == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_9),
                val.I8x16_A == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_A),
                val.I8x16_B == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_B),
                val.I8x16_C == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_C),
                val.I8x16_D == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_D),
                val.I8x16_E == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_E),
                val.I8x16_F == sbyte.MinValue ? sbyte.MinValue : Math.Abs(val.I8x16_F)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8Abs(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                val.I16x8_0 == short.MinValue ? short.MinValue : Math.Abs(val.I16x8_0),
                val.I16x8_1 == short.MinValue ? short.MinValue : Math.Abs(val.I16x8_1),
                val.I16x8_2 == short.MinValue ? short.MinValue : Math.Abs(val.I16x8_2),
                val.I16x8_3 == short.MinValue ? short.MinValue : Math.Abs(val.I16x8_3),
                val.I16x8_4 == short.MinValue ? short.MinValue : Math.Abs(val.I16x8_4),
                val.I16x8_5 == short.MinValue ? short.MinValue : Math.Abs(val.I16x8_5),
                val.I16x8_6 == short.MinValue ? short.MinValue : Math.Abs(val.I16x8_6),
                val.I16x8_7 == short.MinValue ? short.MinValue : Math.Abs(val.I16x8_7)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4Abs(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                val.I32x4_0 == int.MinValue ? int.MinValue : Math.Abs(val.I32x4_0),
                val.I32x4_1 == int.MinValue ? int.MinValue : Math.Abs(val.I32x4_1),
                val.I32x4_2 == int.MinValue ? int.MinValue : Math.Abs(val.I32x4_2),
                val.I32x4_3 == int.MinValue ? int.MinValue : Math.Abs(val.I32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2Abs(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                val.I64x2_0 == long.MinValue ? long.MinValue : Math.Abs(val.I64x2_0),
                val.I64x2_1 == long.MinValue ? long.MinValue : Math.Abs(val.I64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16Neg(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                val.I8x16_0 == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_0,
                val.I8x16_1 == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_1,
                val.I8x16_2 == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_2,
                val.I8x16_3 == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_3,
                val.I8x16_4 == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_4,
                val.I8x16_5 == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_5,
                val.I8x16_6 == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_6,
                val.I8x16_7 == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_7,
                val.I8x16_8 == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_8,
                val.I8x16_9 == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_9,
                val.I8x16_A == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_A,
                val.I8x16_B == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_B,
                val.I8x16_C == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_C,
                val.I8x16_D == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_D,
                val.I8x16_E == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_E,
                val.I8x16_F == sbyte.MinValue ? sbyte.MinValue : (sbyte)-val.I8x16_F
            ); context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8Neg(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                val.I16x8_0 == short.MinValue ? short.MinValue : (short)-val.I16x8_0,
                val.I16x8_1 == short.MinValue ? short.MinValue : (short)-val.I16x8_1,
                val.I16x8_2 == short.MinValue ? short.MinValue : (short)-val.I16x8_2,
                val.I16x8_3 == short.MinValue ? short.MinValue : (short)-val.I16x8_3,
                val.I16x8_4 == short.MinValue ? short.MinValue : (short)-val.I16x8_4,
                val.I16x8_5 == short.MinValue ? short.MinValue : (short)-val.I16x8_5,
                val.I16x8_6 == short.MinValue ? short.MinValue : (short)-val.I16x8_6,
                val.I16x8_7 == short.MinValue ? short.MinValue : (short)-val.I16x8_7
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4Neg(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                val.I32x4_0 == int.MinValue ? int.MinValue : -val.I32x4_0,
                val.I32x4_1 == int.MinValue ? int.MinValue : -val.I32x4_1,
                val.I32x4_2 == int.MinValue ? int.MinValue : -val.I32x4_2,
                val.I32x4_3 == int.MinValue ? int.MinValue : -val.I32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2Neg(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                val.I64x2_0 == long.MinValue ? long.MinValue : -val.I64x2_0,
                val.I64x2_1 == long.MinValue ? long.MinValue : -val.I64x2_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16Popcnt(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                PopCount(val.I8x16_0),
                PopCount(val.I8x16_1),
                PopCount(val.I8x16_2),
                PopCount(val.I8x16_3),
                PopCount(val.I8x16_4),
                PopCount(val.I8x16_5),
                PopCount(val.I8x16_6),
                PopCount(val.I8x16_7),
                PopCount(val.I8x16_8),
                PopCount(val.I8x16_9),
                PopCount(val.I8x16_A),
                PopCount(val.I8x16_B),
                PopCount(val.I8x16_C),
                PopCount(val.I8x16_D),
                PopCount(val.I8x16_E),
                PopCount(val.I8x16_F)
            );
            context.OpStack.PushV128(result);
        }

        private static byte PopCount(sbyte value)
        {
            byte count = 0;
            for (int i = 0; i < 8; i++)
            {
                count += (byte)((value >> i) & 1);
            }
            return count;
        }
    }
}