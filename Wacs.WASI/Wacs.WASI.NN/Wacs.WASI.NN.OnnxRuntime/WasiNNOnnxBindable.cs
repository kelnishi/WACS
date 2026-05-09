// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN.OnnxRuntime
{
    /// <summary>
    /// Parameterless <see cref="IBindable"/> adapter that wires a
    /// fresh <see cref="WasiNNHost"/> with the
    /// <see cref="OnnxBackend"/> registered for
    /// <see cref="GraphEncoding.ONNX"/>. Drop the
    /// <c>Wacs.WASI.NN.OnnxRuntime.dll</c> on the load path and
    /// pass <c>--bind Wacs.WASI.NN.OnnxRuntime.dll</c> to
    /// <c>wacs run</c>: the CLI's <see cref="IBindable"/>-discovery
    /// pass picks this type up automatically and a stock ONNX
    /// component runs end-to-end with no shim.
    ///
    /// <para>For embedders using WACS as a library, prefer the
    /// inline <c>runtime.UseWasiNN(b => b.AddBackend(
    /// GraphEncoding.ONNX, new OnnxBackend()))</c> extension —
    /// this adapter is purely for the <c>--bind</c> CLI path
    /// where <see cref="BindingLoader"/> requires a
    /// parameterless ctor.</para>
    ///
    /// <para>Sibling backends (<c>Wacs.WASI.NN.MLNet</c>,
    /// <c>Wacs.WASI.NN.LlamaSharp</c>) ship their own analog
    /// adapter — keeps the per-package shape identical so
    /// <c>--bind</c> works for any wasi-nn backend WACS ships.</para>
    /// </summary>
    public sealed class WasiNNOnnxBindable : IBindable, IDisposable
    {
        private readonly WasiNNHost _host;

        public WasiNNOnnxBindable()
        {
            var config = WasiNNConfiguration.DefaultConfiguration();
            config.Backends[GraphEncoding.ONNX] = new OnnxBackend();
            _host = new WasiNNHost(config);
        }

        public void BindToRuntime(WasmRuntime runtime)
            => _host.BindToRuntime(runtime);

        public void Dispose() => _host.Dispose();
    }
}
