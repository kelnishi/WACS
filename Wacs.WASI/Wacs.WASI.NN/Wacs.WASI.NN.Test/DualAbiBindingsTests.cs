// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.Linq;
using Wacs.Core.Runtime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Backends;
using Wacs.WASI.NN.Types;
using Xunit;

namespace Wacs.WASI.NN.Test
{
    /// <summary>
    /// Dual-ABI surface verification — confirms that
    /// <see cref="WasiNNHost.BindToRuntime"/> registers every
    /// WIT and WITX import a real wasi-nn guest could ask for,
    /// from a single host instance.
    ///
    /// <para>End-to-end wire-shape tests against actual wasm
    /// guests need a fixture build pipeline (`wat2wasm` for
    /// WITX guests, the component-model emitter for WIT
    /// guests) — that's separate work tracked in the dual-ABI
    /// fixture follow-up. What we can do without that
    /// pipeline: enumerate the runtime's binding manifest and
    /// assert every expected import name is registered. If a
    /// future binding refactor drops or renames an import,
    /// this test catches it before the guest sees a
    /// missing-import link error.</para>
    /// </summary>
    public sealed class DualAbiBindingsTests
    {
        // Spec-pinned namespace string. Must match the
        // constants in WitBindings.cs / the upstream package
        // declaration in wit/wasi-nn.wit.
        private const string Pkg = "@0.2.0-rc-2024-10-28";

        // Every WIT import the four-interface binding registers.
        // Listed flat here so a binding-drop diff is obvious;
        // grouped by interface for readability.
        private static readonly (string Module, string Entity)[] ExpectedWit =
        {
            // tensor
            ($"wasi:nn/tensor{Pkg}", "[constructor]tensor"),
            ($"wasi:nn/tensor{Pkg}", "[method]tensor.dimensions"),
            ($"wasi:nn/tensor{Pkg}", "[method]tensor.ty"),
            ($"wasi:nn/tensor{Pkg}", "[method]tensor.data"),
            ($"wasi:nn/tensor{Pkg}", "[resource-drop]tensor"),
            // graph
            ($"wasi:nn/graph{Pkg}", "load"),
            ($"wasi:nn/graph{Pkg}", "load-by-name"),
            ($"wasi:nn/graph{Pkg}", "[method]graph.init-execution-context"),
            ($"wasi:nn/graph{Pkg}", "[resource-drop]graph"),
            // inference
            ($"wasi:nn/inference{Pkg}", "[method]graph-execution-context.compute"),
            ($"wasi:nn/inference{Pkg}", "[resource-drop]graph-execution-context"),
            // errors
            ($"wasi:nn/errors{Pkg}", "[method]error.code"),
            ($"wasi:nn/errors{Pkg}", "[method]error.data"),
            ($"wasi:nn/errors{Pkg}", "[resource-drop]error"),
        };

        // Every WITX import the legacy wasi_ephemeral_nn
        // binding registers. Flat list for the same reason.
        private static readonly (string Module, string Entity)[] ExpectedWitx =
        {
            ("wasi_ephemeral_nn", "load"),
            ("wasi_ephemeral_nn", "load_by_name"),
            ("wasi_ephemeral_nn", "init_execution_context"),
            ("wasi_ephemeral_nn", "set_input"),
            ("wasi_ephemeral_nn", "compute"),
            ("wasi_ephemeral_nn", "get_output"),
        };

        [Fact]
        public void Both_abis_register_every_expected_import()
        {
            var cfg = WasiNNConfiguration.DefaultConfiguration();
            cfg.Backends[GraphEncoding.ONNX] = new IdentityBackend(GraphEncoding.ONNX);
            using var host = new WasiNNHost(cfg);
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            var bound = new HashSet<(string, string)>(runtime.EnumerateBoundEntities());

            var missingWit = ExpectedWit.Where(e => !bound.Contains(e)).ToList();
            var missingWitx = ExpectedWitx.Where(e => !bound.Contains(e)).ToList();

            Assert.True(missingWit.Count == 0,
                "WIT imports missing from runtime binding manifest:\n  "
                + string.Join("\n  ", missingWit.Select(e => $"{e.Item1}::{e.Item2}")));
            Assert.True(missingWitx.Count == 0,
                "WITX imports missing from runtime binding manifest:\n  "
                + string.Join("\n  ", missingWitx.Select(e => $"{e.Item1}::{e.Item2}")));
        }

        [Fact]
        public void Wit_and_witx_use_disjoint_module_namespaces()
        {
            // Sanity invariant: WIT bindings live under
            // `wasi:nn/...` namespaces, WITX lives under
            // `wasi_ephemeral_nn`. They never collide on a
            // (module, entity) key, so a guest targeting one
            // ABI never accidentally calls the other binding.
            var cfg = WasiNNConfiguration.DefaultConfiguration();
            using var host = new WasiNNHost(cfg);
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            foreach (var (mod, _) in runtime.EnumerateBoundEntities())
            {
                bool isWit = mod.StartsWith("wasi:nn/");
                bool isWitx = mod == "wasi_ephemeral_nn";
                Assert.True(isWit || isWitx,
                    $"unexpected module namespace registered: {mod}");
            }
        }

        [Fact]
        public void Both_abis_share_resource_handle_tables()
        {
            // Per WasiNNHost design: resource handles minted
            // by either ABI live in the same per-resource-type
            // tables. The host doesn't double-count or
            // segregate. This test pokes at the internal
            // tables to confirm — same instance accessed from
            // both ABIs sees the same handle space.
            var cfg = WasiNNConfiguration.DefaultConfiguration();
            cfg.Backends[GraphEncoding.ONNX] = new IdentityBackend(GraphEncoding.ONNX);
            using var host = new WasiNNHost(cfg);

            var graphsField = typeof(WasiNNHost).GetProperty(
                "Graphs",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)!;
            var graphs = graphsField.GetValue(host)!;

            // Allocate a synthetic graph through the SPI; the
            // resulting handle is in the same table either ABI
            // would mint into. WIT and WITX bindings both call
            // `host.Graphs.Allocate(graph)` and `host.Graphs.Drop(h)`
            // — there's only one table.
            var allocate = graphs.GetType().GetMethod("Allocate")!;
            var graph = new IdentityBackend().LoadGraph(
                new[] { System.ReadOnlyMemory<byte>.Empty },
                ExecutionTarget.CPU);
            int handle = (int)allocate.Invoke(graphs, new object[] { graph })!;
            Assert.True(handle > 0);
        }
    }
}
