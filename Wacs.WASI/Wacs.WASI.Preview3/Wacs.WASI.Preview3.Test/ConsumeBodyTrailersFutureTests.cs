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
    /// Phase 5 Slice PP coverage: consume-body's trailers future
    /// resolves with IRequest.Trailers / IResponse.Trailers when
    /// the body pump finishes. Closes another Slice JJ caveat.
    /// </summary>
    public class ConsumeBodyTrailersFutureTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        private static async Task DrainBufferAsync(StreamBuffer<byte> buf)
        {
            while (true)
            {
                try { await buf.ReadAsync(); }
                catch (System.Threading.Channels.ChannelClosedException)
                {
                    return;
                }
            }
        }

        [Fact]
        public async Task ConsumeBody_null_body_resolves_trailers_future_immediately()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var req = new Request(new Fields());  // null Body, null Trailers
            int reqHandle = host.RequestHandles.Allocate(req);

            const int retptr = 64;
            host.InvokeRequestConsumeBody(
                reqHandle, resFutureHandle: 0, retptr: retptr);

            int trailersFuture = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));

            // Future should be resolved immediately with null
            // (none arm).
            var task = dispatcher.FutureReadAsync(trailersFuture);
            await task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(task.IsCompletedSuccessfully);
            Assert.Null(task.Result);
        }

        [Fact]
        public async Task ConsumeBody_body_with_trailers_resolves_after_drain()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var trailers = new Fields();
            trailers.Append("x-trailing", Encoding.UTF8.GetBytes("v1"));
            var req = new Request(new Fields(),
                body: new MemoryStream(Encoding.UTF8.GetBytes("body")),
                trailers: trailers);
            int reqHandle = host.RequestHandles.Allocate(req);

            const int retptr = 64;
            host.InvokeRequestConsumeBody(
                reqHandle, resFutureHandle: 0, retptr: retptr);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            int trailersFuture = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));

            // Drain the body buffer so the pump finishes.
            await DrainBufferAsync(
                dispatcher.GetByteStreamBuffer(streamHandle)!);

            // Trailers future resolves with the trailers IFields.
            var task = dispatcher.FutureReadAsync(trailersFuture);
            object? result = await task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Same(trailers, result);
        }

        [Fact]
        public async Task ConsumeBody_body_without_trailers_resolves_null_after_drain()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var req = new Request(new Fields(),
                body: new MemoryStream(new byte[] { 1, 2, 3 }));
            int reqHandle = host.RequestHandles.Allocate(req);

            const int retptr = 64;
            host.InvokeRequestConsumeBody(
                reqHandle, resFutureHandle: 0, retptr: retptr);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            int trailersFuture = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));

            await DrainBufferAsync(
                dispatcher.GetByteStreamBuffer(streamHandle)!);

            var task = dispatcher.FutureReadAsync(trailersFuture);
            object? result = await task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Null(result);  // none arm
        }

        [Fact]
        public async Task ConsumeBody_body_exception_faults_trailers_future()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var trailers = new Fields();
            var req = new Request(new Fields(),
                body: new ThrowingStream(),
                trailers: trailers);
            int reqHandle = host.RequestHandles.Allocate(req);

            const int retptr = 64;
            host.InvokeRequestConsumeBody(
                reqHandle, resFutureHandle: 0, retptr: retptr);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            int trailersFuture = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));

            await DrainBufferAsync(
                dispatcher.GetByteStreamBuffer(streamHandle)!);

            // The pump's catch path cancels the future cell
            // (without dropping the handle so a guest already
            // awaiting it observes the cancellation).
            var task = dispatcher.FutureReadAsync(trailersFuture);
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => task.WaitAsync(TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public async Task ResponseConsumeBody_trailers_round_trip()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var trailers = new Fields();
            trailers.Append("server-timing",
                Encoding.UTF8.GetBytes("db;dur=5"));
            var resp = new Response(new Fields(),
                body: new MemoryStream(Encoding.UTF8.GetBytes("body")),
                trailers: trailers);
            int respHandle = host.ResponseHandles.Allocate(resp);

            const int retptr = 64;
            host.InvokeResponseConsumeBody(
                respHandle, resFutureHandle: 0, retptr: retptr);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            int trailersFuture = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));

            await DrainBufferAsync(
                dispatcher.GetByteStreamBuffer(streamHandle)!);

            var task = dispatcher.FutureReadAsync(trailersFuture);
            object? result = await task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Same(trailers, result);
        }

        private sealed class ThrowingStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => 0;
            public override long Position
            {
                get => 0; set => throw new NotSupportedException();
            }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
                => throw new IOException("synthetic body failure");
            public override long Seek(long o, SeekOrigin s)
                => throw new NotSupportedException();
            public override void SetLength(long v)
                => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c)
                => throw new NotSupportedException();
        }
    }
}
