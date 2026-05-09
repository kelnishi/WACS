// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.AOT.Emitters;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Pins the contract that <c>MemoryHelpers</c> load/store helpers
    /// address into the high half of a NativePointer-backed
    /// <see cref="MemoryInstance"/> (effective address > int.MaxValue)
    /// without truncating through a <c>(int)ea</c> cast.
    ///
    /// <para>Pre-fix the helpers cast <c>ea</c> to <c>int</c> on the
    /// final <c>RefAs</c> / <c>AsSpan</c> call. With <c>ea</c> in the
    /// 2.1–2.3 GiB range (the heap layout Rust dlmalloc produces past
    /// the first 2 GiB worth of allocations), the cast wraps to a
    /// negative pointer offset and the kernel signals SIGSEGV — the
    /// .NET runtime aborts with <c>AccessViolationException</c>.
    /// Routing through the new <c>RefAs&lt;T&gt;(nuint)</c> /
    /// <c>AsSpan(nuint, int)</c> overloads keeps the full 64-bit
    /// address through the call site.</para>
    ///
    /// <para><c>NativeMemory.AllocZeroed</c> is lazy-zero on calloc,
    /// so the 2 GiB+ virtual allocation does not commit physical
    /// pages — runs on commodity CI.</para>
    /// </summary>
    public class MemoryHelpersHighAddressTests
    {
        // ~2.0625 GiB — comfortably past int.MaxValue, well under the
        // 4 GiB wasm32 ceiling.
        private const int HighMemoryPages = 33_000;

        // Effective address sits above int.MaxValue. addr is the
        // wasm-side i32 argument (interpreted as uint by the helper);
        // offset is the memarg constant. (uint)addr + offset = ea.
        // Using addr=int.MinValue gives uint addr = 0x80000000 = 2 GiB.
        // +1024 keeps the 4-byte store strictly inside the 2.0625 GiB
        // memory.
        private const int HighAddrArg = int.MinValue;  // uint = 0x80000000
        private const long HighAddrOffset = 1024;
        private const long HighEa
            = unchecked((long)(uint)HighAddrArg) + HighAddrOffset;

        private static MemoryInstance NewHighMemory()
        {
            var memType = new MemoryType((uint)HighMemoryPages,
                /*max*/ null);
            return new MemoryInstance(memType,
                MemoryStorageMode.NativePointer);
        }

        [Fact]
        public void StoreI32_LoadI32_RoundTrip_PastTwoGiB()
        {
            // Sanity: ea is genuinely past the int.MaxValue cliff.
            Assert.True(HighEa > (long)int.MaxValue);

            using var mem = NewHighMemory();
            int value = unchecked((int)0xDEADBEEF);
            MemoryHelpers.StoreI32(mem, HighAddrArg, HighAddrOffset, value);
            int read = MemoryHelpers.LoadI32(mem, HighAddrArg, HighAddrOffset);
            Assert.Equal(value, read);
        }

        [Fact]
        public void StoreI64_LoadI64_RoundTrip_PastTwoGiB()
        {
            using var mem = NewHighMemory();
            long value = unchecked((long)0x0123456789ABCDEFUL);
            MemoryHelpers.StoreI64(mem, HighAddrArg, HighAddrOffset, value);
            long read = MemoryHelpers.LoadI64(mem, HighAddrArg, HighAddrOffset);
            Assert.Equal(value, read);
        }

        [Fact]
        public void StoreI32_8_LoadI32_8U_RoundTrip_PastTwoGiB()
        {
            // Narrow store/load goes through AsSpan(nuint, int) on the
            // helper path — same gap shape as the wide ops.
            using var mem = NewHighMemory();
            MemoryHelpers.StoreI32_8(mem, HighAddrArg, HighAddrOffset, 0x42);
            int read = MemoryHelpers.LoadI32_8U(mem, HighAddrArg, HighAddrOffset);
            Assert.Equal(0x42, read);
        }

        [Fact]
        public void StoreF32_LoadF32_RoundTrip_PastTwoGiB()
        {
            using var mem = NewHighMemory();
            float value = 3.14159f;
            MemoryHelpers.StoreF32(mem, HighAddrArg, HighAddrOffset, value);
            float read = MemoryHelpers.LoadF32(mem, HighAddrArg, HighAddrOffset);
            Assert.Equal(value, read);
        }

        [Fact]
        public void StoreF64_LoadF64_RoundTrip_PastTwoGiB()
        {
            using var mem = NewHighMemory();
            double value = 2.7182818284590452;
            MemoryHelpers.StoreF64(mem, HighAddrArg, HighAddrOffset, value);
            double read = MemoryHelpers.LoadF64(mem, HighAddrArg, HighAddrOffset);
            Assert.Equal(value, read);
        }

        // === Bulk-op high-address coverage (gap 16) ===
        // BulkHelpers.{MemoryFill, MemoryCopy, MemoryInit} took raw
        // int dst/src into AsSpan, hitting the int overload that
        // wraps to a negative pointer offset on NativePointer
        // memories past 2 GiB. Pre-fix each AVs; post-fix bytes
        // round-trip.

        private static ThinContext NewHighContext(MemoryInstance mem)
            => new ThinContext(memories: new[] { mem });

        [Fact]
        public void MemoryFill_PastTwoGiB_FillsRegion()
        {
            using var mem = NewHighMemory();
            var ctx = NewHighContext(mem);
            const int len = 256;
            BulkHelpers.MemoryFill(ctx, /*memIdx*/ 0,
                HighAddrArg + (int)HighAddrOffset, /*val*/ 0xAB, len);
            // Read back via the (already-migrated) load helpers.
            var span = mem.AsSpan((nuint)HighEa, len);
            for (int i = 0; i < len; i++)
                Assert.Equal((byte)0xAB, span[i]);
        }

        [Fact]
        public void MemoryCopy_PastTwoGiB_CopiesRegion()
        {
            using var mem = NewHighMemory();
            var ctx = NewHighContext(mem);
            // Stage source bytes at offset 0 (low half), copy them
            // up to the high-address dst. Single-memory copy.
            const int len = 64;
            var srcSpan = mem.AsSpan(0, len);
            for (int i = 0; i < len; i++) srcSpan[i] = (byte)i;
            int dstAddr = HighAddrArg + (int)HighAddrOffset;
            BulkHelpers.MemoryCopy(ctx, /*dstMemIdx*/ 0, /*srcMemIdx*/ 0,
                dstAddr, /*src*/ 0, len);
            var dstSpan = mem.AsSpan((nuint)HighEa, len);
            for (int i = 0; i < len; i++)
                Assert.Equal((byte)i, dstSpan[i]);
        }

        [Fact]
        public void MemoryInit_PastTwoGiB_CopiesFromDataSegment()
        {
            using var mem = NewHighMemory();
            var ctx = NewHighContext(mem);
            // Register a passive data segment in ModuleInit.
            var segBytes = new byte[128];
            for (int i = 0; i < segBytes.Length; i++) segBytes[i] = (byte)(0x80 ^ i);
            int segId = ModuleInit.RegisterDataSegment(segBytes);
            try
            {
                // ctx.DataSegmentBaseId defaults to 0; pass dataIdx
                // = segId so the helper resolves the segment.
                int dstAddr = HighAddrArg + (int)HighAddrOffset;
                BulkHelpers.MemoryInit(ctx, /*memIdx*/ 0,
                    /*dataIdx*/ segId, dstAddr, /*src*/ 0,
                    segBytes.Length);
                var dstSpan = mem.AsSpan((nuint)HighEa, segBytes.Length);
                for (int i = 0; i < segBytes.Length; i++)
                    Assert.Equal((byte)(0x80 ^ i), dstSpan[i]);
            }
            finally
            {
                // Drop the segment so cross-test state doesn't leak.
                ModuleInit.DropDataSegment(segId);
            }
        }
    }
}
