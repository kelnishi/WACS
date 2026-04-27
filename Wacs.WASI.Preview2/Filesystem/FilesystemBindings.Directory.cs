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

namespace Wacs.WASI.Preview2.Filesystem
{
    public sealed partial class FilesystemBindings
    {
        // wasi:filesystem/types@0.2.3 — directory-entry-stream
        // resource. Single method:
        //   read-directory-entry: func()
        //     -> result<option<directory-entry>, error-code>
        //
        // retArea layout (20 bytes, align 4):
        //   +0:  outer result disc (u8) + 3 pad
        //   +4:  inner option disc (u8) + 3 pad
        //   +8:  type (u8) + 3 pad
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
                    var entry = ((DirectoryEntryStream)dirs.Get(handle))
                        .ReadDirectoryEntry();
                    var mem = ctx.Memory();
                    // outer result Ok disc.
                    mem[retArea] = 0;
                    mem[retArea + 1] = 0;
                    mem[retArea + 2] = 0;
                    mem[retArea + 3] = 0;
                    if (entry == null)
                    {
                        // option None — disc=0, rest zero.
                        for (int i = 4; i < 20; i++) mem[retArea + i] = 0;
                        return;
                    }
                    // option Some at offset 4.
                    mem[retArea + 4] = 1;
                    mem[retArea + 5] = 0;
                    mem[retArea + 6] = 0;
                    mem[retArea + 7] = 0;
                    mem[retArea + 8] = (byte)entry.Type;
                    mem[retArea + 9] = 0;
                    mem[retArea + 10] = 0;
                    mem[retArea + 11] = 0;
                    // alloc may invalidate `mem` if the guest's
                    // cabi_realloc grows memory; re-fetch.
                    var (ptr, len) = MemoryWriter.WriteUtf8StringAllocated(
                        ctx.Memory, entry.Name, alloc);
                    mem = ctx.Memory();
                    MemoryWriter.WriteI32LE(mem, retArea + 12, ptr);
                    MemoryWriter.WriteI32LE(mem, retArea + 16, len);
                });
        }
    }
}
