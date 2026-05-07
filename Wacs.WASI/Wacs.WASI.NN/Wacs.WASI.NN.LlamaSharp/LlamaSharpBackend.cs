// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using LLama;
using LLama.Common;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN.LlamaSharp
{
    /// <summary>
    /// LlamaSharp / llama.cpp backend for
    /// <see cref="GraphEncoding.GGML"/> on the WasmEdge GGUF
    /// convention:
    /// <list type="bullet">
    /// <item>Models are loaded via <c>graph.load-by-name</c>;
    /// the embedder configures
    /// <see cref="WasiNNConfiguration.LoadByNameBackend"/> =
    /// this instance and provides a name → model entry map.</item>
    /// <item>Inference inputs are <see cref="TensorType.U8"/>
    /// tensors whose bytes are the UTF-8 prompt; outputs are
    /// <see cref="TensorType.U8"/> tensors whose bytes are the
    /// UTF-8 generated response.</item>
    /// <item>Multi-GB model files are referenced by file path
    /// (not byte buffers) — passing a GGUF through canonical-
    /// ABI lift would force a multi-GB host copy. The
    /// byte-buffer <see cref="LoadGraph"/> path therefore
    /// rejects with <see cref="ErrorCode.UnsupportedOperation"/>;
    /// embedders MUST go through <c>load-by-name</c>.</item>
    /// </list>
    ///
    /// <para>The backend's registry is a
    /// <c>Func&lt;string, GgufModelEntry?&gt;</c> the embedder
    /// supplies. Returning null signals "name not found" and
    /// the binding lifts that to <see cref="ErrorCode.NotFound"/>.
    /// Each entry carries the model file path plus per-name
    /// <see cref="ModelParams"/> + <see cref="InferenceParams"/>
    /// so different models can be tuned independently.</para>
    /// </summary>
    public sealed class LlamaSharpBackend : IBackend
    {
        private readonly Func<string, GgufModelEntry?> _registry;

        /// <summary>
        /// Construct with an explicit name → entry lookup. The
        /// callback is invoked once per <c>load-by-name</c>
        /// call; embedders typically wrap a
        /// <see cref="IDictionary{TKey,TValue}"/> or read from
        /// a config file.
        /// </summary>
        public LlamaSharpBackend(Func<string, GgufModelEntry?> registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// Convenience: build a backend from a static name → path
        /// table with default <see cref="ModelParams"/> /
        /// <see cref="InferenceParams"/>. Useful for the simple
        /// "drop a few GGUFs in a directory and serve them"
        /// embedder flow.
        /// </summary>
        public static LlamaSharpBackend FromPaths(IDictionary<string, string> namesToPaths)
        {
            if (namesToPaths == null)
                throw new ArgumentNullException(nameof(namesToPaths));
            return new LlamaSharpBackend(name =>
                namesToPaths.TryGetValue(name, out var path)
                    ? new GgufModelEntry(path)
                    : (GgufModelEntry?)null);
        }

        public IReadOnlyCollection<GraphEncoding> SupportedEncodings { get; }
            = new[] { GraphEncoding.GGML };

        public IBackendGraph LoadGraph(
            IReadOnlyList<ReadOnlyMemory<byte>> builders,
            ExecutionTarget target)
        {
            // The byte-buffer path is unsupported — see class
            // doc. A GGUF passed through canonical-ABI lift
            // would force a multi-GB host copy on every load,
            // even when the guest sourced the bytes from a
            // local file the host could have opened directly.
            throw new WasiNNException(
                ErrorCode.UnsupportedOperation,
                "LlamaSharpBackend does not support graph.load with "
                + "byte buffers — GGUF models are gigabyte-scale and "
                + "must be referenced by name. Configure "
                + "WasiNNConfiguration.LoadByNameBackend = this "
                + "instance and use graph.load-by-name to resolve "
                + "models through the registry.");
        }

        public IBackendGraph LoadGraphByName(string name, ExecutionTarget target)
        {
            if (target == ExecutionTarget.TPU)
                throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    "LlamaSharpBackend does not support TPU; configure "
                    + "GpuLayerCount on the GgufModelEntry's ModelParams "
                    + "if GPU offload is desired.");

            var entry = _registry(name)
                ?? throw new WasiNNException(
                    ErrorCode.NotFound,
                    $"GGUF model '{name}' not registered with LlamaSharpBackend");

            try
            {
                // ModelParams owns the file path + load-time
                // params (context size, GPU layers, batch size,
                // etc.). The entry's ModelParams takes precedence
                // over the file-path-only convenience ctor.
                var modelParams = entry.ModelParams ?? new ModelParams(entry.FilePath);
                var weights = LLamaWeights.LoadFromFile(modelParams);
                return new LlamaSharpGraph(weights, modelParams, entry.InferenceParams);
            }
            catch (WasiNNException) { throw; }
            catch (Exception ex)
            {
                throw new WasiNNException(
                    ErrorCode.RuntimeError,
                    $"Failed to load GGUF '{name}' from '{entry.FilePath}': {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }
        }
    }

    /// <summary>
    /// Registry entry for one GGUF model. Carries the file
    /// path plus optional per-model
    /// <see cref="LLama.Common.ModelParams"/> /
    /// <see cref="LLama.Common.InferenceParams"/> so each
    /// model can be tuned independently (context size, GPU
    /// layer count, sampling settings, anti-prompts, etc.).
    /// </summary>
    public sealed class GgufModelEntry
    {
        public string FilePath { get; }
        public ModelParams? ModelParams { get; }
        public InferenceParams? InferenceParams { get; }

        public GgufModelEntry(
            string filePath,
            ModelParams? modelParams = null,
            InferenceParams? inferenceParams = null)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            ModelParams = modelParams;
            InferenceParams = inferenceParams;
        }
    }
}
