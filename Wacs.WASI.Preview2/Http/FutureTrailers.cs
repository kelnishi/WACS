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
    /// <c>wasi:http/types.future-trailers</c>.</summary>
    [WasiResource("future-trailers")]
    public class FutureTrailers : IDisposable
    {
        /// <summary>Pollable that fires when the trailers
        /// are ready (or the body has confirmed there are
        /// none).</summary>
        public virtual Pollable Subscribe() => new Pollable();

        /// <summary>WIT <c>get: func()
        ///   -&gt; option&lt;result&lt;option&lt;own&lt;trailers&gt;&gt;,
        ///                       error-code&gt;&gt;</c>.
        /// Three-state tuple return:
        /// <list type="bullet">
        /// <item><c>(false, _)</c> → outer None (still pending)</item>
        /// <item><c>(true, null)</c> → ready with no trailers</item>
        /// <item><c>(true, fields)</c> → ready with trailers</item>
        /// </list>
        /// Default impl: ready with no trailers (the most
        /// common server outcome). Override on a subclass to
        /// stage actual trailers or to return a
        /// not-ready state. v0 always-Ok — the Err side of
        /// the result variant isn't surfaced.</summary>
        public virtual (bool ready, Fields? trailers) Get()
            => (true, null);

        public virtual void Dispose() { }
    }
}
