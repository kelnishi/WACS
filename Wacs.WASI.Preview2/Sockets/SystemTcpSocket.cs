// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Net;
using System.Net.Sockets;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Filesystem;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Sockets
{
    /// <summary>
    /// Real <c>tcp-socket</c> backed by
    /// <see cref="System.Net.Sockets.Socket"/>. Translates
    /// the <c>start-*</c>/<c>finish-*</c> WIT lifecycle into
    /// synchronous System.Net calls (the underlying
    /// <see cref="Socket"/> Bind / Connect / Listen / Accept
    /// are blocking but fast on loopback / typical hosts; an
    /// async-driven host would override these to drive
    /// readiness through pollables).
    ///
    /// <para>State machine — every <c>start-*</c> /
    /// <c>finish-*</c> pairing checks <see cref="_state"/>;
    /// out-of-order calls return
    /// <see cref="ErrorCode.InvalidState"/>:
    /// <list type="bullet">
    /// <item><c>Initial</c> — fresh socket; valid:
    ///   start-bind, start-connect.</item>
    /// <item><c>BindStarted</c> — start-bind seen; valid:
    ///   finish-bind.</item>
    /// <item><c>Bound</c> — finish-bind succeeded; valid:
    ///   start-listen, start-connect.</item>
    /// <item><c>ConnectStarted</c> — start-connect seen;
    ///   valid: finish-connect.</item>
    /// <item><c>Connected</c> — finish-connect succeeded;
    ///   valid: shutdown.</item>
    /// <item><c>ListenStarted</c> — start-listen seen; valid:
    ///   finish-listen.</item>
    /// <item><c>Listening</c> — finish-listen succeeded;
    ///   valid: accept.</item>
    /// </list></para>
    /// </summary>
    public class SystemTcpSocket : TcpSocket
    {
        protected enum State
        {
            Initial,
            BindStarted,
            Bound,
            ConnectStarted,
            Connected,
            ListenStarted,
            Listening,
            Closed,
        }

        protected readonly Socket _socket;
        protected State _state = State.Initial;

        public SystemTcpSocket(IpAddressFamily family)
            : base(family)
        {
            _socket = new Socket(
                family == IpAddressFamily.Ipv6
                    ? System.Net.Sockets.AddressFamily.InterNetworkV6
                    : System.Net.Sockets.AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);
        }

        // Internal ctor for Accept() to wrap an already-connected
        // accepted socket.
        protected SystemTcpSocket(Socket accepted, IpAddressFamily family)
            : base(family)
        {
            _socket = accepted;
            _state = State.Connected;
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
                _socket.Bind(ToEndPoint(_pendingLocal));
                _state = State.Bound;
                return Result<Unit, ErrorCode>.FromOk(Unit.Value);
            }
            catch (SocketException se)
            {
                _state = State.Initial;
                return Result<Unit, ErrorCode>.FromErr(MapSocketError(se));
            }
        }

        // -----------------------------------------------------
        //   connect
        // -----------------------------------------------------

        public override Result<Unit, ErrorCode> StartConnect(INetwork network,
            IpSocketAddress remoteAddress)
        {
            if (_state != State.Initial && _state != State.Bound)
                return Result<Unit, ErrorCode>.FromErr(ErrorCode.InvalidState);
            _pendingRemote = remoteAddress;
            _state = State.ConnectStarted;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public override Result<(IInputStream, IOutputStream), ErrorCode>
            FinishConnect()
        {
            if (_state != State.ConnectStarted || _pendingRemote == null)
                return Result<(IInputStream, IOutputStream), ErrorCode>
                    .FromErr(ErrorCode.InvalidState);
            try
            {
                _socket.Connect(ToEndPoint(_pendingRemote));
                _state = State.Connected;
                var stream = new NetworkStream(_socket, ownsSocket: false);
                return Result<(IInputStream, IOutputStream), ErrorCode>
                    .FromOk((
                        (IInputStream)new HostFileInputStream(stream),
                        (IOutputStream)new HostFileOutputStream(stream)));
            }
            catch (SocketException se)
            {
                _state = State.Initial;
                return Result<(IInputStream, IOutputStream), ErrorCode>
                    .FromErr(MapSocketError(se));
            }
        }

        // -----------------------------------------------------
        //   listen / accept
        // -----------------------------------------------------

        public override Result<Unit, ErrorCode> StartListen()
        {
            if (_state != State.Bound)
                return Result<Unit, ErrorCode>.FromErr(ErrorCode.InvalidState);
            _state = State.ListenStarted;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public override Result<Unit, ErrorCode> FinishListen()
        {
            if (_state != State.ListenStarted)
                return Result<Unit, ErrorCode>.FromErr(ErrorCode.InvalidState);
            try
            {
                _socket.Listen((int)System.Math.Min(
                    ListenBacklogSize, (ulong)int.MaxValue));
                _listening = true;
                _state = State.Listening;
                return Result<Unit, ErrorCode>.FromOk(Unit.Value);
            }
            catch (SocketException se)
            {
                _state = State.Bound;
                return Result<Unit, ErrorCode>.FromErr(MapSocketError(se));
            }
        }

        // The base TcpSocket exposes a public-virtual Accept()
        // returning the (TcpSocket, IInputStream, IOutputStream)
        // triple; the explicit ITcpSocket.Accept on the base
        // wraps that in Result.FromOk(...). v0 SystemTcpSocket
        // overrides this virtual entry point — happy path only;
        // SocketException bubbles up to the binding boundary.
        // (The base API doesn't model an error return on the
        // public-virtual Accept; a future revision could add a
        // Result-shaped extension point on the base, but the
        // present constraint is "no base-class edits".)
        public override (TcpSocket, IInputStream, IOutputStream) Accept()
        {
            var client = _socket.Accept();
            var clientSock = new SystemTcpSocket(client, Family);
            var stream = new NetworkStream(client, ownsSocket: false);
            return (clientSock,
                new HostFileInputStream(stream),
                new HostFileOutputStream(stream));
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
                        FromEndPoint(ep));
                return Result<IpSocketAddress, ErrorCode>.FromErr(
                    ErrorCode.InvalidState);
            }
            catch (SocketException se)
            {
                return Result<IpSocketAddress, ErrorCode>.FromErr(
                    MapSocketError(se));
            }
        }

        public override Result<IpSocketAddress, ErrorCode> RemoteAddress()
        {
            try
            {
                if (_socket.RemoteEndPoint is IPEndPoint ep)
                    return Result<IpSocketAddress, ErrorCode>.FromOk(
                        FromEndPoint(ep));
                return Result<IpSocketAddress, ErrorCode>.FromErr(
                    ErrorCode.InvalidState);
            }
            catch (SocketException se)
            {
                return Result<IpSocketAddress, ErrorCode>.FromErr(
                    MapSocketError(se));
            }
        }

        // -----------------------------------------------------
        //   options
        // -----------------------------------------------------

        // The TcpKeepAlive* SocketOptionName entries (TcpKeepAliveTime,
        // TcpKeepAliveInterval, TcpKeepAliveRetryCount) only exist on
        // net5+; on netstandard2.1 the enum predates them. Gate the
        // OS round-trip on NET5_0_OR_GREATER and fall back to the
        // base class's cached value-as-truth on older targets.
        public override Result<ulong, ErrorCode> KeepAliveIdleTime()
        {
#if NET5_0_OR_GREATER
            try
            {
                int seconds = (int)_socket.GetSocketOption(
                    SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveTime)!;
                return Result<ulong, ErrorCode>.FromOk(
                    (ulong)seconds * 1_000_000_000UL);
            }
            catch (SocketException se)
            {
                return Result<ulong, ErrorCode>.FromErr(MapSocketError(se));
            }
            catch (Exception)
            {
                return Result<ulong, ErrorCode>.FromOk(_keepAliveIdleTime);
            }
#else
            return Result<ulong, ErrorCode>.FromOk(_keepAliveIdleTime);
#endif
        }

        public override Result<Unit, ErrorCode> SetKeepAliveIdleTime(ulong value)
        {
            _keepAliveIdleTime = value;
#if NET5_0_OR_GREATER
            try
            {
                int seconds = (int)System.Math.Min(
                    value / 1_000_000_000UL, (ulong)int.MaxValue);
                _socket.SetSocketOption(SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveTime, seconds);
            }
            catch (SocketException se)
            {
                return Result<Unit, ErrorCode>.FromErr(MapSocketError(se));
            }
            catch (Exception) { /* fall through; value cached */ }
#endif
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public override Result<ulong, ErrorCode> KeepAliveInterval()
        {
#if NET5_0_OR_GREATER
            try
            {
                int seconds = (int)_socket.GetSocketOption(
                    SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveInterval)!;
                return Result<ulong, ErrorCode>.FromOk(
                    (ulong)seconds * 1_000_000_000UL);
            }
            catch (SocketException se)
            {
                return Result<ulong, ErrorCode>.FromErr(MapSocketError(se));
            }
            catch (Exception)
            {
                return Result<ulong, ErrorCode>.FromOk(_keepAliveInterval);
            }
#else
            return Result<ulong, ErrorCode>.FromOk(_keepAliveInterval);
#endif
        }

        public override Result<Unit, ErrorCode> SetKeepAliveInterval(ulong value)
        {
            _keepAliveInterval = value;
#if NET5_0_OR_GREATER
            try
            {
                int seconds = (int)System.Math.Min(
                    value / 1_000_000_000UL, (ulong)int.MaxValue);
                _socket.SetSocketOption(SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveInterval, seconds);
            }
            catch (SocketException se)
            {
                return Result<Unit, ErrorCode>.FromErr(MapSocketError(se));
            }
            catch (Exception) { /* fall through; value cached */ }
#endif
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public override Result<uint, ErrorCode> KeepAliveCount()
        {
#if NET5_0_OR_GREATER
            try
            {
                int count = (int)_socket.GetSocketOption(
                    SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveRetryCount)!;
                return Result<uint, ErrorCode>.FromOk((uint)count);
            }
            catch (SocketException se)
            {
                return Result<uint, ErrorCode>.FromErr(MapSocketError(se));
            }
            catch (Exception)
            {
                return Result<uint, ErrorCode>.FromOk(_keepAliveCount);
            }
#else
            return Result<uint, ErrorCode>.FromOk(_keepAliveCount);
#endif
        }

        public override Result<Unit, ErrorCode> SetKeepAliveCount(uint value)
        {
            _keepAliveCount = value;
#if NET5_0_OR_GREATER
            try
            {
                _socket.SetSocketOption(SocketOptionLevel.Tcp,
                    SocketOptionName.TcpKeepAliveRetryCount, (int)value);
            }
            catch (SocketException se)
            {
                return Result<Unit, ErrorCode>.FromErr(MapSocketError(se));
            }
            catch (Exception) { /* fall through; value cached */ }
#endif
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public override Result<byte, ErrorCode> HopLimit()
        {
            try
            {
                var (level, name) = Family == IpAddressFamily.Ipv6
                    ? (SocketOptionLevel.IPv6, SocketOptionName.IpTimeToLive)
                    : (SocketOptionLevel.IP, SocketOptionName.IpTimeToLive);
                int ttl = (int)_socket.GetSocketOption(level, name)!;
                return Result<byte, ErrorCode>.FromOk(
                    (byte)System.Math.Min(ttl, 255));
            }
            catch (SocketException se)
            {
                return Result<byte, ErrorCode>.FromErr(MapSocketError(se));
            }
        }

        public override Result<Unit, ErrorCode> SetHopLimit(byte value)
        {
            _hopLimit = value;
            try
            {
                var (level, name) = Family == IpAddressFamily.Ipv6
                    ? (SocketOptionLevel.IPv6, SocketOptionName.IpTimeToLive)
                    : (SocketOptionLevel.IP, SocketOptionName.IpTimeToLive);
                _socket.SetSocketOption(level, name, (int)value);
                return Result<Unit, ErrorCode>.FromOk(Unit.Value);
            }
            catch (SocketException se)
            {
                return Result<Unit, ErrorCode>.FromErr(MapSocketError(se));
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
                return Result<ulong, ErrorCode>.FromErr(MapSocketError(se));
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
                return Result<Unit, ErrorCode>.FromErr(MapSocketError(se));
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
                return Result<ulong, ErrorCode>.FromErr(MapSocketError(se));
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
                return Result<Unit, ErrorCode>.FromErr(MapSocketError(se));
            }
        }

        // -----------------------------------------------------
        //   subscribe / shutdown / dispose
        // -----------------------------------------------------

        public override IPollable Subscribe()
            => new SocketReadyPollable(_socket);

        public override Result<Unit, ErrorCode> Shutdown(ShutdownType how)
        {
            try
            {
                _socket.Shutdown(how switch
                {
                    ShutdownType.Receive => SocketShutdown.Receive,
                    ShutdownType.Send    => SocketShutdown.Send,
                    _                    => SocketShutdown.Both,
                });
                return Result<Unit, ErrorCode>.FromOk(Unit.Value);
            }
            catch (SocketException se)
            {
                return Result<Unit, ErrorCode>.FromErr(MapSocketError(se));
            }
        }

        public override void Dispose()
        {
            try { _socket.Close(); } catch { /* best-effort */ }
            try { _socket.Dispose(); } catch { /* best-effort */ }
            _state = State.Closed;
        }

        // -----------------------------------------------------
        //   conversions
        // -----------------------------------------------------

        // IpSocketAddress -> System.Net.IPEndPoint. The IPv6
        // path packs 8 u16 groups into 16 bytes in network
        // byte order, matching the WIT spec's
        // <c>tuple<u16, u16, u16, u16, u16, u16, u16, u16></c>
        // shape (each u16 is host-order in the C# struct, but
        // network-order on the wire).
        protected static IPEndPoint ToEndPoint(IpSocketAddress addr)
        {
            switch (addr)
            {
                case IpSocketAddress.IpSocketAddressIpv4 v4Case:
                {
                    var v4 = v4Case.Value;
                    var bytes = new byte[]
                    {
                        v4.Address.Item1, v4.Address.Item2,
                        v4.Address.Item3, v4.Address.Item4,
                    };
                    return new IPEndPoint(new IPAddress(bytes), v4.Port);
                }
                case IpSocketAddress.IpSocketAddressIpv6 v6Case:
                {
                    var v6 = v6Case.Value;
                    var bytes = new byte[16];
                    Pack(bytes, 0, v6.Address.Item1);
                    Pack(bytes, 2, v6.Address.Item2);
                    Pack(bytes, 4, v6.Address.Item3);
                    Pack(bytes, 6, v6.Address.Item4);
                    Pack(bytes, 8, v6.Address.Item5);
                    Pack(bytes, 10, v6.Address.Item6);
                    Pack(bytes, 12, v6.Address.Item7);
                    Pack(bytes, 14, v6.Address.Item8);
                    var ip = new IPAddress(bytes, v6.ScopeId);
                    return new IPEndPoint(ip, v6.Port);
                }
                default:
                    throw new ArgumentException(
                        "unknown IpSocketAddress variant");
            }
        }

        // IPEndPoint -> IpSocketAddress. Matches the
        // ToEndPoint inverse: u16 groups read in network
        // byte order from the 16-byte address bytes.
        protected static IpSocketAddress FromEndPoint(IPEndPoint ep)
        {
            if (ep.AddressFamily ==
                System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var b = ep.Address.GetAddressBytes();
                return new IpSocketAddress.IpSocketAddressIpv4(
                    new Ipv4SocketAddress
                    {
                        Port = (ushort)ep.Port,
                        Address = (b[0], b[1], b[2], b[3]),
                    });
            }
            // IPv6.
            var bb = ep.Address.GetAddressBytes();
            return new IpSocketAddress.IpSocketAddressIpv6(
                new Ipv6SocketAddress
                {
                    Port = (ushort)ep.Port,
                    FlowInfo = 0,
                    Address = (
                        ToU16(bb, 0), ToU16(bb, 2),
                        ToU16(bb, 4), ToU16(bb, 6),
                        ToU16(bb, 8), ToU16(bb, 10),
                        ToU16(bb, 12), ToU16(bb, 14)),
                    ScopeId = (uint)ep.Address.ScopeId,
                });
        }

        private static void Pack(byte[] dst, int offset, ushort value)
        {
            dst[offset]     = (byte)(value >> 8);
            dst[offset + 1] = (byte)(value & 0xff);
        }

        private static ushort ToU16(byte[] b, int offset)
            => (ushort)((b[offset] << 8) | b[offset + 1]);

        // -----------------------------------------------------
        //   error mapping
        // -----------------------------------------------------

        internal static ErrorCode MapSocketError(SocketException se)
            => se.SocketErrorCode switch
            {
                SocketError.AccessDenied         => ErrorCode.AccessDenied,
                SocketError.AddressAlreadyInUse  => ErrorCode.AddressInUse,
                SocketError.AddressNotAvailable  => ErrorCode.AddressNotBindable,
                SocketError.ConnectionRefused    => ErrorCode.ConnectionRefused,
                SocketError.ConnectionReset      => ErrorCode.ConnectionReset,
                SocketError.HostNotFound         => ErrorCode.RemoteUnreachable,
                SocketError.HostUnreachable      => ErrorCode.RemoteUnreachable,
                SocketError.NetworkUnreachable   => ErrorCode.RemoteUnreachable,
                SocketError.TimedOut             => ErrorCode.Timeout,
                SocketError.WouldBlock           => ErrorCode.WouldBlock,
                _                                => ErrorCode.Unknown,
            };
    }

    /// <summary>
    /// <see cref="Pollable"/> backed by
    /// <see cref="Socket.Poll(int, SelectMode)"/> on
    /// <see cref="SelectMode.SelectRead"/> — fires when the
    /// socket has data to read, has a pending connection (for
    /// listeners), or has been closed by the peer. Used by
    /// both TCP and UDP.
    /// </summary>
    public sealed class SocketReadyPollable : Pollable
    {
        private readonly Socket _socket;
        public SocketReadyPollable(Socket socket)
        {
            _socket = socket
                ?? throw new ArgumentNullException(nameof(socket));
        }

        public override bool Ready()
        {
            try { return _socket.Poll(0, SelectMode.SelectRead); }
            catch { return true; }
        }

        public override void Block()
        {
            try { _socket.Poll(-1, SelectMode.SelectRead); }
            catch { /* completion via the next op surfaces error */ }
        }
    }

    /// <summary>
    /// <see cref="ITcpCreateSocket"/> impl that hands out
    /// <see cref="SystemTcpSocket"/> instances backed by
    /// <see cref="System.Net.Sockets.Socket"/>.
    /// </summary>
    public sealed class SystemTcpCreateSocket : ITcpCreateSocket
    {
        public Result<ITcpSocket, ErrorCode> CreateTcpSocket(
            IpAddressFamily addressFamily)
        {
            try
            {
                return Result<ITcpSocket, ErrorCode>.FromOk(
                    new SystemTcpSocket(addressFamily));
            }
            catch (SocketException se)
            {
                return Result<ITcpSocket, ErrorCode>.FromErr(
                    SystemTcpSocket.MapSocketError(se));
            }
        }
    }
}
