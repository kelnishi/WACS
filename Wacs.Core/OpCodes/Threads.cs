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

using Wacs.Core.Attributes;

namespace Wacs.Core.OpCodes
{
    /// <summary>
    /// Represents all WebAssembly opcodes for the FE prefix
    /// Theoretically, these could be u32, but I'll keep them as bytes so long as they fit.
    /// </summary>
    public enum AtomCode : byte
    {
        [OpCode("memory.atomic.notify")]       MemoryAtomicNotify     = 0x00,
        [OpCode("memory.atomic.wait32")]       MemoryAtomicWait32     = 0x01,
        [OpCode("memory.atomic.wait64")]       MemoryAtomicWait64     = 0x02,
        [OpCode("atomic.fence")]               AtomicFence            = 0x03,
        [OpCode("i32.atomic.load")]            I32AtomicLoad          = 0x10,
        [OpCode("i64.atomic.load")]            I64AtomicLoad          = 0x11,
        [OpCode("i32.atomic.load8_u")]         I32AtomicLoad8U        = 0x12,
        [OpCode("i32.atomic.load16_u")]        I32AtomicLoad16U       = 0x13,
        [OpCode("i64.atomic.load8_u")]         I64AtomicLoad8U        = 0x14,
        [OpCode("i64.atomic.load16_u")]        I64AtomicLoad16U       = 0x15,
        [OpCode("i64.atomic.load32_u")]        I64AtomicLoad32U       = 0x16,
        [OpCode("i32.atomic.store")]           I32AtomicStore         = 0x17,
        [OpCode("i64.atomic.store")]           I64AtomicStore         = 0x18,
        [OpCode("i32.atomic.store8")]          I32AtomicStore8        = 0x19,
        [OpCode("i32.atomic.store16")]         I32AtomicStore16       = 0x1A,
        [OpCode("i64.atomic.store8")]          I64AtomicStore8        = 0x1B,
        [OpCode("i64.atomic.store16")]         I64AtomicStore16       = 0x1C,
        [OpCode("i64.atomic.store32")]         I64AtomicStore32       = 0x1D,
        [OpCode("i32.atomic.rmw.add")]         I32AtomicRmwAdd        = 0x1E,
        [OpCode("i64.atomic.rmw.add")]         I64AtomicRmwAdd        = 0x1F,
        [OpCode("i32.atomic.rmw8.add_u")]      I32AtomicRmw8AddU      = 0x20,
        [OpCode("i32.atomic.rmw16.add_u")]     I32AtomicRmw16AddU     = 0x21,
        [OpCode("i64.atomic.rmw8.add_u")]      I64AtomicRmw8AddU      = 0x22,
        [OpCode("i64.atomic.rmw16.add_u")]     I64AtomicRmw16AddU     = 0x23,
        [OpCode("i64.atomic.rmw32.add_u")]     I64AtomicRmw32AddU     = 0x24,
        [OpCode("i32.atomic.rmw.sub")]         I32AtomicRmwSub        = 0x25,
        [OpCode("i64.atomic.rmw.sub")]         I64AtomicRmwSub        = 0x26,
        [OpCode("i32.atomic.rmw8.sub_u")]      I32AtomicRmw8SubU      = 0x27,
        [OpCode("i32.atomic.rmw16.sub_u")]     I32AtomicRmw16SubU     = 0x28,
        [OpCode("i64.atomic.rmw8.sub_u")]      I64AtomicRmw8SubU      = 0x29,
        [OpCode("i64.atomic.rmw16.sub_u")]     I64AtomicRmw16SubU     = 0x2A,
        [OpCode("i64.atomic.rmw32.sub_u")]     I64AtomicRmw32SubU     = 0x2B,
        [OpCode("i32.atomic.rmw.and")]         I32AtomicRmwAnd        = 0x2C,
        [OpCode("i64.atomic.rmw.and")]         I64AtomicRmwAnd        = 0x2D,
        [OpCode("i32.atomic.rmw8.and_u")]      I32AtomicRmw8AndU      = 0x2E,
        [OpCode("i32.atomic.rmw16.and_u")]     I32AtomicRmw16AndU     = 0x2F,
        [OpCode("i64.atomic.rmw8.and_u")]      I64AtomicRmw8AndU      = 0x30,
        [OpCode("i64.atomic.rmw16.and_u")]     I64AtomicRmw16AndU     = 0x31,
        [OpCode("i64.atomic.rmw32.and_u")]     I64AtomicRmw32AndU     = 0x32,
        [OpCode("i32.atomic.rmw.or")]          I32AtomicRmwOr         = 0x33,
        [OpCode("i64.atomic.rmw.or")]          I64AtomicRmwOr         = 0x34,
        [OpCode("i32.atomic.rmw8.or_u")]       I32AtomicRmw8OrU       = 0x35,
        [OpCode("i32.atomic.rmw16.or_u")]      I32AtomicRmw16OrU      = 0x36,
        [OpCode("i64.atomic.rmw8.or_u")]       I64AtomicRmw8OrU       = 0x37,
        [OpCode("i64.atomic.rmw16.or_u")]      I64AtomicRmw16OrU      = 0x38,
        [OpCode("i64.atomic.rmw32.or_u")]      I64AtomicRmw32OrU      = 0x39,
        [OpCode("i32.atomic.rmw.xor")]         I32AtomicRmwXor        = 0x3A,
        [OpCode("i64.atomic.rmw.xor")]         I64AtomicRmwXor        = 0x3B,
        [OpCode("i32.atomic.rmw8.xor_u")]      I32AtomicRmw8XorU      = 0x3C,
        [OpCode("i32.atomic.rmw16.xor_u")]     I32AtomicRmw16XorU     = 0x3D,
        [OpCode("i64.atomic.rmw8.xor_u")]      I64AtomicRmw8XorU      = 0x3E,
        [OpCode("i64.atomic.rmw16.xor_u")]     I64AtomicRmw16XorU     = 0x3F,
        [OpCode("i64.atomic.rmw32.xor_u")]     I64AtomicRmw32XorU     = 0x40,
        [OpCode("i32.atomic.rmw.xchg")]        I32AtomicRmwXchg       = 0x41,
        [OpCode("i64.atomic.rmw.xchg")]        I64AtomicRmwXchg       = 0x42,
        [OpCode("i32.atomic.rmw8.xchg_u")]     I32AtomicRmw8XchgU     = 0x43,
        [OpCode("i32.atomic.rmw16.xchg_u")]    I32AtomicRmw16XchgU    = 0x44,
        [OpCode("i64.atomic.rmw8.xchg_u")]     I64AtomicRmw8XchgU     = 0x45,
        [OpCode("i64.atomic.rmw16.xchg_u")]    I64AtomicRmw16XchgU    = 0x46,
        [OpCode("i64.atomic.rmw32.xchg_u")]    I64AtomicRmw32XchgU    = 0x47,
        [OpCode("i32.atomic.rmw.cmpxchg")]     I32AtomicRmwCmpxchg    = 0x48,
        [OpCode("i64.atomic.rmw.cmpxchg")]     I64AtomicRmwCmpxchg    = 0x49,
        [OpCode("i32.atomic.rmw8.cmpxchg_u")]  I32AtomicRmw8CmpxchgU  = 0x4A,
        [OpCode("i32.atomic.rmw16.cmpxchg_u")] I32AtomicRmw16CmpxchgU = 0x4B,
        [OpCode("i64.atomic.rmw8.cmpxchg_u")]  I64AtomicRmw8CmpxchgU  = 0x4C,
        [OpCode("i64.atomic.rmw16.cmpxchg_u")] I64AtomicRmw16CmpxchgU = 0x4D,
        [OpCode("i64.atomic.rmw32.cmpxchg_u")] I64AtomicRmw32CmpxchgU = 0x4E,
    }

}