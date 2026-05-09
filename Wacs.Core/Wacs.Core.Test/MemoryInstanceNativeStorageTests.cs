// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Pins the contract for <see cref="MemoryStorageMode.NativePointer"/>:
    /// allocation, indexer + AsSpan parity with the byte[] backing,
    /// grow preserves live bytes and zero-fills the new pages, and
    /// Dispose actually releases the native buffer.
    ///
    /// <para>Phase A from <c>wasi-nn/WACS-GAPS.md</c> gap 12 — the
    /// migration from byte[] (2 GiB cap) to native-pointer storage.
    /// Phase A only adds the storage mode; subsequent phases migrate
    /// the interpreter and transpiler access sites to consult it.</para>
    /// </summary>
    public unsafe class MemoryInstanceNativeStorageTests
    {
        [Fact]
        public void NativePointer_Ctor_AllocatesZeroedPage()
        {
            var memType = new MemoryType(1, 10);
            using var mem = new MemoryInstance(memType,
                MemoryStorageMode.NativePointer);

            Assert.Equal(MemoryStorageMode.NativePointer, mem.StorageMode);
            Assert.True(mem.NativeBase != null);
            Assert.Equal((nuint)65536, mem.NativeSize);
            Assert.Equal(1, mem.Size);
            Assert.Equal((nuint)65536, mem.ByteLength);

            // Zero-initialized — every byte should be 0.
            for (int i = 0; i < 65536; i += 1024)
                Assert.Equal((byte)0, mem.NativeBase[i]);

            // Sentinel managed array stays empty so accidental
            // Data[i] reads fail loudly instead of zero-reading.
            Assert.Empty(mem.Data);
        }

        [Fact]
        public void ManagedArray_Ctor_DefaultsByteArray()
        {
            var memType = new MemoryType(1, 10);
            using var mem = new MemoryInstance(memType);  // default mode

            Assert.Equal(MemoryStorageMode.ManagedArray, mem.StorageMode);
            Assert.Equal(65536, mem.Data.Length);
            Assert.Equal((nuint)65536, mem.ByteLength);
            Assert.Equal(1, mem.Size);
        }

        [Fact]
        public void NativePointer_AsSpan_ReadWrite_RoundTrips()
        {
            var memType = new MemoryType(1, 10);
            using var mem = new MemoryInstance(memType,
                MemoryStorageMode.NativePointer);

            var span = mem.AsSpan(100, 4);
            span[0] = 0xDE;
            span[1] = 0xAD;
            span[2] = 0xBE;
            span[3] = 0xEF;

            // Read via raw pointer to confirm the writes hit native
            // memory (not some shadow buffer).
            Assert.Equal(0xDE, mem.NativeBase[100]);
            Assert.Equal(0xAD, mem.NativeBase[101]);
            Assert.Equal(0xBE, mem.NativeBase[102]);
            Assert.Equal(0xEF, mem.NativeBase[103]);

            // AsSpan re-read returns the same bytes.
            var readback = mem.AsSpan(100, 4);
            Assert.Equal(0xDE, readback[0]);
            Assert.Equal(0xEF, readback[3]);
        }

        [Fact]
        public void NativePointer_Indexer_RangeSlice_RoundTrips()
        {
            var memType = new MemoryType(1, 10);
            using var mem = new MemoryInstance(memType,
                MemoryStorageMode.NativePointer);

            var span = mem[200..208];
            for (int i = 0; i < span.Length; i++) span[i] = (byte)(0xA0 + i);

            for (int i = 0; i < 8; i++)
                Assert.Equal((byte)(0xA0 + i), mem.NativeBase[200 + i]);
        }

        [Fact]
        public void NativePointer_Grow_PreservesLiveBytes_ZeroFillsNew()
        {
            var memType = new MemoryType(1, 10);
            using var mem = new MemoryInstance(memType,
                MemoryStorageMode.NativePointer);

            // Stamp signature bytes near the end of page 0 + start.
            mem.AsSpan(0, 4).CopyTo(new byte[] { 1, 2, 3, 4 });
            mem.NativeBase[0] = 0x11;
            mem.NativeBase[65535] = 0x22;

            byte* preGrowBase = mem.NativeBase;
            Assert.True(mem.Grow(2));  // +2 pages → 3 pages
            Assert.Equal(3, mem.Size);
            Assert.Equal((nuint)(3 * 65536), mem.NativeSize);
            // Realloc should have moved the buffer (or at least not
            // left the same pointer with zeroed signature bytes).
            Assert.Equal(0x11, mem.NativeBase[0]);
            Assert.Equal(0x22, mem.NativeBase[65535]);

            // New pages are zero-initialized.
            Assert.Equal(0, mem.NativeBase[65536]);
            Assert.Equal(0, mem.NativeBase[3 * 65536 - 1]);

            // The freed buffer should not be aliased to the live
            // one; if alloc happened to recycle the same address
            // the test would still pass on byte content but flag
            // unintended aliasing here is informational.
            _ = preGrowBase;
        }

        [Fact]
        public void NativePointer_Grow_RejectedAtHostMaxPages()
        {
            // HostMaxPages = 0x8000 = 32768 pages = 2 GiB. Ask for
            // one page over the cap; Grow must refuse and leave the
            // memory unchanged.
            var memType = new MemoryType(1, Constants.HostMaxPages);
            using var mem = new MemoryInstance(memType,
                MemoryStorageMode.NativePointer);

            // numPages = HostMaxPages would put newNumPages at
            // HostMaxPages + 1 — over the cap.
            Assert.False(mem.Grow(Constants.HostMaxPages));
            Assert.Equal(1, mem.Size);
            Assert.Equal((nuint)65536, mem.NativeSize);
        }

        [Fact]
        public void NativePointer_Dispose_FreesAndIsIdempotent()
        {
            var memType = new MemoryType(1, 10);
            var mem = new MemoryInstance(memType,
                MemoryStorageMode.NativePointer);
            Assert.True(mem.NativeBase != null);

            mem.Dispose();
            Assert.True(mem.NativeBase == null);
            Assert.Equal((nuint)0, mem.NativeSize);

            // Idempotent — second Dispose is a no-op.
            mem.Dispose();
            Assert.True(mem.NativeBase == null);
        }

        [Fact]
        public void ManagedArray_Dispose_NoOpForGCBacking()
        {
            // ManagedArray mode has no native buffer to free; Dispose
            // is a no-op (the GC reclaims Data when the instance
            // becomes unreachable). This test pins that contract so
            // future "always free" refactors don't break ManagedArray.
            var memType = new MemoryType(1, 10);
            var mem = new MemoryInstance(memType);
            int prevDataLen = mem.Data.Length;
            mem.Dispose();
            // Data unchanged; the GC will reclaim it later.
            Assert.Equal(prevDataLen, mem.Data.Length);
        }
    }
}
