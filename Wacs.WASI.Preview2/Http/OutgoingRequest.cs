// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Http
{
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
        protected HttpScheme? _scheme;
        protected OutgoingBody? _body;

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

        public virtual void Dispose() { }
    }
}
