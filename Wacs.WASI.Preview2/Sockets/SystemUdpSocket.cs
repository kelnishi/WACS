// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Sockets
{
    /// <summary>
    /// Real <c>udp-socket</c> backed by
    /// <see cref="System.Net.Sockets.Socket"/> with
    /// <see cref="SocketType.Dgram"/> /
    /// <see cref="ProtocolType.Udp"/>. Translates the WIT
    /// <c>start-bind</c>/<c>finish-bind</c> lifecycle into
    /// synchronous <see cref="Socket.Bind"/> calls; UDP has no
    /// <c>start-connect</c>/<c>start-listen</c> to model — a
    /// "pinned" remote (the optional argument to
    /// <c>udp-socket.%stream</c>) routes a
    /// <see cref="Socket.Connect(EndPoint)"/> to filter inbound
    /// + default outbound peer, then constructs paired
    /// datagram-stream wrappers around the same underlying
    /// socket.
    ///
    /// <para>State machine — parallel to TCP's but simpler;
    /// out-of-order calls return
    /// <see cref="ErrorCode.InvalidState"/>:
    /// <list type="bullet">
    /// <item><c>Initial</c> — fresh socket; valid:
    ///   start-bind.</item>
    /// <item><c>BindStarted</c> — start-bind seen; valid:
    ///   finish-bind.</item>
    /// <item><c>Bound</c> — finish-bind succeeded; valid:
    ///   stream / address getters.</item>
    /// <item><c>Closed</c> — disposed.</item>
    /// </list></para>
    /// </summary>
    public class SystemUdpSocket : UdpSocket
    {
        protected enum State
        {
            Initial,
            BindStarted,
            Bound,
            Closed,
        }

        protected readonly Socket _socket;
        protected State _state = State.Initial;

        public SystemUdpSocket(IpAddressFamily family)
            : base(family)
        {
            _socket = new Socket(
                family == IpAddressFamily.Ipv6
                    ? System.Net.Sockets.AddressFamily.InterNetworkV6
                    : System.Net.Sockets.AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp);
        }

        // -----------------------------------------------------
        //   bind
        // -----------------------------------------------------

        public override Result<Unit, ErrorCode> StartBind(INetwork network,
            IpSocketAddress localAddress)
        {
            if (_state != State.Initial)
                return Result<Unit, ErrorCode>.FromErr(ErrorCode.InvalidState);
            _pendingLocal = localAddress;
            _state = State.BindStarted;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public override Result<Unit, ErrorCode> FinishBind()
        {
            if (_state != State.BindStarted || _pendingLocal == null)
                return Result<Unit, ErrorCode>.FromErr(ErrorCode.InvalidState);
            try
            {
                _socket.Bind(SystemTcpSocket.ToEndPointPublic(_pendingLocal));
                _state = State.Bound;
                return Result<Unit, ErrorCode>.FromOk(Unit.Value);
            }
            catch (SocketException se)
            {
                _state = State.Initial;
                return Result<Unit, ErrorCode>.FromErr(
                    SystemTcpSocket.MapSocketError(se));
            }
        }

        // -----------------------------------------------------
        //   addresses
        // -----------------------------------------------------

        public override Result<IpSocketAddress, ErrorCode> LocalAddress()
        {
            try
            {
                if (_socket.LocalEndPoint is IPEndPoint ep)
                    return Result<IpSocketAddress, ErrorCode>.FromOk(
                        SystemTcpSocket.FromEndPointPublic(ep));
                return Result<IpSocketAddress, ErrorCode>.FromErr(
                    ErrorCode.InvalidState);
            }
            catch (SocketException se)
            {
                return Result<IpSocketAddress, ErrorCode>.FromErr(
                    SystemTcpSocket.MapSocketError(se));
            }
        }

        public override Result<IpSocketAddress, ErrorCode> RemoteAddress()
        {
            try
            {
                // Only meaningful after Socket.Connect has pinned
                // a peer (UDP "connect"). When unpinned, the
                // socket has no RemoteEndPoint and the property
                // throws.
                if (_socket.RemoteEndPoint is IPEndPoint ep)
                    return Result<IpSocketAddress, ErrorCode>.FromOk(
                        SystemTcpSocket.FromEndPointPublic(ep));
                return Result<IpSocketAddress, ErrorCode>.FromErr(
                    ErrorCode.InvalidState);
            }
            catch (SocketException se)
            {
                return Result<IpSocketAddress, ErrorCode>.FromErr(
                    SystemTcpSocket.MapSocketError(se));
            }
            catch (Exception)
            {
                // RemoteEndPoint can throw InvalidOperation /
                // ObjectDisposed when the socket isn't connected.
                return Result<IpSocketAddress, ErrorCode>.FromErr(
                    ErrorCode.InvalidState);
            }
        }

        // -----------------------------------------------------
        //   options
        // -----------------------------------------------------

        public override Result<byte, ErrorCode> UnicastHopLimit()
        {
            try
            {
                var (level, name) = Family == IpAddressFamily.Ipv6
                    ? (SocketOptionLevel.IPv6, SocketOptionName.HopLimit)
                    : (SocketOptionLevel.IP, SocketOptionName.IpTimeToLive);
                int ttl = (int)_socket.GetSocketOption(level, name)!;
                return Result<byte, ErrorCode>.FromOk(
                    (byte)System.Math.Min(ttl, 255));
            }
            catch (SocketException se)
            {
                return Result<byte, ErrorCode>.FromErr(
                    SystemTcpSocket.MapSocketError(se));
            }
        }

        public override Result<Unit, ErrorCode> SetUnicastHopLimit(byte value)
        {
            _hopLimit = value;
            try
            {
                var (level, name) = Family == IpAddressFamily.Ipv6
                    ? (SocketOptionLevel.IPv6, SocketOptionName.HopLimit)
                    : (SocketOptionLevel.IP, SocketOptionName.IpTimeToLive);
                _socket.SetSocketOption(level, name, (int)value);
                return Result<Unit, ErrorCode>.FromOk(Unit.Value);
            }
            catch (SocketException se)
            {
                return Result<Unit, ErrorCode>.FromErr(
                    SystemTcpSocket.MapSocketError(se));
            }
        }

        public override Result<ulong, ErrorCode> ReceiveBufferSize()
        {
            try
            {
                return Result<ulong, ErrorCode>.FromOk(
                    (ulong)_socket.ReceiveBufferSize);
            }
            catch (SocketException se)
            {
                return Result<ulong, ErrorCode>.FromErr(
                    SystemTcpSocket.MapSocketError(se));
            }
        }

        public override Result<Unit, ErrorCode> SetReceiveBufferSize(ulong value)
        {
            _receiveBufferSize = value;
            try
            {
                _socket.ReceiveBufferSize = (int)System.Math.Min(
                    value, (ulong)int.MaxValue);
                return Result<Unit, ErrorCode>.FromOk(Unit.Value);
            }
            catch (SocketException se)
            {
                return Result<Unit, ErrorCode>.FromErr(
                    SystemTcpSocket.MapSocketError(se));
            }
        }

        public override Result<ulong, ErrorCode> SendBufferSize()
        {
            try
            {
                return Result<ulong, ErrorCode>.FromOk(
                    (ulong)_socket.SendBufferSize);
            }
            catch (SocketException se)
            {
                return Result<ulong, ErrorCode>.FromErr(
                    SystemTcpSocket.MapSocketError(se));
            }
        }

        public override Result<Unit, ErrorCode> SetSendBufferSize(ulong value)
        {
            _sendBufferSize = value;
            try
            {
                _socket.SendBufferSize = (int)System.Math.Min(
                    value, (ulong)int.MaxValue);
                return Result<Unit, ErrorCode>.FromOk(Unit.Value);
            }
            catch (SocketException se)
            {
                return Result<Unit, ErrorCode>.FromErr(
                    SystemTcpSocket.MapSocketError(se));
            }
        }

        // -----------------------------------------------------
        //   subscribe / stream / dispose
        // -----------------------------------------------------

        public override IPollable Subscribe()
            => new SocketReadyPollable(_socket);

        public override (IncomingDatagramStream, OutgoingDatagramStream)
            UdpStream(IpSocketAddress? remoteAddress)
        {
            if (remoteAddress != null)
            {
                // UDP "connect" pins the default peer for
                // outgoing send + filters the inbound queue.
                // Best-effort — failure here surfaces on the
                // first send via SendTo's SocketException.
                try
                {
                    _socket.Connect(
                        SystemTcpSocket.ToEndPointPublic(remoteAddress));
                }
                catch (SocketException) { /* lazy-failure path */ }
            }

            return (
                new SystemIncomingDatagramStream(_socket),
                new SystemOutgoingDatagramStream(_socket, remoteAddress));
        }

        public override void Dispose()
        {
            try { _socket.Close(); } catch { /* best-effort */ }
            try { _socket.Dispose(); } catch { /* best-effort */ }
            _state = State.Closed;
        }
    }

    /// <summary>
    /// Real <c>incoming-datagram-stream</c> backed by
    /// <see cref="Socket.ReceiveFrom(byte[], ref EndPoint)"/>.
    /// Receive is non-blocking — when the kernel queue is
    /// empty the call returns <c>Ok(empty)</c>, matching the
    /// WASI semantics that "no datagrams available" is not an
    /// error, just an empty pull.
    /// </summary>
    public sealed class SystemIncomingDatagramStream : IncomingDatagramStream
    {
        private readonly Socket _socket;
        // Per-socket scratch buffer reused across Receive calls.
        // 64 KiB matches the WIT default receive-buffer-size and
        // is large enough for any UDP datagram (max 65,507 bytes
        // payload).
        private readonly byte[] _scratch = new byte[65_536];

        public SystemIncomingDatagramStream(Socket socket)
        {
            _socket = socket
                ?? throw new ArgumentNullException(nameof(socket));
        }

        public override Result<IncomingDatagram[], ErrorCode> Receive(
            ulong maxResults)
        {
            if (maxResults == 0)
                return Result<IncomingDatagram[], ErrorCode>.FromOk(
                    Array.Empty<IncomingDatagram>());

            var results = new List<IncomingDatagram>();
            try
            {
                // Pre-pick an EndPoint of the right family so
                // ReceiveFrom can populate it. The .NET API
                // requires this to be writable + matching family.
                EndPoint scratchEp = _socket.AddressFamily
                    == System.Net.Sockets.AddressFamily.InterNetworkV6
                    ? new IPEndPoint(IPAddress.IPv6Any, 0)
                    : new IPEndPoint(IPAddress.Any, 0);

                ulong taken = 0;
                while (taken < maxResults && _socket.Available > 0)
                {
                    EndPoint ep = scratchEp;
                    int n = _socket.ReceiveFrom(
                        _scratch, 0, _scratch.Length,
                        SocketFlags.None, ref ep);
                    var data = new byte[n];
                    Buffer.BlockCopy(_scratch, 0, data, 0, n);

                    results.Add(new IncomingDatagram
                    {
                        Data = data,
                        RemoteAddress = ep is IPEndPoint ipep
                            ? SystemTcpSocket.FromEndPointPublic(ipep)
                            : DefaultV4Zero(),
                    });
                    taken++;
                }

                return Result<IncomingDatagram[], ErrorCode>.FromOk(
                    results.ToArray());
            }
            catch (SocketException se)
            {
                // WouldBlock just means "no data right now" —
                // surface what we already collected as Ok.
                if (se.SocketErrorCode == SocketError.WouldBlock)
                    return Result<IncomingDatagram[], ErrorCode>.FromOk(
                        results.ToArray());
                return Result<IncomingDatagram[], ErrorCode>.FromErr(
                    SystemTcpSocket.MapSocketError(se));
            }
        }

        public override IPollable Subscribe()
            => new SocketReadyPollable(_socket);

        public override void Dispose()
        {
            // The underlying Socket is owned by the parent
            // SystemUdpSocket — don't double-dispose here.
        }

        private static IpSocketAddress DefaultV4Zero()
            => new IpSocketAddress.IpSocketAddressIpv4(
                new Ipv4SocketAddress
                {
                    Port = 0,
                    Address = (0, 0, 0, 0),
                });
    }

    /// <summary>
    /// Real <c>outgoing-datagram-stream</c> backed by
    /// <see cref="Socket.SendTo(byte[], EndPoint)"/>. The
    /// stream remembers an optional pinned remote (set when
    /// the parent <c>udp-socket.%stream(some(addr))</c>) — when
    /// a per-datagram <c>OutgoingDatagram.RemoteAddress</c> is
    /// <c>None</c> the pinned address is used; an explicit
    /// per-datagram override always wins.
    /// </summary>
    public sealed class SystemOutgoingDatagramStream : OutgoingDatagramStream
    {
        private readonly Socket _socket;
        private readonly IpSocketAddress? _pinned;

        public SystemOutgoingDatagramStream(Socket socket,
            IpSocketAddress? pinned)
        {
            _socket = socket
                ?? throw new ArgumentNullException(nameof(socket));
            _pinned = pinned;
        }

        public override Result<ulong, ErrorCode> CheckSend()
        {
            // Production-grade flow control would query the
            // kernel send buffer; .NET doesn't expose that, so
            // hand back the same generous constant the base
            // class uses (64 datagrams).
            return Result<ulong, ErrorCode>.FromOk(64UL);
        }

        public override Result<ulong, ErrorCode> Send(
            OutgoingDatagram[] datagrams)
        {
            if (datagrams == null || datagrams.Length == 0)
                return Result<ulong, ErrorCode>.FromOk(0UL);

            ulong sent = 0;
            for (int i = 0; i < datagrams.Length; i++)
            {
                var dg = datagrams[i];

                // Pick remote: per-datagram override else
                // stream-pinned.
                IpSocketAddress? target = dg.RemoteAddress.HasValue
                    ? dg.RemoteAddress.Value
                    : _pinned;

                try
                {
                    if (target != null)
                    {
                        var ep = SystemTcpSocket.ToEndPointPublic(target);
                        _socket.SendTo(dg.Data, ep);
                    }
                    else if (_socket.Connected)
                    {
                        // Fall back to the kernel-pinned peer
                        // (set by the UDP-connect inside
                        // UdpStream).
                        _socket.Send(dg.Data);
                    }
                    else
                    {
                        // No peer to send to — the WIT contract
                        // says this is an error.
                        return sent == 0
                            ? Result<ulong, ErrorCode>.FromErr(
                                ErrorCode.InvalidState)
                            : Result<ulong, ErrorCode>.FromOk(sent);
                    }
                    sent++;
                }
                catch (SocketException se)
                {
                    // Stop on first error. Return the count we
                    // managed to push if at least one made it;
                    // otherwise surface the error.
                    return sent == 0
                        ? Result<ulong, ErrorCode>.FromErr(
                            SystemTcpSocket.MapSocketError(se))
                        : Result<ulong, ErrorCode>.FromOk(sent);
                }
            }

            return Result<ulong, ErrorCode>.FromOk(sent);
        }

        public override IPollable Subscribe()
            => new SocketReadyPollable(_socket, SelectMode.SelectWrite);

        public override void Dispose()
        {
            // Underlying Socket owned by parent SystemUdpSocket.
        }
    }

    /// <summary>
    /// <see cref="IUdpCreateSocket"/> impl that hands out
    /// <see cref="SystemUdpSocket"/> instances backed by
    /// <see cref="System.Net.Sockets.Socket"/>.
    /// </summary>
    public sealed class SystemUdpCreateSocket : IUdpCreateSocket
    {
        public Result<IUdpSocket, ErrorCode> CreateUdpSocket(
            IpAddressFamily addressFamily)
        {
            try
            {
                return Result<IUdpSocket, ErrorCode>.FromOk(
                    new SystemUdpSocket(addressFamily));
            }
            catch (SocketException se)
            {
                return Result<IUdpSocket, ErrorCode>.FromErr(
                    SystemTcpSocket.MapSocketError(se));
            }
        }
    }
}
