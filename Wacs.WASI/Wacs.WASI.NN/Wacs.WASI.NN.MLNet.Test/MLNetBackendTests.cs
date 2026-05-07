// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Microsoft.ML;
using Wacs.WASI.NN;
using Wacs.WASI.NN.MLNet;
using Wacs.WASI.NN.Types;
using Xunit;

namespace Wacs.WASI.NN.MLNet.Test
{
    public sealed class MLNetBackendTests
    {
        [Fact]
        public void SupportedEncodings_IncludesOnnxAndAutodetect()
        {
            var backend = new MLNetBackend();
            Assert.Contains(GraphEncoding.ONNX, backend.SupportedEncodings);
            Assert.Contains(GraphEncoding.Autodetect, backend.SupportedEncodings);
        }

        [Fact]
        public void Context_PropertyExposesMLContext()
        {
            // Embedders need to reach the MLContext to compose
            // pipelines (custom transformers, logging, etc.).
            // The accessor is part of the public contract.
            var ml = new MLContext(seed: 42);
            var backend = new MLNetBackend(ml);
            Assert.Same(ml, backend.Context);
        }

        [Fact]
        public void LoadGraph_EmptyBuilders_InvalidArgument()
        {
            var backend = new MLNetBackend();
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraph(
                    new List<ReadOnlyMemory<byte>>(),
                    ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.InvalidArgument, ex.Code);
        }

        [Fact]
        public void LoadGraph_TpuTarget_UnsupportedOperation()
        {
            var backend = new MLNetBackend();
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraph(
                    new[] { (ReadOnlyMemory<byte>)new byte[] { 0x08 } },
                    ExecutionTarget.TPU));
            Assert.Equal(ErrorCode.UnsupportedOperation, ex.Code);
        }

        [Fact]
        public void LoadGraph_MalformedBytes_RuntimeError()
        {
            var backend = new MLNetBackend();
            var notAnOnnx = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC };
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraph(
                    new[] { (ReadOnlyMemory<byte>)notAnOnnx },
                    ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.RuntimeError, ex.Code);
        }

        [Fact]
        public void LoadGraphByName_RoutesToHostRegistry()
        {
            var backend = new MLNetBackend();
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraphByName("any-name", ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.NotFound, ex.Code);
        }
    }
}
