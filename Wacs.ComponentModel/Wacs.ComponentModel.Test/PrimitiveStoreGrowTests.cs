// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using Wacs.ComponentModel.CanonicalABI;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Pins the contract that PrimitiveStore.StoreXxx helpers
    /// re-fetch <see cref="MemoryInstance.Data"/> AFTER each
    /// cabi_realloc invocation. <c>Array.Resize</c> on grow
    /// reallocates the byte[] field and a captured-before
    /// reference goes stale — gap 11 from
    /// <c>wasi-nn/WACS-GAPS.md</c>: rust's
    /// <c>std::io::Read::read</c> threw
    /// <see cref="ArgumentOutOfRangeException"/> for any single
    /// call past ~24 KiB because the lower path was capturing the
    /// pre-grow byte[] before the cabi_realloc that triggered the
    /// grow.
    /// </summary>
    public class PrimitiveStoreGrowTests
    {
        [Fact]
        public void StoreByteArray_ReallocGrowsMemory_BytesRoundTrip()
        {
            // 1-page initial memory; 100 KiB byte[] forces grow.
            var memType = new MemoryType(1, 10);
            var mem = new MemoryInstance(memType);
            Assert.Equal(65536, mem.Data.Length);

            byte[] value = new byte[100_000];
            for (int i = 0; i < value.Length; i++)
                value[i] = (byte)(i & 0xFF);

            int next = 32;  // bump start
            Func<int, int, int, int, int> cabiRealloc =
                (oldPtr, oldSize, align, newSize) =>
                {
                    int aligned = (next + align - 1) & ~(align - 1);
                    int needed = aligned + newSize;
                    int curBytes = mem.Data.Length;
                    if (needed > curBytes)
                    {
                        int pages = (needed - curBytes + 65535) / 65536;
                        Assert.True(mem.Grow(pages));
                    }
                    next = needed;
                    return aligned;
                };

            // Pre-fix this throws ArgumentOutOfRangeException inside
            // Buffer.BlockCopy because mem.Data is captured before
            // cabi_realloc grows it. Post-fix the helper re-fetches
            // mem.Data after cabi_realloc returns.
            PrimitiveStore.StoreByteArray(mem, 0, value, cabiRealloc);

            Assert.True(mem.Data.Length >= 100_032,
                $"expected mem.Data >= 100032, got {mem.Data.Length}");

            int ptr = BinaryPrimitives.ReadInt32LittleEndian(
                mem.Data.AsSpan(0, 4));
            int len = BinaryPrimitives.ReadInt32LittleEndian(
                mem.Data.AsSpan(4, 4));
            Assert.Equal(32, ptr);
            Assert.Equal(100_000, len);

            for (int i = 0; i < value.Length; i++)
                Assert.Equal((byte)(i & 0xFF), mem.Data[ptr + i]);
        }

        [Fact]
        public void StoreString_ReallocGrowsMemory_BytesRoundTrip()
        {
            var memType = new MemoryType(1, 10);
            var mem = new MemoryInstance(memType);

            // 50 KiB ASCII string — encoded as 50 KiB UTF-8, fits
            // in the first page minus retArea slot but ride the
            // post-realloc fetch path anyway. Pad to push past the
            // initial 64 KiB memory.
            string value = new string('A', 80_000);

            int next = 32;
            Func<int, int, int, int, int> cabiRealloc =
                (oldPtr, oldSize, align, newSize) =>
                {
                    int aligned = (next + align - 1) & ~(align - 1);
                    int needed = aligned + newSize;
                    int curBytes = mem.Data.Length;
                    if (needed > curBytes)
                    {
                        int pages = (needed - curBytes + 65535) / 65536;
                        Assert.True(mem.Grow(pages));
                    }
                    next = needed;
                    return aligned;
                };

            PrimitiveStore.StoreString(mem, 0, value, cabiRealloc);

            int ptr = BinaryPrimitives.ReadInt32LittleEndian(
                mem.Data.AsSpan(0, 4));
            int len = BinaryPrimitives.ReadInt32LittleEndian(
                mem.Data.AsSpan(4, 4));
            Assert.Equal(80_000, len);
            Assert.Equal((byte)'A', mem.Data[ptr]);
            Assert.Equal((byte)'A', mem.Data[ptr + 79_999]);
        }

        [Fact]
        public void StoreByteArrayList_PerElementGrowDuringIter_BytesRoundTrip()
        {
            // Multiple sub-arrays — cabi_realloc fires per-element.
            // First element fits, second triggers grow. The helper's
            // outer (sub_ptr, sub_len) writes happen after the grow
            // — must use the fresh mem.Data.
            var memType = new MemoryType(1, 10);
            var mem = new MemoryInstance(memType);

            byte[][] value = new byte[][]
            {
                new byte[100],
                new byte[80_000],   // forces grow on second iteration
                new byte[50],
            };
            for (int i = 0; i < value[0].Length; i++) value[0][i] = 0xAA;
            for (int i = 0; i < value[1].Length; i++) value[1][i] = 0xBB;
            for (int i = 0; i < value[2].Length; i++) value[2][i] = 0xCC;

            int next = 32;
            Func<int, int, int, int, int> cabiRealloc =
                (oldPtr, oldSize, align, newSize) =>
                {
                    int aligned = (next + align - 1) & ~(align - 1);
                    int needed = aligned + newSize;
                    int curBytes = mem.Data.Length;
                    if (needed > curBytes)
                    {
                        int pages = (needed - curBytes + 65535) / 65536;
                        Assert.True(mem.Grow(pages));
                    }
                    next = needed;
                    return aligned;
                };

            PrimitiveStore.StoreByteArrayList(mem, 0, value, cabiRealloc);

            int outerPtr = BinaryPrimitives.ReadInt32LittleEndian(
                mem.Data.AsSpan(0, 4));
            int count = BinaryPrimitives.ReadInt32LittleEndian(
                mem.Data.AsSpan(4, 4));
            Assert.Equal(3, count);

            for (int i = 0; i < count; i++)
            {
                int subPtr = BinaryPrimitives.ReadInt32LittleEndian(
                    mem.Data.AsSpan(outerPtr + i * 8, 4));
                int subLen = BinaryPrimitives.ReadInt32LittleEndian(
                    mem.Data.AsSpan(outerPtr + i * 8 + 4, 4));
                Assert.Equal(value[i].Length, subLen);
                Assert.Equal(value[i][0], mem.Data[subPtr]);
                Assert.Equal(value[i][value[i].Length - 1],
                    mem.Data[subPtr + subLen - 1]);
            }
        }
    }
}
