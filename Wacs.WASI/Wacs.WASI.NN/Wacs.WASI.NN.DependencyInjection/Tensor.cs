// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Nn = Wacs.WASI.NN.Nn;

namespace Wacs.WASI.NN.DependencyInjection
{
    /// <summary>
    /// Host representation of <c>wasi:nn/tensor.tensor</c>. Holds
    /// dimensions, element type, and raw data — created by either
    /// the guest (via the WIT <c>constructor</c>, lowered to a
    /// parameterless host instantiation + <see cref="Create"/>
    /// populate) or by the host (via
    /// <see cref="FromBackendOutput"/> when an inference returns
    /// output tensors).
    ///
    /// <para>The class has a parameterless ctor so the canonical-
    /// ABI resource-construct lift can <c>Activator.CreateInstance</c>
    /// it before calling <see cref="Create"/>; subsequent
    /// <see cref="Create"/> calls on the same instance throw —
    /// each tensor is single-construction.</para>
    /// </summary>
    public sealed class Tensor : Nn.ITensor, IDisposable
    {
        private uint[]? _dimensions;
        private Nn.TensorType _ty;
        private byte[]? _data;

        public Tensor() { }

        /// <summary>
        /// Host-side factory for output tensors. The wasi-nn
        /// <c>compute</c> result lifts <c>list&lt;named-tensor&gt;</c>;
        /// each output tensor is constructed here and lowered
        /// back into a fresh resource handle.
        /// </summary>
        public static Tensor FromBackendOutput(uint[] dimensions,
            Nn.TensorType ty, byte[] data)
        {
            var t = new Tensor();
            t.Create(dimensions, ty, data);
            return t;
        }

        public void Create(uint[] dimensions, Nn.TensorType ty, byte[] data)
        {
            if (_dimensions != null)
                throw new InvalidOperationException(
                    "Tensor.Create called on an already-constructed instance");
            _dimensions = dimensions
                ?? throw new ArgumentNullException(nameof(dimensions));
            _ty = ty;
            _data = data
                ?? throw new ArgumentNullException(nameof(data));
        }

        public uint[] Dimensions()
        {
            if (_dimensions == null)
                throw new InvalidOperationException(
                    "Tensor.Dimensions called before Create");
            return _dimensions;
        }

        public Nn.TensorType Ty()
        {
            if (_dimensions == null)
                throw new InvalidOperationException(
                    "Tensor.Ty called before Create");
            return _ty;
        }

        public byte[] Data()
        {
            if (_data == null)
                throw new InvalidOperationException(
                    "Tensor.Data called before Create");
            return _data;
        }

        public void Dispose() { }
    }
}
