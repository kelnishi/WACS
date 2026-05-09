// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Sockets
{
    /// <summary>
    /// Host representation of <c>wasi:sockets/network@0.2.8</c>'s
    /// <c>network</c> resource. The WIT type carries no
    /// methods in 0.2.3 — it's an opaque capability handle
    /// that gates which sockets a guest can create. Pass to
    /// tcp-create-socket / udp-create-socket / etc.
    ///
    /// <para>Implements <see cref="INetwork"/> directly; the
    /// generated interface is empty (matches the WIT shape).
    /// The top-level <c>network-error-code</c> function on the
    /// <c>network</c> interface (<see cref="INetworkFuncs"/>)
    /// is not currently bound — concrete embeddings that need
    /// to classify <see cref="Wacs.WASI.Preview2.Io.Error"/>
    /// instances as network errors should add the binding
    /// alongside their host impl.</para>
    /// </summary>
    [WasiResource("network")]
    public class Network : INetwork, IDisposable
    {
        public virtual void Dispose() { }
    }
}
