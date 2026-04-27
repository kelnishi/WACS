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

namespace Wacs.WASI.Preview2.Filesystem
{
    public sealed partial class FilesystemBindings
    {
        // wasi:filesystem/types@0.2.3 — top-level
        //   filesystem-error-code: func(err: borrow<error>)
        //     -> option<error-code>
        //
        // Wire form: (handle i32, retArea i32) → void.
        // retArea = 2 bytes (option disc + 1B enum payload).
        private static void BindFilesystemErrorCode(WasmRuntime runtime,
            ResourceContext resources, IFilesystemErrorCode impl)
        {
            var errors = resources.Table<Error>();

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "filesystem-error-code"),
                (ctx, handle, retArea) =>
                {
                    var err = (Error)errors.Get(handle);
                    var code = impl.FilesystemErrorCode(err);
                    var mem = ctx.Memory();
                    if (code == null)
                    {
                        mem[retArea] = 0;
                        mem[retArea + 1] = 0;
                        return;
                    }
                    mem[retArea] = 1;
                    mem[retArea + 1] = (byte)code.Value;
                });
        }
    }
}
