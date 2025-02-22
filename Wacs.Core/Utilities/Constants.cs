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

namespace Wacs.Core.Utilities
{
    public static class Constants
    {
        public const long TwoTo32 = 0x1_0000_0000;

        //Memory
        public const uint PageSize = 0x1_00_00; //64Ki

        public const uint WasmMaxPages = 0x1_00_00; //2^16 64K (Spec allows up to 4GB for 32bit)
        public const uint HostMaxPages = 0x0_80_00; //2^15 32K (C# generally only accomodates 2GB array)

        //Table
        public const uint MaxTableSize = 0xFFFF_FFFF; //2^32 - 1
    }
}