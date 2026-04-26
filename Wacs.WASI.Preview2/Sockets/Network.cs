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
    /// Host representation of <c>wasi:sockets/network@0.2.x</c>'s
    /// <c>network</c> resource. The WIT type carries no
    /// methods in 0.2.3 — it's an opaque capability handle
    /// that gates which sockets a guest can create. Pass to
    /// tcp-create-socket / udp-create-socket / etc.
    ///
    /// <para>v0 ships the marker. The actual socket creation
    /// methods (tcp-create-socket / udp-create-socket / etc.)
    /// arrive when their respective interfaces ship — each
    /// needs the network handle as one of its params.</para>
    /// </summary>
    [WasiResource("network")]
    public class Network : IDisposable
    {
        public virtual void Dispose() { }
    }
}
