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
