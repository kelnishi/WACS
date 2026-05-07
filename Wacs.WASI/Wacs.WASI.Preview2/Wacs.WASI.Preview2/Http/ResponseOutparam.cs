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
    /// <summary>WIT
    /// <c>wasi:http/types.response-outparam</c>.</summary>
    [WasiResource("response-outparam")]
    public class ResponseOutparam : IResponseOutparam, IDisposable
    {
        /// <summary>Deliver the response into the outparam.
        /// WIT <c>set: static func(param: own&lt;response-outparam&gt;,
        ///   response: result&lt;own&lt;outgoing-response&gt;,
        ///                       error-code&gt;)</c>.</summary>
        public virtual void Set(IResponseOutparam param,
            Result<IOutgoingResponse, ErrorCode> response)
        {
            // Subclasses override SetImpl to observe the
            // response in the historical OutgoingResponse?
            // shape (Ok → resolved response, Err → null).
            SetImpl(response.IsOk
                ? (OutgoingResponse?)response.Ok
                : null);
        }

        /// <summary>Override hook — receives the resolved
        /// response on Ok, null on Err. Default no-op.</summary>
        public virtual void SetImpl(OutgoingResponse? response) { }

        public virtual void Dispose() { }
    }
}
