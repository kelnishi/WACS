// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;

namespace Wacs.WASI.Preview2.HostBinding.CanonicalAbi
{
    /// <summary>
    /// Lazy <c>cabi_realloc</c> resolver. Components that emit
    /// any aggregate-return method import <c>cabi_realloc</c>
    /// from the guest; the host calls into it to reserve guest
    /// memory for strings, lists, and outer retArea records
    /// before writing the canon-lowered bytes.
    ///
    /// <para>Constructed with a <see cref="WasmRuntime"/>; the
    /// resolver caches the guest export on first
    /// <see cref="Allocate"/> call. Throws if the component
    /// doesn't export <c>cabi_realloc</c>.</para>
    /// </summary>
    internal sealed class Realloc
    {
        private readonly WasmRuntime _runtime;
        private Wacs.Core.Runtime.Delegates.GenericFuncs? _cabiRealloc;

        public Realloc(WasmRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(
                nameof(runtime));
        }

        /// <summary>Allocate <paramref name="size"/> bytes of
        /// guest memory aligned to <paramref name="align"/>.
        /// Returns the i32 pointer the guest reserved.</summary>
        public int Allocate(int align, int size)
        {
            if (_cabiRealloc == null)
            {
                if (!_runtime.TryGetExportedFunction(
                        "cabi_realloc", out var addr))
                    throw new InvalidOperationException(
                        "Component does not export cabi_realloc"
                        + " — required for any host method that"
                        + " writes string / list / aggregate"
                        + " payloads back to guest memory.");
                _cabiRealloc = _runtime.CreateInvoker(
                    addr, new InvokerOptions());
            }
            return _cabiRealloc(0, 0, align, size)[0].Data.Int32;
        }
    }
}
