// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.WASI.NN;
using Wacs.WASI.NN.LlamaSharp;
using Wacs.WASI.NN.Types;
using Xunit;

namespace Wacs.WASI.NN.LlamaSharp.Test
{
    /// <summary>
    /// SPI-level tests against <see cref="LlamaSharpBackend"/>
    /// that DON'T need a real GGUF on disk. Real-model
    /// generation tests live in the dual-ABI fixture suite
    /// (task #35) and are gated on a WACS_GGUF_MODEL_PATH env
    /// var so CI doesn't need to provision a multi-GB file.
    /// </summary>
    public sealed class LlamaSharpBackendTests
    {
        [Fact]
        public void SupportedEncodings_OnlyGgml()
        {
            var backend = new LlamaSharpBackend(_ => null);
            Assert.Single(backend.SupportedEncodings);
            Assert.Contains(GraphEncoding.GGML, backend.SupportedEncodings);
        }

        [Fact]
        public void LoadGraph_ByteBufferPath_Unsupported()
        {
            // Per the WasmEdge convention, multi-GB GGUF
            // models must come through load-by-name; the
            // byte-buffer path always rejects.
            var backend = new LlamaSharpBackend(_ => null);
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraph(
                    new[] { (ReadOnlyMemory<byte>)new byte[] { 0x01 } },
                    ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.UnsupportedOperation, ex.Code);
        }

        [Fact]
        public void LoadGraphByName_UnknownName_NotFound()
        {
            var backend = new LlamaSharpBackend(_ => null);
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraphByName("missing-model", ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.NotFound, ex.Code);
        }

        [Fact]
        public void LoadGraphByName_TpuTarget_Unsupported()
        {
            // The registry can return any entry; TPU is
            // rejected before the registry is even consulted.
            var backend = new LlamaSharpBackend(name =>
                new GgufModelEntry("/never-read.gguf"));
            var ex = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraphByName("any", ExecutionTarget.TPU));
            Assert.Equal(ErrorCode.UnsupportedOperation, ex.Code);
        }

        [Fact]
        public void FromPaths_StaticConvenience()
        {
            // The dictionary-based convenience ctor wires a
            // simple name → path lookup with default model
            // params. Calling LoadGraphByName surfaces
            // RuntimeError when the path doesn't exist
            // (LlamaSharp throws on file-not-found), not
            // NotFound — NotFound is reserved for "name not
            // in registry".
            var backend = LlamaSharpBackend.FromPaths(
                new Dictionary<string, string>
                {
                    ["my-model"] = "/this/file/does/not/exist.gguf",
                });
            var unknown = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraphByName("missing", ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.NotFound, unknown.Code);

            var registered = Assert.Throws<WasiNNException>(() =>
                backend.LoadGraphByName("my-model", ExecutionTarget.CPU));
            Assert.Equal(ErrorCode.RuntimeError, registered.Code);
        }

        [Fact]
        public void Host_LoadByNameBackend_TakesPrecedence()
        {
            // When both LoadByNameBackend and
            // NamedModelResolver are configured, the backend
            // wins — that's the path LlamaSharp guests use.
            var llama = new LlamaSharpBackend(name =>
                name == "registered" ? new GgufModelEntry("/missing.gguf") : null);
            var cfg = WasiNNConfiguration.DefaultConfiguration();
            cfg.LoadByNameBackend = llama;
            cfg.NamedModelResolver = name => null; // would also fail

            using var host = new WasiNNHost(cfg);

            // Reach the dispatch via reflection (it's internal).
            var dispatch = typeof(WasiNNHost).GetMethod(
                "LoadGraphByNameDispatch",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)!;

            // Unknown name: backend's NotFound surfaces.
            var unknownEx = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                dispatch.Invoke(host, new object[] { "unknown" }));
            var unknownInner = Assert.IsType<WasiNNException>(unknownEx.InnerException);
            Assert.Equal(ErrorCode.NotFound, unknownInner.Code);

            // Known name: backend tries to load → file
            // missing → RuntimeError. Confirms the dispatch
            // routed to LlamaSharpBackend, not the resolver.
            var knownEx = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                dispatch.Invoke(host, new object[] { "registered" }));
            var knownInner = Assert.IsType<WasiNNException>(knownEx.InnerException);
            Assert.Equal(ErrorCode.RuntimeError, knownInner.Code);
        }
    }
}
