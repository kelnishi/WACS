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
    /// <summary>WIT <c>wasi:http/types.incoming-request</c>
    /// — a request being processed on the server side.
    /// </summary>
    [WasiResource("incoming-request")]
    public class IncomingRequest : IDisposable
    {
        protected HttpMethod _method = new HttpMethodGet();
        protected HttpScheme? _scheme;
        protected Fields _headers = new Fields();
        protected string? _pathWithQuery;
        protected string? _authority;
        protected IncomingBody? _body;

        /// <summary>HTTP request method on the server side.
        /// WIT <c>method() -&gt; method</c>.</summary>
        public virtual HttpMethod Method() => _method;

        /// <summary>HTTP scheme. WIT <c>scheme: func()
        ///   -&gt; option&lt;scheme&gt;</c>.</summary>
        [WasiOptionalReturn]
        public virtual HttpScheme? Scheme() => _scheme;

        public virtual Fields Headers() => _headers;

        [WasiOptionalReturn]
        [WasiMethodName("path-with-query")]
        public virtual string? PathWithQuery() => _pathWithQuery;

        [WasiOptionalReturn]
        public virtual string? Authority() => _authority;

        /// <summary>Take ownership of the request body for
        /// reading.</summary>
        [WasiErrorResult]
        public virtual IncomingBody Consume()
            => _body ??= new IncomingBody();

        public virtual void Dispose() { }
    }
}
