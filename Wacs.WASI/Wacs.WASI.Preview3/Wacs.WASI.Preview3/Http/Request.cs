// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;

namespace Wacs.WASI.Preview3.Http
{
    /// <summary>
    /// Host interface for the WIT <c>resource request</c>. An
    /// HTTP request — method, scheme, authority,
    /// path-with-query, headers, body, optional request-options.
    /// </summary>
    public interface IRequest
    {
        HttpMethod GetMethod();
        void SetMethod(HttpMethod method);

        string? GetPathWithQuery();
        void SetPathWithQuery(string? pathWithQuery);

        HttpScheme? GetScheme();
        void SetScheme(HttpScheme? scheme);

        string? GetAuthority();
        void SetAuthority(string? authority);

        IRequestOptions? GetOptions();

        IFields GetHeaders();

        /// <summary>The request body, in host-side terms. Stream
        /// abstraction; null means no body. The canon-async
        /// binding bridges this <see cref="System.IO.Stream"/>
        /// to a <c>stream&lt;u8&gt;</c> handle on the wire.</summary>
        Stream? Body { get; }

        /// <summary>Optional trailer fields delivered after the
        /// body. Set by the producer once the body finishes.</summary>
        IFields? Trailers { get; }
    }

    /// <summary>
    /// In-memory <see cref="IRequest"/> implementation. Header
    /// and body state live entirely in the CLR object — no
    /// network I/O. The
    /// <see cref="HttpBackedClient"/> consumes one of these to
    /// invoke the outbound HTTP call;
    /// <see cref="IHandler"/> implementations receive one
    /// representing an inbound call.
    /// </summary>
    public sealed class Request : IRequest
    {
        private HttpMethod _method = HttpMethod.Get;
        private string? _pathWithQuery;
        private HttpScheme? _scheme;
        private string? _authority;
        private readonly IRequestOptions? _options;
        private readonly IFields _headers;

        public Request(
            IFields headers,
            Stream? body = null,
            IFields? trailers = null,
            IRequestOptions? options = null)
        {
            _headers = headers
                ?? throw new ArgumentNullException(nameof(headers));
            Body = body;
            Trailers = trailers;
            _options = options;
        }

        public HttpMethod GetMethod() => _method;
        public void SetMethod(HttpMethod method) => _method = method;

        public string? GetPathWithQuery() => _pathWithQuery;
        public void SetPathWithQuery(string? pathWithQuery)
            => _pathWithQuery = pathWithQuery;

        public HttpScheme? GetScheme() => _scheme;
        public void SetScheme(HttpScheme? scheme) => _scheme = scheme;

        public string? GetAuthority() => _authority;
        public void SetAuthority(string? authority) => _authority = authority;

        public IRequestOptions? GetOptions() => _options;

        public IFields GetHeaders() => _headers;

        public Stream? Body { get; }
        public IFields? Trailers { get; }
    }
}
