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
        // wasi:filesystem/preopens@0.2.8
        //   get-directories: func()
        //     -> list<tuple<own<descriptor>, string>>
        //
        // Wire form: (param i32 retArea) → void.
        // retArea = 8 bytes (list-ptr i32, list-len i32).
        // Each list element = 12 bytes:
        //   +0: descriptor handle (i32)
        //   +4: string ptr (i32)
        //   +8: string len (i32)
        private static void BindPreopens(WasmRuntime runtime,
            ResourceContext resources, Realloc alloc, IPreopens impl)
        {
            var descriptors = resources.Table<Descriptor>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (PreopensNs, "get-directories"),
                (ctx, retArea) =>
                {
                    var entries = impl.GetDirectories()
                        ?? System.Array.Empty<(IDescriptor, string)>();
                    int count = entries.Length;
                    int arrayPtr = count == 0 ? 0
                        : alloc.Allocate(4, count * 12);
                    for (int i = 0; i < count; i++)
                    {
                        // Descriptor table is keyed on the host class
                        // type; cast the IDescriptor returned by the
                        // impl down to the concrete Descriptor for
                        // allocation. Hosts that need richer impls
                        // can subclass Descriptor.
                        int handle = descriptors.Allocate(
                            (Descriptor)entries[i].Item1);
                        var (sPtr, sLen) = MemoryWriter
                            .WriteUtf8StringAllocated(
                                ctx.Memory(), entries[i].Item2, alloc);
                        var mem = ctx.Memory();
                        MemoryWriter.WriteI32LE(
                            mem, arrayPtr + i * 12, handle);
                        MemoryWriter.WriteI32LE(
                            mem, arrayPtr + i * 12 + 4, sPtr);
                        MemoryWriter.WriteI32LE(
                            mem, arrayPtr + i * 12 + 8, sLen);
                    }
                    var memEnd = ctx.Memory();
                    MemoryWriter.WriteI32LE(memEnd, retArea, arrayPtr);
                    MemoryWriter.WriteI32LE(memEnd, retArea + 4, count);
                });
        }
    }
}
