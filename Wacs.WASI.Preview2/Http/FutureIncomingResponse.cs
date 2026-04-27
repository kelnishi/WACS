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
    /// <summary>WIT
    /// <c>wasi:http/types.future-incoming-response</c> —
    /// async handle for an outbound request's response.
    /// </summary>
    [WasiResource("future-incoming-response")]
    public class FutureIncomingResponse : IDisposable
    {
        /// <summary>Pollable that fires when the response
        /// is ready to be retrieved via get(). Default
        /// returns an always-ready pollable.</summary>
        public virtual Pollable Subscribe() => new Pollable();

        /// <summary>WIT <c>get: func() -&gt; option&lt;result&lt;
        ///   result&lt;own&lt;incoming-response&gt;,
        ///                error-code&gt;,
        ///   _&gt;&gt;</c>. Tuple return shape:
        /// <list type="bullet">
        /// <item><c>(false, _)</c> → outer None (still pending)</item>
        /// <item><c>(true, response)</c> → ready with response</item>
        /// </list>
        /// Default impl: ready with a fresh
        /// <see cref="IncomingResponse"/> stub. v0 surfaces
        /// only the always-Ok subset; inner Err (error-code)
        /// and outer Err (already-consumed) follow when the
        /// error-code variant payload encoder lands.</summary>
        [WasiFutureIncomingResponseResult]
        public virtual (bool ready, IncomingResponse? response) Get()
            => (true, new IncomingResponse());

        public virtual void Dispose() { }
    }
}
