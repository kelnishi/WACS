// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0.

using System;
using Wacs.HostBindings;
using Xunit;

namespace Wacs.HostBindings.Test
{
    /// <summary>
    /// Bounds-check + accessor coverage for the only piece of
    /// <c>Wacs.HostBindings.Abstractions</c> with non-trivial logic.
    /// </summary>
    public class WacsHostMemoryTests
    {
        [Fact]
        public void ReadByteRespectsLength()
        {
            var backing = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60 };
            var mem = new WacsHostMemory(backing, length: 4);

            Assert.Equal(0x10, mem.ReadByte(0));
            Assert.Equal(0x40, mem.ReadByte(3));
            // Length=4 hides the last two bytes even though they're in the array.
            Assert.Throws<WacsHostFault>(() => mem.ReadByte(4));
            Assert.Throws<WacsHostFault>(() => mem.ReadByte(-1));
        }

        [Fact]
        public void WriteByteRespectsLength()
        {
            var backing = new byte[8];
            var mem = new WacsHostMemory(backing, length: 8);

            mem.WriteByte(0, 0xAA);
            mem.WriteByte(7, 0xBB);
            Assert.Equal(0xAA, backing[0]);
            Assert.Equal(0xBB, backing[7]);
            Assert.Throws<WacsHostFault>(() => mem.WriteByte(8, 0xCC));
        }

        [Fact]
        public void Int32LERoundTrip()
        {
            var backing = new byte[16];
            var mem = new WacsHostMemory(backing, 16);

            mem.WriteInt32LE(4, unchecked((int)0xDEADBEEF));
            // Confirm the byte layout is little-endian (matches wasm).
            Assert.Equal(0xEF, backing[4]);
            Assert.Equal(0xBE, backing[5]);
            Assert.Equal(0xAD, backing[6]);
            Assert.Equal(0xDE, backing[7]);
            Assert.Equal(unchecked((int)0xDEADBEEF), mem.ReadInt32LE(4));
        }

        [Fact]
        public void Int32AccessorsBoundsCheck()
        {
            var mem = new WacsHostMemory(new byte[10], length: 10);
            // 4-byte access at offset 7: 7 + 4 = 11 > 10 → fault
            Assert.Throws<WacsHostFault>(() => mem.ReadInt32LE(7));
            Assert.Throws<WacsHostFault>(() => mem.WriteInt32LE(7, 0));
            // 4-byte access at offset 6: 6 + 4 = 10 = length → ok
            mem.WriteInt32LE(6, 0xCAFEBABE.ToInt32());
            Assert.Equal(0xCAFEBABE.ToInt32(), mem.ReadInt32LE(6));
        }

        [Fact]
        public void AsSpanAllowsBulkAccess()
        {
            var mem = new WacsHostMemory(new byte[16], 16);
            var span = mem.AsSpan(2, 6);
            for (int i = 0; i < span.Length; i++) span[i] = (byte)(i + 1);
            // Verify the writes landed in the backing array (no copy).
            for (int i = 0; i < 6; i++) Assert.Equal(i + 1, mem.ReadByte(2 + i));
        }

        [Fact]
        public void AsSpanBoundsCheck()
        {
            var mem = new WacsHostMemory(new byte[16], 16);
            Assert.Throws<WacsHostFault>(() => mem.AsSpan(15, 2));
            Assert.Throws<WacsHostFault>(() => mem.AsSpan(-1, 1));
            Assert.Throws<WacsHostFault>(() => mem.AsSpan(0, 17));
            // Zero-length at boundary is OK.
            var z = mem.AsSpan(16, 0);
            Assert.Equal(0, z.Length);
        }

        [Fact]
        public void RejectsLengthGreaterThanBackingArray()
        {
            var backing = new byte[8];
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new WacsHostMemory(backing, length: 9));
        }

        [Fact]
        public void NullDataThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => new WacsHostMemory(null!, length: 0));
        }

        // === NativePointer-mode coverage ===

        [Fact]
        public unsafe void NativePointer_ReadWriteByte_RoundTrips()
        {
            const int len = 16;
            var ptr = (byte*)System.Runtime.InteropServices.NativeMemory
                .AllocZeroed((nuint)len);
            try
            {
                var mem = new WacsHostMemory((IntPtr)ptr, len);
                Assert.True(mem.IsNative);
                Assert.Equal(len, mem.Length);

                mem.WriteByte(0, 0xAA);
                mem.WriteByte(15, 0xBB);
                Assert.Equal(0xAA, ptr[0]);
                Assert.Equal(0xBB, ptr[15]);
                Assert.Equal(0xAA, mem.ReadByte(0));
                Assert.Equal(0xBB, mem.ReadByte(15));

                Assert.Throws<WacsHostFault>(() => mem.ReadByte(16));
                Assert.Throws<WacsHostFault>(() => mem.WriteByte(-1, 0));
            }
            finally
            {
                System.Runtime.InteropServices.NativeMemory.Free(ptr);
            }
        }

        [Fact]
        public unsafe void NativePointer_AsSpan_RoundTrips()
        {
            const int len = 32;
            var ptr = (byte*)System.Runtime.InteropServices.NativeMemory
                .AllocZeroed((nuint)len);
            try
            {
                var mem = new WacsHostMemory((IntPtr)ptr, len);
                var span = mem.AsSpan(8, 16);
                for (int i = 0; i < span.Length; i++) span[i] = (byte)(i + 0xC0);

                // Verify writes hit native memory directly.
                for (int i = 0; i < 16; i++)
                    Assert.Equal((byte)(i + 0xC0), ptr[8 + i]);
            }
            finally
            {
                System.Runtime.InteropServices.NativeMemory.Free(ptr);
            }
        }

        [Fact]
        public unsafe void NativePointer_Int32LERoundTrip()
        {
            const int len = 16;
            var ptr = (byte*)System.Runtime.InteropServices.NativeMemory
                .AllocZeroed((nuint)len);
            try
            {
                var mem = new WacsHostMemory((IntPtr)ptr, len);
                mem.WriteInt32LE(4, unchecked((int)0xCAFEBABE));
                Assert.Equal(0xBE, ptr[4]);
                Assert.Equal(0xBA, ptr[5]);
                Assert.Equal(0xFE, ptr[6]);
                Assert.Equal(0xCA, ptr[7]);
                Assert.Equal(unchecked((int)0xCAFEBABE), mem.ReadInt32LE(4));
            }
            finally
            {
                System.Runtime.InteropServices.NativeMemory.Free(ptr);
            }
        }

        [Fact]
        public unsafe void NativePointer_Utf8String_RoundTrips()
        {
            const int len = 64;
            var ptr = (byte*)System.Runtime.InteropServices.NativeMemory
                .AllocZeroed((nuint)len);
            try
            {
                var mem = new WacsHostMemory((IntPtr)ptr, len);
                int written = mem.WriteUtf8String(8, "hello", nullTerminate: true);
                Assert.Equal(6, written);  // 5 bytes + nul
                Assert.Equal("hello", mem.ReadUtf8String(8, 5));
                Assert.Equal((byte)'h', ptr[8]);
                Assert.Equal((byte)0, ptr[13]);
            }
            finally
            {
                System.Runtime.InteropServices.NativeMemory.Free(ptr);
            }
        }

        [Fact]
        public void NativePointer_ZeroLength_AcceptsZeroPointer()
        {
            // Empty memory is a degenerate but valid case (no allocation).
            var mem = new WacsHostMemory(IntPtr.Zero, 0);
            Assert.True(mem.IsNative);
            Assert.Equal(0, mem.Length);
            Assert.Equal(0, mem.AsSpan(0, 0).Length);
            Assert.Throws<WacsHostFault>(() => mem.ReadByte(0));
        }

        [Fact]
        public void NativePointer_NonZeroLengthRequiresPointer()
        {
            Assert.Throws<ArgumentNullException>(
                () => new WacsHostMemory(IntPtr.Zero, 8));
        }
    }

    internal static class TestExt
    {
        public static int ToInt32(this uint u) => unchecked((int)u);
    }
}
