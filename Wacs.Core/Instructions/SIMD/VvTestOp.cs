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

using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst V128AnyTrue = new(SimdCode.V128AnyTrue, ExecuteV128AnyTrue,
            ValidateOperands(pop: ValType.V128, push: ValType.I32), 0);

        private static void ExecuteV128AnyTrue(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            int result = (c.U64x2_0 & 0xFFFF_FFFF_FFFF_FFFF) != 0UL || (c.U64x2_1 & 0xFFFF_FFFF_FFFF_FFFF) != 0UL ? 1 : 0;
            context.OpStack.PushI32(result);
        }
    }
}