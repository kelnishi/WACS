// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;

namespace Wacs.WASI.Preview2.Http
{
    public sealed partial class HttpTypes
    {
        // wasi:http/types.response-outparam — host receives a
        // result<own<outgoing-response>, error-code> through
        // the static set() method. v0 surfaces only the Ok side
        // through the host hook; the Err disc passes null to the
        // host (the payload is not decoded yet).
        private static void BindResponseOutparam(WasmRuntime runtime,
            ResourceContext resources)
        {
            var outparams = resources.Table<ResponseOutparam>();
            var responses = resources.Table<OutgoingResponse>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]response-outparam"),
                (_, h) => outparams.Drop(h));

            // [static]response-outparam.set(
            //   param: own<response-outparam>,
            //   response: result<own<outgoing-response>,
            //                    error-code>)
            //   -> _.
            //
            // Wire: (paramHandle, resultDisc, payloadHandle).
            // No retArea — the WIT return is bare _ (unit, no
            // result wrapper). disc==0 → resolve payloadHandle
            // through the OutgoingResponse table; disc!=0 →
            // host gets null (v0 doesn't surface the
            // error-code variant).
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (Ns, "[static]response-outparam.set"),
                (_, paramHandle, disc, payloadHandle) =>
                {
                    var inst = (ResponseOutparam)outparams.Get(paramHandle);
                    OutgoingResponse? response = disc == 0
                        ? (OutgoingResponse)responses.Get(payloadHandle)
                        : null;
                    inst.Set(response);
                });
        }
    }
}
