// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Buffers.Binary;
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
    /// Phase 5 Slice H coverage: <c>wasi:http/client.send</c>
    /// and <c>wasi:http/handler.handle</c> wire-up. Both bind
    /// sync-blocking (await the Task<IResponse> in the
    /// delegate body) until the canon-async-func wire
    /// convention settles.
    /// </summary>
    public class HttpClientHandlerBindingTests
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

        [Fact]
        public void BindToRuntime_registers_client_send_and_handler_handle()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.HttpClientModuleName, "send"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.HttpHandlerModuleName, "handle"),
                out _));
        }

        [Fact]
        public void InvokeClientSend_writes_ok_disc_and_response_handle()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    HttpClient = new StubClient(),
                })
            {
                Dispatcher = dispatcher,
            };

            var req = new Request(new Fields());
            req.SetAuthority("example.test");
            req.SetScheme(HttpScheme.Http);
            req.SetPathWithQuery("/");
            var reqHandle = host.RequestHandles.Allocate(req);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeClientSend(reqHandle, retptr, realloc);

            // disc=0 (ok) at +0; handle at +4.
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            int respHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 8, 4));
            Assert.True(respHandle > 0);
            var resp = host.ResponseHandles.Get(respHandle);
            Assert.NotNull(resp);
            Assert.Equal(202, resp!.GetStatusCode());
        }

        [Fact]
        public void InvokeClientSend_writes_err_disc_on_http_exception()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    HttpClient = new ThrowingClient(
                        new HttpException(
                            HttpErrorCode.ConnectionRefused,
                            "no route")),
                })
            {
                Dispatcher = dispatcher,
            };

            var req = new Request(new Fields());
            var reqHandle = host.RequestHandles.Allocate(req);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeClientSend(reqHandle, retptr, realloc);

            // disc=1 (err) at +0; error-code variant disc at +4.
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)HttpErrorCode.ConnectionRefused,
                dispatcher.Memory.AsSpan(retptr + 8, 1)[0]);
        }

        [Fact]
        public void InvokeClientSend_invalid_request_handle_writes_internal_err()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host
            {
                Dispatcher = dispatcher,
            };
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeClientSend(
                requestHandle: 999, retptr, realloc);

            // RequireRequest throws HttpException(InternalError);
            // wire layer routes it to disc=1 + error-code at +4.
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)HttpErrorCode.InternalError,
                dispatcher.Memory.AsSpan(retptr + 8, 1)[0]);
        }

        [Fact]
        public void InvokeHandlerHandle_routes_to_configured_handler()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    HttpHandler = new StubHandler(statusCode: 418),
                })
            {
                Dispatcher = dispatcher,
            };

            var req = new Request(new Fields());
            var reqHandle = host.RequestHandles.Allocate(req);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeHandlerHandle(reqHandle, retptr, realloc);

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            int respHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 8, 4));
            var resp = host.ResponseHandles.Get(respHandle);
            Assert.Equal(418, resp!.GetStatusCode());
        }

        [Fact]
        public void InvokeHandlerHandle_writes_configuration_error_when_unset()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host
            {
                Dispatcher = dispatcher,
            };
            var req = new Request(new Fields());
            var reqHandle = host.RequestHandles.Allocate(req);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeHandlerHandle(reqHandle, retptr, realloc);

            // ConfigurationError lowered to err-variant.
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)HttpErrorCode.ConfigurationError,
                dispatcher.Memory.AsSpan(retptr + 8, 1)[0]);
        }

        [Fact]
        public void InvokeHandlerHandle_writes_err_disc_on_http_exception()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    HttpHandler = new ThrowingHandler(
                        new HttpException(
                            HttpErrorCode.HttpResponseTimeout,
                            "took too long")),
                })
            {
                Dispatcher = dispatcher,
            };
            var reqHandle = host.RequestHandles.Allocate(
                new Request(new Fields()));
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeHandlerHandle(reqHandle, retptr, realloc);

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)HttpErrorCode.HttpResponseTimeout,
                dispatcher.Memory.AsSpan(retptr + 8, 1)[0]);
        }

        // ---- Test stubs ----------------------------------------------

        private sealed class StubClient : IClient
        {
            public Task<IResponse> SendAsync(
                IRequest request,
                CancellationToken cancellationToken = default)
            {
                var resp = new Response(new Fields());
                resp.SetStatusCode(202);
                return Task.FromResult<IResponse>(resp);
            }
        }

        private sealed class ThrowingClient : IClient
        {
            private readonly System.Exception _ex;
            public ThrowingClient(System.Exception ex) { _ex = ex; }
            public Task<IResponse> SendAsync(
                IRequest request,
                CancellationToken cancellationToken = default)
                => Task.FromException<IResponse>(_ex);
        }

        private sealed class StubHandler : IHandler
        {
            private readonly ushort _statusCode;
            public StubHandler(ushort statusCode) { _statusCode = statusCode; }
            public Task<IResponse> HandleAsync(
                IRequest request,
                CancellationToken cancellationToken = default)
            {
                var resp = new Response(new Fields());
                resp.SetStatusCode(_statusCode);
                return Task.FromResult<IResponse>(resp);
            }
        }

        private sealed class ThrowingHandler : IHandler
        {
            private readonly System.Exception _ex;
            public ThrowingHandler(System.Exception ex) { _ex = ex; }
            public Task<IResponse> HandleAsync(
                IRequest request,
                CancellationToken cancellationToken = default)
                => Task.FromException<IResponse>(_ex);
        }
    }
}
