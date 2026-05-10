// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Reflection;
using Wacs.WASI.NN.DependencyInjection;
using Wacs.WASI.NN.Types;
using Xunit;
using NnGraphFuncs = Wacs.WASI.NN.Nn.IGraphFuncs;
using NnErrorCode = Wacs.WASI.NN.Nn.ErrorCode;

namespace Wacs.WASI.NN.Test
{
    // Regression test for wasi-nn/WACS-GAPS.md gap 24.
    //
    // The DI bundle's GraphFuncsImpl.LoadByName historically only
    // checked WasiNNConfiguration.NamedModelResolver, ignoring the
    // sibling LoadByNameBackend field. The WitBindings path
    // (WasiNNHost.LoadGraphByNameDispatch) checked LoadByNameBackend
    // first, then fell back to the resolver — backends with
    // internal name registries (LlamaSharp / GGUF) wired
    // LoadByNameBackend and worked through the legacy interpreter
    // path but tripped "no named-model resolver configured" through
    // the DI bundle / transpiler-direct-link path.
    //
    // Round-19 fix: GraphFuncsImpl.LoadByName routes through
    // LoadByNameBackend.LoadGraphByName(name, target) BEFORE the
    // resolver check — same shape as WitBindings.
    public class GraphFuncsImplLoadByNameTests
    {
        // Minimal stub backend that responds only to LoadGraphByName.
        // Throws from the byte-flow LoadGraph path (matches LlamaSharp's
        // intentional UnsupportedOperation surface for multi-GB GGUFs).
        private sealed class NameOnlyStubBackend : IBackend
        {
            private readonly Dictionary<string, IBackendGraph> _registry;
            public NameOnlyStubBackend(
                Dictionary<string, IBackendGraph> registry)
            { _registry = registry; }

            public IReadOnlyCollection<GraphEncoding> SupportedEncodings
                => new[] { GraphEncoding.GGML };

            public IBackendGraph LoadGraph(
                IReadOnlyList<ReadOnlyMemory<byte>> builders,
                ExecutionTarget target)
                => throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    "byte-loader path is intentionally unsupported");

            public IBackendGraph LoadGraphByName(
                string name, ExecutionTarget target)
            {
                if (!_registry.TryGetValue(name, out var graph))
                    throw new WasiNNException(
                        ErrorCode.NotFound,
                        "no graph registered for '" + name + "'");
                return graph;
            }
        }

        private sealed class StubBackendGraph : IBackendGraph
        {
            public string Tag { get; }
            public StubBackendGraph(string tag) { Tag = tag; }
            public IBackendContext CreateContext()
                => throw new NotImplementedException("not exercised");
            public void Dispose() { }
        }

        [Fact]
        public void LoadByName_RoutesThroughLoadByNameBackend_WhenWired()
        {
            // The Direct path: LoadByNameBackend wired, no
            // NamedModelResolver. Pre-fix this returned NotFound
            // ("no named-model resolver configured"). Post-fix the
            // backend's LoadGraphByName fires and the OK arm carries
            // the lifted IGraph.
            var stubGraph = new StubBackendGraph("qwen");
            var registry = new Dictionary<string, IBackendGraph>
            {
                { "qwen-0.5b", stubGraph },
            };
            var backend = new NameOnlyStubBackend(registry);

            var config = WasiNNConfiguration.DefaultConfiguration();
            config.LoadByNameBackend = backend;
            // Leave NamedModelResolver and Backends[GGML] unset to
            // confirm the new branch fires before either check.

            var funcs = (NnGraphFuncs)NewGraphFuncsImpl(config);
            var result = funcs.LoadByName("qwen-0.5b");

            Assert.True(result.IsOk,
                "LoadByName should route through LoadByNameBackend; "
                + "got Err " + (result.IsErr
                    ? result.Err.Code() + ": " + result.Err.Data()
                    : "<unreachable>"));
        }

        [Fact]
        public void LoadByName_FallsBackToResolver_WhenLoadByNameBackendNull()
        {
            // The byte-flow path: LoadByNameBackend null, resolver
            // returns a (bytes, encoding) record, Backends[encoding]
            // resolves the byte-loader. Existing behavior — the new
            // branch must not break it.
            var byteGraph = new StubBackendGraph("byte");
            var bytesBackend = new NameOnlyStubBackend(
                new Dictionary<string, IBackendGraph>());
            // Hack the byte-flow side: inject a backend whose
            // LoadGraph (rather than LoadGraphByName) yields the
            // graph. NameOnlyStubBackend trips on LoadGraph by
            // design, so use a different stub here.
            var byteFlowBackend = new ByteFlowStubBackend(byteGraph);

            var config = WasiNNConfiguration.DefaultConfiguration();
            config.LoadByNameBackend = null;
            config.NamedModelResolver = name => new NamedModel(
                encoding: GraphEncoding.ONNX,
                builders: new[] { ReadOnlyMemory<byte>.Empty },
                target: ExecutionTarget.CPU);
            config.Backends[GraphEncoding.ONNX] = byteFlowBackend;

            var funcs = (NnGraphFuncs)NewGraphFuncsImpl(config);
            var result = funcs.LoadByName("any");

            Assert.True(result.IsOk,
                "Byte-flow fallback should work when LoadByNameBackend "
                + "is null; got Err "
                + (result.IsErr
                    ? result.Err.Code() + ": " + result.Err.Data()
                    : "<unreachable>"));
        }

        [Fact]
        public void LoadByName_ReturnsNotFound_WhenNeitherWired()
        {
            // Both paths absent: should return NotFound with a
            // diagnostic that mentions LoadByNameBackend (so the
            // failure mode is discoverable from the error message
            // alone, not just by reading the source).
            var config = WasiNNConfiguration.DefaultConfiguration();
            // No LoadByNameBackend, no NamedModelResolver.

            var funcs = (NnGraphFuncs)NewGraphFuncsImpl(config);
            var result = funcs.LoadByName("any");

            Assert.True(result.IsErr);
            Assert.Equal(NnErrorCode.NotFound, result.Err.Code());
            Assert.Contains("LoadByNameBackend", result.Err.Data());
        }

        // GraphFuncsImpl is internal sealed; reach it via reflection
        // so we don't need to widen the surface for tests.
        private static object NewGraphFuncsImpl(WasiNNConfiguration config)
        {
            var t = typeof(WasiNNBundle).Assembly.GetType(
                "Wacs.WASI.NN.DependencyInjection.GraphFuncsImpl",
                throwOnError: true)!;
            return Activator.CreateInstance(t,
                BindingFlags.Instance | BindingFlags.NonPublic
                    | BindingFlags.Public,
                binder: null,
                args: new object?[] { config },
                culture: null)!;
        }

        // Byte-flow stub: LoadGraph yields the supplied graph.
        // LoadGraphByName traps so the test catches a path-confusion
        // regression (the byte-flow test must NOT touch the name path).
        private sealed class ByteFlowStubBackend : IBackend
        {
            private readonly IBackendGraph _g;
            public ByteFlowStubBackend(IBackendGraph g) { _g = g; }
            public IReadOnlyCollection<GraphEncoding> SupportedEncodings
                => new[] { GraphEncoding.ONNX };
            public IBackendGraph LoadGraph(
                IReadOnlyList<ReadOnlyMemory<byte>> builders,
                ExecutionTarget target) => _g;
            public IBackendGraph LoadGraphByName(
                string name, ExecutionTarget target)
                => throw new InvalidOperationException(
                    "byte-flow stub's name path must not be invoked");
        }
    }
}
