// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Text;
using Wacs.Core.Runtime;
using Wacs.WASI.NN.HostBinding;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN
{
    /// <summary>
    /// Legacy core-module ABI binding for <c>wasi_ephemeral_nn</c>,
    /// targeting the bytecodealliance <c>wasi-nn 0.6</c> wire format
    /// that ships in the upstream Rust crate. Six imports, indexed
    /// inputs / outputs, plain i32 handles, u16 errnos.
    ///
    /// <para>The errno encoding splits across two conventions, matching
    /// what the 0.6 crate emits:
    /// <list type="bullet">
    /// <item>Data-returning calls (<c>load</c>, <c>load_by_name</c>,
    /// <c>init_execution_context</c>, <c>get_output</c>) take a
    /// trailing <c>retArea</c> i32 pointer. Host writes the errno
    /// to retArea+0 and the payload to retArea+4; the function's
    /// own i32 result is unused (always 0).</item>
    /// <item>Void-Ok calls (<c>set_input</c>, <c>compute</c>) carry
    /// no payload, so the errno collapses to the function's i32
    /// return value — no retArea pointer.</item>
    /// </list></para>
    ///
    /// <para>Memory layout helpers (all little-endian):
    /// <list type="bullet">
    /// <item><c>graph_builder_array</c> = (ptr, len) where each
    /// element is itself (ptr, len) describing one
    /// <c>graph_builder</c> blob.</item>
    /// <item><c>set_input</c>'s tensor arg is a single i32 pointer
    /// to a 20-byte packed record: (dims_ptr i32, dims_len i32,
    /// type u8 + 3 padding, data_ptr i32, data_len i32).</item>
    /// </list></para>
    ///
    /// <para>The same <see cref="IBackend"/> SPI the WIT resource
    /// layer consumes drives this binding; per-context tensors
    /// stash in <see cref="WasiNNHost.WitxContextState"/> across
    /// the stateful set_input / compute / get_output lifecycle.</para>
    /// </summary>
    internal static class WitxBindings
    {
        // Module name the legacy ABI imports under. Per the witx
        // header line 59: `(module $wasi_ephemeral_nn ...)`.
        private const string Module = "wasi_ephemeral_nn";

        public static void Bind(WasmRuntime runtime, WasiNNHost host)
        {
            BindLoad(runtime, host);
            BindLoadByName(runtime, host);
            BindInitExecutionContext(runtime, host);
            BindSetInput(runtime, host);
            BindCompute(runtime, host);
            BindGetOutput(runtime, host);
        }

        // load(builder: graph_builder_array, encoding: graph_encoding,
        //      target: execution_target) -> expected<graph, nn_errno>
        // Wire form: (i32 builder_array_ptr, i32 builder_array_len,
        //             i32 encoding, i32 target, i32 retArea) -> i32 errno
        // retArea: u16 errno @0, then graph handle at +4 on success.
        private static void BindLoad(WasmRuntime runtime, WasiNNHost host)
        {
            runtime.BindHostFunction<Func<ExecContext, int, int, int, int, int, int>>(
                (Module, "load"),
                (ctx, arrPtr, arrLen, encoding, target, retArea) =>
                {
                    try
                    {
                        var builders = LiftBuilderArray(ctx, arrPtr, arrLen);
                        var enc = ParseWitxEncoding(encoding);
                        var tgt = ParseWitxTarget(target);
                        var backend = host.ResolveBackend(enc);
                        var graph = backend.LoadGraph(builders, tgt);
                        var handle = host.Graphs.Allocate(graph);
                        WriteWitxOk(ctx, retArea);
                        ctx.WriteI32LE(retArea + 4, handle);
                        return 0;
                    }
                    catch (WasiNNException e)
                    {
                        WriteWitxErr(ctx, retArea, e.Code);
                        return 0;
                    }
                });
        }

        // load_by_name(name: string) -> expected<graph, nn_errno>
        // Wire form: (i32 name_ptr, i32 name_len, i32 retArea) -> i32 errno
        private static void BindLoadByName(WasmRuntime runtime, WasiNNHost host)
        {
            runtime.BindHostFunction<Func<ExecContext, int, int, int, int>>(
                (Module, "load_by_name"),
                (ctx, namePtr, nameLen, retArea) =>
                {
                    try
                    {
                        var name = ReadString(ctx, namePtr, nameLen);
                        var graph = host.LoadGraphByNameDispatch(name);
                        var handle = host.Graphs.Allocate(graph);
                        WriteWitxOk(ctx, retArea);
                        ctx.WriteI32LE(retArea + 4, handle);
                        return 0;
                    }
                    catch (WasiNNException e)
                    {
                        WriteWitxErr(ctx, retArea, e.Code);
                        return 0;
                    }
                });
        }

        // init_execution_context(graph: graph)
        //   -> expected<graph_execution_context, nn_errno>
        private static void BindInitExecutionContext(WasmRuntime runtime, WasiNNHost host)
        {
            runtime.BindHostFunction<Func<ExecContext, int, int, int>>(
                (Module, "init_execution_context"),
                (ctx, graphHandle, retArea) =>
                {
                    try
                    {
                        if (!host.Graphs.TryGet<IBackendGraph>(graphHandle, out var graph) || graph == null)
                            throw new WasiNNException(
                                ErrorCode.InvalidArgument,
                                $"invalid graph handle {graphHandle}");
                        var bctx = graph.CreateContext();
                        var ctxHandle = host.Contexts.Allocate(bctx);
                        host.WitxContextState[ctxHandle] = new WitxContextState(bctx);
                        WriteWitxOk(ctx, retArea);
                        ctx.WriteI32LE(retArea + 4, ctxHandle);
                        return 0;
                    }
                    catch (WasiNNException e)
                    {
                        WriteWitxErr(ctx, retArea, e.Code);
                        return 0;
                    }
                });
        }

        // set_input(context: graph_execution_context, index: u32, tensor: *const Tensor)
        //   -> nn_errno
        // bytecodealliance wasi-nn 0.6 ABI: the tensor record is passed
        // by-pointer, not by-value-expanded. Guest writes a 20-byte
        // packed struct into linear memory and passes its address:
        //   offset 0:  dims_ptr   i32  (*const usize — wasm32 usize = u32)
        //   offset 4:  dims_len   i32  (slice len)
        //   offset 8:  type       u8   (TensorType, low byte of i32 cell)
        //   offset 9:  padding    3    (to 4-byte align)
        //   offset 12: data_ptr   i32  (*const u8)
        //   offset 16: data_len   i32  (slice len)
        // The errno is the function's i32 return value; there is no retArea.
        private static void BindSetInput(WasmRuntime runtime, WasiNNHost host)
        {
            runtime.BindHostFunction<Func<ExecContext, int, int, int, int>>(
                (Module, "set_input"),
                (ctx, ctxHandle, index, tensorPtr) =>
                {
                    try
                    {
                        if (!host.WitxContextState.TryGetValue(ctxHandle, out var state))
                            throw new WasiNNException(
                                ErrorCode.InvalidArgument,
                                $"invalid execution-context handle {ctxHandle}");
                        var mem = ctx.Memory();
                        int dimsPtr  = ReadI32LE(mem, tensorPtr + 0);
                        int dimsLen  = ReadI32LE(mem, tensorPtr + 4);
                        byte typeTag = mem[tensorPtr + 8];
                        int dataPtr  = ReadI32LE(mem, tensorPtr + 12);
                        int dataLen  = ReadI32LE(mem, tensorPtr + 16);
                        var tensor = LiftTensor(ctx, dimsPtr, dimsLen,
                            ParseWitxTensorType(typeTag), dataPtr, dataLen);
                        state.Inputs[(uint)index] = tensor;
                        // Recompute on next compute() call.
                        state.HasComputed = false;
                        return 0;
                    }
                    catch (WasiNNException e)
                    {
                        return (int)(ushort)e.Code.ToWitx();
                    }
                });
        }

        // compute(context: graph_execution_context) -> nn_errno
        // bytecodealliance wasi-nn 0.6 ABI: errno is the i32 return value,
        // no retArea pointer (the void-Ok case carries no payload).
        private static void BindCompute(WasmRuntime runtime, WasiNNHost host)
        {
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Module, "compute"),
                (ctx, ctxHandle) =>
                {
                    try
                    {
                        if (!host.WitxContextState.TryGetValue(ctxHandle, out var state))
                            throw new WasiNNException(
                                ErrorCode.InvalidArgument,
                                $"invalid execution-context handle {ctxHandle}");
                        // Reshape indexed inputs into the WIT
                        // named-tensor shape using the
                        // synthetic-name convention.
                        var named = new List<NamedTensor>(state.Inputs.Count);
                        foreach (var kv in state.Inputs)
                            named.Add(new NamedTensor(kv.Key.ToString(), kv.Value));
                        var outputs = MemoryDiagnostics.RunWithDiagnostics(
                            state.Context, named);
                        state.Outputs.Clear();
                        for (int i = 0; i < outputs.Count; i++)
                        {
                            // WITX guests retrieve outputs by
                            // index; assume backend returns
                            // them in input-order or with
                            // numeric names. If the name is a
                            // valid uint, use it; otherwise
                            // fall back to the position.
                            uint idx = uint.TryParse(outputs[i].Name, out var n)
                                ? n : (uint)i;
                            state.Outputs[idx] = outputs[i].Tensor;
                        }
                        state.HasComputed = true;
                        return 0;
                    }
                    catch (WasiNNException e)
                    {
                        return (int)(ushort)e.Code.ToWitx();
                    }
                });
        }

        // get_output(context: graph_execution_context, index: u32,
        //            out_buffer: ptr<u8>, out_buffer_max_size: buffer_size)
        //   -> expected<buffer_size, nn_errno>
        // retArea: u16 errno @0, written-byte-count u32 at +4.
        private static void BindGetOutput(WasmRuntime runtime, WasiNNHost host)
        {
            runtime.BindHostFunction<Func<ExecContext, int, int, int, int, int, int>>(
                (Module, "get_output"),
                (ctx, ctxHandle, index, outPtr, outMax, retArea) =>
                {
                    try
                    {
                        if (!host.WitxContextState.TryGetValue(ctxHandle, out var state))
                            throw new WasiNNException(
                                ErrorCode.InvalidArgument,
                                $"invalid execution-context handle {ctxHandle}");
                        if (!state.HasComputed)
                            throw new WasiNNException(
                                ErrorCode.InvalidArgument,
                                "compute has not been called on this context");
                        if (!state.Outputs.TryGetValue((uint)index, out var t))
                            throw new WasiNNException(
                                ErrorCode.InvalidArgument,
                                $"no output at index {index}");
                        if (outMax < t.Data.Length)
                            throw new WasiNNException(
                                ErrorCode.TooLarge,
                                $"output buffer too small: {outMax} < {t.Data.Length}");
                        var span = t.Data.Span;
                        var mem = ctx.Memory();
                        for (int i = 0; i < span.Length; i++)
                            mem[outPtr + i] = span[i];
                        WriteWitxOk(ctx, retArea);
                        ctx.WriteI32LE(retArea + 4, span.Length);
                        return 0;
                    }
                    catch (WasiNNException e)
                    {
                        WriteWitxErr(ctx, retArea, e.Code);
                        return 0;
                    }
                });
        }

        // ---- canonical-ABI lift helpers ----

        private static IReadOnlyList<ReadOnlyMemory<byte>> LiftBuilderArray(
            ExecContext ctx, int arrPtr, int arrLen)
        {
            var mem = ctx.Memory();
            var builders = new List<ReadOnlyMemory<byte>>(arrLen);
            // Each element is (ptr, len) — 8 bytes per element.
            // Wrap guest memory directly; per IBackend.LoadGraph
            // contract the backend consumes the bytes before
            // returning, so the view is valid for the call's
            // duration. Saves the host-side copy that earlier
            // versions paid up-front — for multi-MB ONNX (the
            // typical WITX case) that's measurable on cold
            // load. WITX guests can't actually pass GB-scale
            // GGUFs since the legacy ABI lacks ggml encoding,
            // but the same shape applies to large ONNX/TF.
            for (int i = 0; i < arrLen; i++)
            {
                int elemBase = arrPtr + i * 8;
                int ptr = ReadI32LE(mem, elemBase);
                int len = ReadI32LE(mem, elemBase + 4);
                builders.Add(len == 0
                    ? ReadOnlyMemory<byte>.Empty
                    : new ReadOnlyMemory<byte>(mem, ptr, len));
            }
            return builders;
        }

        private static Tensor LiftTensor(ExecContext ctx,
            int dimsPtr, int dimsLen, TensorType type, int dataPtr, int dataLen)
        {
            var mem = ctx.Memory();
            var dims = new uint[dimsLen];
            for (int i = 0; i < dimsLen; i++)
                dims[i] = (uint)ReadI32LE(mem, dimsPtr + i * 4);
            var data = new byte[dataLen];
            if (dataLen > 0) Array.Copy(mem, dataPtr, data, 0, dataLen);
            return new Tensor(dims, type, data);
        }

        private static string ReadString(ExecContext ctx, int ptr, int len)
        {
            if (len == 0) return string.Empty;
            return Encoding.UTF8.GetString(ctx.Memory(), ptr, len);
        }

        // ---- enum mappings (WITX bytes → typed enums) ----

        private static GraphEncoding ParseWitxEncoding(int wireValue)
        {
            // WITX graph_encoding (line 41-49) lacks GGML —
            // wire bytes 0..5 are openvino..autodetect. WIT's
            // GGML at index 5 doesn't exist here; if a guest
            // passes 6 we surface invalid-encoding rather than
            // silently routing.
            return wireValue switch
            {
                0 => GraphEncoding.OpenVINO,
                1 => GraphEncoding.ONNX,
                2 => GraphEncoding.TensorFlow,
                3 => GraphEncoding.PyTorch,
                4 => GraphEncoding.TensorFlowLite,
                5 => GraphEncoding.Autodetect,
                _ => throw new WasiNNException(
                    ErrorCode.InvalidEncoding,
                    $"unknown WITX graph_encoding {wireValue}"),
            };
        }

        private static ExecutionTarget ParseWitxTarget(int wireValue)
        {
            return wireValue switch
            {
                0 => ExecutionTarget.CPU,
                1 => ExecutionTarget.GPU,
                2 => ExecutionTarget.TPU,
                _ => throw new WasiNNException(
                    ErrorCode.InvalidArgument,
                    $"unknown WITX execution_target {wireValue}"),
            };
        }

        private static TensorType ParseWitxTensorType(byte wireValue)
        {
            // WITX tensor_type (line 19-28): f16, f32, f64, u8,
            // i32, i64. No BF16. Map onto the WIT enum's spread
            // (which interleaves BF16 between FP64 and U8).
            return wireValue switch
            {
                0 => TensorType.FP16,
                1 => TensorType.FP32,
                2 => TensorType.FP64,
                3 => TensorType.U8,
                4 => TensorType.I32,
                5 => TensorType.I64,
                _ => throw new WasiNNException(
                    ErrorCode.InvalidArgument,
                    $"unknown WITX tensor_type {wireValue}"),
            };
        }

        // ---- retArea writers ----

        // expected<T, nn_errno> on success: errno=0 in the first
        // u16 of the retArea. The actual T then sits at offset
        // matching the result's payload alignment (max(2, align_of<T>)).
        // For graph / context / buffer_size returns the payload
        // alignment is 4, so the i32 sits at +4. Only the four
        // data-returning calls use retArea — set_input and compute
        // collapse the empty-Ok / errno case onto the function's
        // i32 return value (no retArea pointer in their wire form).
        private static void WriteWitxOk(ExecContext ctx, int retArea)
        {
            ctx.WriteI32LE(retArea, 0);
        }

        private static void WriteWitxErr(ExecContext ctx, int retArea, ErrorCode code)
        {
            // Witx errno discriminant goes in the first u16. We
            // write 32 bits but only the low 16 are meaningful;
            // the high 16 is reserved padding (some compilers
            // would zero-fill it, others leave it undefined —
            // we explicitly zero).
            ctx.WriteI32LE(retArea, (int)(ushort)code.ToWitx());
        }

        private static int ReadI32LE(byte[] mem, int offset)
        {
            return mem[offset] | (mem[offset + 1] << 8) |
                   (mem[offset + 2] << 16) | (mem[offset + 3] << 24);
        }
    }
}
