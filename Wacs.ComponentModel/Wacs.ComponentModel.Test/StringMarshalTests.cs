// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Text;
using Wacs.ComponentModel.CanonicalABI;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
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
        // Helper: build a one-page MemoryInstance and copy `bytes`
        // into it at offset 0. Tests want a guest-memory shape they
        // can hand to the now-MemoryInstance-typed LiftXxx helpers.
        private static MemoryInstance NewMemoryWith(byte[] bytes, int offset = 0)
        {
            var mem = new MemoryInstance(new MemoryType(1));
            bytes.AsSpan().CopyTo(mem.AsSpan(offset, bytes.Length));
            return mem;
        }

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
                // buffer, then stage in a MemoryInstance and lift.
                // Simulates what the transpiler IL path does after
                // the core call.
                var roundtrip = new byte[len];
                System.Runtime.InteropServices.Marshal.Copy(
                    (System.IntPtr)addr, roundtrip, 0, len);
                var mem = NewMemoryWith(roundtrip);
                Assert.Equal(input, StringMarshal.LiftUtf8(mem, 0, len));
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
            // Simulates the transpiler/interpreter's guest memory
            // access: mem is the core module's linear memory,
            // (ptr, len) locates the string within.
            var payload = Encoding.UTF8.GetBytes("canonical");
            var mem = NewMemoryWith(payload, offset: 100);

            Assert.Equal("canonical",
                StringMarshal.LiftUtf8(mem, 100, payload.Length));
        }

        [Fact]
        public void LiftUtf8_rejects_out_of_range_span()
        {
            // MemoryInstance.AsSpan does the bounds check; for the
            // out-of-range cases below it surfaces as
            // ArgumentOutOfRangeException with a different paramName
            // ("offset" vs StringMarshal's old "ptr"), so just match
            // the type.
            var mem = new MemoryInstance(new MemoryType(1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StringMarshal.LiftUtf8(mem, 65530, 20));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StringMarshal.LiftUtf8(mem, -1, 5));
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
