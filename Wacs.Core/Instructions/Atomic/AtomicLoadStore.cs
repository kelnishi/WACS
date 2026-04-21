// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Runtime.CompilerServices;
using System.Threading;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.Atomic
{
    // Loads ──────────────────────────────────────────────────────────────
    // i32/i64 full-width loads use the typed AtomicLoadInt32/Int64
    // helpers on MemoryInstance (Volatile.Read / Interlocked.Read under
    // the hood). Subword loads use Volatile.Read directly on a byte or
    // short ref — aligned sub-word reads are atomic on all supported
    // architectures, so no CAS loop is needed.

    public sealed class InstI32AtomicLoad : InstAtomicLoad
    {
        public InstI32AtomicLoad() : base((ByteCode)AtomCode.I32AtomicLoad, ValType.I32, 4) {}
        protected override void DoLoad(ExecContext context, int ea) =>
            context.OpStack.PushI32(CachedMem.AtomicLoadInt32(ea));
    }

    public sealed class InstI64AtomicLoad : InstAtomicLoad
    {
        public InstI64AtomicLoad() : base((ByteCode)AtomCode.I64AtomicLoad, ValType.I64, 8) {}
        protected override void DoLoad(ExecContext context, int ea) =>
            context.OpStack.PushI64(CachedMem.AtomicLoadInt64(ea));
    }

    public sealed class InstI32AtomicLoad8U : InstAtomicLoad
    {
        public InstI32AtomicLoad8U() : base((ByteCode)AtomCode.I32AtomicLoad8U, ValType.I32, 1) {}
        protected override void DoLoad(ExecContext context, int ea) =>
            context.OpStack.PushU32(Volatile.Read(ref CachedMem.Data[ea]));
    }

    public sealed class InstI32AtomicLoad16U : InstAtomicLoad
    {
        public InstI32AtomicLoad16U() : base((ByteCode)AtomCode.I32AtomicLoad16U, ValType.I32, 2) {}
        protected override void DoLoad(ExecContext context, int ea)
        {
            ref ushort cell = ref Unsafe.As<byte, ushort>(ref CachedMem.Data[ea]);
            context.OpStack.PushU32(Volatile.Read(ref cell));
        }
    }

    public sealed class InstI64AtomicLoad8U : InstAtomicLoad
    {
        public InstI64AtomicLoad8U() : base((ByteCode)AtomCode.I64AtomicLoad8U, ValType.I64, 1) {}
        protected override void DoLoad(ExecContext context, int ea) =>
            context.OpStack.PushU64(Volatile.Read(ref CachedMem.Data[ea]));
    }

    public sealed class InstI64AtomicLoad16U : InstAtomicLoad
    {
        public InstI64AtomicLoad16U() : base((ByteCode)AtomCode.I64AtomicLoad16U, ValType.I64, 2) {}
        protected override void DoLoad(ExecContext context, int ea)
        {
            ref ushort cell = ref Unsafe.As<byte, ushort>(ref CachedMem.Data[ea]);
            context.OpStack.PushU64(Volatile.Read(ref cell));
        }
    }

    public sealed class InstI64AtomicLoad32U : InstAtomicLoad
    {
        public InstI64AtomicLoad32U() : base((ByteCode)AtomCode.I64AtomicLoad32U, ValType.I64, 4) {}
        protected override void DoLoad(ExecContext context, int ea)
        {
            // Reuse the typed helper; widen to u64.
            context.OpStack.PushU64((uint)CachedMem.AtomicLoadInt32(ea));
        }
    }

    // Stores ─────────────────────────────────────────────────────────────
    // Full-width stores go through MemoryInstance's Interlocked.Exchange
    // (seq-cst, matching the threads proposal requirement). Subword
    // stores use Volatile.Write on a byte/short ref — aligned subword
    // writes are atomic on all supported architectures.

    public sealed class InstI32AtomicStore : InstAtomicStore32
    {
        public InstI32AtomicStore() : base((ByteCode)AtomCode.I32AtomicStore, 4) {}
        protected override void DoStore(MemoryInstance mem, int ea, int value) =>
            mem.AtomicStoreInt32(ea, value);
    }

    public sealed class InstI64AtomicStore : InstAtomicStore64
    {
        public InstI64AtomicStore() : base((ByteCode)AtomCode.I64AtomicStore, 8) {}
        protected override void DoStore(MemoryInstance mem, int ea, long value) =>
            mem.AtomicStoreInt64(ea, value);
    }

    public sealed class InstI32AtomicStore8 : InstAtomicStore32
    {
        public InstI32AtomicStore8() : base((ByteCode)AtomCode.I32AtomicStore8, 1) {}
        protected override void DoStore(MemoryInstance mem, int ea, int value) =>
            Volatile.Write(ref mem.Data[ea], (byte)value);
    }

    public sealed class InstI32AtomicStore16 : InstAtomicStore32
    {
        public InstI32AtomicStore16() : base((ByteCode)AtomCode.I32AtomicStore16, 2) {}
        protected override void DoStore(MemoryInstance mem, int ea, int value)
        {
            ref ushort cell = ref Unsafe.As<byte, ushort>(ref mem.Data[ea]);
            Volatile.Write(ref cell, (ushort)value);
        }
    }

    public sealed class InstI64AtomicStore8 : InstAtomicStore64
    {
        public InstI64AtomicStore8() : base((ByteCode)AtomCode.I64AtomicStore8, 1) {}
        protected override void DoStore(MemoryInstance mem, int ea, long value) =>
            Volatile.Write(ref mem.Data[ea], (byte)value);
    }

    public sealed class InstI64AtomicStore16 : InstAtomicStore64
    {
        public InstI64AtomicStore16() : base((ByteCode)AtomCode.I64AtomicStore16, 2) {}
        protected override void DoStore(MemoryInstance mem, int ea, long value)
        {
            ref ushort cell = ref Unsafe.As<byte, ushort>(ref mem.Data[ea]);
            Volatile.Write(ref cell, (ushort)value);
        }
    }

    public sealed class InstI64AtomicStore32 : InstAtomicStore64
    {
        public InstI64AtomicStore32() : base((ByteCode)AtomCode.I64AtomicStore32, 4) {}
        protected override void DoStore(MemoryInstance mem, int ea, long value) =>
            mem.AtomicStoreInt32(ea, (int)value);
    }
}
