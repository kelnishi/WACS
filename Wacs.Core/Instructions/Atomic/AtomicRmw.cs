// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Runtime.CompilerServices;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Instructions.Atomic
{
    // Read-modify-write atomics.
    //
    // Full-width (i32, i64) ops dispatch to the typed helpers on
    // MemoryInstance, which wrap Interlocked.* (Add/And/Or/Exchange/
    // CompareExchange, with #if NET8+ gating on Interlocked.And/Or and
    // CAS loop fallback on netstandard2.1).
    //
    // Sub-word (8-bit, 16-bit) ops CAS-loop on the enclosing 32-bit
    // word because .NET BCL does not expose sub-word Interlocked ops.
    // Spec alignment rules guarantee the enclosing word is in-bounds
    // and aligned.
    //
    // Naming: class names follow the AtomCode enum 1:1. File grouped
    // by op family (add/sub/and/or/xor/xchg).

    public static class SubwordCas
    {
        /// <summary>
        /// CAS loop on a sub-word within the enclosing 32-bit word.
        /// Returns the original sub-word value zero-extended to int.
        /// </summary>
        /// <param name="mem">Target memory.</param>
        /// <param name="ea">Byte-level effective address of the sub-word.</param>
        /// <param name="byteWidth">1 or 2 bytes.</param>
        /// <param name="apply">Transform applied to the old sub-word
        /// value; returned new sub-word value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Loop(MemoryInstance mem, int ea, int byteWidth,
            System.Func<int, int> apply)
        {
            int wordEa = ea & ~3;
            int byteShift = (ea & 3) * 8;
            int bitMask = byteWidth == 1 ? 0xFF : 0xFFFF;
            int wordMask = bitMask << byteShift;

            int old, newWord, oldSub;
            do
            {
                old = mem.AtomicLoadInt32(wordEa);
                oldSub = (old >> byteShift) & bitMask;
                int newSub = apply(oldSub) & bitMask;
                newWord = (old & ~wordMask) | (newSub << byteShift);
            }
            while (mem.AtomicCompareExchangeInt32(wordEa, newWord, old) != old);
            return oldSub;
        }

        /// <summary>
        /// Sub-word cmpxchg: atomically replace if current matches
        /// expected. Returns original (zero-extended).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Cmpxchg(MemoryInstance mem, int ea, int byteWidth,
            int expected, int replacement)
        {
            int wordEa = ea & ~3;
            int byteShift = (ea & 3) * 8;
            int bitMask = byteWidth == 1 ? 0xFF : 0xFFFF;
            int wordMask = bitMask << byteShift;
            int expMasked = expected & bitMask;
            int repMasked = replacement & bitMask;

            while (true)
            {
                int old = mem.AtomicLoadInt32(wordEa);
                int oldSub = (old >> byteShift) & bitMask;
                if (oldSub != expMasked) return oldSub;
                int newWord = (old & ~wordMask) | (repMasked << byteShift);
                if (mem.AtomicCompareExchangeInt32(wordEa, newWord, old) == old)
                    return oldSub;
                // Another writer beat us; retry.
            }
        }
    }

    // ─── Add ─────────────────────────────────────────────────────────
    public sealed class InstI32AtomicRmwAdd : InstAtomicRmw32
    {
        public InstI32AtomicRmwAdd() : base((ByteCode)AtomCode.I32AtomicRmwAdd, 4) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            mem.AtomicAddInt32(ea, arg);
    }

    public sealed class InstI64AtomicRmwAdd : InstAtomicRmw64
    {
        public InstI64AtomicRmwAdd() : base((ByteCode)AtomCode.I64AtomicRmwAdd, 8) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            mem.AtomicAddInt64(ea, arg);
    }

    public sealed class InstI32AtomicRmw8AddU : InstAtomicRmw32
    {
        public InstI32AtomicRmw8AddU() : base((ByteCode)AtomCode.I32AtomicRmw8AddU, 1) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            SubwordCas.Loop(mem, ea, 1, old => old + arg);
    }

    public sealed class InstI32AtomicRmw16AddU : InstAtomicRmw32
    {
        public InstI32AtomicRmw16AddU() : base((ByteCode)AtomCode.I32AtomicRmw16AddU, 2) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            SubwordCas.Loop(mem, ea, 2, old => old + arg);
    }

    public sealed class InstI64AtomicRmw8AddU : InstAtomicRmw64
    {
        public InstI64AtomicRmw8AddU() : base((ByteCode)AtomCode.I64AtomicRmw8AddU, 1) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)SubwordCas.Loop(mem, ea, 1, old => old + (int)arg);
    }

    public sealed class InstI64AtomicRmw16AddU : InstAtomicRmw64
    {
        public InstI64AtomicRmw16AddU() : base((ByteCode)AtomCode.I64AtomicRmw16AddU, 2) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)SubwordCas.Loop(mem, ea, 2, old => old + (int)arg);
    }

    public sealed class InstI64AtomicRmw32AddU : InstAtomicRmw64
    {
        public InstI64AtomicRmw32AddU() : base((ByteCode)AtomCode.I64AtomicRmw32AddU, 4) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)mem.AtomicAddInt32(ea, (int)arg);
    }

    // ─── Sub ─────────────────────────────────────────────────────────
    // sub = add(-arg). .NET Interlocked has no native Sub.

    public sealed class InstI32AtomicRmwSub : InstAtomicRmw32
    {
        public InstI32AtomicRmwSub() : base((ByteCode)AtomCode.I32AtomicRmwSub, 4) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            mem.AtomicAddInt32(ea, -arg);
    }

    public sealed class InstI64AtomicRmwSub : InstAtomicRmw64
    {
        public InstI64AtomicRmwSub() : base((ByteCode)AtomCode.I64AtomicRmwSub, 8) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            mem.AtomicAddInt64(ea, -arg);
    }

    public sealed class InstI32AtomicRmw8SubU : InstAtomicRmw32
    {
        public InstI32AtomicRmw8SubU() : base((ByteCode)AtomCode.I32AtomicRmw8SubU, 1) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            SubwordCas.Loop(mem, ea, 1, old => old - arg);
    }

    public sealed class InstI32AtomicRmw16SubU : InstAtomicRmw32
    {
        public InstI32AtomicRmw16SubU() : base((ByteCode)AtomCode.I32AtomicRmw16SubU, 2) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            SubwordCas.Loop(mem, ea, 2, old => old - arg);
    }

    public sealed class InstI64AtomicRmw8SubU : InstAtomicRmw64
    {
        public InstI64AtomicRmw8SubU() : base((ByteCode)AtomCode.I64AtomicRmw8SubU, 1) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)SubwordCas.Loop(mem, ea, 1, old => old - (int)arg);
    }

    public sealed class InstI64AtomicRmw16SubU : InstAtomicRmw64
    {
        public InstI64AtomicRmw16SubU() : base((ByteCode)AtomCode.I64AtomicRmw16SubU, 2) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)SubwordCas.Loop(mem, ea, 2, old => old - (int)arg);
    }

    public sealed class InstI64AtomicRmw32SubU : InstAtomicRmw64
    {
        public InstI64AtomicRmw32SubU() : base((ByteCode)AtomCode.I64AtomicRmw32SubU, 4) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)mem.AtomicAddInt32(ea, (int)(-arg));
    }

    // ─── And ─────────────────────────────────────────────────────────
    public sealed class InstI32AtomicRmwAnd : InstAtomicRmw32
    {
        public InstI32AtomicRmwAnd() : base((ByteCode)AtomCode.I32AtomicRmwAnd, 4) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            mem.AtomicAndInt32(ea, arg);
    }

    public sealed class InstI64AtomicRmwAnd : InstAtomicRmw64
    {
        public InstI64AtomicRmwAnd() : base((ByteCode)AtomCode.I64AtomicRmwAnd, 8) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            mem.AtomicAndInt64(ea, arg);
    }

    public sealed class InstI32AtomicRmw8AndU : InstAtomicRmw32
    {
        public InstI32AtomicRmw8AndU() : base((ByteCode)AtomCode.I32AtomicRmw8AndU, 1) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            SubwordCas.Loop(mem, ea, 1, old => old & arg);
    }

    public sealed class InstI32AtomicRmw16AndU : InstAtomicRmw32
    {
        public InstI32AtomicRmw16AndU() : base((ByteCode)AtomCode.I32AtomicRmw16AndU, 2) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            SubwordCas.Loop(mem, ea, 2, old => old & arg);
    }

    public sealed class InstI64AtomicRmw8AndU : InstAtomicRmw64
    {
        public InstI64AtomicRmw8AndU() : base((ByteCode)AtomCode.I64AtomicRmw8AndU, 1) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)SubwordCas.Loop(mem, ea, 1, old => old & (int)arg);
    }

    public sealed class InstI64AtomicRmw16AndU : InstAtomicRmw64
    {
        public InstI64AtomicRmw16AndU() : base((ByteCode)AtomCode.I64AtomicRmw16AndU, 2) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)SubwordCas.Loop(mem, ea, 2, old => old & (int)arg);
    }

    public sealed class InstI64AtomicRmw32AndU : InstAtomicRmw64
    {
        public InstI64AtomicRmw32AndU() : base((ByteCode)AtomCode.I64AtomicRmw32AndU, 4) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)mem.AtomicAndInt32(ea, (int)arg);
    }

    // ─── Or ──────────────────────────────────────────────────────────
    public sealed class InstI32AtomicRmwOr : InstAtomicRmw32
    {
        public InstI32AtomicRmwOr() : base((ByteCode)AtomCode.I32AtomicRmwOr, 4) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            mem.AtomicOrInt32(ea, arg);
    }

    public sealed class InstI64AtomicRmwOr : InstAtomicRmw64
    {
        public InstI64AtomicRmwOr() : base((ByteCode)AtomCode.I64AtomicRmwOr, 8) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            mem.AtomicOrInt64(ea, arg);
    }

    public sealed class InstI32AtomicRmw8OrU : InstAtomicRmw32
    {
        public InstI32AtomicRmw8OrU() : base((ByteCode)AtomCode.I32AtomicRmw8OrU, 1) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            SubwordCas.Loop(mem, ea, 1, old => old | arg);
    }

    public sealed class InstI32AtomicRmw16OrU : InstAtomicRmw32
    {
        public InstI32AtomicRmw16OrU() : base((ByteCode)AtomCode.I32AtomicRmw16OrU, 2) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            SubwordCas.Loop(mem, ea, 2, old => old | arg);
    }

    public sealed class InstI64AtomicRmw8OrU : InstAtomicRmw64
    {
        public InstI64AtomicRmw8OrU() : base((ByteCode)AtomCode.I64AtomicRmw8OrU, 1) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)SubwordCas.Loop(mem, ea, 1, old => old | (int)arg);
    }

    public sealed class InstI64AtomicRmw16OrU : InstAtomicRmw64
    {
        public InstI64AtomicRmw16OrU() : base((ByteCode)AtomCode.I64AtomicRmw16OrU, 2) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)SubwordCas.Loop(mem, ea, 2, old => old | (int)arg);
    }

    public sealed class InstI64AtomicRmw32OrU : InstAtomicRmw64
    {
        public InstI64AtomicRmw32OrU() : base((ByteCode)AtomCode.I64AtomicRmw32OrU, 4) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)mem.AtomicOrInt32(ea, (int)arg);
    }

    // ─── Xor ─────────────────────────────────────────────────────────
    public sealed class InstI32AtomicRmwXor : InstAtomicRmw32
    {
        public InstI32AtomicRmwXor() : base((ByteCode)AtomCode.I32AtomicRmwXor, 4) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            mem.AtomicXorInt32(ea, arg);
    }

    public sealed class InstI64AtomicRmwXor : InstAtomicRmw64
    {
        public InstI64AtomicRmwXor() : base((ByteCode)AtomCode.I64AtomicRmwXor, 8) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            mem.AtomicXorInt64(ea, arg);
    }

    public sealed class InstI32AtomicRmw8XorU : InstAtomicRmw32
    {
        public InstI32AtomicRmw8XorU() : base((ByteCode)AtomCode.I32AtomicRmw8XorU, 1) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            SubwordCas.Loop(mem, ea, 1, old => old ^ arg);
    }

    public sealed class InstI32AtomicRmw16XorU : InstAtomicRmw32
    {
        public InstI32AtomicRmw16XorU() : base((ByteCode)AtomCode.I32AtomicRmw16XorU, 2) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            SubwordCas.Loop(mem, ea, 2, old => old ^ arg);
    }

    public sealed class InstI64AtomicRmw8XorU : InstAtomicRmw64
    {
        public InstI64AtomicRmw8XorU() : base((ByteCode)AtomCode.I64AtomicRmw8XorU, 1) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)SubwordCas.Loop(mem, ea, 1, old => old ^ (int)arg);
    }

    public sealed class InstI64AtomicRmw16XorU : InstAtomicRmw64
    {
        public InstI64AtomicRmw16XorU() : base((ByteCode)AtomCode.I64AtomicRmw16XorU, 2) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)SubwordCas.Loop(mem, ea, 2, old => old ^ (int)arg);
    }

    public sealed class InstI64AtomicRmw32XorU : InstAtomicRmw64
    {
        public InstI64AtomicRmw32XorU() : base((ByteCode)AtomCode.I64AtomicRmw32XorU, 4) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)mem.AtomicXorInt32(ea, (int)arg);
    }

    // ─── Xchg ────────────────────────────────────────────────────────
    public sealed class InstI32AtomicRmwXchg : InstAtomicRmw32
    {
        public InstI32AtomicRmwXchg() : base((ByteCode)AtomCode.I32AtomicRmwXchg, 4) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            mem.AtomicExchangeInt32(ea, arg);
    }

    public sealed class InstI64AtomicRmwXchg : InstAtomicRmw64
    {
        public InstI64AtomicRmwXchg() : base((ByteCode)AtomCode.I64AtomicRmwXchg, 8) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            mem.AtomicExchangeInt64(ea, arg);
    }

    public sealed class InstI32AtomicRmw8XchgU : InstAtomicRmw32
    {
        public InstI32AtomicRmw8XchgU() : base((ByteCode)AtomCode.I32AtomicRmw8XchgU, 1) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            SubwordCas.Loop(mem, ea, 1, _ => arg);
    }

    public sealed class InstI32AtomicRmw16XchgU : InstAtomicRmw32
    {
        public InstI32AtomicRmw16XchgU() : base((ByteCode)AtomCode.I32AtomicRmw16XchgU, 2) {}
        protected override int DoRmw(MemoryInstance mem, int ea, int arg) =>
            SubwordCas.Loop(mem, ea, 2, _ => arg);
    }

    public sealed class InstI64AtomicRmw8XchgU : InstAtomicRmw64
    {
        public InstI64AtomicRmw8XchgU() : base((ByteCode)AtomCode.I64AtomicRmw8XchgU, 1) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg)
        {
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 1, _ => a);
        }
    }

    public sealed class InstI64AtomicRmw16XchgU : InstAtomicRmw64
    {
        public InstI64AtomicRmw16XchgU() : base((ByteCode)AtomCode.I64AtomicRmw16XchgU, 2) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg)
        {
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 2, _ => a);
        }
    }

    public sealed class InstI64AtomicRmw32XchgU : InstAtomicRmw64
    {
        public InstI64AtomicRmw32XchgU() : base((ByteCode)AtomCode.I64AtomicRmw32XchgU, 4) {}
        protected override long DoRmw(MemoryInstance mem, int ea, long arg) =>
            (uint)mem.AtomicExchangeInt32(ea, (int)arg);
    }
}
