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
    /// Placeholder <see cref="Nn.IGraph"/> returned by
    /// <see cref="GraphFuncsImpl"/>. Holds a reference to the
    /// backend's loaded graph so the resource handle round-trips
    /// through the canonical-ABI lift; calling
    /// <see cref="InitExecutionContext"/> traps with a clear
    /// message because the resource-method-direct-link path isn't
    /// wired yet (Phase C step 4 follow-up).
    ///
    /// <para>The interpreter binding (<c>WitBindings.Bind</c>) DOES
    /// implement <c>graph.[method]graph.init-execution-context</c>
    /// — guests dispatching through that path work today. Only the
    /// transpiler-direct-link path passes through this stub.</para>
    /// </summary>
    internal sealed class GraphStub : Nn.IGraph
    {
        // Held so a future implementation can re-route directly
        // without going through the resource-handle bridge.
        internal IBackendGraph BackendGraph { get; }

        public GraphStub(IBackendGraph backendGraph)
        {
            BackendGraph = backendGraph
                ?? throw new ArgumentNullException(nameof(backendGraph));
        }

        public Result<Nn.IGraphExecutionContext, Nn.IError> InitExecutionContext()
        {
            return Result<Nn.IGraphExecutionContext, Nn.IError>.FromErr(
                new ErrorStub(Nn.ErrorCode.UnsupportedOperation,
                    "graph.init-execution-context is not yet wired through "
                    + "the transpiler-direct-link path. The interpreter "
                    + "WitBindings.Bind path implements it; route through "
                    + "the WasiNNHost.BindToRuntime path instead, or wait "
                    + "for the Phase C step 4 resource-impl PR."));
        }
    }

    /// <summary>
    /// Placeholder <see cref="Nn.IError"/>. Carries a code + data
    /// string from a <see cref="Types.WasiNNException"/> or an
    /// inline error in <see cref="GraphFuncsImpl"/>. Faithful to
    /// the WIT contract; lives here so the
    /// <c>Result&lt;_, IError&gt;</c> branches are constructible
    /// without requiring a separate concrete-class commit.
    /// </summary>
    internal sealed class ErrorStub : Nn.IError
    {
        private readonly Nn.ErrorCode _code;
        private readonly string _data;

        public ErrorStub(Nn.ErrorCode code, string data)
        {
            _code = code;
            _data = data ?? string.Empty;
        }

        public Nn.ErrorCode Code() => _code;
        public string Data() => _data;
    }
}
