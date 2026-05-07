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
        // wasi:http/types.request-options — per-request
        // transport tunables (timeouts). All three are
        // option<duration> = option<u64> nanoseconds.
        private static void BindRequestOptions(WasmRuntime runtime,
            ResourceContext resources)
        {
            var opts = resources.Table<RequestOptions>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]request-options"),
                (_, h) => opts.Drop(h));

            runtime.BindHostFunction<Func<ExecContext, int>>(
                (Ns, "[constructor]request-options"),
                _ => opts.Allocate(RequestOptions.New()));

            // [method]request-options.connect-timeout()
            //   -> option<duration>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]request-options.connect-timeout"),
                (ctx, handle, retArea) =>
                {
                    var v = ((RequestOptions)opts.Get(handle))
                        .ConnectTimeout();
                    WriteOptionU64(ctx.Memory(), retArea, v);
                });

            // [method]request-options.set-connect-timeout(
            //   duration: option<duration>) -> result<_, _>.
            // result<_, _> lowers to a single i32 disc, returned
            // flat. Wire: (handle, optDisc, value) → i32.
            runtime.BindHostFunction<Func<ExecContext, int, int, long, int>>(
                (Ns, "[method]request-options.set-connect-timeout"),
                (_, handle, optDisc, value) =>
                {
                    Option<ulong> v = optDisc == 0
                        ? Option<ulong>.None
                        : Option<ulong>.Some((ulong)value);
                    var r = ((RequestOptions)opts.Get(handle))
                        .SetConnectTimeout(v);
                    return r.IsOk ? 0 : 1;
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]request-options.first-byte-timeout"),
                (ctx, handle, retArea) =>
                {
                    var v = ((RequestOptions)opts.Get(handle))
                        .FirstByteTimeout();
                    WriteOptionU64(ctx.Memory(), retArea, v);
                });

            runtime.BindHostFunction<Func<ExecContext, int, int, long, int>>(
                (Ns, "[method]request-options.set-first-byte-timeout"),
                (_, handle, optDisc, value) =>
                {
                    Option<ulong> v = optDisc == 0
                        ? Option<ulong>.None
                        : Option<ulong>.Some((ulong)value);
                    var r = ((RequestOptions)opts.Get(handle))
                        .SetFirstByteTimeout(v);
                    return r.IsOk ? 0 : 1;
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]request-options.between-bytes-timeout"),
                (ctx, handle, retArea) =>
                {
                    var v = ((RequestOptions)opts.Get(handle))
                        .BetweenBytesTimeout();
                    WriteOptionU64(ctx.Memory(), retArea, v);
                });

            runtime.BindHostFunction<Func<ExecContext, int, int, long, int>>(
                (Ns, "[method]request-options.set-between-bytes-timeout"),
                (_, handle, optDisc, value) =>
                {
                    Option<ulong> v = optDisc == 0
                        ? Option<ulong>.None
                        : Option<ulong>.Some((ulong)value);
                    var r = ((RequestOptions)opts.Get(handle))
                        .SetBetweenBytesTimeout(v);
                    return r.IsOk ? 0 : 1;
                });
        }
    }
}
