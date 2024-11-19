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

using System;

namespace Wacs.Core.Types
{
    public enum BitWidth : short
    {
        S8 = -8,
        S16 = -16,
        S32 = -32,

        U8 = 8,
        U16 = 16,
        U32 = 32,
        U64 = 64,
        
        V128 = 128,
    }

    public static class BitWidthHelpers
    {
        public static int ByteSize(this BitWidth width) =>
            width switch
            {
                BitWidth.S8 => 1,
                BitWidth.S16 => 2,
                BitWidth.S32 => 4,
                BitWidth.U8 => 1,
                BitWidth.U16 => 2,
                BitWidth.U32 => 4,
                BitWidth.U64 => 8,
                BitWidth.V128 => 16,
                _ => (short)width / 8
            };

        public static int BitSize(this BitWidth width) =>
            width switch
            {
                BitWidth.S8 => 8,
                BitWidth.S16 => 16,
                BitWidth.S32 => 32,
                BitWidth.U8 => 8,
                BitWidth.U16 => 16,
                BitWidth.U32 => 32,
                BitWidth.U64 => 64,
                BitWidth.V128 => 128,
                _ => Math.Abs((short)width)
            };
    }
}