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
        public static readonly NumericInst F64Eq = new(OpCode.F64Eq, ExecuteF64Eq,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly NumericInst F64Ne = new(OpCode.F64Ne, ExecuteF64Ne,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly NumericInst F64Lt = new(OpCode.F64Lt, ExecuteF64Lt,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly NumericInst F64Gt = new(OpCode.F64Gt, ExecuteF64Gt,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly NumericInst F64Le = new(OpCode.F64Le, ExecuteF64Le,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly NumericInst F64Ge = new(OpCode.F64Ge, ExecuteF64Ge,
            ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));


        private static void ExecuteF64Eq(ExecContext context)
        {
            double i2 = context.OpStack.PopF64().Float64;
            double i1 = context.OpStack.PopF64().Float64;

            int result = i1 == i2 ? 1 : 0;
            
            context.OpStack.PushI32(result);
        }

        private static void ExecuteF64Ne(ExecContext context)
        {
            double i2 = context.OpStack.PopF64().Float64;
            double i1 = context.OpStack.PopF64().Float64;

            int result = i1 != i2 ? 1 : 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF64Lt(ExecContext context)
        {
            double i2 = context.OpStack.PopF64().Float64;
            double i1 = context.OpStack.PopF64().Float64;

            int result = i1 < i2 ? 1 : 0;
            if (double.IsNaN(i1) || double.IsNaN(i2))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF64Gt(ExecContext context)
        {
            double i2 = context.OpStack.PopF64().Float64;
            double i1 = context.OpStack.PopF64().Float64;

            int result = i1 > i2 ? 1 : 0;
            if (double.IsNaN(i1) || double.IsNaN(i2))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF64Le(ExecContext context)
        {
            double i2 = context.OpStack.PopF64().Float64;
            double i1 = context.OpStack.PopF64().Float64;

            int result = i1 <= i2 ? 1 : 0;
            if (double.IsNaN(i1) || double.IsNaN(i2))
                result = 0;

            context.OpStack.PushI32(result);
        }

        private static void ExecuteF64Ge(ExecContext context)
        {
            double i2 = context.OpStack.PopF64().Float64;
            double i1 = context.OpStack.PopF64().Float64;

            int result = i1 >= i2 ? 1 : 0;
            if (double.IsNaN(i1) || double.IsNaN(i2))
                result = 0;

            context.OpStack.PushI32(result);
        }
    }
}