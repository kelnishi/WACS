// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Wacs.WASI.Preview3.Http
{
    /// <summary>
    /// Host interface for
    /// <c>wasi:http/client@0.3.0-rc-2026-03-15</c>. Outbound
    /// HTTP request — guest builds a request, host sends it and
    /// returns the response.
    /// </summary>
    public interface IClient
    {
        /// <summary><c>send: async func(request: request)
        /// -&gt; result&lt;response, error-code&gt;</c>.</summary>
        Task<IResponse> SendAsync(
            IRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Default <see cref="IClient"/> implementation backed by
    /// <see cref="HttpClient"/>. Maps WIT request properties to
    /// <see cref="HttpRequestMessage"/>, sends, lifts the
    /// <see cref="HttpResponseMessage"/> back to a <see cref="Response"/>.
    ///
    /// <para>Maps the most common .NET HTTP exceptions to
    /// typed <see cref="HttpException"/> codes:</para>
    /// <list type="bullet">
    ///   <item><see cref="TaskCanceledException"/> → <see cref="HttpErrorCode.ConnectionTimeout"/></item>
    ///   <item><see cref="HttpRequestException"/> →
    ///     <see cref="HttpErrorCode.ConnectionRefused"/> (best-effort)</item>
    ///   <item>Uncategorized → <see cref="HttpErrorCode.InternalError"/>
    ///     with the exception message as payload</item>
    /// </list>
    ///
    /// <para>The default
    /// <see cref="HttpClient"/> is constructed with a fresh
    /// <see cref="HttpClientHandler"/>; embedders that want
    /// pinned cert validation, custom proxies, or shared client
    /// pooling should construct an <see cref="HttpBackedClient"/>
    /// with their own <see cref="HttpClient"/> instance.</para>
    /// </summary>
    public sealed class HttpBackedClient : IClient, IDisposable
    {
        private readonly HttpClient _http;
        private readonly bool _ownsClient;

        public HttpBackedClient()
        {
            _http = new HttpClient();
            _ownsClient = true;
        }

        public HttpBackedClient(HttpClient httpClient)
        {
            _http = httpClient
                ?? throw new ArgumentNullException(nameof(httpClient));
            _ownsClient = false;
        }

        /// <summary>Dispose the underlying
        /// <see cref="HttpClient"/> iff this instance owns it
        /// (constructed via the parameterless ctor).</summary>
        public void Dispose()
        {
            if (_ownsClient) _http.Dispose();
        }

        public async Task<IResponse> SendAsync(
            IRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var msg = BuildHttpRequestMessage(request);

            HttpResponseMessage httpResp;
            try
            {
                httpResp = await _http.SendAsync(
                    msg,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException oce)
                when (oce.CancellationToken == cancellationToken)
            {
                // User-driven cancellation (their token fired).
                // Propagate as-is so async/await semantics hold.
                throw;
            }
            catch (OperationCanceledException)
            {
                // Otherwise: HttpClient.Timeout fired or some
                // internal cancellation — surface as connection
                // timeout.
                throw new HttpException(
                    HttpErrorCode.ConnectionTimeout,
                    "outbound request timed out.");
            }
            catch (HttpRequestException hex)
            {
                // .NET surfaces a wide variety of failures through
                // HttpRequestException; without unwrapping the
                // inner exception we can't distinguish DNS from
                // connect-refused. Default to ConnectionRefused
                // for the simple "couldn't connect" case.
                throw new HttpException(
                    HttpErrorCode.ConnectionRefused, hex.Message);
            }
            catch (Exception ex)
            {
                throw new HttpException(HttpErrorCode.InternalError,
                    ex.Message);
            }

            return await LiftResponse(httpResp, cancellationToken)
                .ConfigureAwait(false);
        }

        private static HttpRequestMessage BuildHttpRequestMessage(
            IRequest request)
        {
            var methodTag = request.GetMethod().Kind;
            var methodStr = methodTag == HttpMethod.Tag.Other
                ? request.GetMethod().OtherName
                : methodTag.ToString().ToUpperInvariant();
            var method = new System.Net.Http.HttpMethod(methodStr);

            var scheme = request.GetScheme();
            var schemeStr = scheme.HasValue
                ? scheme.Value.ToString()
                : "http";
            var authority = request.GetAuthority()
                ?? throw new HttpException(
                    HttpErrorCode.HttpRequestUriInvalid,
                    "request authority not set");
            var path = request.GetPathWithQuery() ?? "/";
            var uri = new Uri(
                $"{schemeStr}://{authority}{path}", UriKind.Absolute);

            var msg = new HttpRequestMessage(method, uri);

            // Headers — split between "request headers" and
            // "content headers" by HttpClient convention.
            HttpContent? content = null;
            if (request.Body != null)
            {
                content = new StreamContent(request.Body);
                msg.Content = content;
            }

            foreach (var (name, value) in request.GetHeaders().CopyAll())
            {
                var valueStr = System.Text.Encoding.UTF8.GetString(value);
                // Try content headers first if we have a content;
                // fall back to request headers. Standard HttpClient
                // accepts only specific names per category, so the
                // .Add return value tells us which one succeeded.
                if (content != null
                    && content.Headers.TryAddWithoutValidation(name, valueStr))
                    continue;
                msg.Headers.TryAddWithoutValidation(name, valueStr);
            }

            return msg;
        }

        private static async Task<IResponse> LiftResponse(
            HttpResponseMessage httpResp, CancellationToken cancellationToken)
        {
            var fields = new Fields();
            foreach (var h in httpResp.Headers)
                foreach (var v in h.Value)
                    fields.Append(h.Key,
                        System.Text.Encoding.UTF8.GetBytes(v));
            if (httpResp.Content != null)
                foreach (var h in httpResp.Content.Headers)
                    foreach (var v in h.Value)
                        fields.Append(h.Key,
                            System.Text.Encoding.UTF8.GetBytes(v));
            fields.Freeze();

            Stream? body = null;
            if (httpResp.Content != null)
            {
                // Use a memory stream so the body is consumable
                // without dragging the HttpResponseMessage's
                // disposal lifetime into the IResponse.
                var ms = new MemoryStream();
                await httpResp.Content.CopyToAsync(ms)
                    .ConfigureAwait(false);
                ms.Position = 0;
                body = ms;
            }

            var resp = new Response(fields, body);
            resp.SetStatusCode((ushort)httpResp.StatusCode);
            httpResp.Dispose();
            return resp;
        }
    }

    /// <summary>
    /// Host interface for
    /// <c>wasi:http/handler@0.3.0-rc-2026-03-15</c>. Inbound
    /// HTTP request — guest provides this; host invokes it for
    /// each incoming request and returns the produced response.
    /// </summary>
    public interface IHandler
    {
        /// <summary><c>handle: async func(request: request)
        /// -&gt; result&lt;response, error-code&gt;</c>.</summary>
        Task<IResponse> HandleAsync(
            IRequest request, CancellationToken cancellationToken = default);
    }
}
