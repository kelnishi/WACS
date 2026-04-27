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
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Http
{
    public sealed partial class HttpTypes
    {
        // wasi:http/types.outgoing-body — write side of an
        // HTTP body. Take the OutputStream once via write();
        // mark complete with optional trailers via the static
        // finish().
        private static void BindOutgoingBody(WasmRuntime runtime,
            ResourceContext resources)
        {
            var bodies = resources.Table<OutgoingBody>();
            var fields = resources.Table<Fields>();
            var outputs = resources.Table<OutputStream>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]outgoing-body"),
                (_, h) => bodies.Drop(h));

            // [method]outgoing-body.write()
            //   -> result<own<output-stream>, _>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]outgoing-body.write"),
                (ctx, handle, retArea) =>
                {
                    var stream = ((OutgoingBody)bodies.Get(handle)).Write();
                    WriteOkHandle(ctx.Memory(), retArea,
                        outputs.Allocate(stream));
                });

            // [static]outgoing-body.finish(this: own<outgoing-body>,
            //   trailers: option<own<trailers>>)
            //   -> result<_, error-code>.
            // Wire: (selfHandle, optDisc, trailerHandle, retArea)
            //   → void. retArea = 1 byte (just the result disc).
            //
            // The host method is declared as an instance method
            // (taking trailers); the canon-lower wire form prefixes
            // the receiver handle. on the host
            // class only affects the import-name prefix
            // ([static] vs [method]) — the wire shape is
            // unchanged.
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "[static]outgoing-body.finish"),
                (ctx, selfHandle, optDisc, trailerHandle, retArea) =>
                {
                    Fields? trailers = optDisc == 0 ? null
                        : (Fields)fields.Get(trailerHandle);
                    var inst = (OutgoingBody)bodies.Get(selfHandle);
                    inst.Finish(trailers);
                    WriteOkUnit(ctx.Memory(), retArea);
                });
        }

        // wasi:http/types.incoming-body — read side of an
        // HTTP body. Take the InputStream once via stream();
        // surface the future-trailers via the static finish().
        private static void BindIncomingBody(WasmRuntime runtime,
            ResourceContext resources)
        {
            var bodies = resources.Table<IncomingBody>();
            var inputs = resources.Table<InputStream>();
            var futureTrailers = resources.Table<FutureTrailers>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]incoming-body"),
                (_, h) => bodies.Drop(h));

            // [method]incoming-body.stream()
            //   -> result<own<input-stream>, _>.
            // The host method is named Stream (% escapes the
            // keyword in the WIT but the wire import-name is
            // "stream").
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-body.stream"),
                (ctx, handle, retArea) =>
                {
                    var stream = ((IncomingBody)bodies.Get(handle)).Stream();
                    WriteOkHandle(ctx.Memory(), retArea,
                        inputs.Allocate(stream));
                });

            // [static]incoming-body.finish(this: own<incoming-body>)
            //   -> own<future-trailers>. Bare own return — no
            // result wrapper.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[static]incoming-body.finish"),
                (_, selfHandle) =>
                {
                    var ft = ((IncomingBody)bodies.Get(selfHandle)).Finish();
                    return futureTrailers.Allocate(ft);
                });
        }
    }
}
