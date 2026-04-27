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
    /// <summary>WIT
    /// <c>wasi:http/types.response-outparam</c>.</summary>
    [WasiResource("response-outparam")]
    public class ResponseOutparam : IDisposable
    {
        /// <summary>Deliver the response into the outparam.
        /// WIT <c>set: static func(param: own&lt;response-outparam&gt;,
        ///   response: result&lt;own&lt;outgoing-response&gt;,
        ///                       error-code&gt;)</c>.
        /// v0 surfaces only the Ok side: when wire disc=0 the
        /// host receives the resolved <see cref="OutgoingResponse"/>;
        /// when disc!=0 the host receives null. Payload-bearing
        /// error-code variants will follow when the binder
        /// learns to decode them.</summary>
        [WasiStaticMethod]
        public virtual void Set(
            [WasiResultParam] OutgoingResponse? response) { }

        public virtual void Dispose() { }
    }
}
