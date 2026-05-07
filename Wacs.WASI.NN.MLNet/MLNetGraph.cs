// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Microsoft.ML;
using Microsoft.ML.OnnxRuntime;
using Wacs.WASI.NN;

namespace Wacs.WASI.NN.MLNet
{
    /// <summary>
    /// Wraps an <see cref="InferenceSession"/> + the
    /// <see cref="SessionOptions"/> it was built with, and
    /// holds a back-reference to the owning
    /// <see cref="MLContext"/>. The MLContext isn't disposed
    /// here — the backend is the conceptual owner — but the
    /// reference is retained so contexts minted from this
    /// graph can reach pipeline-related services (logging,
    /// custom transformer registration) through it.
    /// </summary>
    internal sealed class MLNetGraph : IBackendGraph
    {
        internal MLContext MLContext { get; }
        internal InferenceSession Session { get; }
        private readonly SessionOptions _options;
        private bool _disposed;

        public MLNetGraph(MLContext mlContext, InferenceSession session, SessionOptions options)
        {
            MLContext = mlContext ?? throw new ArgumentNullException(nameof(mlContext));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public IBackendContext CreateContext()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MLNetGraph));
            return new MLNetContext(this);
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
