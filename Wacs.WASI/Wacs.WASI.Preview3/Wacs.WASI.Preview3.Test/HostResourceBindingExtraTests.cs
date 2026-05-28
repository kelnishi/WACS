// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

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
    /// Phase 5 Slice G coverage: full
    /// <c>wasi:http/types.request-options</c> wire-up + simple
    /// <c>response</c> methods. Companion to
    /// <c>HostResourceBindingTests</c> which covers Slice F's
    /// `fields` representative subset.
    /// </summary>
    public class HostResourceBindingExtraTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        // ---- request-options wire-up ---------------------------------

        [Fact]
        public void BindToRuntime_registers_request_options_methods()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            string m = WasiPreview3Host.HttpTypesModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[constructor]request-options"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[resource-drop]request-options"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request-options.get-connect-timeout"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request-options.set-connect-timeout"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request-options.get-first-byte-timeout"),
                out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request-options.set-first-byte-timeout"),
                out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request-options.get-between-bytes-timeout"),
                out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request-options.set-between-bytes-timeout"),
                out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request-options.clone"), out _));
        }

        [Fact]
        public void InvokeRequestOptionsSetTimeout_sets_value_when_isSome_nonzero()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var opt = new RequestOptions();
            var handle = host.RequestOptionsHandles.Allocate(opt);

            host.InvokeRequestOptionsSetTimeout(
                handle, isSome: 1, value: 1_500_000_000L,
                (o, v) => o.SetConnectTimeout(v));
            Assert.Equal(1_500_000_000UL, opt.GetConnectTimeout());
        }

        [Fact]
        public void InvokeRequestOptionsSetTimeout_clears_when_isSome_zero()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var opt = new RequestOptions();
            opt.SetConnectTimeout(99);
            var handle = host.RequestOptionsHandles.Allocate(opt);

            host.InvokeRequestOptionsSetTimeout(
                handle, isSome: 0, value: 0,
                (o, v) => o.SetConnectTimeout(v));
            Assert.Null(opt.GetConnectTimeout());
        }

        [Fact]
        public void InvokeRequestOptionsGetTimeout_writes_some_pair_at_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var opt = new RequestOptions();
            opt.SetFirstByteTimeout(2_500_000_000UL);
            var handle = host.RequestOptionsHandles.Allocate(opt);

            const int retptr = 64;
            host.InvokeRequestOptionsGetTimeout(
                handle, retptr, o => o.GetFirstByteTimeout());

            var bytes = dispatcher.Memory.AsSpan(retptr, 16);
            int disc = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(0));
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8));
            Assert.Equal(1, disc);
            Assert.Equal(2_500_000_000UL, value);
        }

        [Fact]
        public void InvokeRequestOptionsGetTimeout_writes_none_disc_when_unset()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var opt = new RequestOptions();
            var handle = host.RequestOptionsHandles.Allocate(opt);

            const int retptr = 64;
            host.InvokeRequestOptionsGetTimeout(
                handle, retptr, o => o.GetConnectTimeout());

            var bytes = dispatcher.Memory.AsSpan(retptr, 16);
            int disc = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(0));
            Assert.Equal(0, disc);
        }

        [Fact]
        public void InvokeRequestOptionsGetTimeout_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var opt = new RequestOptions();
            var handle = host.RequestOptionsHandles.Allocate(opt);

            var ex = Assert.Throws<RequestOptionsException>(
                () => host.InvokeRequestOptionsGetTimeout(
                    handle, 12, o => o.GetConnectTimeout()));
            Assert.Equal(RequestOptionsError.Other, ex.Code);
            Assert.Contains("misaligned", ex.Message);
        }

        [Fact]
        public void InvokeRequestOptionsSetTimeout_invalid_handle_throws()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var ex = Assert.Throws<RequestOptionsException>(
                () => host.InvokeRequestOptionsSetTimeout(
                    999, 1, 0,
                    (o, v) => o.SetConnectTimeout(v)));
            Assert.Equal(RequestOptionsError.Other, ex.Code);
            Assert.Contains("999", ex.Message);
        }

        // ---- response wire-up ----------------------------------------

        [Fact]
        public void BindToRuntime_registers_response_status_methods()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            string m = WasiPreview3Host.HttpTypesModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[resource-drop]response"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]response.get-status-code"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]response.set-status-code"), out _));
        }

        [Fact]
        public void Response_status_round_trips_via_handle()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var resp = new Response(new Fields());
            var handle = host.ResponseHandles.Allocate(resp);

            // Method-style: call the IResponse directly through
            // the handle indirection (the bound delegate body
            // reaches IResponse via host.RequireResponse).
            Assert.Equal(200, resp.GetStatusCode());
            resp.SetStatusCode(404);
            Assert.Equal(404, host.ResponseHandles.Get(handle)!.GetStatusCode());
        }

        [Fact]
        public void Response_drop_releases_handle()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var handle = host.ResponseHandles.Allocate(new Response(new Fields()));
            Assert.Equal(1, host.ResponseHandles.Count);
            host.ResponseHandles.Drop(handle);
            Assert.Equal(0, host.ResponseHandles.Count);
        }
    }
}
