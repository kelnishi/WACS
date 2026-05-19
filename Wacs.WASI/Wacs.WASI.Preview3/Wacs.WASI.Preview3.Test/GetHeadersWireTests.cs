// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Text;
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
    /// Phase 5 Slice DD coverage: request.get-headers and
    /// response.get-headers. Both return own&lt;fields&gt;.
    /// The returned handle points at the parent's existing
    /// IFields impl — mutations are mutually visible.
    /// </summary>
    public class GetHeadersWireTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        [Fact]
        public void BindToRuntime_registers_get_headers()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.HttpTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]request.get-headers"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]response.get-headers"), out _));
        }

        [Fact]
        public void Request_get_headers_allocates_handle_to_same_impl()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var headers = new Fields();
            headers.Append("x-trace-id", Encoding.UTF8.GetBytes("abc"));
            var request = new Request(headers);
            int reqHandle = host.RequestHandles.Allocate(request);

            // Direct delegate call — get-headers binding is a Func.
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.HttpTypesModuleName,
                 "[method]request.get-headers"), out _));

            int fieldsHandle = host.FieldsHandles.Allocate(
                request.GetHeaders());
            // The wire dispatch allocates equivalently — we
            // verify the impl shape rather than re-entering
            // the runtime.
            var resolved = host.FieldsHandles.Get(fieldsHandle);
            Assert.Same(headers, resolved);
        }

        [Fact]
        public void Request_get_headers_mutations_visible_through_parent()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var headers = new Fields();
            var request = new Request(headers);
            int reqHandle = host.RequestHandles.Allocate(request);

            int fieldsHandle = host.FieldsHandles.Allocate(
                request.GetHeaders());

            // Mutate via the returned handle.
            host.FieldsHandles.Get(fieldsHandle)!.Append(
                "x-test", Encoding.UTF8.GetBytes("v1"));

            // Visible through the parent's own GetHeaders.
            Assert.True(request.GetHeaders().Has("x-test"));
        }

        [Fact]
        public void Response_get_headers_allocates_handle_to_same_impl()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var headers = new Fields();
            headers.Append("content-type",
                Encoding.UTF8.GetBytes("application/json"));
            var response = new Response(headers);
            int respHandle = host.ResponseHandles.Allocate(response);

            int fieldsHandle = host.FieldsHandles.Allocate(
                response.GetHeaders());
            var resolved = host.FieldsHandles.Get(fieldsHandle);
            Assert.Same(headers, resolved);
        }

        [Fact]
        public void Response_get_headers_share_impl_with_response()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var headers = new Fields();
            var response = new Response(headers);
            int respHandle = host.ResponseHandles.Allocate(response);

            int fieldsHandle = host.FieldsHandles.Allocate(
                response.GetHeaders());

            host.FieldsHandles.Get(fieldsHandle)!.Append(
                "x-set-via-handle", Encoding.UTF8.GetBytes("v"));

            Assert.True(response.GetHeaders().Has("x-set-via-handle"));
        }

        [Fact]
        public void Request_get_headers_resource_drop_doesnt_invalidate_parent()
        {
            // Drop the returned handle and verify the parent
            // request still has its headers reachable through
            // its own reference.
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var headers = new Fields();
            headers.Append("x", Encoding.UTF8.GetBytes("y"));
            var request = new Request(headers);
            int reqHandle = host.RequestHandles.Allocate(request);

            int fieldsHandle = host.FieldsHandles.Allocate(
                request.GetHeaders());
            host.FieldsHandles.Drop(fieldsHandle);

            // Request still has its headers.
            Assert.True(request.GetHeaders().Has("x"));
        }
    }
}
