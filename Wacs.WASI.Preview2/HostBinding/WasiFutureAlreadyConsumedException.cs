// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.Preview2.HostBinding
{
    /// <summary>
    /// Host-side signal for the OUTER Err (already-consumed)
    /// state on <c>future-incoming-response.get</c>:
    /// <code>
    /// option&lt;result&lt;result&lt;own&lt;incoming-response&gt;,
    ///                       error-code&gt;,
    ///               _&gt;&gt;
    /// //          ^^^ outer Err = bare unit
    /// </code>
    /// The outer Err is the spec's signal that get() was
    /// called on a future that was already consumed by a
    /// previous get(). Its payload is bare unit — no
    /// information attached.
    ///
    /// <para>Throw from a
    /// <see cref="Wacs.WASI.Preview2.Http.FutureIncomingResponse.Get"/>
    /// override on a re-entrant call to surface the outer-
    /// Err state. The binder catches and writes outer
    /// disc=1 (Some) + outer-result disc=1 (Err) at the
    /// retArea, leaving the rest zeroed.</para>
    ///
    /// <para>Distinct from
    /// <see cref="WasiErrorCodeException"/>, which carries
    /// the inner Err (error-code variant payload). The two
    /// occupy different positions in the result-of-result
    /// structure and have different wire-encoding paths.</para>
    /// </summary>
    public sealed class WasiFutureAlreadyConsumedException
        : Exception
    {
        public WasiFutureAlreadyConsumedException()
            : base("WASI future already consumed — calling "
                  + "get() a second time on a future returns "
                  + "the outer Err side of the result-of-"
                  + "result encoding.") { }

        public WasiFutureAlreadyConsumedException(string message)
            : base(message) { }
    }
}
