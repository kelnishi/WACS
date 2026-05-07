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

        public const uint WasmMaxPages = 0x1_00_00; //2^16 (Spec allows up to 4GB for 32-bit memories)
        public const long WasmMaxPages64 = 0x1_0000_0000_0000L; //2^48 (Spec allows up to 2^64 bytes for 64-bit memories)
        public const uint HostMaxPages = 0x0_80_00; //2^15 32K (C# generally only accomodates 2GB array)

        //Table
        // Runtime cap on List<>-backed table storage. Spec K (3.2.4)
        // is 2^32 for table32 and 2^64 for table64; the validator handles
        // those bounds independently. This constant is the .NET-side
        // ceiling that gates Grow / instantiation.
        public const long MaxTableSize = 0xFFFF_FFFFL; //runtime cap: 2^32 - 1
    }
}