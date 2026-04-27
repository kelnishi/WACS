// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Sockets
{
    /// <summary>
    /// Host representation of
    /// <c>wasi:sockets/ip-name-lookup@0.2.3</c>'s
    /// <c>resolve-address-stream</c> resource. A resolver
    /// pull-stream — guest calls
    /// <see cref="ResolveNextAddress"/> in a loop, getting
    /// one address at a time until None signals "exhausted".
    ///
    /// <para>v0 base class returns Ok(None) on every call
    /// (empty resolution). Concrete hosts override
    /// <see cref="ResolveNextAddress"/> to surface real DNS
    /// lookup results — addresses are returned as
    /// <see cref="IpAddress"/> variant cases
    /// (<see cref="IpAddress.IpAddressIpv4"/> /
    /// <see cref="IpAddress.IpAddressIpv6"/>).</para>
    /// </summary>
    [WasiResource("resolve-address-stream")]
    public class ResolveAddressStream : IResolveAddressStream, IDisposable
    {
        /// <summary>Pull the next resolved address, or None
        /// when the resolution is exhausted. Default returns
        /// Ok(None) — empty stream.</summary>
        public virtual Result<Option<IpAddress>, ErrorCode> ResolveNextAddress()
            => Result<Option<IpAddress>, ErrorCode>.FromOk(
                Option<IpAddress>.None);

        /// <summary>Pollable that fires when more addresses
        /// are available or the stream is fully resolved.
        /// Default returns an always-ready pollable.</summary>
        public virtual IPollable Subscribe() => new Pollable();

        public virtual void Dispose() { }
    }

    /// <summary>Default <see cref="IIpNameLookup"/> impl —
    /// returns an empty <see cref="ResolveAddressStream"/>
    /// regardless of input. Concrete hosts override to wire
    /// real DNS resolution (System.Net.Dns.GetHostAddressesAsync,
    /// etc.).</summary>
    public sealed class IpNameLookup : IIpNameLookup
    {
        public Result<IResolveAddressStream, ErrorCode> ResolveAddresses(
            INetwork network, string name)
            => Result<IResolveAddressStream, ErrorCode>.FromOk(
                new ResolveAddressStream());
    }
}
