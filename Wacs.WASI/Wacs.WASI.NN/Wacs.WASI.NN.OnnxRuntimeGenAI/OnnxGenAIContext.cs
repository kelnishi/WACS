// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using GenAI = Microsoft.ML.OnnxRuntimeGenAI;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN.OnnxRuntimeGenAI
{
    /// <summary>
    /// Inference dispatcher. <see cref="Compute"/> selects between
    /// two shapes by the input tensor's name (see the class docs on
    /// <see cref="OnnxGenAIBackend"/>):
    /// <list type="bullet">
    ///   <item><c>"prompt"</c> → full single-shot generation</item>
    ///   <item><c>"input_ids"</c> → single-forward-pass logits</item>
    /// </list>
    /// Each <see cref="Compute"/> call constructs and disposes a
    /// fresh <see cref="GenAI.Generator"/> so context state stays
    /// stateless from the guest's point of view (matches plain ORT
    /// semantics). For workloads that benefit from cross-call KV
    /// cache, the <c>"prompt"</c> path keeps the cache live for
    /// the duration of the call's internal decode loop — that's
    /// where GenAI's optimized kernels are worth the load.
    /// </summary>
    internal sealed class OnnxGenAIContext : IBackendContext
    {
        private readonly OnnxGenAIGraph _graph;
        private bool _disposed;

        public OnnxGenAIContext(OnnxGenAIGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        public IReadOnlyList<NamedTensor> Compute(
            IReadOnlyList<NamedTensor> inputs)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OnnxGenAIContext));
            if (inputs == null || inputs.Count == 0)
                throw new WasiNNException(ErrorCode.InvalidArgument,
                    "OnnxGenAIBackend.compute requires at least one input.");

            // Dispatch by the first input's name. Unknown names
            // produce InvalidArgument with the recognized set
            // surfaced for diagnostics.
            var first = inputs[0];
            switch (first.Name)
            {
                case "prompt":
                    return GenerateFromPrompt(first);
                case "input_ids":
                    return ForwardLogits(first);
                default:
                    throw new WasiNNException(ErrorCode.InvalidArgument,
                        $"OnnxGenAIBackend.compute: unrecognized input "
                        + $"name '{first.Name}'. Send 'prompt' (utf-8 "
                        + "string) for full generation or 'input_ids' "
                        + "(int64 tensor) for a single forward pass.");
            }
        }

        // "prompt" shape — full single-shot generation. Uses the
        // GenAI decode loop, which keeps a KV cache hot across
        // GenerateNextToken calls. This is where the GenAI value
        // proposition lives.
        private IReadOnlyList<NamedTensor> GenerateFromPrompt(NamedTensor input)
        {
            if (input.Tensor.Type != TensorType.U8)
                throw new WasiNNException(ErrorCode.InvalidArgument,
                    $"'prompt' input must be U8 (utf-8 bytes); got "
                    + $"{input.Tensor.Type}.");

            string prompt = Encoding.UTF8.GetString(
                input.Tensor.Data.Span);

            // Apply the model's chat template before tokenizing.
            // Instruction-tuned chat models (Gemma 3 IT, Llama 3
            // Instruct, Qwen Instruct, Phi-4 Mini Instruct, …)
            // need the prompt wrapped in their turn markers
            // (e.g., `<start_of_turn>user\n...<end_of_turn>\n<start_of_turn>model\n`)
            // for the decode loop to produce coherent output. Raw
            // text input produces gibberish.
            //
            // GenAI's Tokenizer.ApplyChatTemplate(template, messages,
            // tools, add_generation_prompt) consumes the model's
            // chat_template.jinja when the first arg is null and the
            // model directory has one. Messages is a JSON array of
            // {role, content}; we send a single user turn here.
            // add_generation_prompt=true appends the assistant-turn
            // opener so the model continues into a reply rather
            // than starting a fresh user turn.
            //
            // If ApplyChatTemplate fails (model dir lacks a
            // template, or the template uses Jinja features GenAI
            // can't render), fall back to the raw prompt — base
            // (non-instruct) models work fine without templating
            // and we don't want to break them.
            string templated;
            try
            {
                // Build the messages JSON via JsonSerializer.Serialize
                // so the prompt string is properly quoted and escaped
                // (including embedded quotes / newlines / control
                // chars). Earlier hand-built JSON via JsonEncodedText
                // produced unquoted content fields, which silently
                // failed ApplyChatTemplate inside GenAI and dropped
                // us through the catch into the raw-prompt fallback —
                // gemma-3 IT then generated several hundred tokens of
                // ?-pattern gibberish because no chat template framed
                // the user turn.
                var messages = new[] { new { role = "user", content = prompt } };
                var messagesJson = System.Text.Json.JsonSerializer
                    .Serialize(messages);
                templated = _graph.Tokenizer.ApplyChatTemplate(
                    template_str: null!,
                    messages: messagesJson,
                    tools: null!,
                    add_generation_prompt: true);
            }
            catch
            {
                templated = prompt;
            }

            int[] promptTokens;
            try
            {
                using var promptSeqs = _graph.Tokenizer.Encode(templated);
                // Tokenizer.Encode returns Sequences indexed by
                // batch — we always send one prompt, so [0] is it.
                promptTokens = promptSeqs[0].ToArray();
            }
            catch (Exception ex)
            {
                throw new WasiNNException(ErrorCode.RuntimeError,
                    $"Tokenizer.Encode failed: {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }

            int[] outputTokens;
            try
            {
                using var ps = new GenAI.GeneratorParams(_graph.Model);
                ps.SetSearchOption("max_length", _graph.Options.MaxLength);
                ps.SetSearchOption("do_sample", _graph.Options.DoSample);
                if (_graph.Options.DoSample)
                {
                    ps.SetSearchOption("temperature",
                        _graph.Options.Temperature);
                    ps.SetSearchOption("top_p", _graph.Options.TopP);
                    ps.SetSearchOption("top_k", _graph.Options.TopK);
                }

                using var gen = new GenAI.Generator(_graph.Model, ps);
                gen.AppendTokens(promptTokens);
                while (!gen.IsDone())
                    gen.GenerateNextToken();
                outputTokens = gen.GetSequence(0).ToArray();
            }
            catch (Exception ex)
            {
                throw new WasiNNException(ErrorCode.RuntimeError,
                    $"GenAI decode loop failed: {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }

            // Slice off the prompt prefix unless the embedder
            // wants it back.
            int[] toDecode = outputTokens;
            if (!_graph.Options.IncludePromptInResponse
                && outputTokens.Length > promptTokens.Length)
            {
                var sliced = new int[outputTokens.Length - promptTokens.Length];
                Array.Copy(outputTokens, promptTokens.Length,
                    sliced, 0, sliced.Length);
                toDecode = sliced;
            }

            string response;
            try
            {
                response = _graph.Tokenizer.Decode(toDecode);
            }
            catch (Exception ex)
            {
                throw new WasiNNException(ErrorCode.RuntimeError,
                    $"Tokenizer.Decode failed: {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }

            var bytes = Encoding.UTF8.GetBytes(response);
            return new[]
            {
                new NamedTensor("response",
                    new Tensor(
                        new uint[] { (uint)bytes.Length },
                        TensorType.U8,
                        bytes)),
            };
        }

        // "input_ids" shape — single forward pass, returns logits.
        // Fresh generator per call so the KV cache is wiped (the
        // guest is presumed to drive its own decode loop; mixing
        // a stale cache across compute calls would produce wrong
        // logits). GenAI doesn't expose raw model.run(...) so we
        // route through Generator: AppendTokens populates state,
        // GenerateNextToken runs one forward pass, GetOutput
        // extracts the logits tensor.
        private IReadOnlyList<NamedTensor> ForwardLogits(NamedTensor input)
        {
            if (input.Tensor.Type != TensorType.I64)
                throw new WasiNNException(ErrorCode.InvalidArgument,
                    $"'input_ids' input must be I64; got {input.Tensor.Type}.");
            if (input.Tensor.Dimensions.Length == 0)
                throw new WasiNNException(ErrorCode.InvalidArgument,
                    "'input_ids' input must have at least one dimension.");

            // Re-interpret the bytes as int64, then narrow to int
            // (GenAI's AppendTokens takes ReadOnlySpan<int>). Values
            // outside int range trip the bounds-check below; this
            // matters for embedding-table indices that are valid
            // int64 but never realistically >2^31.
            var src = MemoryMarshal.Cast<byte, long>(
                input.Tensor.Data.Span);
            var tokens = new int[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                long v = src[i];
                if (v < int.MinValue || v > int.MaxValue)
                    throw new WasiNNException(ErrorCode.InvalidArgument,
                        $"input_ids[{i}] = {v} doesn't fit in int32; "
                        + "GenAI's token API uses int.");
                tokens[i] = (int)v;
            }

            try
            {
                using var ps = new GenAI.GeneratorParams(_graph.Model);
                // max_length must exceed the prompt length; bump
                // enough to allow the single forward pass that
                // GenerateNextToken needs internally.
                ps.SetSearchOption("max_length", tokens.Length + 1);

                using var gen = new GenAI.Generator(_graph.Model, ps);
                gen.AppendTokens(tokens);
                gen.GenerateNextToken();
                using var logits = gen.GetOutput("logits");

                // Marshal the FP32 tensor back into wasi-nn bytes.
                // GetData<float>() returns a ReadOnlySpan<float> over
                // the native buffer; copy out before disposal.
                var shapeI64 = logits.Shape();
                var dims = new uint[shapeI64.Length];
                long elements = 1;
                for (int i = 0; i < shapeI64.Length; i++)
                {
                    if (shapeI64[i] < 0 || shapeI64[i] > uint.MaxValue)
                        throw new WasiNNException(ErrorCode.RuntimeError,
                            $"logits dim[{i}] = {shapeI64[i]} doesn't "
                            + "fit in u32.");
                    dims[i] = (uint)shapeI64[i];
                    elements = checked(elements * shapeI64[i]);
                }
                if (logits.Type() != GenAI.ElementType.float32)
                    throw new WasiNNException(ErrorCode.RuntimeError,
                        $"expected logits to be float32; got {logits.Type()}.");

                var floatData = logits.GetData<float>();
                var outBytes = new byte[checked(elements * 4)];
                MemoryMarshal.AsBytes(floatData).CopyTo(outBytes);

                return new[]
                {
                    new NamedTensor("logits",
                        new Tensor(dims, TensorType.FP32, outBytes)),
                };
            }
            catch (WasiNNException) { throw; }
            catch (Exception ex)
            {
                throw new WasiNNException(ErrorCode.RuntimeError,
                    $"GenAI forward-pass failed: {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // No per-context state — generators are scoped to
            // individual Compute calls.
        }
    }
}
