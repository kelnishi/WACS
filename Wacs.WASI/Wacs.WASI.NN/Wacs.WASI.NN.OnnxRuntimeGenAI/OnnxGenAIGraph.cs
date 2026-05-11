// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using GenAI = Microsoft.ML.OnnxRuntimeGenAI;
using Wacs.WASI.NN;

namespace Wacs.WASI.NN.OnnxRuntimeGenAI
{
    /// <summary>
    /// Owns the GenAI <see cref="GenAI.Model"/> +
    /// <see cref="GenAI.Tokenizer"/> pair. Multiple contexts can
    /// be minted from the same graph; the underlying Model is
    /// thread-safe for read-only generation if the Generator
    /// instances are per-context (which they are — see
    /// <see cref="OnnxGenAIContext"/>).
    /// </summary>
    internal sealed class OnnxGenAIGraph : IBackendGraph
    {
        internal GenAI.Model Model { get; }
        internal GenAI.Tokenizer Tokenizer { get; }
        internal OnnxGenAIBackendOptions Options { get; }

        // Config is held when the graph was loaded through the
        // override-providers path (Config(dir) -> ClearProviders ->
        // Model(config)). GenAI's Model copies the Config state at
        // construction but the underlying native handle is shared
        // with the Model in some 0.x builds; keep the Config alive
        // for the Model's lifetime to be safe. Null when the graph
        // was loaded via the simpler Model(dir) path.
        private readonly GenAI.Config? _config;
        private bool _disposed;

        public OnnxGenAIGraph(
            GenAI.Model model,
            GenAI.Tokenizer tokenizer,
            OnnxGenAIBackendOptions options,
            GenAI.Config? config = null)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Tokenizer = tokenizer
                ?? throw new ArgumentNullException(nameof(tokenizer));
            Options = options
                ?? throw new ArgumentNullException(nameof(options));
            _config = config;
        }

        public IBackendContext CreateContext()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OnnxGenAIGraph));
            return new OnnxGenAIContext(this);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Tokenizer.Dispose();
            Model.Dispose();
            _config?.Dispose();
        }
    }
}
