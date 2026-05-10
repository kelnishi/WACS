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

namespace Wacs.WASI.NN.OnnxRuntimeGenAI
{
    /// <summary>
    /// Parameterless <see cref="IBindable"/> for the
    /// <see cref="OnnxGenAIBackend"/>. Scans
    /// <c>$WACS_WASINN_GENAI_DIR</c> for first-level subdirectories
    /// that look like GenAI model directories (contain
    /// <c>genai_config.json</c>) and registers each under the
    /// subdirectory name. So:
    ///
    /// <code>
    ///   export WACS_WASINN_GENAI_DIR=/path/to/models
    ///   ls /path/to/models
    ///   #=> gemma-3-270m-it/  qwen2.5-1.5b-instruct/  phi-4-mini/
    ///   wacs run --wasip2 --bind Wacs.WASI.NN.OnnxRuntimeGenAI.dll my.wasm
    /// </code>
    ///
    /// <para>Lets a guest call
    /// <c>graph.load-by-name("gemma-3-270m-it")</c> and have it
    /// resolve to
    /// <c>/path/to/models/gemma-3-270m-it/</c> — the entire
    /// directory, which GenAI's <c>Model</c> ctor unpacks.</para>
    ///
    /// <para>Wires through <c>LoadByNameBackend</c> only. The
    /// <c>Backends[ONNX]</c> slot is left alone so the regular
    /// <see cref="N:Wacs.WASI.NN.OnnxRuntime"/> backend can still
    /// handle byte-loaded <c>graph.load</c> in the same process.
    /// Composes alongside the regular ONNX backend — each handles
    /// its preferred entry point.</para>
    /// </summary>
    public sealed class WasiNNOnnxGenAIBindable : IBindable, IDisposable
    {
        private const string EnvVarName = "WACS_WASINN_GENAI_DIR";

        private readonly WasiNNHost _host;

        public WasiNNOnnxGenAIBindable()
        {
            var registry = BuildEnvRegistry();
            var backend = new OnnxGenAIBackend(name =>
                registry.TryGetValue(name, out var dir) ? dir : null);
            var config = WasiNNConfiguration.DefaultConfiguration();
            // Wire through load-by-name only; leave Backends[ONNX]
            // untouched so the regular OnnxBackend can still serve
            // byte-loaded ONNX through graph.load.
            config.LoadByNameBackend = backend;
            _host = new WasiNNHost(config);
        }

        public void BindToRuntime(WasmRuntime runtime)
            => _host.BindToRuntime(runtime);

        public void Dispose() => _host.Dispose();

        // Scan WACS_WASINN_GENAI_DIR for first-level subdirectories
        // that contain genai_config.json (the GenAI model
        // descriptor). Each match registers under its directory
        // name. Empty registry on miss / unset / IO error — by
        // design: guests get NotFound rather than a hard failure
        // at bind time.
        private static Dictionary<string, string> BuildEnvRegistry()
        {
            var dir = Environment.GetEnvironmentVariable(EnvVarName);
            var registry = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(dir)) return registry;
            try
            {
                if (!Directory.Exists(dir)) return registry;
                foreach (var modelDir in Directory.EnumerateDirectories(dir))
                {
                    var configPath = Path.Combine(
                        modelDir, "genai_config.json");
                    if (!File.Exists(configPath)) continue;
                    var name = Path.GetFileName(modelDir);
                    if (!string.IsNullOrEmpty(name))
                        registry[name!] = modelDir;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return registry;
        }
    }
}
