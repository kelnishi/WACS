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
    // VvUnOps - Bit-wise Logical Not
    public partial class NumericInst
    {
        public static readonly NumericInst V128Not = new(SimdCode.V128Not, ExecuteV128Not,
            ValidateOperands(pop: ValType.V128, push: ValType.V128), 0);

        private static void ExecuteV128Not(ExecContext context)
        {
            V128 v1 = context.OpStack.PopV128();
            V128 result = ~v1;
            context.OpStack.PushV128(result);
        }
    }
}