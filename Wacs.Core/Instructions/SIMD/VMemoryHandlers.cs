// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Runtime.InteropServices;
using Wacs.Core.Compilation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;

namespace Wacs.Core.Instructions.SIMD
{
    /// <summary>
    /// [OpHandler] entries for v128.load / v128.store — the 128-bit memory ops. Same
    /// stream format as the scalar memory ops: [memIdx:u32][offset:u64] = 12 bytes of
    /// immediates. Bounds checks reuse MemoryHandlers.MemSlice.
    /// </summary>
    internal static class VMemoryHandlers
    {
        // 0xFD 00 v128.load — pop addr, read 16 bytes, push as V128.
        [OpHandler(SimdCode.V128Load)]
        private static V128 V128Load(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
            => MemoryMarshal.Read<V128>(MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 16, "v128.load"));

        // 0xFD 0C v128.const — push the 16-byte literal from the stream.
        [OpHandler(SimdCode.V128Const)]
        private static V128 V128Const([Imm] V128 value) => value;

        // 0xFD 0B v128.store — pop val (top), pop addr, write 16 bytes.
        [OpHandler(SimdCode.V128Store)]
        private static void V128Store(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr, V128 val)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 16, "v128.store");
            VMemoryHandlers.WriteV128(bs, val);
        }

        // ── v128.loadNx2/4/8 — widen-load: read N-bit lanes, sign/zero-extend. ───

        [OpHandler(SimdCode.V128Load8x8S)]
        private static V128 V128Load8x8S(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "v128.load8x8_s");
            return new V128(
                (short)(sbyte)bs[0], (short)(sbyte)bs[1], (short)(sbyte)bs[2], (short)(sbyte)bs[3],
                (short)(sbyte)bs[4], (short)(sbyte)bs[5], (short)(sbyte)bs[6], (short)(sbyte)bs[7]);
        }

        [OpHandler(SimdCode.V128Load8x8U)]
        private static V128 V128Load8x8U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "v128.load8x8_u");
            return new V128(
                (ushort)bs[0], (ushort)bs[1], (ushort)bs[2], (ushort)bs[3],
                (ushort)bs[4], (ushort)bs[5], (ushort)bs[6], (ushort)bs[7]);
        }

        [OpHandler(SimdCode.V128Load16x4S)]
        private static V128 V128Load16x4S(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "v128.load16x4_s");
            return new V128(
                System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bs),
                System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bs.Slice(2)),
                System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bs.Slice(4)),
                System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bs.Slice(6)));
        }

        [OpHandler(SimdCode.V128Load16x4U)]
        private static V128 V128Load16x4U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "v128.load16x4_u");
            return new V128(
                (uint)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bs),
                (uint)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bs.Slice(2)),
                (uint)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bs.Slice(4)),
                (uint)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bs.Slice(6)));
        }

        [OpHandler(SimdCode.V128Load32x2S)]
        private static V128 V128Load32x2S(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "v128.load32x2_s");
            return new V128(
                (long)System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bs),
                (long)System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bs.Slice(4)));
        }

        [OpHandler(SimdCode.V128Load32x2U)]
        private static V128 V128Load32x2U(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "v128.load32x2_u");
            return new V128(
                (ulong)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bs),
                (ulong)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bs.Slice(4)));
        }

        // ── v128.loadN_splat — broadcast N-bit value to all lanes. ───────────────

        [OpHandler(SimdCode.V128Load8Splat)]
        private static V128 V128Load8Splat(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 1, "v128.load8_splat");
            byte v = bs[0];
            return new V128(v, v, v, v, v, v, v, v, v, v, v, v, v, v, v, v);
        }

        [OpHandler(SimdCode.V128Load16Splat)]
        private static V128 V128Load16Splat(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 2, "v128.load16_splat");
            ushort v = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bs);
            return new V128(v, v, v, v, v, v, v, v);
        }

        [OpHandler(SimdCode.V128Load32Splat)]
        private static V128 V128Load32Splat(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 4, "v128.load32_splat");
            uint v = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bs);
            return new V128(v, v, v, v);
        }

        [OpHandler(SimdCode.V128Load64Splat)]
        private static V128 V128Load64Splat(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "v128.load64_splat");
            ulong v = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bs);
            return new V128(v, v);
        }

        // ── v128.loadN_zero — load N bits into lane 0, zero-fill the rest. ───────

        [OpHandler(SimdCode.V128Load32Zero)]
        private static V128 V128Load32Zero(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 4, "v128.load32_zero");
            uint v = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bs);
            return new V128(v, 0u, 0u, 0u);
        }

        [OpHandler(SimdCode.V128Load64Zero)]
        private static V128 V128Load64Zero(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset, uint addr)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "v128.load64_zero");
            ulong v = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bs);
            return new V128(v, 0UL);
        }

        // ── v128.loadN_lane — read N bits, install in a specific lane of an existing v128.

        [OpHandler(SimdCode.V128Load8Lane)]
        private static V128 V128Load8Lane(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
                                          [Imm] byte lane, uint addr, V128 v)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 1, "v128.load8_lane");
            MV128 m = v;
            m[(byte)lane] = bs[0];
            return m;
        }

        [OpHandler(SimdCode.V128Load16Lane)]
        private static V128 V128Load16Lane(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
                                           [Imm] byte lane, uint addr, V128 v)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 2, "v128.load16_lane");
            ushort s = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bs);
            MV128 m = v;
            m[(ushort)lane] = s;
            return m;
        }

        [OpHandler(SimdCode.V128Load32Lane)]
        private static V128 V128Load32Lane(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
                                           [Imm] byte lane, uint addr, V128 v)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 4, "v128.load32_lane");
            int s = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bs);
            MV128 m = v;
            m[(int)lane] = s;
            return m;
        }

        [OpHandler(SimdCode.V128Load64Lane)]
        private static V128 V128Load64Lane(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
                                           [Imm] byte lane, uint addr, V128 v)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "v128.load64_lane");
            long s = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(bs);
            MV128 m = v;
            m[(long)lane] = s;
            return m;
        }

        // ── v128.storeN_lane — read a specific lane, write N bits. ───────────────

        [OpHandler(SimdCode.V128Store8Lane)]
        private static void V128Store8Lane(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
                                           [Imm] byte lane, uint addr, V128 v)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 1, "v128.store8_lane");
            bs[0] = v[(byte)lane];
        }

        [OpHandler(SimdCode.V128Store16Lane)]
        private static void V128Store16Lane(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
                                            [Imm] byte lane, uint addr, V128 v)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 2, "v128.store16_lane");
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bs, v[(ushort)lane]);
        }

        [OpHandler(SimdCode.V128Store32Lane)]
        private static void V128Store32Lane(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
                                            [Imm] byte lane, uint addr, V128 v)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 4, "v128.store32_lane");
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bs, v[(int)lane]);
        }

        [OpHandler(SimdCode.V128Store64Lane)]
        private static void V128Store64Lane(ExecContext ctx, [Imm] uint memIdx, [Imm] ulong offset,
                                            [Imm] byte lane, uint addr, V128 v)
        {
            var bs = MemoryHandlers.MemSlice(ctx, memIdx, addr, offset, 8, "v128.store64_lane");
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(bs, v[(long)lane]);
        }

        /// <summary>
        /// Helper: writes a V128 into a 16-byte span. The framework-specific `in` vs `ref`
        /// overload dance for MemoryMarshal.Write lives here so handler bodies stay simple
        /// and inline cleanly into GeneratedDispatcher.
        /// </summary>
        internal static void WriteV128(Span<byte> bs, V128 val)
        {
#if NET8_0_OR_GREATER
            MemoryMarshal.Write(bs, in val);
#else
            MemoryMarshal.Write(bs, ref val);
#endif
        }
    }
}
