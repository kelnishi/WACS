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

        public void Connect(IpSocketAddress remoteAddress)
        {
            if (_state != State.Bound && _state != State.Connected)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "udp-socket.connect: socket must be bound first.");
            try
            {
                var ep = TcpEndpointHelper.ToIpEndPoint(remoteAddress);
                _socket.Connect(ep);
                _state = State.Connected;
            }
            catch (SocketException sx)
            {
                throw TcpEndpointHelper.MapSocketException(sx);
            }
        }

        public void Disconnect()
        {
            if (_state != State.Connected)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "udp-socket.disconnect: socket is not connected.");
            try
            {
                _socket.Disconnect(reuseSocket: true);
                _state = State.Bound;
            }
            catch (SocketException sx)
            {
                throw TcpEndpointHelper.MapSocketException(sx);
            }
        }

        public Task SendAsync(
            byte[] data, IpSocketAddress? remoteAddress = null,
            CancellationToken cancellationToken = default)
        {
            throw new SocketsException(
                ErrorCode.NotSupported,
                "udp-socket.send: pending canon-async list-arg + " +
                "option<ip-socket-address> wire-up.");
        }

        public Task<(byte[] data, IpSocketAddress remoteAddress)>
            ReceiveAsync(CancellationToken cancellationToken = default)
        {
            throw new SocketsException(
                ErrorCode.NotSupported,
                "udp-socket.receive: pending canon-async " +
                "tuple<list<u8>, ip-socket-address> return wire-up.");
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
            if (_state != State.Connected)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "udp-socket.get-remote-address: socket is not " +
                    "connected.");
            if (_socket.RemoteEndPoint is not System.Net.IPEndPoint ep)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "udp-socket.get-remote-address: no remote endpoint.");
            return TcpEndpointHelper.FromIpEndPoint(ep);
        }

        public byte GetUnicastHopLimit()
        {
            EnsureNotClosed();
            var level = _family == IpAddressFamily.Ipv4
                ? SocketOptionLevel.IP : SocketOptionLevel.IPv6;
            var option = _family == IpAddressFamily.Ipv4
                ? SocketOptionName.IpTimeToLive
                : SocketOptionName.HopLimit;
            return (byte)(int)(_socket.GetSocketOption(level, option) ?? 0);
        }
        public void SetUnicastHopLimit(byte value)
        {
            EnsureNotClosed();
            var level = _family == IpAddressFamily.Ipv4
                ? SocketOptionLevel.IP : SocketOptionLevel.IPv6;
            var option = _family == IpAddressFamily.Ipv4
                ? SocketOptionName.IpTimeToLive
                : SocketOptionName.HopLimit;
            _socket.SetSocketOption(level, option, (int)value);
        }

        public ulong GetReceiveBufferSize()
        {
            EnsureNotClosed();
            return (ulong)(int)(_socket.GetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReceiveBuffer) ?? 0);
        }
        public void SetReceiveBufferSize(ulong value)
        {
            EnsureNotClosed();
            _socket.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReceiveBuffer,
                (int)Math.Min(value, int.MaxValue));
        }
        public ulong GetSendBufferSize()
        {
            EnsureNotClosed();
            return (ulong)(int)(_socket.GetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.SendBuffer) ?? 0);
        }
        public void SetSendBufferSize(ulong value)
        {
            EnsureNotClosed();
            _socket.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.SendBuffer,
                (int)Math.Min(value, int.MaxValue));
        }

        private void EnsureState(State required, string op)
        {
            if (_state != required)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    $"udp-socket.{op}: socket is in state {_state}, " +
                    $"expected {required}.");
        }

        private void EnsureNotClosed()
        {
            if (_state == State.Closed)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "udp-socket: socket is closed.");
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
