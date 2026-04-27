// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

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
}
