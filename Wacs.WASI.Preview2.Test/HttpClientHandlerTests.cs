// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Http;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>
    /// Verifies the production HttpClient-backed
    /// IOutgoingHandler. Uses a mock HttpMessageHandler so the
    /// tests don't hit the network.
    /// </summary>
    public class HttpClientHandlerTests
    {
        // Stub message handler that returns a canned response.
        private sealed class StubHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest;
            public byte[]? LastBody;
            public Func<HttpRequestMessage, HttpResponseMessage> Respond;

            public StubHandler(
                Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                Respond = respond;
            }

            protected override async Task<HttpResponseMessage>
                SendAsync(HttpRequestMessage request,
                          CancellationToken cancellationToken)
            {
                LastRequest = request;
                if (request.Content != null)
                    LastBody = await request.Content
                        .ReadAsByteArrayAsync();
                return Respond(request);
            }
        }

        [Fact]
        public void Handle_returns_FutureIncomingResponse_with_pending_task()
        {
            var stub = new StubHandler(req =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(
                        new byte[] { (byte)'h', (byte)'i' }),
                });
            var handler = new HttpClientOutgoingHandler(
                new HttpClient(stub));

            var req = new OutgoingRequest();
            req.SetScheme(Option<Scheme>.Some(new Scheme.SchemeHTTP()));
            req.SetAuthority(Option<string>.Some("example.com"));
            req.SetPathWithQuery(Option<string>.Some("/hello"));

            var r = handler.Handle(req,
                Option<IRequestOptions>.None);
            Assert.True(r.IsOk);
            var future = (FutureIncomingResponse)r.Ok;

            // Drain the future — Block on the pollable to wait,
            // then Get to read the result.
            future.Subscribe().Block();
            var got = future.Get();
            Assert.True(got.HasValue);
            Assert.True(got.Value.IsOk);
            Assert.True(got.Value.Ok.IsOk);
            var resp = got.Value.Ok.Ok;
            Assert.Equal(200, resp.Status());

            // Verify outbound request reached the stub handler.
            Assert.NotNull(stub.LastRequest);
            Assert.Equal("http://example.com/hello",
                stub.LastRequest!.RequestUri!.ToString());
        }

        [Fact]
        public void Handle_serializes_body_bytes_into_request()
        {
            var stub = new StubHandler(req =>
                new HttpResponseMessage(HttpStatusCode.OK));
            var handler = new HttpClientOutgoingHandler(
                new HttpClient(stub));

            var req = new OutgoingRequest();
            req.SetMethod(new Method.MethodPost());
            req.SetScheme(Option<Scheme>.Some(new Scheme.SchemeHTTP()));
            req.SetAuthority(Option<string>.Some("example.com"));
            req.SetPathWithQuery(Option<string>.Some("/upload"));

            // Write some bytes through the body's output stream
            // — this is how a guest assembles a request body.
            var body = req.BodyConcrete();
            var stream = body.WriteConcrete();
            stream.Write(new byte[] {
                (byte)'p', (byte)'a', (byte)'y', (byte)'l', (byte)'o',
                (byte)'a', (byte)'d' });

            var r = handler.Handle(req,
                Option<IRequestOptions>.None);
            Assert.True(r.IsOk);
            ((FutureIncomingResponse)r.Ok).Subscribe().Block();

            Assert.Equal(new byte[] {
                (byte)'p', (byte)'a', (byte)'y', (byte)'l', (byte)'o',
                (byte)'a', (byte)'d' }, stub.LastBody);
        }

        [Fact]
        public void Handle_missing_authority_returns_URIInvalid_error()
        {
            var stub = new StubHandler(req =>
                new HttpResponseMessage(HttpStatusCode.OK));
            var handler = new HttpClientOutgoingHandler(
                new HttpClient(stub));

            var req = new OutgoingRequest();
            // Skip authority — the request can't compose a URI.

            var r = handler.Handle(req,
                Option<IRequestOptions>.None);
            Assert.True(r.IsErr);
            Assert.IsType<ErrorCode.ErrorCodeHTTPRequestURIInvalid>(
                r.Err);
        }

        [Fact]
        public void Future_Get_returns_already_consumed_on_second_call()
        {
            var stub = new StubHandler(req =>
                new HttpResponseMessage(HttpStatusCode.OK));
            var handler = new HttpClientOutgoingHandler(
                new HttpClient(stub));

            var req = new OutgoingRequest();
            req.SetScheme(Option<Scheme>.Some(new Scheme.SchemeHTTP()));
            req.SetAuthority(Option<string>.Some("example.com"));

            var r = handler.Handle(req,
                Option<IRequestOptions>.None);
            var future = (FutureIncomingResponse)r.Ok;

            future.Subscribe().Block();
            // First Get: ready response.
            var first = future.Get();
            Assert.True(first.HasValue);
            Assert.True(first.Value.IsOk);

            // Second Get is the already-consumed branch
            // (outer Err per the WIT spec).
            var second = future.Get();
            Assert.True(second.HasValue);
            Assert.True(second.Value.IsErr);
        }
    }
}
