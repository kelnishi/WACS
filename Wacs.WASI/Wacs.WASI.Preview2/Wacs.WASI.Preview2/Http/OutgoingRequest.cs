// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT <c>wasi:http/types.outgoing-request</c>
    /// — a request being constructed for outbound dispatch.
    /// Implements the generated
    /// <see cref="IOutgoingRequest"/>; result-returning
    /// methods return Result&lt;X, ErrorCode&gt; faithfully —
    /// v0 always Ok.</summary>
    [WasiResource("outgoing-request")]
    public class OutgoingRequest : IOutgoingRequest, IDisposable
    {
        protected Fields _headers = new Fields();
        protected string? _pathWithQuery;
        protected string? _authority;
        protected Method _method = new Method.MethodGet();
        protected Scheme? _scheme;
        protected OutgoingBody? _body;

        /// <summary>WIT <c>constructor(headers: own&lt;headers&gt;)</c>.
        /// Guest passes a Fields handle; we adopt it on the
        /// fresh request. Headers ownership transfers in.</summary>
        public static OutgoingRequest New(Fields headers)
            => new OutgoingRequest { _headers = headers };

        /// <summary>Generated-interface constructor stub —
        /// the WIT constructor is dispatched through
        /// <see cref="New"/> by the binder.</summary>
        public virtual void Create(IFields headers)
        {
            _headers = (Fields)headers;
        }

        /// <summary>HTTP request method. WIT
        /// <c>method() -&gt; method</c>.</summary>
        public virtual Method Method() => _method;

        /// <summary>WIT <c>set-method: func(method: method)
        ///   -&gt; result</c>. Always-Ok in v0.</summary>
        public virtual Result<Unit, Unit> SetMethod(Method method)
        {
            _method = method;
            return Result<Unit, Unit>.FromOk(Unit.Value);
        }

        /// <summary>WIT <c>scheme: func()
        ///   -&gt; option&lt;scheme&gt;</c>.</summary>
        public virtual Option<Scheme> Scheme()
            => _scheme == null
                ? Option<Scheme>.None
                : Option<Scheme>.Some(_scheme);

        /// <summary>WIT <c>set-scheme: func(
        ///   scheme: option&lt;scheme&gt;) -&gt; result</c>.
        /// </summary>
        public virtual Result<Unit, Unit> SetScheme(
            Option<Scheme> scheme)
        {
            _scheme = scheme.HasValue ? scheme.Value : null;
            SetSchemeImpl(_scheme);
            return Result<Unit, Unit>.FromOk(Unit.Value);
        }

        /// <summary>Override hook for tests / subclasses to
        /// observe the SetScheme call without re-implementing
        /// the Result wrapper. The interface method invokes
        /// this after caching the value.</summary>
        protected virtual void SetSchemeImpl(Scheme? scheme) { }

        /// <summary>Per WIT semantics, returns ownership of
        /// the headers Fields to the guest.</summary>
        public virtual IFields Headers() => _headers;

        /// <summary>HTTP path + optional query string.</summary>
        public virtual Option<string> PathWithQuery()
            => _pathWithQuery == null
                ? Option<string>.None
                : Option<string>.Some(_pathWithQuery);

        /// <summary>WIT <c>set-path-with-query</c>.</summary>
        public virtual Result<Unit, Unit> SetPathWithQuery(
            Option<string> pathWithQuery)
        {
            _pathWithQuery = pathWithQuery.HasValue
                ? pathWithQuery.Value : null;
            return Result<Unit, Unit>.FromOk(Unit.Value);
        }

        /// <summary>Authority (host:port) for the request.
        /// </summary>
        public virtual Option<string> Authority()
            => _authority == null
                ? Option<string>.None
                : Option<string>.Some(_authority);

        /// <summary>WIT <c>set-authority</c>.</summary>
        public virtual Result<Unit, Unit> SetAuthority(
            Option<string> authority)
        {
            _authority = authority.HasValue ? authority.Value : null;
            return Result<Unit, Unit>.FromOk(Unit.Value);
        }

        /// <summary>Take ownership of the request body for
        /// writing. Per WIT semantics the body can only be
        /// taken once; v0 default doesn't enforce that and
        /// just returns a fresh OutgoingBody every call.
        /// WIT <c>body: func()
        ///   -&gt; result&lt;own&lt;outgoing-body&gt;,
        ///                _&gt;</c>. The result Err side here
        /// has empty payload (bare <c>result</c> with default
        /// Unit error) — the body is a single-use resource
        /// and we always return Ok in v0.</summary>
        public virtual Result<IOutgoingBody, Unit> Body()
            => Result<IOutgoingBody, Unit>.FromOk(
                BodyConcrete());

        /// <summary>Concrete-typed body factory called by
        /// the binder so it can downcast to
        /// <see cref="OutgoingBody"/> for resource-table
        /// allocation. Subclasses can override to return a
        /// pre-pinned body instance.</summary>
        public virtual OutgoingBody BodyConcrete()
            => _body ??= new OutgoingBody();

        public virtual void Dispose() { }
    }
}
