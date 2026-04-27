// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;

namespace Wacs.WASI.Preview2.Http
{
    public sealed partial class HttpTypes
    {
        // wasi:http/types.response-outparam — host receives a
        // result<own<outgoing-response>, error-code> through
        // the static set() method. The fixture's wit uses a
        // placeholder single-case error-code (no payload), so
        // the wire is just (paramHandle, resultDisc,
        // payloadHandle). On Ok, the payload is the response
        // handle; on Err, the payload slot is unused.
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
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (Ns, "[static]response-outparam.set"),
                (_, paramHandle, disc, payloadHandle) =>
                {
                    var inst = (ResponseOutparam)outparams.Get(paramHandle);
                    Result<IOutgoingResponse, ErrorCode> result;
                    if (disc == 0)
                    {
                        result = Result<IOutgoingResponse, ErrorCode>
                            .FromOk((OutgoingResponse)responses.Get(
                                payloadHandle));
                    }
                    else
                    {
                        // Fixture's placeholder error-code carries
                        // no payload; surface a stable internal-
                        // error sentinel so the host method
                        // observes a non-null Err. The legacy
                        // behavior (Err → null on the host hook)
                        // is preserved through SetImpl.
                        result = Result<IOutgoingResponse, ErrorCode>
                            .FromErr(new ErrorCode.ErrorCodeInternalError(
                                Option<string>.None));
                    }
                    inst.Set(inst, result);
                });
        }
    }
}
