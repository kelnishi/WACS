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

namespace Wacs.WASI.NN.MLNet
{
    /// <summary>
    /// Parameterless <see cref="IBindable"/> adapter that wires a
    /// fresh <see cref="WasiNNHost"/> with the
    /// <see cref="MLNetBackend"/> registered for
    /// <see cref="GraphEncoding.ONNX"/>. Drop the
    /// <c>Wacs.WASI.NN.MLNet.dll</c> on the load path and pass
    /// <c>--bind Wacs.WASI.NN.MLNet</c> to <c>wacs run</c>: the
    /// CLI's <see cref="IBindable"/>-discovery pass picks this
    /// type up automatically and a stock ONNX component runs end-
    /// to-end with no shim.
    ///
    /// <para>For embedders using WACS as a library, prefer the
    /// inline <c>runtime.UseWasiNN(b => b.AddBackend(
    /// GraphEncoding.ONNX, new MLNetBackend()))</c> extension —
    /// this adapter is purely for the <c>--bind</c> CLI path
    /// where <see cref="BindingLoader"/> requires a
    /// parameterless ctor.</para>
    ///
    /// <para>The default <see cref="MLNetBackend"/> ctor uses an
    /// ambient <c>MLContext</c> with default seed; swap to the
    /// <see cref="MLNetBackend(Microsoft.ML.MLContext)"/> overload
    /// programmatically when reproducibility or pipeline-shared
    /// state matters.</para>
    /// </summary>
    public sealed class WasiNNMLNetBindable : IBindable, IDisposable
    {
        private readonly WasiNNHost _host;

        public WasiNNMLNetBindable()
        {
            var config = WasiNNConfiguration.DefaultConfiguration();
            config.Backends[GraphEncoding.ONNX] = new MLNetBackend();
            _host = new WasiNNHost(config);
        }

        public void BindToRuntime(WasmRuntime runtime)
            => _host.BindToRuntime(runtime);

        public void Dispose() => _host.Dispose();
    }
}
