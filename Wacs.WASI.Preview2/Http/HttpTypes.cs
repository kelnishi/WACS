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

        /// <summary>Append a (key, value) entry. Host-side
        /// helper not bound to WIT yet (the WIT
        /// <c>append(field-key, field-value)</c> will route
        /// here once the binder accepts byte[] alongside
        /// string on a void+result wrapper).</summary>
        public void AppendEntry(string name, byte[] value)
            => _entries.Add((name, value));

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

        /// <summary>Snapshot of all (key, value) entries.
        /// Host-side accessor — the WIT <c>entries()</c>
        /// method needs list<tuple<string, list<u8>>> return
        /// support.</summary>
        public System.Collections.Generic.IReadOnlyList<
            (string Key, byte[] Value)> Entries => _entries;

        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.outgoing-request</c>
    /// — a request being constructed for outbound dispatch.
    /// v0 is a marker resource; methods land incrementally.
    /// </summary>
    [WasiResource("outgoing-request")]
    public class OutgoingRequest : IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.incoming-request</c>
    /// — a request being processed on the server side.
    /// </summary>
    [WasiResource("incoming-request")]
    public class IncomingRequest : IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.incoming-response</c>.
    /// </summary>
    [WasiResource("incoming-response")]
    public class IncomingResponse : IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.outgoing-response</c>.
    /// </summary>
    [WasiResource("outgoing-response")]
    public class OutgoingResponse : IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.request-options</c>
    /// — per-request transport tunables (timeouts etc.).
    /// </summary>
    [WasiResource("request-options")]
    public class RequestOptions : IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.incoming-body</c>.
    /// </summary>
    [WasiResource("incoming-body")]
    public class IncomingBody : IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>WIT <c>wasi:http/types.outgoing-body</c>.
    /// </summary>
    [WasiResource("outgoing-body")]
    public class OutgoingBody : IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>WIT
    /// <c>wasi:http/types.future-incoming-response</c> —
    /// async handle for an outbound request's response.
    /// </summary>
    [WasiResource("future-incoming-response")]
    public class FutureIncomingResponse : IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>WIT
    /// <c>wasi:http/types.future-trailers</c>.</summary>
    [WasiResource("future-trailers")]
    public class FutureTrailers : IDisposable
    {
        public virtual void Dispose() { }
    }

    /// <summary>WIT
    /// <c>wasi:http/types.response-outparam</c>.</summary>
    [WasiResource("response-outparam")]
    public class ResponseOutparam : IDisposable
    {
        public virtual void Dispose() { }
    }
}
