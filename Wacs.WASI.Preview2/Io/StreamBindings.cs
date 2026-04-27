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
    /// <summary>
    /// Orchestrator for <c>wasi:io/streams@0.2.3</c> — both
    /// the <see cref="InputStream"/> and
    /// <see cref="OutputStream"/> resources, plus their
    /// (resource-drop) destructors.
    ///
    /// <para>v0 ships always-Ok semantics: every method
    /// writes <c>result&lt;X, stream-error&gt;</c> Ok-side
    /// (disc=0 + payload). Surfacing
    /// <c>last-operation-failed(own&lt;error&gt;)</c> /
    /// <c>closed</c> needs the variant Err writer + Error
    /// allocation pathway, which can use the same
    /// <see cref="Realloc"/> + <see cref="Error"/> table the
    /// rest of the package consumes.</para>
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

        // result<list<u8>, stream-error> retArea = 12 bytes,
        // align 4. Ok side: disc=0, ptr@+4, len@+8.
        private static void WriteOkByteList(ExecContext ctx,
            Realloc alloc, int retArea, byte[] data)
        {
            var mem = ctx.Memory();
            mem[retArea] = 0;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            int ptr = data.Length == 0 ? 0
                : alloc.Allocate(1, data.Length);
            if (data.Length > 0)
                Array.Copy(data, 0, mem, ptr, data.Length);
            MemoryWriter.WriteI32LE(mem, retArea + 4, ptr);
            MemoryWriter.WriteI32LE(mem, retArea + 8, data.Length);
        }

        // result<u64, stream-error> retArea = 16 bytes,
        // align 8. Ok side: disc=0, value@+8.
        private static void WriteOkU64(ExecContext ctx,
            int retArea, ulong value)
        {
            var mem = ctx.Memory();
            mem[retArea] = 0;
            for (int i = 1; i < 8; i++) mem[retArea + i] = 0;
            MemoryWriter.WriteU64LE(mem, retArea + 8, value);
        }

        // result<_, stream-error> retArea = 12 bytes, align 4.
        // Ok side: disc=0; rest stays zero from cabi_realloc.
        private static void WriteOkUnit(ExecContext ctx,
            int retArea)
        {
            var mem = ctx.Memory();
            mem[retArea] = 0;
            for (int i = 1; i < 12; i++) mem[retArea + i] = 0;
        }
    }
}
