// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Buffers.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.CanonicalAbi;
using Wacs.WASI.Preview3.Http;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice HH coverage: typed-payload arms of the
    /// wasi:http/error-code variant when surfaced through the
    /// 40-byte 8-aligned result&lt;response, error-code&gt;
    /// retptr from client.send / handler.handle. Each test
    /// throws an HttpException with the per-arm payload fields
    /// set, then asserts the wire layout at +16..40 (the
    /// 24-byte typed-payload section, offset +8 within the
    /// error-code variant which itself sits at +8 within the
    /// result retptr).
    /// </summary>
    public class HttpErrorCodeTypedPayloadTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        private sealed class SequentialRealloc : ICabiRealloc
        {
            private int _next;
            public SequentialRealloc(int baseAddr) { _next = baseAddr; }
            public int Allocate(int align, int size)
            {
                _next = (_next + (align - 1)) & ~(align - 1);
                int ptr = _next;
                _next += size;
                return ptr;
            }
        }

        private sealed class ThrowingHandler : IHandler
        {
            private readonly HttpException _ex;
            public ThrowingHandler(HttpException ex) { _ex = ex; }
            public Task<IResponse> HandleAsync(
                IRequest request,
                CancellationToken cancellationToken = default)
                => Task.FromException<IResponse>(_ex);
        }

        private static (AsyncDispatcher disp, WasiPreview3Host host,
            SequentialRealloc realloc, int reqHandle)
            ArrangeWithThrowingHandler(HttpException ex)
        {
            var disp = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    HttpHandler = new ThrowingHandler(ex),
                })
            { Dispatcher = disp };
            int reqHandle = host.RequestHandles.Allocate(
                new Request(new Fields()));
            return (disp, host, new SequentialRealloc(1024), reqHandle);
        }

        private static byte ReadByte(MemoryInstance m, int o)
            => m.AsSpan(o, 1)[0];
        private static ushort ReadU16(MemoryInstance m, int o)
            => BinaryPrimitives.ReadUInt16LittleEndian(m.AsSpan(o, 2));
        private static uint ReadU32(MemoryInstance m, int o)
            => BinaryPrimitives.ReadUInt32LittleEndian(m.AsSpan(o, 4));
        private static ulong ReadU64(MemoryInstance m, int o)
            => BinaryPrimitives.ReadUInt64LittleEndian(m.AsSpan(o, 8));
        private static int ReadI32(MemoryInstance m, int o)
            => BinaryPrimitives.ReadInt32LittleEndian(m.AsSpan(o, 4));
        private static string ReadString(MemoryInstance m, int ptr, int len)
        {
            byte[] b = new byte[len];
            m.AsSpan(ptr, len).CopyTo(b);
            return Encoding.UTF8.GetString(b);
        }

        // The result retptr layout:
        //   +0:  result-disc
        //   +8:  error-code variant disc
        //   +16: typed payload starts here (24 bytes max)

        [Fact]
        public void DnsError_encodes_rcode_string_into_option_string_at_payload()
        {
            var (disp, host, realloc, req) =
                ArrangeWithThrowingHandler(
                    new HttpException(HttpErrorCode.DnsError)
                    { DnsRcode = "NXDOMAIN" });

            const int retptr = 64;
            host.InvokeHandlerHandle(req, retptr, realloc);

            Assert.Equal(1, ReadByte(disp.Memory, retptr));
            Assert.Equal((byte)HttpErrorCode.DnsError,
                ReadByte(disp.Memory, retptr + 8));
            // option<string> at +16..28: disc=1, ptr/len at +20..28
            Assert.Equal(1, ReadByte(disp.Memory, retptr + 16));
            int strPtr = ReadI32(disp.Memory, retptr + 20);
            int strLen = ReadI32(disp.Memory, retptr + 24);
            Assert.Equal("NXDOMAIN", ReadString(disp.Memory, strPtr, strLen));
        }

        [Fact]
        public void TlsAlertReceived_encodes_alert_id_and_message()
        {
            var (disp, host, realloc, req) =
                ArrangeWithThrowingHandler(
                    new HttpException(HttpErrorCode.TlsAlertReceived)
                    {
                        TlsAlertId = 80,
                        TlsAlertMessage = "internal_error",
                    });

            const int retptr = 64;
            host.InvokeHandlerHandle(req, retptr, realloc);

            Assert.Equal(1, ReadByte(disp.Memory, retptr));
            // option<u8> alert-id at +16..18
            Assert.Equal(1, ReadByte(disp.Memory, retptr + 16));
            Assert.Equal(80, ReadByte(disp.Memory, retptr + 17));
            // option<string> alert-message at +20..32 (padded to align-4 after option<u8>)
            Assert.Equal(1, ReadByte(disp.Memory, retptr + 20));
            int msgPtr = ReadI32(disp.Memory, retptr + 24);
            int msgLen = ReadI32(disp.Memory, retptr + 28);
            Assert.Equal("internal_error",
                ReadString(disp.Memory, msgPtr, msgLen));
        }

        [Fact]
        public void HttpRequestBodySize_encodes_option_u64()
        {
            var (disp, host, realloc, req) =
                ArrangeWithThrowingHandler(
                    new HttpException(HttpErrorCode.HttpRequestBodySize)
                    { BodySize = 0xDEADBEEFCAFEBABEUL });

            const int retptr = 64;
            host.InvokeHandlerHandle(req, retptr, realloc);

            Assert.Equal(1, ReadByte(disp.Memory, retptr));
            Assert.Equal((byte)HttpErrorCode.HttpRequestBodySize,
                ReadByte(disp.Memory, retptr + 8));
            // option<u64> at +16..32: disc=1 at +16, u64 at +24..32
            Assert.Equal(1, ReadByte(disp.Memory, retptr + 16));
            Assert.Equal(0xDEADBEEFCAFEBABEUL,
                ReadU64(disp.Memory, retptr + 24));
        }

        [Fact]
        public void HttpRequestBodySize_with_none_encodes_zero_disc()
        {
            var (disp, host, realloc, req) =
                ArrangeWithThrowingHandler(
                    new HttpException(HttpErrorCode.HttpRequestBodySize));

            const int retptr = 64;
            host.InvokeHandlerHandle(req, retptr, realloc);

            Assert.Equal(0, ReadByte(disp.Memory, retptr + 16));
            // u64 stays zero too
            Assert.Equal(0UL, ReadU64(disp.Memory, retptr + 24));
        }

        [Fact]
        public void HttpRequestHeaderSectionSize_encodes_option_u32()
        {
            var (disp, host, realloc, req) =
                ArrangeWithThrowingHandler(
                    new HttpException(
                        HttpErrorCode.HttpRequestHeaderSectionSize)
                    { FieldSize = 8192 });

            const int retptr = 64;
            host.InvokeHandlerHandle(req, retptr, realloc);

            // option<u32> at +16..24: disc=1 at +16, u32 at +20..24
            Assert.Equal(1, ReadByte(disp.Memory, retptr + 16));
            Assert.Equal(8192u, ReadU32(disp.Memory, retptr + 20));
        }

        [Fact]
        public void HttpRequestHeaderSize_encodes_option_field_size_payload()
        {
            var (disp, host, realloc, req) =
                ArrangeWithThrowingHandler(
                    new HttpException(
                        HttpErrorCode.HttpRequestHeaderSize)
                    {
                        FieldName = "x-large-header",
                        FieldSize = 16384,
                    });

            const int retptr = 64;
            host.InvokeHandlerHandle(req, retptr, realloc);

            // option<field-size-payload> at +16..40:
            //   +16: opt-disc (u8) + 3 pad
            //   +20..40: field-size-payload (20 bytes)
            //     +20..32: option<string> field-name
            //     +32..40: option<u32> field-size
            Assert.Equal(1, ReadByte(disp.Memory, retptr + 16));
            // option<string> field-name at +20..32
            Assert.Equal(1, ReadByte(disp.Memory, retptr + 20));
            int nPtr = ReadI32(disp.Memory, retptr + 24);
            int nLen = ReadI32(disp.Memory, retptr + 28);
            Assert.Equal("x-large-header",
                ReadString(disp.Memory, nPtr, nLen));
            // option<u32> field-size at +32..40
            Assert.Equal(1, ReadByte(disp.Memory, retptr + 32));
            Assert.Equal(16384u, ReadU32(disp.Memory, retptr + 36));
        }

        [Fact]
        public void HttpResponseHeaderSize_encodes_bare_field_size_payload()
        {
            // *HeaderSize / *TrailerSize on response take the
            // record directly (not wrapped in option<...>).
            var (disp, host, realloc, req) =
                ArrangeWithThrowingHandler(
                    new HttpException(
                        HttpErrorCode.HttpResponseHeaderSize)
                    {
                        FieldName = "set-cookie",
                        FieldSize = 65000,
                    });

            const int retptr = 64;
            host.InvokeHandlerHandle(req, retptr, realloc);

            // Bare field-size-payload at +16..36:
            //   +16..28: option<string> field-name
            //   +28..36: option<u32> field-size
            Assert.Equal(1, ReadByte(disp.Memory, retptr + 16));
            int nPtr = ReadI32(disp.Memory, retptr + 20);
            int nLen = ReadI32(disp.Memory, retptr + 24);
            Assert.Equal("set-cookie",
                ReadString(disp.Memory, nPtr, nLen));
            Assert.Equal(1, ReadByte(disp.Memory, retptr + 28));
            Assert.Equal(65000u, ReadU32(disp.Memory, retptr + 32));
        }

        [Fact]
        public void HttpResponseTransferCoding_encodes_option_string()
        {
            var (disp, host, realloc, req) =
                ArrangeWithThrowingHandler(
                    new HttpException(
                        HttpErrorCode.HttpResponseTransferCoding)
                    { OtherPayload = "chunked-unknown" });

            const int retptr = 64;
            host.InvokeHandlerHandle(req, retptr, realloc);

            Assert.Equal(1, ReadByte(disp.Memory, retptr + 16));
            int strPtr = ReadI32(disp.Memory, retptr + 20);
            int strLen = ReadI32(disp.Memory, retptr + 24);
            Assert.Equal("chunked-unknown",
                ReadString(disp.Memory, strPtr, strLen));
        }

        [Fact]
        public void InternalError_with_message_encodes_option_string()
        {
            var (disp, host, realloc, req) =
                ArrangeWithThrowingHandler(
                    new HttpException(
                        HttpErrorCode.InternalError,
                        "something went very wrong"));

            const int retptr = 64;
            host.InvokeHandlerHandle(req, retptr, realloc);

            // disc=1 + InternalError + option<string> at +16
            Assert.Equal(1, ReadByte(disp.Memory, retptr));
            Assert.Equal((byte)HttpErrorCode.InternalError,
                ReadByte(disp.Memory, retptr + 8));
            Assert.Equal(1, ReadByte(disp.Memory, retptr + 16));
            int strPtr = ReadI32(disp.Memory, retptr + 20);
            int strLen = ReadI32(disp.Memory, retptr + 24);
            Assert.Equal("something went very wrong",
                ReadString(disp.Memory, strPtr, strLen));
        }

        [Fact]
        public void SimpleArm_no_typed_payload_leaves_payload_zero()
        {
            var (disp, host, realloc, req) =
                ArrangeWithThrowingHandler(
                    new HttpException(HttpErrorCode.ConnectionRefused));

            const int retptr = 64;
            host.InvokeHandlerHandle(req, retptr, realloc);

            Assert.Equal(1, ReadByte(disp.Memory, retptr));
            Assert.Equal((byte)HttpErrorCode.ConnectionRefused,
                ReadByte(disp.Memory, retptr + 8));
            // Payload section +16..40 stays zero.
            for (int i = 16; i < 40; i++)
                Assert.Equal(0, ReadByte(disp.Memory, retptr + i));
        }

        [Fact]
        public void Misaligned_retptr_throws()
        {
            var (disp, host, realloc, req) =
                ArrangeWithThrowingHandler(
                    new HttpException(HttpErrorCode.InternalError));

            var ex = Assert.Throws<System.InvalidOperationException>(
                () => host.InvokeHandlerHandle(
                    req, /* not 8-aligned */ 12, realloc));
            Assert.Contains("misaligned", ex.Message);
        }
    }
}
