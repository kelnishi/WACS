// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.NN.Types
{
    /// <summary>
    /// Element type for tensors. Mirrors the WIT
    /// <c>tensor.tensor-type</c> enum (wasi:nn@0.2.0-rc-2024-10-28).
    /// Discriminant values match the WIT enum order so the binding
    /// layer can cast wire byte → enum directly.
    ///
    /// <para>The legacy WITX <c>tensor_type</c> enum is a subset
    /// (no <see cref="BF16"/>); the WITX binding maps wire bytes
    /// {0,1,2,3,4,5} ↔ {FP16, FP32, FP64, U8, I32, I64} explicitly,
    /// since BF16 sits between FP64 and U8 in the WIT order.</para>
    /// </summary>
    public enum TensorType : byte
    {
        FP16 = 0,
        FP32 = 1,
        FP64 = 2,
        BF16 = 3,
        U8 = 4,
        I32 = 5,
        I64 = 6,
    }

    /// <summary>
    /// Byte size of a single element of <paramref name="ty"/>.
    /// Used to compute total byte count from
    /// <c>product(dimensions) * ElementSize(ty)</c>.
    /// </summary>
    public static class TensorTypeExtensions
    {
        public static int ElementSize(this TensorType ty) => ty switch
        {
            TensorType.FP16 => 2,
            TensorType.FP32 => 4,
            TensorType.FP64 => 8,
            TensorType.BF16 => 2,
            TensorType.U8 => 1,
            TensorType.I32 => 4,
            TensorType.I64 => 8,
            _ => 0,
        };
    }
}
