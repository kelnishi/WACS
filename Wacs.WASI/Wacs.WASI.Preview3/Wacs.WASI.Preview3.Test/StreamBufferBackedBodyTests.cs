// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.Http;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice NN coverage: bridging the contents stream
    /// handle in request.new / response.new through a
    /// StreamBufferBackedStream into the Request/Response impl's
    /// Body. This closes one of Slice II's v0 caveats — the
    /// stream-handle arg now actually flows data into the impl
    /// rather than being silently dropped.
    /// </summary>
    public class StreamBufferBackedBodyTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        // ---- StreamBufferBackedStream direct tests ---------------

        [Fact]
        public void Read_blocks_until_data_then_returns_what_is_available()
        {
            var buffer = new ComponentModel.Async.StreamBuffer<byte>(64);
            var stream = new StreamBufferBackedStream(buffer);

            // Write some bytes ahead of Read so we don't have
            // to deal with cross-thread coordination.
            buffer.TryWrite(0xAA);
            buffer.TryWrite(0xBB);
            buffer.TryWrite(0xCC);

            byte[] dst = new byte[16];
            int read = stream.Read(dst, 0, dst.Length);
            Assert.Equal(3, read);
            Assert.Equal(0xAA, dst[0]);
            Assert.Equal(0xBB, dst[1]);
            Assert.Equal(0xCC, dst[2]);
        }

        [Fact]
        public void Read_returns_zero_on_complete_buffer()
        {
            var buffer = new ComponentModel.Async.StreamBuffer<byte>(64);
            buffer.Complete();
            var stream = new StreamBufferBackedStream(buffer);

            byte[] dst = new byte[16];
            Assert.Equal(0, stream.Read(dst, 0, dst.Length));
        }

        [Fact]
        public async Task ReadAsync_drains_then_signals_eof()
        {
            var buffer = new ComponentModel.Async.StreamBuffer<byte>(64);
            byte[] payload = Encoding.UTF8.GetBytes("hello");
            foreach (byte b in payload) buffer.TryWrite(b);
            buffer.Complete();

            var stream = new StreamBufferBackedStream(buffer);
            byte[] dst = new byte[16];
            int read = await stream.ReadAsync(dst, 0, dst.Length);
            Assert.Equal(5, read);
            Assert.Equal("hello", Encoding.UTF8.GetString(dst, 0, read));

            // After draining, next read returns EOF.
            int eof = await stream.ReadAsync(dst, 0, dst.Length);
            Assert.Equal(0, eof);
        }

        [Fact]
        public void Write_throws_not_supported()
        {
            var stream = new StreamBufferBackedStream(
                new ComponentModel.Async.StreamBuffer<byte>(8));
            Assert.Throws<NotSupportedException>(
                () => stream.Write(new byte[] { 1 }, 0, 1));
        }

        // ---- request.new / response.new body bridge -------------

        [Fact]
        public void RequestNew_with_contents_stream_wires_body()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            int headersHandle = host.FieldsHandles.Allocate(new Fields());

            // Allocate a stream<u8> handle and pre-fill some
            // bytes (canon stream.write equivalent for the
            // test).
            int streamHandle = dispatcher.StreamNew(typeIdx: 0);
            var streamBuf = dispatcher.GetByteStreamBuffer(streamHandle);
            byte[] payload = Encoding.UTF8.GetBytes("body bytes");
            foreach (byte b in payload) streamBuf!.TryWrite(b);
            streamBuf.Complete();

            const int retptr = 64;
            host.InvokeRequestNew(
                headersHandle,
                contentsOptDisc: 1, contentsStreamHandle: streamHandle,
                trailersFutureHandle: 0,
                optsOptDisc: 0, optsHandle: 0,
                retptr: retptr);

            int reqHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            var req = host.RequestHandles.Get(reqHandle);
            Assert.NotNull(req);
            Assert.NotNull(req!.Body);

            byte[] read = new byte[64];
            int n = req.Body!.Read(read, 0, read.Length);
            Assert.Equal(payload.Length, n);
            Assert.Equal("body bytes",
                Encoding.UTF8.GetString(read, 0, n));
        }

        [Fact]
        public void RequestNew_with_none_contents_leaves_body_null()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            int headersHandle = host.FieldsHandles.Allocate(new Fields());

            const int retptr = 64;
            host.InvokeRequestNew(
                headersHandle,
                contentsOptDisc: 0, contentsStreamHandle: 0,
                trailersFutureHandle: 0,
                optsOptDisc: 0, optsHandle: 0,
                retptr: retptr);

            int reqHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            var req = host.RequestHandles.Get(reqHandle);
            Assert.Null(req!.Body);
        }

        [Fact]
        public void ResponseNew_with_contents_stream_wires_body()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            int headersHandle = host.FieldsHandles.Allocate(new Fields());

            int streamHandle = dispatcher.StreamNew(typeIdx: 0);
            var streamBuf = dispatcher.GetByteStreamBuffer(streamHandle);
            byte[] payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            foreach (byte b in payload) streamBuf!.TryWrite(b);
            streamBuf.Complete();

            const int retptr = 64;
            host.InvokeResponseNew(
                headersHandle,
                contentsOptDisc: 1, contentsStreamHandle: streamHandle,
                trailersFutureHandle: 0,
                retptr: retptr);

            int respHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            var resp = host.ResponseHandles.Get(respHandle);
            Assert.NotNull(resp!.Body);

            byte[] read = new byte[8];
            int n = resp.Body!.Read(read, 0, read.Length);
            Assert.Equal(4, n);
            Assert.Equal(payload, read.AsSpan(0, n).ToArray());
        }

        [Fact]
        public void RequestNew_with_unknown_stream_handle_leaves_body_null()
        {
            // option-disc=some but the handle doesn't resolve to
            // a byte stream — the bridge returns null rather
            // than throwing.
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            int headersHandle = host.FieldsHandles.Allocate(new Fields());

            const int retptr = 64;
            host.InvokeRequestNew(
                headersHandle,
                contentsOptDisc: 1, contentsStreamHandle: 999,
                trailersFutureHandle: 0,
                optsOptDisc: 0, optsHandle: 0,
                retptr: retptr);

            int reqHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            var req = host.RequestHandles.Get(reqHandle);
            Assert.Null(req!.Body);
        }
    }
}
