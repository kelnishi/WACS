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

namespace Wacs.Core.OpCodes
{
    /// <summary>
    /// Administrative OpCodes used by WACS
    /// </summary>
    public enum WacsCode : byte
    {
        [OpCode("stack.val")]      StackVal    = 0x10,
        [OpCode("stack.i32")]     StackI32    = 0x11,
        [OpCode("stack.u32")]     StackU32    = 0x12,

        [OpCode("aggr.1x0")] Aggr1_0     = 0x20,
        [OpCode("aggr.1x1")] Aggr1_1     = 0x21,
        [OpCode("aggr.2x0")] Aggr2_0     = 0x22,
        [OpCode("aggr.2x1")] Aggr2_1     = 0x23,
        [OpCode("aggr.3x1")] Aggr3_1     = 0x24,

        [OpCode("i32.fused.add")] I32FusedAdd = 0x30,
        [OpCode("i32.fused.sub")] I32FusedSub = 0x31,
        [OpCode("i32.fused.mul")] I32FusedMul = 0x32,
        [OpCode("i32.fused.and")] I32FusedAnd = 0x33,
        [OpCode("i32.fused.or")]  I32FusedOr  = 0x34,
        
        [OpCode("i64.fused.add")] I64FusedAdd = 0x38,
        [OpCode("i64.fused.sub")] I64FusedSub = 0x39,
        [OpCode("i64.fused.mul")] I64FusedMul = 0x3A,
        [OpCode("i64.fused.and")] I64FusedAnd = 0x3B,
        [OpCode("i64.fused.or")]  I64FusedOr  = 0x3C,
        
        [OpCode("local.getset")] LocalGetSet = 0x40,
        [OpCode("local.constset")] LocalConstSet = 0x41,
        
        [OpCode("catch")] Catch = 0x69,
    }
}