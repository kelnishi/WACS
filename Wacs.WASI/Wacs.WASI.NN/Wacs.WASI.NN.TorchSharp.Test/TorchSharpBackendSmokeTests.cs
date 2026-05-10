// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;
using Xunit;

namespace Wacs.WASI.NN.TorchSharp.Test
{
    // Backend SPI surface smoke tests. Real TorchScript inference
    // tests would need a committed .pt fixture (libtorch's binary
    // format isn't trivial to hand-encode), which we skip until a
    // real PyTorch harness lands. These cover the contract shape:
    // SupportedEncodings is right, byte-loaded LoadGraph rejects
    // garbage with the right ErrorCode, name registry round-trips.
    public class TorchSharpBackendSmokeTests
    {
        [Fact]
        public void SupportedEncodings_OnlyPyTorch()
        {
            var backend = new TorchSharpBackend();
            Assert.Single(backend.SupportedEncodings, GraphEncoding.PyTorch);
        }

        [Fact]
        public void LoadGraph_TpuTarget_ThrowsUnsupportedOperation()
        {
            var backend = new TorchSharpBackend();
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraph(
                    new[] { ReadOnlyMemory<byte>.Empty },
                    ExecutionTarget.TPU));
            Assert.Equal(ErrorCode.UnsupportedOperation, ex.Code);
        }

        [Fact]
        public void LoadGraph_EmptyBuilders_ThrowsInvalidArgument()
        {
            var backend = new TorchSharpBackend();
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraph(
                    Array.Empty<ReadOnlyMemory<byte>>(),
                    ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.InvalidArgument, ex.Code);
        }

        [Fact]
        public void LoadGraph_GarbageBytes_LiftsTorchExceptionToRuntimeError()
        {
            var backend = new TorchSharpBackend();
            // 64 bytes of nonsense — torch.jit.load expects a
            // zip with specific entries; opening this trips a
            // libtorch internal error which the binding maps to
            // RuntimeError (not InvalidArgument — bad bytes are
            // a runtime concern, not a contract violation).
            var bytes = new byte[64];
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraph(
                    new ReadOnlyMemory<byte>[] { bytes },
                    ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.RuntimeError, ex.Code);
            Assert.Contains("torch.jit.load", ex.Message,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LoadGraphByName_NoRegistry_ThrowsNotFound()
        {
            var backend = new TorchSharpBackend();   // null registry
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraphByName("anything", ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.NotFound, ex.Code);
            Assert.Contains("registry", ex.Message,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LoadGraphByName_UnknownName_ThrowsNotFound()
        {
            var backend = TorchSharpBackend.FromPaths(
                new Dictionary<string, string>
                {
                    ["resnet50"] = "/some/path.pt",
                });
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraphByName("mobilenet", ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.NotFound, ex.Code);
        }

        [Fact]
        public void LoadGraphByName_RegisteredButFileMissing_ThrowsNotFound()
        {
            var backend = TorchSharpBackend.FromPaths(
                new Dictionary<string, string>
                {
                    ["nope"] = "/this/path/definitely/does/not/exist.pt",
                });
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraphByName("nope", ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.NotFound, ex.Code);
            Assert.Contains("doesn't exist", ex.Message,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Bindable_DefaultCtor_DoesNotThrow()
        {
            // The auto-discovery path requires the IBindable
            // class to have a parameterless ctor that doesn't
            // throw at construction time (otherwise --bind
            // tries activate-and-fail with a confusing trace).
            // Empty $WACS_WASINN_TORCH_DIR → empty registry,
            // backend constructs, host wires; that's the
            // happy path we verify here.
            using var bindable = new WasiNNTorchSharpBindable();
            Assert.NotNull(bindable);
        }
    }
}
