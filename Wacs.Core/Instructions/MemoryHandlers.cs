// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Wacs.Core.Compilation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// [OpHandler] entry points for full-width memory loads/stores and memory.size/grow
    /// on the monolithic-switch path. Narrow load/store variants (i32.load8_s, etc.) and
    /// bulk memory (0xFC memory.init/copy/fill/data.drop) come in the next memory commit.
    ///
    /// Stream format per load/store: [opcode][memIdx:u32][offset:u64] = 13 bytes.
    /// Stream format per memory.size/grow: [opcode][memIdx:u32] = 5 bytes.
    ///
    /// The align hint from the WASM memarg is validation-only — it never affects
    /// execution — so the annotated stream doesn't carry it.
    /// </summary>
    internal static class MemoryHandlers
    {
        /// <summary>
        /// Resolves a bounds-checked Span&lt;byte&gt; into the module's memory. Called from
        /// handler bodies via its fully-qualified name (<c>MemoryHandlers.MemSlice</c>) so
        /// the lookup still resolves when the generator inlines those bodies into the
        /// <c>GeneratedDispatcher</c> partial — the helpers live on a different class.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Span<byte> MemSlice(ExecContext ctx, uint memIdx, uint addr, ulong offset, int width, string op)
        {
            var mem = ctx.Store[ctx.Frame.Module.MemAddrs[(MemIdx)memIdx]];
            long ea = (long)addr + (long)offset;
            if (ea < 0 || ea + width > mem.Data.Length)
                throw new Wacs.Core.Runtime.Types.TrapException(
                    $"{op}: out of bounds memory access (ea={ea}, width={width}, size={mem.Data.Length})");
            return new Span<byte>(mem.Data, (int)ea, width);
        }

        // ---- Loads ---------------------------------------------------------------------

        // 0x28 i32.load
        [OpHandler(OpCode.I32Load)]
        private static uint I32Load(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => BinaryPrimitives.ReadUInt32LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 4, "i32.load"));

        // 0x29 i64.load
        [OpHandler(OpCode.I64Load)]
        private static ulong I64Load(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => BinaryPrimitives.ReadUInt64LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "i64.load"));

        // 0x2A f32.load
        [OpHandler(OpCode.F32Load)]
        private static float F32Load(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 4, "f32.load")));

        // 0x2B f64.load
        [OpHandler(OpCode.F64Load)]
        private static double F64Load(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => BitConverter.Int64BitsToDouble(
                BinaryPrimitives.ReadInt64LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "f64.load")));

        // ---- Stores --------------------------------------------------------------------
        // Stack order for store is [addr, value] → top is value. Generator pops in reverse
        // parameter order, so put `addr` first and `value` second in each signature.

        // 0x36 i32.store
        [OpHandler(OpCode.I32Store)]
        private static void I32Store(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint value)
            => BinaryPrimitives.WriteUInt32LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 4, "i32.store"), value);

        // 0x37 i64.store
        [OpHandler(OpCode.I64Store)]
        private static void I64Store(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong value)
            => BinaryPrimitives.WriteUInt64LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "i64.store"), value);

        // 0x38 f32.store
        [OpHandler(OpCode.F32Store)]
        private static void F32Store(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, float value)
            => BinaryPrimitives.WriteInt32LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 4, "f32.store"),
                                                       BitConverter.SingleToInt32Bits(value));

        // 0x39 f64.store
        [OpHandler(OpCode.F64Store)]
        private static void F64Store(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, double value)
            => BinaryPrimitives.WriteInt64LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "f64.store"),
                                                       BitConverter.DoubleToInt64Bits(value));

        // ---- Size/Grow -----------------------------------------------------------------

        // 0x3F memory.size — push current size in 64 KiB pages.
        [OpHandler(OpCode.MemorySize)]
        private static int MemorySize(ExecContext ctx, [Imm] uint memIdx)
            => (int)ctx.Store[ctx.Frame.Module.MemAddrs[(MemIdx)memIdx]].Size;

        // 0x40 memory.grow — pop N; grow memory by N pages. Push old size (in pages) on
        // success, or -1 on failure (size cap exceeded, allocation failed).
        [OpHandler(OpCode.MemoryGrow)]
        private static int MemoryGrow(ExecContext ctx, [Imm] uint memIdx, int n)
        {
            var mem = ctx.Store[ctx.Frame.Module.MemAddrs[(MemIdx)memIdx]];
            int oldSize = (int)mem.Size;
            return mem.Grow(n) ? oldSize : -1;
        }
    }
}
