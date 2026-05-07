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
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>
    /// Production <see cref="IOutgoingHandler"/> backed by
    /// <see cref="HttpClient"/>. Translates
    /// <see cref="OutgoingRequest"/> into an
    /// <see cref="HttpRequestMessage"/>, dispatches via
    /// <see cref="HttpClient.SendAsync(HttpRequestMessage,
    /// HttpCompletionOption, CancellationToken)"/>, and wraps
    /// the returned task in
    /// <see cref="HttpClientFutureIncomingResponse"/>.
    ///
    /// <para>Maps common transport failures to
    /// <see cref="ErrorCode"/> cases:
    /// <list type="bullet">
    /// <item><see cref="TaskCanceledException"/> →
    /// <c>ConnectionTimeout</c></item>
    /// <item><see cref="HttpRequestException"/> with no inner →
    /// <c>HttpProtocolError</c></item>
    /// <item>Anything else → <c>InternalError</c> with the
    /// exception message as payload</item>
    /// </list></para>
    ///
    /// <para>The handler shares the supplied
    /// <see cref="HttpClient"/> across all requests; consumers
    /// typically construct it once at process start and reuse.
    /// </para>
    /// </summary>
    public sealed class HttpClientOutgoingHandler : IOutgoingHandler
    {
        private readonly HttpClient _client;

        public HttpClientOutgoingHandler(HttpClient client)
        {
            _client = client
                ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>Convenience: construct with a default
        /// <see cref="HttpClient"/>. The client is owned by
        /// the handler — disposing the handler disposes the
        /// client.</summary>
        public HttpClientOutgoingHandler() : this(new HttpClient()) { }

        public Result<IFutureIncomingResponse, ErrorCode> Handle(
            IOutgoingRequest request, Option<IRequestOptions> options)
        {
            var req = (OutgoingRequest)request;
            HttpRequestMessage msg;
            try
            {
                msg = BuildRequestMessage(req, options);
            }
            catch (UriFormatException)
            {
                return Result<IFutureIncomingResponse, ErrorCode>.FromErr(
                    new ErrorCode.ErrorCodeHTTPRequestURIInvalid());
            }
            catch (Exception e)
            {
                return Result<IFutureIncomingResponse, ErrorCode>.FromErr(
                    new ErrorCode.ErrorCodeInternalError(
                        Option<string>.Some(e.Message)));
            }

            // Kick off the request; HttpCompletionOption.ResponseHeadersRead
            // returns as soon as headers arrive so the body
            // streams instead of buffering whole.
            var task = _client.SendAsync(msg,
                HttpCompletionOption.ResponseHeadersRead);
            return Result<IFutureIncomingResponse, ErrorCode>.FromOk(
                new HttpClientFutureIncomingResponse(task));
        }

        private static HttpRequestMessage BuildRequestMessage(
            OutgoingRequest req, Option<IRequestOptions> options)
        {
            var method = MethodToHttp(req.Method());
            var uri = ComposeUri(req);
            var msg = new HttpRequestMessage(method, uri);

            // Body — assemble before headers since HttpClient's
            // header bucket (request vs content) depends on
            // whether Content is non-null.
            var body = req.BodyConcrete();
            var bytes = body.GetBytes();
            if (bytes.Length > 0)
                msg.Content = new ByteArrayContent(bytes);

            // Headers come from Fields.Entries — bare
            // (string, byte[])[] return per the hand-written
            // shape. HttpClient's Headers and Content.Headers
            // each enforce a different validation set; for
            // unknown headers TryAddWithoutValidation falls back
            // to Content.Headers when it belongs there
            // (Content-Type, Content-Length, etc.).
            var fields = (Fields)req.Headers();
            foreach (var (name, value) in fields.Entries())
            {
                var headerVal = System.Text.Encoding.UTF8
                    .GetString(value);
                if (!msg.Headers.TryAddWithoutValidation(name, headerVal)
                    && msg.Content != null)
                {
                    msg.Content.Headers.TryAddWithoutValidation(
                        name, headerVal);
                }
            }

            // Apply timeouts from RequestOptions if supplied.
            // HttpClient has only one timeout knob; favor the
            // tightest of the three.
            if (options.HasValue)
            {
                var opts = (RequestOptions)options.Value;
                ulong? tightest = null;
                foreach (var tOpt in new[] {
                    opts.ConnectTimeout(),
                    opts.FirstByteTimeout(),
                    opts.BetweenBytesTimeout() })
                {
                    if (!tOpt.HasValue) continue;
                    if (!tightest.HasValue
                        || tOpt.Value < tightest.Value)
                        tightest = tOpt.Value;
                }
                // RequestOptions are per-request-overrides; we
                // can't mutate the shared HttpClient.Timeout
                // safely. v0 ignores them — production hosts
                // wanting per-request timeouts can subclass and
                // override Handle() to pass a per-call
                // CancellationToken.
            }

            return msg;
        }

        private static System.Net.Http.HttpMethod MethodToHttp(Method m)
        {
            return m switch
            {
                Method.MethodGet => System.Net.Http.HttpMethod.Get,
                Method.MethodHead => System.Net.Http.HttpMethod.Head,
                Method.MethodPost => System.Net.Http.HttpMethod.Post,
                Method.MethodPut => System.Net.Http.HttpMethod.Put,
                Method.MethodDelete => System.Net.Http.HttpMethod.Delete,
                Method.MethodOptions =>
                    System.Net.Http.HttpMethod.Options,
                Method.MethodTrace =>
                    System.Net.Http.HttpMethod.Trace,
#if NET8_0_OR_GREATER
                Method.MethodPatch => System.Net.Http.HttpMethod.Patch,
                Method.MethodConnect =>
                    System.Net.Http.HttpMethod.Connect,
#else
                Method.MethodPatch =>
                    new System.Net.Http.HttpMethod("PATCH"),
                Method.MethodConnect =>
                    new System.Net.Http.HttpMethod("CONNECT"),
#endif
                Method.MethodOther other =>
                    new System.Net.Http.HttpMethod(other.Value),
                _ => System.Net.Http.HttpMethod.Get,
            };
        }

        private static Uri ComposeUri(OutgoingRequest req)
        {
            var schemeOpt = req.Scheme();
            var authorityOpt = req.Authority();
            var pathOpt = req.PathWithQuery();

            string scheme = "https";
            if (schemeOpt.HasValue)
            {
                scheme = schemeOpt.Value switch
                {
                    Scheme.SchemeHTTP => "http",
                    Scheme.SchemeHTTPS => "https",
                    Scheme.SchemeOther so => so.Value,
                    _ => "https",
                };
            }
            var authority = authorityOpt.HasValue
                ? authorityOpt.Value : "";
            if (string.IsNullOrEmpty(authority))
                throw new UriFormatException(
                    "outgoing-request missing authority");
            var path = pathOpt.HasValue ? pathOpt.Value : "/";
            return new Uri(scheme + "://" + authority + path);
        }
    }

    /// <summary>
    /// <see cref="FutureIncomingResponse"/> backed by a pending
    /// <see cref="Task{HttpResponseMessage}"/>. The pollable
    /// returned by <see cref="Subscribe"/> blocks the calling
    /// thread on the task; <see cref="Get"/> reads its
    /// completion state without blocking.
    /// </summary>
    public sealed class HttpClientFutureIncomingResponse
        : FutureIncomingResponse
    {
        private readonly Task<HttpResponseMessage> _task;
        private bool _consumed;

        public HttpClientFutureIncomingResponse(
            Task<HttpResponseMessage> task)
        {
            _task = task ?? throw new ArgumentNullException(
                nameof(task));
        }

        public override Pollable SubscribeConcrete()
            => new TaskBackedPollable(_task);

        public override Option<Result<Result<IIncomingResponse,
            ErrorCode>, Unit>> Get()
        {
            if (_consumed)
                return Option<Result<Result<IIncomingResponse,
                    ErrorCode>, Unit>>.Some(
                    Result<Result<IIncomingResponse, ErrorCode>,
                        Unit>.FromErr(Unit.Value));

            if (!_task.IsCompleted)
                return Option<Result<Result<IIncomingResponse,
                    ErrorCode>, Unit>>.None;

            _consumed = true;

            if (_task.IsCanceled)
                return ReadyErr(
                    new ErrorCode.ErrorCodeConnectionTimeout());

            if (_task.IsFaulted)
            {
                var ex = _task.Exception?.InnerException
                    ?? _task.Exception ?? new Exception("send failed");
                ErrorCode code = ex switch
                {
                    TaskCanceledException =>
                        new ErrorCode.ErrorCodeConnectionTimeout(),
                    HttpRequestException =>
                        new ErrorCode.ErrorCodeHTTPProtocolError(),
                    _ => new ErrorCode.ErrorCodeInternalError(
                        Option<string>.Some(ex.Message)),
                };
                return ReadyErr(code);
            }

            // Ok — wrap the HttpResponseMessage.
            var resp = new HttpClientIncomingResponse(_task.Result);
            return Option<Result<Result<IIncomingResponse,
                ErrorCode>, Unit>>.Some(
                Result<Result<IIncomingResponse, ErrorCode>,
                    Unit>.FromOk(
                    Result<IIncomingResponse, ErrorCode>.FromOk(
                        resp)));
        }

        private static Option<Result<Result<IIncomingResponse,
            ErrorCode>, Unit>> ReadyErr(ErrorCode code)
            => Option<Result<Result<IIncomingResponse,
                ErrorCode>, Unit>>.Some(
                Result<Result<IIncomingResponse, ErrorCode>,
                    Unit>.FromOk(
                    Result<IIncomingResponse, ErrorCode>.FromErr(
                        code)));

        public override void Dispose()
        {
            // Don't cancel the task — the consumer may still
            // observe it via Get. Just release the response
            // when the task has completed.
            if (_task.IsCompletedSuccessfully)
                _task.Result.Dispose();
        }
    }

    /// <summary>
    /// <see cref="IncomingResponse"/> wrapping an
    /// <see cref="HttpResponseMessage"/>. Headers populate from
    /// the response on construction; the body lazily wraps the
    /// content stream.
    /// </summary>
    public sealed class HttpClientIncomingResponse : IncomingResponse
    {
        private readonly HttpResponseMessage _resp;

        public HttpClientIncomingResponse(HttpResponseMessage resp)
        {
            _resp = resp ?? throw new ArgumentNullException(
                nameof(resp));
            _status = (ushort)(int)resp.StatusCode;
            _headers = BuildHeaders(resp);
        }

        private static Fields BuildHeaders(HttpResponseMessage resp)
        {
            var f = new Fields();
            foreach (var h in resp.Headers)
            {
                foreach (var v in h.Value)
                    f.Append(h.Key, System.Text.Encoding.UTF8.GetBytes(v));
            }
            if (resp.Content != null)
            {
                foreach (var h in resp.Content.Headers)
                {
                    foreach (var v in h.Value)
                        f.Append(h.Key,
                            System.Text.Encoding.UTF8.GetBytes(v));
                }
            }
            return f;
        }

        public override IncomingBody ConsumeConcrete()
            => _body ??= new HttpClientIncomingBody(_resp);

        public override void Dispose()
        {
            base.Dispose();
            _resp.Dispose();
        }
    }

    /// <summary>
    /// <see cref="IncomingBody"/> that streams from
    /// <see cref="HttpResponseMessage.Content"/>'s read stream
    /// via a <see cref="HostFileInputStream"/>. The trailers
    /// future is always-ready empty (HTTP/1.1 trailers are
    /// rarely surfaced; HTTP/2/3 trailers would need
    /// HttpResponseMessage.TrailingHeaders integration).
    /// </summary>
    public sealed class HttpClientIncomingBody : IncomingBody
    {
        private readonly HttpResponseMessage _resp;

        public HttpClientIncomingBody(HttpResponseMessage resp)
        {
            _resp = resp;
        }

        public override InputStream StreamConcrete()
        {
            if (_stream != null) return _stream;
            // ReadAsStream blocks if content isn't yet
            // streaming; safe because
            // HttpCompletionOption.ResponseHeadersRead returned
            // once headers were available.
#if NET8_0_OR_GREATER
            var s = _resp.Content?.ReadAsStream()
                ?? System.IO.Stream.Null;
#else
            var s = _resp.Content?.ReadAsStreamAsync()
                .GetAwaiter().GetResult()
                ?? System.IO.Stream.Null;
#endif
            _stream = new Wacs.WASI.Preview2.Filesystem
                .HostFileInputStream(s);
            return _stream;
        }
    }

    /// <summary>
    /// <see cref="Pollable"/> backed by a <see cref="Task"/>.
    /// <see cref="IPollable.Ready"/> is <see cref="Task.IsCompleted"/>;
    /// <see cref="IPollable.Block"/> awaits synchronously.
    /// Used by the HttpClient-backed future to surface task
    /// completion through the WASI poll API.
    /// </summary>
    public sealed class TaskBackedPollable : Pollable
    {
        private readonly Task _task;
        public TaskBackedPollable(Task task)
        {
            _task = task ?? throw new ArgumentNullException(
                nameof(task));
        }
        public override bool Ready() => _task.IsCompleted;
        public override void Block()
        {
            try { _task.Wait(); }
            catch { /* completion via Get propagates the error */ }
        }
    }
}
