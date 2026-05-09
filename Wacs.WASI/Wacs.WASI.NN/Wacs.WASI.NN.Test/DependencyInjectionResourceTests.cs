// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.NN;
using Wacs.WASI.NN.DependencyInjection;
using Wacs.WASI.NN.Types;
using Xunit;
using DiTensor = Wacs.WASI.NN.DependencyInjection.Tensor;
using DiError = Wacs.WASI.NN.DependencyInjection.Error;
using NnTensorType = Wacs.WASI.NN.Nn.TensorType;
using NnErrorCode = Wacs.WASI.NN.Nn.ErrorCode;

namespace Wacs.WASI.NN.Test
{
    /// <summary>
    /// Smoke coverage for the resource concrete classes shipped by
    /// <c>Wacs.WASI.NN.DependencyInjection</c>. The transpiler-direct-
    /// link path doesn't have a self-contained test rig yet (multi-
    /// bundle wiring is the open follow-up), but the resource impls
    /// can be exercised in isolation: parameterless construct,
    /// populate via the WIT-constructor lift, read back via the
    /// resource accessors.
    /// </summary>
    public class DependencyInjectionResourceTests
    {
        [Fact]
        public void Tensor_GuestConstructorRoundTrip()
        {
            // The WIT `constructor(dimensions, ty, data)` lowers to
            // a parameterless host instantiation followed by the
            // generated `void Create(...)` instance method.
            var t = new DiTensor();
            var dims = new uint[] { 1, 4 };
            var data = new byte[16];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)i;

            t.Create(dims, NnTensorType.FP32, data);

            Assert.Equal(dims, t.Dimensions());
            Assert.Equal(NnTensorType.FP32, t.Ty());
            Assert.Equal(data, t.Data());
        }

        [Fact]
        public void Tensor_HostFactoryAlsoWorks()
        {
            var t = DiTensor.FromBackendOutput(
                new uint[] { 2, 2 }, NnTensorType.I32,
                new byte[16]);

            Assert.Equal(new uint[] { 2, 2 }, t.Dimensions());
            Assert.Equal(NnTensorType.I32, t.Ty());
        }

        [Fact]
        public void Tensor_DoubleConstructionThrows()
        {
            var t = new DiTensor();
            t.Create(new uint[] { 1 }, NnTensorType.U8, new byte[1]);
            Assert.Throws<InvalidOperationException>(() =>
                t.Create(new uint[] { 1 }, NnTensorType.U8, new byte[1]));
        }

        [Fact]
        public void Tensor_AccessBeforeConstructionThrows()
        {
            var t = new DiTensor();
            Assert.Throws<InvalidOperationException>(() => t.Dimensions());
            Assert.Throws<InvalidOperationException>(() => t.Ty());
            Assert.Throws<InvalidOperationException>(() => t.Data());
        }

        [Fact]
        public void Error_FromException_PreservesCodeAndMessage()
        {
            var ex = new WasiNNException(ErrorCode.InvalidEncoding,
                "bogus encoding", backendData: "backend rejected");
            var err = DiError.FromException(ex);

            Assert.Equal(NnErrorCode.InvalidEncoding, err.Code());
            Assert.Equal("backend rejected", err.Data());
        }

        [Fact]
        public void Error_FromException_FallsBackToMessage()
        {
            var ex = new WasiNNException(ErrorCode.RuntimeError,
                "fallback msg");
            var err = DiError.FromException(ex);

            Assert.Equal(NnErrorCode.RuntimeError, err.Code());
            Assert.Equal("fallback msg", err.Data());
        }
    }
}
