// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Text;
using Wacs.WASI.Preview2.Http;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>Unit tests for
    /// <see cref="ErrorCodeEncoder.Write"/>. Exercises each
    /// payload shape (no-payload, option<u32>, option<u64>,
    /// option<string>, DnsErrorPayload, TlsAlertReceived
    /// Payload, FieldSizePayload, option<FieldSizePayload>)
    /// using a stub allocator that bumps a cursor through a
    /// shared backing buffer.</summary>
    public class ErrorCodeEncoderTests
    {
        // Bump-pointer allocator simulating cabi_realloc.
        // Returns the next aligned offset and advances the
        // cursor; tests can read back from the same memory.
        private sealed class BumpAllocator
        {
            public readonly byte[] Memory;
            private int _next;

            public BumpAllocator(int size, int initialCursor)
            {
                Memory = new byte[size];
                _next = initialCursor;
            }

            public int Allocate(int align, int size)
            {
                int aligned = (_next + align - 1) & ~(align - 1);
                _next = aligned + size;
                return aligned;
            }
        }

        [Fact]
        public void NoPayload_writes_disc_only_and_zeroes_payload()
        {
            var alloc = new BumpAllocator(64, 32);
            // disc 6 = connection-refused, disc 36 = loop-detected
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCodeConnectionRefused(), alloc.Allocate);
            Assert.Equal(6, alloc.Memory[0]);
            // bytes 1..31 should all be zero
            for (int i = 1; i < 32; i++)
                Assert.Equal(0, alloc.Memory[i]);

            // Re-encode at offset 32 to verify pre-existing
            // garbage gets cleared.
            for (int i = 32; i < 64; i++) alloc.Memory[i] = 0xAA;
            ErrorCodeEncoder.Write(alloc.Memory, 32,
                new ErrorCodeLoopDetected(), alloc.Allocate);
            Assert.Equal(36, alloc.Memory[32]);
            for (int i = 33; i < 64; i++)
                Assert.Equal(0, alloc.Memory[i]);
        }

        [Fact]
        public void HttpRequestBodySize_writes_option_u64_at_offset_8()
        {
            var alloc = new BumpAllocator(64, 32);
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCodeHttpRequestBodySize(0x0123456789ABCDEFUL),
                alloc.Allocate);
            Assert.Equal(17, alloc.Memory[0]);   // disc
            Assert.Equal(1, alloc.Memory[8]);    // Some
            // u64 lives at offset 8 + 8 = 16
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(
                alloc.Memory.AsSpan(16, 8));
            Assert.Equal(0x0123456789ABCDEFUL, v);

            // Now None
            for (int i = 0; i < 32; i++) alloc.Memory[i] = 0xAA;
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCodeHttpRequestBodySize(null),
                alloc.Allocate);
            Assert.Equal(17, alloc.Memory[0]);
            Assert.Equal(0, alloc.Memory[8]);    // None
        }

        [Fact]
        public void HttpRequestHeaderSectionSize_writes_option_u32()
        {
            var alloc = new BumpAllocator(64, 32);
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCodeHttpRequestHeaderSectionSize(4096u),
                alloc.Allocate);
            Assert.Equal(21, alloc.Memory[0]);
            Assert.Equal(1, alloc.Memory[8]);    // option Some
            // u32 at +8+4 = +12
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(
                alloc.Memory.AsSpan(12, 4));
            Assert.Equal(4096u, v);
        }

        [Fact]
        public void InternalError_writes_option_string_with_allocated_buffer()
        {
            var alloc = new BumpAllocator(256, 64);
            var msg = "host: connection torn";
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCodeInternalError(msg),
                alloc.Allocate);
            Assert.Equal(38, alloc.Memory[0]);
            Assert.Equal(1, alloc.Memory[8]);   // option Some
            // option<string> ptr at offset+4, len at offset+8
            // (within the option block, so absolute 8+4=12 / 8+8=16).
            int ptr = BinaryPrimitives.ReadInt32LittleEndian(
                alloc.Memory.AsSpan(12, 4));
            int len = BinaryPrimitives.ReadInt32LittleEndian(
                alloc.Memory.AsSpan(16, 4));
            Assert.True(ptr >= 64,
                "string buffer should be allocated via allocate()");
            Assert.Equal(msg.Length, len);
            Assert.Equal(msg, Encoding.UTF8.GetString(
                alloc.Memory, ptr, len));
        }

        [Fact]
        public void DnsError_writes_record_with_two_option_fields()
        {
            var alloc = new BumpAllocator(256, 64);
            var payload = new DnsErrorPayload(
                rcode: "SERVFAIL", infoCode: 2);
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCodeDnsError(payload), alloc.Allocate);
            Assert.Equal(1, alloc.Memory[0]);    // disc
            // DnsErrorPayload starts at offset 8.
            // rcode (option<string>) at +8: disc + 3 padding +
            // ptr@+12 + len@+16
            Assert.Equal(1, alloc.Memory[8]);
            int ptr = BinaryPrimitives.ReadInt32LittleEndian(
                alloc.Memory.AsSpan(12, 4));
            int len = BinaryPrimitives.ReadInt32LittleEndian(
                alloc.Memory.AsSpan(16, 4));
            Assert.Equal("SERVFAIL",
                Encoding.UTF8.GetString(alloc.Memory, ptr, len));
            // info-code (option<u16>) at +8+12 = +20: disc + 1B
            // padding + 2B value
            Assert.Equal(1, alloc.Memory[20]);
            ushort info = BinaryPrimitives.ReadUInt16LittleEndian(
                alloc.Memory.AsSpan(22, 2));
            Assert.Equal(2, info);
        }

        [Fact]
        public void TlsAlertReceived_writes_option_u8_then_option_string()
        {
            var alloc = new BumpAllocator(256, 64);
            var payload = new TlsAlertReceivedPayload(
                alertId: 51, alertMessage: "decrypt error");
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCodeTlsAlertReceived(payload),
                alloc.Allocate);
            Assert.Equal(14, alloc.Memory[0]);
            // alert-id (option<u8>) at +8: 1 + value
            Assert.Equal(1, alloc.Memory[8]);
            Assert.Equal(51, alloc.Memory[9]);
            // padding bytes 10..11 should be zero
            Assert.Equal(0, alloc.Memory[10]);
            Assert.Equal(0, alloc.Memory[11]);
            // alert-message (option<string>) at +12: disc + 3B
            // padding + ptr@+16 + len@+20
            Assert.Equal(1, alloc.Memory[12]);
            int ptr = BinaryPrimitives.ReadInt32LittleEndian(
                alloc.Memory.AsSpan(16, 4));
            int len = BinaryPrimitives.ReadInt32LittleEndian(
                alloc.Memory.AsSpan(20, 4));
            Assert.Equal("decrypt error",
                Encoding.UTF8.GetString(alloc.Memory, ptr, len));
        }

        [Fact]
        public void HttpRequestHeaderSize_writes_option_field_size_payload()
        {
            var alloc = new BumpAllocator(256, 64);
            // Some(field-size-payload { Authorization, 8192 })
            var payload = new FieldSizePayload(
                fieldName: "Authorization", fieldSize: 8192u);
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCodeHttpRequestHeaderSize(payload),
                alloc.Allocate);
            Assert.Equal(22, alloc.Memory[0]);
            // outer option disc at +8 (the variant payload area)
            Assert.Equal(1, alloc.Memory[8]);
            // FieldSizePayload starts at +12 (disc=1 + 3 padding):
            //   field-name option<string> at +12 (disc + 3 pad +
            //                                     ptr@16 + len@20)
            //   field-size option<u32> at +24 (disc + 3 pad +
            //                                   value@28)
            Assert.Equal(1, alloc.Memory[12]);
            int ptr = BinaryPrimitives.ReadInt32LittleEndian(
                alloc.Memory.AsSpan(16, 4));
            int len = BinaryPrimitives.ReadInt32LittleEndian(
                alloc.Memory.AsSpan(20, 4));
            Assert.Equal("Authorization",
                Encoding.UTF8.GetString(alloc.Memory, ptr, len));
            Assert.Equal(1, alloc.Memory[24]);
            uint sz = BinaryPrimitives.ReadUInt32LittleEndian(
                alloc.Memory.AsSpan(28, 4));
            Assert.Equal(8192u, sz);

            // None outer
            for (int i = 0; i < 32; i++) alloc.Memory[i] = 0xAA;
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCodeHttpRequestHeaderSize(null),
                alloc.Allocate);
            Assert.Equal(22, alloc.Memory[0]);
            Assert.Equal(0, alloc.Memory[8]);  // outer None
        }

        [Fact]
        public void HttpResponseTransferCoding_writes_option_string_none()
        {
            var alloc = new BumpAllocator(64, 32);
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCodeHttpResponseTransferCoding(null),
                alloc.Allocate);
            Assert.Equal(31, alloc.Memory[0]);
            Assert.Equal(0, alloc.Memory[8]);   // option None
        }

        [Fact]
        public void Discriminants_match_spec_wit_case_indices()
        {
            // Quick consistency check that no case got
            // mis-numbered when typing out 39 sealed classes.
            Assert.Equal(0, new ErrorCodeDnsTimeout().Discriminant);
            Assert.Equal(14,
                new ErrorCodeTlsAlertReceived(
                    new TlsAlertReceivedPayload(null, null)
                ).Discriminant);
            Assert.Equal(17,
                new ErrorCodeHttpRequestBodySize(null).Discriminant);
            Assert.Equal(22,
                new ErrorCodeHttpRequestHeaderSize(null).Discriminant);
            Assert.Equal(38,
                new ErrorCodeInternalError(null).Discriminant);
        }
    }
}
