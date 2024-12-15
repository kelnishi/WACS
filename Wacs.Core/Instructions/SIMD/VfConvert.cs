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

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst F32x4ConvertI32x4S = new(SimdCode.F32x4ConvertI32x4S, ExecuteF32x4ConvertI32x4S, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4ConvertI32x4U = new(SimdCode.F32x4ConvertI32x4U, ExecuteF32x4ConvertI32x4U, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2ConvertLowI32x4S = new(SimdCode.F64x2ConvertLowI32x4S, ExecuteF64x2ConvertLowI32x4S, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2ConvertLowI32x4U = new(SimdCode.F64x2ConvertLowI32x4U, ExecuteF64x2ConvertLowI32x4U, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F32x4DemoteF64x2Zero = new(SimdCode.F32x4DemoteF64x2Zero, ExecuteF32x4DemoteF64x2Zero, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst F64x2PromoteLowF32x4 = new(SimdCode.F64x2PromoteLowF32x4, ExecuteF64x2PromoteLowF32x4, ValidateOperands(pop: ValType.V128, push: ValType.V128));

        private static void ExecuteF32x4ConvertI32x4S(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)val.I32x4_0,
                (float)val.I32x4_1,
                (float)val.I32x4_2,
                (float)val.I32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4ConvertI32x4U(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)val.U32x4_0,
                (float)val.U32x4_1,
                (float)val.U32x4_2,
                (float)val.U32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2ConvertLowI32x4S(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (double)val.I32x4_0,
                (double)val.I32x4_1
                );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2ConvertLowI32x4U(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (double)val.U32x4_0,
                (double)val.U32x4_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF32x4DemoteF64x2Zero(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (float)val.F64x2_0,
                (float)val.F64x2_1,
                0.0f,
                0.0f
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteF64x2PromoteLowF32x4(ExecContext context)
        {
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (double)val.F32x4_0,
                (double)val.F32x4_1
            );
            context.OpStack.PushV128(result);
        }
    }
}
