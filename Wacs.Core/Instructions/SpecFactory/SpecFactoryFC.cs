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
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions
{
    public partial class SpecFactory
    {
        public static InstructionBase? CreateInstruction(ExtCode opcode) => opcode switch
        {
            //Non-Trapping Saturating Float to Int Conversion 
            ExtCode.I32TruncSatF32S   => NumericInst.I32TruncSatF32S,
            ExtCode.I32TruncSatF32U   => NumericInst.I32TruncSatF32U,
            ExtCode.I32TruncSatF64S   => NumericInst.I32TruncSatF64S,
            ExtCode.I32TruncSatF64U   => NumericInst.I32TruncSatF64U,
            ExtCode.I64TruncSatF32S   => NumericInst.I64TruncSatF32S,
            ExtCode.I64TruncSatF32U   => NumericInst.I64TruncSatF32U,
            ExtCode.I64TruncSatF64S   => NumericInst.I64TruncSatF64S,
            ExtCode.I64TruncSatF64U   => NumericInst.I64TruncSatF64U,
            
            //Memory Instructions
            ExtCode.MemoryInit        => new InstMemoryInit(),
            ExtCode.DataDrop          => new InstDataDrop(),
            ExtCode.MemoryCopy        => new InstMemoryCopy(),
            ExtCode.MemoryFill        => new InstMemoryFill(),
            
            //Table Instructions
            ExtCode.TableInit         => new InstTableInit(),
            ExtCode.ElemDrop          => new InstElemDrop(),
            ExtCode.TableCopy         => new InstTableCopy(),
            ExtCode.TableGrow         => new InstTableGrow(),
            ExtCode.TableSize         => new InstTableSize(),
            ExtCode.TableFill         => new InstTableFill(),
            _ => 
                throw new InvalidDataException($"Unsupported instruction {opcode.GetMnemonic()}. ByteCode: 0xFC{(byte)opcode:X2}")
        };
    }
}