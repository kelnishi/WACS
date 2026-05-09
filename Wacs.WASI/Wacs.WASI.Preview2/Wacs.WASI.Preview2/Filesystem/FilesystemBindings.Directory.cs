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

namespace Wacs.WASI.Preview2.Filesystem
{
    public sealed partial class FilesystemBindings
    {
        // wasi:filesystem/types@0.2.8 — directory-entry-stream
        // resource. Single method:
        //   read-directory-entry: func()
        //     -> result<option<directory-entry>, error-code>
        //
        // retArea layout (20 bytes, align 4):
        //   +0:  outer result disc (u8) + 3 pad
        //   +4:  Ok side: inner option disc (u8) + 3 pad
        //        Err side: error-code (u8) at +1, rest zeroed
        //   +8:  type (u8) + 3 pad   (Ok-Some only)
        //   +12: name string ptr (i32)
        //   +16: name string len (i32)
        // option<record> with record-align=4: 4 (disc + pad) + 12
        //   (record) = 16. result wrapper: 4 (outer disc + pad) +
        //   max(16, 1) = 20.
        private static void BindDirectoryEntryStream(WasmRuntime runtime,
            ResourceContext resources, Realloc alloc)
        {
            var dirs = resources.Table<DirectoryEntryStream>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]directory-entry-stream"),
                (_, h) => dirs.Drop(h));

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]directory-entry-stream.read-directory-entry"),
                (ctx, handle, retArea) =>
                {
                    var d = (IDirectoryEntryStream)dirs.Get(handle);
                    var r = d.ReadDirectoryEntry();
                    var mem = ctx.Memory();
                    if (r.IsErr)
                    {
                        // Outer Err disc + ErrorCode at +1, rest zero.
                        WriteErrCode(mem, retArea, 20, r.Err);
                        return;
                    }
                    // Outer Ok disc.
                    mem.AsSpan(retArea, 1)[0] = 0;
                    mem.AsSpan(retArea + 1, 1)[0] = 0;
                    mem.AsSpan(retArea + 2, 1)[0] = 0;
                    mem.AsSpan(retArea + 3, 1)[0] = 0;
                    if (!r.Ok.HasValue)
                    {
                        // option None — disc=0, rest zero.
                        for (int i = 4; i < 20; i++) mem.AsSpan(retArea + i, 1)[0] = 0;
                        return;
                    }
                    var entry = r.Ok.Value;
                    // option Some at offset 4.
                    mem.AsSpan(retArea + 4, 1)[0] = 1;
                    mem.AsSpan(retArea + 5, 1)[0] = 0;
                    mem.AsSpan(retArea + 6, 1)[0] = 0;
                    mem.AsSpan(retArea + 7, 1)[0] = 0;
                    mem.AsSpan(retArea + 8, 1)[0] = (byte)entry.Type;
                    mem.AsSpan(retArea + 9, 1)[0] = 0;
                    mem.AsSpan(retArea + 10, 1)[0] = 0;
                    mem.AsSpan(retArea + 11, 1)[0] = 0;
                    // alloc may invalidate `mem` if the guest's
                    // cabi_realloc grows memory; re-fetch.
                    var (ptr, len) = MemoryWriter.WriteUtf8StringAllocated(
                        ctx.Memory(), entry.Name, alloc);
                    mem = ctx.Memory();
                    MemoryWriter.WriteI32LE(mem, retArea + 12, ptr);
                    MemoryWriter.WriteI32LE(mem, retArea + 16, len);
                });
        }
    }
}
