// Copyright 2025 Kelvin Nishikawa
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

using System;

namespace Wacs.Core.Types.Defs
{
    [Flags]
    public enum LimitsFlag : byte
    {
        HasMax = 0x01,
        IsShared = 0x02 | HasMax, // Shared is only valid if there is a maximum
        Is64Bit = 0x04,
        
        Mem32Min = 0x00,
        Mem32MinMax = Mem32Min | HasMax,
        Mem32MinMaxShared = Mem32MinMax | IsShared,
        
        Mem64Min = Is64Bit,
        Mem64MinMax = Is64Bit | HasMax,
        Mem64MinMaxShared = Mem64MinMax | IsShared,
    }
}