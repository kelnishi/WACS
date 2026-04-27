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
        //   resource output-stream {
        //     check-write: func() -> result<u64, stream-error>;
        //     write: func(contents: list<u8>) -> result<_, stream-error>;
        //     blocking-write-and-flush: ... same shape ...
        //     flush: func() -> result<_, stream-error>;
        //     blocking-flush: ... same ...
        //     write-zeroes: func(len: u64) -> result<_, stream-error>;
        //     blocking-write-zeroes-and-flush: ... same ...
        //     splice: func(src: borrow<input-stream>, len: u64)
        //         -> result<u64, stream-error>;
        //     blocking-splice: ... same ...
        //     subscribe: func() -> own<pollable>;
        //   }
        private static void BindOutputStream(WasmRuntime runtime,
            ResourceContext resources, Realloc alloc)
        {
            var streams = resources.Table<OutputStream>();
            var inputs = resources.Table<InputStream>();
            var pollables = resources.Table<Pollable>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]output-stream"),
                (_, h) => streams.Drop(h));

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]output-stream.check-write"),
                (ctx, handle, retArea) =>
                    WriteOkU64(ctx, retArea,
                        ((OutputStream)streams.Get(handle)).CheckWrite()));

            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "[method]output-stream.write"),
                (ctx, handle, ptr, len, retArea) =>
                {
                    var bytes = ctx.ReadByteArray(ptr, len);
                    ((OutputStream)streams.Get(handle)).Write(bytes);
                    WriteOkUnit(ctx, retArea);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "[method]output-stream.blocking-write-and-flush"),
                (ctx, handle, ptr, len, retArea) =>
                {
                    var bytes = ctx.ReadByteArray(ptr, len);
                    ((OutputStream)streams.Get(handle))
                        .BlockingWriteAndFlush(bytes);
                    WriteOkUnit(ctx, retArea);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]output-stream.flush"),
                (ctx, handle, retArea) =>
                {
                    ((OutputStream)streams.Get(handle)).Flush();
                    WriteOkUnit(ctx, retArea);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]output-stream.blocking-flush"),
                (ctx, handle, retArea) =>
                {
                    ((OutputStream)streams.Get(handle)).BlockingFlush();
                    WriteOkUnit(ctx, retArea);
                });

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (Ns, "[method]output-stream.write-zeroes"),
                (ctx, handle, len, retArea) =>
                {
                    ((OutputStream)streams.Get(handle))
                        .WriteZeroes((ulong)len);
                    WriteOkUnit(ctx, retArea);
                });

            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (Ns, "[method]output-stream.blocking-write-zeroes-and-flush"),
                (ctx, handle, len, retArea) =>
                {
                    ((OutputStream)streams.Get(handle))
                        .BlockingWriteZeroesAndFlush((ulong)len);
                    WriteOkUnit(ctx, retArea);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int, long, int>>(
                (Ns, "[method]output-stream.splice"),
                (ctx, handle, srcHandle, len, retArea) =>
                {
                    var src = (InputStream)inputs.Get(srcHandle);
                    var moved = ((OutputStream)streams.Get(handle))
                        .Splice(src, (ulong)len);
                    WriteOkU64(ctx, retArea, moved);
                });

            runtime.BindHostFunction<Action<ExecContext, int, int, long, int>>(
                (Ns, "[method]output-stream.blocking-splice"),
                (ctx, handle, srcHandle, len, retArea) =>
                {
                    var src = (InputStream)inputs.Get(srcHandle);
                    var moved = ((OutputStream)streams.Get(handle))
                        .BlockingSplice(src, (ulong)len);
                    WriteOkU64(ctx, retArea, moved);
                });

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]output-stream.subscribe"),
                (_, handle) => pollables.Allocate(
                    ((OutputStream)streams.Get(handle)).Subscribe()));
        }
    }
}
