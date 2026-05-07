// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN.Resources
{
    /// <summary>
    /// WIT <c>tensor</c> resource. Wraps the immutable
    /// <see cref="Tensor"/> value type as a reference-typed
    /// box so it can sit in
    /// <see cref="HostBinding.ResourceTable"/> and be addressed
    /// by an i32 handle. The constructor binding mints one of
    /// these and registers it in the host's tensor table; the
    /// three accessor methods (<c>dimensions</c> / <c>ty</c> /
    /// <c>data</c>) read the wrapped <see cref="Value"/>.
    ///
    /// <para>Mutability isn't part of the WIT contract — the
    /// resource is constructed once and read-only thereafter.
    /// Pure data; no native handles to dispose.</para>
    /// </summary>
    internal sealed class TensorResource
    {
        public Tensor Value { get; }

        public TensorResource(Tensor value)
        {
            Value = value;
        }
    }
}
