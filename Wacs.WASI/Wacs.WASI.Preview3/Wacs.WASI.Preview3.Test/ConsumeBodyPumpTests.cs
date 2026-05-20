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
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.Http;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice OO coverage: the body-pump that
    /// consume-body kicks off. When the request/response has a
    /// non-null Body Stream, a background task reads from Body
    /// and writes into the returned StreamBuffer until EOF.
    /// This closes Slice JJ's "body data doesn't flow" caveat.
    /// </summary>
    public class ConsumeBodyPumpTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        private static async Task<byte[]> DrainBufferAsync(
            StreamBuffer<byte> buf, int maxBytes = 1024)
        {
            byte[] dst = new byte[maxBytes];
            int read = 0;
            while (read < maxBytes)
            {
                try
                {
                    byte b = await buf.ReadAsync();
                    dst[read++] = b;
                }
                catch (System.Threading.Channels.ChannelClosedException)
                {
                    break;
                }
            }
            return dst.AsSpan(0, read).ToArray();
        }

        [Fact]
        public async Task RequestConsumeBody_pumps_body_bytes_into_stream()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            byte[] payload = Encoding.UTF8.GetBytes("body bytes");
            // Build a Request with a MemoryStream body — the
            // pump should drain it into the returned StreamBuffer.
            var req = new Request(new Fields(),
                body: new MemoryStream(payload));
            int reqHandle = host.RequestHandles.Allocate(req);

            const int retptr = 64;
            host.InvokeRequestConsumeBody(
                reqHandle, resFutureHandle: 0, retptr: retptr);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            var buffer = dispatcher.GetByteStreamBuffer(streamHandle);
            Assert.NotNull(buffer);

            byte[] read = await DrainBufferAsync(buffer!);
            Assert.Equal(payload, read);
            Assert.True(buffer!.IsCompleted);
        }

        [Fact]
        public async Task RequestConsumeBody_null_body_closes_immediately()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var req = new Request(new Fields());  // null Body
            int reqHandle = host.RequestHandles.Allocate(req);

            const int retptr = 64;
            host.InvokeRequestConsumeBody(
                reqHandle, resFutureHandle: 0, retptr: retptr);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            var buffer = dispatcher.GetByteStreamBuffer(streamHandle);

            // With null Body the buffer is Complete()d immediately;
            // a read returns 0 bytes via the EOF path.
            byte[] read = await DrainBufferAsync(buffer!);
            Assert.Empty(read);
            Assert.True(buffer!.IsCompleted);
        }

        [Fact]
        public async Task ResponseConsumeBody_pumps_body_bytes_into_stream()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            byte[] payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            var resp = new Response(new Fields(),
                body: new MemoryStream(payload));
            int respHandle = host.ResponseHandles.Allocate(resp);

            const int retptr = 64;
            host.InvokeResponseConsumeBody(
                respHandle, resFutureHandle: 0, retptr: retptr);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            var buffer = dispatcher.GetByteStreamBuffer(streamHandle);
            Assert.NotNull(buffer);

            byte[] read = await DrainBufferAsync(buffer!);
            Assert.Equal(payload, read);
            Assert.True(buffer!.IsCompleted);
        }

        [Fact]
        public async Task ConsumeBody_pump_handles_body_exception_gracefully()
        {
            // A Stream that throws partway through Read should
            // still leave the buffer in a Completed state with
            // whatever bytes were drained before the throw.
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var req = new Request(new Fields(),
                body: new ThrowingPartwayStream(
                    Encoding.UTF8.GetBytes("abc"), throwAfter: 3));
            int reqHandle = host.RequestHandles.Allocate(req);

            const int retptr = 64;
            host.InvokeRequestConsumeBody(
                reqHandle, resFutureHandle: 0, retptr: retptr);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            var buffer = dispatcher.GetByteStreamBuffer(streamHandle);

            byte[] read = await DrainBufferAsync(buffer!);
            Assert.Equal(Encoding.UTF8.GetBytes("abc"), read);
            Assert.True(buffer!.IsCompleted);
        }

        // Stream that yields N bytes then throws on the next
        // read — exercises the pump's catch-and-complete path.
        private sealed class ThrowingPartwayStream : Stream
        {
            private readonly byte[] _data;
            private readonly int _throwAfter;
            private int _pos;
            public ThrowingPartwayStream(byte[] data, int throwAfter)
            {
                _data = data; _throwAfter = throwAfter;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _data.Length;
            public override long Position
            {
                get => _pos;
                set => throw new NotSupportedException();
            }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_pos >= _throwAfter)
                    throw new IOException("synthetic body failure");
                int remaining = Math.Min(_throwAfter - _pos, count);
                int actual = Math.Min(remaining, _data.Length - _pos);
                if (actual <= 0) return 0;
                Array.Copy(_data, _pos, buffer, offset, actual);
                _pos += actual;
                return actual;
            }
            public override long Seek(long o, SeekOrigin s)
                => throw new NotSupportedException();
            public override void SetLength(long v)
                => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c)
                => throw new NotSupportedException();
        }
    }
}
