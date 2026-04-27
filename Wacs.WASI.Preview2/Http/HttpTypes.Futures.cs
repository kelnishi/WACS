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
        // wasi:http/types.future-incoming-response — async
        // handle for an outbound request's response. get()
        // canon-lowers to the deeply-nested
        //   option<result<result<own<incoming-response>,
        //                 error-code>, _>>
        // retArea — 56 bytes, align 8.
        private static void BindFutureIncomingResponse(
            WasmRuntime runtime, ResourceContext resources,
            Realloc alloc)
        {
            var futures = resources.Table<FutureIncomingResponse>();
            var responses = resources.Table<IncomingResponse>();
            var pollables = resources.Table<Pollable>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]future-incoming-response"),
                (_, h) => futures.Drop(h));

            // [method]future-incoming-response.subscribe()
            //   -> own<pollable>.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]future-incoming-response.subscribe"),
                (_, handle) =>
                    pollables.Allocate(
                        ((FutureIncomingResponse)futures.Get(handle))
                            .Subscribe()));

            // [method]future-incoming-response.get()
            //   -> option<result<result<own<incoming-response>,
            //                          error-code>, _>>.
            //
            // retArea (56 bytes, align 8):
            //   +0:  outer option disc (1B)
            //   +1..+7: pad
            //   +8:  outer result disc (1B) — 0=Ok
            //   +9..+15: pad
            //   +16: inner result disc (1B) — 0=Ok
            //   +17..+23: pad
            //   +24: own<incoming-response> handle (4B) on Ok,
            //         or error-code variant slot (32B) on Err.
            //
            // States:
            //   (false, _) → outer disc=0
            //   (true, resp) → outer=1, outer-result=0,
            //     inner-result=0, handle@+24
            //   throw WasiErrorCodeException(c) → outer=1,
            //     outer-result=0, inner-result=1, error-code@+24
            //   throw WasiFutureAlreadyConsumedException → outer=1,
            //     outer-result=1, rest zeroed
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]future-incoming-response.get"),
                (ctx, handle, retArea) =>
                {
                    var inst = (FutureIncomingResponse)futures.Get(handle);
                    (bool ready, IncomingResponse? response) ret;
                    try
                    {
                        ret = inst.Get();
                    }
                    catch (WasiErrorCodeException wec)
                    {
                        var memErr = ctx.Memory();
                        memErr[retArea] = 1;        // outer Some
                        for (int p = 1; p <= 7; p++)
                            memErr[retArea + p] = 0;
                        memErr[retArea + 8] = 0;    // outer Ok
                        for (int p = 9; p <= 15; p++)
                            memErr[retArea + p] = 0;
                        memErr[retArea + 16] = 1;   // inner Err
                        for (int p = 17; p <= 23; p++)
                            memErr[retArea + p] = 0;
                        ErrorCodeEncoder.Write(memErr, retArea + 24,
                            wec.Code, alloc.Allocate);
                        return;
                    }
                    catch (WasiFutureAlreadyConsumedException)
                    {
                        var memC = ctx.Memory();
                        memC[retArea] = 1;          // outer Some
                        for (int p = 1; p <= 7; p++)
                            memC[retArea + p] = 0;
                        memC[retArea + 8] = 1;      // outer Err
                        for (int p = 9; p <= 55; p++)
                            memC[retArea + p] = 0;
                        return;
                    }
                    var mem = ctx.Memory();
                    if (!ret.ready)
                    {
                        mem[retArea] = 0;           // outer None
                        return;
                    }
                    if (ret.response == null)
                        throw new InvalidOperationException(
                            "FutureIncomingResponse.Get() returned "
                            + "(true, null) — when ready=true the "
                            + "response handle must be non-null "
                            + "(throw WasiErrorCodeException to "
                            + "surface inner Err instead).");
                    mem[retArea] = 1;               // outer Some
                    for (int p = 1; p <= 7; p++)
                        mem[retArea + p] = 0;
                    mem[retArea + 8] = 0;           // outer Ok
                    for (int p = 9; p <= 15; p++)
                        mem[retArea + p] = 0;
                    mem[retArea + 16] = 0;          // inner Ok
                    for (int p = 17; p <= 23; p++)
                        mem[retArea + p] = 0;
                    int respHandle = responses.Allocate(ret.response);
                    MemoryWriter.WriteI32LE(mem, retArea + 24, respHandle);
                    for (int p = 28; p <= 55; p++)
                        mem[retArea + p] = 0;
                });
        }

        // wasi:http/types.future-trailers — async handle for
        // the trailers of an HTTP body. get() canon-lowers to
        //   option<result<option<own<trailers>>, error-code>>
        // retArea — 48 bytes, align 8.
        private static void BindFutureTrailers(WasmRuntime runtime,
            ResourceContext resources, Realloc alloc)
        {
            var futures = resources.Table<FutureTrailers>();
            var trailers = resources.Table<Fields>();
            var pollables = resources.Table<Pollable>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]future-trailers"),
                (_, h) => futures.Drop(h));

            // [method]future-trailers.subscribe() -> own<pollable>.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]future-trailers.subscribe"),
                (_, handle) =>
                    pollables.Allocate(
                        ((FutureTrailers)futures.Get(handle))
                            .Subscribe()));

            // [method]future-trailers.get()
            //   -> option<result<option<own<trailers>>,
            //                    error-code>>.
            //
            // retArea (48 bytes, align 8):
            //   +0:  outer option disc (1B)
            //   +1..+7: pad
            //   +8:  result disc (1B)
            //   +9..+15: pad (result-payload at +16)
            //   +16: inner option disc (1B) when Ok, or
            //        error-code variant slot (32B) when Err
            //   +17..+19: pad
            //   +20: trailers handle (4B) when inner Some
            //
            // States:
            //   (false, _)         → outer=0
            //   (true, null)       → outer=1, result=0, inner=0
            //   (true, fields)     → outer=1, result=0, inner=1,
            //                          handle@+20
            //   throw WasiErrorCodeException(c) → outer=1,
            //                          result=1, error-code@+16
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]future-trailers.get"),
                (ctx, handle, retArea) =>
                {
                    var inst = (FutureTrailers)futures.Get(handle);
                    (bool ready, Fields? trailers) ret;
                    try
                    {
                        ret = inst.Get();
                    }
                    catch (WasiErrorCodeException wec)
                    {
                        var memErr = ctx.Memory();
                        memErr[retArea] = 1;        // outer Some
                        for (int p = 1; p <= 7; p++)
                            memErr[retArea + p] = 0;
                        memErr[retArea + 8] = 1;    // result Err
                        for (int p = 9; p <= 15; p++)
                            memErr[retArea + p] = 0;
                        ErrorCodeEncoder.Write(memErr, retArea + 16,
                            wec.Code, alloc.Allocate);
                        return;
                    }
                    var mem = ctx.Memory();
                    if (!ret.ready)
                    {
                        mem[retArea] = 0;           // outer None
                        return;
                    }
                    mem[retArea] = 1;               // outer Some
                    for (int p = 1; p <= 7; p++)
                        mem[retArea + p] = 0;
                    mem[retArea + 8] = 0;           // result Ok
                    for (int p = 9; p <= 15; p++)
                        mem[retArea + p] = 0;
                    if (ret.trailers == null)
                    {
                        mem[retArea + 16] = 0;      // inner None
                        for (int p = 17; p <= 19; p++)
                            mem[retArea + p] = 0;
                        MemoryWriter.WriteI32LE(mem, retArea + 20, 0);
                        for (int p = 24; p <= 47; p++)
                            mem[retArea + p] = 0;
                        return;
                    }
                    mem[retArea + 16] = 1;          // inner Some
                    for (int p = 17; p <= 19; p++)
                        mem[retArea + p] = 0;
                    int tHandle = trailers.Allocate(ret.trailers);
                    MemoryWriter.WriteI32LE(mem, retArea + 20, tHandle);
                    for (int p = 24; p <= 47; p++)
                        mem[retArea + p] = 0;
                });
        }
    }
}
