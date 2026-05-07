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
    /// End-to-end inference round-trip against the real
    /// Microsoft.ML.OnnxRuntime backend. Loads a tiny
    /// hand-built ONNX Identity model (built inline by
    /// <see cref="TinyOnnxModel.IdentityFloat1"/>), runs
    /// inference through the full SPI lift / lower path, and
    /// verifies output bytes match input bytes exactly.
    ///
    /// <para>Catches the byte → typed-array reinterp path
    /// (<see cref="OnnxContext.BuildOrtValue"/>) and the
    /// typed-span → byte materialization path
    /// (<see cref="OnnxContext.MaterializeOrtValue"/>) end-to-
    /// end against an actual ORT session — covers what the
    /// IdentityBackend smoke tests can't.</para>
    /// </summary>
    public sealed class InferenceRoundTripTests
    {
        [Fact]
        public void Identity_float1_round_trips_through_real_ort()
        {
            var modelBytes = TinyOnnxModel.IdentityFloat1();

            var backend = new OnnxBackend();
            using var graph = backend.LoadGraph(
                new[] { (ReadOnlyMemory<byte>)modelBytes },
                ExecutionTarget.CPU);
            using var ctx = graph.CreateContext();

            // Input X = [42.0f]; reinterp the single float as
            // its 4 little-endian bytes for the canonical-ABI-
            // shaped Tensor.Data buffer.
            var inputFloats = new[] { 42.0f };
            var inputBytes = new byte[4];
            Buffer.BlockCopy(inputFloats, 0, inputBytes, 0, 4);

            var outputs = ctx.Compute(new[]
            {
                new NamedTensor("X",
                    new Tensor(new uint[] { 1 }, TensorType.FP32, inputBytes)),
            });

            // Identity → exactly one output named "Y" with the
            // same shape, type, and bytes as the input.
            Assert.Single(outputs);
            Assert.Equal("Y", outputs[0].Name);
            Assert.Equal(new uint[] { 1 }, outputs[0].Tensor.Dimensions);
            Assert.Equal(TensorType.FP32, outputs[0].Tensor.Type);

            var outFloats = MemoryMarshal.Cast<byte, float>(
                outputs[0].Tensor.Data.Span);
            Assert.Equal(1, outFloats.Length);
            Assert.Equal(42.0f, outFloats[0]);
        }

        [Fact]
        public void Identity_handles_consecutive_compute_calls()
        {
            // Same context, multiple Compute calls — verifies
            // that ORT's stateless Run + the OrtValue lifecycle
            // hold up across repeated invocations on one
            // session. Catches Dispose-on-input regressions
            // that pass single-call but break the second time
            // around.
            var backend = new OnnxBackend();
            using var graph = backend.LoadGraph(
                new[] { (ReadOnlyMemory<byte>)TinyOnnxModel.IdentityFloat1() },
                ExecutionTarget.CPU);
            using var ctx = graph.CreateContext();

            for (int i = 0; i < 3; i++)
            {
                var f = (float)(i * 100);
                var bytes = new byte[4];
                Buffer.BlockCopy(new[] { f }, 0, bytes, 0, 4);
                var outputs = ctx.Compute(new[]
                {
                    new NamedTensor("X",
                        new Tensor(new uint[] { 1 }, TensorType.FP32, bytes)),
                });
                var got = MemoryMarshal.Cast<byte, float>(
                    outputs[0].Tensor.Data.Span)[0];
                Assert.Equal(f, got);
            }
        }

        [Fact]
        public void Wrong_input_name_surfaces_RuntimeError()
        {
            // ORT throws OnnxRuntimeException when an input
            // name doesn't match the model's declared inputs.
            // The backend should wrap that as a typed
            // WasiNNException with RuntimeError, NOT swallow
            // it or let the raw ORT exception propagate.
            var backend = new OnnxBackend();
            using var graph = backend.LoadGraph(
                new[] { (ReadOnlyMemory<byte>)TinyOnnxModel.IdentityFloat1() },
                ExecutionTarget.CPU);
            using var ctx = graph.CreateContext();

            var bytes = new byte[4];
            var ex = Assert.Throws<WasiNNException>(() =>
                ctx.Compute(new[]
                {
                    new NamedTensor("Q", // model expects "X"
                        new Tensor(new uint[] { 1 }, TensorType.FP32, bytes)),
                }));
            Assert.Equal(ErrorCode.RuntimeError, ex.Code);
            Assert.NotNull(ex.BackendData);
        }
    }
}
