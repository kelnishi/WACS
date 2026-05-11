// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using GenAI = Microsoft.ML.OnnxRuntimeGenAI;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN.OnnxRuntimeGenAI
{
    /// <summary>
    /// <see cref="IBackend"/> implementation that wraps
    /// <a href="https://github.com/microsoft/onnxruntime-genai">
    /// OnnxRuntime-GenAI</a> — Microsoft's generative-LLM runtime
    /// built on top of ONNX Runtime. Where the plain
    /// <c>WACS.WASI.NN.OnnxRuntime</c> backend exposes single-shot
    /// tensor-in / tensor-out inference, this backend exposes a
    /// first-class generation pipeline: tokenizer, KV cache,
    /// argmax / sampling, EOS detection — all driven from inside
    /// the host.
    ///
    /// <para>Selected by <c>graph.load-by-name</c>: model
    /// directories (containing <c>genai_config.json</c> +
    /// <c>tokenizer.json</c> + the ONNX file + weights) are
    /// registered through the constructor's resolver delegate.
    /// The <c>WasiNNOnnxGenAIBindable</c> adapter scans
    /// <c>$WACS_WASINN_GENAI_DIR</c> for first-level
    /// subdirectories that look like GenAI models (have
    /// <c>genai_config.json</c>) and registers each by directory
    /// name.</para>
    ///
    /// <para>Byte-loaded <c>graph.load</c> doesn't apply — GenAI
    /// needs a full directory layout, not a single ONNX file. The
    /// <see cref="LoadGraph"/> path traps with
    /// <see cref="ErrorCode.UnsupportedOperation"/> to make that
    /// explicit.</para>
    ///
    /// <para>Inside <see cref="OnnxGenAIContext.Compute"/> the
    /// shape of the call is determined by the named tensor's name:
    ///   <list type="bullet">
    ///     <item><c>"prompt"</c> (UTF-8 bytes) → full generation:
    ///       tokenize, generator loop with KV cache, detokenize.
    ///       Returns <c>"response"</c> (UTF-8 bytes). Best path
    ///       for chat / completion workloads — uses GenAI's
    ///       optimized decode kernels.</item>
    ///     <item><c>"input_ids"</c> (int64 tensor) → single
    ///       forward pass. Returns <c>"logits"</c> (FP32 tensor)
    ///       shaped <c>[batch, seq_len, vocab]</c>. Stateless: a
    ///       fresh generator state per call so the guest's own
    ///       decode loop sees the same semantics as plain ORT.
    ///       Mainly useful for guests that already drive the
    ///       autoregressive loop.</item>
    ///   </list>
    /// </para>
    ///
    /// <para>Hardware acceleration: on osx-arm64 the GenAI native
    /// dylib links directly against <c>CoreML.framework</c>. On
    /// Windows the <c>Microsoft.ML.OnnxRuntimeGenAI.DirectML</c>
    /// variant takes the same code path. On Linux the
    /// <c>.Cuda</c> variant does. Bound by the same op-coverage
    /// caveats as plain ORT — see the README for the per-model
    /// verification posture.</para>
    /// </summary>
    public sealed class OnnxGenAIBackend : IBackend, IDisposable
    {
        private readonly Func<string, string?> _resolver;
        private readonly OnnxGenAIBackendOptions _options;
        private bool _disposed;

        /// <summary>
        /// Create the backend with default options (read from env)
        /// and a name-to-directory resolver that returns the
        /// absolute path of the model directory for a given name,
        /// or <c>null</c> for "not found".
        /// </summary>
        public OnnxGenAIBackend(Func<string, string?> nameToDirectoryResolver)
            : this(nameToDirectoryResolver,
                OnnxGenAIBackendOptions.FromEnvironment())
        { }

        /// <summary>
        /// Create the backend with an explicit
        /// <see cref="OnnxGenAIBackendOptions"/> and resolver.
        /// </summary>
        public OnnxGenAIBackend(
            Func<string, string?> nameToDirectoryResolver,
            OnnxGenAIBackendOptions options)
        {
            _resolver = nameToDirectoryResolver
                ?? throw new ArgumentNullException(
                    nameof(nameToDirectoryResolver));
            _options = options
                ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Static factory matching the
        /// <c>WasiPreview2RuntimeScope</c> auto-wire shape
        /// (mirror of <c>TorchSharpBackend.FromPaths</c>):
        /// take a name→directory map, build a resolver that looks
        /// up by name. Lets the DI scope's reflective auto-wire
        /// construct the backend without needing to thread a
        /// <see cref="Func{T,TResult}"/> through Linq.Expressions
        /// IL emission.
        /// </summary>
        public static OnnxGenAIBackend FromDirectories(
            IDictionary<string, string> nameToDirectory)
        {
            if (nameToDirectory == null)
                throw new ArgumentNullException(nameof(nameToDirectory));
            var registry = new Dictionary<string, string>(
                nameToDirectory, StringComparer.OrdinalIgnoreCase);
            return new OnnxGenAIBackend(
                name => registry.TryGetValue(name, out var dir)
                    ? dir : null);
        }

        /// <summary>
        /// Exposes <see cref="GraphEncoding.ONNX"/> since the
        /// underlying model is in ONNX format (just packaged with
        /// GenAI's directory layout). The bindable wires this
        /// through <c>LoadByNameBackend</c> rather than
        /// <c>Backends[ONNX]</c> so it composes alongside the
        /// regular <c>OnnxBackend</c> — that one handles
        /// byte-loaded <c>graph.load</c>, this one handles
        /// <c>graph.load-by-name</c>.
        /// </summary>
        public IReadOnlyCollection<GraphEncoding> SupportedEncodings { get; }
            = new[] { GraphEncoding.ONNX };

        public IBackendGraph LoadGraph(
            IReadOnlyList<ReadOnlyMemory<byte>> builders,
            ExecutionTarget target)
        {
            throw new WasiNNException(
                ErrorCode.UnsupportedOperation,
                "OnnxGenAIBackend doesn't support byte-loaded graph.load. "
                + "GenAI needs a full model directory (genai_config.json + "
                + "tokenizer.json + model.onnx + weights). Use "
                + "graph.load-by-name with a name resolvable through "
                + "WACS_WASINN_GENAI_DIR or the constructor resolver.");
        }

        public IBackendGraph LoadGraphByName(string name, ExecutionTarget target)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OnnxGenAIBackend));
            if (target == ExecutionTarget.TPU)
                throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    "OnnxGenAIBackend doesn't support ExecutionTarget.TPU; "
                    + "the underlying ORT has no public TPU EP.");

            var dir = _resolver(name);
            if (dir == null || !Directory.Exists(dir))
                throw new WasiNNException(
                    ErrorCode.NotFound,
                    $"OnnxGenAIBackend: model '{name}' not found "
                    + "(no entry in the directory resolver / "
                    + "$WACS_WASINN_GENAI_DIR).");

            // genai_config.json is GenAI's required descriptor —
            // its presence is what distinguishes a GenAI model
            // directory from a vanilla ONNX dump.
            var configPath = Path.Combine(dir, "genai_config.json");
            if (!File.Exists(configPath))
                throw new WasiNNException(
                    ErrorCode.InvalidArgument,
                    $"OnnxGenAIBackend: '{dir}' lacks genai_config.json. "
                    + "Build the model with the onnxruntime-genai "
                    + "model_builder.py script or fetch a pre-built "
                    + "ONNX directory from Hugging Face.");

            GenAI.Config? config = null;
            GenAI.Model? model = null;
            GenAI.Tokenizer? tokenizer = null;
            try
            {
                // Load through Config so we can override the
                // execution-provider list. Pre-built HuggingFace
                // models often declare provider preferences (e.g.,
                // `nnapi` on Android-targeted exports) that aren't
                // compiled into every platform's GenAI native lib;
                // GenAI's Model(dir) ctor rejects the load with
                // "Unknown provider name '<X>'" when an unsupported
                // provider is hard-coded in genai_config.json.
                // ClearProviders strips the model-declared list so
                // GenAI picks the platform default — CPU on macOS,
                // matching the OnnxBackend's opt-in posture for
                // hardware acceleration. Embedders that want CoreML
                // / CUDA / DirectML opt in explicitly through
                // OnnxGenAIBackendOptions (follow-up).
                config = new GenAI.Config(dir);
                config.ClearProviders();
                model = new GenAI.Model(config);
                tokenizer = new GenAI.Tokenizer(model);
                // Config is owned by the Model after construction;
                // dispose-on-success would yank state from under the
                // generation loop. Keep it alive for the graph's
                // lifetime by transferring ownership.
                var owned = config;
                config = null;
                return new OnnxGenAIGraph(model, tokenizer, _options, owned);
            }
            catch (Exception ex)
            {
                tokenizer?.Dispose();
                model?.Dispose();
                config?.Dispose();
                throw new WasiNNException(
                    ErrorCode.RuntimeError,
                    $"OnnxGenAIBackend: failed to load model "
                    + $"'{name}' from '{dir}': {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
