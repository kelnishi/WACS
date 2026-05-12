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

namespace Wacs.WASI.NN.LlamaSharp
{
    /// <summary>
    /// Parameterless <see cref="IBindable"/> adapter that wires a
    /// fresh <see cref="WasiNNHost"/> with the
    /// <see cref="LlamaSharpBackend"/> registered for
    /// <see cref="GraphEncoding.GGML"/>.
    ///
    /// <para>LlamaSharp models are GB-scale and resolved by name
    /// rather than embedded in the wasm. The parameterless adapter
    /// reads the <c>WACS_WASINN_GGUF_DIR</c> environment variable
    /// — when set, every <c>*.gguf</c> file in that directory is
    /// registered under its filename-without-extension. So:</para>
    ///
    /// <code>
    ///   export WACS_WASINN_GGUF_DIR=/path/to/models
    ///   wacs run --wasip2 --bind Wacs.WASI.NN.LlamaSharp my.wasm
    /// </code>
    ///
    /// <para>Lets a guest call
    /// <c>graph.load-by-name("llama-3-8b-instruct")</c> and have
    /// it resolve to <c>/path/to/models/llama-3-8b-instruct.gguf</c>.</para>
    ///
    /// <para>When <c>WACS_WASINN_GGUF_DIR</c> is unset or empty,
    /// the registry is empty — guests calling load-by-name get a
    /// <c>NotFound</c> error. For richer registries (HF cache
    /// scan, custom paths, per-model <c>ModelParams</c>), embedders
    /// using WACS as a library should construct
    /// <see cref="LlamaSharpBackend"/> directly and pass it through
    /// <c>runtime.UseWasiNN(b => b.AddBackend(...))</c>.</para>
    /// </summary>
    public sealed class WasiNNLlamaSharpBindable
        : IBindable, IWasiNNBackendRegistration, IDisposable
    {
        private const string EnvVarName = "WACS_WASINN_GGUF_DIR";

        private readonly WasiNNHost _host;

        public WasiNNLlamaSharpBindable()
        {
            var config = WasiNNConfiguration.DefaultConfiguration();
            ConfigureConfiguration(config);
            _host = new WasiNNHost(config);
        }

        public void ConfigureConfiguration(WasiNNConfiguration config)
        {
            var registry = BuildEnvRegistry();
            var backend = new LlamaSharpBackend(name =>
                registry.TryGetValue(name, out var path)
                    ? new GgufModelEntry(path)
                    : (GgufModelEntry?)null);
            config.Backends[GraphEncoding.GGML] = backend;
            // LlamaSharpBackend's only practical entry is load-by-
            // name (the byte-buffer path traps for GB-scale GGUFs);
            // route load-by-name through this backend explicitly
            // rather than dropping back to the generic resolver.
            config.LoadByNameBackend = backend;
        }

        public void BindToRuntime(WasmRuntime runtime)
            => _host.BindToRuntime(runtime);

        public void Dispose() => _host.Dispose();

        // Scan the directory in $WACS_WASINN_GGUF_DIR for *.gguf
        // files; register each under its filename-sans-extension.
        // Empty registry on miss / unset / IO error — by design,
        // not a hard failure (the bindable still wires the backend
        // for any guest that loads bytes inline, even though
        // LlamaSharpBackend doesn't currently support that path).
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
                    "*.gguf", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (!string.IsNullOrEmpty(name))
                        registry[name!] = path;
                }
            }
            catch (IOException)
            {
                // Read errors leave registry empty; guests get
                // NotFound rather than a wasi-nn trap.
            }
            catch (UnauthorizedAccessException) { }
            return registry;
        }
    }
}
