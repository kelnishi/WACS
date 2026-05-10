// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using Wacs.Core.Runtime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN.TorchSharp
{
    /// <summary>
    /// Parameterless <see cref="IBindable"/> adapter that wires a
    /// fresh <see cref="WasiNNHost"/> with the
    /// <see cref="TorchSharpBackend"/> registered for both
    /// <see cref="GraphEncoding.PyTorch"/> (encoding-keyed) and
    /// <see cref="WasiNNConfiguration.LoadByNameBackend"/>
    /// (name-keyed) lookups.
    ///
    /// <para>The bindable scans the directory pointed at by the
    /// <c>WACS_WASINN_TORCH_DIR</c> environment variable for
    /// TorchScript files (<c>*.pt</c> and <c>*.ts</c>) at
    /// construction time. Each file registers under its
    /// filename-sans-extension as a load-by-name key; the guest
    /// can call <c>graph.load-by-name("resnet50")</c> against a
    /// host model at <c>$WACS_WASINN_TORCH_DIR/resnet50.pt</c>.
    /// Mirrors the GGUF-directory convention
    /// <see cref="Wacs.WASI.NN.LlamaSharp.WasiNNLlamaSharpBindable"/>
    /// uses for llama.cpp.</para>
    ///
    /// <para>When <c>WACS_WASINN_TORCH_DIR</c> is unset or empty,
    /// the registry is empty — guests calling load-by-name get a
    /// <c>NotFound</c> error. The byte-loaded
    /// <c>graph.load(builders, encoding=PyTorch, target)</c> path
    /// continues to work (the canonical-ABI lift hands a single
    /// builder buffer through). For richer registries (per-model
    /// device choice, custom file extensions, HF cache scan),
    /// embedders using WACS as a library should construct
    /// <see cref="TorchSharpBackend"/> directly via
    /// <c>runtime.UseWasiNN(b => b.AddBackend(GraphEncoding.PyTorch,
    /// new TorchSharpBackend(...)))</c>.</para>
    /// </summary>
    public sealed class WasiNNTorchSharpBindable : IBindable, IDisposable
    {
        private const string EnvVarName = "WACS_WASINN_TORCH_DIR";

        private readonly WasiNNHost _host;

        public WasiNNTorchSharpBindable()
        {
            var registry = BuildEnvRegistry();
            var backend = TorchSharpBackend.FromPaths(registry);
            var config = WasiNNConfiguration.DefaultConfiguration();
            config.Backends[GraphEncoding.PyTorch] = backend;
            // Route load-by-name through this backend explicitly
            // so big TorchScript files skip the canonical-ABI
            // byte-flow indirection.
            config.LoadByNameBackend = backend;
            _host = new WasiNNHost(config);
        }

        public void BindToRuntime(WasmRuntime runtime)
            => _host.BindToRuntime(runtime);

        public void Dispose() => _host.Dispose();

        // Scan $WACS_WASINN_TORCH_DIR for TorchScript files;
        // register each under filename-sans-extension. *.pt is
        // PyTorch's canonical extension; *.ts the alternative
        // some pipelines use (TorchScript-specific). Empty
        // registry on miss / unset / IO error — fail-soft, like
        // the LlamaSharp sibling.
        private static Dictionary<string, string> BuildEnvRegistry()
        {
            var dir = Environment.GetEnvironmentVariable(EnvVarName);
            var registry = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(dir)) return registry;
            try
            {
                if (!Directory.Exists(dir)) return registry;
                foreach (var path in Directory.EnumerateFiles(dir,
                    "*.pt", SearchOption.TopDirectoryOnly))
                    Register(registry, path);
                foreach (var path in Directory.EnumerateFiles(dir,
                    "*.ts", SearchOption.TopDirectoryOnly))
                    Register(registry, path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return registry;
        }

        private static void Register(IDictionary<string, string> registry,
            string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(name))
                registry[name!] = path;
        }
    }
}
