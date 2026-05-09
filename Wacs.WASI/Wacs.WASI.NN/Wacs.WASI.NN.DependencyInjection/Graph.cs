// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Nn = Wacs.WASI.NN.Nn;

namespace Wacs.WASI.NN.DependencyInjection
{
    /// <summary>
    /// Host representation of <c>wasi:nn/graph.graph</c>. Wraps
    /// an <see cref="IBackendGraph"/> minted by
    /// <see cref="GraphFuncsImpl.Load"/> /
    /// <see cref="GraphFuncsImpl.LoadByName"/>; subsequent
    /// <see cref="InitExecutionContext"/> calls forward through
    /// to <see cref="IBackendGraph.CreateContext"/>.
    ///
    /// <para>Owns the backend graph resource — disposed when the
    /// guest's <c>resource.drop</c> for this graph fires.
    /// Multiple <c>graph-execution-context</c>s minted from one
    /// graph share the underlying weights; each context holds its
    /// own per-call state.</para>
    /// </summary>
    public sealed class Graph : Nn.IGraph, IDisposable
    {
        internal IBackendGraph BackendGraph { get; }
        private bool _disposed;

        public Graph(IBackendGraph backendGraph)
        {
            BackendGraph = backendGraph
                ?? throw new ArgumentNullException(nameof(backendGraph));
        }

        public Result<Nn.IGraphExecutionContext, Nn.IError>
            InitExecutionContext()
        {
            try
            {
                if (_disposed)
                    return Result<Nn.IGraphExecutionContext, Nn.IError>.FromErr(
                        new Error(Nn.ErrorCode.RuntimeError,
                            "graph has been disposed"));
                var backendCtx = BackendGraph.CreateContext();
                return Result<Nn.IGraphExecutionContext, Nn.IError>.FromOk(
                    new GraphExecutionContext(backendCtx));
            }
            catch (Types.WasiNNException ex)
            {
                return Result<Nn.IGraphExecutionContext, Nn.IError>.FromErr(
                    Error.FromException(ex));
            }
            catch (Exception ex)
            {
                return Result<Nn.IGraphExecutionContext, Nn.IError>.FromErr(
                    new Error(Nn.ErrorCode.RuntimeError, ex.Message));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            BackendGraph.Dispose();
        }
    }
}
