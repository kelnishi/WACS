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
        // wasi:http/types.outgoing-request — request being
        // constructed for outbound dispatch. Constructor takes
        // an own<headers> handle; method/scheme/authority/path
        // mutate state via setter methods.
        private static void BindOutgoingRequest(WasmRuntime runtime,
            ResourceContext resources, Realloc alloc)
        {
            var requests = resources.Table<OutgoingRequest>();
            var fields = resources.Table<Fields>();
            var bodies = resources.Table<OutgoingBody>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]outgoing-request"),
                (_, h) => requests.Drop(h));

            // [constructor]outgoing-request(headers: own<headers>)
            //   -> own<outgoing-request>.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[constructor]outgoing-request"),
                (_, hHeaders) =>
                {
                    var headers = (Fields)fields.Get(hHeaders);
                    return requests.Allocate(OutgoingRequest.New(headers));
                });

            // [method]outgoing-request.method() -> method.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]outgoing-request.method"),
                (ctx, handle, retArea) =>
                {
                    var m = ((OutgoingRequest)requests.Get(handle)).Method();
                    WriteHttpMethod(ctx, retArea, m, alloc);
                });

            // [method]outgoing-request.set-method(method: method)
            //   -> result<_, _>. Wire: returns the disc directly
            //   (i32) since result<_, _> is flat-lifted.
            runtime.BindHostFunction<Func<ExecContext, int, int, int, int, int>>(
                (Ns, "[method]outgoing-request.set-method"),
                (ctx, handle, disc, ptr, len) =>
                {
                    var mem = ctx.Memory();
                    var method = DecodeHttpMethodFlat(mem, disc, ptr, len);
                    var r = ((OutgoingRequest)requests.Get(handle))
                        .SetMethod(method);
                    return r.IsOk ? 0 : 1;
                });

            // [method]outgoing-request.scheme() -> option<scheme>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]outgoing-request.scheme"),
                (ctx, handle, retArea) =>
                {
                    var s = ((OutgoingRequest)requests.Get(handle)).Scheme();
                    WriteOptionHttpScheme(ctx, retArea, s, alloc);
                });

            // [method]outgoing-request.set-scheme(
            //   scheme: option<scheme>) -> result<_, _>.
            runtime.BindHostFunction<Func<ExecContext, int, int, int,
                int, int, int>>(
                (Ns, "[method]outgoing-request.set-scheme"),
                (ctx, handle, optDisc, varDisc, ptr, len) =>
                {
                    Option<Scheme> scheme;
                    if (optDisc != 0)
                    {
                        var mem = ctx.Memory();
                        scheme = Option<Scheme>.Some(
                            DecodeHttpSchemeFlat(mem, varDisc, ptr, len));
                    }
                    else
                    {
                        scheme = Option<Scheme>.None;
                    }
                    var r = ((OutgoingRequest)requests.Get(handle))
                        .SetScheme(scheme);
                    return r.IsOk ? 0 : 1;
                });

            // [method]outgoing-request.authority() -> option<string>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]outgoing-request.authority"),
                (ctx, handle, retArea) =>
                {
                    var s = ((OutgoingRequest)requests.Get(handle)).Authority();
                    WriteOptionString(ctx, retArea, s, alloc);
                });

            // [method]outgoing-request.set-authority(
            //   authority: option<string>) -> result<_, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int, int>>(
                (Ns, "[method]outgoing-request.set-authority"),
                (ctx, handle, optDisc, ptr, len, retArea) =>
                {
                    Option<string> value = optDisc == 0
                        ? Option<string>.None
                        : Option<string>.Some(ctx.ReadUtf8String(ptr, len));
                    var r = ((OutgoingRequest)requests.Get(handle))
                        .SetAuthority(value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            // [method]outgoing-request.path-with-query()
            //   -> option<string>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]outgoing-request.path-with-query"),
                (ctx, handle, retArea) =>
                {
                    var s = ((OutgoingRequest)requests.Get(handle))
                        .PathWithQuery();
                    WriteOptionString(ctx, retArea, s, alloc);
                });

            // [method]outgoing-request.set-path-with-query(
            //   path-with-query: option<string>) -> result<_, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int, int>>(
                (Ns, "[method]outgoing-request.set-path-with-query"),
                (ctx, handle, optDisc, ptr, len, retArea) =>
                {
                    Option<string> value = optDisc == 0
                        ? Option<string>.None
                        : Option<string>.Some(ctx.ReadUtf8String(ptr, len));
                    var r = ((OutgoingRequest)requests.Get(handle))
                        .SetPathWithQuery(value);
                    WriteResultUnit(ctx.Memory(), retArea, r);
                });

            // [method]outgoing-request.headers() -> own<headers>.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]outgoing-request.headers"),
                (_, handle) =>
                {
                    var h = (Fields)((OutgoingRequest)requests.Get(handle))
                        .Headers();
                    return fields.Allocate(h);
                });

            // [method]outgoing-request.body()
            //   -> result<own<outgoing-body>, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]outgoing-request.body"),
                (ctx, handle, retArea) =>
                {
                    var inst = (OutgoingRequest)requests.Get(handle);
                    var r = inst.Body();
                    if (r.IsOk)
                    {
                        // Downcast IOutgoingBody → concrete
                        // OutgoingBody for resource-table allocate.
                        // The concrete factory is BodyConcrete()
                        // — call it directly to avoid double-cast
                        // through the interface result.
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

        // wasi:http/types.incoming-request — request being
        // processed on the server side. Read-only-ish (no
        // setters); body is taken once via consume().
        private static void BindIncomingRequest(WasmRuntime runtime,
            ResourceContext resources, Realloc alloc)
        {
            var requests = resources.Table<IncomingRequest>();
            var fields = resources.Table<Fields>();
            var bodies = resources.Table<IncomingBody>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]incoming-request"),
                (_, h) => requests.Drop(h));

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-request.method"),
                (ctx, handle, retArea) =>
                {
                    var m = ((IncomingRequest)requests.Get(handle)).Method();
                    WriteHttpMethod(ctx, retArea, m, alloc);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-request.scheme"),
                (ctx, handle, retArea) =>
                {
                    var s = ((IncomingRequest)requests.Get(handle)).Scheme();
                    WriteOptionHttpScheme(ctx, retArea, s, alloc);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-request.authority"),
                (ctx, handle, retArea) =>
                {
                    var s = ((IncomingRequest)requests.Get(handle)).Authority();
                    WriteOptionString(ctx, retArea, s, alloc);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-request.path-with-query"),
                (ctx, handle, retArea) =>
                {
                    var s = ((IncomingRequest)requests.Get(handle))
                        .PathWithQuery();
                    WriteOptionString(ctx, retArea, s, alloc);
                });

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]incoming-request.headers"),
                (_, handle) =>
                {
                    var h = (Fields)((IncomingRequest)requests.Get(handle))
                        .Headers();
                    return fields.Allocate(h);
                });

            // [method]incoming-request.consume()
            //   -> result<own<incoming-body>, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-request.consume"),
                (ctx, handle, retArea) =>
                {
                    var inst = (IncomingRequest)requests.Get(handle);
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
