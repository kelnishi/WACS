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
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst F32Eq = new(OpCode.F32Eq, ExecuteF32Eq,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly NumericInst F32Ne = new(OpCode.F32Ne, ExecuteF32Ne,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly NumericInst F32Lt = new(OpCode.F32Lt, ExecuteF32Lt,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly NumericInst F32Gt = new(OpCode.F32Gt, ExecuteF32Gt,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly NumericInst F32Le = new(OpCode.F32Le, ExecuteF32Le,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly NumericInst F32Ge = new(OpCode.F32Ge, ExecuteF32Ge,
            ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        private static void ExecuteF32Eq(ExecContext context)
        {
            float i2 = context.OpStack.PopF32();
            float i1 = context.OpStack.PopF32();

            int result = i1 == i2 ? 1 : 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF32Ne(ExecContext context)
        {
            float i2 = context.OpStack.PopF32();
            float i1 = context.OpStack.PopF32();

            int result = i1 != i2 ? 1 : 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF32Lt(ExecContext context)
        {
            float i2 = context.OpStack.PopF32();
            float i1 = context.OpStack.PopF32();

            int result = i1 < i2 ? 1 : 0;
            if (float.IsNaN(i1) || float.IsNaN(i2))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF32Gt(ExecContext context)
        {
            float i2 = context.OpStack.PopF32();
            float i1 = context.OpStack.PopF32();

            int result = i1 > i2 ? 1 : 0;
            if (float.IsNaN(i1) || float.IsNaN(i2))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF32Le(ExecContext context)
        {
            float i2 = context.OpStack.PopF32();
            float i1 = context.OpStack.PopF32();

            int result = i1 <= i2 ? 1 : 0;
            if (float.IsNaN(i1) || float.IsNaN(i2))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF32Ge(ExecContext context)
        {
            float i2 = context.OpStack.PopF32();
            float i1 = context.OpStack.PopF32();

            int result = i1 >= i2 ? 1 : 0;
            if (float.IsNaN(i1) || float.IsNaN(i2))
                result = 0;

            context.OpStack.PushI32(result);
        }
    }
}