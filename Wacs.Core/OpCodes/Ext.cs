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

using Wacs.Core.Attributes;

namespace Wacs.Core.OpCodes
{
    /// <summary>
    /// Represents all WebAssembly opcodes from Extensions (FC prefix)
    /// see WebAssembly Specification Release 2.0 (Draft 2024-09-26)
    /// </summary>
    public enum ExtCode : byte
    {
        [OpCode("i32.trunc_sat_f32_s")] I32TruncSatF32S = 0x00,
        [OpCode("i32.trunc_sat_f32_u")] I32TruncSatF32U = 0x01,
        [OpCode("i32.trunc_sat_f64_s")] I32TruncSatF64S = 0x02,
        [OpCode("i32.trunc_sat_f64_u")] I32TruncSatF64U = 0x03,
        [OpCode("i64.trunc_sat_f32_s")] I64TruncSatF32S = 0x04,
        [OpCode("i64.trunc_sat_f32_u")] I64TruncSatF32U = 0x05,
        [OpCode("i64.trunc_sat_f64_s")] I64TruncSatF64S = 0x06,
        [OpCode("i64.trunc_sat_f64_u")] I64TruncSatF64U = 0x07,
    
        [OpCode("memory.init")]         MemoryInit = 0x08,
        [OpCode("data.drop")]           DataDrop   = 0x09,
        [OpCode("memory.copy")]         MemoryCopy = 0x0A,
        [OpCode("memory.fill")]         MemoryFill = 0x0B,
   
        [OpCode("table.init")]          TableInit = 0x0C, //12
        [OpCode("elem.drop")]           ElemDrop  = 0x0D, //13
        [OpCode("table.copy")]          TableCopy = 0x0E, //14
        [OpCode("table.grow")]          TableGrow = 0x0F, //15
        [OpCode("table.size")]          TableSize = 0x10, //16
        [OpCode("table.fill")]          TableFill = 0x11, //17
    }

}