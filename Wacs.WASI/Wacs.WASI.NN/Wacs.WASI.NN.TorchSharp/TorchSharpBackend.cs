// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using TorchSharp;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN.TorchSharp
{
    /// <summary>
    /// TorchSharp / libtorch backend for
    /// <see cref="GraphEncoding.PyTorch"/>. Loads TorchScript
    /// modules (<c>torch.jit.save</c> output, typically <c>.pt</c>
    /// or <c>.ts</c>) from either byte buffers or named files, and
    /// runs inference through libtorch's C++ runtime.
    ///
    /// <para>Lifetime: each <see cref="LoadGraph"/> /
    /// <see cref="LoadGraphByName"/> call constructs one
    /// <see cref="torch.jit.ScriptModule"/>; multiple
    /// <see cref="IBackendContext"/>s minted from a single graph
    /// share that module (TorchScript modules are stateless across
    /// forward calls when in <c>eval()</c> mode, which the backend
    /// switches them to during load). The module is disposed when
    /// the <see cref="TorchSharpGraph"/> is dropped, which the WIT
    /// <c>[resource-drop]graph</c> binding fires when the guest
    /// releases the handle.</para>
    ///
    /// <para>Both byte-loaded and named-file paths are supported:
    /// <list type="bullet">
    /// <item>Byte-loaded (<see cref="LoadGraph"/>) — practical for
    /// small/medium models (~ &lt; 500 MB) where the canonical-ABI
    /// lift cost is acceptable.</item>
    /// <item>Named (<see cref="LoadGraphByName"/>) — preferred for
    /// big models. The embedder supplies a <c>Func&lt;string, string?&gt;</c>
    /// resolving names to filesystem paths, and TorchSharp opens
    /// the file directly via <c>torch.jit.load(path)</c> with no
    /// host-side copy.</item>
    /// </list>
    /// </para>
    ///
    /// <para>Execution targets: CPU is the default. GPU /
    /// Metal / TPU dispatch isn't auto-wired in v0 — embedders
    /// who want CUDA / MPS swap the <c>TorchSharp-cpu</c> meta-
    /// package for <c>TorchSharp-cuda-12.1</c> /
    /// <c>TorchSharp-macos-x64</c> in their consumer csproj and
    /// pass a <see cref="ExecutionTarget"/> through the WIT
    /// <c>graph.load</c> / <c>graph.load-by-name</c> call. Guest
    /// <see cref="ExecutionTarget.TPU"/> requests trip
    /// <see cref="ErrorCode.UnsupportedOperation"/> — libtorch
    /// has no public TPU dispatch.</para>
    /// </summary>
    public sealed class TorchSharpBackend : IBackend
    {
        private readonly Func<string, string?>? _registry;

        /// <summary>
        /// Construct a backend with no name → file-path registry.
        /// Only <see cref="LoadGraph"/> (byte-loaded) is usable;
        /// <see cref="LoadGraphByName"/> trips
        /// <see cref="ErrorCode.NotFound"/> for every name.
        /// </summary>
        public TorchSharpBackend() : this(null) { }

        /// <summary>
        /// Construct a backend with an embedder-supplied name →
        /// file-path registry. Returning <c>null</c> from the
        /// registry signals "name not registered" and the
        /// binding lifts that to <see cref="ErrorCode.NotFound"/>.
        /// </summary>
        public TorchSharpBackend(Func<string, string?>? registry)
        {
            _registry = registry;
        }

        /// <summary>
        /// Convenience: build a backend from a static name → path
        /// table. Useful for the simple "drop a few TorchScript
        /// files in a directory and serve them" embedder flow.
        /// </summary>
        public static TorchSharpBackend FromPaths(IDictionary<string, string> namesToPaths)
        {
            if (namesToPaths == null)
                throw new ArgumentNullException(nameof(namesToPaths));
            return new TorchSharpBackend(name =>
                namesToPaths.TryGetValue(name, out var path) ? path : null);
        }

        public IReadOnlyCollection<GraphEncoding> SupportedEncodings { get; }
            = new[] { GraphEncoding.PyTorch };

        public IBackendGraph LoadGraph(
            IReadOnlyList<ReadOnlyMemory<byte>> builders,
            ExecutionTarget target)
        {
            if (target == ExecutionTarget.TPU)
                throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    "TorchSharpBackend does not support ExecutionTarget.TPU; "
                    + "swap the TorchSharp-cpu meta-package for a GPU "
                    + "variant (TorchSharp-cuda-12.1 / -macos-x64) and use "
                    + "ExecutionTarget.GPU instead.");
            if (builders.Count == 0)
                throw new WasiNNException(
                    ErrorCode.InvalidArgument,
                    "graph.load received an empty builder list");

            // TorchScript is a single self-contained zip; the
            // canonical case is one builder. Multi-builder input
            // gets concatenated defensively (no real PyTorch
            // guests we know of split a model across builders,
            // but the spec allows it for backends like OpenVINO).
            byte[] modelBytes = ConcatBuilders(builders);

            try
            {
                // torch.jit.load(byte[]) opens an in-memory
                // zip — no temp file or host-side copy beyond
                // the canonical-ABI lift.
                var module = torch.jit.load(modelBytes);
                module.eval();   // disable autograd / dropout —
                                 // the wasi-nn flow is
                                 // inference-only.
                return new TorchSharpGraph(module);
            }
            catch (Exception ex)
            {
                throw new WasiNNException(
                    ErrorCode.RuntimeError,
                    $"torch.jit.load failed: {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }
        }

        public IBackendGraph LoadGraphByName(string name, ExecutionTarget target)
        {
            if (target == ExecutionTarget.TPU)
                throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    "TorchSharpBackend does not support ExecutionTarget.TPU.");
            if (_registry == null)
                throw new WasiNNException(
                    ErrorCode.NotFound,
                    "TorchSharpBackend has no internal name → path registry; "
                    + "construct it with TorchSharpBackend.FromPaths(...) or "
                    + "configure WasiNNConfiguration.NamedModelResolver to "
                    + "the byte-flow path instead.");

            var path = _registry(name);
            if (path == null)
                throw new WasiNNException(
                    ErrorCode.NotFound,
                    $"TorchScript model '{name}' not registered with TorchSharpBackend");
            if (!File.Exists(path))
                throw new WasiNNException(
                    ErrorCode.NotFound,
                    $"TorchScript model '{name}' resolves to '{path}' which doesn't exist");

            try
            {
                var module = torch.jit.load(path);
                module.eval();
                return new TorchSharpGraph(module);
            }
            catch (Exception ex)
            {
                throw new WasiNNException(
                    ErrorCode.RuntimeError,
                    $"Failed to load TorchScript '{name}' from '{path}': {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }
        }

        private static byte[] ConcatBuilders(IReadOnlyList<ReadOnlyMemory<byte>> builders)
        {
            if (builders.Count == 1)
                return builders[0].ToArray();
            int total = 0;
            for (int i = 0; i < builders.Count; i++)
                total += builders[i].Length;
            var concat = new byte[total];
            int offset = 0;
            for (int i = 0; i < builders.Count; i++)
            {
                builders[i].Span.CopyTo(concat.AsSpan(offset));
                offset += builders[i].Length;
            }
            return concat;
        }
    }
}
