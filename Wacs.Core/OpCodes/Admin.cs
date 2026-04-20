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
        [OpCode("local.i64constset")] LocalI64ConstSet = 0x42,

        // Local-local 3-op arithmetic fusions: `local.get a; local.get b; i32.<op>`.
        // Encoding: [FF][code][idxA:4][idxB:4] = 10 bytes.
        [OpCode("i32.lladd")] I32LLAdd = 0x50,
        [OpCode("i32.llsub")] I32LLSub = 0x51,
        [OpCode("i32.llmul")] I32LLMul = 0x52,
        [OpCode("i32.lland")] I32LLAnd = 0x53,
        [OpCode("i32.llor")]  I32LLOr  = 0x54,
        [OpCode("i32.llxor")] I32LLXor = 0x55,

        // Local-local 3-op relational fusions (i32). Same encoding as arith.
        [OpCode("i32.lleq")]   I32LLEq   = 0x58,
        [OpCode("i32.llne")]   I32LLNe   = 0x59,
        [OpCode("i32.lllts")]  I32LLLtS  = 0x5A,
        [OpCode("i32.lllgu")]  I32LLLtU  = 0x5B,
        [OpCode("i32.llgts")]  I32LLGtS  = 0x5C,
        [OpCode("i32.llgtu")]  I32LLGtU  = 0x5D,
        [OpCode("i32.llles")]  I32LLLeS  = 0x5E,
        [OpCode("i32.llleu")]  I32LLLeU  = 0x5F,
        [OpCode("i32.llges")]  I32LLGeS  = 0x60,
        [OpCode("i32.llgeu")]  I32LLGeU  = 0x61,

        // Local-local 3-op i64 arith fusions.
        [OpCode("i64.lladd")] I64LLAdd = 0x68,

        [OpCode("catch")] Catch = 0x69,

        [OpCode("i64.llmul")] I64LLMul = 0x6A,

        // Local + i64.extend_i32_s (2-op). Encoding: [FF][code][idx:4] = 6 bytes.
        [OpCode("i64.extendi32s.l")] I64ExtendI32SL = 0x70,

        // Register-program super-op (prototype). Encodes an arbitrary-depth
        // pure-arith subtree as an inner bytecode executed against a
        // register file of 8 ulong locals. Stream format:
        //   [FF][RegProg][nInputs:u8][nOutputs:u8][microByteCount:u16]
        //   [outputRegs:u8 * nOutputs]
        //   [micro bytecode: microByteCount bytes]
        //
        // See DispatchGenerator's EmitRegProgCase for the microop ISA and
        // inner dispatch. Intent: collapse "stack traffic" on deep
        // expression subtrees that StreamFusePass's fixed-depth patterns
        // can't cover.
        [OpCode("reg.prog")] RegProg = 0x80,
    }
}