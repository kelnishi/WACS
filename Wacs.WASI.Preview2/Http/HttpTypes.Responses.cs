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
        // wasi:http/types.outgoing-response — response under
        // construction on the server side.
        private static void BindOutgoingResponse(WasmRuntime runtime,
            ResourceContext resources)
        {
            var responses = resources.Table<OutgoingResponse>();
            var fields = resources.Table<Fields>();
            var bodies = resources.Table<OutgoingBody>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]outgoing-response"),
                (_, h) => responses.Drop(h));

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[constructor]outgoing-response"),
                (_, hHeaders) =>
                {
                    var headers = (Fields)fields.Get(hHeaders);
                    return responses.Allocate(OutgoingResponse.New(headers));
                });

            // [method]outgoing-response.status-code() -> status-code.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]outgoing-response.status-code"),
                (_, handle) =>
                    ((OutgoingResponse)responses.Get(handle)).StatusCode());

            // [method]outgoing-response.set-status-code -> result<_, _>.
            // The fixture's wit makes this return a flat i32 disc
            // OR a 1-byte retArea — looking at the existing
            // binding, it uses 1-byte retArea form.
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (Ns, "[method]outgoing-response.set-status-code"),
                (ctx, handle, value, retArea) =>
                {
                    var r = ((OutgoingResponse)responses.Get(handle))
                        .SetStatusCode((ushort)value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]outgoing-response.headers"),
                (_, handle) =>
                {
                    var h = (Fields)((OutgoingResponse)responses.Get(handle))
                        .Headers();
                    return fields.Allocate(h);
                });

            // [method]outgoing-response.body()
            //   -> result<own<outgoing-body>, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]outgoing-response.body"),
                (ctx, handle, retArea) =>
                {
                    var inst = (OutgoingResponse)responses.Get(handle);
                    var r = inst.Body();
                    if (r.IsOk)
                    {
                        var body = inst.BodyConcrete();
                        int h = bodies.Allocate(body);
                        WriteResultHandleBareErr(ctx.Memory(), retArea,
                            Result<int, Unit>.FromOk(h));
                    }
                    else
                    {
                        WriteResultHandleBareErr(ctx.Memory(), retArea,
                            Result<int, Unit>.FromErr(r.Err));
                    }
                });
        }

        // wasi:http/types.incoming-response — server-sent
        // response received on the client side.
        private static void BindIncomingResponse(WasmRuntime runtime,
            ResourceContext resources)
        {
            var responses = resources.Table<IncomingResponse>();
            var fields = resources.Table<Fields>();
            var bodies = resources.Table<IncomingBody>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]incoming-response"),
                (_, h) => responses.Drop(h));

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]incoming-response.status"),
                (_, handle) =>
                    ((IncomingResponse)responses.Get(handle)).Status());

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]incoming-response.headers"),
                (_, handle) =>
                {
                    var h = (Fields)((IncomingResponse)responses.Get(handle))
                        .Headers();
                    return fields.Allocate(h);
                });

            // [method]incoming-response.consume()
            //   -> result<own<incoming-body>, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-response.consume"),
                (ctx, handle, retArea) =>
                {
                    var inst = (IncomingResponse)responses.Get(handle);
                    var r = inst.Consume();
                    if (r.IsOk)
                    {
                        var body = inst.ConsumeConcrete();
                        int h = bodies.Allocate(body);
                        WriteResultHandleBareErr(ctx.Memory(), retArea,
                            Result<int, Unit>.FromOk(h));
                    }
                    else
                    {
                        WriteResultHandleBareErr(ctx.Memory(), retArea,
                            Result<int, Unit>.FromErr(r.Err));
                    }
                });
        }
    }
}
