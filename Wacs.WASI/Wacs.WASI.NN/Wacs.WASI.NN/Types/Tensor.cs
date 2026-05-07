// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.NN.Types
{
    /// <summary>
    /// Backend-agnostic tensor view. <see cref="Data"/> is a
    /// <see cref="ReadOnlyMemory{T}"/> so backends can hold the
    /// reference for the lifetime of an inference call without
    /// mandating a host-side copy. Inputs received from a guest
    /// are typically lifted into a host-allocated array because
    /// guest linear memory can grow (and thus relocate) between
    /// the canonical-ABI lift and the backend's call into native
    /// code; outputs the host produces are wrapped without copy.
    ///
    /// <para>Layout: row-major, contiguous, no stride/padding.
    /// Total byte length must equal
    /// <c>product(Dimensions) * <see cref="TensorTypeExtensions.ElementSize"/>(Type)</c>.
    /// The constructor enforces that invariant — callers passing
    /// mismatched lengths get an <see cref="ArgumentException"/>
    /// rather than a backend crash mid-inference.</para>
    /// </summary>
    public readonly struct Tensor
    {
        public uint[] Dimensions { get; }
        public TensorType Type { get; }
        public ReadOnlyMemory<byte> Data { get; }

        public Tensor(uint[] dimensions, TensorType type, ReadOnlyMemory<byte> data)
        {
            if (dimensions == null) throw new ArgumentNullException(nameof(dimensions));
            Dimensions = dimensions;
            Type = type;
            Data = data;

            // Tensor-data length is mostly unchecked at the WIT
            // boundary (the spec defines the relation but doesn't
            // require the host to enforce it). We enforce here so
            // a malformed host produced tensor doesn't get past
            // the Tensor construction site — failures at
            // construction are far easier to diagnose than the
            // backend's downstream complaint.
            long expected = type.ElementSize();
            for (int i = 0; i < dimensions.Length; i++)
                expected = checked(expected * dimensions[i]);
            if (data.Length != expected)
                throw new ArgumentException(
                    $"Tensor data length {data.Length} does not match dimensions {DescribeDimensions(dimensions)} × {type} ({expected} bytes expected)",
                    nameof(data));
        }

        private static string DescribeDimensions(uint[] dims)
        {
            if (dims.Length == 0) return "[]";
            var sb = new System.Text.StringBuilder();
            sb.Append('[');
            for (int i = 0; i < dims.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(dims[i]);
            }
            sb.Append(']');
            return sb.ToString();
        }
    }

    /// <summary>
    /// (name, tensor) pair from the WIT <c>inference.named-tensor</c>
    /// type. The legacy WITX binding synthesizes names by index
    /// (<c>"0"</c>, <c>"1"</c>, …) so a single
    /// <see cref="IBackend"/> implementation can serve both ABIs
    /// without per-ABI plumbing — the indexed-input WITX shape
    /// becomes the named-input WIT shape with a deterministic
    /// naming convention.
    /// </summary>
    public readonly struct NamedTensor
    {
        public string Name { get; }
        public Tensor Tensor { get; }

        public NamedTensor(string name, Tensor tensor)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Tensor = tensor;
        }
    }
}
