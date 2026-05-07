// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using LLama;
using LLama.Common;
using Wacs.WASI.NN;

namespace Wacs.WASI.NN.LlamaSharp
{
    /// <summary>
    /// Loaded GGUF weights + the model params they were
    /// loaded with. <see cref="LLamaWeights"/> owns the
    /// llama.cpp native model handle; disposal here releases
    /// the gigabyte-scale memory mapping. Multiple contexts
    /// minted from one graph share the weights but each
    /// context keeps its own <see cref="StatelessExecutor"/>
    /// for KV-cache isolation across concurrent inferences.
    /// </summary>
    internal sealed class LlamaSharpGraph : IBackendGraph
    {
        internal LLamaWeights Weights { get; }
        internal ModelParams ModelParams { get; }
        internal InferenceParams DefaultInferenceParams { get; }
        private bool _disposed;

        public LlamaSharpGraph(
            LLamaWeights weights,
            ModelParams modelParams,
            InferenceParams? inferenceParams)
        {
            Weights = weights ?? throw new ArgumentNullException(nameof(weights));
            ModelParams = modelParams ?? throw new ArgumentNullException(nameof(modelParams));
            // Default inference params: enough for short Q&A
            // responses. Embedders override per-model via the
            // GgufModelEntry.InferenceParams field.
            DefaultInferenceParams = inferenceParams ?? new InferenceParams
            {
                MaxTokens = 256,
            };
        }

        public IBackendContext CreateContext()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LlamaSharpGraph));
            return new LlamaSharpContext(this);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Weights.Dispose();
        }
    }
}
