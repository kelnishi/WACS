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

namespace Wacs.WASI.Preview2.Io
{
    /// <summary>
    /// Orchestrator for <c>wasi:io/streams@0.2.3</c> — both
    /// the <see cref="InputStream"/> and
    /// <see cref="OutputStream"/> resources, plus their
    /// (resource-drop) destructors.
    ///
    /// <para>Host methods return
    /// <see cref="Result{TOk,TErr}"/> over
    /// <see cref="StreamError"/>. The bindings encode both
    /// sides faithfully: Ok writes the payload,
    /// Err writes disc=1 + the
    /// <c>last-operation-failed(own&lt;error&gt;)</c> /
    /// <c>closed</c> variant slot.</para>
    /// </summary>
    public sealed partial class StreamBindings : IBindable
    {
        private const string Ns = "wasi:io/streams@0.2.3";
        private readonly ResourceContext _resources;

        public StreamBindings(ResourceContext resources)
        {
            _resources = resources
                ?? throw new ArgumentNullException(nameof(resources));
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            var alloc = new Realloc(runtime);
            BindInputStream(runtime, _resources, alloc);
            BindOutputStream(runtime, _resources, alloc);
        }

        // ---------- result<list<u8>, stream-error> --------------
        // retArea = 12 bytes, align 4.
        //   +0: outer disc (0=Ok, 1=Err) + 3 pad
        //   +4: Ok: list ptr  | Err: variant disc + 3 pad
        //   +8: Ok: list len  | Err: own<error> handle (or 0 for closed)
        private void WriteResultByteList(ExecContext ctx,
            ResourceTable errors, Realloc alloc, int retArea,
            Result<byte[], StreamError> r)
        {
            if (r.IsOk)
            {
                var data = r.Ok;
                int ptr = data.Length == 0 ? 0
                    : alloc.Allocate(1, data.Length);
                var mem = ctx.Memory();
                mem[retArea] = 0;
                mem[retArea + 1] = 0;
                mem[retArea + 2] = 0;
                mem[retArea + 3] = 0;
                if (data.Length > 0)
                    Array.Copy(data, 0, mem, ptr, data.Length);
                MemoryWriter.WriteI32LE(mem, retArea + 4, ptr);
                MemoryWriter.WriteI32LE(mem, retArea + 8, data.Length);
                return;
            }
            WriteStreamErrorVariant(ctx, errors, retArea, r.Err,
                payloadOffset: 4, handleOffset: 8);
        }

        // ---------- result<u64, stream-error> --------------------
        // retArea = 16 bytes, align 8.
        //   +0:  outer disc + 7 pad
        //   +8:  Ok: u64 value | Err: variant disc + 3 pad
        //   +12: Err: own<error> handle (or 0 for closed)
        private void WriteResultU64(ExecContext ctx,
            ResourceTable errors, int retArea,
            Result<ulong, StreamError> r)
        {
            var mem = ctx.Memory();
            if (r.IsOk)
            {
                mem[retArea] = 0;
                for (int i = 1; i < 8; i++) mem[retArea + i] = 0;
                MemoryWriter.WriteU64LE(mem, retArea + 8, r.Ok);
                return;
            }
            WriteStreamErrorVariant(ctx, errors, retArea, r.Err,
                payloadOffset: 8, handleOffset: 12);
        }

        // ---------- result<_, stream-error> ----------------------
        // retArea = 12 bytes, align 4. Ok side: disc=0, payload area
        // zeroed. Err side: same as result<list<u8>, stream-error>.
        private void WriteResultUnit(ExecContext ctx,
            ResourceTable errors, int retArea,
            Result<Unit, StreamError> r)
        {
            var mem = ctx.Memory();
            if (r.IsOk)
            {
                mem[retArea] = 0;
                for (int i = 1; i < 12; i++) mem[retArea + i] = 0;
                return;
            }
            WriteStreamErrorVariant(ctx, errors, retArea, r.Err,
                payloadOffset: 4, handleOffset: 8);
        }

        // Encode the Err side: outer disc=1, then the inner
        // stream-error variant at <payloadOffset> as a u8 disc
        // (0=last-operation-failed, 1=closed) followed by the
        // own<error> handle for the failure case (zero for closed).
        private static void WriteStreamErrorVariant(ExecContext ctx,
            ResourceTable errors, int retArea, StreamError err,
            int payloadOffset, int handleOffset)
        {
            var mem = ctx.Memory();
            // Outer Err disc + leading padding to payloadOffset.
            mem[retArea] = 1;
            for (int i = 1; i < payloadOffset; i++)
                mem[retArea + i] = 0;
            if (err is StreamError.StreamErrorLastOperationFailed lof)
            {
                mem[retArea + payloadOffset] = 0;   // case disc
                for (int i = payloadOffset + 1;
                     i < handleOffset; i++)
                    mem[retArea + i] = 0;
                int handle = errors.Allocate(lof.Value);
                MemoryWriter.WriteI32LE(mem,
                    retArea + handleOffset, handle);
            }
            else  // StreamErrorClosed
            {
                mem[retArea + payloadOffset] = 1;   // case disc
                for (int i = payloadOffset + 1;
                     i < handleOffset + 4; i++)
                    mem[retArea + i] = 0;
            }
        }
    }
}
