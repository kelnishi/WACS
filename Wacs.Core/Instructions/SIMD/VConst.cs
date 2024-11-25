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

using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Simd
{
    //0x41
    public class InstV128Const : InstructionBase, IConstInstruction
    {
        public override ByteCode Op => SimdCode.V128Const;
        private V128 V128;

        /// <summary>
        /// @Spec 3.3.1.1 t.const
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context) =>
            context.OpStack.PushV128(V128);

        /// <summary>
        /// @Spec 4.4.1.1. t.const c
        /// </summary>
        public override int Execute(ExecContext context)
        {
            context.OpStack.PushV128(V128);
            return 1;
        }

        public override IInstruction Parse(BinaryReader reader)
        {
            V128 = new V128(reader.ReadBytes(16));
            return this;
        }

        public IInstruction Immediate(V128 value)
        {
            V128 = value;
            return this;
        }
    }
}