// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wacs.WASI.Preview3.Http;
using Xunit;
using HttpMethod = Wacs.WASI.Preview3.Http.HttpMethod;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice E coverage: <c>wasi:http</c> type system,
    /// in-memory resource impls (fields, request, response,
    /// request-options), and HttpClient-backed
    /// <see cref="HttpBackedClient"/>.
    /// </summary>
    public class HttpTests
    {
        // ---- HttpMethod / HttpScheme ---------------------------------

        [Fact]
        public void HttpMethod_factories_set_kind()
        {
            Assert.Equal(HttpMethod.Tag.Get, HttpMethod.Get.Kind);
            Assert.Equal(HttpMethod.Tag.Post, HttpMethod.Post.Kind);
            var other = HttpMethod.Other("PROPFIND");
            Assert.Equal(HttpMethod.Tag.Other, other.Kind);
            Assert.Equal("PROPFIND", other.OtherName);
        }

        [Fact]
        public void HttpScheme_to_string_canonical_form()
        {
            Assert.Equal("http", HttpScheme.Http.ToString());
            Assert.Equal("https", HttpScheme.Https.ToString());
            Assert.Equal("gopher", HttpScheme.Other("gopher").ToString());
        }

        // ---- Fields --------------------------------------------------

        [Fact]
        public void Fields_set_and_get_case_insensitive()
        {
            var f = new Fields();
            f.Set("Content-Type", new[]
            {
                Encoding.UTF8.GetBytes("application/json"),
            });
            Assert.True(f.Has("content-type"));
            Assert.True(f.Has("Content-Type"));
            Assert.False(f.Has("X-Missing"));

            var got = f.Get("CONTENT-TYPE");
            Assert.Single(got);
            Assert.Equal("application/json",
                Encoding.UTF8.GetString(got[0]));
        }

        [Fact]
        public void Fields_append_preserves_insertion_order()
        {
            var f = new Fields();
            f.Append("X-Trace", Encoding.UTF8.GetBytes("a"));
            f.Append("X-Trace", Encoding.UTF8.GetBytes("b"));
            f.Append("X-Trace", Encoding.UTF8.GetBytes("c"));
            var got = f.Get("X-Trace");
            Assert.Equal(new[] { "a", "b", "c" },
                got.Select(v => Encoding.UTF8.GetString(v)).ToArray());
        }

        [Fact]
        public void Fields_get_and_delete_returns_values_then_removes()
        {
            var f = new Fields();
            f.Append("X", Encoding.UTF8.GetBytes("1"));
            f.Append("X", Encoding.UTF8.GetBytes("2"));
            var got = f.GetAndDelete("X");
            Assert.Equal(2, got.Count);
            Assert.False(f.Has("X"));
        }

        [Fact]
        public void Fields_frozen_rejects_mutations_with_immutable()
        {
            var f = new Fields();
            f.Append("X", Encoding.UTF8.GetBytes("y"));
            f.Freeze();

            var ex1 = Assert.Throws<HeaderException>(
                () => f.Set("Z", new[] { Encoding.UTF8.GetBytes("v") }));
            Assert.Equal(HeaderError.Immutable, ex1.Code);

            var ex2 = Assert.Throws<HeaderException>(
                () => f.Append("Z", Encoding.UTF8.GetBytes("v")));
            Assert.Equal(HeaderError.Immutable, ex2.Code);

            var ex3 = Assert.Throws<HeaderException>(
                () => f.Delete("X"));
            Assert.Equal(HeaderError.Immutable, ex3.Code);
        }

        [Fact]
        public void Fields_invalid_name_throws_invalid_syntax()
        {
            var f = new Fields();
            var ex = Assert.Throws<HeaderException>(
                () => f.Append("", Encoding.UTF8.GetBytes("v")));
            Assert.Equal(HeaderError.InvalidSyntax, ex.Code);

            var ex2 = Assert.Throws<HeaderException>(
                () => f.Append("bad name", Encoding.UTF8.GetBytes("v")));
            Assert.Equal(HeaderError.InvalidSyntax, ex2.Code);
        }

        [Fact]
        public void Fields_from_list_constructs_with_entries()
        {
            var f = Fields.FromList(new[]
            {
                ("Accept", Encoding.UTF8.GetBytes("application/json")),
                ("Accept", Encoding.UTF8.GetBytes("text/html")),
                ("User-Agent", Encoding.UTF8.GetBytes("wacs/1.0")),
            });
            Assert.Equal(2, f.Get("Accept").Count);
            Assert.Single(f.Get("User-Agent"));
        }

        [Fact]
        public void Fields_clone_is_independent_of_original()
        {
            var f = new Fields();
            f.Append("X", Encoding.UTF8.GetBytes("a"));
            var c = f.Clone();
            f.Append("X", Encoding.UTF8.GetBytes("b"));
            Assert.Single(c.Get("X"));
            Assert.Equal(2, f.Get("X").Count);
        }

        // ---- RequestOptions ------------------------------------------

        [Fact]
        public void RequestOptions_round_trips_timeouts()
        {
            var opt = new RequestOptions();
            Assert.Null(opt.GetConnectTimeout());
            opt.SetConnectTimeout(5_000_000_000UL);
            opt.SetFirstByteTimeout(1_000_000_000UL);
            opt.SetBetweenBytesTimeout(500_000_000UL);
            Assert.Equal(5_000_000_000UL, opt.GetConnectTimeout());
            Assert.Equal(1_000_000_000UL, opt.GetFirstByteTimeout());
            Assert.Equal(500_000_000UL, opt.GetBetweenBytesTimeout());
        }

        [Fact]
        public void RequestOptions_frozen_throws_immutable()
        {
            var opt = new RequestOptions();
            opt.SetConnectTimeout(1);
            opt.Freeze();
            var ex = Assert.Throws<RequestOptionsException>(
                () => opt.SetConnectTimeout(2));
            Assert.Equal(RequestOptionsError.Immutable, ex.Code);
        }

        [Fact]
        public void RequestOptions_clone_decouples_from_original()
        {
            var a = new RequestOptions();
            a.SetConnectTimeout(100);
            var b = a.Clone();
            a.SetConnectTimeout(200);
            Assert.Equal(100UL, b.GetConnectTimeout());
            Assert.Equal(200UL, a.GetConnectTimeout());
        }

        // ---- Request / Response shape -------------------------------

        [Fact]
        public void Request_default_method_is_get()
        {
            var req = new Request(new Fields());
            Assert.Equal(HttpMethod.Tag.Get, req.GetMethod().Kind);
        }

        [Fact]
        public void Request_setters_round_trip()
        {
            var req = new Request(new Fields());
            req.SetMethod(HttpMethod.Post);
            req.SetScheme(HttpScheme.Https);
            req.SetAuthority("example.com:8443");
            req.SetPathWithQuery("/api/v1/widgets?id=42");
            Assert.Equal(HttpMethod.Tag.Post, req.GetMethod().Kind);
            Assert.Equal(HttpScheme.Tag.Https, req.GetScheme()!.Value.Kind);
            Assert.Equal("example.com:8443", req.GetAuthority());
            Assert.Equal("/api/v1/widgets?id=42",
                req.GetPathWithQuery());
        }

        [Fact]
        public void Response_default_status_code_is_200()
        {
            var resp = new Response(new Fields());
            Assert.Equal(200, resp.GetStatusCode());
        }

        [Fact]
        public void Response_setters_round_trip()
        {
            var resp = new Response(new Fields());
            resp.SetStatusCode(418);
            Assert.Equal(418, resp.GetStatusCode());
        }

        // ---- HttpBackedClient: error mapping -------------------------

        [Fact]
        public async Task HttpBackedClient_user_cancellation_propagates_as_oce()
        {
            var stub = new BlockingHandlerStub();
            using var http = new System.Net.Http.HttpClient(stub);
            using var client = new HttpBackedClient(http);

            var req = new Request(new Fields());
            req.SetScheme(HttpScheme.Http);
            req.SetAuthority("example.invalid");
            req.SetPathWithQuery("/");

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(20);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.SendAsync(req, cts.Token));
        }

        [Fact]
        public async Task HttpBackedClient_unreachable_host_maps_to_connection_refused()
        {
            // We use a stub that throws HttpRequestException
            // directly to avoid network round-trips on CI.
            var stub = new ThrowingHandlerStub(
                new HttpRequestException("no connection"));
            using var http = new System.Net.Http.HttpClient(stub);
            using var client = new HttpBackedClient(http);

            var req = new Request(new Fields());
            req.SetScheme(HttpScheme.Http);
            req.SetAuthority("example.invalid");
            req.SetPathWithQuery("/");

            var ex = await Assert.ThrowsAsync<HttpException>(
                () => client.SendAsync(req));
            Assert.Equal(HttpErrorCode.ConnectionRefused, ex.Code);
        }

        [Fact]
        public async Task HttpBackedClient_round_trips_status_and_body()
        {
            // Stub handler returns 201 + body bytes — confirms
            // the lift converts status + body correctly.
            var stub = new StaticResponseHandlerStub(
                statusCode: 201,
                body: Encoding.UTF8.GetBytes("created!"),
                contentType: "text/plain");
            using var http = new System.Net.Http.HttpClient(stub);
            using var client = new HttpBackedClient(http);

            var req = new Request(new Fields());
            req.SetScheme(HttpScheme.Https);
            req.SetAuthority("example.test");
            req.SetPathWithQuery("/items");
            req.SetMethod(HttpMethod.Post);

            var resp = await client.SendAsync(req);
            Assert.Equal(201, resp.GetStatusCode());

            var bodyMs = (MemoryStream)resp.Body!;
            Assert.Equal("created!",
                Encoding.UTF8.GetString(bodyMs.ToArray()));

            // Content-Type header is lifted onto the response
            // headers collection.
            Assert.True(resp.GetHeaders().Has("Content-Type"));
        }

        // ---- Host integration ----------------------------------------

        [Fact]
        public void Host_default_construct_provides_default_http_client()
        {
            var host = new WasiPreview3Host();
            Assert.NotNull(host.HttpClient);
            Assert.Null(host.HttpHandler); // no default handler
        }

        [Fact]
        public void Host_builder_overrides_http_client_and_handler()
        {
            var stubClient = new StubClient();
            var stubHandler = new StubHandler();
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    HttpClient = stubClient,
                    HttpHandler = stubHandler,
                });
            Assert.Same(stubClient, host.HttpClient);
            Assert.Same(stubHandler, host.HttpHandler);
        }

        // ---- Test stubs ----------------------------------------------

        private sealed class BlockingHandlerStub : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return new HttpResponseMessage();
            }
        }

        private sealed class ThrowingHandlerStub : HttpMessageHandler
        {
            private readonly Exception _ex;
            public ThrowingHandlerStub(Exception ex) { _ex = ex; }
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
                => Task.FromException<HttpResponseMessage>(_ex);
        }

        private sealed class StaticResponseHandlerStub : HttpMessageHandler
        {
            private readonly int _statusCode;
            private readonly byte[] _body;
            private readonly string _contentType;

            public StaticResponseHandlerStub(
                int statusCode, byte[] body, string contentType)
            {
                _statusCode = statusCode;
                _body = body;
                _contentType = contentType;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var resp = new HttpResponseMessage(
                    (System.Net.HttpStatusCode)_statusCode)
                {
                    Content = new ByteArrayContent(_body),
                };
                resp.Content.Headers.TryAddWithoutValidation(
                    "Content-Type", _contentType);
                return Task.FromResult(resp);
            }
        }

        private sealed class StubClient : IClient
        {
            public Task<IResponse> SendAsync(
                IRequest request, CancellationToken cancellationToken = default)
                => Task.FromResult<IResponse>(new Response(new Fields()));
        }

        private sealed class StubHandler : IHandler
        {
            public Task<IResponse> HandleAsync(
                IRequest request, CancellationToken cancellationToken = default)
                => Task.FromResult<IResponse>(new Response(new Fields()));
        }
    }
}
