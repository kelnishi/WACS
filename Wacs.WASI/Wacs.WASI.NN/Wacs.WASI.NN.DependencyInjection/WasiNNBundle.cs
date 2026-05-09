// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using NnGraph = Wacs.WASI.NN.Nn;

namespace Wacs.WASI.NN.DependencyInjection
{
    /// <summary>
    /// Aggregate over the stateless WASI-NN host interfaces shipped
    /// by <c>Wacs.WASI.NN</c>. The transpiler's direct-linked import
    /// path lowers each guest <c>call $import</c> on
    /// <c>wasi:nn/graph#load</c> /
    /// <c>wasi:nn/graph#load-by-name</c> into inline IL that loads
    /// the generated component class's <c>_bundle</c> field,
    /// dereferences <see cref="GraphFuncs"/>, and invokes it
    /// directly — no <see cref="System.Delegate"/> hop, no
    /// <c>WasmRuntime.BindHostFunction</c> table lookup.
    ///
    /// <para>Symmetric with <c>WasiPreview2Bundle</c>. Only
    /// stateless impls live here. The resource interfaces
    /// (<c>ITensor</c>, <c>IGraph</c>, <c>IGraphExecutionContext</c>,
    /// <c>IError</c>) are stateful — instantiated per-resource as
    /// the guest mints handles. Their concrete implementations are
    /// the next chunk of work (Phase C step 4 follow-up); until they
    /// land, the transpiler-direct-link path covers `graph.load` /
    /// `graph.load-by-name` only, and resource methods on the
    /// returned handles still flow through the
    /// <c>WitBindings.BindHostFunction</c> path the interpreter
    /// uses.</para>
    /// </summary>
    public sealed class WasiNNBundle
    {
        public NnGraph.IGraphFuncs GraphFuncs { get; }

        public WasiNNBundle(NnGraph.IGraphFuncs graphFuncs)
        {
            GraphFuncs = graphFuncs ?? throw new ArgumentNullException(
                nameof(graphFuncs));
        }
    }
}
