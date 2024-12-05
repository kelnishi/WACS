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
using Wacs.Core.Types.Defs;

// ReSharper disable InconsistentNaming
// ReSharper disable CompareOfFloatsByEqualityOperator
namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst F32x4Eq = new(SimdCode.F32x4Eq, ExecuteF32x4Eq, ValidateOperands(pop1:ValType.V128, pop2:ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Ne = new(SimdCode.F32x4Ne, ExecuteF32x4Ne, ValidateOperands(pop1:ValType.V128, pop2:ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Lt = new(SimdCode.F32x4Lt, ExecuteF32x4Lt, ValidateOperands(pop1:ValType.V128, pop2:ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Gt = new(SimdCode.F32x4Gt, ExecuteF32x4Gt, ValidateOperands(pop1:ValType.V128, pop2:ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Le = new(SimdCode.F32x4Le, ExecuteF32x4Le, ValidateOperands(pop1:ValType.V128, pop2:ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4Ge = new(SimdCode.F32x4Ge, ExecuteF32x4Ge, ValidateOperands(pop1:ValType.V128, pop2:ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Eq = new(SimdCode.F64x2Eq, ExecuteF64x2Eq, ValidateOperands(pop1:ValType.V128, pop2:ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Ne = new(SimdCode.F64x2Ne, ExecuteF64x2Ne, ValidateOperands(pop1:ValType.V128, pop2:ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Lt = new(SimdCode.F64x2Lt, ExecuteF64x2Lt, ValidateOperands(pop1:ValType.V128, pop2:ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Gt = new(SimdCode.F64x2Gt, ExecuteF64x2Gt, ValidateOperands(pop1:ValType.V128, pop2:ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Le = new(SimdCode.F64x2Le, ExecuteF64x2Le, ValidateOperands(pop1:ValType.V128, pop2:ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2Ge = new(SimdCode.F64x2Ge, ExecuteF64x2Ge, ValidateOperands(pop1:ValType.V128, pop2:ValType.V128, push: ValType.V128));

        private static void ExecuteF32x4Eq(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();

            V128 result = new V128(
                c1.F32x4_0 == c2.F32x4_0 ? -1 : 0,
                c1.F32x4_1 == c2.F32x4_1 ? -1 : 0,
                c1.F32x4_2 == c2.F32x4_2 ? -1 : 0,
                c1.F32x4_3 == c2.F32x4_3 ? -1 : 0
            );

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Ne(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();

            V128 result = new V128(
                c1.F32x4_0 != c2.F32x4_0 ? -1 : 0,
                c1.F32x4_1 != c2.F32x4_1 ? -1 : 0,
                c1.F32x4_2 != c2.F32x4_2 ? -1 : 0,
                c1.F32x4_3 != c2.F32x4_3 ? -1 : 0
            );

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Lt(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();

            V128 result = new V128(
                c1.F32x4_0 < c2.F32x4_0 ? -1 : 0,
                c1.F32x4_1 < c2.F32x4_1 ? -1 : 0,
                c1.F32x4_2 < c2.F32x4_2 ? -1 : 0,
                c1.F32x4_3 < c2.F32x4_3 ? -1 : 0
            );

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Gt(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();

            V128 result = new V128(
                c1.F32x4_0 > c2.F32x4_0 ? -1 : 0,
                c1.F32x4_1 > c2.F32x4_1 ? -1 : 0,
                c1.F32x4_2 > c2.F32x4_2 ? -1 : 0,
                c1.F32x4_3 > c2.F32x4_3 ? -1 : 0
            );

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Le(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();

            V128 result = new V128(
                c1.F32x4_0 <= c2.F32x4_0 ? -1 : 0,
                c1.F32x4_1 <= c2.F32x4_1 ? -1 : 0,
                c1.F32x4_2 <= c2.F32x4_2 ? -1 : 0,
                c1.F32x4_3 <= c2.F32x4_3 ? -1 : 0
            );

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4Ge(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();

            V128 result = new V128(
                c1.F32x4_0 >= c2.F32x4_0 ? -1 : 0,
                c1.F32x4_1 >= c2.F32x4_1 ? -1 : 0,
                c1.F32x4_2 >= c2.F32x4_2 ? -1 : 0,
                c1.F32x4_3 >= c2.F32x4_3 ? -1 : 0
            );

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Eq(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();

            V128 result = new V128(
                c1.F64x2_0 == c2.F64x2_0 ? -1 : 0,
                c1.F64x2_1 == c2.F64x2_1 ? -1 : 0
            );

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Ne(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();

            V128 result = new V128(
                c1.F64x2_0 != c2.F64x2_0 ? -1 : 0,
                c1.F64x2_1 != c2.F64x2_1 ? -1 : 0
            );

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Lt(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();

            V128 result = new V128(
                c1.F64x2_0 < c2.F64x2_0 ? -1 : 0,
                c1.F64x2_1 < c2.F64x2_1 ? -1 : 0
            );

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Gt(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();

            V128 result = new V128(
                c1.F64x2_0 > c2.F64x2_0 ? -1 : 0,
                c1.F64x2_1 > c2.F64x2_1 ? -1 : 0
            );

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Le(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();

            V128 result = new V128(
                c1.F64x2_0 <= c2.F64x2_0 ? -1 : 0,
                c1.F64x2_1 <= c2.F64x2_1 ? -1 : 0
            );

            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2Ge(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();

            V128 result = new V128(
                c1.F64x2_0 >= c2.F64x2_0 ? -1 : 0,
                c1.F64x2_1 >= c2.F64x2_1 ? -1 : 0
            );

            context.OpStack.PushV128(result);
        }
    }
}
