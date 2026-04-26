// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Sockets
{
    /// <summary>
    /// Host-side surface for
    /// <c>wasi:sockets/tcp-create-socket@0.2.x</c>.
    /// <code>
    /// interface tcp-create-socket {
    ///     create-tcp-socket: func(address-family: ip-address-family)
    ///         -&gt; result&lt;tcp-socket, error-code&gt;;
    /// }
    /// </code>
    /// </summary>
    public interface ITcpCreateSocket
    {
        [WasiErrorResult]
        TcpSocket CreateTcpSocket(IpAddressFamily addressFamily);
    }

    public interface IUdpCreateSocket
    {
        [WasiErrorResult]
        UdpSocket CreateUdpSocket(IpAddressFamily addressFamily);
    }

    /// <summary>Default <see cref="ITcpCreateSocket"/> impl —
    /// hands out a fresh marker TcpSocket. Real socket
    /// creation hooks into <see cref="System.Net.Sockets"/>
    /// in a follow-up.</summary>
    public sealed class TcpCreateSocket : ITcpCreateSocket
    {
        [WasiErrorResult]
        public TcpSocket CreateTcpSocket(IpAddressFamily addressFamily) =>
            new TcpSocket(addressFamily);
    }

    public sealed class UdpCreateSocket : IUdpCreateSocket
    {
        [WasiErrorResult]
        public UdpSocket CreateUdpSocket(IpAddressFamily addressFamily) =>
            new UdpSocket(addressFamily);
    }
}
