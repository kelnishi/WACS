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
    public enum AddrType : byte
    {
        I32 = 32,
        I64 = 64,
    }
    
    public static class AddrTypeExtensions
    {
        public static AddrType Min(this AddrType type, AddrType other) => type <= other ? type : other;

        public static ValType ToValType(this AddrType type) => type switch
        {
            AddrType.I32 => ValType.I32,
            AddrType.I64 => ValType.I64,
            _ => throw new NotImplementedException()
        };
    }
}