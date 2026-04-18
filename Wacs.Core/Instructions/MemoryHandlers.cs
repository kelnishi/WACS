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

        // ---- Narrow loads: sign- or zero-extended into i32/i64 ------------------------

        // 0x2C i32.load8_s — read 1 byte, sign-extend to i32.
        [OpHandler(OpCode.I32Load8S)]
        private static int I32Load8S(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => (sbyte)MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 1, "i32.load8_s")[0];

        // 0x2D i32.load8_u — read 1 byte, zero-extend to i32.
        [OpHandler(OpCode.I32Load8U)]
        private static uint I32Load8U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 1, "i32.load8_u")[0];

        // 0x2E i32.load16_s
        [OpHandler(OpCode.I32Load16S)]
        private static int I32Load16S(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => BinaryPrimitives.ReadInt16LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 2, "i32.load16_s"));

        // 0x2F i32.load16_u
        [OpHandler(OpCode.I32Load16U)]
        private static uint I32Load16U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => BinaryPrimitives.ReadUInt16LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 2, "i32.load16_u"));

        // 0x30 i64.load8_s
        [OpHandler(OpCode.I64Load8S)]
        private static long I64Load8S(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => (sbyte)MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 1, "i64.load8_s")[0];

        // 0x31 i64.load8_u
        [OpHandler(OpCode.I64Load8U)]
        private static ulong I64Load8U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 1, "i64.load8_u")[0];

        // 0x32 i64.load16_s
        [OpHandler(OpCode.I64Load16S)]
        private static long I64Load16S(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => BinaryPrimitives.ReadInt16LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 2, "i64.load16_s"));

        // 0x33 i64.load16_u
        [OpHandler(OpCode.I64Load16U)]
        private static ulong I64Load16U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => BinaryPrimitives.ReadUInt16LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 2, "i64.load16_u"));

        // 0x34 i64.load32_s — read i32 and sign-extend implicitly via int → long conversion.
        [OpHandler(OpCode.I64Load32S)]
        private static long I64Load32S(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => BinaryPrimitives.ReadInt32LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 4, "i64.load32_s"));

        // 0x35 i64.load32_u — zero-extend u32 to u64.
        [OpHandler(OpCode.I64Load32U)]
        private static ulong I64Load32U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => BinaryPrimitives.ReadUInt32LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 4, "i64.load32_u"));

        // ---- Narrow stores: truncate down to N low bytes ------------------------------

        // 0x3A i32.store8
        [OpHandler(OpCode.I32Store8)]
        private static void I32Store8(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint value)
            => MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 1, "i32.store8")[0] = (byte)value;

        // 0x3B i32.store16
        [OpHandler(OpCode.I32Store16)]
        private static void I32Store16(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, uint value)
            => BinaryPrimitives.WriteUInt16LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 2, "i32.store16"),
                                                        (ushort)value);

        // 0x3C i64.store8
        [OpHandler(OpCode.I64Store8)]
        private static void I64Store8(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong value)
            => MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 1, "i64.store8")[0] = (byte)value;

        // 0x3D i64.store16
        [OpHandler(OpCode.I64Store16)]
        private static void I64Store16(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong value)
            => BinaryPrimitives.WriteUInt16LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 2, "i64.store16"),
                                                        (ushort)value);

        // 0x3E i64.store32
        [OpHandler(OpCode.I64Store32)]
        private static void I64Store32(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, ulong value)
            => BinaryPrimitives.WriteUInt32LittleEndian(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 4, "i64.store32"),
                                                        (uint)value);

        // ---- Bulk memory (0xFC prefix) -------------------------------------------------
        // Stack for all three ternary ops is [d, s|val, n] with `n` on top; param order
        // below puts n last so the generator pops it first, matching WASM's stack shape.

        // 0xFC 08 memory.init — copy `n` bytes from data[s..s+n] into mem[d..d+n].
        [OpHandler(ExtCode.MemoryInit)]
        private static void MemoryInit(ExecContext ctx, [Imm] uint dataIdx, [Imm] uint memIdx,
                                        uint d, uint s, uint n)
        {
            var mem = ctx.Store[ctx.Frame.Module.MemAddrs[(MemIdx)memIdx]];
            var data = ctx.Store[ctx.Frame.Module.DataAddrs[(DataIdx)dataIdx]];
            if ((long)s + n > data.Data.Length || (long)d + n > mem.Data.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("memory.init: out of bounds memory access");
            if (n == 0) return;
            Array.Copy(data.Data, (int)s, mem.Data, (int)d, (int)n);
        }

        // 0xFC 09 data.drop — invalidate the data segment (subsequent memory.init traps).
        [OpHandler(ExtCode.DataDrop)]
        private static void DataDrop(ExecContext ctx, [Imm] uint dataIdx)
            => ctx.Store.DropData(ctx.Frame.Module.DataAddrs[(DataIdx)dataIdx]);

        // 0xFC 0A memory.copy — memmove, handles overlap.
        [OpHandler(ExtCode.MemoryCopy)]
        private static void MemoryCopy(ExecContext ctx, [Imm] uint dstIdx, [Imm] uint srcIdx,
                                        uint d, uint s, uint n)
        {
            var src = ctx.Store[ctx.Frame.Module.MemAddrs[(MemIdx)srcIdx]];
            var dst = ctx.Store[ctx.Frame.Module.MemAddrs[(MemIdx)dstIdx]];
            if ((long)s + n > src.Data.Length || (long)d + n > dst.Data.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("memory.copy: out of bounds memory access");
            if (n == 0) return;
            // Array.Copy handles overlapping regions correctly (memmove semantics).
            Array.Copy(src.Data, (int)s, dst.Data, (int)d, (int)n);
        }

        // 0xFC 0B memory.fill — mem[d..d+n] = (byte)val for the low 8 bits of val.
        [OpHandler(ExtCode.MemoryFill)]
        private static void MemoryFill(ExecContext ctx, [Imm] uint memIdx,
                                        uint d, uint val, uint n)
        {
            var mem = ctx.Store[ctx.Frame.Module.MemAddrs[(MemIdx)memIdx]];
            if ((long)d + n > mem.Data.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("memory.fill: out of bounds memory access");
            if (n == 0) return;
            new Span<byte>(mem.Data, (int)d, (int)n).Fill((byte)val);
        }

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
