// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Threading;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
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
        public void InvokeClientSend_returns_response_handle_from_configured_client()
        {
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    HttpClient = new StubClient(),
                })
            {
                Dispatcher = new AsyncDispatcher(),
            };

            var req = new Request(new Fields());
            req.SetAuthority("example.test");
            req.SetScheme(HttpScheme.Http);
            req.SetPathWithQuery("/");
            var reqHandle = host.RequestHandles.Allocate(req);

            var respHandle = host.InvokeClientSend(reqHandle);
            Assert.True(respHandle > 0);
            var resp = host.ResponseHandles.Get(respHandle);
            Assert.NotNull(resp);
            Assert.Equal(202, resp!.GetStatusCode());
        }

        [Fact]
        public void InvokeClientSend_propagates_http_exception_from_client()
        {
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    HttpClient = new ThrowingClient(
                        new HttpException(
                            HttpErrorCode.ConnectionRefused,
                            "no route")),
                })
            {
                Dispatcher = new AsyncDispatcher(),
            };

            var req = new Request(new Fields());
            var reqHandle = host.RequestHandles.Allocate(req);

            var ex = Assert.Throws<HttpException>(
                () => host.InvokeClientSend(reqHandle));
            Assert.Equal(HttpErrorCode.ConnectionRefused, ex.Code);
        }

        [Fact]
        public void InvokeClientSend_invalid_request_handle_throws()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var ex = Assert.Throws<HttpException>(
                () => host.InvokeClientSend(999));
            Assert.Equal(HttpErrorCode.InternalError, ex.Code);
            Assert.Contains("999", ex.Message);
        }

        [Fact]
        public void InvokeHandlerHandle_routes_to_configured_handler()
        {
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    HttpHandler = new StubHandler(statusCode: 418),
                })
            {
                Dispatcher = new AsyncDispatcher(),
            };

            var req = new Request(new Fields());
            var reqHandle = host.RequestHandles.Allocate(req);

            var respHandle = host.InvokeHandlerHandle(reqHandle);
            var resp = host.ResponseHandles.Get(respHandle);
            Assert.Equal(418, resp!.GetStatusCode());
        }

        [Fact]
        public void InvokeHandlerHandle_throws_configuration_error_when_unset()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var req = new Request(new Fields());
            var reqHandle = host.RequestHandles.Allocate(req);

            var ex = Assert.Throws<HttpException>(
                () => host.InvokeHandlerHandle(reqHandle));
            Assert.Equal(HttpErrorCode.ConfigurationError, ex.Code);
            Assert.Contains("WasiPreview3HostBuilder.HttpHandler", ex.Message);
        }

        [Fact]
        public void InvokeHandlerHandle_propagates_http_exception_from_handler()
        {
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    HttpHandler = new ThrowingHandler(
                        new HttpException(
                            HttpErrorCode.HttpResponseTimeout,
                            "took too long")),
                })
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var reqHandle = host.RequestHandles.Allocate(new Request(new Fields()));
            var ex = Assert.Throws<HttpException>(
                () => host.InvokeHandlerHandle(reqHandle));
            Assert.Equal(HttpErrorCode.HttpResponseTimeout, ex.Code);
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
