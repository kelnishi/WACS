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

using System.IO;
using Wacs.Core.Instructions.Atomic;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Instructions
{
    public partial class SpecFactory
    {
        public static InstructionBase? CreateInstruction(AtomCode opcode) => opcode switch
        {
            // Blocking + fence primitives
            AtomCode.MemoryAtomicNotify       => new InstMemoryAtomicNotify(),
            AtomCode.MemoryAtomicWait32       => new InstMemoryAtomicWait32(),
            AtomCode.MemoryAtomicWait64       => new InstMemoryAtomicWait64(),
            AtomCode.AtomicFence              => new InstAtomicFence(),

            // Loads
            AtomCode.I32AtomicLoad            => new InstI32AtomicLoad(),
            AtomCode.I64AtomicLoad            => new InstI64AtomicLoad(),
            AtomCode.I32AtomicLoad8U          => new InstI32AtomicLoad8U(),
            AtomCode.I32AtomicLoad16U         => new InstI32AtomicLoad16U(),
            AtomCode.I64AtomicLoad8U          => new InstI64AtomicLoad8U(),
            AtomCode.I64AtomicLoad16U         => new InstI64AtomicLoad16U(),
            AtomCode.I64AtomicLoad32U         => new InstI64AtomicLoad32U(),

            // Stores
            AtomCode.I32AtomicStore           => new InstI32AtomicStore(),
            AtomCode.I64AtomicStore           => new InstI64AtomicStore(),
            AtomCode.I32AtomicStore8          => new InstI32AtomicStore8(),
            AtomCode.I32AtomicStore16         => new InstI32AtomicStore16(),
            AtomCode.I64AtomicStore8          => new InstI64AtomicStore8(),
            AtomCode.I64AtomicStore16         => new InstI64AtomicStore16(),
            AtomCode.I64AtomicStore32         => new InstI64AtomicStore32(),

            // RMW add
            AtomCode.I32AtomicRmwAdd          => new InstI32AtomicRmwAdd(),
            AtomCode.I64AtomicRmwAdd          => new InstI64AtomicRmwAdd(),
            AtomCode.I32AtomicRmw8AddU        => new InstI32AtomicRmw8AddU(),
            AtomCode.I32AtomicRmw16AddU       => new InstI32AtomicRmw16AddU(),
            AtomCode.I64AtomicRmw8AddU        => new InstI64AtomicRmw8AddU(),
            AtomCode.I64AtomicRmw16AddU       => new InstI64AtomicRmw16AddU(),
            AtomCode.I64AtomicRmw32AddU       => new InstI64AtomicRmw32AddU(),

            // RMW sub
            AtomCode.I32AtomicRmwSub          => new InstI32AtomicRmwSub(),
            AtomCode.I64AtomicRmwSub          => new InstI64AtomicRmwSub(),
            AtomCode.I32AtomicRmw8SubU        => new InstI32AtomicRmw8SubU(),
            AtomCode.I32AtomicRmw16SubU       => new InstI32AtomicRmw16SubU(),
            AtomCode.I64AtomicRmw8SubU        => new InstI64AtomicRmw8SubU(),
            AtomCode.I64AtomicRmw16SubU       => new InstI64AtomicRmw16SubU(),
            AtomCode.I64AtomicRmw32SubU       => new InstI64AtomicRmw32SubU(),

            // RMW and
            AtomCode.I32AtomicRmwAnd          => new InstI32AtomicRmwAnd(),
            AtomCode.I64AtomicRmwAnd          => new InstI64AtomicRmwAnd(),
            AtomCode.I32AtomicRmw8AndU        => new InstI32AtomicRmw8AndU(),
            AtomCode.I32AtomicRmw16AndU       => new InstI32AtomicRmw16AndU(),
            AtomCode.I64AtomicRmw8AndU        => new InstI64AtomicRmw8AndU(),
            AtomCode.I64AtomicRmw16AndU       => new InstI64AtomicRmw16AndU(),
            AtomCode.I64AtomicRmw32AndU       => new InstI64AtomicRmw32AndU(),

            // RMW or
            AtomCode.I32AtomicRmwOr           => new InstI32AtomicRmwOr(),
            AtomCode.I64AtomicRmwOr           => new InstI64AtomicRmwOr(),
            AtomCode.I32AtomicRmw8OrU         => new InstI32AtomicRmw8OrU(),
            AtomCode.I32AtomicRmw16OrU        => new InstI32AtomicRmw16OrU(),
            AtomCode.I64AtomicRmw8OrU         => new InstI64AtomicRmw8OrU(),
            AtomCode.I64AtomicRmw16OrU        => new InstI64AtomicRmw16OrU(),
            AtomCode.I64AtomicRmw32OrU        => new InstI64AtomicRmw32OrU(),

            // RMW xor
            AtomCode.I32AtomicRmwXor          => new InstI32AtomicRmwXor(),
            AtomCode.I64AtomicRmwXor          => new InstI64AtomicRmwXor(),
            AtomCode.I32AtomicRmw8XorU        => new InstI32AtomicRmw8XorU(),
            AtomCode.I32AtomicRmw16XorU       => new InstI32AtomicRmw16XorU(),
            AtomCode.I64AtomicRmw8XorU        => new InstI64AtomicRmw8XorU(),
            AtomCode.I64AtomicRmw16XorU       => new InstI64AtomicRmw16XorU(),
            AtomCode.I64AtomicRmw32XorU       => new InstI64AtomicRmw32XorU(),

            // RMW xchg
            AtomCode.I32AtomicRmwXchg         => new InstI32AtomicRmwXchg(),
            AtomCode.I64AtomicRmwXchg         => new InstI64AtomicRmwXchg(),
            AtomCode.I32AtomicRmw8XchgU       => new InstI32AtomicRmw8XchgU(),
            AtomCode.I32AtomicRmw16XchgU      => new InstI32AtomicRmw16XchgU(),
            AtomCode.I64AtomicRmw8XchgU       => new InstI64AtomicRmw8XchgU(),
            AtomCode.I64AtomicRmw16XchgU      => new InstI64AtomicRmw16XchgU(),
            AtomCode.I64AtomicRmw32XchgU      => new InstI64AtomicRmw32XchgU(),

            // Cmpxchg
            AtomCode.I32AtomicRmwCmpxchg      => new InstI32AtomicRmwCmpxchg(),
            AtomCode.I64AtomicRmwCmpxchg      => new InstI64AtomicRmwCmpxchg(),
            AtomCode.I32AtomicRmw8CmpxchgU    => new InstI32AtomicRmw8CmpxchgU(),
            AtomCode.I32AtomicRmw16CmpxchgU   => new InstI32AtomicRmw16CmpxchgU(),
            AtomCode.I64AtomicRmw8CmpxchgU    => new InstI64AtomicRmw8CmpxchgU(),
            AtomCode.I64AtomicRmw16CmpxchgU   => new InstI64AtomicRmw16CmpxchgU(),
            AtomCode.I64AtomicRmw32CmpxchgU   => new InstI64AtomicRmw32CmpxchgU(),

            _ => throw new InvalidDataException(
                $"Unsupported instruction {opcode.GetMnemonic()}. ByteCode: 0xFE{(byte)opcode:X2}")
        };
    }
}
