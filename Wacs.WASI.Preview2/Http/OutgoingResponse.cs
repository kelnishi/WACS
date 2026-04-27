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
    /// <summary>WIT <c>wasi:http/types.outgoing-response</c>.
    /// Server-side response under construction.</summary>
    [WasiResource("outgoing-response")]
    public class OutgoingResponse : IDisposable
    {
        protected ushort _statusCode = 200;
        protected Fields _headers = new Fields();
        protected OutgoingBody? _body;

        /// <summary>WIT <c>constructor(headers: own&lt;headers&gt;)</c>.
        /// Guest passes a Fields handle; ownership transfers
        /// in on construction.</summary>
        public static OutgoingResponse New(Fields headers)
            => new OutgoingResponse { _headers = headers };

        public virtual ushort StatusCode() => _statusCode;

        public virtual void SetStatusCode(ushort statusCode)
            => _statusCode = statusCode;

        public virtual Fields Headers() => _headers;

        /// <summary>Take ownership of the response body for
        /// writing.</summary>
        public virtual OutgoingBody Body()
            => _body ??= new OutgoingBody();

        public virtual void Dispose() { }
    }
}
