// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Runtime.InteropServices;
using Wacs.ComponentModel.CanonicalABI;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Contract tests for the ListMarshal canonical-ABI helper.
    /// Pins the byte-level encoding (little-endian, tightly
    /// packed, elemSize-aligned) so adapters routing through
    /// these methods inherit the correct behavior without
    /// re-verifying per call site.
    /// </summary>
    public class ListMarshalTests
    {
        // Helper: build a one-page MemoryInstance with `bytes` copied
        // in at `offset`. Tests want a guest-memory shape they can
        // hand to the now-MemoryInstance-typed LiftPrim helpers.
        private static MemoryInstance NewMemoryWith(byte[] bytes, int offset = 0)
        {
            var mem = new MemoryInstance(new MemoryType(1));
            bytes.AsSpan().CopyTo(mem.AsSpan(offset, bytes.Length));
            return mem;
        }

        [Fact]
        public void LowerPrim_u32_pins_address_and_reports_count()
        {
            var values = new uint[] { 0x11223344, 0x55667788, 0xDEADBEEF };
            var h = ListMarshal.LowerPrim(values, out var addr, out var count);
            try
            {
                Assert.Equal(values.Length, count);
                Assert.NotEqual(IntPtr.Zero, (IntPtr)addr);
                // Pinned memory should contain the exact little-
                // endian byte representation of each u32.
                var probe = new byte[4];
                Marshal.Copy((IntPtr)addr, probe, 0, 4);
                Assert.Equal(0x44, probe[0]);
                Assert.Equal(0x33, probe[1]);
                Assert.Equal(0x22, probe[2]);
                Assert.Equal(0x11, probe[3]);
            }
            finally { h.Free(); }
        }

        [Fact]
        public void LiftPrim_u32_roundtrips_through_byte_array()
        {
            var original = new uint[] { 1, 2, 3, 42, 0xFFFFFFFF };
            // Build the byte blob the same way guest memory would
            // contain it — 4 bytes per element, little-endian.
            var raw = new byte[20];
            for (int i = 0; i < original.Length; i++)
                BitConverter.GetBytes(original[i]).CopyTo(raw, i * 4);
            var mem = NewMemoryWith(raw, offset: 4);

            var lifted = ListMarshal.LiftPrim<uint>(mem, 4, original.Length);
            Assert.Equal(original, lifted);
        }

        [Fact]
        public void LiftPrim_u8_reads_bytes_directly()
        {
            var raw = new byte[] {
                0x00, 0x01, 0x02, 0x10, 0x20, 0x30, 0xFF
            };
            var mem = NewMemoryWith(raw);
            var lifted = ListMarshal.LiftPrim<byte>(mem, 3, 3);
            Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, lifted);
        }

        [Fact]
        public void LiftPrim_u64_unpacks_8_byte_elements()
        {
            var original = new ulong[] { 0x0123456789ABCDEFUL, 0xFEDCBA9876543210UL };
            var raw = new byte[16];
            for (int i = 0; i < original.Length; i++)
                BitConverter.GetBytes(original[i]).CopyTo(raw, i * 8);
            var mem = NewMemoryWith(raw);
            var lifted = ListMarshal.LiftPrim<ulong>(mem, 0, original.Length);
            Assert.Equal(original, lifted);
        }

        [Fact]
        public void LiftPrim_rejects_out_of_range_span()
        {
            // MemoryInstance.AsSpan does the bounds check; tests
            // exercise the throw-on-overrun shape against a single
            // page to avoid materializing larger backing.
            var mem = new MemoryInstance(new MemoryType(1));
            // Past end of the page.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ListMarshal.LiftPrim<uint>(mem, 65530, 5));
            // Negative offset.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ListMarshal.LiftPrim<uint>(mem, -1, 1));
        }

        [Fact]
        public void LiftPrim_span_overload_validates_length()
        {
            var bytes = new byte[7];
            // 7 bytes isn't a whole multiple of sizeof(uint)=4.
            Assert.Throws<ArgumentException>(
                () => ListMarshal.LiftPrim<uint>(bytes, 2));
        }

        [Fact]
        public void LowerPrim_then_LiftPrim_roundtrips_mixed_widths()
        {
            var original = new short[] { -1, 0, 1, 32767, -32768 };
            var h = ListMarshal.LowerPrim(original, out var addr, out var count);
            try
            {
                // Simulate guest-side read-back: copy the pinned
                // bytes into a managed buffer and lift through
                // the bytes-only overload.
                var bytes = new byte[count * 2];
                Marshal.Copy((IntPtr)addr, bytes, 0, bytes.Length);
                var mem = NewMemoryWith(bytes);
                var lifted = ListMarshal.LiftPrim<short>(mem, 0, count);
                Assert.Equal(original, lifted);
            }
            finally { h.Free(); }
        }
    }
}
