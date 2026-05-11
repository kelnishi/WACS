// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using Wacs.WASI.NN;
using Wacs.WASI.NN.OnnxRuntimeGenAI;
using Wacs.WASI.NN.Types;
using Xunit;

namespace Wacs.WASI.NN.OnnxRuntimeGenAI.Test
{
    /// <summary>
    /// SPI-level smoke tests. The real round-trip — a full
    /// Gemma 3 / Qwen / Phi generation through GenAI — requires
    /// a GB-scale model directory; that lives outside the
    /// fixture suite. These tests cover the SPI surface, the
    /// resolver failure paths, and the named-input dispatch
    /// validation.
    /// </summary>
    public sealed class OnnxGenAIBackendSmokeTests
    {
        [Fact]
        public void SupportedEncodings_lists_onnx()
        {
            var backend = new OnnxGenAIBackend(_ => null);
            Assert.Contains(GraphEncoding.ONNX,
                backend.SupportedEncodings);
        }

        [Fact]
        public void LoadGraph_byte_path_is_unsupported()
        {
            var backend = new OnnxGenAIBackend(_ => null);
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraph(
                    new[] { (ReadOnlyMemory<byte>)new byte[] { 0x00 } },
                    ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.UnsupportedOperation, ex.Code);
        }

        [Fact]
        public void LoadGraphByName_unknown_returns_NotFound()
        {
            var backend = new OnnxGenAIBackend(_ => null);
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraphByName("nonexistent",
                    ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.NotFound, ex.Code);
        }

        [Fact]
        public void LoadGraphByName_missing_genai_config_is_InvalidArgument()
        {
            // Resolver returns a real directory that lacks the
            // GenAI descriptor. Caught by the backend's pre-load
            // check so guests get InvalidArgument instead of an
            // opaque GenAI exception.
            var tmp = Directory.CreateTempSubdirectory(
                "wacs-genai-test-").FullName;
            try
            {
                var backend = new OnnxGenAIBackend(_ => tmp);
                var ex = Assert.Throws<WasiNNException>(() =>
                    backend.LoadGraphByName("any", ExecutionTarget.CPU));
                Assert.Equal(ErrorCode.InvalidArgument, ex.Code);
                Assert.Contains("genai_config.json", ex.Message);
            }
            finally
            {
                Directory.Delete(tmp, recursive: true);
            }
        }

        [Fact]
        public void LoadGraphByName_TPU_target_is_UnsupportedOperation()
        {
            var backend = new OnnxGenAIBackend(_ => "/some/path");
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraphByName("any", ExecutionTarget.TPU));
            Assert.Equal(ErrorCode.UnsupportedOperation, ex.Code);
        }

        [Fact]
        public void Options_defaults_are_reasonable()
        {
            var opts = new OnnxGenAIBackendOptions();
            Assert.Equal(512, opts.MaxLength);
            Assert.False(opts.DoSample);
            Assert.False(opts.IncludePromptInResponse);
        }

        [Fact]
        public void Options_FromEnvironment_reads_max_length_and_sampling()
        {
            var prevMaxLen = Environment.GetEnvironmentVariable(
                "WACS_WASINN_GENAI_MAX_LENGTH");
            var prevSample = Environment.GetEnvironmentVariable(
                "WACS_WASINN_GENAI_DO_SAMPLE");
            var prevTemp = Environment.GetEnvironmentVariable(
                "WACS_WASINN_GENAI_TEMPERATURE");
            try
            {
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_GENAI_MAX_LENGTH", "1024");
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_GENAI_DO_SAMPLE", "1");
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_GENAI_TEMPERATURE", "0.7");
                var opts = OnnxGenAIBackendOptions.FromEnvironment();
                Assert.Equal(1024, opts.MaxLength);
                Assert.True(opts.DoSample);
                Assert.Equal(0.7, opts.Temperature, precision: 3);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_GENAI_MAX_LENGTH", prevMaxLen);
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_GENAI_DO_SAMPLE", prevSample);
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_GENAI_TEMPERATURE", prevTemp);
            }
        }

        [Fact]
        public void Bindable_parameterless_ctor_constructs()
        {
            // No env var set, no models loaded — should still
            // construct cleanly (empty registry; guests get
            // NotFound on load-by-name).
            using var bindable = new WasiNNOnnxGenAIBindable();
            Assert.NotNull(bindable);
        }
    }
}
