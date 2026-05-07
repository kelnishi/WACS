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
// Microsoft.ML.OnnxRuntime.Tensors.Tensor collides with our
// Tensor; alias to keep TensorElementType reachable without
// shadowing our type.
using Tensor = Wacs.WASI.NN.Types.Tensor;

namespace Wacs.WASI.NN.MLNet
{
    /// <summary>
    /// Per-inference context. Stateless across <see cref="Compute"/>
    /// calls — same reasoning as the OnnxRuntime backend's
    /// context: ORT sessions are reentrant + ONNX models are
    /// stateless, so multiple contexts can fan out concurrent
    /// inferences against one graph safely.
    ///
    /// <para>The lift / lower path duplicates
    /// <c>Wacs.WASI.NN.OnnxRuntime.OnnxContext</c>'s — both
    /// backends drive the same ORT session and need the same
    /// byte ↔ typed-array reinterp. Keeping the helpers
    /// per-package avoids cross-package type leaks while the
    /// SPI is settling; if more backends end up sharing them
    /// the helpers will move to a common
    /// <c>Wacs.WASI.NN.OnnxShared</c> sibling.</para>
    /// </summary>
    internal sealed class MLNetContext : IBackendContext
    {
        private readonly MLNetGraph _graph;

        public MLNetContext(MLNetGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        public IReadOnlyList<NamedTensor> Compute(IReadOnlyList<NamedTensor> inputs)
        {
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

                var outputNames = new List<string>(_graph.Session.OutputMetadata.Keys);
                using var runOpts = new RunOptions();
                using var results = _graph.Session.Run(
                    runOpts, inputNames, inputValues, outputNames);

                var output = new List<NamedTensor>(results.Count);
                for (int i = 0; i < results.Count; i++)
                    output.Add(new NamedTensor(
                        outputNames[i], MaterializeOrtValue(results[i])));
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

        public void Dispose() { /* session lifetime tracks the graph */ }

        // ---- lift / lower (mirrors OnnxContext) ----

        private static OrtValue BuildOrtValue(Tensor t)
        {
            var shape = ToLongShape(t.Dimensions);
            var data = t.Data.Span;
            return t.Type switch
            {
                TensorType.U8 => OrtValue.CreateTensorValueFromMemory(data.ToArray(), shape),
                TensorType.FP32 => OrtValue.CreateTensorValueFromMemory(Cast<float>(data), shape),
                TensorType.FP64 => OrtValue.CreateTensorValueFromMemory(Cast<double>(data), shape),
                TensorType.I32 => OrtValue.CreateTensorValueFromMemory(Cast<int>(data), shape),
                TensorType.I64 => OrtValue.CreateTensorValueFromMemory(Cast<long>(data), shape),
                TensorType.FP16 or TensorType.BF16 => throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    $"MLNetBackend v0 does not yet wire {t.Type} inputs"),
                _ => throw new WasiNNException(
                    ErrorCode.InvalidArgument,
                    $"unknown TensorType {t.Type}"),
            };
        }

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
                        $"output tensor has dynamic dimension at index {i}");
                dims[i] = (uint)shape[i];
            }
            var (ourType, elemSize) = MapElementType(info.ElementDataType);
            var bytes = new byte[checked((int)info.ElementCount * elemSize)];
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
            TensorElementType.Float16 or TensorElementType.BFloat16 =>
                throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    $"MLNetBackend v0 does not yet materialize {ortType} outputs"),
            _ => throw new WasiNNException(
                ErrorCode.UnsupportedOperation,
                $"MLNetBackend v0 does not map ORT element type {ortType}"),
        };

        private static void CopyOrtTensorBytes(OrtValue val, TensorType ty, byte[] dst)
        {
            switch (ty)
            {
                case TensorType.U8:
                    val.GetTensorDataAsSpan<byte>().CopyTo(dst); break;
                case TensorType.FP32:
                    MemoryMarshal.AsBytes(val.GetTensorDataAsSpan<float>()).CopyTo(dst); break;
                case TensorType.FP64:
                    MemoryMarshal.AsBytes(val.GetTensorDataAsSpan<double>()).CopyTo(dst); break;
                case TensorType.I32:
                    MemoryMarshal.AsBytes(val.GetTensorDataAsSpan<int>()).CopyTo(dst); break;
                case TensorType.I64:
                    MemoryMarshal.AsBytes(val.GetTensorDataAsSpan<long>()).CopyTo(dst); break;
                default:
                    throw new WasiNNException(
                        ErrorCode.RuntimeError, $"unmappable output type {ty}");
            }
        }
    }
}
