// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.ComponentModel.Runtime;

namespace Wacs.WASI.Preview2.Sockets
{
    // The ITcpCreateSocket / IUdpCreateSocket interfaces are
    // now emitted by the source generator from
    // wit/deps/sockets/{tcp,udp}-create-socket.wit. This file
    // retains only the default conservative impls — the
    // generated interfaces are authoritative.

    /// <summary>Default <see cref="ITcpCreateSocket"/> impl —
    /// hands out a fresh marker TcpSocket. Real socket
    /// creation hooks into <see cref="System.Net.Sockets"/>
    /// in a follow-up.</summary>
    public sealed class TcpCreateSocket : ITcpCreateSocket
    {
        public Result<ITcpSocket, ErrorCode> CreateTcpSocket(
            IpAddressFamily addressFamily)
            => Result<ITcpSocket, ErrorCode>.FromOk(
                new TcpSocket(addressFamily));
    }

    public sealed class UdpCreateSocket : IUdpCreateSocket
    {
        public Result<IUdpSocket, ErrorCode> CreateUdpSocket(
            IpAddressFamily addressFamily)
            => Result<IUdpSocket, ErrorCode>.FromOk(
                new UdpSocket(addressFamily));
    }
}
