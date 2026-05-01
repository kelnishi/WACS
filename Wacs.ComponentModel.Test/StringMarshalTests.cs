// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Text;
using Wacs.ComponentModel.CanonicalABI;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// StringMarshal is the single chokepoint at which
    /// System.String crosses the canonical-ABI boundary. These
    /// tests pin its contract so adapter emitters (CSharpEmit,
    /// transpiler IL, interpreter binding) can rely on the
    /// behavior without re-verifying in each path.
    ///
    /// <para>When the JS-string externref variant eventually
    /// lands, siblings to these will assert its parallel
    /// contract without changing this UTF-8 suite.</para>
    /// </summary>
    public class StringMarshalTests
    {
        [Fact]
        public void LowerUtf8_roundtrips_ascii_via_lift()
        {
            var input = "Hello, World!";
            var handle = StringMarshal.LowerUtf8(input, out var addr, out var len);
            try
            {
                Assert.Equal(Encoding.UTF8.GetByteCount(input), len);
                Assert.NotEqual(System.IntPtr.Zero, (System.IntPtr)addr);

                // Copy out via the pinned address into a managed
                // buffer, then lift. Simulates what the transpiler
                // IL path does after the core call.
                var roundtrip = new byte[len];
                System.Runtime.InteropServices.Marshal.Copy(
                    (System.IntPtr)addr, roundtrip, 0, len);
                Assert.Equal(input, StringMarshal.LiftUtf8(roundtrip, 0, len));
            }
            finally
            {
                handle.Free();
            }
        }

        [Fact]
        public void LowerUtf8_handles_multibyte_characters()
        {
            // Non-ASCII UTF-8: π is 2 bytes (0xCF 0x80), smiley
            // face is 4 bytes (surrogate pair → 4 UTF-8 bytes).
            var input = "π 😀";
            var handle = StringMarshal.LowerUtf8(input, out _, out var len);
            try
            {
                Assert.Equal(Encoding.UTF8.GetByteCount(input), len);
                Assert.True(len > input.Length,
                    "Multi-byte UTF-8 should produce more bytes than chars.");
            }
            finally
            {
                handle.Free();
            }
        }

        [Fact]
        public void LiftUtf8_byte_array_decodes_span_at_offset()
        {
            // Simulates the transpiler/interpreter's guest
            // memory access: bytes is the core module's linear
            // memory, (ptr, len) locates the string within.
            var memory = new byte[256];
            var payload = Encoding.UTF8.GetBytes("canonical");
            Array.Copy(payload, 0, memory, 100, payload.Length);

            Assert.Equal("canonical",
                StringMarshal.LiftUtf8(memory, 100, payload.Length));
        }

        [Fact]
        public void LiftUtf8_rejects_out_of_range_span()
        {
            var memory = new byte[16];
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StringMarshal.LiftUtf8(memory, 10, 20));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StringMarshal.LiftUtf8(memory, -1, 5));
        }

        [Fact]
        public void LiftUtf8_span_decodes_readonlyspan_directly()
        {
            var bytes = Encoding.UTF8.GetBytes("span-lift");
            Assert.Equal("span-lift",
                StringMarshal.LiftUtf8(bytes.AsSpan()));
        }

        [Fact]
        public void LowerUtf8_empty_string_yields_zero_length()
        {
            var handle = StringMarshal.LowerUtf8("", out _, out var len);
            try
            {
                Assert.Equal(0, len);
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
