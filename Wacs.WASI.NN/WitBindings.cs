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
using Wacs.WASI.NN.Resources;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN
{
    /// <summary>
    /// Component-model (WIT) bindings for
    /// <c>wasi:nn@0.2.0-rc-2024-10-28</c> — four interfaces:
    /// <c>tensor</c>, <c>graph</c>, <c>inference</c>,
    /// <c>errors</c>. Hand-rolled per
    /// <c>RandomBindings.cs</c>'s pattern; each method names
    /// its WIT shape in the comment above the
    /// <see cref="WasmRuntime.BindHostFunction{T}"/> call so
    /// the wire layout decisions are auditable inline.
    ///
    /// <para>Backend-side errors flow through
    /// <see cref="WasiNNException"/>; the per-method handlers
    /// catch it, allocate a fresh <see cref="ErrorResource"/>,
    /// register it in the error table, and write the result's
    /// Err discriminant + handle into the retArea. The Ok side
    /// allocates whatever resource the spec returns and writes
    /// the Ok discriminant + handle.</para>
    ///
    /// <para>Canonical-ABI shape facts used here:
    /// <list type="bullet">
    /// <item><c>result&lt;own&lt;X&gt;, own&lt;Y&gt;&gt;</c>:
    /// 8 bytes total (1 disc + 3 pad + 4 handle), align 4.</item>
    /// <item><c>result&lt;list&lt;_&gt;, own&lt;Y&gt;&gt;</c>:
    /// 12 bytes (1 disc + 3 pad + 4 ptr + 4 len), align 4.</item>
    /// <item><c>list&lt;named-tensor&gt;</c> element:
    /// <c>tuple&lt;string, own&lt;tensor&gt;&gt;</c> = 12 bytes
    /// (4 ptr + 4 len + 4 handle), align 4.</item>
    /// <item><c>list&lt;u32&gt;</c> for tensor-dimensions:
    /// element size 4, align 4.</item>
    /// <item><c>list&lt;u8&gt;</c> for tensor-data /
    /// graph-builder: element size 1, align 1.</item>
    /// </list></para>
    /// </summary>
    internal static class WitBindings
    {
        private const string TensorNs    = "wasi:nn/tensor@0.2.0-rc-2024-10-28";
        private const string GraphNs     = "wasi:nn/graph@0.2.0-rc-2024-10-28";
        private const string InferenceNs = "wasi:nn/inference@0.2.0-rc-2024-10-28";
        private const string ErrorsNs    = "wasi:nn/errors@0.2.0-rc-2024-10-28";

        public static void Bind(WasmRuntime runtime, WasiNNHost host)
        {
            var alloc = new Realloc(runtime);
            BindErrors(runtime, host, alloc);
            BindTensor(runtime, host, alloc);
            BindGraph(runtime, host, alloc);
            BindInference(runtime, host, alloc);
        }

        // ----------------------------------------------------
        //   wasi:nn/errors@0.2.0-rc-2024-10-28
        //     resource error {
        //       code: func() -> error-code;
        //       data: func() -> string;
        //     }
        // ----------------------------------------------------

        private static void BindErrors(WasmRuntime runtime, WasiNNHost host, Realloc alloc)
        {
            // [method]error.code(self: borrow<error>) -> error-code
            // Wire: Func<ExecContext, int, int> — handle in, u8 enum out
            // (canonical-ABI flat-form: enum returns as i32).
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (ErrorsNs, "[method]error.code"),
                (_, h) =>
                {
                    var err = (ErrorResource)host.Errors.Get(h);
                    return (int)(byte)err.Code;
                });

            // [method]error.data(self: borrow<error>) -> string
            // Wire: Action<ExecContext, int, int> — handle + retArea
            // (string lowered as ptr+len; retArea is 8 bytes).
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (ErrorsNs, "[method]error.data"),
                (ctx, h, retArea) =>
                {
                    var err = (ErrorResource)host.Errors.Get(h);
                    WriteString(ctx, alloc, retArea, err.Data);
                });

            // [resource-drop]error(self: own<error>)
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ErrorsNs, "[resource-drop]error"),
                (_, h) => host.Errors.Drop(h));
        }

        // ----------------------------------------------------
        //   wasi:nn/tensor@0.2.0-rc-2024-10-28
        //     resource tensor {
        //       constructor(dims: list<u32>, ty: tensor-type, data: list<u8>);
        //       dimensions: func() -> list<u32>;
        //       ty:         func() -> tensor-type;
        //       data:       func() -> list<u8>;
        //     }
        // ----------------------------------------------------

        private static void BindTensor(WasmRuntime runtime, WasiNNHost host, Realloc alloc)
        {
            // [constructor]tensor(dims_ptr, dims_len, ty, data_ptr, data_len) -> own<tensor>
            // Wire signature: 5 i32 in, 1 i32 out (handle).
            runtime.BindHostFunction<Func<ExecContext, int, int, int, int, int, int>>(
                (TensorNs, "[constructor]tensor"),
                (ctx, dimsPtr, dimsLen, ty, dataPtr, dataLen) =>
                {
                    var dims = ReadU32List(ctx, dimsPtr, dimsLen);
                    var data = ReadU8List(ctx, dataPtr, dataLen);
                    var tensor = new Tensor(dims, (TensorType)(byte)ty, data);
                    return host.Tensors.Allocate(new TensorResource(tensor));
                });

            // [method]tensor.dimensions(self) -> list<u32>
            // Wire: Action<ExecContext, int, int> — handle + retArea (8 bytes).
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TensorNs, "[method]tensor.dimensions"),
                (ctx, h, retArea) =>
                {
                    var t = ((TensorResource)host.Tensors.Get(h)).Value;
                    WriteU32List(ctx, alloc, retArea, t.Dimensions);
                });

            // [method]tensor.ty(self) -> tensor-type
            // Wire: Func<ExecContext, int, int> — handle in, u8 enum out.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (TensorNs, "[method]tensor.ty"),
                (_, h) =>
                {
                    var t = ((TensorResource)host.Tensors.Get(h)).Value;
                    return (int)(byte)t.Type;
                });

            // [method]tensor.data(self) -> list<u8>
            // Wire: Action<ExecContext, int, int> — handle + retArea (8 bytes).
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (TensorNs, "[method]tensor.data"),
                (ctx, h, retArea) =>
                {
                    var t = ((TensorResource)host.Tensors.Get(h)).Value;
                    WriteU8List(ctx, alloc, retArea, t.Data.Span);
                });

            // [resource-drop]tensor(self)
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (TensorNs, "[resource-drop]tensor"),
                (_, h) => host.Tensors.Drop(h));
        }

        // ----------------------------------------------------
        //   wasi:nn/graph@0.2.0-rc-2024-10-28
        //     load: func(builder: list<list<u8>>, encoding, target)
        //             -> result<own<graph>, own<error>>;
        //     load-by-name: func(name: string)
        //             -> result<own<graph>, own<error>>;
        //     resource graph {
        //       init-execution-context: func() -> result<own<ctx>, own<error>>;
        //     }
        // ----------------------------------------------------

        private static void BindGraph(WasmRuntime runtime, WasiNNHost host, Realloc alloc)
        {
            // load(builder_arr_ptr, builder_arr_len, encoding, target, retArea)
            //   -> result<own<graph>, own<error>>   (8-byte retArea, align 4)
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int, int>>(
                (GraphNs, "load"),
                (ctx, arrPtr, arrLen, encoding, target, retArea) =>
                {
                    try
                    {
                        var builders = ReadListOfU8List(ctx, arrPtr, arrLen);
                        var enc = (GraphEncoding)(byte)encoding;
                        var tgt = (ExecutionTarget)(byte)target;
                        var backend = host.ResolveBackend(enc);
                        var graph = backend.LoadGraph(builders, tgt);
                        var handle = host.Graphs.Allocate(graph);
                        WriteResultHandleOk(ctx, retArea, handle);
                    }
                    catch (WasiNNException e)
                    {
                        var errHandle = host.Errors.Allocate(new ErrorResource(e));
                        WriteResultHandleErr(ctx, retArea, errHandle);
                    }
                });

            // load-by-name(name_ptr, name_len, retArea)
            //   -> result<own<graph>, own<error>>
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (GraphNs, "load-by-name"),
                (ctx, namePtr, nameLen, retArea) =>
                {
                    try
                    {
                        var name = ReadUtf8String(ctx, namePtr, nameLen);
                        var graph = host.LoadGraphByNameDispatch(name);
                        var handle = host.Graphs.Allocate(graph);
                        WriteResultHandleOk(ctx, retArea, handle);
                    }
                    catch (WasiNNException e)
                    {
                        var errHandle = host.Errors.Allocate(new ErrorResource(e));
                        WriteResultHandleErr(ctx, retArea, errHandle);
                    }
                });

            // [method]graph.init-execution-context(self, retArea)
            //   -> result<own<graph-execution-context>, own<error>>
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (GraphNs, "[method]graph.init-execution-context"),
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
                        WriteResultHandleOk(ctx, retArea, ctxHandle);
                    }
                    catch (WasiNNException e)
                    {
                        var errHandle = host.Errors.Allocate(new ErrorResource(e));
                        WriteResultHandleErr(ctx, retArea, errHandle);
                    }
                });

            // [resource-drop]graph(self)
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (GraphNs, "[resource-drop]graph"),
                (_, h) => host.Graphs.Drop(h));
        }

        // ----------------------------------------------------
        //   wasi:nn/inference@0.2.0-rc-2024-10-28
        //     resource graph-execution-context {
        //       compute: func(inputs: list<named-tensor>)
        //                  -> result<list<named-tensor>, own<error>>;
        //     }
        //     where named-tensor = tuple<string, own<tensor>>
        // ----------------------------------------------------

        private static void BindInference(WasmRuntime runtime, WasiNNHost host, Realloc alloc)
        {
            // [method]graph-execution-context.compute(self, inputs_ptr, inputs_len, retArea)
            //   -> result<list<named-tensor>, own<error>>
            // retArea: 12 bytes — 1 disc + 3 pad + (4 ptr + 4 len) OR (4 err handle + 4 pad).
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (InferenceNs, "[method]graph-execution-context.compute"),
                (ctx, ctxHandle, inPtr, inLen, retArea) =>
                {
                    try
                    {
                        if (!host.Contexts.TryGet<IBackendContext>(ctxHandle, out var bctx) || bctx == null)
                            throw new WasiNNException(
                                ErrorCode.InvalidArgument,
                                $"invalid execution-context handle {ctxHandle}");
                        var inputs = ReadNamedTensorList(ctx, host, inPtr, inLen);
                        var outputs = bctx.Compute(inputs);
                        WriteResultNamedTensorListOk(ctx, host, alloc, retArea, outputs);
                    }
                    catch (WasiNNException e)
                    {
                        var errHandle = host.Errors.Allocate(new ErrorResource(e));
                        WriteResultListErr(ctx, retArea, errHandle);
                    }
                });

            // [resource-drop]graph-execution-context(self)
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (InferenceNs, "[resource-drop]graph-execution-context"),
                (_, h) => host.Contexts.Drop(h));
        }

        // ====================================================
        //   Canonical-ABI lift / lower helpers
        // ====================================================

        // Read list<u32> as a host uint[]. Element size 4, align 4.
        private static uint[] ReadU32List(ExecContext ctx, int ptr, int len)
        {
            var arr = new uint[len];
            var mem = ctx.Memory();
            for (int i = 0; i < len; i++)
                arr[i] = (uint)ctx.ReadI32LE(ptr + i * 4);
            return arr;
        }

        // Read list<u8> as a host byte[]. Element size 1, align 1.
        // Materializes — guest memory.grow can move pages between
        // this lift and any later host call into native code.
        private static byte[] ReadU8List(ExecContext ctx, int ptr, int len)
        {
            var arr = new byte[len];
            if (len > 0) Array.Copy(ctx.Memory(), ptr, arr, 0, len);
            return arr;
        }

        // Read list<list<u8>> — outer (ptr, len) where each
        // element is itself an 8-byte (inner_ptr, inner_len)
        // pair. Yields a host List<ReadOnlyMemory<byte>> sized
        // for the IBackend.LoadGraph signature.
        private static IReadOnlyList<ReadOnlyMemory<byte>> ReadListOfU8List(
            ExecContext ctx, int arrPtr, int arrLen)
        {
            var list = new List<ReadOnlyMemory<byte>>(arrLen);
            for (int i = 0; i < arrLen; i++)
            {
                int elemBase = arrPtr + i * 8;
                int innerPtr = ctx.ReadI32LE(elemBase);
                int innerLen = ctx.ReadI32LE(elemBase + 4);
                // Materialize each builder buffer into a host
                // array — same memory.grow safety reasoning as
                // ReadU8List. Phase 4 introduces a pinning lift
                // that hands out a ReadOnlyMemory<byte> view
                // over the live page for the duration of the
                // backend's LoadGraph call.
                var buf = new byte[innerLen];
                if (innerLen > 0)
                    Array.Copy(ctx.Memory(), innerPtr, buf, 0, innerLen);
                list.Add(buf);
            }
            return list;
        }

        // Read list<named-tensor> where named-tensor =
        // tuple<string, own<tensor>>. Element size 12 bytes,
        // align 4: (str_ptr@0, str_len@4, tensor_handle@8).
        private static IReadOnlyList<NamedTensor> ReadNamedTensorList(
            ExecContext ctx, WasiNNHost host, int arrPtr, int arrLen)
        {
            var list = new List<NamedTensor>(arrLen);
            for (int i = 0; i < arrLen; i++)
            {
                int elemBase = arrPtr + i * 12;
                int strPtr = ctx.ReadI32LE(elemBase);
                int strLen = ctx.ReadI32LE(elemBase + 4);
                int tensorHandle = ctx.ReadI32LE(elemBase + 8);
                var name = ReadUtf8String(ctx, strPtr, strLen);
                if (!host.Tensors.TryGet<TensorResource>(tensorHandle, out var tres) || tres == null)
                    throw new WasiNNException(
                        ErrorCode.InvalidArgument,
                        $"named-tensor '{name}': invalid tensor handle {tensorHandle}");
                list.Add(new NamedTensor(name, tres.Value));
            }
            return list;
        }

        private static string ReadUtf8String(ExecContext ctx, int ptr, int len)
        {
            if (len == 0) return string.Empty;
            return Encoding.UTF8.GetString(ctx.Memory(), ptr, len);
        }

        // Allocate guest memory + write the bytes; emit (ptr, len)
        // into retArea. Used for tensor.data, tensor.dimensions
        // (list<u32> with stride 4), error.data (string).
        private static void WriteU8List(ExecContext ctx, Realloc alloc,
            int retArea, ReadOnlySpan<byte> bytes)
        {
            int ptr = bytes.Length == 0 ? 0 : alloc.Allocate(1, bytes.Length);
            if (bytes.Length > 0)
            {
                var mem = ctx.Memory();
                for (int i = 0; i < bytes.Length; i++)
                    mem[ptr + i] = bytes[i];
            }
            ctx.WriteI32LE(retArea, ptr);
            ctx.WriteI32LE(retArea + 4, bytes.Length);
        }

        private static void WriteU32List(ExecContext ctx, Realloc alloc,
            int retArea, uint[] values)
        {
            int byteLen = values.Length * 4;
            int ptr = byteLen == 0 ? 0 : alloc.Allocate(4, byteLen);
            for (int i = 0; i < values.Length; i++)
                ctx.WriteI32LE(ptr + i * 4, (int)values[i]);
            ctx.WriteI32LE(retArea, ptr);
            ctx.WriteI32LE(retArea + 4, values.Length);
        }

        private static void WriteString(ExecContext ctx, Realloc alloc,
            int retArea, string s)
        {
            // String is just list<u8> in the canonical ABI's
            // utf8 encoding. (Per-byte length, not codepoint.)
            var bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
            WriteU8List(ctx, alloc, retArea, bytes);
        }

        // result<own<X>, own<Y>>: 8-byte retArea, align 4.
        // Layout: disc@0 (1 byte + 3 pad), handle@4.
        private static void WriteResultHandleOk(ExecContext ctx, int retArea, int handle)
        {
            var mem = ctx.Memory();
            mem[retArea] = 0;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            ctx.WriteI32LE(retArea + 4, handle);
        }

        private static void WriteResultHandleErr(ExecContext ctx, int retArea, int errHandle)
        {
            var mem = ctx.Memory();
            mem[retArea] = 1;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            ctx.WriteI32LE(retArea + 4, errHandle);
        }

        // result<list<named-tensor>, own<error>>: 12-byte retArea.
        // Ok layout: disc@0 + 3 pad + ptr@4 + len@8.
        // Err layout: disc@0 + 3 pad + err_handle@4 + 4 pad.
        private static void WriteResultNamedTensorListOk(
            ExecContext ctx, WasiNNHost host, Realloc alloc, int retArea,
            IReadOnlyList<NamedTensor> outputs)
        {
            // Allocate the 12-byte tuple array in guest memory,
            // populate each element, then write the result Ok
            // header pointing at that array.
            int byteLen = outputs.Count * 12;
            int arrPtr = byteLen == 0 ? 0 : alloc.Allocate(4, byteLen);
            for (int i = 0; i < outputs.Count; i++)
            {
                var nt = outputs[i];
                // Lower the tuple<string, own<tensor>> into the
                // 12-byte slot. String first via cabi_realloc;
                // then mint a tensor handle from a fresh
                // TensorResource.
                var nameBytes = Encoding.UTF8.GetBytes(nt.Name);
                int strPtr = nameBytes.Length == 0 ? 0
                    : alloc.Allocate(1, nameBytes.Length);
                if (nameBytes.Length > 0)
                {
                    var mem = ctx.Memory();
                    for (int j = 0; j < nameBytes.Length; j++)
                        mem[strPtr + j] = nameBytes[j];
                }
                int tensorHandle = host.Tensors.Allocate(new TensorResource(nt.Tensor));
                int slot = arrPtr + i * 12;
                ctx.WriteI32LE(slot, strPtr);
                ctx.WriteI32LE(slot + 4, nameBytes.Length);
                ctx.WriteI32LE(slot + 8, tensorHandle);
            }
            // Result header
            var memHdr = ctx.Memory();
            memHdr[retArea] = 0;
            memHdr[retArea + 1] = 0;
            memHdr[retArea + 2] = 0;
            memHdr[retArea + 3] = 0;
            ctx.WriteI32LE(retArea + 4, arrPtr);
            ctx.WriteI32LE(retArea + 8, outputs.Count);
        }

        private static void WriteResultListErr(ExecContext ctx, int retArea, int errHandle)
        {
            var mem = ctx.Memory();
            mem[retArea] = 1;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            ctx.WriteI32LE(retArea + 4, errHandle);
            // Err side has no second i32 payload; the trailing
            // 4 bytes of the 12-byte retArea are spec-undefined
            // but we zero them for determinism.
            ctx.WriteI32LE(retArea + 8, 0);
        }
    }
}
