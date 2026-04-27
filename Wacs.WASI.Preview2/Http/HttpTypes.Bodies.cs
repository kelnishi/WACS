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
        // wasi:http/types.outgoing-body — write side of an
        // HTTP body.
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
                    var inst = (OutgoingBody)bodies.Get(handle);
                    var r = inst.Write();
                    if (r.IsOk)
                    {
                        var stream = inst.WriteConcrete();
                        int h = outputs.Allocate(stream);
                        WriteResultHandleBareErr(ctx.Memory(), retArea,
                            Result<int, Unit>.FromOk(h));
                    }
                    else
                    {
                        WriteResultHandleBareErr(ctx.Memory(), retArea,
                            Result<int, Unit>.FromErr(r.Err));
                    }
                });

            // [static]outgoing-body.finish(this: own<outgoing-body>,
            //   trailers: option<own<trailers>>)
            //   -> result<_, error-code>.
            // The fixture's wit uses a placeholder single-case
            // error-code, so retArea = 1 byte (just the outer
            // disc).
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "[static]outgoing-body.finish"),
                (ctx, selfHandle, optDisc, trailerHandle, retArea) =>
                {
                    Option<IFields> trailers = optDisc == 0
                        ? Option<IFields>.None
                        : Option<IFields>.Some((Fields)fields.Get(trailerHandle));
                    var inst = (OutgoingBody)bodies.Get(selfHandle);
                    var r = inst.Finish(inst, trailers);
                    WriteResultUnitPlaceholder(ctx.Memory(), retArea, r);
                });
        }

        // wasi:http/types.incoming-body — read side of an
        // HTTP body.
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
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]incoming-body.stream"),
                (ctx, handle, retArea) =>
                {
                    var inst = (IncomingBody)bodies.Get(handle);
                    var r = inst.Stream();
                    if (r.IsOk)
                    {
                        var stream = inst.StreamConcrete();
                        int h = inputs.Allocate(stream);
                        WriteResultHandleBareErr(ctx.Memory(), retArea,
                            Result<int, Unit>.FromOk(h));
                    }
                    else
                    {
                        WriteResultHandleBareErr(ctx.Memory(), retArea,
                            Result<int, Unit>.FromErr(r.Err));
                    }
                });

            // [static]incoming-body.finish(this: own<incoming-body>)
            //   -> own<future-trailers>. Bare own return.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[static]incoming-body.finish"),
                (_, selfHandle) =>
                {
                    var inst = (IncomingBody)bodies.Get(selfHandle);
                    var ft = inst.FinishConcrete();
                    return futureTrailers.Allocate(ft);
                });
        }
    }
}
