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
    public enum LimitsFlag : byte
    {
        Mem32Min = 0x00,
        Mem32MinMax = 0x01,
        
        Mem64Min = 0x04,
        Mem64MinMax = 0x05,
    }
}