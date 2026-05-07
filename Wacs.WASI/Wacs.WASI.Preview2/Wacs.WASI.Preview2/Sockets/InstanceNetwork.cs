// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Sockets
{
    // The IInstanceNetwork interface is now emitted by the
    // source generator from
    // wit/deps/sockets/instance-network.wit. This file retains
    // only the default conservative impl — the generated
    // interface is authoritative.

    /// <summary>Default <see cref="IInstanceNetwork"/> impl —
    /// returns a fresh, empty Network capability. Hosts that
    /// want to gate access (e.g. only allow loopback) should
    /// substitute a Network subclass with their own
    /// policy.</summary>
    public sealed class InstanceNetworkSource : IInstanceNetwork
    {
        public INetwork InstanceNetwork() => new Network();
    }
}
