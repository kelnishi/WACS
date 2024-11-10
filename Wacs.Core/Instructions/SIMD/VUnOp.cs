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
        public static readonly NumericInst F32x4Abs     = new (SimdCode.F32x4Abs     , ExecuteF32x4Abs    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Abs     = new (SimdCode.F64x2Abs     , ExecuteF64x2Abs    , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16Neg     = new (SimdCode.I8x16Neg     , ExecuteI8x16Neg    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8Neg     = new (SimdCode.I16x8Neg     , ExecuteI16x8Neg    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4Neg     = new (SimdCode.I32x4Neg     , ExecuteI32x4Neg    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2Neg     = new (SimdCode.I64x2Neg     , ExecuteI64x2Neg    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Neg     = new (SimdCode.F32x4Neg     , ExecuteF32x4Neg    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Neg     = new (SimdCode.F64x2Neg     , ExecuteF64x2Neg    , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst F32x4Sqrt    = new (SimdCode.F32x4Sqrt    , ExecuteF32x4Sqrt   , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Sqrt    = new (SimdCode.F64x2Sqrt    , ExecuteF64x2Sqrt   , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst F32x4Ceil    = new (SimdCode.F32x4Ceil    , ExecuteF32x4Ceil   , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Ceil    = new (SimdCode.F64x2Ceil    , ExecuteF64x2Ceil   , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Floor   = new (SimdCode.F32x4Floor   , ExecuteF32x4Floor  , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Floor   = new (SimdCode.F64x2Floor   , ExecuteF64x2Floor  , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Trunc   = new (SimdCode.F32x4Trunc   , ExecuteF32x4Trunc  , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Trunc   = new (SimdCode.F64x2Trunc   , ExecuteF64x2Trunc  , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Nearest = new (SimdCode.F32x4Nearest , ExecuteF32x4Nearest, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Nearest = new (SimdCode.F64x2Nearest , ExecuteF64x2Nearest, ValidateOperands(pop: ValType.V128, push: ValType.V128));

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

        private static void ExecuteF32x4Abs(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Abs(val.F32x4_0),
                Math.Abs(val.F32x4_1),
                Math.Abs(val.F32x4_2),
                Math.Abs(val.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Abs(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Abs(val.F64x2_0),
                Math.Abs(val.F64x2_1)
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

        private static void ExecuteF32x4Neg(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                -val.F32x4_0,
                -val.F32x4_1,
                -val.F32x4_2,
                -val.F32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Neg(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                -val.F64x2_0,
                -val.F64x2_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Sqrt(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)Math.Sqrt(val.F32x4_0),
                (float)Math.Sqrt(val.F32x4_1),
                (float)Math.Sqrt(val.F32x4_2),
                (float)Math.Sqrt(val.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Sqrt(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Sqrt(val.F64x2_0),
                Math.Sqrt(val.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Ceil(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)Math.Ceiling(val.F32x4_0),
                (float)Math.Ceiling(val.F32x4_1),
                (float)Math.Ceiling(val.F32x4_2),
                (float)Math.Ceiling(val.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Ceil(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Ceiling(val.F64x2_0),
                Math.Ceiling(val.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Floor(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)Math.Floor(val.F32x4_0),
                (float)Math.Floor(val.F32x4_1),
                (float)Math.Floor(val.F32x4_2),
                (float)Math.Floor(val.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Floor(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Floor(val.F64x2_0),
                Math.Floor(val.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Trunc(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)Math.Truncate(val.F32x4_0),
                (float)Math.Truncate(val.F32x4_1),
                (float)Math.Truncate(val.F32x4_2),
                (float)Math.Truncate(val.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Trunc(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Truncate(val.F64x2_0),
                Math.Truncate(val.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Nearest(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)Math.Round(val.F32x4_0),
                (float)Math.Round(val.F32x4_1),
                (float)Math.Round(val.F32x4_2),
                (float)Math.Round(val.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Nearest(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Round(val.F64x2_0),
                Math.Round(val.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }
    }
}