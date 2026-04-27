// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT variant <c>wasi:http/types.method</c>:
    /// <code>variant method {
    ///   get, head, post, put, delete, connect, options,
    ///   trace, patch, other(string),
    /// }</code>
    /// Wire form: 1 disc + 2 joined-payload slots
    /// (string ptr + len, ignored when not "other").
    /// Total 3 flat wire slots. The well-known cases use
    /// <see cref="HttpMethodGet"/> through
    /// <see cref="HttpMethodPatch"/>; arbitrary methods
    /// surface as <see cref="HttpMethodOther"/>.</summary>
    public abstract class HttpMethod
    {
        public string Name { get; }
        protected HttpMethod(string name) { Name = name; }
    }

    public sealed class HttpMethodGet : HttpMethod
    { public HttpMethodGet() : base("GET") { } }
    public sealed class HttpMethodHead : HttpMethod
    { public HttpMethodHead() : base("HEAD") { } }
    public sealed class HttpMethodPost : HttpMethod
    { public HttpMethodPost() : base("POST") { } }
    public sealed class HttpMethodPut : HttpMethod
    { public HttpMethodPut() : base("PUT") { } }
    public sealed class HttpMethodDelete : HttpMethod
    { public HttpMethodDelete() : base("DELETE") { } }
    public sealed class HttpMethodConnect : HttpMethod
    { public HttpMethodConnect() : base("CONNECT") { } }
    public sealed class HttpMethodOptions : HttpMethod
    { public HttpMethodOptions() : base("OPTIONS") { } }
    public sealed class HttpMethodTrace : HttpMethod
    { public HttpMethodTrace() : base("TRACE") { } }
    public sealed class HttpMethodPatch : HttpMethod
    { public HttpMethodPatch() : base("PATCH") { } }

    /// <summary>method case "other(string)" — a non-standard
    /// method name carried as a string.</summary>
    public sealed class HttpMethodOther : HttpMethod
    {
        public HttpMethodOther(string name) : base(name) { }
    }

    /// <summary>WIT variant <c>wasi:http/types.scheme</c>:
    /// <code>variant scheme { HTTP, HTTPS, other(string) }</code>
    /// </summary>
    public abstract class HttpScheme
    {
        public string Name { get; }
        protected HttpScheme(string name) { Name = name; }
    }
    public sealed class HttpSchemeHttp : HttpScheme
    { public HttpSchemeHttp() : base("http") { } }
    public sealed class HttpSchemeHttps : HttpScheme
    { public HttpSchemeHttps() : base("https") { } }
    public sealed class HttpSchemeOther : HttpScheme
    {
        public HttpSchemeOther(string name) : base(name) { }
    }

    /// <summary>WIT
    /// <c>wasi:http/types.fields</c> — case-insensitive
    /// HTTP header / trailer key-value collection.
    ///
    /// <para>v0 base class is array-backed and mutable. The
    /// WIT API has constructor + has/get/set/append/delete
    /// /entries/clone surface; this v0 ships the methods
    /// whose canon-lower shape rides existing binder paths
    /// (delete + clone). has + append + entries land as
    /// the binder gains string-param-on-primitive-return /
    /// byte[]-param-on-void+result / list-of-(string,byte[])-
    /// return support respectively.</para></summary>
    [WasiResource("fields")]
    public class Fields : IDisposable
    {
        private readonly System.Collections.Generic.List<
            (string Key, byte[] Value)> _entries
            = new System.Collections.Generic.List<
                (string Key, byte[] Value)>();

        /// <summary>WIT <c>constructor()</c> — guest calls
        /// <c>[constructor]fields</c> to create a fresh empty
        /// fields collection. The auto-binder registers this
        /// factory under that import name and table-allocates
        /// the returned instance, handing back the i32 handle.
        /// </summary>
        [WasiConstructor]
        public static Fields New() => new Fields();

        /// <summary>True iff there is at least one entry
        /// with key matching <paramref name="name"/> (case-
        /// insensitive). WIT
        /// <c>has: func(name: field-key) -&gt; bool</c>.</summary>
        public virtual bool Has(string name)
        {
            foreach (var (k, _) in _entries)
                if (string.Equals(k, name,
                    System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>All values for entries matching
        /// <paramref name="name"/> (case-insensitive). WIT
        /// <c>get: func(name: field-key)
        ///   -&gt; list&lt;field-value&gt;</c>.</summary>
        public virtual byte[][] Get(string name)
        {
            var result = new System.Collections.Generic.List<byte[]>();
            foreach (var (k, v) in _entries)
                if (string.Equals(k, name,
                    System.StringComparison.OrdinalIgnoreCase))
                    result.Add(v);
            return result.ToArray();
        }

        /// <summary>Append a (key, value) entry. WIT
        /// <c>append(field-key, field-value)
        ///   -&gt; result&lt;_, header-error&gt;</c>.</summary>
        [WasiErrorResult]
        public virtual void Append(string name, byte[] value)
            => _entries.Add((name, value));

        /// <summary>Replace all entries matching
        /// <paramref name="name"/> with the supplied list
        /// of values. WIT
        /// <c>set: func(name: field-key,
        ///              value: list&lt;field-value&gt;)
        ///   -&gt; result&lt;_, header-error&gt;</c>.</summary>
        [WasiErrorResult]
        public virtual void Set(string name, byte[][] value)
        {
            _entries.RemoveAll(e => string.Equals(e.Key, name,
                System.StringComparison.OrdinalIgnoreCase));
            foreach (var v in value)
                _entries.Add((name, v));
        }

        /// <summary>Host-side alias for
        /// <see cref="Append(string, byte[])"/> kept for
        /// fixture setup paths that pre-seed the entry list
        /// without going through the canon-lower wire.</summary>
        public void AppendEntry(string name, byte[] value)
            => Append(name, value);

        /// <summary>Remove every entry matching
        /// <paramref name="name"/> (case-insensitive).</summary>
        [WasiErrorResult]
        public virtual void Delete(string name)
            => _entries.RemoveAll(e => string.Equals(e.Key, name,
                System.StringComparison.OrdinalIgnoreCase));

        /// <summary>Deep-clone the entry list into a fresh
        /// Fields instance. WIT
        /// <c>clone: func() -&gt; fields</c>.</summary>
        public virtual Fields Clone()
        {
            var copy = new Fields();
            foreach (var (k, v) in _entries)
                copy._entries.Add((k, (byte[])v.Clone()));
            return copy;
        }

        /// <summary>Host-side List<(Key, Value)> backing
        /// store accessor. Used by tests to inspect the
        /// captured entry list; the WIT-bound entries() goes
        /// through <see cref="EntriesArray"/> which returns
        /// a fresh ValueTuple array (the shape the canon-
        /// lower binder consumes).</summary>
        public System.Collections.Generic.IReadOnlyList<
            (string Key, byte[] Value)> Entries => _entries;

        /// <summary>WIT <c>entries: func() -&gt;
        ///   list&lt;tuple&lt;field-key, field-value&gt;&gt;</c>.
        /// Snapshot of the entry list as ValueTuple<string,
        /// byte[]>[]; the binder writes the canon-lower form
        /// (list-ptr, list-len) at retArea + element pairs
        /// at the allocated array.</summary>
        [WasiMethodName("entries")]
        public virtual System.ValueTuple<string, byte[]>[] EntriesArray()
        {
            var arr = new System.ValueTuple<string, byte[]>[
                _entries.Count];
            for (int i = 0; i < _entries.Count; i++)
                arr[i] = (_entries[i].Key, _entries[i].Value);
            return arr;
        }

        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.outgoing-request</c>
    /// — a request being constructed for outbound dispatch.
    /// </summary>
    [WasiResource("outgoing-request")]
    public class OutgoingRequest : IDisposable
    {
        protected Fields _headers = new Fields();
        protected string? _pathWithQuery;
        protected string? _authority;
        protected HttpMethod _method = new HttpMethodGet();

        /// <summary>WIT <c>constructor(headers: own&lt;headers&gt;)</c>.
        /// Guest passes a Fields handle; we adopt it on the
        /// fresh request. Headers ownership transfers in.</summary>
        [WasiConstructor]
        public static OutgoingRequest New(Fields headers)
            => new OutgoingRequest { _headers = headers };

        /// <summary>HTTP request method. WIT
        /// <c>method() -&gt; method</c>.</summary>
        public virtual HttpMethod Method() => _method;

        /// <summary>WIT <c>set-method: func(method: method)
        ///   -&gt; result</c>. Variant param flat-encoded
        /// (disc + ptr + len for the joined string payload).
        /// Always-Ok in v0.</summary>
        [WasiUnitResult]
        [WasiMethodName("set-method")]
        public virtual void SetMethod(HttpMethod method)
            => _method = method;

        /// <summary>HTTP scheme for the request — null when
        /// not set. WIT <c>scheme() -&gt; option&lt;scheme&gt;</c>.
        /// </summary>
        protected HttpScheme? _scheme;

        /// <summary>WIT <c>scheme: func()
        ///   -&gt; option&lt;scheme&gt;</c>. Reads back the
        /// scheme set by SetScheme — null is None on the wire.
        /// </summary>
        [WasiOptionalReturn]
        public virtual HttpScheme? Scheme() => _scheme;

        /// <summary>WIT <c>set-scheme: func(
        ///   scheme: option&lt;scheme&gt;) -&gt; result</c>.
        /// option<variant> param: 4 wire slots
        /// (option disc + variant disc + ptr + len). null
        /// clears the scheme.</summary>
        [WasiUnitResult]
        [WasiMethodName("set-scheme")]
        public virtual void SetScheme(
            [WasiOptionalParam] HttpScheme? scheme)
            => _scheme = scheme;

        /// <summary>Per WIT semantics, returns ownership of
        /// the headers Fields to the guest. Default returns
        /// the configured _headers.</summary>
        public virtual Fields Headers() => _headers;

        /// <summary>HTTP path + optional query string. Null
        /// when no path is set yet.</summary>
        [WasiOptionalReturn]
        [WasiMethodName("path-with-query")]
        public virtual string? PathWithQuery() => _pathWithQuery;

        /// <summary>WIT <c>set-path-with-query: func(
        ///   path-with-query: option&lt;string&gt;)
        ///   -&gt; result&lt;_, _&gt;</c>.</summary>
        [WasiErrorResult]
        [WasiMethodName("set-path-with-query")]
        public virtual void SetPathWithQuery(
            [WasiOptionalParam] string? pathWithQuery)
            => _pathWithQuery = pathWithQuery;

        /// <summary>Authority (host:port) for the request.
        /// Null when not set.</summary>
        [WasiOptionalReturn]
        public virtual string? Authority() => _authority;

        /// <summary>WIT <c>set-authority: func(
        ///   authority: option&lt;string&gt;)
        ///   -&gt; result&lt;_, _&gt;</c>.</summary>
        [WasiErrorResult]
        [WasiMethodName("set-authority")]
        public virtual void SetAuthority(
            [WasiOptionalParam] string? authority)
            => _authority = authority;

        /// <summary>Take ownership of the request body for
        /// writing. Per WIT semantics the body can only be
        /// taken once; v0 default doesn't enforce that and
        /// just returns a fresh OutgoingBody every call.</summary>
        [WasiErrorResult]
        public virtual OutgoingBody Body()
            => _body ??= new OutgoingBody();

        protected OutgoingBody? _body;

        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.incoming-request</c>
    /// — a request being processed on the server side.
    /// </summary>
    [WasiResource("incoming-request")]
    public class IncomingRequest : IDisposable
    {
        protected HttpMethod _method = new HttpMethodGet();
        protected HttpScheme? _scheme;

        /// <summary>HTTP request method on the server side.
        /// WIT <c>method() -&gt; method</c>.</summary>
        public virtual HttpMethod Method() => _method;

        /// <summary>HTTP scheme. WIT <c>scheme: func()
        ///   -&gt; option&lt;scheme&gt;</c>.</summary>
        [WasiOptionalReturn]
        public virtual HttpScheme? Scheme() => _scheme;

        public virtual Fields Headers() => new Fields();

        [WasiOptionalReturn]
        [WasiMethodName("path-with-query")]
        public virtual string? PathWithQuery() => null;

        [WasiOptionalReturn]
        public virtual string? Authority() => null;

        /// <summary>Take ownership of the request body for
        /// reading.</summary>
        [WasiErrorResult]
        public virtual IncomingBody Consume()
            => _body ??= new IncomingBody();

        protected IncomingBody? _body;

        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.incoming-response</c>.
    /// Holds status + headers + body of a server-sent
    /// response.</summary>
    [WasiResource("incoming-response")]
    public class IncomingResponse : IDisposable
    {
        /// <summary>HTTP status code (200, 404, etc.).
        /// WIT <c>status() -&gt; status-code</c> where
        /// status-code is u16.</summary>
        public virtual ushort Status() => 200;

        /// <summary>Headers attached to this response. The
        /// returned <see cref="Fields"/> handle is owned by
        /// the guest per WIT semantics. Default returns an
        /// empty Fields.</summary>
        public virtual Fields Headers() => new Fields();

        /// <summary>Take ownership of the response body for
        /// reading.</summary>
        [WasiErrorResult]
        public virtual IncomingBody Consume()
            => _body ??= new IncomingBody();

        protected IncomingBody? _body;

        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.outgoing-response</c>.
    /// Server-side response under construction.</summary>
    [WasiResource("outgoing-response")]
    public class OutgoingResponse : IDisposable
    {
        protected ushort _statusCode = 200;
        protected Fields _headers = new Fields();

        /// <summary>WIT <c>constructor(headers: own&lt;headers&gt;)</c>.
        /// Guest passes a Fields handle; ownership transfers
        /// in on construction.</summary>
        [WasiConstructor]
        public static OutgoingResponse New(Fields headers)
            => new OutgoingResponse { _headers = headers };

        public virtual ushort StatusCode() => _statusCode;

        [WasiErrorResult]
        [WasiMethodName("set-status-code")]
        public virtual void SetStatusCode(ushort statusCode)
            => _statusCode = statusCode;

        public virtual Fields Headers() => _headers;

        /// <summary>Take ownership of the response body for
        /// writing.</summary>
        [WasiErrorResult]
        public virtual OutgoingBody Body()
            => _body ??= new OutgoingBody();

        protected OutgoingBody? _body;

        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.request-options</c>
    /// — per-request transport tunables (timeouts etc.).
    /// All timeouts are option<duration> = option<u64>;
    /// null means "no timeout".</summary>
    [WasiResource("request-options")]
    public class RequestOptions : IDisposable
    {
        protected ulong? _connectTimeout;
        protected ulong? _firstByteTimeout;
        protected ulong? _betweenBytesTimeout;

        /// <summary>WIT <c>constructor()</c> — zero-arg
        /// factory; guests call <c>[constructor]request-options</c>
        /// to get a fresh tunable bundle with all timeouts
        /// unset.</summary>
        [WasiConstructor]
        public static RequestOptions New() => new RequestOptions();

        [WasiOptionalReturn]
        [WasiMethodName("connect-timeout")]
        public virtual ulong? ConnectTimeout() => _connectTimeout;

        /// <summary>WIT <c>set-connect-timeout: func(
        ///   duration: option&lt;duration&gt;)
        ///   -&gt; result&lt;_, error-code&gt;</c>. duration is
        /// option<u64>; null clears the timeout.</summary>
        [WasiErrorResult]
        [WasiMethodName("set-connect-timeout")]
        public virtual void SetConnectTimeout(
            [WasiOptionalParam] ulong? duration)
            => _connectTimeout = duration;

        [WasiOptionalReturn]
        [WasiMethodName("first-byte-timeout")]
        public virtual ulong? FirstByteTimeout() => _firstByteTimeout;

        /// <summary>WIT <c>set-first-byte-timeout</c>.</summary>
        [WasiErrorResult]
        [WasiMethodName("set-first-byte-timeout")]
        public virtual void SetFirstByteTimeout(
            [WasiOptionalParam] ulong? duration)
            => _firstByteTimeout = duration;

        [WasiOptionalReturn]
        [WasiMethodName("between-bytes-timeout")]
        public virtual ulong? BetweenBytesTimeout()
            => _betweenBytesTimeout;

        /// <summary>WIT <c>set-between-bytes-timeout</c>.</summary>
        [WasiErrorResult]
        [WasiMethodName("set-between-bytes-timeout")]
        public virtual void SetBetweenBytesTimeout(
            [WasiOptionalParam] ulong? duration)
            => _betweenBytesTimeout = duration;

        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.incoming-body</c>.
    /// Read side of an HTTP body — guest pulls bytes via
    /// the <see cref="Stream"/> InputStream.</summary>
    [WasiResource("incoming-body")]
    public class IncomingBody : IDisposable
    {
        protected InputStream? _stream;
        protected FutureTrailers? _trailers;

        /// <summary>Take ownership of the read stream.
        /// WIT <c>%stream: func()
        ///   -&gt; result&lt;own&lt;input-stream&gt;,
        ///                _&gt;</c> (% escapes the keyword;
        /// the wire import-name is "stream").</summary>
        [WasiErrorResult]
        [WasiMethodName("stream")]
        public virtual InputStream Stream()
            => _stream ??= new InputStream();

        /// <summary>Take ownership of the trailers future.
        /// WIT <c>finish: static func(this: own&lt;incoming-body&gt;)
        ///   -&gt; own&lt;future-trailers&gt;</c>. Bare own
        /// return — no result wrapper, just an i32 handle.
        /// [WasiStaticMethod] swaps the import-name prefix to
        /// [static]; the wire form is unchanged from a
        /// zero-arg instance factory.</summary>
        [WasiStaticMethod]
        public virtual FutureTrailers Finish()
            => _trailers ??= new FutureTrailers();

        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.outgoing-body</c>.
    /// Write side of an HTTP body — guest pushes bytes via
    /// the <see cref="Write"/> OutputStream.</summary>
    [WasiResource("outgoing-body")]
    public class OutgoingBody : IDisposable
    {
        protected OutputStream? _stream;

        /// <summary>Take ownership of the write stream.
        /// WIT <c>write: func()
        ///   -&gt; result&lt;own&lt;output-stream&gt;,
        ///                _&gt;</c>.</summary>
        [WasiErrorResult]
        public virtual OutputStream Write()
            => _stream ??= new OutputStream();

        /// <summary>Mark the body as complete with optional
        /// trailers. WIT
        /// <c>finish: static func(this: own&lt;outgoing-body&gt;,
        ///   trailers: option&lt;own&lt;trailers&gt;&gt;)
        ///   -&gt; result&lt;_, error-code&gt;</c>.
        /// At the wire level, an instance method's first
        /// param IS the resource handle, so this can stay an
        /// instance method on the C# class — the
        /// [WasiStaticMethod] attribute just changes the
        /// import-name prefix from [method] to [static].
        /// </summary>
        [WasiErrorResult]
        [WasiStaticMethod]
        public virtual void Finish(
            [WasiOptionalParam] Fields? trailers) { }

        public virtual void Dispose() { }
    }

    /// <summary>WIT
    /// <c>wasi:http/types.future-incoming-response</c> —
    /// async handle for an outbound request's response.
    /// </summary>
    [WasiResource("future-incoming-response")]
    public class FutureIncomingResponse : IDisposable
    {
        /// <summary>Pollable that fires when the response
        /// is ready to be retrieved via get(). Default
        /// returns an always-ready pollable.</summary>
        public virtual Pollable Subscribe() => new Pollable();

        public virtual void Dispose() { }
    }

    /// <summary>Host-side surface for
    /// <c>wasi:http/outgoing-handler.handle</c>:
    /// <code>handle: func(
    ///     request: own&lt;outgoing-request&gt;,
    ///     options: option&lt;own&lt;request-options&gt;&gt;,
    /// ) -&gt; result&lt;own&lt;future-incoming-response&gt;,
    ///                 error-code&gt;;</code>
    /// The dispatch entrypoint every HTTP-client guest
    /// reaches for to send a request.</summary>
    public interface IOutgoingHandler
    {
        FutureIncomingResponse Handle(OutgoingRequest request,
            RequestOptions? options);
    }

    /// <summary>Default <see cref="IOutgoingHandler"/> impl
    /// — returns a fresh stub
    /// <see cref="FutureIncomingResponse"/> regardless of
    /// input. Concrete hosts override to plumb through
    /// <c>System.Net.Http.HttpClient</c> or similar.</summary>
    public sealed class OutgoingHandlerSource : IOutgoingHandler
    {
        [WasiErrorResult]
        public FutureIncomingResponse Handle(OutgoingRequest request,
            [WasiOptionalParam] RequestOptions? options)
            => new FutureIncomingResponse();
    }

    /// <summary>WIT
    /// <c>wasi:http/types.future-trailers</c>.</summary>
    [WasiResource("future-trailers")]
    public class FutureTrailers : IDisposable
    {
        /// <summary>Pollable that fires when the trailers
        /// are ready (or the body has confirmed there are
        /// none).</summary>
        public virtual Pollable Subscribe() => new Pollable();

        public virtual void Dispose() { }
    }

    /// <summary>WIT
    /// <c>wasi:http/types.response-outparam</c>.</summary>
    [WasiResource("response-outparam")]
    public class ResponseOutparam : IDisposable
    {
        /// <summary>Deliver the response into the outparam.
        /// WIT <c>set: static func(param: own&lt;response-outparam&gt;,
        ///   response: result&lt;own&lt;outgoing-response&gt;,
        ///                       error-code&gt;)</c>.
        /// v0 surfaces only the Ok side: when wire disc=0 the
        /// host receives the resolved <see cref="OutgoingResponse"/>;
        /// when disc!=0 the host receives null. Payload-bearing
        /// error-code variants will follow when the binder
        /// learns to decode them.</summary>
        [WasiStaticMethod]
        public virtual void Set(
            [WasiResultParam] OutgoingResponse? response) { }

        public virtual void Dispose() { }
    }
}
