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
    /// Phase 5 Slice II coverage: [static]request.new and
    /// [static]response.new constructors. Both return
    /// tuple&lt;X, future&lt;result&lt;_, error-code&gt;&gt;&gt;
    /// via an 8-byte retptr. v0 ignores the body-stream and
    /// trailers-future args (impl gets null Body/Trailers); the
    /// completion future is allocated but stays pending.
    /// </summary>
    public class RequestResponseNewWireTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        [Fact]
        public void BindToRuntime_registers_new_constructors()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.HttpTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[static]request.new"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[static]response.new"), out _));
        }

        // ---- request.new ---------------------------------------------

        [Fact]
        public void InvokeRequestNew_allocates_request_handle_and_future()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var headers = new Fields();
            headers.Append("x-test",
                System.Text.Encoding.UTF8.GetBytes("v"));
            int headersHandle = host.FieldsHandles.Allocate(headers);

            const int retptr = 64;
            host.InvokeRequestNew(
                headersHandle,
                contentsOptDisc: 0, contentsStreamHandle: 0,
                trailersFutureHandle: 0,
                optsOptDisc: 0, optsHandle: 0,
                retptr: retptr);

            int reqHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            int futureHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.True(reqHandle > 0);
            Assert.True(futureHandle > 0);

            var req = host.RequestHandles.Get(reqHandle);
            Assert.NotNull(req);
            // Headers impl is the same instance we passed in.
            Assert.Same(headers, req!.GetHeaders());
            Assert.Null(req.Body);     // v0: no stream bridge
            Assert.Null(req.Trailers); // v0: no trailers bridge
            Assert.Null(req.GetOptions());
        }

        [Fact]
        public void InvokeRequestNew_with_options_threads_them_into_impl()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            int headersHandle = host.FieldsHandles.Allocate(new Fields());
            var opts = new RequestOptions();
            opts.SetConnectTimeout(5_000_000_000UL);
            int optsHandle = host.RequestOptionsHandles.Allocate(opts);

            const int retptr = 64;
            host.InvokeRequestNew(
                headersHandle,
                contentsOptDisc: 0, contentsStreamHandle: 0,
                trailersFutureHandle: 0,
                optsOptDisc: 1, optsHandle: optsHandle,
                retptr: retptr);

            int reqHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            var req = host.RequestHandles.Get(reqHandle);
            Assert.Same(opts, req!.GetOptions());
        }

        [Fact]
        public void InvokeRequestNew_completion_future_is_pending()
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

            int futureHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));

            // The completion future is pending until something
            // resolves it. v0 doesn't resolve it on construction.
            var task = dispatcher.FutureReadAsync(futureHandle);
            Assert.False(task.IsCompleted);
        }

        // ---- response.new --------------------------------------------

        [Fact]
        public void InvokeResponseNew_allocates_response_handle_and_future()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var headers = new Fields();
            int headersHandle = host.FieldsHandles.Allocate(headers);

            const int retptr = 64;
            host.InvokeResponseNew(
                headersHandle,
                contentsOptDisc: 0, contentsStreamHandle: 0,
                trailersFutureHandle: 0,
                retptr: retptr);

            int respHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            int futureHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.True(respHandle > 0);
            Assert.True(futureHandle > 0);

            var resp = host.ResponseHandles.Get(respHandle);
            Assert.NotNull(resp);
            Assert.Same(headers, resp!.GetHeaders());
            Assert.Equal((ushort)200, resp.GetStatusCode());
            Assert.Null(resp.Body);
            Assert.Null(resp.Trailers);
        }

        [Fact]
        public void InvokeRequestResponseNew_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            int headersHandle = host.FieldsHandles.Allocate(new Fields());

            var ex1 = Assert.Throws<InvalidOperationException>(
                () => host.InvokeRequestNew(
                    headersHandle, 0, 0, 0, 0, 0,
                    /* not 4-aligned */ 13));
            Assert.Contains("misaligned", ex1.Message);

            var ex2 = Assert.Throws<InvalidOperationException>(
                () => host.InvokeResponseNew(
                    headersHandle, 0, 0, 0,
                    /* not 4-aligned */ 13));
            Assert.Contains("misaligned", ex2.Message);
        }
    }
}
