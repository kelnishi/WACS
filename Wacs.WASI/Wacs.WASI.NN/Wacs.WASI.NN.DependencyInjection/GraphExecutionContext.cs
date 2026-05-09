// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.NN.Types;
using Nn = Wacs.WASI.NN.Nn;

namespace Wacs.WASI.NN.DependencyInjection
{
    /// <summary>
    /// Host representation of
    /// <c>wasi:nn/inference.graph-execution-context</c>. Wraps an
    /// <see cref="IBackendContext"/>; per-call state (input
    /// tensors uploaded, output tensors materialized) lives in
    /// the backend's instance.
    ///
    /// <para>The <see cref="Compute"/> method bridges between the
    /// generated <see cref="Nn.ITensor"/> resources (handles in
    /// the wasi-nn resource tables) and the backend SPI's
    /// <see cref="Wacs.WASI.NN.Types.NamedTensor"/> values: input
    /// tensors are read out of their host instances and packaged
    /// for the backend; output tensors come back as fresh
    /// <see cref="Tensor"/> instances ready to lower to new
    /// resource handles.</para>
    /// </summary>
    public sealed class GraphExecutionContext : Nn.IGraphExecutionContext,
        IDisposable
    {
        private readonly IBackendContext _backendCtx;
        private bool _disposed;

        public GraphExecutionContext(IBackendContext backendCtx)
        {
            _backendCtx = backendCtx
                ?? throw new ArgumentNullException(nameof(backendCtx));
        }

        public Result<(string, Nn.ITensor)[], Nn.IError> Compute(
            (string, Nn.ITensor)[] inputs)
        {
            if (_disposed)
                return Result<(string, Nn.ITensor)[], Nn.IError>.FromErr(
                    new Error(Nn.ErrorCode.RuntimeError,
                        "graph-execution-context has been disposed"));
            try
            {
                var bridged = new NamedTensor[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                {
                    var name = inputs[i].Item1;
                    var t = inputs[i].Item2;
                    var dims = t.Dimensions();
                    var ty = (TensorType)(int)t.Ty();
                    var data = t.Data();
                    bridged[i] = new NamedTensor(name,
                        new Wacs.WASI.NN.Types.Tensor(dims, ty, data));
                }

                var outputs = _backendCtx.Compute(bridged);
                var result = new (string, Nn.ITensor)[outputs.Count];
                for (int i = 0; i < outputs.Count; i++)
                {
                    var nt = outputs[i];
                    var dims = nt.Tensor.Dimensions;
                    var nnTy = (Nn.TensorType)(int)nt.Tensor.Type;
                    // ReadOnlyMemory<byte> from the backend may
                    // alias a buffer the backend reuses across
                    // calls; copy out so the resource handle owns
                    // its data independent of the next compute.
                    var data = nt.Tensor.Data.ToArray();
                    result[i] = (nt.Name,
                        Tensor.FromBackendOutput(dims, nnTy, data));
                }
                return Result<(string, Nn.ITensor)[], Nn.IError>.FromOk(result);
            }
            catch (Types.WasiNNException ex)
            {
                return Result<(string, Nn.ITensor)[], Nn.IError>.FromErr(
                    Error.FromException(ex));
            }
            catch (Exception ex)
            {
                return Result<(string, Nn.ITensor)[], Nn.IError>.FromErr(
                    new Error(Nn.ErrorCode.RuntimeError, ex.Message));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _backendCtx.Dispose();
        }
    }
}
