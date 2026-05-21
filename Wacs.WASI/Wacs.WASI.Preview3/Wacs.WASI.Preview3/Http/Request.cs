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
        {
            if (pathWithQuery == null) { _pathWithQuery = null; return; }
            // Spec corner: empty path-with-query becomes "/"
            // (the http-request fixture asserts this directly).
            if (pathWithQuery.Length == 0) { _pathWithQuery = "/"; return; }
            ValidatePathWithQuery(pathWithQuery);
            _pathWithQuery = pathWithQuery;
        }

        public HttpScheme? GetScheme() => _scheme;
        public void SetScheme(HttpScheme? scheme) => _scheme = scheme;

        public string? GetAuthority() => _authority;
        public void SetAuthority(string? authority)
        {
            if (authority == null) { _authority = null; return; }
            ValidateAuthority(authority);
            _authority = authority;
        }

        // RFC 3986 §3.3: path = *( pchar / "/" )  query = *( pchar / "/" / "?" )
        //   pchar = unreserved / pct-encoded / sub-delims / ":" / "@"
        //   unreserved = ALPHA / DIGIT / "-" / "." / "_" / "~"
        //   sub-delims = "!" / "$" / "&" / "'" / "(" / ")"
        //              / "*" / "+" / "," / ";" / "="
        //
        // The wasip3 fixture's `is_valid_path_char` also accepts
        // any byte >= 0x80 (UTF-8 passthrough). We mirror that.
        private static void ValidatePathWithQuery(string s)
        {
            foreach (var c in s)
            {
                if (IsValidPathOrQueryChar(c)) continue;
                throw new System.ArgumentException(
                    $"path-with-query contains invalid char " +
                    $"'\\u{(int)c:X4}'.");
            }
        }

        private static bool IsValidPathOrQueryChar(char c)
        {
            if ((c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')) return true;
            if (c == '-' || c == '.' || c == '_' || c == '~') return true;
            if (c == '%') return true;
            if ("!$&'()*+,;=".IndexOf(c) >= 0) return true;
            if (c == ':' || c == '@') return true;
            if (c == '/' || c == '?') return true;
            if (c >= 0x80) return true;
            return false;
        }

        // RFC 3986 §3.2:
        //   authority = [ userinfo "@" ] host [ ":" port ]
        //   userinfo  = *( unreserved / pct-encoded / sub-delims / ":" )
        //   host      = IP-literal / IPv4address / reg-name
        //   reg-name  = *( unreserved / pct-encoded / sub-delims )
        //   port      = *DIGIT
        //
        // The wasip3 fixture probes corner cases:
        //   valid:   1.2.3.4 / example.com:80 / user:pass@host:80
        //   invalid: "" / " " / "#" / "::" / ":@" / "@@" / "@:"
        //            "localhost:what" / "["
        private static void ValidateAuthority(string s)
        {
            if (s.Length == 0)
                throw new System.ArgumentException(
                    "authority cannot be empty.");
            // Find a single "@" — at-most-one is allowed.
            int atIdx = -1;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '@') continue;
                if (atIdx >= 0)
                    throw new System.ArgumentException(
                        "authority contains multiple '@'.");
                atIdx = i;
            }
            int hostStart = 0;
            if (atIdx >= 0)
            {
                ValidateUserInfo(s, 0, atIdx);
                hostStart = atIdx + 1;
            }
            // Find ":port" suffix (last ":" outside any IPv6
            // bracket). For our purposes (no IP-literal support
            // per the fixture's commented-out cases) we use the
            // RIGHTMOST ":".
            int colonIdx = s.LastIndexOf(':');
            int hostEnd;
            if (colonIdx > hostStart)
            {
                hostEnd = colonIdx;
                ValidatePort(s, colonIdx + 1, s.Length);
            }
            else
            {
                hostEnd = s.Length;
            }
            ValidateHost(s, hostStart, hostEnd);
        }

        private static void ValidateUserInfo(string s, int start, int end)
        {
            // userinfo grammar permits ":" — required for
            // "user:pass@host" patterns.
            for (int i = start; i < end; i++)
            {
                var c = s[i];
                if (IsUnreservedOrPct(c) || IsSubDelim(c) || c == ':')
                    continue;
                throw new System.ArgumentException(
                    $"authority userinfo: invalid char " +
                    $"'\\u{(int)c:X4}'.");
            }
        }

        private static void ValidateHost(string s, int start, int end)
        {
            // host cannot be empty.
            if (start >= end)
                throw new System.ArgumentException(
                    "authority host cannot be empty.");
            // We only support reg-name (no IP-literal here).
            // IPv4 + reg-name share the same char class —
            // unreserved + pct-encoded + sub-delims (NOT ":" or
            // "@" or "[" / "]").
            for (int i = start; i < end; i++)
            {
                var c = s[i];
                if (IsUnreservedOrPct(c) || IsSubDelim(c))
                    continue;
                throw new System.ArgumentException(
                    $"authority host: invalid char " +
                    $"'\\u{(int)c:X4}'.");
            }
        }

        private static void ValidatePort(string s, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (s[i] < '0' || s[i] > '9')
                    throw new System.ArgumentException(
                        $"authority port: non-digit char " +
                        $"'\\u{(int)s[i]:X4}'.");
            }
        }

        private static bool IsUnreservedOrPct(char c)
        {
            if ((c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')) return true;
            return c == '-' || c == '.' || c == '_' || c == '~'
                || c == '%';
        }

        private static bool IsSubDelim(char c) =>
            "!$&'()*+,;=".IndexOf(c) >= 0;

        public IRequestOptions? GetOptions() => _options;

        public IFields GetHeaders() => _headers;

        public Stream? Body { get; }
        public IFields? Trailers { get; }
    }
}
