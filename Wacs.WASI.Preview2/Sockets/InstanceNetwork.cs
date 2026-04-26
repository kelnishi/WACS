// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Sockets
{
    /// <summary>
    /// Host-side surface for
    /// <c>wasi:sockets/instance-network@0.2.x</c>.
    /// <code>
    /// interface instance-network {
    ///     instance-network: func() -&gt; network;
    /// }
    /// </code>
    /// Returns the instance-wide network capability the guest
    /// can use to create sockets.
    /// </summary>
    public interface IInstanceNetwork
    {
        Network InstanceNetwork();
    }

    /// <summary>Default <see cref="IInstanceNetwork"/> impl —
    /// returns a fresh, empty Network capability. Hosts that
    /// want to gate access (e.g. only allow loopback) should
    /// substitute a Network subclass with their own
    /// policy.</summary>
    public sealed class InstanceNetworkSource : IInstanceNetwork
    {
        public Network InstanceNetwork() => new Network();
    }
}
