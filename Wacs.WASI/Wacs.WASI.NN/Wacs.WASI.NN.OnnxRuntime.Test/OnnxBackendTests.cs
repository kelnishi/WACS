// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.WASI.NN;
using Wacs.WASI.NN.OnnxRuntime;
using Wacs.WASI.NN.Types;
using Xunit;

namespace Wacs.WASI.NN.OnnxRuntime.Test
{
    /// <summary>
    /// SPI-level unit tests for <see cref="OnnxBackend"/>. The
    /// happy-path inference round-trip needs a real ONNX
    /// fixture; that lives in the dual-ABI fixture suite
    /// alongside the LlamaSharp backend (task #35). Until
    /// then these tests cover the error / argument paths plus
    /// the SPI surface.
    /// </summary>
    public sealed class OnnxBackendTests
    {
        [Fact]
        public void SupportedEncodings_ListsOnnxOnly()
        {
            var backend = new OnnxBackend();
            Assert.Contains(GraphEncoding.ONNX, backend.SupportedEncodings);
            Assert.Single(backend.SupportedEncodings);
        }

        [Fact]
        public void LoadGraph_EmptyBuilders_InvalidArgument()
        {
            var backend = new OnnxBackend();
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraph(
                    new List<ReadOnlyMemory<byte>>(),
                    ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.InvalidArgument, ex.Code);
        }

        [Fact]
        public void LoadGraph_TpuTarget_UnsupportedOperation()
        {
            // Even with valid-looking bytes the TPU branch
            // should short-circuit before hitting ORT.
            var backend = new OnnxBackend();
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraph(
                    new[] { (ReadOnlyMemory<byte>)new byte[] { 0x08, 0x01 } },
                    ExecutionTarget.TPU));
            Assert.Equal(ErrorCode.UnsupportedOperation, ex.Code);
        }

        [Fact]
        public void LoadGraph_MalformedBytes_RuntimeError()
        {
            // ORT rejects malformed protobuf at session
            // construction. Our backend wraps that as
            // RuntimeError with the original message exposed
            // through BackendData.
            var backend = new OnnxBackend();
            var notAnOnnx = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC };
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraph(
                    new[] { (ReadOnlyMemory<byte>)notAnOnnx },
                    ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.RuntimeError, ex.Code);
            Assert.NotNull(ex.BackendData);
        }

        [Fact]
        public void LoadGraphByName_AlwaysRoutesToHostRegistry()
        {
            // The per-backend load-by-name path is a fallback
            // used only when a backend has its own internal
            // registry (LlamaSharp may; ORT doesn't). The
            // host orchestrator routes load-by-name through
            // WasiNNConfiguration.NamedModelResolver instead;
            // this method only fires if an embedder calls it
            // directly on the backend.
            var backend = new OnnxBackend();
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraphByName("any-name", ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.NotFound, ex.Code);
        }

        [Fact]
        public void Host_RegistersOnnxBackend_RoutesByEncoding()
        {
            // Smoke test that the OnnxBackend can be wired
            // into a WasiNNConfiguration and resolved by
            // encoding through the host orchestrator. No
            // wasm guest involved; uses the host's internal
            // ResolveBackend (visible via reflection).
            var cfg = WasiNNConfiguration.DefaultConfiguration();
            cfg.Backends[GraphEncoding.ONNX] = new OnnxBackend();
            using var host = new WasiNNHost(cfg);

            var resolve = typeof(WasiNNHost).GetMethod(
                "ResolveBackend",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)!;
            var resolved = resolve.Invoke(
                host, new object[] { GraphEncoding.ONNX });
            Assert.IsType<OnnxBackend>(resolved);
        }
    }
}
