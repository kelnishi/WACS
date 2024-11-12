using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst F32x4Add = new (SimdCode.F32x4Add, ExecuteF32x4Add, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Sub = new (SimdCode.F32x4Sub, ExecuteF32x4Sub, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Mul = new (SimdCode.F32x4Mul, ExecuteF32x4Mul, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Div = new (SimdCode.F32x4Div, ExecuteF32x4Div, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Min = new (SimdCode.F32x4Min, ExecuteF32x4Min, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Max = new (SimdCode.F32x4Max, ExecuteF32x4Max, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4PMin = new (SimdCode.F32x4PMin, ExecuteF32x4PMin, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4PMax = new (SimdCode.F32x4PMax, ExecuteF32x4PMax, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst F64x2Add = new (SimdCode.F64x2Add, ExecuteF64x2Add, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Sub = new (SimdCode.F64x2Sub, ExecuteF64x2Sub, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Mul = new (SimdCode.F64x2Mul, ExecuteF64x2Mul, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Div = new (SimdCode.F64x2Div, ExecuteF64x2Div, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Min = new (SimdCode.F64x2Min, ExecuteF64x2Min, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Max = new (SimdCode.F64x2Max, ExecuteF64x2Max, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2PMin = new (SimdCode.F64x2PMin, ExecuteF64x2PMin, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2PMax = new (SimdCode.F64x2PMax, ExecuteF64x2PMax, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        private static void ExecuteF32x4Add(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                val1.F32x4_0 + val2.F32x4_0,
                val1.F32x4_1 + val2.F32x4_1,
                val1.F32x4_2 + val2.F32x4_2,
                val1.F32x4_3 + val2.F32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Sub(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                val1.F32x4_0 - val2.F32x4_0,
                val1.F32x4_1 - val2.F32x4_1,
                val1.F32x4_2 - val2.F32x4_2,
                val1.F32x4_3 - val2.F32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Mul(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                val1.F32x4_0 * val2.F32x4_0,
                val1.F32x4_1 * val2.F32x4_1,
                val1.F32x4_2 * val2.F32x4_2,
                val1.F32x4_3 * val2.F32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Div(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                val1.F32x4_0 / val2.F32x4_0,
                val1.F32x4_1 / val2.F32x4_1,
                val1.F32x4_2 / val2.F32x4_2,
                val1.F32x4_3 / val2.F32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Min(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Min(val1.F32x4_0, val2.F32x4_0),
                Math.Min(val1.F32x4_1, val2.F32x4_1),
                Math.Min(val1.F32x4_2, val2.F32x4_2),
                Math.Min(val1.F32x4_3, val2.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Max(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Max(val1.F32x4_0, val2.F32x4_0),
                Math.Max(val1.F32x4_1, val2.F32x4_1),
                Math.Max(val1.F32x4_2, val2.F32x4_2),
                Math.Max(val1.F32x4_3, val2.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4PMin(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                (val1.F32x4_0 < val2.F32x4_0) ? val1.F32x4_0 : val2.F32x4_0,
                (val1.F32x4_1 < val2.F32x4_1) ? val1.F32x4_1 : val2.F32x4_1,
                (val1.F32x4_2 < val2.F32x4_2) ? val1.F32x4_2 : val2.F32x4_2,
                (val1.F32x4_3 < val2.F32x4_3) ? val1.F32x4_3 : val2.F32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4PMax(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                (val1.F32x4_0 > val2.F32x4_0) ? val1.F32x4_0 : val2.F32x4_0,
                (val1.F32x4_1 > val2.F32x4_1) ? val1.F32x4_1 : val2.F32x4_1,
                (val1.F32x4_2 > val2.F32x4_2) ? val1.F32x4_2 : val2.F32x4_2,
                (val1.F32x4_3 > val2.F32x4_3) ? val1.F32x4_3 : val2.F32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Add(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                val1.F64x2_0 + val2.F64x2_0,
                val1.F64x2_1 + val2.F64x2_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Sub(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                val1.F64x2_0 - val2.F64x2_0,
                val1.F64x2_1 - val2.F64x2_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Mul(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                val1.F64x2_0 * val2.F64x2_0,
                val1.F64x2_1 * val2.F64x2_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Div(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                val1.F64x2_0 / val2.F64x2_0,
                val1.F64x2_1 / val2.F64x2_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Min(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Min(val1.F64x2_0, val2.F64x2_0),
                Math.Min(val1.F64x2_1, val2.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Max(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                Math.Max(val1.F64x2_0, val2.F64x2_0),
                Math.Max(val1.F64x2_1, val2.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2PMin(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                (val1.F64x2_0 < val2.F64x2_0) ? val1.F64x2_0 : val2.F64x2_0,
                (val1.F64x2_1 < val2.F64x2_1) ? val1.F64x2_1 : val2.F64x2_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2PMax(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                (val1.F64x2_0 > val2.F64x2_0) ? val1.F64x2_0 : val2.F64x2_0,
                (val1.F64x2_1 > val2.F64x2_1) ? val1.F64x2_1 : val2.F64x2_1
            );
            context.OpStack.PushV128(result);
        }
    }
}