// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LLama;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN.LlamaSharp
{
    /// <summary>
    /// One inference session against a
    /// <see cref="LlamaSharpGraph"/>. Holds a
    /// <see cref="StatelessExecutor"/> which carries its own
    /// per-context KV cache so concurrent contexts don't
    /// trample each other's mid-generation state.
    ///
    /// <para>WasmEdge GGUF convention applied here:
    /// inputs[0].Tensor is U8 with UTF-8 prompt bytes;
    /// outputs[0].Tensor is U8 with UTF-8 generated text.
    /// Input names are ignored (the convention uses index 0
    /// regardless); output is named "0" to match the WITX
    /// indexed-output flow that the host's WITX binding
    /// translates between.</para>
    /// </summary>
    internal sealed class LlamaSharpContext : IBackendContext
    {
        private readonly LlamaSharpGraph _graph;
        private readonly StatelessExecutor _executor;
        private bool _disposed;

        public LlamaSharpContext(LlamaSharpGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            // StatelessExecutor builds its own LLamaContext
            // internally on each call, so multiple concurrent
            // contexts on the same graph won't interfere — but
            // creating one executor per WIT context lets us
            // amortize the executor setup cost across multiple
            // computes on the same context.
            _executor = new StatelessExecutor(graph.Weights, graph.ModelParams);
        }

        public IReadOnlyList<NamedTensor> Compute(IReadOnlyList<NamedTensor> inputs)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LlamaSharpContext));

            // WasmEdge convention: a single U8 input tensor
            // carrying UTF-8 prompt bytes. We don't enforce
            // exactly-one-input — extras are tolerated for
            // forward compat with future per-input conventions
            // (e.g., a second tensor for system prompt) — but
            // we DO require the first input be U8.
            if (inputs.Count == 0)
                throw new WasiNNException(
                    ErrorCode.InvalidArgument,
                    "LlamaSharpBackend.Compute received no inputs; "
                    + "expected one U8 tensor with the UTF-8 prompt bytes.");
            var promptTensor = inputs[0].Tensor;
            if (promptTensor.Type != TensorType.U8)
                throw new WasiNNException(
                    ErrorCode.InvalidArgument,
                    $"LlamaSharpBackend expects input[0] to be U8 "
                    + $"(WasmEdge GGUF convention); got {promptTensor.Type}.");

            string prompt = Encoding.UTF8.GetString(promptTensor.Data.Span);

            // StatelessExecutor.InferAsync streams tokens as
            // they're generated. The wasi-nn compute call is
            // synchronous, so we block-collect the stream here.
            // Inference is CPU-bound (or GPU-bound on offloaded
            // layers); blocking one wasm thread for the
            // duration of generation matches what the spec
            // expects.
            string responseText = RunInferenceSync(prompt);

            var responseBytes = Encoding.UTF8.GetBytes(responseText);
            var outputTensor = new Tensor(
                new uint[] { (uint)responseBytes.Length },
                TensorType.U8,
                responseBytes);
            return new[] { new NamedTensor("0", outputTensor) };
        }

        private string RunInferenceSync(string prompt)
        {
            var sb = new StringBuilder();
            try
            {
                // Drain the IAsyncEnumerable<string> from the
                // executor. Task.Run isolates the async state
                // machine onto a thread-pool thread so we don't
                // re-enter wasm context state from a sync-over-
                // async caller's continuation.
                var task = Task.Run(async () =>
                {
                    await foreach (var piece in _executor.InferAsync(
                        prompt, _graph.DefaultInferenceParams).ConfigureAwait(false))
                    {
                        sb.Append(piece);
                    }
                });
                task.GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not WasiNNException)
            {
                throw new WasiNNException(
                    ErrorCode.RuntimeError,
                    $"LlamaSharp inference failed: {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // StatelessExecutor doesn't expose IDisposable on
            // every LlamaSharp version; rely on GC for the
            // executor's internal LLamaContext. The graph's
            // Weights ownership is what actually holds the
            // gigabyte-scale memory.
        }
    }
}
