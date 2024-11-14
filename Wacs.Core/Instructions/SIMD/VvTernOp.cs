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
        // New entry for the V128BitSelect operation
        public static readonly NumericInst V128BitSelect = new(SimdCode.V128BitSelect, ExecuteV128BitSelect,
            ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, pop3: ValType.V128, push: ValType.V128));

        /// <summary>
        /// @Spec 4.3.2.35. ibitselect
        /// </summary>
        /// <param name="context"></param>
        private static void ExecuteV128BitSelect(ExecContext context)
        {
            V128 i3 = context.OpStack.PopV128(); // Bits to select from if not set
            V128 i2 = context.OpStack.PopV128(); // Bits to select from if set
            V128 i1 = context.OpStack.PopV128(); // Selection mask

            V128 j1 = i1 & i3;
            V128 j3 = ~i3;
            V128 j2 = i2 & j3;
            V128 result = j1 | j2;
            
            context.OpStack.PushV128(result);
        }
    }
}