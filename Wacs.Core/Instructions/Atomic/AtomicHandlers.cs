// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Runtime.CompilerServices;
using System.Threading;
using Wacs.Core.Compilation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Atomic
{
    /// <summary>
    /// [OpHandler] entry points for the 0xFE-prefixed atomic ops
    /// (threads proposal) on the switch runtime. Mirror the
    /// polymorphic implementations in <c>AtomicBase.cs</c> + the
    /// concrete <c>InstI32AtomicXxx</c> families, executing through
    /// the same <see cref="MemoryInstance"/> atomic helpers so the
    /// two back-ends share correctness properties.
    ///
    /// Stream encoding per memarg-carrying op:
    /// <c>[memIdx:u32][offset:u64]</c> (12 bytes). Align is
    /// validation-only and omitted. <c>atomic.fence</c> carries no
    /// immediate.
    /// </summary>
    internal static class AtomicHandlers
    {
        // ---- Shared helper --------------------------------------------

        /// <summary>
        /// Resolve the memory, compute the effective address, and
        /// verify bounds + exact natural alignment. Traps on any
        /// failure. Returns the <see cref="MemoryInstance"/> so the
        /// caller can route through its atomic helpers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static MemoryInstance ResolveAtomic(
            ExecContext ctx, uint memIdx, uint addr, ulong offset,
            int widthBytes, string op, out int ea)
        {
            var mem = ctx.Store[ctx.Frame.Module.MemAddrs[(MemIdx)memIdx]];
            long eaLong = (long)addr + (long)offset;
            if (eaLong < 0 || eaLong + widthBytes > mem.Data.Length)
                throw new TrapException(
                    $"{op}: out of bounds atomic access (ea={eaLong}, width={widthBytes}, size={mem.Data.Length})");
            if ((eaLong & (widthBytes - 1)) != 0)
                throw new TrapException(
                    $"{op}: unaligned atomic access at ea={eaLong} (width={widthBytes})");
            ea = (int)eaLong;
            return mem;
        }

        // ---- Loads ----------------------------------------------------
        // Stack: (addr) → result. Handler signature: return type is
        // pushed; trailing plain params are popped in reverse order.

        [OpHandler(AtomCode.I32AtomicLoad)]
        private static uint I32AtomicLoad(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i32.atomic.load", out int ea);
            return (uint)mem.AtomicLoadInt32(ea);
        }

        [OpHandler(AtomCode.I64AtomicLoad)]
        private static ulong I64AtomicLoad(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 8, "i64.atomic.load", out int ea);
            return (ulong)mem.AtomicLoadInt64(ea);
        }

        [OpHandler(AtomCode.I32AtomicLoad8U)]
        private static uint I32AtomicLoad8U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i32.atomic.load8_u", out int ea);
            return System.Threading.Volatile.Read(ref mem.Data[ea]);
        }

        [OpHandler(AtomCode.I32AtomicLoad16U)]
        private static uint I32AtomicLoad16U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i32.atomic.load16_u", out int ea);
            ref ushort cell = ref System.Runtime.CompilerServices.Unsafe.As<byte, ushort>(ref mem.Data[ea]);
            return System.Threading.Volatile.Read(ref cell);
        }

        [OpHandler(AtomCode.I64AtomicLoad8U)]
        private static ulong I64AtomicLoad8U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i64.atomic.load8_u", out int ea);
            return System.Threading.Volatile.Read(ref mem.Data[ea]);
        }

        [OpHandler(AtomCode.I64AtomicLoad16U)]
        private static ulong I64AtomicLoad16U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i64.atomic.load16_u", out int ea);
            ref ushort cell = ref System.Runtime.CompilerServices.Unsafe.As<byte, ushort>(ref mem.Data[ea]);
            return System.Threading.Volatile.Read(ref cell);
        }

        [OpHandler(AtomCode.I64AtomicLoad32U)]
        private static ulong I64AtomicLoad32U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i64.atomic.load32_u", out int ea);
            return (uint)mem.AtomicLoadInt32(ea);
        }

        // ---- Stores ---------------------------------------------------
        // Stack: (addr, value) → ∅. Handler pops in reverse param order,
        // so put `addr` before `value` in the parameter list.

        [OpHandler(AtomCode.I32AtomicStore)]
        private static void I32AtomicStore(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint value)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i32.atomic.store", out int ea);
            mem.AtomicStoreInt32(ea, (int)value);
        }

        [OpHandler(AtomCode.I64AtomicStore)]
        private static void I64AtomicStore(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong value)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 8, "i64.atomic.store", out int ea);
            mem.AtomicStoreInt64(ea, (long)value);
        }

        [OpHandler(AtomCode.I32AtomicStore8)]
        private static void I32AtomicStore8(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint value)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i32.atomic.store8", out int ea);
            System.Threading.Volatile.Write(ref mem.Data[ea], (byte)value);
        }

        [OpHandler(AtomCode.I32AtomicStore16)]
        private static void I32AtomicStore16(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint value)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i32.atomic.store16", out int ea);
            ref ushort cell = ref System.Runtime.CompilerServices.Unsafe.As<byte, ushort>(ref mem.Data[ea]);
            System.Threading.Volatile.Write(ref cell, (ushort)value);
        }

        [OpHandler(AtomCode.I64AtomicStore8)]
        private static void I64AtomicStore8(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong value)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i64.atomic.store8", out int ea);
            System.Threading.Volatile.Write(ref mem.Data[ea], (byte)value);
        }

        [OpHandler(AtomCode.I64AtomicStore16)]
        private static void I64AtomicStore16(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong value)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i64.atomic.store16", out int ea);
            ref ushort cell = ref System.Runtime.CompilerServices.Unsafe.As<byte, ushort>(ref mem.Data[ea]);
            System.Threading.Volatile.Write(ref cell, (ushort)value);
        }

        [OpHandler(AtomCode.I64AtomicStore32)]
        private static void I64AtomicStore32(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong value)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i64.atomic.store32", out int ea);
            mem.AtomicStoreInt32(ea, (int)value);
        }

        // ---- RMW: add ------------------------------------------------

        [OpHandler(AtomCode.I32AtomicRmwAdd)]
        private static uint I32AtomicRmwAdd(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i32.atomic.rmw.add", out int ea);
            return (uint)mem.AtomicAddInt32(ea, (int)arg);
        }

        [OpHandler(AtomCode.I64AtomicRmwAdd)]
        private static ulong I64AtomicRmwAdd(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 8, "i64.atomic.rmw.add", out int ea);
            return (ulong)mem.AtomicAddInt64(ea, (long)arg);
        }

        [OpHandler(AtomCode.I32AtomicRmw8AddU)]
        private static uint I32AtomicRmw8AddU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i32.atomic.rmw8.add_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 1, old => old + (int)arg);
        }

        [OpHandler(AtomCode.I32AtomicRmw16AddU)]
        private static uint I32AtomicRmw16AddU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i32.atomic.rmw16.add_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 2, old => old + (int)arg);
        }

        [OpHandler(AtomCode.I64AtomicRmw8AddU)]
        private static ulong I64AtomicRmw8AddU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i64.atomic.rmw8.add_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 1, old => old + a);
        }

        [OpHandler(AtomCode.I64AtomicRmw16AddU)]
        private static ulong I64AtomicRmw16AddU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i64.atomic.rmw16.add_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 2, old => old + a);
        }

        [OpHandler(AtomCode.I64AtomicRmw32AddU)]
        private static ulong I64AtomicRmw32AddU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i64.atomic.rmw32.add_u", out int ea);
            return (uint)mem.AtomicAddInt32(ea, (int)arg);
        }

        // ---- RMW: sub ------------------------------------------------

        [OpHandler(AtomCode.I32AtomicRmwSub)]
        private static uint I32AtomicRmwSub(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i32.atomic.rmw.sub", out int ea);
            return (uint)mem.AtomicAddInt32(ea, -(int)arg);
        }

        [OpHandler(AtomCode.I64AtomicRmwSub)]
        private static ulong I64AtomicRmwSub(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 8, "i64.atomic.rmw.sub", out int ea);
            return (ulong)mem.AtomicAddInt64(ea, -(long)arg);
        }

        [OpHandler(AtomCode.I32AtomicRmw8SubU)]
        private static uint I32AtomicRmw8SubU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i32.atomic.rmw8.sub_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 1, old => old - (int)arg);
        }

        [OpHandler(AtomCode.I32AtomicRmw16SubU)]
        private static uint I32AtomicRmw16SubU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i32.atomic.rmw16.sub_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 2, old => old - (int)arg);
        }

        [OpHandler(AtomCode.I64AtomicRmw8SubU)]
        private static ulong I64AtomicRmw8SubU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i64.atomic.rmw8.sub_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 1, old => old - a);
        }

        [OpHandler(AtomCode.I64AtomicRmw16SubU)]
        private static ulong I64AtomicRmw16SubU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i64.atomic.rmw16.sub_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 2, old => old - a);
        }

        [OpHandler(AtomCode.I64AtomicRmw32SubU)]
        private static ulong I64AtomicRmw32SubU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i64.atomic.rmw32.sub_u", out int ea);
            return (uint)mem.AtomicAddInt32(ea, -(int)arg);
        }

        // ---- RMW: and ------------------------------------------------

        [OpHandler(AtomCode.I32AtomicRmwAnd)]
        private static uint I32AtomicRmwAnd(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i32.atomic.rmw.and", out int ea);
            return (uint)mem.AtomicAndInt32(ea, (int)arg);
        }

        [OpHandler(AtomCode.I64AtomicRmwAnd)]
        private static ulong I64AtomicRmwAnd(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 8, "i64.atomic.rmw.and", out int ea);
            return (ulong)mem.AtomicAndInt64(ea, (long)arg);
        }

        [OpHandler(AtomCode.I32AtomicRmw8AndU)]
        private static uint I32AtomicRmw8AndU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i32.atomic.rmw8.and_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 1, old => old & (int)arg);
        }

        [OpHandler(AtomCode.I32AtomicRmw16AndU)]
        private static uint I32AtomicRmw16AndU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i32.atomic.rmw16.and_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 2, old => old & (int)arg);
        }

        [OpHandler(AtomCode.I64AtomicRmw8AndU)]
        private static ulong I64AtomicRmw8AndU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i64.atomic.rmw8.and_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 1, old => old & a);
        }

        [OpHandler(AtomCode.I64AtomicRmw16AndU)]
        private static ulong I64AtomicRmw16AndU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i64.atomic.rmw16.and_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 2, old => old & a);
        }

        [OpHandler(AtomCode.I64AtomicRmw32AndU)]
        private static ulong I64AtomicRmw32AndU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i64.atomic.rmw32.and_u", out int ea);
            return (uint)mem.AtomicAndInt32(ea, (int)arg);
        }

        // ---- RMW: or -------------------------------------------------

        [OpHandler(AtomCode.I32AtomicRmwOr)]
        private static uint I32AtomicRmwOr(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i32.atomic.rmw.or", out int ea);
            return (uint)mem.AtomicOrInt32(ea, (int)arg);
        }

        [OpHandler(AtomCode.I64AtomicRmwOr)]
        private static ulong I64AtomicRmwOr(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 8, "i64.atomic.rmw.or", out int ea);
            return (ulong)mem.AtomicOrInt64(ea, (long)arg);
        }

        [OpHandler(AtomCode.I32AtomicRmw8OrU)]
        private static uint I32AtomicRmw8OrU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i32.atomic.rmw8.or_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 1, old => old | (int)arg);
        }

        [OpHandler(AtomCode.I32AtomicRmw16OrU)]
        private static uint I32AtomicRmw16OrU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i32.atomic.rmw16.or_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 2, old => old | (int)arg);
        }

        [OpHandler(AtomCode.I64AtomicRmw8OrU)]
        private static ulong I64AtomicRmw8OrU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i64.atomic.rmw8.or_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 1, old => old | a);
        }

        [OpHandler(AtomCode.I64AtomicRmw16OrU)]
        private static ulong I64AtomicRmw16OrU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i64.atomic.rmw16.or_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 2, old => old | a);
        }

        [OpHandler(AtomCode.I64AtomicRmw32OrU)]
        private static ulong I64AtomicRmw32OrU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i64.atomic.rmw32.or_u", out int ea);
            return (uint)mem.AtomicOrInt32(ea, (int)arg);
        }

        // ---- RMW: xor ------------------------------------------------

        [OpHandler(AtomCode.I32AtomicRmwXor)]
        private static uint I32AtomicRmwXor(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i32.atomic.rmw.xor", out int ea);
            return (uint)mem.AtomicXorInt32(ea, (int)arg);
        }

        [OpHandler(AtomCode.I64AtomicRmwXor)]
        private static ulong I64AtomicRmwXor(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 8, "i64.atomic.rmw.xor", out int ea);
            return (ulong)mem.AtomicXorInt64(ea, (long)arg);
        }

        [OpHandler(AtomCode.I32AtomicRmw8XorU)]
        private static uint I32AtomicRmw8XorU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i32.atomic.rmw8.xor_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 1, old => old ^ (int)arg);
        }

        [OpHandler(AtomCode.I32AtomicRmw16XorU)]
        private static uint I32AtomicRmw16XorU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i32.atomic.rmw16.xor_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 2, old => old ^ (int)arg);
        }

        [OpHandler(AtomCode.I64AtomicRmw8XorU)]
        private static ulong I64AtomicRmw8XorU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i64.atomic.rmw8.xor_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 1, old => old ^ a);
        }

        [OpHandler(AtomCode.I64AtomicRmw16XorU)]
        private static ulong I64AtomicRmw16XorU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i64.atomic.rmw16.xor_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 2, old => old ^ a);
        }

        [OpHandler(AtomCode.I64AtomicRmw32XorU)]
        private static ulong I64AtomicRmw32XorU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i64.atomic.rmw32.xor_u", out int ea);
            return (uint)mem.AtomicXorInt32(ea, (int)arg);
        }

        // ---- RMW: xchg -----------------------------------------------

        [OpHandler(AtomCode.I32AtomicRmwXchg)]
        private static uint I32AtomicRmwXchg(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i32.atomic.rmw.xchg", out int ea);
            return (uint)mem.AtomicExchangeInt32(ea, (int)arg);
        }

        [OpHandler(AtomCode.I64AtomicRmwXchg)]
        private static ulong I64AtomicRmwXchg(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 8, "i64.atomic.rmw.xchg", out int ea);
            return (ulong)mem.AtomicExchangeInt64(ea, (long)arg);
        }

        [OpHandler(AtomCode.I32AtomicRmw8XchgU)]
        private static uint I32AtomicRmw8XchgU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i32.atomic.rmw8.xchg_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 1, _ => a);
        }

        [OpHandler(AtomCode.I32AtomicRmw16XchgU)]
        private static uint I32AtomicRmw16XchgU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i32.atomic.rmw16.xchg_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 2, _ => a);
        }

        [OpHandler(AtomCode.I64AtomicRmw8XchgU)]
        private static ulong I64AtomicRmw8XchgU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i64.atomic.rmw8.xchg_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 1, _ => a);
        }

        [OpHandler(AtomCode.I64AtomicRmw16XchgU)]
        private static ulong I64AtomicRmw16XchgU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i64.atomic.rmw16.xchg_u", out int ea);
            int a = (int)arg;
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Loop(mem, ea, 2, _ => a);
        }

        [OpHandler(AtomCode.I64AtomicRmw32XchgU)]
        private static ulong I64AtomicRmw32XchgU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong arg)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i64.atomic.rmw32.xchg_u", out int ea);
            return (uint)mem.AtomicExchangeInt32(ea, (int)arg);
        }

        // ---- Cmpxchg -------------------------------------------------
        // Stack: (addr, expected, replacement) → original.

        [OpHandler(AtomCode.I32AtomicRmwCmpxchg)]
        private static uint I32AtomicRmwCmpxchg(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
            uint addr, uint expected, uint replacement)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i32.atomic.rmw.cmpxchg", out int ea);
            return (uint)mem.AtomicCompareExchangeInt32(ea, (int)replacement, (int)expected);
        }

        [OpHandler(AtomCode.I64AtomicRmwCmpxchg)]
        private static ulong I64AtomicRmwCmpxchg(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
            uint addr, ulong expected, ulong replacement)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 8, "i64.atomic.rmw.cmpxchg", out int ea);
            return (ulong)mem.AtomicCompareExchangeInt64(ea, (long)replacement, (long)expected);
        }

        [OpHandler(AtomCode.I32AtomicRmw8CmpxchgU)]
        private static uint I32AtomicRmw8CmpxchgU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
            uint addr, uint expected, uint replacement)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i32.atomic.rmw8.cmpxchg_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Cmpxchg(mem, ea, 1, (int)expected, (int)replacement);
        }

        [OpHandler(AtomCode.I32AtomicRmw16CmpxchgU)]
        private static uint I32AtomicRmw16CmpxchgU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
            uint addr, uint expected, uint replacement)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i32.atomic.rmw16.cmpxchg_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Cmpxchg(mem, ea, 2, (int)expected, (int)replacement);
        }

        [OpHandler(AtomCode.I64AtomicRmw8CmpxchgU)]
        private static ulong I64AtomicRmw8CmpxchgU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
            uint addr, ulong expected, ulong replacement)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 1, "i64.atomic.rmw8.cmpxchg_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Cmpxchg(mem, ea, 1, (int)expected, (int)replacement);
        }

        [OpHandler(AtomCode.I64AtomicRmw16CmpxchgU)]
        private static ulong I64AtomicRmw16CmpxchgU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
            uint addr, ulong expected, ulong replacement)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 2, "i64.atomic.rmw16.cmpxchg_u", out int ea);
            return (uint)Wacs.Core.Instructions.Atomic.SubwordCas.Cmpxchg(mem, ea, 2, (int)expected, (int)replacement);
        }

        [OpHandler(AtomCode.I64AtomicRmw32CmpxchgU)]
        private static ulong I64AtomicRmw32CmpxchgU(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
            uint addr, ulong expected, ulong replacement)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "i64.atomic.rmw32.cmpxchg_u", out int ea);
            return (uint)mem.AtomicCompareExchangeInt32(ea, (int)replacement, (int)expected);
        }

        // ---- Wait / notify -------------------------------------------

        [OpHandler(AtomCode.MemoryAtomicNotify)]
        private static uint MemoryAtomicNotify(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
            uint addr, uint count)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "memory.atomic.notify", out int ea);
            return (uint)ctx.ConcurrencyPolicy.Notify(mem, ea, (int)count);
        }

        [OpHandler(AtomCode.MemoryAtomicWait32)]
        private static uint MemoryAtomicWait32(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
            uint addr, uint expected, long timeoutNs)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 4, "memory.atomic.wait32", out int ea);
            return (uint)ctx.ConcurrencyPolicy.Wait32(mem, ea, (int)expected, timeoutNs);
        }

        [OpHandler(AtomCode.MemoryAtomicWait64)]
        private static uint MemoryAtomicWait64(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
            uint addr, ulong expected, long timeoutNs)
        {
            var mem = Wacs.Core.Instructions.Atomic.AtomicHandlers.ResolveAtomic(ctx, memIdx, addr, offset, 8, "memory.atomic.wait64", out int ea);
            return (uint)ctx.ConcurrencyPolicy.Wait64(mem, ea, (long)expected, timeoutNs);
        }

        // ---- Fence ---------------------------------------------------

        [OpHandler(AtomCode.AtomicFence)]
        private static void AtomicFence(ExecContext ctx) => System.Threading.Interlocked.MemoryBarrier();
    }
}
