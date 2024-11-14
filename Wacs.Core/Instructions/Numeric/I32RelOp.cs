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
        // @Spec 3.3.1.5. i.relop
        public static readonly NumericInst I32Eq = new(OpCode.I32Eq, ExecuteI32Eq,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32Ne = new(OpCode.I32Ne, ExecuteI32Ne,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32LtS = new(OpCode.I32LtS, ExecuteI32LtS,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32LtU = new(OpCode.I32LtU, ExecuteI32LtU,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32GtS = new(OpCode.I32GtS, ExecuteI32GtS,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32GtU = new(OpCode.I32GtU, ExecuteI32GtU,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32LeS = new(OpCode.I32LeS, ExecuteI32LeS,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32LeU = new(OpCode.I32LeU, ExecuteI32LeU,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32GeS = new(OpCode.I32GeS, ExecuteI32GeS,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I32GeU = new(OpCode.I32GeU, ExecuteI32GeU,
            ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        private static void ExecuteI32Eq(ExecContext context)
        {
            int i2 = context.OpStack.PopI32();
            int i1 = context.OpStack.PopI32();
            int result = i1 == i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI32Ne(ExecContext context)
        {
            int i2 = context.OpStack.PopI32();
            int i1 = context.OpStack.PopI32();
            int result = i1 != i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI32LtS(ExecContext context)
        {
            int i2 = context.OpStack.PopI32();
            int i1 = context.OpStack.PopI32();
            int result = i1 < i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI32LtU(ExecContext context)
        {
            uint i2 = context.OpStack.PopI32();
            uint i1 = context.OpStack.PopI32();
            int result = i1 < i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI32GtS(ExecContext context)
        {
            int b = context.OpStack.PopI32();
            int a = context.OpStack.PopI32();
            int result = a > b ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI32GtU(ExecContext context)
        {
            uint i2 = context.OpStack.PopI32();
            uint i1 = context.OpStack.PopI32();
            int result = i1 > i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI32LeS(ExecContext context)
        {
            int i2 = context.OpStack.PopI32();
            int i1 = context.OpStack.PopI32();
            int result = i1 <= i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI32LeU(ExecContext context)
        {
            uint i2 = context.OpStack.PopI32();
            uint i1 = context.OpStack.PopI32();
            int result = i1 <= i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI32GeS(ExecContext context)
        {
            int i2 = context.OpStack.PopI32();
            int i1 = context.OpStack.PopI32();
            int result = i1 >= i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI32GeU(ExecContext context)
        {
            uint i2 = context.OpStack.PopI32();
            uint i1 = context.OpStack.PopI32();
            int result = i1 >= i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }
    }
}