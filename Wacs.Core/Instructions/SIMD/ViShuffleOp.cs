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

namespace Wacs.Core.Instructions
{
    public class InstShuffleOp : InstructionBase
    {
        private V128 X;

        public override ByteCode Op => SimdCode.I8x16Shuffle;

        public override void Validate(IWasmValidationContext context)
        {
            for (int i = 0; i < 16; ++i)
            {
                context.Assert(X[(byte)i] < 32,
                    "Instruction {0} was invalid. Lane {1} ({2}) was >= 32.",Op.GetMnemonic(),i,X[(byte)i]);
            }

            context.OpStack.PopV128();
            context.OpStack.PopV128();
            context.OpStack.PushV128();
        }

        /// <summary>
        /// @Spec 4.4.3.7. i8x16.shuffle x
        /// </summary>
        /// <param name="context"></param>
        public override void Execute(ExecContext context)
        {
            V128 b = context.OpStack.PopV128();
            V128 a = context.OpStack.PopV128();
            MV128 result = new();
            for (byte i = 0; i < 16; ++i)
            {
                byte laneIndex = X[i];
                result[i] = laneIndex < 16 ? a[laneIndex] : b[(byte)(laneIndex - 16)];
            }
            context.OpStack.PushV128(result);
        }

        public static V128 ParseLanes(BinaryReader reader) => 
            new(reader.ReadBytes(16));

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = ParseLanes(reader);
            return this;
        }
    }
}