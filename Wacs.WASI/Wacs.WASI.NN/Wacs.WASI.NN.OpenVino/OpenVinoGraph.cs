// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using OpenVinoSharp;
using Wacs.WASI.NN;
// `OpenVinoSharp.Core` collides with the `Wacs.Core` namespace
// at name resolution time (the compiler sees Wacs.Core first
// because this assembly transitively references it). Alias the
// OpenVINO type to a name that can't collide.
using OvCore = OpenVinoSharp.Core;

namespace Wacs.WASI.NN.OpenVino
{
    /// <summary>
    /// Wraps a <see cref="Core"/> + <see cref="Model"/> +
    /// <see cref="CompiledModel"/> triple. OpenVINO requires the
    /// <see cref="Core"/> to outlive any model loaded through it
    /// and the model to outlive its compiled form, so the graph
    /// owns all three and disposes them in dependency order
    /// (compiled → model → core).
    ///
    /// <para>Per-graph state, not per-context. Multiple
    /// <see cref="OpenVinoContext"/>s minted from
    /// <see cref="CreateContext"/> each get their own
    /// <see cref="InferRequest"/> off the same compiled model.
    /// OpenVINO infer requests are not safe to invoke
    /// concurrently from multiple threads, but multiple infer
    /// requests off one compiled model are — fan out for
    /// parallelism via multiple contexts, not multiple
    /// <see cref="OpenVinoContext.Compute"/> calls on the same
    /// context.</para>
    /// </summary>
    internal sealed class OpenVinoGraph : IBackendGraph
    {
        // Internal so OpenVinoContext can mint InferRequests
        // without going through reflection or widening the SPI.
        internal CompiledModel Compiled { get; }
        private readonly OvCore _core;
        private readonly Model _model;
        private bool _disposed;

        public OpenVinoGraph(OvCore core, Model model, CompiledModel compiled)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            Compiled = compiled ?? throw new ArgumentNullException(nameof(compiled));
        }

        public IBackendContext CreateContext()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OpenVinoGraph));
            return new OpenVinoContext(this);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Compiled.Dispose();
            _model.Dispose();
            _core.Dispose();
        }
    }
}
