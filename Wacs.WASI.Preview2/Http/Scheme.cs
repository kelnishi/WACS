// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Http
{
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
}
