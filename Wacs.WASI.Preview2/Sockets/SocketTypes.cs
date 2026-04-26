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
    /// IP address family per WIT
    /// <c>wasi:sockets/network@0.2.x</c>:
    /// <code>
    /// enum ip-address-family { ipv4, ipv6 }
    /// </code>
    /// </summary>
    public enum IpAddressFamily : int
    {
        Ipv4 = 0,
        Ipv6 = 1,
    }

    /// <summary>
    /// Subset of WASI's <c>error-code</c> variant. The full
    /// 0.2.3 enum has ~37 cases; this v0 subset covers the
    /// common ones. Wire form is a 1-byte discriminator (the
    /// canon-lower wrapper writes it directly).
    /// </summary>
    public enum WasiErrorCode : byte
    {
        Unknown = 0,
        AccessDenied = 1,
        NotPermitted = 2,
        AlreadyConnected = 3,
        AddressNotBindable = 4,
        ConcurrencyConflict = 5,
        ConnectionRefused = 6,
        ConnectionReset = 7,
        InvalidArgument = 8,
        InvalidState = 9,
        NotInProgress = 10,
        TimeoutExpired = 11,
        WouldBlock = 12,
    }

    /// <summary>Host representation of <c>tcp-socket</c>.
    /// Marker in v0 — bind / connect / send / recv methods
    /// are deferred. Each instance is tagged with the
    /// <see cref="IpAddressFamily"/> it was created for.</summary>
    [WasiResource("tcp-socket")]
    public class TcpSocket : IDisposable
    {
        public IpAddressFamily Family { get; }
        public TcpSocket(IpAddressFamily family) { Family = family; }
        public virtual void Dispose() { }
    }

    /// <summary>Host representation of <c>udp-socket</c>.
    /// Same marker pattern as <see cref="TcpSocket"/>.</summary>
    [WasiResource("udp-socket")]
    public class UdpSocket : IDisposable
    {
        public IpAddressFamily Family { get; }
        public UdpSocket(IpAddressFamily family) { Family = family; }
        public virtual void Dispose() { }
    }
}
