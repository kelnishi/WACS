// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Runtime.InteropServices;
using Wacs.WASI.NN;
using Wacs.WASI.NN.MLNet;
using Wacs.WASI.NN.Test;
using Wacs.WASI.NN.Types;
using Xunit;

namespace Wacs.WASI.NN.MLNet.Test
{
    /// <summary>
    /// End-to-end inference round-trip against the MLNet
    /// backend. Same shape as the OnnxRuntime backend's
    /// integration tests — both backends ultimately route
    /// through the same ORT InferenceSession; this verifies
    /// the MLContext-wrapped path produces identical results.
    /// </summary>
    public sealed class InferenceRoundTripTests
    {
        [Fact]
        public void Identity_float1_round_trips_through_mlnet_backend()
        {
            var modelBytes = TinyOnnxModel.IdentityFloat1();

            var backend = new MLNetBackend();
            using var graph = backend.LoadGraph(
                new[] { (ReadOnlyMemory<byte>)modelBytes },
                ExecutionTarget.CPU);
            using var ctx = graph.CreateContext();

            var inputBytes = new byte[4];
            Buffer.BlockCopy(new[] { 17.5f }, 0, inputBytes, 0, 4);

            var outputs = ctx.Compute(new[]
            {
                new NamedTensor("X",
                    new Tensor(new uint[] { 1 }, TensorType.FP32, inputBytes)),
            });

            Assert.Single(outputs);
            Assert.Equal("Y", outputs[0].Name);
            Assert.Equal(TensorType.FP32, outputs[0].Tensor.Type);
            var outFloats = MemoryMarshal.Cast<byte, float>(
                outputs[0].Tensor.Data.Span);
            Assert.Equal(17.5f, outFloats[0]);
        }
    }
}
