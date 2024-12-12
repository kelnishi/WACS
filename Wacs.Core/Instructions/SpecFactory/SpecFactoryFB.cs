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
using Wacs.Core.Instructions.GC;
using Wacs.Core.OpCodes;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions
{
    public partial class SpecFactory
    {
        public static InstructionBase? CreateInstruction(GcCode opcode) => opcode switch
        {
            GcCode.StructNew        => new InstStructNew(),
            GcCode.StructNewDefault => new InstStructNewDefault(),
            GcCode.StructGet        => new InstStructGet(PackedExt.NotPacked),
            GcCode.StructGetS       => new InstStructGet(PackedExt.Signed),
            GcCode.StructGetU       => new InstStructGet(PackedExt.Unsigned),
            GcCode.StructSet        => new InstStructSet(),
            
            GcCode.ArrayNew         => new InstArrayNew(),
            GcCode.ArrayNewDefault  => new InstArrayNewDefault(),
            GcCode.ArrayNewFixed    => new InstArrayNewFixed(),
            GcCode.ArrayNewData     => new InstArrayNewData(),
            GcCode.ArrayNewElem     => new InstArrayNewElem(),
            
            GcCode.ArrayGet         => new InstArrayGet(PackedExt.NotPacked),
            GcCode.ArrayGetS        => new InstArrayGet(PackedExt.Signed),
            GcCode.ArrayGetU        => new InstArrayGet(PackedExt.Unsigned),
            GcCode.ArraySet         => new InstArraySet(),

            GcCode.ArrayLen         => new InstArrayLen(),
            GcCode.ArrayFill        => new InstArrayFill(),
            GcCode.ArrayCopy        => new InstArrayCopy(),
            


            
            _ => throw new InvalidDataException($"Unsupported instruction {opcode.GetMnemonic()}. ByteCode: 0xFB{(byte)opcode:X2}")
        };
    }
}