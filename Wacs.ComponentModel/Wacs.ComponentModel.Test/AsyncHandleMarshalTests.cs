// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.CanonicalABI;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Round-trip read/write coverage for the three async-ABI
    /// handle helpers (<see cref="StreamMarshal"/> /
    /// <see cref="FutureMarshal"/> /
    /// <see cref="ErrorContextMarshal"/>). All three share the
    /// same wire shape — 4-byte little-endian i32 — so the
    /// tests target each helper's surface uniformly.
    /// </summary>
    public class AsyncHandleMarshalTests
    {
        [Fact]
        public void StreamMarshal_round_trips_handle()
        {
            Span<byte> buf = stackalloc byte[StreamMarshal.HandleSize];
            StreamMarshal.WriteHandle(buf, 0, 0x12345678);
            Assert.Equal(0x12345678, StreamMarshal.ReadHandle(buf, 0));
        }

        [Fact]
        public void FutureMarshal_round_trips_handle()
        {
            Span<byte> buf = stackalloc byte[FutureMarshal.HandleSize];
            FutureMarshal.WriteHandle(buf, 0, -1);
            Assert.Equal(-1, FutureMarshal.ReadHandle(buf, 0));
        }

        [Fact]
        public void ErrorContextMarshal_round_trips_handle()
        {
            Span<byte> buf = stackalloc byte[ErrorContextMarshal.HandleSize];
            ErrorContextMarshal.WriteHandle(buf, 0, 7);
            Assert.Equal(7, ErrorContextMarshal.ReadHandle(buf, 0));
        }

        [Fact]
        public void All_handles_are_4_bytes_4_aligned()
        {
            Assert.Equal(4, StreamMarshal.HandleSize);
            Assert.Equal(4, StreamMarshal.HandleAlign);
            Assert.Equal(4, FutureMarshal.HandleSize);
            Assert.Equal(4, FutureMarshal.HandleAlign);
            Assert.Equal(4, ErrorContextMarshal.HandleSize);
            Assert.Equal(4, ErrorContextMarshal.HandleAlign);
        }

        [Fact]
        public void StreamMarshal_rejects_out_of_bounds_read()
        {
            byte[] tooSmall = new byte[3];
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                StreamMarshal.ReadHandle(tooSmall, 0));
        }

        [Fact]
        public void StreamMarshal_writes_at_nonzero_offset()
        {
            // Handle at offset 4 within an 8-byte buffer — verifies
            // the helper uses the offset rather than baking 0.
            Span<byte> buf = stackalloc byte[8];
            StreamMarshal.WriteHandle(buf, 4, 0x42);
            Assert.Equal(0, StreamMarshal.ReadHandle(buf, 0));
            Assert.Equal(0x42, StreamMarshal.ReadHandle(buf, 4));
        }
    }
}
