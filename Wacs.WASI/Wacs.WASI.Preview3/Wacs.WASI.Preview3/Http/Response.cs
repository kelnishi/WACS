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
    /// Host interface for the WIT <c>resource response</c>. An
    /// HTTP response — status code, headers, body, optional
    /// trailers.
    /// </summary>
    public interface IResponse
    {
        ushort GetStatusCode();
        void SetStatusCode(ushort statusCode);
        IFields GetHeaders();

        /// <summary>The response body. Stream abstraction; null
        /// means no body.</summary>
        Stream? Body { get; }

        /// <summary>Trailer fields delivered after the body.
        /// Set by the producer once the body finishes.</summary>
        IFields? Trailers { get; }
    }

    /// <summary>
    /// In-memory <see cref="IResponse"/> implementation.
    /// </summary>
    public sealed class Response : IResponse
    {
        private ushort _statusCode = 200;
        private readonly IFields _headers;

        public Response(
            IFields headers,
            Stream? body = null,
            IFields? trailers = null)
        {
            _headers = headers
                ?? throw new ArgumentNullException(nameof(headers));
            Body = body;
            Trailers = trailers;
        }

        public ushort GetStatusCode() => _statusCode;
        public void SetStatusCode(ushort statusCode) =>
            _statusCode = statusCode;

        public IFields GetHeaders() => _headers;
        public Stream? Body { get; }
        public IFields? Trailers { get; }
    }
}
