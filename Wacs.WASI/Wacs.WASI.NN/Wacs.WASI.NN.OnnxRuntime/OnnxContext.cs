// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;
// `Microsoft.ML.OnnxRuntime.Tensors.Tensor` collides with our
// own Tensor type; alias to keep the using's TensorElementType
// available without making our type ambiguous.
using Tensor = Wacs.WASI.NN.Types.Tensor;

namespace Wacs.WASI.NN.OnnxRuntime
{
    /// <summary>
    /// One inference session against an
    /// <see cref="OnnxGraph"/>. Holds no per-call state —
    /// <see cref="InferenceSession"/> is stateless across
    /// <c>Run</c> calls, so a single context can be invoked
    /// many times and concurrent contexts can share the
    /// underlying session safely.
    ///
    /// <para>The context dispose path is a no-op; the heavy
    /// resource (the session) is owned by the parent
    /// <see cref="OnnxGraph"/> and released when the graph
    /// handle is dropped.</para>
    /// </summary>
    internal sealed class OnnxContext : IBackendContext
    {
        private readonly OnnxGraph _graph;

        public OnnxContext(OnnxGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        public IReadOnlyList<NamedTensor> Compute(IReadOnlyList<NamedTensor> inputs)
        {
            // Parallel input arrays — ORT's Run(OrtValue)
            // overload takes (names, values) collections rather
            // than a dictionary. Ownership of the values stays
            // with the local list so we can dispose them in
            // the finally block regardless of how Run exits.
            var inputNames = new List<string>(inputs.Count);
            var inputValues = new List<OrtValue>(inputs.Count);
            var seen = new HashSet<string>();
            try
            {
                for (int i = 0; i < inputs.Count; i++)
                {
                    var nt = inputs[i];
                    if (!seen.Add(nt.Name))
                        throw new WasiNNException(
                            ErrorCode.InvalidArgument,
                            $"duplicate input name '{nt.Name}'");
                    inputNames.Add(nt.Name);
                    inputValues.Add(BuildOrtValue(nt.Tensor));
                }

                // Run against every output the model declares.
                // Real wasi-nn guests typically know their
                // outputs ahead of time but the WIT contract
                // returns "the inferred outputs" — we map that
                // to "all of the model's outputs". OutputMetadata
                // preserves declaration order, which matches
                // what guests expect.
                var outputNames = new List<string>(
                    _graph.Session.OutputMetadata.Keys);

                using var runOpts = new RunOptions();
                using var results = _graph.Session.Run(
                    runOpts, inputNames, inputValues, outputNames);

                var output = new List<NamedTensor>(results.Count);
                for (int i = 0; i < results.Count; i++)
                    output.Add(new NamedTensor(
                        outputNames[i],
                        MaterializeOrtValue(results[i])));
                return output;
            }
            catch (Exception ex) when (ex is not WasiNNException)
            {
                throw new WasiNNException(
                    ErrorCode.RuntimeError,
                    $"ONNX Run failed: {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }
            finally
            {
                for (int i = 0; i < inputValues.Count; i++)
                    inputValues[i].Dispose();
            }
        }

        public void Dispose()
        {
            // Session lifetime tracks the graph. Nothing to
            // clean up here; the graph's Dispose will tear
            // down the session when the WIT [resource-drop]graph
            // import fires.
        }

        // ----------------------------------------------------
        //   Tensor lift / materialize
        // ----------------------------------------------------

        // Build an OrtValue from our typed Tensor. We
        // reinterpret the byte buffer as the target element
        // type. The ORT API requires a typed array (not raw
        // bytes), so we copy out a typed array and hand it to
        // CreateTensorValueFromMemory. The copy is bounded by
        // the input tensor size (typically KB-MB for compute
        // inputs); compared to the inference cost it's noise.
        private static OrtValue BuildOrtValue(Tensor t)
        {
            var shape = ToLongShape(t.Dimensions);
            var data = t.Data.Span;

            return t.Type switch
            {
                TensorType.U8 => OrtValue.CreateTensorValueFromMemory(
                    data.ToArray(), shape),
                TensorType.FP32 => OrtValue.CreateTensorValueFromMemory(
                    Cast<float>(data), shape),
                TensorType.FP64 => OrtValue.CreateTensorValueFromMemory(
                    Cast<double>(data), shape),
                TensorType.I32 => OrtValue.CreateTensorValueFromMemory(
                    Cast<int>(data), shape),
                TensorType.I64 => OrtValue.CreateTensorValueFromMemory(
                    Cast<long>(data), shape),
                TensorType.FP16 => throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    "OnnxBackend v0 does not yet wire FP16 inputs; "
                    + "ORT supports them but the byte → Float16 "
                    + "reinterpretation needs the .NET 8 Half type "
                    + "or ORT's Float16 struct."),
                TensorType.BF16 => throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    "OnnxBackend v0 does not yet wire BF16 inputs; "
                    + "ORT's BFloat16 struct needs explicit byte "
                    + "lift to be exposed here."),
                _ => throw new WasiNNException(
                    ErrorCode.InvalidArgument,
                    $"unknown TensorType {t.Type}"),
            };
        }

        // Reinterpret the byte buffer as a typed array. The
        // typed array is owned by ORT after
        // CreateTensorValueFromMemory; we never touch it
        // again. Length is checked at the Tensor ctor so the
        // cast can't overrun.
        private static T[] Cast<T>(ReadOnlySpan<byte> bytes) where T : unmanaged
        {
            var span = MemoryMarshal.Cast<byte, T>(bytes);
            var arr = new T[span.Length];
            span.CopyTo(arr);
            return arr;
        }

        private static long[] ToLongShape(uint[] dims)
        {
            var s = new long[dims.Length];
            for (int i = 0; i < dims.Length; i++) s[i] = dims[i];
            return s;
        }

        // Pull bytes + dims + type out of an OrtValue and
        // build our Tensor. The OrtValue is owned by the
        // results collection — the caller (Compute) disposes
        // results, so we have to copy bytes out before that
        // happens.
        private static Tensor MaterializeOrtValue(OrtValue val)
        {
            var info = val.GetTensorTypeAndShape();
            var shape = info.Shape;
            var dims = new uint[shape.Length];
            for (int i = 0; i < shape.Length; i++)
            {
                if (shape[i] < 0)
                    throw new WasiNNException(
                        ErrorCode.RuntimeError,
                        $"output tensor has dynamic dimension at index {i}; "
                        + "wasi-nn requires concrete shapes");
                dims[i] = (uint)shape[i];
            }

            var (ourType, elemSize) = MapElementType(info.ElementDataType);
            int totalBytes = checked((int)info.ElementCount * elemSize);
            var bytes = new byte[totalBytes];

            // Copy out the typed buffer as raw bytes.
            // GetTensorDataAsSpan<T> typed, then MemoryMarshal
            // back to bytes; works for any unmanaged element
            // type the model returns.
            CopyOrtTensorBytes(val, ourType, bytes);

            return new Tensor(dims, ourType, bytes);
        }

        private static (TensorType ourType, int elemSize) MapElementType(
            TensorElementType ortType) => ortType switch
        {
            TensorElementType.Float => (TensorType.FP32, 4),
            TensorElementType.Double => (TensorType.FP64, 8),
            TensorElementType.UInt8 => (TensorType.U8, 1),
            TensorElementType.Int32 => (TensorType.I32, 4),
            TensorElementType.Int64 => (TensorType.I64, 8),
            TensorElementType.Float16 => throw new WasiNNException(
                ErrorCode.UnsupportedOperation,
                "OnnxBackend v0 does not yet materialize FP16 outputs"),
            TensorElementType.BFloat16 => throw new WasiNNException(
                ErrorCode.UnsupportedOperation,
                "OnnxBackend v0 does not yet materialize BF16 outputs"),
            _ => throw new WasiNNException(
                ErrorCode.UnsupportedOperation,
                $"OnnxBackend v0 does not map ORT element type {ortType} "
                + "to a wasi-nn tensor-type (the WIT enum has only the "
                + "seven primitive element types)."),
        };

        private static void CopyOrtTensorBytes(OrtValue val, TensorType ty, byte[] dst)
        {
            switch (ty)
            {
                case TensorType.U8:
                    val.GetTensorDataAsSpan<byte>().CopyTo(dst);
                    break;
                case TensorType.FP32:
                    MemoryMarshal.AsBytes(val.GetTensorDataAsSpan<float>())
                        .CopyTo(dst);
                    break;
                case TensorType.FP64:
                    MemoryMarshal.AsBytes(val.GetTensorDataAsSpan<double>())
                        .CopyTo(dst);
                    break;
                case TensorType.I32:
                    MemoryMarshal.AsBytes(val.GetTensorDataAsSpan<int>())
                        .CopyTo(dst);
                    break;
                case TensorType.I64:
                    MemoryMarshal.AsBytes(val.GetTensorDataAsSpan<long>())
                        .CopyTo(dst);
                    break;
                default:
                    throw new WasiNNException(
                        ErrorCode.RuntimeError,
                        $"unmappable output type {ty}");
            }
        }
    }
}
