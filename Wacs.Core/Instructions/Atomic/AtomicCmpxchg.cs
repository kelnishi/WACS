// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Instructions.Atomic
{
    // Atomic compare-and-swap. Stack: (addr, expected, replacement) → original.
    // Returns the original cell value (zero-extended for sub-words).

    public sealed class InstI32AtomicRmwCmpxchg : InstAtomicCmpxchg32
    {
        public InstI32AtomicRmwCmpxchg() : base((ByteCode)AtomCode.I32AtomicRmwCmpxchg, 4) {}
        protected override int DoCmpxchg(MemoryInstance mem, int ea, int expected, int replacement) =>
            mem.AtomicCompareExchangeInt32(ea, replacement, expected);
    }

    public sealed class InstI64AtomicRmwCmpxchg : InstAtomicCmpxchg64
    {
        public InstI64AtomicRmwCmpxchg() : base((ByteCode)AtomCode.I64AtomicRmwCmpxchg, 8) {}
        protected override long DoCmpxchg(MemoryInstance mem, int ea, long expected, long replacement) =>
            mem.AtomicCompareExchangeInt64(ea, replacement, expected);
    }

    public sealed class InstI32AtomicRmw8CmpxchgU : InstAtomicCmpxchg32
    {
        public InstI32AtomicRmw8CmpxchgU() : base((ByteCode)AtomCode.I32AtomicRmw8CmpxchgU, 1) {}
        protected override int DoCmpxchg(MemoryInstance mem, int ea, int expected, int replacement) =>
            SubwordCas.Cmpxchg(mem, ea, 1, expected, replacement);
    }

    public sealed class InstI32AtomicRmw16CmpxchgU : InstAtomicCmpxchg32
    {
        public InstI32AtomicRmw16CmpxchgU() : base((ByteCode)AtomCode.I32AtomicRmw16CmpxchgU, 2) {}
        protected override int DoCmpxchg(MemoryInstance mem, int ea, int expected, int replacement) =>
            SubwordCas.Cmpxchg(mem, ea, 2, expected, replacement);
    }

    public sealed class InstI64AtomicRmw8CmpxchgU : InstAtomicCmpxchg64
    {
        public InstI64AtomicRmw8CmpxchgU() : base((ByteCode)AtomCode.I64AtomicRmw8CmpxchgU, 1) {}
        protected override long DoCmpxchg(MemoryInstance mem, int ea, long expected, long replacement) =>
            (uint)SubwordCas.Cmpxchg(mem, ea, 1, (int)expected, (int)replacement);
    }

    public sealed class InstI64AtomicRmw16CmpxchgU : InstAtomicCmpxchg64
    {
        public InstI64AtomicRmw16CmpxchgU() : base((ByteCode)AtomCode.I64AtomicRmw16CmpxchgU, 2) {}
        protected override long DoCmpxchg(MemoryInstance mem, int ea, long expected, long replacement) =>
            (uint)SubwordCas.Cmpxchg(mem, ea, 2, (int)expected, (int)replacement);
    }

    public sealed class InstI64AtomicRmw32CmpxchgU : InstAtomicCmpxchg64
    {
        public InstI64AtomicRmw32CmpxchgU() : base((ByteCode)AtomCode.I64AtomicRmw32CmpxchgU, 4) {}
        protected override long DoCmpxchg(MemoryInstance mem, int ea, long expected, long replacement) =>
            (uint)mem.AtomicCompareExchangeInt32(ea, (int)replacement, (int)expected);
    }
}
