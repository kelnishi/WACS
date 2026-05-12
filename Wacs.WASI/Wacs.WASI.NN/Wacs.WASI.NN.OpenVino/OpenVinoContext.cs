// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenVinoSharp;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;
// `OpenVinoSharp.Tensor` collides with our own Tensor type;
// alias to keep both available.
using OvTensor = OpenVinoSharp.Tensor;
using OvShape = OpenVinoSharp.Shape;
// `OpenVinoSharp.element.Type` is the element-type wrapper —
// `element` is a child namespace of OpenVinoSharp; the
// compiler can't find it without a fully qualified path
// because `element` isn't a top-level token. Alias to a crisp
// single name.
using OvElementType = OpenVinoSharp.element.Type;
using Tensor = Wacs.WASI.NN.Types.Tensor;

namespace Wacs.WASI.NN.OpenVino
{
    /// <summary>
    /// One inference session against an
    /// <see cref="OpenVinoGraph"/>. Owns a single
    /// <see cref="InferRequest"/> minted off the parent's
    /// compiled model — the request carries the per-context
    /// state (input tensors, output buffers) across infer
    /// calls. OpenVINO infer requests are not thread-safe for
    /// concurrent <c>infer()</c> calls; fan out via multiple
    /// contexts when parallelism matters.
    ///
    /// <para>Input naming: the WIT shape supplies
    /// <see cref="NamedTensor.Name"/> for every input; we feed
    /// it directly through <see cref="InferRequest.set_tensor(string, OvTensor)"/>
    /// if the model has an input with that name, falling back
    /// to positional binding (parsed-as-int index →
    /// <see cref="InferRequest.set_input_tensor(ulong, OvTensor)"/>)
    /// for the WITX path that uses "0", "1", … as synthetic
    /// names.</para>
    /// </summary>
    internal sealed class OpenVinoContext : IBackendContext
    {
        private readonly OpenVinoGraph _graph;
        private readonly InferRequest _request;
        private bool _disposed;

        public OpenVinoContext(OpenVinoGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _request = graph.Compiled.create_infer_request();
        }

        public IReadOnlyList<NamedTensor> Compute(IReadOnlyList<NamedTensor> inputs)
        {
            // Track each OvTensor we allocate so we can dispose
            // them after infer() — OpenVINO copies the bytes
            // into its own native arena during set_tensor, so
            // dropping the wrapper after infer is safe.
            var tensorRefs = new List<OvTensor>(inputs.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < inputs.Count; i++)
                {
                    var nt = inputs[i];
                    if (!seen.Add(nt.Name))
                        throw new WasiNNException(
                            ErrorCode.InvalidArgument,
                            $"duplicate input name '{nt.Name}'");

                    var ovTensor = BuildOvTensor(nt.Tensor);
                    tensorRefs.Add(ovTensor);
                    BindInput(nt.Name, i, ovTensor);
                }

                _request.infer();

                ulong outputCount = _graph.Compiled.get_outputs_size();
                var outputs = new List<NamedTensor>((int)outputCount);
                for (ulong i = 0; i < outputCount; i++)
                {
                    using var outNode = _graph.Compiled.get_output(i);
                    string name = SafeNodeName(outNode, fallback: i.ToString());
                    var ov = _request.get_output_tensor(i);
                    outputs.Add(new NamedTensor(name, MaterializeOvTensor(ov)));
                    ov.Dispose();
                }
                return outputs;
            }
            catch (WasiNNException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new WasiNNException(
                    ErrorCode.RuntimeError,
                    $"OpenVINO infer failed: {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }
            finally
            {
                for (int i = 0; i < tensorRefs.Count; i++)
                    tensorRefs[i].Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _request.Dispose();
        }

        // ----------------------------------------------------
        //   Input binding (name → set_tensor, else index → set_input_tensor)
        // ----------------------------------------------------

        // Prefer name-binding (the WIT path supplies the
        // model's actual input names) and fall back to
        // positional (the WITX path synthesizes "0", "1", …).
        // If neither matches, raise InvalidArgument so the
        // guest sees a clear error rather than a downstream
        // shape mismatch.
        private void BindInput(string name, int positionalIdx, OvTensor tensor)
        {
            // Try positional first when the name parses to an
            // integer — keeps the WITX synthetic-naming path
            // O(1) and avoids dictionary lookups against the
            // model's name list.
            if (int.TryParse(name, out var parsedIdx)
                && parsedIdx >= 0
                && (ulong)parsedIdx < _graph.Compiled.get_inputs_size())
            {
                _request.set_input_tensor((ulong)parsedIdx, tensor);
                return;
            }

            try
            {
                _request.set_tensor(name, tensor);
                return;
            }
            catch
            {
                // Name not found — fall through to positional.
            }

            if ((ulong)positionalIdx >= _graph.Compiled.get_inputs_size())
                throw new WasiNNException(
                    ErrorCode.InvalidArgument,
                    $"input '{name}' (positional {positionalIdx}) "
                    + "exceeds the OpenVINO model's input count "
                    + $"({_graph.Compiled.get_inputs_size()})");
            _request.set_input_tensor((ulong)positionalIdx, tensor);
        }

        // CompiledModel.get_output(idx) returns a Node — Node has
        // get_name() (Output has get_any_name() but we don't have
        // one in hand here). Some graph outputs are unnamed; fall
        // back to a positional placeholder so the guest can still
        // index by order, matching the WITX convention for
        // tensor-id-less results.
        private static string SafeNodeName(Node node, string fallback)
        {
            try
            {
                var n = node.get_name();
                return string.IsNullOrEmpty(n) ? fallback : n;
            }
            catch
            {
                return fallback;
            }
        }

        // ----------------------------------------------------
        //   Tensor lift / materialize
        // ----------------------------------------------------

        // Build an OvTensor from our typed Tensor. OpenVINO's
        // `new Tensor(element.Type, Shape, byte[])` ctor copies
        // the bytes into native memory at construction, so we
        // can hand it the same byte buffer the guest gave us
        // and drop the wrapper after infer.
        private static OvTensor BuildOvTensor(Tensor t)
        {
            var shape = new OvShape(ToLongShape(t.Dimensions));
            var elemType = MapToOvType(t.Type);
            // OpenVINO's byte-array ctor needs a copy because
            // the wasi-nn Tensor's Data is ReadOnlyMemory<byte>
            // (potentially backed by guest linear memory).
            // ToArray materializes the bytes into a stable
            // host-owned buffer so OpenVINO's copy is safe even
            // if the guest grows memory mid-call.
            return new OvTensor(elemType, shape, t.Data.ToArray());
        }

        private static OvElementType MapToOvType(TensorType ty)
        {
            switch (ty)
            {
                case TensorType.FP16: return new OvElementType(ElementType.F16);
                case TensorType.FP32: return new OvElementType(ElementType.F32);
                case TensorType.FP64: return new OvElementType(ElementType.F64);
                case TensorType.BF16: return new OvElementType(ElementType.BF16);
                case TensorType.U8:   return new OvElementType(ElementType.U8);
                case TensorType.I32:  return new OvElementType(ElementType.I32);
                case TensorType.I64:  return new OvElementType(ElementType.I64);
                default:
                    throw new WasiNNException(
                        ErrorCode.InvalidArgument,
                        $"unknown TensorType {ty}");
            }
        }

        private static long[] ToLongShape(uint[] dims)
        {
            var s = new long[dims.Length];
            for (int i = 0; i < dims.Length; i++) s[i] = dims[i];
            return s;
        }

        // Pull bytes + dims + type out of an OvTensor and build
        // our Tensor. The OvTensor is owned by the InferRequest
        // — get_byte_data returns a fresh byte[] copied out of
        // the OvTensor's native arena, so we get a stable buffer
        // independent of the InferRequest's lifetime.
        private static Tensor MaterializeOvTensor(OvTensor val)
        {
            var ovShape = val.get_shape();
            var dims = ShapeToUInts(ovShape);
            ovShape.Dispose();

            // element.Type is value-like — no Dispose method.
            var ovType = val.get_element_type();
            var (ourType, _) = MapFromOvType(ovType);

            ulong byteSize = val.get_byte_size();
            // Tensor.get_byte_data(int) returns a fresh byte[]
            // copied out of the OvTensor's native buffer — the
            // copy is what we want here so the buffer survives
            // the OvTensor's later Dispose.
            byte[] bytes = byteSize > 0
                ? val.get_byte_data(checked((int)byteSize))
                : Array.Empty<byte>();
            return new Tensor(dims, ourType, bytes);
        }

        private static uint[] ShapeToUInts(OvShape shape)
        {
            // OpenVinoSharp.Shape carries its dims as a public
            // ov_shape struct field; ov_shape.get_dims() returns
            // a long[] (concrete dims at the model's compile
            // time). Convert to our uint[] (wasi-nn tensor dims
            // are u32).
            long[] raw = shape.shape.get_dims();
            var dims = new uint[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                long d = raw[i];
                if (d < 0)
                    throw new WasiNNException(
                        ErrorCode.RuntimeError,
                        $"output tensor has dynamic dimension at index {i}; "
                        + "wasi-nn requires concrete shapes");
                dims[i] = checked((uint)d);
            }
            return dims;
        }

        private static (TensorType ourType, int elemSize) MapFromOvType(
            OvElementType ovType)
        {
            // OpenVinoSharp wraps the C++ ov::element::Type as a
            // managed type with a get_type() accessor that
            // returns the ElementType enum. Match against that.
            var t = ovType.get_type();
            switch (t)
            {
                case ElementType.F16: return (TensorType.FP16, 2);
                case ElementType.F32: return (TensorType.FP32, 4);
                case ElementType.F64: return (TensorType.FP64, 8);
                case ElementType.BF16: return (TensorType.BF16, 2);
                case ElementType.U8:  return (TensorType.U8, 1);
                case ElementType.I32: return (TensorType.I32, 4);
                case ElementType.I64: return (TensorType.I64, 8);
                default:
                    throw new WasiNNException(
                        ErrorCode.UnsupportedOperation,
                        $"OpenVinoBackend does not map OV element type {t} "
                        + "to a wasi-nn tensor-type; the WIT enum covers "
                        + "only the seven primitive types.");
            }
        }
    }
}
