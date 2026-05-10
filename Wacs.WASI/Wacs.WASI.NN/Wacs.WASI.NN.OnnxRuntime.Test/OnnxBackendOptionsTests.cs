// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Wacs.WASI.NN;
using Wacs.WASI.NN.OnnxRuntime;
using Wacs.WASI.NN.Test;
using Wacs.WASI.NN.Types;
using Xunit;

namespace Wacs.WASI.NN.OnnxRuntime.Test
{
    /// <summary>
    /// Tests for the EP-selection surface introduced with the
    /// hardware-acceleration knob: <see cref="OnnxBackendOptions"/>,
    /// <see cref="OnnxExecutionProvider"/>, and
    /// <c>WACS_WASINN_ONNX_EP</c> parsing.
    /// </summary>
    public sealed class OnnxBackendOptionsTests
    {
        [Fact]
        public void Defaults_pick_cpu_with_fallback_enabled()
        {
            var opts = new OnnxBackendOptions();
            // CPU is the safe default — hardware acceleration is
            // explicit opt-in via WACS_WASINN_ONNX_EP or the typed
            // ExecutionProvider setter. The Auto enum value still
            // exists for users who want platform-best-pick semantics
            // but is no longer the default for OnnxBackendOptions.
            Assert.Equal(OnnxExecutionProvider.Cpu, opts.ExecutionProvider);
            Assert.True(opts.FallbackToCpu);
        }

        [Theory]
        [InlineData("auto", OnnxExecutionProvider.Auto)]
        [InlineData("AUTO", OnnxExecutionProvider.Auto)]
        [InlineData("cpu", OnnxExecutionProvider.Cpu)]
        [InlineData("CoreML", OnnxExecutionProvider.CoreML)]
        [InlineData("cuda", OnnxExecutionProvider.Cuda)]
        [InlineData("dml", OnnxExecutionProvider.DirectML)]
        [InlineData("directml", OnnxExecutionProvider.DirectML)]
        [InlineData("rocm", OnnxExecutionProvider.Rocm)]
        [InlineData("not-a-real-ep", OnnxExecutionProvider.Auto)]
        public void FromEnvironment_parses_ep_strings(
            string envValue, OnnxExecutionProvider expected)
        {
            var prev = Environment.GetEnvironmentVariable("WACS_WASINN_ONNX_EP");
            try
            {
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_ONNX_EP", envValue);
                var opts = OnnxBackendOptions.FromEnvironment();
                Assert.Equal(expected, opts.ExecutionProvider);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_ONNX_EP", prev);
            }
        }

        [Fact]
        public void FromEnvironment_parses_device_ids()
        {
            var prevCuda = Environment.GetEnvironmentVariable(
                "WACS_WASINN_ONNX_CUDA_DEVICE");
            var prevRocm = Environment.GetEnvironmentVariable(
                "WACS_WASINN_ONNX_ROCM_DEVICE");
            var prevDml = Environment.GetEnvironmentVariable(
                "WACS_WASINN_ONNX_DML_DEVICE");
            try
            {
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_ONNX_CUDA_DEVICE", "2");
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_ONNX_ROCM_DEVICE", "3");
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_ONNX_DML_DEVICE", "1");
                var opts = OnnxBackendOptions.FromEnvironment();
                Assert.Equal(2, opts.CudaDeviceId);
                Assert.Equal(3, opts.RocmDeviceId);
                Assert.Equal(1, opts.DirectMLDeviceId);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_ONNX_CUDA_DEVICE", prevCuda);
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_ONNX_ROCM_DEVICE", prevRocm);
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_ONNX_DML_DEVICE", prevDml);
            }
        }

        [Fact]
        public void OnnxBackend_typed_options_ctor_constructs()
        {
            // Doesn't load a session — just confirms the ctor
            // overload exists and accepts options without throwing.
            var opts = new OnnxBackendOptions
            {
                ExecutionProvider = OnnxExecutionProvider.Cpu
            };
            var backend = new OnnxBackend(opts);
            Assert.Contains(GraphEncoding.ONNX, backend.SupportedEncodings);
        }

        [Fact]
        public void OnnxBackend_typed_options_null_throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new OnnxBackend((OnnxBackendOptions)null!));
        }

        [Fact]
        public void Cpu_explicit_load_round_trips()
        {
            // Sanity check that the new options path produces a
            // working CPU session — same baseline as the existing
            // round-trip test, but reached through the typed-
            // options ctor instead of the parameterless ctor.
            var backend = new OnnxBackend(new OnnxBackendOptions
            {
                ExecutionProvider = OnnxExecutionProvider.Cpu
            });
            using var graph = backend.LoadGraph(
                new[] { (ReadOnlyMemory<byte>)TinyOnnxModel.IdentityFloat1() },
                ExecutionTarget.CPU);
            using var ctx = graph.CreateContext();

            var inputFloats = new[] { 7.0f };
            var inputBytes = new byte[4];
            Buffer.BlockCopy(inputFloats, 0, inputBytes, 0, 4);
            var outputs = ctx.Compute(new[]
            {
                new NamedTensor("X",
                    new Tensor(new uint[] { 1 }, TensorType.FP32, inputBytes)),
            });
            var outFloats = MemoryMarshal.Cast<byte, float>(
                outputs[0].Tensor.Data.Span);
            Assert.Equal(7.0f, outFloats[0]);
        }

        [Fact]
        public void CoreML_load_round_trips_on_macos_otherwise_falls_back()
        {
            // CoreML EP append should succeed on macOS-arm64 (the
            // bundled libonnxruntime.dylib ships the EP symbol).
            // On non-macOS hosts the append throws and we expect
            // the silent fallback to CPU to keep inference alive.
            // Either path produces the same Identity output.
            var backend = new OnnxBackend(new OnnxBackendOptions
            {
                ExecutionProvider = OnnxExecutionProvider.CoreML,
                FallbackToCpu = true,
            });
            using var graph = backend.LoadGraph(
                new[] { (ReadOnlyMemory<byte>)TinyOnnxModel.IdentityFloat1() },
                ExecutionTarget.GPU);
            using var ctx = graph.CreateContext();

            var inputFloats = new[] { 13.0f };
            var inputBytes = new byte[4];
            Buffer.BlockCopy(inputFloats, 0, inputBytes, 0, 4);
            var outputs = ctx.Compute(new[]
            {
                new NamedTensor("X",
                    new Tensor(new uint[] { 1 }, TensorType.FP32, inputBytes)),
            });
            var outFloats = MemoryMarshal.Cast<byte, float>(
                outputs[0].Tensor.Data.Span);
            Assert.Equal(13.0f, outFloats[0]);
        }

        [Theory]
        [InlineData("MLProgram", "COREML_FLAG_CREATE_MLPROGRAM")]
        [InlineData("create_mlprogram", "COREML_FLAG_CREATE_MLPROGRAM")]
        [InlineData("UseCpuAndGpu", "COREML_FLAG_USE_CPU_AND_GPU")]
        [InlineData("use_cpu_only", "COREML_FLAG_USE_CPU_ONLY")]
        [InlineData("ANE", "COREML_FLAG_ONLY_ENABLE_DEVICE_WITH_ANE")]
        [InlineData("Static", "COREML_FLAG_ONLY_ALLOW_STATIC_INPUT_SHAPES")]
        [InlineData("subgraph", "COREML_FLAG_ENABLE_ON_SUBGRAPH")]
        public void ParseCoreMLFlags_recognizes_single_names(
            string name, string expectedFlagName)
        {
            var flags = OnnxBackendOptions.ParseCoreMLFlags(name);
            var expected = (Microsoft.ML.OnnxRuntime.CoreMLFlags)Enum.Parse(
                typeof(Microsoft.ML.OnnxRuntime.CoreMLFlags),
                expectedFlagName);
            Assert.True((flags & expected) == expected,
                $"expected {expectedFlagName} to be set in {flags}");
        }

        [Fact]
        public void ParseCoreMLFlags_combines_multiple_separators()
        {
            var combined = OnnxBackendOptions.ParseCoreMLFlags(
                "MLProgram, UseCpuAndGpu | Static");
            Assert.True((combined & Microsoft.ML.OnnxRuntime.CoreMLFlags
                .COREML_FLAG_CREATE_MLPROGRAM) != 0);
            Assert.True((combined & Microsoft.ML.OnnxRuntime.CoreMLFlags
                .COREML_FLAG_USE_CPU_AND_GPU) != 0);
            Assert.True((combined & Microsoft.ML.OnnxRuntime.CoreMLFlags
                .COREML_FLAG_ONLY_ALLOW_STATIC_INPUT_SHAPES) != 0);
        }

        [Fact]
        public void ParseCoreMLFlags_empty_returns_use_none()
        {
            Assert.Equal(
                Microsoft.ML.OnnxRuntime.CoreMLFlags.COREML_FLAG_USE_NONE,
                OnnxBackendOptions.ParseCoreMLFlags(""));
            Assert.Equal(
                Microsoft.ML.OnnxRuntime.CoreMLFlags.COREML_FLAG_USE_NONE,
                OnnxBackendOptions.ParseCoreMLFlags(null));
        }

        [Fact]
        public void FromEnvironment_reads_coreml_flags()
        {
            var prev = Environment.GetEnvironmentVariable(
                "WACS_WASINN_ONNX_COREML_FLAGS");
            try
            {
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_ONNX_COREML_FLAGS", "MLProgram");
                var opts = OnnxBackendOptions.FromEnvironment();
                Assert.True((opts.CoreMLFlags & Microsoft.ML.OnnxRuntime
                    .CoreMLFlags.COREML_FLAG_CREATE_MLPROGRAM) != 0);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "WACS_WASINN_ONNX_COREML_FLAGS", prev);
            }
        }

        [Fact]
        public void Strict_mode_propagates_ep_append_failure_on_unsupported_platform()
        {
            // Cuda EP append throws on macOS where no CUDA driver
            // exists; with FallbackToCpu=false that should surface
            // as a WasiNNException with RuntimeError at LoadGraph.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) == false)
                return;  // skip on non-mac (the negative path is the
                         // mac-specific assertion).
            var backend = new OnnxBackend(new OnnxBackendOptions
            {
                ExecutionProvider = OnnxExecutionProvider.Cuda,
                FallbackToCpu = false,
            });
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraph(
                    new[] { (ReadOnlyMemory<byte>)TinyOnnxModel.IdentityFloat1() },
                    ExecutionTarget.GPU));
            Assert.Equal(ErrorCode.RuntimeError, ex.Code);
        }
    }
}
