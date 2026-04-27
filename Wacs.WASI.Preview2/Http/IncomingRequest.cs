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
    /// <summary>WIT <c>wasi:http/types.incoming-request</c>
    /// — a request being processed on the server side.
    /// Implements the generated
    /// <see cref="IIncomingRequest"/>.</summary>
    [WasiResource("incoming-request")]
    public class IncomingRequest : IIncomingRequest, IDisposable
    {
        protected Method _method = new Method.MethodGet();
        protected Scheme? _scheme;
        protected Fields _headers = new Fields();
        protected string? _pathWithQuery;
        protected string? _authority;
        protected IncomingBody? _body;

        /// <summary>HTTP request method on the server side.
        /// </summary>
        public virtual Method Method() => _method;

        /// <summary>HTTP scheme.</summary>
        public virtual Option<Scheme> Scheme()
            => _scheme == null
                ? Option<Scheme>.None
                : Option<Scheme>.Some(_scheme);

        public virtual IFields Headers() => _headers;

        public virtual Option<string> PathWithQuery()
            => _pathWithQuery == null
                ? Option<string>.None
                : Option<string>.Some(_pathWithQuery);

        public virtual Option<string> Authority()
            => _authority == null
                ? Option<string>.None
                : Option<string>.Some(_authority);

        /// <summary>Take ownership of the request body for
        /// reading. WIT
        /// <c>consume: func() -&gt;
        ///   result&lt;own&lt;incoming-body&gt;&gt;</c> —
        /// the bare <c>result</c> form, Err = Unit.</summary>
        public virtual Result<IIncomingBody, Unit> Consume()
            => Result<IIncomingBody, Unit>.FromOk(
                ConsumeConcrete());

        /// <summary>Concrete-typed factory so the binder can
        /// downcast to <see cref="IncomingBody"/> for
        /// resource-table allocation.</summary>
        public virtual IncomingBody ConsumeConcrete()
            => _body ??= new IncomingBody();

        public virtual void Dispose() { }
    }
}
