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
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Http
{
    public sealed partial class HttpTypes
    {
        // wasi:http/types.http-error-code — top-level function
        //   http-error-code: func(err: borrow<io-error>)
        //     -> option<error-code>
        //
        // retArea = 40 bytes, align 8:
        //   +0:   outer option disc (1B)
        //   +1..+7: pad
        //   +8..+39: error-code variant slot (32 bytes,
        //              ErrorCodeEncoder.Size).
        //
        // <paramref name="impl"/> may be null — without a mapper
        // the host returns Option.None, matching the spec's
        // intent that error-code classification is best-effort.
        private static void BindHttpErrorCode(WasmRuntime runtime,
            ResourceContext resources, Realloc alloc,
            IHttpErrorCodeMapper? impl)
        {
            var errors = resources.Table<Error>();

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "http-error-code"),
                (ctx, handle, retArea) =>
                {
                    var mem = ctx.Memory();
                    if (impl == null)
                    {
                        mem[retArea] = 0;       // None
                        return;
                    }
                    var err = (Error)errors.Get(handle);
                    var code = impl.HttpErrorCode(err);
                    if (code == null)
                    {
                        mem[retArea] = 0;       // None
                        return;
                    }
                    mem[retArea] = 1;           // Some
                    for (int p = 1; p < 8; p++)
                        mem[retArea + p] = 0;
                    ErrorCodeEncoder.Write(mem, retArea + 8, code,
                        alloc.Allocate);
                });
        }
    }
}
