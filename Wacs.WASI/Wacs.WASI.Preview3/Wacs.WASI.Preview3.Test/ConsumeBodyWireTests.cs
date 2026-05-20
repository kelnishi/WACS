// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
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
    /// Phase 5 Slice JJ coverage: [static]request.consume-body and
    /// [static]response.consume-body. Both allocate a stream<u8>
    /// + trailers future and write the tuple at retptr. v0
    /// caveat: stream is empty / trailers future stays pending
    /// until the cooperative-yield refactor lands.
    /// </summary>
    public class ConsumeBodyWireTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        [Fact]
        public void BindToRuntime_registers_consume_body()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.HttpTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[static]request.consume-body"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[static]response.consume-body"), out _));
        }

        [Fact]
        public void InvokeRequestConsumeBody_allocates_stream_and_future()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            int reqHandle = host.RequestHandles.Allocate(
                new Request(new Fields()));

            const int retptr = 64;
            host.InvokeRequestConsumeBody(
                reqHandle, resFutureHandle: 0, retptr: retptr);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            int trailersFuture = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.True(streamHandle > 0);
            Assert.True(trailersFuture > 0);

            // Stream is allocated through the dispatcher.
            Assert.NotNull(dispatcher.GetByteStreamBuffer(streamHandle));
            // Trailers future is pending.
            var task = dispatcher.FutureReadAsync(trailersFuture);
            Assert.False(task.IsCompleted);
        }

        [Fact]
        public void InvokeResponseConsumeBody_allocates_stream_and_future()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            int respHandle = host.ResponseHandles.Allocate(
                new Response(new Fields()));

            const int retptr = 64;
            host.InvokeResponseConsumeBody(
                respHandle, resFutureHandle: 0, retptr: retptr);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            int trailersFuture = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.True(streamHandle > 0);
            Assert.True(trailersFuture > 0);

            Assert.NotNull(dispatcher.GetByteStreamBuffer(streamHandle));
            var task = dispatcher.FutureReadAsync(trailersFuture);
            Assert.False(task.IsCompleted);
        }

        [Fact]
        public void InvokeRequestConsumeBody_invalid_handle_throws()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var ex = Assert.Throws<HttpException>(
                () => host.InvokeRequestConsumeBody(
                    thisHandle: 999, resFutureHandle: 0,
                    retptr: 64));
            Assert.Equal(HttpErrorCode.InternalError, ex.Code);
        }

        [Fact]
        public void InvokeRequestResponseConsumeBody_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            int reqHandle = host.RequestHandles.Allocate(
                new Request(new Fields()));

            var ex1 = Assert.Throws<InvalidOperationException>(
                () => host.InvokeRequestConsumeBody(
                    reqHandle, 0, /* not 4-aligned */ 13));
            Assert.Contains("misaligned", ex1.Message);

            int respHandle = host.ResponseHandles.Allocate(
                new Response(new Fields()));
            var ex2 = Assert.Throws<InvalidOperationException>(
                () => host.InvokeResponseConsumeBody(
                    respHandle, 0, /* not 4-aligned */ 13));
            Assert.Contains("misaligned", ex2.Message);
        }
    }
}
