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
    [Flags]
    public enum Alignment : byte
    {
        LogBits   = 0b0011_1111, //Mask for alignment value
        MemIdxSet = 0b0100_0000, //bit 6
    }

    public static class AlignmentHelper
    {
        public static int ExpSize(this Alignment alignment)
        {
            int exponent = (int)alignment & (int)Alignment.LogBits;
            if (exponent == 0)
                return 0;
            int linear = 1 << exponent;
            return 1 << linear;
        }

        public static int LinearSize(this Alignment alignment)
        {
            int exponent = (int)alignment & (int)Alignment.LogBits;
            if (exponent == 0)
                return 0;
            return 1 << exponent;
        }

        public static int LogSize(this Alignment alignment)
        {
            return (int)alignment & (int)Alignment.LogBits;
        }
    }
}