// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Text;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Http;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>Unit tests for
    /// <see cref="ErrorCodeEncoder.Write"/>. Exercises each
    /// payload shape (no-payload, option<u32>, option<u64>,
    /// option<string>, DNSErrorPayload, TLSAlertReceived
    /// Payload, FieldSizePayload, option<FieldSizePayload>)
    /// using a stub allocator that bumps a cursor through a
    /// shared backing buffer.</summary>
    public class ErrorCodeEncoderTests
    {
        // Bump-pointer allocator simulating cabi_realloc.
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
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCode.ErrorCodeConnectionRefused(),
                alloc.Allocate);
            Assert.Equal(6, alloc.Memory[0]);
            for (int i = 1; i < 32; i++)
                Assert.Equal(0, alloc.Memory[i]);

            for (int i = 32; i < 64; i++) alloc.Memory[i] = 0xAA;
            ErrorCodeEncoder.Write(alloc.Memory, 32,
                new ErrorCode.ErrorCodeLoopDetected(),
                alloc.Allocate);
            Assert.Equal(36, alloc.Memory[32]);
            for (int i = 33; i < 64; i++)
                Assert.Equal(0, alloc.Memory[i]);
        }

        [Fact]
        public void HttpRequestBodySize_writes_option_u64_at_offset_8()
        {
            var alloc = new BumpAllocator(64, 32);
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCode.ErrorCodeHTTPRequestBodySize(
                    Option<ulong>.Some(0x0123456789ABCDEFUL)),
                alloc.Allocate);
            Assert.Equal(17, alloc.Memory[0]);
            Assert.Equal(1, alloc.Memory[8]);
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(
                alloc.Memory.AsSpan(16, 8));
            Assert.Equal(0x0123456789ABCDEFUL, v);

            for (int i = 0; i < 32; i++) alloc.Memory[i] = 0xAA;
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCode.ErrorCodeHTTPRequestBodySize(
                    Option<ulong>.None),
                alloc.Allocate);
            Assert.Equal(17, alloc.Memory[0]);
            Assert.Equal(0, alloc.Memory[8]);
        }

        [Fact]
        public void HttpRequestHeaderSectionSize_writes_option_u32()
        {
            var alloc = new BumpAllocator(64, 32);
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCode.ErrorCodeHTTPRequestHeaderSectionSize(
                    Option<uint>.Some(4096u)),
                alloc.Allocate);
            Assert.Equal(21, alloc.Memory[0]);
            Assert.Equal(1, alloc.Memory[8]);
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
                new ErrorCode.ErrorCodeInternalError(
                    Option<string>.Some(msg)),
                alloc.Allocate);
            Assert.Equal(38, alloc.Memory[0]);
            Assert.Equal(1, alloc.Memory[8]);
            int ptr = BinaryPrimitives.ReadInt32LittleEndian(
                alloc.Memory.AsSpan(12, 4));
            int len = BinaryPrimitives.ReadInt32LittleEndian(
                alloc.Memory.AsSpan(16, 4));
            Assert.True(ptr >= 64);
            Assert.Equal(msg.Length, len);
            Assert.Equal(msg, Encoding.UTF8.GetString(
                alloc.Memory, ptr, len));
        }

        [Fact]
        public void DnsError_writes_record_with_two_option_fields()
        {
            var alloc = new BumpAllocator(256, 64);
            var payload = new DNSErrorPayload {
                Rcode = Option<string>.Some("SERVFAIL"),
                InfoCode = Option<ushort>.Some((ushort)2),
            };
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCode.ErrorCodeDNSError(payload),
                alloc.Allocate);
            Assert.Equal(1, alloc.Memory[0]);
            Assert.Equal(1, alloc.Memory[8]);
            int ptr = BinaryPrimitives.ReadInt32LittleEndian(
                alloc.Memory.AsSpan(12, 4));
            int len = BinaryPrimitives.ReadInt32LittleEndian(
                alloc.Memory.AsSpan(16, 4));
            Assert.Equal("SERVFAIL",
                Encoding.UTF8.GetString(alloc.Memory, ptr, len));
            Assert.Equal(1, alloc.Memory[20]);
            ushort info = BinaryPrimitives.ReadUInt16LittleEndian(
                alloc.Memory.AsSpan(22, 2));
            Assert.Equal(2, info);
        }

        [Fact]
        public void TlsAlertReceived_writes_option_u8_then_option_string()
        {
            var alloc = new BumpAllocator(256, 64);
            var payload = new TLSAlertReceivedPayload {
                AlertId = Option<byte>.Some((byte)51),
                AlertMessage = Option<string>.Some("decrypt error"),
            };
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCode.ErrorCodeTLSAlertReceived(payload),
                alloc.Allocate);
            Assert.Equal(14, alloc.Memory[0]);
            Assert.Equal(1, alloc.Memory[8]);
            Assert.Equal(51, alloc.Memory[9]);
            Assert.Equal(0, alloc.Memory[10]);
            Assert.Equal(0, alloc.Memory[11]);
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
            var payload = new FieldSizePayload {
                FieldName = Option<string>.Some("Authorization"),
                FieldSize = Option<uint>.Some(8192u),
            };
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCode.ErrorCodeHTTPRequestHeaderSize(
                    Option<FieldSizePayload>.Some(payload)),
                alloc.Allocate);
            Assert.Equal(22, alloc.Memory[0]);
            Assert.Equal(1, alloc.Memory[8]);
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

            for (int i = 0; i < 32; i++) alloc.Memory[i] = 0xAA;
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCode.ErrorCodeHTTPRequestHeaderSize(
                    Option<FieldSizePayload>.None),
                alloc.Allocate);
            Assert.Equal(22, alloc.Memory[0]);
            Assert.Equal(0, alloc.Memory[8]);
        }

        [Fact]
        public void HttpResponseTransferCoding_writes_option_string_none()
        {
            var alloc = new BumpAllocator(64, 32);
            ErrorCodeEncoder.Write(alloc.Memory, 0,
                new ErrorCode.ErrorCodeHTTPResponseTransferCoding(
                    Option<string>.None),
                alloc.Allocate);
            Assert.Equal(31, alloc.Memory[0]);
            Assert.Equal(0, alloc.Memory[8]);
        }

        [Fact]
        public void Discriminants_match_spec_wit_case_indices()
        {
            Assert.Equal(0,
                ErrorCodeEncoder.Discriminant(
                    new ErrorCode.ErrorCodeDNSTimeout()));
            Assert.Equal(14,
                ErrorCodeEncoder.Discriminant(
                    new ErrorCode.ErrorCodeTLSAlertReceived(
                        new TLSAlertReceivedPayload())));
            Assert.Equal(17,
                ErrorCodeEncoder.Discriminant(
                    new ErrorCode.ErrorCodeHTTPRequestBodySize(
                        Option<ulong>.None)));
            Assert.Equal(22,
                ErrorCodeEncoder.Discriminant(
                    new ErrorCode.ErrorCodeHTTPRequestHeaderSize(
                        Option<FieldSizePayload>.None)));
            Assert.Equal(38,
                ErrorCodeEncoder.Discriminant(
                    new ErrorCode.ErrorCodeInternalError(
                        Option<string>.None)));
        }
    }
}
