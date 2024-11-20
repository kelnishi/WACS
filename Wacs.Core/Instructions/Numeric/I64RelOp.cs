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
        public static readonly NumericInst I64Eq = new(OpCode.I64Eq, ExecuteI64Eq,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly NumericInst I64Ne = new(OpCode.I64Ne, ExecuteI64Ne,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly NumericInst I64LtS = new(OpCode.I64LtS, ExecuteI64LtS,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly NumericInst I64LtU = new(OpCode.I64LtU, ExecuteI64LtU,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly NumericInst I64GtS = new(OpCode.I64GtS, ExecuteI64GtS,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly NumericInst I64GtU = new(OpCode.I64GtU, ExecuteI64GtU,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly NumericInst I64LeS = new(OpCode.I64LeS, ExecuteI64LeS,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly NumericInst I64LeU = new(OpCode.I64LeU, ExecuteI64LeU,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly NumericInst I64GeS = new(OpCode.I64GeS, ExecuteI64GeS,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly NumericInst I64GeU = new(OpCode.I64GeU, ExecuteI64GeU,
            ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        private static void ExecuteI64Eq(ExecContext context)
        {
            long i2 = context.OpStack.PopI64().Int64;
            long i1 = context.OpStack.PopI64().Int64;
            int result = i1 == i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI64Ne(ExecContext context)
        {
            long i2 = context.OpStack.PopI64().Int64;
            long i1 = context.OpStack.PopI64().Int64;
            int result = i1 != i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI64LtS(ExecContext context)
        {
            long i2 = context.OpStack.PopI64().Int64;
            long i1 = context.OpStack.PopI64().Int64;
            int result = i1 < i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI64LtU(ExecContext context)
        {
            ulong i2 = context.OpStack.PopI64().UInt64;
            ulong i1 = context.OpStack.PopI64().UInt64;
            int result = i1 < i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI64GtS(ExecContext context)
        {
            long i2 = context.OpStack.PopI64().Int64;
            long i1 = context.OpStack.PopI64().Int64;
            int result = i1 > i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI64GtU(ExecContext context)
        {
            ulong i2 = context.OpStack.PopI64().UInt64;
            ulong i1 = context.OpStack.PopI64().UInt64;
            int result = i1 > i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI64LeS(ExecContext context)
        {
            long i2 = context.OpStack.PopI64().Int64;
            long i1 = context.OpStack.PopI64().Int64;
            int result = i1 <= i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI64LeU(ExecContext context)
        {
            ulong i2 = context.OpStack.PopI64().UInt64;
            ulong i1 = context.OpStack.PopI64().UInt64;
            int result = i1 <= i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI64GeS(ExecContext context)
        {
            long i2 = context.OpStack.PopI64().Int64;
            long i1 = context.OpStack.PopI64().Int64;
            int result = i1 >= i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI64GeU(ExecContext context)
        {
            ulong i2 = context.OpStack.PopI64().UInt64;
            ulong i1 = context.OpStack.PopI64().UInt64;
            int result = i1 >= i2 ? 1 : 0;
            context.OpStack.PushI32(result);
        }
    }
}