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
        // wasi:http/types.outgoing-response — response under
        // construction on the server side. Mutable status-code
        // setter + body taking; headers borrowable.
        private static void BindOutgoingResponse(WasmRuntime runtime,
            ResourceContext resources)
        {
            var responses = resources.Table<OutgoingResponse>();
            var fields = resources.Table<Fields>();
            var bodies = resources.Table<OutgoingBody>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]outgoing-response"),
                (_, h) => responses.Drop(h));

            // [constructor]outgoing-response(headers: own<headers>)
            //   -> own<outgoing-response>.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[constructor]outgoing-response"),
                (_, hHeaders) =>
                {
                    var headers = (Fields)fields.Get(hHeaders);
                    return responses.Allocate(OutgoingResponse.New(headers));
                });

            // [method]outgoing-response.status-code() -> status-code.
            // status-code is u16; bare return (no result wrap).
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]outgoing-response.status-code"),
                (_, handle) =>
                    ((OutgoingResponse)responses.Get(handle)).StatusCode());

            // [method]outgoing-response.set-status-code(
            //   status-code: status-code) -> result<_, _>.
            // Wire: (handle, value, retArea) → void.
            // status-code is u16; widens to i32 on the wire.
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (Ns, "[method]outgoing-response.set-status-code"),
                (ctx, handle, value, retArea) =>
                {
                    ((OutgoingResponse)responses.Get(handle))
                        .SetStatusCode((ushort)value);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            // [method]outgoing-response.headers() -> own<headers>.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]outgoing-response.headers"),
                (_, handle) =>
                {
                    var h = ((OutgoingResponse)responses.Get(handle))
                        .Headers();
                    return fields.Allocate(h);
                });

            // [method]outgoing-response.body()
            //   -> result<own<outgoing-body>, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]outgoing-response.body"),
                (ctx, handle, retArea) =>
                {
                    var body = ((OutgoingResponse)responses.Get(handle))
                        .Body();
                    WriteOkHandle(ctx.Memory(), retArea,
                        bodies.Allocate(body));
                });
        }

        // wasi:http/types.incoming-response — server-sent
        // response received on the client side. Read-only;
        // body taken once via consume().
        private static void BindIncomingResponse(WasmRuntime runtime,
            ResourceContext resources)
        {
            var responses = resources.Table<IncomingResponse>();
            var fields = resources.Table<Fields>();
            var bodies = resources.Table<IncomingBody>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]incoming-response"),
                (_, h) => responses.Drop(h));

            // [method]incoming-response.status() -> status-code.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]incoming-response.status"),
                (_, handle) =>
                    ((IncomingResponse)responses.Get(handle)).Status());

            // [method]incoming-response.headers() -> own<headers>.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]incoming-response.headers"),
                (_, handle) =>
                {
                    var h = ((IncomingResponse)responses.Get(handle))
                        .Headers();
                    return fields.Allocate(h);
                });

            // [method]incoming-response.consume()
            //   -> result<own<incoming-body>, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-response.consume"),
                (ctx, handle, retArea) =>
                {
                    var body = ((IncomingResponse)responses.Get(handle))
                        .Consume();
                    WriteOkHandle(ctx.Memory(), retArea,
                        bodies.Allocate(body));
                });
        }
    }
}
