// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using TorchSharp;
using Wacs.WASI.NN;

namespace Wacs.WASI.NN.TorchSharp
{
    /// <summary>
    /// Wraps a loaded <see cref="torch.jit.ScriptModule"/>. The
    /// module is in <c>eval()</c> mode (autograd / dropout off)
    /// before this graph is constructed; multiple
    /// <see cref="TorchSharpContext"/>s minted from
    /// <see cref="CreateContext"/> share the module — TorchScript
    /// modules are stateless across forward calls when in eval
    /// mode, so concurrent contexts dispatching against one module
    /// are safe in the inference path.
    ///
    /// <para>The module is disposed when this graph is dropped,
    /// which the WIT <c>[resource-drop]graph</c> binding fires
    /// when the guest releases the handle.</para>
    /// </summary>
    internal sealed class TorchSharpGraph : IBackendGraph
    {
        // Internal so TorchSharpContext can call forward / call
        // without going through reflection or a forwarding
        // accessor that complicates the SPI.
        internal torch.jit.ScriptModule Module { get; }
        private bool _disposed;

        public TorchSharpGraph(torch.jit.ScriptModule module)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        public IBackendContext CreateContext()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TorchSharpGraph));
            return new TorchSharpContext(this);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Module.Dispose();
        }
    }
}
