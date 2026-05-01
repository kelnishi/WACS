// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Http
{
    /// <summary>WIT
    /// <c>wasi:http/types.future-trailers</c>.</summary>
    [WasiResource("future-trailers")]
    public class FutureTrailers : IFutureTrailers, IDisposable
    {
        /// <summary>Pollable that fires when the trailers
        /// are ready (or the body has confirmed there are
        /// none).</summary>
        public virtual IPollable Subscribe() => SubscribeConcrete();

        public virtual Pollable SubscribeConcrete() => new Pollable();

        /// <summary>WIT <c>get: func()
        ///   -&gt; option&lt;result&lt;result&lt;option&lt;own&lt;trailers&gt;&gt;,
        ///                       error-code&gt;,
        ///                _&gt;&gt;</c>.
        /// <list type="bullet">
        /// <item>Outer None → still pending</item>
        /// <item>Outer Some(Ok(Ok(None))) → ready with no trailers</item>
        /// <item>Outer Some(Ok(Ok(Some(fields)))) → ready with trailers</item>
        /// <item>Outer Some(Ok(Err(error))) → ready with error</item>
        /// </list>
        /// Default impl: ready with no trailers (the most
        /// common server outcome).</summary>
        public virtual Option<Result<Result<Option<IFields>,
            ErrorCode>, Unit>> Get()
            => Option<Result<Result<Option<IFields>, ErrorCode>,
                Unit>>.Some(
                Result<Result<Option<IFields>, ErrorCode>,
                    Unit>.FromOk(
                    Result<Option<IFields>, ErrorCode>.FromOk(
                        Option<IFields>.None)));

        public virtual void Dispose() { }
    }
}
