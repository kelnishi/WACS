// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;

namespace Wacs.WASI.GFX.HostBinding
{
    /// <summary>
    /// Lazy <c>cabi_realloc</c> resolver. Mirror of
    /// <c>Wacs.WASI.NN.HostBinding.Realloc</c> — inlined here so
    /// this package doesn't depend on the NN tree. Component-
    /// model guests export <c>cabi_realloc</c>; the host calls
    /// it to reserve guest memory for the (ptr, len) tuples
    /// returned to wasm-side via retArea.
    /// </summary>
    internal sealed class Realloc
    {
        private readonly WasmRuntime _runtime;
        private Wacs.Core.Runtime.Delegates.GenericFuncs? _cabiRealloc;

        public Realloc(WasmRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public int Allocate(int align, int size)
        {
            if (_cabiRealloc == null)
            {
                if (!_runtime.TryGetExportedFunction("cabi_realloc", out var addr))
                    throw new InvalidOperationException(
                        "Component does not export cabi_realloc — "
                        + "required for any host method that writes "
                        + "list / aggregate payloads back to guest memory.");
                _cabiRealloc = _runtime.CreateInvoker(addr, new InvokerOptions());
            }
            return _cabiRealloc(0, 0, align, size)[0].Data.Int32;
        }
    }
}
