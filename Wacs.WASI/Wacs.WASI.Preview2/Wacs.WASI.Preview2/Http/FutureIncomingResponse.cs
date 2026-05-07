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
    /// <c>wasi:http/types.future-incoming-response</c> —
    /// async handle for an outbound request's response.
    /// </summary>
    [WasiResource("future-incoming-response")]
    public class FutureIncomingResponse
        : IFutureIncomingResponse, IDisposable
    {
        /// <summary>Pollable that fires when the response
        /// is ready. Default returns an always-ready
        /// pollable.</summary>
        public virtual IPollable Subscribe() => SubscribeConcrete();

        public virtual Pollable SubscribeConcrete() => new Pollable();

        /// <summary>WIT <c>get: func() -&gt;
        ///   option&lt;result&lt;result&lt;own&lt;incoming-response&gt;,
        ///                          error-code&gt;,
        ///   _&gt;&gt;</c>.
        /// <list type="bullet">
        /// <item>Outer None → still pending</item>
        /// <item>Outer Some(Ok(Ok(resp))) → ready with response</item>
        /// <item>Outer Some(Ok(Err(error))) → ready with error</item>
        /// <item>Outer Some(Err(_)) → already-consumed (outer-Err
        ///   is bare unit)</item>
        /// </list>
        /// Default impl: ready with a fresh response stub
        /// (Ok / Ok). Override on a subclass to change the
        /// state — return None for not-ready, Some(Ok(Err))
        /// for inner-Err, Some(Err) for already-consumed.
        /// </summary>
        public virtual Option<Result<Result<IIncomingResponse,
            ErrorCode>, Unit>> Get()
            => Option<Result<Result<IIncomingResponse, ErrorCode>,
                Unit>>.Some(
                Result<Result<IIncomingResponse, ErrorCode>,
                    Unit>.FromOk(
                    Result<IIncomingResponse, ErrorCode>.FromOk(
                        new IncomingResponse())));

        public virtual void Dispose() { }
    }
}
