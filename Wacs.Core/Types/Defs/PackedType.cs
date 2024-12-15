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

namespace Wacs.Core.Types.Defs
{
    public enum PackedType : byte
    {
        NotPacked = 0x79,
        
        I8  = 0x78, // -0x08
        I16 = 0x77, // -0x09
        
        S8  = I8,
        S16 = I16,
        U8  = 0x76,
        U16 = 0x75,
    }
}