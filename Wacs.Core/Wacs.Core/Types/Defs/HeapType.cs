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

using Wacs.Core.Attributes;

namespace Wacs.Core.Types.Defs
{
    /// <summary>
    /// https://webassembly.github.io/gc/core/bikeshed/index.html#heap-types⑦
    ///  absheaptype
    /// </summary>
    public enum HeapType : byte
    {
        [WatToken("nofunc")]   NoFunc      = 0x73,   // -0x0d
        [WatToken("noextern")] NoExtern    = 0x72,   // -0x0e
        [WatToken("none")]     None        = 0x71,   // -0x0f
        [WatToken("func")]     Func        = 0x70,   // -0x10
        [WatToken("extern")]   Extern      = 0x6F,   // -0x11
        [WatToken("any")]      Any         = 0x6E,   // -0x12
        [WatToken("eq")]       Eq          = 0x6D,   // -0x13
        [WatToken("i31")]      I31         = 0x6C,   // -0x14
        [WatToken("struct")]   Struct      = 0x6B,   // -0x15
        [WatToken("array")]    Array       = 0x6A,   // -0x16
        
        [WatToken("exn")]      Exn         = 0x69,   // -0x17
        [WatToken("noexn")]    NoExn       = 0x74,   // -0x0c
        
        Bot = 0x81, // -0x81
    }
}