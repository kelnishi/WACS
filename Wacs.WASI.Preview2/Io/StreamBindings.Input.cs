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

namespace Wacs.WASI.Preview2.Io
{
    public sealed partial class StreamBindings
    {
        // wasi:io/streams@0.2.3
        //   resource input-stream {
        //     read: func(len: u64) -> result<list<u8>, stream-error>;
        //     blocking-read: func(len: u64) -> result<list<u8>, stream-error>;
        //     skip: func(len: u64) -> result<u64, stream-error>;
        //     blocking-skip: func(len: u64) -> result<u64, stream-error>;
        //     subscribe: func() -> own<pollable>;
        //   }
        private void BindInputStream(WasmRuntime runtime,
            ResourceContext resources, Realloc alloc)
        {
            var streams = resources.Table<InputStream>();
            var pollables = resources.Table<Pollable>();
            var errors = resources.Table<Error>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]input-stream"),
                (_, h) => streams.Drop(h));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (Ns, "[method]input-stream.read"),
                (ctx, handle, len, retArea) =>
                    WriteResultByteList(ctx, errors, alloc, retArea,
                        ((InputStream)streams.Get(handle))
                            .Read((ulong)len)));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (Ns, "[method]input-stream.blocking-read"),
                (ctx, handle, len, retArea) =>
                    WriteResultByteList(ctx, errors, alloc, retArea,
                        ((InputStream)streams.Get(handle))
                            .BlockingRead((ulong)len)));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (Ns, "[method]input-stream.skip"),
                (ctx, handle, len, retArea) =>
                    WriteResultU64(ctx, errors, retArea,
                        ((InputStream)streams.Get(handle))
                            .Skip((ulong)len)));

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (Ns, "[method]input-stream.blocking-skip"),
                (ctx, handle, len, retArea) =>
                    WriteResultU64(ctx, errors, retArea,
                        ((InputStream)streams.Get(handle))
                            .BlockingSkip((ulong)len)));

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]input-stream.subscribe"),
                (_, handle) =>
                {
                    var p = ((InputStream)streams.Get(handle))
                        .Subscribe();
                    return pollables.Allocate((Pollable)p);
                });
        }
    }
}
