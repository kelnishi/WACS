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
    /// <summary>WIT <c>wasi:http/types.incoming-response</c>.
    /// Holds status + headers + body of a server-sent
    /// response.</summary>
    [WasiResource("incoming-response")]
    public class IncomingResponse : IIncomingResponse, IDisposable
    {
        protected ushort _status = 200;
        protected Fields _headers = new Fields();
        protected IncomingBody? _body;

        /// <summary>HTTP status code (200, 404, etc.).</summary>
        public virtual ushort Status() => _status;

        /// <summary>Headers attached to this response.</summary>
        public virtual IFields Headers() => _headers;

        /// <summary>Take ownership of the response body for
        /// reading.</summary>
        public virtual Result<IIncomingBody, Unit> Consume()
            => Result<IIncomingBody, Unit>.FromOk(ConsumeConcrete());

        public virtual IncomingBody ConsumeConcrete()
            => _body ??= new IncomingBody();

        public virtual void Dispose() { }
    }
}
