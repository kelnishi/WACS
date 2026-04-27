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
            //   -> own<outgoing-request>. Wire: (handlesIn) → handle.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[constructor]outgoing-request"),
                (_, hHeaders) =>
                {
                    var headers = (Fields)fields.Get(hHeaders);
                    return requests.Allocate(OutgoingRequest.New(headers));
                });

            // [method]outgoing-request.method() -> method.
            // Wire: (handle, retArea) → void. retArea = 12 bytes
            // (variant disc + 3 pad + ptr + len).
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]outgoing-request.method"),
                (ctx, handle, retArea) =>
                {
                    var m = ((OutgoingRequest)requests.Get(handle)).Method();
                    WriteHttpMethod(ctx, retArea, m, alloc);
                });

            // [method]outgoing-request.set-method(method: method)
            //   -> result<_, _>. Wire: (handle, disc, ptr, len)
            //   → i32 (the result disc — always 0 in v0).
            runtime.BindHostFunction<Func<ExecContext, int, int, int, int, int>>(
                (Ns, "[method]outgoing-request.set-method"),
                (ctx, handle, disc, ptr, len) =>
                {
                    var mem = ctx.Memory();
                    var method = DecodeHttpMethodFlat(mem, disc, ptr, len);
                    ((OutgoingRequest)requests.Get(handle)).SetMethod(method);
                    return 0;
                });

            // [method]outgoing-request.scheme() -> option<scheme>.
            // retArea = 16 bytes (option disc + variant disc +
            // ptr + len).
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]outgoing-request.scheme"),
                (ctx, handle, retArea) =>
                {
                    var s = ((OutgoingRequest)requests.Get(handle)).Scheme();
                    WriteOptionHttpScheme(ctx, retArea, s, alloc);
                });

            // [method]outgoing-request.set-scheme(
            //   scheme: option<scheme>) -> result<_, _>.
            // Wire: (handle, optDisc, varDisc, ptr, len) → i32.
            runtime.BindHostFunction<Func<ExecContext, int, int, int,
                int, int, int>>(
                (Ns, "[method]outgoing-request.set-scheme"),
                (ctx, handle, optDisc, varDisc, ptr, len) =>
                {
                    HttpScheme? scheme = null;
                    if (optDisc != 0)
                    {
                        var mem = ctx.Memory();
                        scheme = DecodeHttpSchemeFlat(mem, varDisc, ptr, len);
                    }
                    ((OutgoingRequest)requests.Get(handle)).SetScheme(scheme);
                    return 0;
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
            // Wire: (handle, optDisc, ptr, len, retArea) → void.
            // retArea = 1 byte (just the result disc).
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int, int>>(
                (Ns, "[method]outgoing-request.set-authority"),
                (ctx, handle, optDisc, ptr, len, retArea) =>
                {
                    string? value = optDisc == 0 ? null
                        : ctx.ReadUtf8String(ptr, len);
                    ((OutgoingRequest)requests.Get(handle))
                        .SetAuthority(value);
                    WriteOkUnit(ctx.Memory(), retArea);
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
                    string? value = optDisc == 0 ? null
                        : ctx.ReadUtf8String(ptr, len);
                    ((OutgoingRequest)requests.Get(handle))
                        .SetPathWithQuery(value);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            // [method]outgoing-request.headers() -> own<headers>.
            // Bare resource return (the WIT spec says the
            // returned fields handle is "owned" — the guest
            // takes responsibility for dropping it).
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]outgoing-request.headers"),
                (_, handle) =>
                {
                    var h = ((OutgoingRequest)requests.Get(handle)).Headers();
                    return fields.Allocate(h);
                });

            // [method]outgoing-request.body()
            //   -> result<own<outgoing-body>, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]outgoing-request.body"),
                (ctx, handle, retArea) =>
                {
                    var body = ((OutgoingRequest)requests.Get(handle)).Body();
                    WriteOkHandle(ctx.Memory(), retArea,
                        bodies.Allocate(body));
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

            // [method]incoming-request.method() -> method.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-request.method"),
                (ctx, handle, retArea) =>
                {
                    var m = ((IncomingRequest)requests.Get(handle)).Method();
                    WriteHttpMethod(ctx, retArea, m, alloc);
                });

            // [method]incoming-request.scheme() -> option<scheme>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-request.scheme"),
                (ctx, handle, retArea) =>
                {
                    var s = ((IncomingRequest)requests.Get(handle)).Scheme();
                    WriteOptionHttpScheme(ctx, retArea, s, alloc);
                });

            // [method]incoming-request.authority() -> option<string>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-request.authority"),
                (ctx, handle, retArea) =>
                {
                    var s = ((IncomingRequest)requests.Get(handle)).Authority();
                    WriteOptionString(ctx, retArea, s, alloc);
                });

            // [method]incoming-request.path-with-query()
            //   -> option<string>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-request.path-with-query"),
                (ctx, handle, retArea) =>
                {
                    var s = ((IncomingRequest)requests.Get(handle))
                        .PathWithQuery();
                    WriteOptionString(ctx, retArea, s, alloc);
                });

            // [method]incoming-request.headers() -> own<headers>.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]incoming-request.headers"),
                (_, handle) =>
                {
                    var h = ((IncomingRequest)requests.Get(handle))
                        .Headers();
                    return fields.Allocate(h);
                });

            // [method]incoming-request.consume()
            //   -> result<own<incoming-body>, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-request.consume"),
                (ctx, handle, retArea) =>
                {
                    var body = ((IncomingRequest)requests.Get(handle))
                        .Consume();
                    WriteOkHandle(ctx.Memory(), retArea,
                        bodies.Allocate(body));
                });
        }
    }
}
