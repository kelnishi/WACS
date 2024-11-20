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
        // @Spec 3.3.1.4 i.testop
        public static readonly NumericInst I32Eqz = new(OpCode.I32Eqz, ExecuteI32Eqz,
            ValidateOperands(pop: ValType.I32, push: ValType.I32));

        public static readonly NumericInst I64Eqz = new(OpCode.I64Eqz, ExecuteI64Eqz,
            ValidateOperands(pop: ValType.I64, push: ValType.I32));

        private static void ExecuteI32Eqz(ExecContext context)
        {
            int i = context.OpStack.PopI32().Int32;
            int result = i == 0 ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI64Eqz(ExecContext context)
        {
            long i = context.OpStack.PopI64().Int64;
            int result = i == 0 ? 1 : 0;
            context.OpStack.PushI32(result);
        }
    }
}