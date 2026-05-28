// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Wacs.WASI.Preview3.Sockets
{
    /// <summary>
    /// Default <see cref="IUdpSocket"/> implementation backed
    /// by <see cref="System.Net.Sockets.Socket"/>. Simpler
    /// state machine than TCP (no listen / connecting):
    /// <c>unbound → bound → optionally-connected</c>.
    ///
    /// <para><b>Scope.</b> Phase 5 Slice P wires the static
    /// factory + the simple primitive getters/setters. Bind /
    /// Connect / Send / Receive need the canon-async variant-
    /// arg lowering and ship in a follow-up.</para>
    /// </summary>
    public sealed class UdpSocket : IUdpSocket, IDisposable
    {
        public enum State
        {
            Unbound,
            Bound,
            Connected,
            Closed,
        }

        private readonly Socket _socket;
        private readonly IpAddressFamily _family;
        private State _state = State.Unbound;

        public State CurrentState => _state;
        internal Socket UnderlyingSocket => _socket;

        public UdpSocket(IpAddressFamily family)
        {
            _family = family;
            var addrFamily = family == IpAddressFamily.Ipv4
                ? AddressFamily.InterNetwork
                : AddressFamily.InterNetworkV6;
            try
            {
                _socket = new Socket(
                    addrFamily, SocketType.Dgram, ProtocolType.Udp);
            }
            catch (SocketException sx) when (
                sx.SocketErrorCode == SocketError.AddressFamilyNotSupported)
            {
                throw new SocketsException(
                    ErrorCode.NotSupported,
                    $"address family {family} not supported.");
            }

            if (family == IpAddressFamily.Ipv6)
            {
                try { _socket.DualMode = false; } catch { /* best-effort */ }
            }
        }

        public void Dispose()
        {
            _state = State.Closed;
            _socket.Dispose();
        }

        public IpAddressFamily GetAddressFamily() => _family;

        // ---- Bind / Connect / Disconnect / Send / Receive ------------
        //
        // Variant-arg / list-arg methods pending the canon-async
        // variant flat-lowering wire-up; throw NotSupported so
        // the surface is honest.

        public void Bind(IpSocketAddress localAddress)
        {
            EnsureState(State.Unbound, "bind");
            TcpEndpointHelper.ValidateBindAddress(_family, localAddress);
            try
            {
                var ep = TcpEndpointHelper.ToIpEndPoint(localAddress);
                _socket.Bind(ep);
                _state = State.Bound;
            }
            catch (SocketException sx)
            {
                _state = State.Closed;
                throw TcpEndpointHelper.MapSocketException(sx);
            }
        }

        // UDP is connectionless. The Connect call is purely a
        // host-side convenience that pins a default destination
        // for unaddressed sends and filters received datagrams
        // by source. We track the connected endpoint manually
        // (rather than relying on .NET's Socket.Connect /
        // Disconnect lifecycle, which forbids same-endpoint
        // re-connects after a prior disconnect).
        private System.Net.IPEndPoint? _connectedRemote;

        public void Connect(IpSocketAddress remoteAddress)
        {
            // Spec: connect rejects family-mismatch, the unspec
            // address, port=0, and ipv4-mapped-ipv6 (dual-stack).
            TcpEndpointHelper.ValidateConnectAddress(_family, remoteAddress);
            // Implicit bind on connect from Unbound. Bind to the
            // same address as the connect target (port 0 for an
            // ephemeral port) so get_local_address resolves to a
            // routable IP — wildcard-bind leaves macOS's
            // getsockname returning 0.0.0.0 even after connect,
            // which the spec doesn't allow.
            if (_state == State.Unbound)
            {
                try
                {
                    var targetEp =
                        TcpEndpointHelper.ToIpEndPoint(remoteAddress);
                    var implicitLocal = new System.Net.IPEndPoint(
                        targetEp.Address, 0);
                    if (_family == IpAddressFamily.Ipv6)
                    {
                        try
                        {
                            _socket.SetSocketOption(
                                SocketOptionLevel.IPv6,
                                SocketOptionName.IPv6Only, 1);
                        }
                        catch (SocketException) { }
                    }
                    _socket.Bind(implicitLocal);
                    _state = State.Bound;
                }
                catch (SocketException sx)
                {
                    _state = State.Closed;
                    throw TcpEndpointHelper.MapSocketException(sx);
                }
            }
            if (_state != State.Bound && _state != State.Connected)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "udp-socket.connect: socket must be bound first.");
            _connectedRemote =
                TcpEndpointHelper.ToIpEndPoint(remoteAddress);
            _state = State.Connected;
        }

        public void Disconnect()
        {
            if (_state != State.Connected)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "udp-socket.disconnect: socket is not connected.");
            _connectedRemote = null;
            _state = State.Bound;
        }

        // Maximum UDP payload per the IP spec: 65,535 minus the
        // 8-byte UDP header and 20/40-byte IP header. We use the
        // simpler conservative cap of 65,507 (v4) — anything
        // larger than that yields ErrorCode.DatagramTooLarge.
        private const int MaxUdpPayloadIpv4 = 65507;
        private const int MaxUdpPayloadIpv6 = 65527;

        public async Task SendAsync(
            byte[] data, IpSocketAddress? remoteAddress = null,
            CancellationToken cancellationToken = default)
        {
            EnsureNotClosed();
            int maxPayload = _family == IpAddressFamily.Ipv4
                ? MaxUdpPayloadIpv4 : MaxUdpPayloadIpv6;
            if (data.Length > maxPayload)
                throw new SocketsException(
                    ErrorCode.DatagramTooLarge,
                    $"send: datagram is {data.Length} bytes, " +
                    $"exceeds {_family} max of {maxPayload}.");
            System.Net.IPEndPoint? destination;
            if (remoteAddress.HasValue)
            {
                var addr = remoteAddress.Value;
                if (addr.Family != _family)
                    throw new SocketsException(
                        ErrorCode.InvalidArgument,
                        "send: remote-address family does not " +
                        "match socket family.");
                ushort port = addr.Family == IpAddressFamily.Ipv4
                    ? addr.V4.Port : addr.V6.Port;
                if (port == 0)
                    throw new SocketsException(
                        ErrorCode.AddressNotBindable,
                        "send: remote port must be non-zero.");
                if (IsUnspecifiedAddress(addr))
                    throw new SocketsException(
                        ErrorCode.InvalidArgument,
                        "send: remote address must be specified.");
                // Dual-stack: ipv6 socket sending to ipv6-mapped-ipv4
                if (_family == IpAddressFamily.Ipv6
                    && IsIpv4MappedIpv6(addr))
                    throw new SocketsException(
                        ErrorCode.InvalidArgument,
                        "send: dual-stack (ipv6-mapped-ipv4) is " +
                        "not allowed.");
                destination =
                    TcpEndpointHelper.ToIpEndPoint(addr);
                // If connected, the explicit destination must
                // match the connected peer.
                if (_state == State.Connected
                    && _connectedRemote != null
                    && !destination.Equals(_connectedRemote))
                    throw new SocketsException(
                        ErrorCode.InvalidArgument,
                        "send: connected socket cannot send to a " +
                        "different remote address.");
            }
            else
            {
                if (_state != State.Connected
                    || _connectedRemote == null)
                    throw new SocketsException(
                        ErrorCode.InvalidArgument,
                        "send: non-connected socket requires a " +
                        "remote address.");
                destination = _connectedRemote;
            }
            if (_state == State.Unbound)
            {
                // Implicit bind to a wildcard ephemeral port —
                // matches Connect's same-family handling.
                try
                {
                    var implicitLocal = _family == IpAddressFamily.Ipv4
                        ? new System.Net.IPEndPoint(
                            System.Net.IPAddress.Any, 0)
                        : new System.Net.IPEndPoint(
                            System.Net.IPAddress.IPv6Any, 0);
                    _socket.Bind(implicitLocal);
                    _state = State.Bound;
                }
                catch (SocketException sx)
                {
                    throw TcpEndpointHelper.MapSocketException(sx);
                }
            }
            try
            {
                await _socket.SendToAsync(
                    new System.ArraySegment<byte>(data),
                    SocketFlags.None, destination!)
                    .ConfigureAwait(false);
            }
            catch (SocketException sx)
            {
                throw TcpEndpointHelper.MapSocketException(sx);
            }
        }

        private static bool IsUnspecifiedAddress(IpSocketAddress addr)
        {
            if (addr.Family == IpAddressFamily.Ipv4)
            {
                var a = addr.V4.Address;
                return a.A == 0 && a.B == 0 && a.C == 0 && a.D == 0;
            }
            var v6 = addr.V6.Address;
            return v6.G0 == 0 && v6.G1 == 0 && v6.G2 == 0 && v6.G3 == 0
                && v6.G4 == 0 && v6.G5 == 0 && v6.G6 == 0 && v6.G7 == 0;
        }

        private static bool IsIpv4MappedIpv6(IpSocketAddress addr)
        {
            if (addr.Family != IpAddressFamily.Ipv6) return false;
            var v6 = addr.V6.Address;
            return v6.G0 == 0 && v6.G1 == 0 && v6.G2 == 0
                && v6.G3 == 0 && v6.G4 == 0 && v6.G5 == 0xFFFF;
        }

        private void EnsureNotClosed()
        {
            if (_state == State.Closed)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "udp-socket: socket is closed.");
        }

        public async Task<(byte[] data, IpSocketAddress remoteAddress)>
            ReceiveAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotClosed();
            if (_state == State.Unbound)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "udp-socket.receive: socket must be bound " +
                    "before it can receive.");
            int maxPayload = _family == IpAddressFamily.Ipv4
                ? MaxUdpPayloadIpv4 : MaxUdpPayloadIpv6;
            var buffer = new byte[maxPayload];
            var anyRemote = _family == IpAddressFamily.Ipv4
                ? (System.Net.EndPoint)new System.Net.IPEndPoint(
                    System.Net.IPAddress.Any, 0)
                : new System.Net.IPEndPoint(
                    System.Net.IPAddress.IPv6Any, 0);
            try
            {
                var result = await _socket.ReceiveFromAsync(
                    new System.ArraySegment<byte>(buffer),
                    SocketFlags.None, anyRemote)
                    .ConfigureAwait(false);
                var actual = new byte[result.ReceivedBytes];
                System.Buffer.BlockCopy(
                    buffer, 0, actual, 0, result.ReceivedBytes);
                var ep = (System.Net.IPEndPoint)result.RemoteEndPoint;
                return (actual,
                    TcpEndpointHelper.FromIpEndPoint(ep));
            }
            catch (SocketException sx)
            {
                throw TcpEndpointHelper.MapSocketException(sx);
            }
        }

        public IpSocketAddress GetLocalAddress()
        {
            if (_state == State.Unbound || _state == State.Closed)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "udp-socket.get-local-address: socket must be " +
                    "bound first.");
            if (_socket.LocalEndPoint is not System.Net.IPEndPoint ep)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "udp-socket.get-local-address: no local endpoint.");
            return TcpEndpointHelper.FromIpEndPoint(ep);
        }

        public IpSocketAddress GetRemoteAddress()
        {
            if (_state != State.Connected || _connectedRemote == null)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "udp-socket.get-remote-address: socket is not " +
                    "connected.");
            return TcpEndpointHelper.FromIpEndPoint(_connectedRemote);
        }

        // Same shadow-storage pattern as TcpSocket: the WASI
        // spec wants guest-requested values round-tripped
        // verbatim, while the OS layer may clamp them. We
        // best-effort apply to the socket and shadow.
        private byte _unicastHopLimit = 64;
        private ulong _receiveBufferSize = 65536;
        private ulong _sendBufferSize = 65536;
        public byte GetUnicastHopLimit()
        {
            EnsureNotClosed();
            return _unicastHopLimit;
        }
        public void SetUnicastHopLimit(byte value)
        {
            EnsureNotClosed();
            if (value == 0)
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "set-unicast-hop-limit: value must be > 0.");
            var level = _family == IpAddressFamily.Ipv4
                ? SocketOptionLevel.IP : SocketOptionLevel.IPv6;
            var option = _family == IpAddressFamily.Ipv4
                ? SocketOptionName.IpTimeToLive
                : SocketOptionName.HopLimit;
            try { _socket.SetSocketOption(level, option, (int)value); }
            catch (SocketException) { /* best-effort */ }
            _unicastHopLimit = value;
        }

        public ulong GetReceiveBufferSize()
        {
            EnsureNotClosed();
            return _receiveBufferSize;
        }
        public void SetReceiveBufferSize(ulong value)
        {
            EnsureNotClosed();
            if (value == 0)
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "set-receive-buffer-size: value must be > 0.");
            try
            {
                _socket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReceiveBuffer,
                    (int)Math.Min(value, int.MaxValue));
            }
            catch (SocketException) { /* best-effort */ }
            _receiveBufferSize = value;
        }
        public ulong GetSendBufferSize()
        {
            EnsureNotClosed();
            return _sendBufferSize;
        }
        public void SetSendBufferSize(ulong value)
        {
            EnsureNotClosed();
            if (value == 0)
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "set-send-buffer-size: value must be > 0.");
            try
            {
                _socket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.SendBuffer,
                    (int)Math.Min(value, int.MaxValue));
            }
            catch (SocketException) { /* best-effort */ }
            _sendBufferSize = value;
        }

        private void EnsureState(State required, string op)
        {
            if (_state != required)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    $"udp-socket.{op}: socket is in state {_state}, " +
                    $"expected {required}.");
        }

    }

    /// <summary>
    /// Shared endpoint <-> IpSocketAddress helpers used by
    /// both <see cref="TcpSocket"/> and <see cref="UdpSocket"/>.
    /// Extracted so the per-socket-type backings don't
    /// duplicate the byte-order arithmetic.
    /// </summary>
    internal static class TcpEndpointHelper
    {
        public static System.Net.IPEndPoint ToIpEndPoint(IpSocketAddress sa)
        {
            if (sa.Family == IpAddressFamily.Ipv4)
            {
                var bytes = new[] {
                    sa.V4.Address.A, sa.V4.Address.B,
                    sa.V4.Address.C, sa.V4.Address.D };
                return new System.Net.IPEndPoint(
                    new System.Net.IPAddress(bytes), sa.V4.Port);
            }
            var ipv6 = new byte[16];
            void WriteU16BE(int slot, ushort value)
            {
                ipv6[slot * 2 + 0] = (byte)((value >> 8) & 0xFF);
                ipv6[slot * 2 + 1] = (byte)(value & 0xFF);
            }
            WriteU16BE(0, sa.V6.Address.G0);
            WriteU16BE(1, sa.V6.Address.G1);
            WriteU16BE(2, sa.V6.Address.G2);
            WriteU16BE(3, sa.V6.Address.G3);
            WriteU16BE(4, sa.V6.Address.G4);
            WriteU16BE(5, sa.V6.Address.G5);
            WriteU16BE(6, sa.V6.Address.G6);
            WriteU16BE(7, sa.V6.Address.G7);
            return new System.Net.IPEndPoint(
                new System.Net.IPAddress(ipv6, sa.V6.ScopeId), sa.V6.Port);
        }

        public static IpSocketAddress FromIpEndPoint(
            System.Net.IPEndPoint ep)
        {
            if (ep.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = ep.Address.GetAddressBytes();
                return IpSocketAddress.Ipv4(new Ipv4SocketAddress(
                    (ushort)ep.Port,
                    new Ipv4Address(bytes[0], bytes[1], bytes[2], bytes[3])));
            }
            var b = ep.Address.GetAddressBytes();
            ushort ReadU16BE(int off) =>
                (ushort)((b[off] << 8) | b[off + 1]);
            return IpSocketAddress.Ipv6(new Ipv6SocketAddress(
                (ushort)ep.Port,
                flowInfo: 0,
                new Ipv6Address(
                    ReadU16BE(0), ReadU16BE(2), ReadU16BE(4), ReadU16BE(6),
                    ReadU16BE(8), ReadU16BE(10), ReadU16BE(12), ReadU16BE(14)),
                scopeId: (uint)ep.Address.ScopeId));
        }

        /// <summary>
        /// Spec-level address validation applied before handing
        /// the address to the OS:
        ///
        /// 1. <b>Family match.</b> An ipv4-socket can't bind /
        ///    connect to an ipv6 address (and vice versa).
        /// 2. <b>Unicast only.</b> WASIp3 bans multicast and
        ///    broadcast addresses at the socket boundary —
        ///    `ip-address-validation.md` in the wasi-sockets
        ///    proposal.
        /// 3. <b>No dual-stack.</b> WASIp3 disallows binding an
        ///    ipv6 socket to an ipv6-mapped-ipv4 address
        ///    (`::ffff:0.0.0.0/96`) — IPV6_V6ONLY is implicitly
        ///    on.
        /// </summary>
        public static void ValidateBindAddress(
            IpAddressFamily sockFamily, IpSocketAddress addr)
        {
            if (addr.Family != sockFamily)
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "bind: address family does not match socket family.");
            ValidateAddressIsUnicast(addr);
            ValidateNoDualStack(sockFamily, addr);
        }

        /// <summary>
        /// Connect-time variant of address validation. Adds two
        /// rules on top of bind validation:
        ///
        /// 1. <b>Port must be non-zero.</b> A remote endpoint
        ///    needs a real port — connect to :0 is meaningless.
        /// 2. <b>Address must be specified.</b> Rejects
        ///    <c>0.0.0.0</c> / <c>::</c>.
        /// </summary>
        public static void ValidateConnectAddress(
            IpAddressFamily sockFamily, IpSocketAddress addr)
        {
            if (addr.Family != sockFamily)
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "connect: address family does not match socket family.");
            ValidateAddressIsUnicast(addr);
            ValidateNoDualStack(sockFamily, addr);
            ushort port = addr.Family == IpAddressFamily.Ipv4
                ? addr.V4.Port : addr.V6.Port;
            if (port == 0)
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "connect: remote port must be non-zero.");
            if (IsUnspecified(addr))
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "connect: remote address must be specified.");
        }

        private static bool IsUnspecified(IpSocketAddress addr)
        {
            if (addr.Family == IpAddressFamily.Ipv4)
            {
                var a = addr.V4.Address;
                return a.A == 0 && a.B == 0 && a.C == 0 && a.D == 0;
            }
            var v6 = addr.V6.Address;
            return v6.G0 == 0 && v6.G1 == 0 && v6.G2 == 0 && v6.G3 == 0
                && v6.G4 == 0 && v6.G5 == 0 && v6.G6 == 0 && v6.G7 == 0;
        }

        private static void ValidateAddressIsUnicast(IpSocketAddress addr)
        {
            if (addr.Family == IpAddressFamily.Ipv4)
            {
                var a = addr.V4.Address.A;
                // 224.0.0.0/4 multicast, 255.255.255.255 broadcast.
                if (a >= 224 && a <= 239)
                    throw new SocketsException(
                        ErrorCode.InvalidArgument,
                        "bind: ipv4 multicast addresses are not bindable.");
                if (addr.V4.Address.A == 255 && addr.V4.Address.B == 255
                    && addr.V4.Address.C == 255 && addr.V4.Address.D == 255)
                    throw new SocketsException(
                        ErrorCode.InvalidArgument,
                        "bind: ipv4 limited broadcast address is not bindable.");
            }
            else
            {
                // ff00::/8 is the ipv6 multicast prefix.
                if ((addr.V6.Address.G0 & 0xFF00) == 0xFF00)
                    throw new SocketsException(
                        ErrorCode.InvalidArgument,
                        "bind: ipv6 multicast addresses are not bindable.");
            }
        }

        private static void ValidateNoDualStack(
            IpAddressFamily sockFamily, IpSocketAddress addr)
        {
            if (sockFamily != IpAddressFamily.Ipv6) return;
            // ipv6-mapped-ipv4 form: ::ffff:a.b.c.d — top 80 bits
            // zero, next 16 bits 0xFFFF.
            var v6 = addr.V6.Address;
            if (v6.G0 == 0 && v6.G1 == 0 && v6.G2 == 0
                && v6.G3 == 0 && v6.G4 == 0 && v6.G5 == 0xFFFF)
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "bind: dual-stack (ipv6-mapped-ipv4) is not allowed.");
        }

        public static SocketsException MapSocketException(SocketException sx) =>
            sx.SocketErrorCode switch
            {
                SocketError.AccessDenied =>
                    new SocketsException(ErrorCode.AccessDenied, sx.Message),
                SocketError.AddressAlreadyInUse =>
                    new SocketsException(ErrorCode.AddressInUse, sx.Message),
                SocketError.AddressNotAvailable =>
                    new SocketsException(ErrorCode.AddressNotBindable, sx.Message),
                SocketError.HostUnreachable
                or SocketError.NetworkUnreachable =>
                    new SocketsException(ErrorCode.RemoteUnreachable, sx.Message),
                SocketError.ConnectionRefused =>
                    new SocketsException(ErrorCode.ConnectionRefused, sx.Message),
                SocketError.ConnectionReset =>
                    new SocketsException(ErrorCode.ConnectionReset, sx.Message),
                SocketError.ConnectionAborted =>
                    new SocketsException(ErrorCode.ConnectionAborted, sx.Message),
                SocketError.TimedOut =>
                    new SocketsException(ErrorCode.Timeout, sx.Message),
                _ => new SocketsException(
                    ErrorCode.Other, sx.Message),
            };
    }
}
