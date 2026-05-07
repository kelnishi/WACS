// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.Core.Runtime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Backends;
using Wacs.WASI.NN.Types;
using Xunit;

namespace Wacs.WASI.NN.Test
{
    /// <summary>
    /// Direct SPI smoke tests against <see cref="IdentityBackend"/>
    /// — no wasm guest involved. Exercises the configuration +
    /// IBackend SPI shape so regressions in the SPI surface
    /// (parameter types, dispose order, exception type) get
    /// caught quickly. Wire-level wasi-nn tests against a real
    /// guest module land alongside the WIT binder in a follow-up
    /// commit.
    /// </summary>
    public sealed class IdentityBackendSmokeTests
    {
        [Fact]
        public void DefaultConfiguration_HasNoBackends()
        {
            var cfg = WasiNNConfiguration.DefaultConfiguration();
            Assert.Empty(cfg.Backends);
            Assert.Null(cfg.NamedModelResolver);
            Assert.True(cfg.AllowLoadByName);
        }

        [Fact]
        public void IdentityBackend_LoadsAndComputes()
        {
            var backend = new IdentityBackend(GraphEncoding.ONNX);

            // Builders are bag-of-bytes; identity ignores them.
            var builders = new List<ReadOnlyMemory<byte>>
            {
                new byte[] { 0x01, 0x02, 0x03 },
            };

            using var graph = backend.LoadGraph(builders, ExecutionTarget.CPU);
            using var ctx = graph.CreateContext();

            var inputBytes = new byte[] { 10, 20, 30, 40 };
            var inputs = new List<NamedTensor>
            {
                new NamedTensor(
                    "0",
                    new Tensor(new uint[] { 4 }, TensorType.U8, inputBytes)),
            };

            var outputs = ctx.Compute(inputs);
            Assert.Single(outputs);
            Assert.Equal("0", outputs[0].Name);
            Assert.Equal(new uint[] { 4 }, outputs[0].Tensor.Dimensions);
            Assert.Equal(TensorType.U8, outputs[0].Tensor.Type);
            Assert.Equal(inputBytes, outputs[0].Tensor.Data.ToArray());
        }

        [Fact]
        public void Tensor_RejectsLengthMismatch()
        {
            // 4 elements × 4 bytes/FP32 = 16 expected; pass 12.
            var ex = Assert.Throws<ArgumentException>(() =>
                new Tensor(new uint[] { 4 }, TensorType.FP32, new byte[12]));
            Assert.Contains("does not match dimensions", ex.Message);
        }

        [Fact]
        public void Errno_MappingRoundTrips()
        {
            // Cases that exist on both enums round-trip clean.
            Assert.Equal(WitxErrno.InvalidArgument,
                ErrorCode.InvalidArgument.ToWitx());
            Assert.Equal(ErrorCode.InvalidArgument,
                WitxErrno.InvalidArgument.ToWit());

            Assert.Equal(WitxErrno.NotFound,
                ErrorCode.NotFound.ToWitx());
            Assert.Equal(ErrorCode.NotFound,
                WitxErrno.NotFound.ToWit());

            // WIT cases without WITX equivalents collapse onto
            // RuntimeError on the WITX side.
            Assert.Equal(WitxErrno.RuntimeError,
                ErrorCode.Security.ToWitx());
            Assert.Equal(WitxErrno.RuntimeError,
                ErrorCode.Unknown.ToWitx());
        }

        [Fact]
        public void Host_BindToRuntime_RegistersBothAbis()
        {
            // A fresh runtime + fresh host should bind without
            // any exceptions, registering both the WIT and WITX
            // imports. We don't have a real component fixture
            // wired in this smoke suite — that's a follow-up
            // task — but the bind path itself exercises every
            // BindHostFunction call site, which catches obvious
            // problems like duplicate registration, mismatched
            // generic delegate arity, or wrong namespace strings.
            var cfg = WasiNNConfiguration.DefaultConfiguration();
            cfg.Backends[GraphEncoding.ONNX] = new IdentityBackend(GraphEncoding.ONNX);

            using var host = new WasiNNHost(cfg);
            var runtime = new WasmRuntime();

            var ex = Record.Exception(() => host.BindToRuntime(runtime));
            Assert.Null(ex);
        }

        [Fact]
        public void Host_ResolveBackend_ThrowsInvalidEncodingOnUnregistered()
        {
            var cfg = WasiNNConfiguration.DefaultConfiguration();
            using var host = new WasiNNHost(cfg);

            // Resolving any encoding fails — backends dict is empty.
            // ResolveBackend is internal; reach it through reflection
            // since the test project has InternalsVisibleTo.
            var method = typeof(WasiNNHost).GetMethod(
                "ResolveBackend",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)!;
            var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                method.Invoke(host, new object[] { GraphEncoding.ONNX }));
            var inner = Assert.IsType<WasiNNException>(ex.InnerException);
            Assert.Equal(ErrorCode.InvalidEncoding, inner.Code);
        }
    }
}
