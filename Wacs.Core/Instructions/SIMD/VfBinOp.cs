// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst F32x4Add = new (SimdCode.F32x4Add, ExecuteF32x4Add, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F32x4Sub = new (SimdCode.F32x4Sub, ExecuteF32x4Sub, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F32x4Mul = new (SimdCode.F32x4Mul, ExecuteF32x4Mul, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F32x4Div = new (SimdCode.F32x4Div, ExecuteF32x4Div, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F32x4Min = new (SimdCode.F32x4Min, ExecuteF32x4Min, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F32x4Max = new (SimdCode.F32x4Max, ExecuteF32x4Max, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F32x4PMin = new (SimdCode.F32x4PMin, ExecuteF32x4PMin, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F32x4PMax = new (SimdCode.F32x4PMax, ExecuteF32x4PMax, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

        public static readonly NumericInst F64x2Add = new (SimdCode.F64x2Add, ExecuteF64x2Add, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F64x2Sub = new (SimdCode.F64x2Sub, ExecuteF64x2Sub, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F64x2Mul = new (SimdCode.F64x2Mul, ExecuteF64x2Mul, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F64x2Div = new (SimdCode.F64x2Div, ExecuteF64x2Div, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F64x2Min = new (SimdCode.F64x2Min, ExecuteF64x2Min, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F64x2Max = new (SimdCode.F64x2Max, ExecuteF64x2Max, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F64x2PMin = new (SimdCode.F64x2PMin, ExecuteF64x2PMin, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);
        public static readonly NumericInst F64x2PMax = new (SimdCode.F64x2PMax, ExecuteF64x2PMax, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128), -1);

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

        private static float PseudoMin(float a, float b)
        {
            if (float.IsNaN(a))
                return float.NaN;
            if (float.IsNaN(b))
                return a;
            return a < b ? a : b;
        }

        private static void ExecuteF32x4PMin(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                PseudoMin(val1.F32x4_0, val2.F32x4_0),
                PseudoMin(val1.F32x4_1, val2.F32x4_1),
                PseudoMin(val1.F32x4_2, val2.F32x4_2),
                PseudoMin(val1.F32x4_3, val2.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static float PseudoMax(float a, float b)
        {
            if (float.IsNaN(a))
                return float.NaN;
            if (float.IsNaN(b))
                return a;
            return a > b ? a : b;
        }

        private static void ExecuteF32x4PMax(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                PseudoMax(val1.F32x4_0, val2.F32x4_0),
                PseudoMax(val1.F32x4_1, val2.F32x4_1),
                PseudoMax(val1.F32x4_2, val2.F32x4_2),
                PseudoMax(val1.F32x4_3, val2.F32x4_3)
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

        private static double PseudoMin(double a, double b)
        {
            if (double.IsNaN(a))
                return double.NaN;
            if (double.IsNaN(b))
                return a;
            return a < b ? a : b;
        }

        private static void ExecuteF64x2PMin(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                PseudoMin(val1.F64x2_0, val2.F64x2_0),
                PseudoMin(val1.F64x2_1, val2.F64x2_1)
            );
            context.OpStack.PushV128(result);
        }

        private static double PseudoMax(double a, double b)
        {
            if (double.IsNaN(a))
                return double.NaN;
            if (double.IsNaN(b))
                return a;
            return a > b ? a : b;
        }

        private static void ExecuteF64x2PMax(ExecContext context)
        {
            V128 val2 = context.OpStack.PopV128();
            V128 val1 = context.OpStack.PopV128();
            V128 result = new V128(
                PseudoMax(val1.F64x2_0, val2.F64x2_0),
                PseudoMax(val1.F64x2_1, val2.F64x2_1) 
            );
            context.OpStack.PushV128(result);
        }
    }
}