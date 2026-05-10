// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TorchSharp;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;
// Microsoft's torch.Tensor and our Wacs.WASI.NN.Types.Tensor
// collide; alias the local one so the using stays clean.
using NnTensor = Wacs.WASI.NN.Types.Tensor;

namespace Wacs.WASI.NN.TorchSharp
{
    /// <summary>
    /// One inference session against a
    /// <see cref="TorchSharpGraph"/>. Holds no per-call state —
    /// TorchScript modules in <c>eval()</c> mode are stateless
    /// across <c>forward</c> calls, so a single context can be
    /// invoked many times and concurrent contexts can share the
    /// underlying module.
    ///
    /// <para>The context dispose path is a no-op; the heavy
    /// resource (the module) is owned by the parent
    /// <see cref="TorchSharpGraph"/> and released when the
    /// graph handle is dropped.</para>
    ///
    /// <para>Input / output naming convention: TorchScript
    /// modules consume positional <c>forward(t1, t2, …)</c> args
    /// and return either a single tensor or a tuple. The wasi-nn
    /// WIT contract is name-keyed
    /// (<c>list&lt;tuple&lt;string, tensor&gt;&gt;</c>), so the
    /// binding follows the WasmEdge convention: input names are
    /// <c>"0"</c>, <c>"1"</c>, … (positional index as decimal
    /// string); outputs are emitted under the same indexed
    /// scheme. Sorting inputs by parsed integer name produces
    /// the positional argument order; non-numeric names trip
    /// <see cref="ErrorCode.InvalidArgument"/> at compute time.</para>
    /// </summary>
    internal sealed class TorchSharpContext : IBackendContext
    {
        private readonly TorchSharpGraph _graph;

        public TorchSharpContext(TorchSharpGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        // Per-call state lives only in Compute's locals; the
        // module is owned by TorchSharpGraph. Nothing to release.
        public void Dispose() { }

        public IReadOnlyList<NamedTensor> Compute(IReadOnlyList<NamedTensor> inputs)
        {
            // Sort inputs by parsed integer name so positional
            // dispatch order is deterministic regardless of how
            // the guest enumerated the named-tensor list. WasmEdge
            // wasi-nn's PyTorch backend uses the same convention.
            var ordered = new (int Index, NnTensor Tensor)[inputs.Count];
            for (int i = 0; i < inputs.Count; i++)
            {
                if (!int.TryParse(
                        inputs[i].Name,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var index)
                    || index < 0)
                {
                    throw new WasiNNException(
                        ErrorCode.InvalidArgument,
                        $"TorchSharpBackend expects positional indexed input "
                        + $"names ('0', '1', …); got '{inputs[i].Name}'. "
                        + "TorchScript modules consume positional forward "
                        + "args, so the binding maps name → index.");
                }
                ordered[i] = (index, inputs[i].Tensor);
            }
            Array.Sort(ordered, (a, b) => a.Index.CompareTo(b.Index));

            // Allocate libtorch tensors for each input. The
            // TorchSharp `bytes` setter copies into the tensor's
            // underlying storage; for inputs we own the storage,
            // so a simple Span.CopyTo is enough.
            var torchInputs = new torch.Tensor[ordered.Length];
            try
            {
                for (int i = 0; i < ordered.Length; i++)
                {
                    if (ordered[i].Index != i)
                        throw new WasiNNException(
                            ErrorCode.InvalidArgument,
                            "TorchSharpBackend input indices must be a "
                            + "contiguous 0..n-1 sequence; got "
                            + $"non-sequential index {ordered[i].Index} "
                            + $"at position {i}.");
                    torchInputs[i] = MaterializeInput(ordered[i].Tensor);
                }

                object? result;
                try
                {
                    // forward + call are aliased on ScriptModule;
                    // call(object[]) is the public dispatch API.
                    result = _graph.Module.call(
                        torchInputs.Cast<object>().ToArray());
                }
                catch (Exception ex) when (ex is not WasiNNException)
                {
                    throw new WasiNNException(
                        ErrorCode.RuntimeError,
                        $"TorchScript forward failed: {ex.Message}",
                        backendData: ex.ToString(),
                        innerException: ex);
                }

                return MaterializeOutputs(result);
            }
            finally
            {
                // Dispose input tensors regardless of how forward
                // exited — they're allocated per-call and don't
                // outlive the inference. Output tensors are
                // returned to the caller through MaterializeOutputs;
                // their lifetime is the wasi-nn `tensor` resource
                // handle the guest receives.
                for (int i = 0; i < torchInputs.Length; i++)
                    torchInputs[i]?.Dispose();
            }
        }

        // Allocate a libtorch tensor of the right dtype + shape
        // and copy the wasi-nn input bytes into its storage.
        private static torch.Tensor MaterializeInput(NnTensor input)
        {
            var dtype = MapToScalarType(input.Type);
            var shape = ToInt64Shape(input.Dimensions);
            var t = torch.empty(shape, dtype);
            try
            {
                // Tensor.bytes is a settable Span<byte> — copying
                // the input data into it lands in libtorch's
                // contiguous storage with no extra allocations.
                input.Data.Span.CopyTo(t.bytes);
            }
            catch
            {
                t.Dispose();
                throw;
            }
            return t;
        }

        private static IReadOnlyList<NamedTensor> MaterializeOutputs(object? result)
        {
            switch (result)
            {
                case null:
                    throw new WasiNNException(
                        ErrorCode.RuntimeError,
                        "TorchScript forward returned null");
                case torch.Tensor single:
                    return new[] { ExtractNamed("0", single) };
                default:
                    return UnpackMultiOutput(result);
            }
        }

        private static IReadOnlyList<NamedTensor> UnpackMultiOutput(object result)
        {
            // TorchScript modules can return:
            //   - a single torch.Tensor (handled above)
            //   - object[] (TorchSharp lifts a C++ tuple this way)
            //   - System.Runtime.CompilerServices.ITuple
            //     (ValueTuples land here)
            //   - IList<torch.Tensor> (Python `list[Tensor]` ↔
            //     TorchSharp List)
            // Treat each as a flat sequence of tensors named by
            // index ("0", "1", …). Anything else trips
            // RuntimeError so the caller can debug the model
            // signature.
            var tensors = new List<torch.Tensor>();
            switch (result)
            {
                case object[] arr:
                    foreach (var item in arr) AppendTensor(tensors, item);
                    break;
                case System.Runtime.CompilerServices.ITuple tup:
                    for (int i = 0; i < tup.Length; i++)
                        AppendTensor(tensors, tup[i]);
                    break;
                case IList<torch.Tensor> list:
                    foreach (var t in list) tensors.Add(t);
                    break;
                case IEnumerable enumerable:
                    foreach (var item in enumerable)
                        AppendTensor(tensors, item);
                    break;
                default:
                    throw new WasiNNException(
                        ErrorCode.RuntimeError,
                        $"TorchScript forward returned an unexpected type "
                        + $"'{result.GetType().FullName}'. Wrap the model so "
                        + "it returns a Tensor, tuple, or list of tensors.");
            }

            var named = new NamedTensor[tensors.Count];
            for (int i = 0; i < tensors.Count; i++)
            {
                named[i] = ExtractNamed(
                    i.ToString(CultureInfo.InvariantCulture),
                    tensors[i]);
            }
            return named;
        }

        private static void AppendTensor(List<torch.Tensor> sink, object? item)
        {
            if (item is torch.Tensor t)
            {
                sink.Add(t);
                return;
            }
            throw new WasiNNException(
                ErrorCode.RuntimeError,
                $"TorchScript forward returned a container with a non-Tensor "
                + $"element of type '{item?.GetType().FullName ?? "null"}'. "
                + "Mixed-type outputs aren't supported on the wasi-nn surface.");
        }

        private static NamedTensor ExtractNamed(string name, torch.Tensor t)
        {
            try
            {
                var dims = ToUInt32Shape(t.shape);
                var dtype = MapFromScalarType(t.dtype);
                // Tensor.bytes returns a Span over libtorch's
                // contiguous storage. ToArray() copies once
                // into managed memory — necessary because the
                // guest's lift will read from this past the
                // current call site (the libtorch tensor's
                // lifetime ends with the inference, but the
                // wasi-nn `own<tensor>` handle outlives it).
                var bytes = t.bytes.ToArray();
                return new NamedTensor(name, new NnTensor(dims, dtype, bytes));
            }
            finally
            {
                // Output tensors are libtorch-owned; the wasi-nn
                // resource handle owns the byte copy now.
                t.Dispose();
            }
        }

        private static torch.ScalarType MapToScalarType(TensorType type) => type switch
        {
            TensorType.FP16 => torch.ScalarType.Float16,
            TensorType.FP32 => torch.ScalarType.Float32,
            TensorType.FP64 => torch.ScalarType.Float64,
            TensorType.BF16 => torch.ScalarType.BFloat16,
            TensorType.U8 => torch.ScalarType.Byte,
            TensorType.I32 => torch.ScalarType.Int32,
            TensorType.I64 => torch.ScalarType.Int64,
            _ => throw new WasiNNException(
                ErrorCode.InvalidArgument,
                $"Unsupported TensorType '{type}' for TorchSharp backend"),
        };

        private static TensorType MapFromScalarType(torch.ScalarType type) => type switch
        {
            torch.ScalarType.Float16 => TensorType.FP16,
            torch.ScalarType.Float32 => TensorType.FP32,
            torch.ScalarType.Float64 => TensorType.FP64,
            torch.ScalarType.BFloat16 => TensorType.BF16,
            torch.ScalarType.Byte => TensorType.U8,
            torch.ScalarType.Int32 => TensorType.I32,
            torch.ScalarType.Int64 => TensorType.I64,
            _ => throw new WasiNNException(
                ErrorCode.RuntimeError,
                $"TorchScript output dtype '{type}' has no wasi-nn "
                + "TensorType mapping. Convert the model's output to a "
                + "supported dtype (FP16/FP32/FP64/BF16/U8/I32/I64) "
                + "before saving the TorchScript module."),
        };

        private static long[] ToInt64Shape(uint[] dims)
        {
            var s = new long[dims.Length];
            for (int i = 0; i < dims.Length; i++) s[i] = dims[i];
            return s;
        }

        private static uint[] ToUInt32Shape(long[] dims)
        {
            var s = new uint[dims.Length];
            for (int i = 0; i < dims.Length; i++)
            {
                if (dims[i] < 0)
                    throw new WasiNNException(
                        ErrorCode.RuntimeError,
                        "TorchScript output has a negative-sized dim "
                        + $"({dims[i]}); model likely has a dynamic shape "
                        + "the binding can't lower into wasi-nn yet.");
                if (dims[i] > uint.MaxValue)
                    throw new WasiNNException(
                        ErrorCode.TooLarge,
                        $"TorchScript output dim {dims[i]} exceeds "
                        + "uint.MaxValue (the wasi-nn dimension type).");
                s[i] = (uint)dims[i];
            }
            return s;
        }
    }
}
