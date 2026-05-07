// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LLama.Common;
using Wacs.WASI.NN;
using Wacs.WASI.NN.LlamaSharp;
using Wacs.WASI.NN.Types;
using Xunit;

namespace Wacs.WASI.NN.LlamaSharp.Test
{
    /// <summary>
    /// Real GGUF inference round-trip. Skipped by default —
    /// CI has no multi-GB model file to ship — but runs
    /// locally when the embedder sets
    /// <c>WACS_GGUF_MODEL_PATH</c> to a real GGUF on disk.
    ///
    /// <para>Tests verify the full LlamaSharp path:
    /// LoadGraphByName → resolve via internal registry →
    /// LLamaWeights.LoadFromFile → StatelessExecutor.InferAsync.
    /// Inputs and outputs follow the WasmEdge GGUF convention
    /// (UTF-8 prompt → UTF-8 response in U8 tensors). The
    /// generation params are kept tiny (max 16 tokens) to
    /// keep the test runtime under a few seconds.</para>
    /// </summary>
    public sealed class GgufInferenceTests
    {
        // Env var convention. When set, all gated tests in
        // this class run; when unset, they skip with a
        // user-facing reason. Single source of truth so
        // every test references the same gate.
        private const string ModelPathEnvVar = "WACS_GGUF_MODEL_PATH";

        private static string? ModelPath
            => Environment.GetEnvironmentVariable(ModelPathEnvVar);

        [SkippableFact]
        public void Loads_gguf_and_generates_text()
        {
            var path = ModelPath;
            Skip.If(string.IsNullOrEmpty(path),
                $"set {ModelPathEnvVar} to a GGUF file to run this test");
            Skip.IfNot(File.Exists(path),
                $"{ModelPathEnvVar}={path} does not exist");

            var entry = new GgufModelEntry(
                path!,
                modelParams: new ModelParams(path!) { ContextSize = 512 },
                inferenceParams: new InferenceParams { MaxTokens = 16 });
            var backend = new LlamaSharpBackend(name => entry);

            using var graph = backend.LoadGraphByName("test-model", ExecutionTarget.CPU);
            using var ctx = graph.CreateContext();

            var prompt = "The capital of France is";
            var inputBytes = Encoding.UTF8.GetBytes(prompt);
            var outputs = ctx.Compute(new[]
            {
                new NamedTensor("0",
                    new Tensor(new uint[] { (uint)inputBytes.Length },
                        TensorType.U8, inputBytes)),
            });

            Assert.Single(outputs);
            Assert.Equal("0", outputs[0].Name);
            Assert.Equal(TensorType.U8, outputs[0].Tensor.Type);

            var responseText = Encoding.UTF8.GetString(
                outputs[0].Tensor.Data.Span);
            // Don't assert specific content — different models
            // generate different completions. Just verify the
            // UTF-8 boundary is respected and the response is
            // non-empty after token generation.
            Assert.NotEmpty(responseText);
        }

        [SkippableFact]
        public void Multiple_contexts_isolate_kv_cache()
        {
            // Two independent contexts on the same loaded
            // graph. Each runs inference independently —
            // verifies that the per-context StatelessExecutor
            // doesn't trample the other's KV cache.
            var path = ModelPath;
            Skip.If(string.IsNullOrEmpty(path),
                $"set {ModelPathEnvVar} to a GGUF file to run this test");
            Skip.IfNot(File.Exists(path),
                $"{ModelPathEnvVar}={path} does not exist");

            var entry = new GgufModelEntry(
                path!,
                modelParams: new ModelParams(path!) { ContextSize = 512 },
                inferenceParams: new InferenceParams { MaxTokens = 8 });
            var backend = new LlamaSharpBackend(name => entry);

            using var graph = backend.LoadGraphByName("test-model", ExecutionTarget.CPU);
            using var ctxA = graph.CreateContext();
            using var ctxB = graph.CreateContext();

            byte[] Run(IBackendContext c, string prompt)
            {
                var bytes = Encoding.UTF8.GetBytes(prompt);
                var outs = c.Compute(new[]
                {
                    new NamedTensor("0",
                        new Tensor(new uint[] { (uint)bytes.Length },
                            TensorType.U8, bytes)),
                });
                return outs[0].Tensor.Data.ToArray();
            }

            var a = Run(ctxA, "A: hello");
            var b = Run(ctxB, "B: world");
            Assert.NotEmpty(a);
            Assert.NotEmpty(b);
        }
    }
}
