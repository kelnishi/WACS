// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Sockets
{
    /// <summary>
    /// Host representation of
    /// <c>wasi:sockets/ip-name-lookup@0.2.x</c>'s
    /// <c>resolve-address-stream</c> resource. A resolver
    /// pull-stream — guest calls
    /// <see cref="ResolveNextAddress"/> in a loop, getting
    /// one address at a time until None signals "exhausted".
    ///
    /// <para>v0 base class returns None on every call (empty
    /// resolution). Concrete hosts override
    /// <see cref="ResolveNextAddress"/> to surface real DNS
    /// lookup results — the addresses returned should be
    /// <see cref="Ipv4SocketAddress"/>-style 4-byte tuples
    /// (no port) or 8× u16 tuples for IPv6, expressed as
    /// <see cref="byte"/>[] / <see cref="ushort"/>[] in the
    /// returned <see cref="IpAddressEnumerationItem"/>.</para>
    /// </summary>
    [WasiResource("resolve-address-stream")]
    public class ResolveAddressStream : IDisposable
    {
        /// <summary>Pull the next resolved address, or null
        /// when the resolution is exhausted. Default returns
        /// null (empty stream).</summary>
        [WasiErrorResult]
        [WasiMethodName("resolve-next-address")]
        public virtual IpAddressEnumerationItem? ResolveNextAddress()
            => null;

        /// <summary>Pollable that fires when more addresses
        /// are available or the stream is fully resolved.
        /// Default returns an always-ready pollable.</summary>
        public virtual Pollable Subscribe() => new Pollable();

        public virtual void Dispose() { }
    }

    /// <summary>One resolved address from
    /// <see cref="ResolveAddressStream"/> — either an IPv4
    /// 4-byte address or an IPv6 8-group address. Mirrors
    /// the WIT shape <c>variant ip-address { ipv4(ipv4-address),
    /// ipv6(ipv6-address) }</c>; concrete subclasses pick
    /// the case at construction.</summary>
    public abstract class IpAddressEnumerationItem
    {
        public IpAddressFamily Family { get; }
        protected IpAddressEnumerationItem(IpAddressFamily family)
        {
            Family = family;
        }
    }

    /// <summary>IPv4 case of
    /// <see cref="IpAddressEnumerationItem"/>.</summary>
    public sealed class Ipv4Address : IpAddressEnumerationItem
    {
        public byte[] Address { get; }
        public Ipv4Address(byte[] address)
            : base(IpAddressFamily.Ipv4)
        {
            if (address == null || address.Length != 4)
                throw new ArgumentException(
                    "IPv4 address must be 4 bytes.",
                    nameof(address));
            Address = address;
        }
    }

    /// <summary>IPv6 case.</summary>
    public sealed class Ipv6Address : IpAddressEnumerationItem
    {
        public ushort[] Address { get; }
        public Ipv6Address(ushort[] address)
            : base(IpAddressFamily.Ipv6)
        {
            if (address == null || address.Length != 8)
                throw new ArgumentException(
                    "IPv6 address must be 8 u16 groups.",
                    nameof(address));
            Address = address;
        }
    }

    /// <summary>Host-side surface for
    /// <c>wasi:sockets/ip-name-lookup</c>'s sole top-level
    /// function:
    /// <code>
    /// resolve-addresses: func(network: borrow&lt;network&gt;, name: string)
    ///     -&gt; result&lt;own&lt;resolve-address-stream&gt;, error-code&gt;;
    /// </code>
    /// </summary>
    public interface IIpNameLookup
    {
        /// <summary>Initiate a DNS lookup for
        /// <paramref name="name"/>; returns a
        /// <see cref="ResolveAddressStream"/> the guest
        /// pulls addresses from.</summary>
        ResolveAddressStream ResolveAddresses(Network network, string name);
    }

    /// <summary>Default <see cref="IIpNameLookup"/> impl —
    /// returns an empty <see cref="ResolveAddressStream"/>
    /// regardless of input. Concrete hosts override to wire
    /// real DNS resolution (System.Net.Dns.GetHostAddressesAsync,
    /// etc.).</summary>
    public sealed class IpNameLookup : IIpNameLookup
    {
        [WasiErrorResult]
        [WasiMethodName("resolve-addresses")]
        public ResolveAddressStream ResolveAddresses(Network network,
            string name) => new ResolveAddressStream();
    }
}
