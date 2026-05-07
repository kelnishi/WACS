// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Microsoft.ML.OnnxRuntime;
using Wacs.WASI.NN;

namespace Wacs.WASI.NN.OnnxRuntime
{
    /// <summary>
    /// Wraps an <see cref="InferenceSession"/> + the
    /// <see cref="SessionOptions"/> instance it was built with.
    /// ORT requires the options to outlive the session, so the
    /// graph owns both and disposes them in dependency order
    /// (session first, then options).
    ///
    /// <para>Per-graph state, not per-context. Multiple
    /// <see cref="OnnxContext"/>s minted from
    /// <see cref="CreateContext"/> share this session — ORT's
    /// <c>Run</c> is reentrant and the model is stateless
    /// across calls, so concurrent inferences against one
    /// session are safe.</para>
    /// </summary>
    internal sealed class OnnxGraph : IBackendGraph
    {
        // Internal so OnnxContext can read it without going
        // through reflection or an accessor that complicates
        // the SPI surface.
        internal InferenceSession Session { get; }
        private readonly SessionOptions _options;
        private bool _disposed;

        public OnnxGraph(InferenceSession session, SessionOptions options)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public IBackendContext CreateContext()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OnnxGraph));
            return new OnnxContext(this);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Session.Dispose();
            _options.Dispose();
        }
    }
}
