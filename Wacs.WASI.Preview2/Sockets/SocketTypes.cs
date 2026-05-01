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
    // The WIT-derived enums + records (IpAddressFamily,
    // ShutdownType, ErrorCode (21 cases), IpAddress + nested
    // cases, IpSocketAddress + nested cases, Ipv4SocketAddress,
    // Ipv6SocketAddress, IncomingDatagram, OutgoingDatagram,
    // INetwork, ITcpSocket, IUdpSocket, IIncomingDatagramStream,
    // IOutgoingDatagramStream, IResolveAddressStream,
    // IInstanceNetwork, ITcpCreateSocket, IUdpCreateSocket,
    // IIpNameLookup, INetworkFuncs) are emitted by the source
    // generator from wit/deps/sockets/*.wit and land in this
    // namespace. Hand-written code below adds the host-side
    // resource impls that implement those generated interfaces
    // with Result<X, ErrorCode> return shapes.

    /// <summary>Host representation of <c>tcp-socket</c>.
    /// Implements <see cref="ITcpSocket"/> directly — every
    /// method that the WIT marks <c>result&lt;X, error-code&gt;</c>
    /// returns the faithful canonical-ABI shape, surfacing
    /// <see cref="ErrorCode"/> values rather than thrown
    /// exceptions on the explicit error path. Each instance is
    /// tagged with the <see cref="IpAddressFamily"/> it was
    /// created for.</summary>
    [WasiResource("tcp-socket")]
    public class TcpSocket : ITcpSocket, IDisposable
    {
        public IpAddressFamily Family { get; }
        public TcpSocket(IpAddressFamily family) { Family = family; }

        /// <summary>Return the socket's address family. WIT's
        /// <c>address-family() -&gt; ip-address-family</c> —
        /// no result wrapping; bare enum return.</summary>
        public virtual IpAddressFamily AddressFamily() => Family;

        /// <summary>Yield a pollable that fires when this
        /// socket has work to do. v0 default returns an
        /// always-ready Pollable; concrete implementations
        /// subclass for real I/O readiness.</summary>
        public virtual IPollable Subscribe() => new Pollable();

        /// <summary>Begin binding to <paramref name="localAddress"/>.
        /// Pairs with <see cref="FinishBind"/>. Default
        /// captures the address; concrete impls invoke real
        /// socket APIs.</summary>
        public virtual Result<Unit, ErrorCode> StartBind(INetwork network,
            IpSocketAddress localAddress)
        {
            _pendingLocal = localAddress;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        /// <summary>Begin connecting to
        /// <paramref name="remoteAddress"/>.</summary>
        public virtual Result<Unit, ErrorCode> StartConnect(INetwork network,
            IpSocketAddress remoteAddress)
        {
            _pendingRemote = remoteAddress;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public IpSocketAddress? PendingLocal => _pendingLocal;
        public IpSocketAddress? PendingRemote => _pendingRemote;
        protected IpSocketAddress? _pendingLocal;
        protected IpSocketAddress? _pendingRemote;

        /// <summary>Complete a previously-issued <c>start-bind</c>
        /// call. Default impl is a no-op since v0 doesn't yet
        /// model the start/finish split.</summary>
        public virtual Result<Unit, ErrorCode> FinishBind()
            => Result<Unit, ErrorCode>.FromOk(Unit.Value);

        /// <summary>Begin transitioning to the listening state.
        /// Pairs with <see cref="FinishListen"/>.</summary>
        public virtual Result<Unit, ErrorCode> StartListen()
            => Result<Unit, ErrorCode>.FromOk(Unit.Value);

        /// <summary>Complete the listen transition.</summary>
        public virtual Result<Unit, ErrorCode> FinishListen()
            => Result<Unit, ErrorCode>.FromOk(Unit.Value);

        /// <summary>Complete a previously-issued
        /// <c>start-connect</c> call. Returns the
        /// (input-stream, output-stream) pair for the freshly
        /// established connection — guests use these to read
        /// and write the TCP byte stream. Default returns a
        /// pair of stub streams.</summary>
        public virtual Result<(IInputStream, IOutputStream), ErrorCode>
            FinishConnect()
            => Result<(IInputStream, IOutputStream), ErrorCode>.FromOk(
                ((IInputStream)new InputStream(),
                 (IOutputStream)new OutputStream()));

        /// <summary>Accept a pending incoming connection.
        /// Returns the new <c>tcp-socket</c> + its
        /// (input-stream, output-stream) pair. Default returns
        /// stub instances. Public-virtual return type uses the
        /// concrete <see cref="TcpSocket"/> so subclasses can
        /// hand back a more-derived socket; the explicit-
        /// interface shim downcasts at the binding boundary.
        /// </summary>
        public virtual (TcpSocket, IInputStream, IOutputStream) Accept()
            => (new TcpSocket(Family),
                new InputStream(), new OutputStream());

        Result<(ITcpSocket, IInputStream, IOutputStream), ErrorCode>
            ITcpSocket.Accept()
        {
            var (s, inS, outS) = Accept();
            return Result<(ITcpSocket, IInputStream, IOutputStream),
                          ErrorCode>.FromOk((s, inS, outS));
        }

        /// <summary>Initiate a connection-shutdown of the
        /// requested direction. The base impl is a no-op
        /// stub.</summary>
        public virtual Result<Unit, ErrorCode> Shutdown(ShutdownType how)
            => Result<Unit, ErrorCode>.FromOk(Unit.Value);

        /// <summary>Local address the socket is bound to.
        /// Default returns 0.0.0.0:0 — concrete subclasses
        /// override after a real bind.</summary>
        public virtual Result<IpSocketAddress, ErrorCode> LocalAddress()
            => Result<IpSocketAddress, ErrorCode>.FromOk(
                new IpSocketAddress.IpSocketAddressIpv4(
                    new Ipv4SocketAddress {
                        Port = 0,
                        Address = (0, 0, 0, 0),
                    }));

        /// <summary>Remote (peer) address the socket is
        /// connected to.</summary>
        public virtual Result<IpSocketAddress, ErrorCode> RemoteAddress()
            => Result<IpSocketAddress, ErrorCode>.FromOk(
                new IpSocketAddress.IpSocketAddressIpv4(
                    new Ipv4SocketAddress {
                        Port = 0,
                        Address = (0, 0, 0, 0),
                    }));

        /// <summary>True iff this socket has transitioned into
        /// the listening state. Bare <c>bool</c> return — no
        /// result wrapping per WIT.</summary>
        public virtual bool IsListening() => _listening;

        /// <summary>Per-socket setter for the listen backlog.
        /// v0 just records the value.</summary>
        public virtual Result<Unit, ErrorCode> SetListenBacklogSize(ulong value)
        {
            ListenBacklogSize = value;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public ulong ListenBacklogSize { get; protected set; } = 128;

        /// <summary>SO_KEEPALIVE getter.</summary>
        public virtual Result<bool, ErrorCode> KeepAliveEnabled()
            => Result<bool, ErrorCode>.FromOk(_keepAliveEnabled);

        public virtual Result<Unit, ErrorCode> SetKeepAliveEnabled(bool value)
        {
            _keepAliveEnabled = value;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        /// <summary>SO_KEEPALIVE idle-time getter (nanoseconds
        /// before the first keep-alive probe).</summary>
        public virtual Result<ulong, ErrorCode> KeepAliveIdleTime()
            => Result<ulong, ErrorCode>.FromOk(_keepAliveIdleTime);

        public virtual Result<Unit, ErrorCode> SetKeepAliveIdleTime(ulong value)
        {
            _keepAliveIdleTime = value;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public virtual Result<ulong, ErrorCode> KeepAliveInterval()
            => Result<ulong, ErrorCode>.FromOk(_keepAliveInterval);

        public virtual Result<Unit, ErrorCode> SetKeepAliveInterval(ulong value)
        {
            _keepAliveInterval = value;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public virtual Result<uint, ErrorCode> KeepAliveCount()
            => Result<uint, ErrorCode>.FromOk(_keepAliveCount);

        public virtual Result<Unit, ErrorCode> SetKeepAliveCount(uint value)
        {
            _keepAliveCount = value;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        /// <summary>IP_TTL / IPV6_UNICAST_HOPS getter.</summary>
        public virtual Result<byte, ErrorCode> HopLimit()
            => Result<byte, ErrorCode>.FromOk(_hopLimit);

        public virtual Result<Unit, ErrorCode> SetHopLimit(byte value)
        {
            _hopLimit = value;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        /// <summary>SO_RCVBUF getter.</summary>
        public virtual Result<ulong, ErrorCode> ReceiveBufferSize()
            => Result<ulong, ErrorCode>.FromOk(_receiveBufferSize);

        public virtual Result<Unit, ErrorCode> SetReceiveBufferSize(ulong value)
        {
            _receiveBufferSize = value;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        /// <summary>SO_SNDBUF getter.</summary>
        public virtual Result<ulong, ErrorCode> SendBufferSize()
            => Result<ulong, ErrorCode>.FromOk(_sendBufferSize);

        public virtual Result<Unit, ErrorCode> SetSendBufferSize(ulong value)
        {
            _sendBufferSize = value;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        protected bool _listening;
        protected bool _keepAliveEnabled;
        protected byte _hopLimit = 64;
        protected ulong _receiveBufferSize = 65_536;
        protected ulong _sendBufferSize = 65_536;
        protected ulong _keepAliveIdleTime = 7_200UL * 1_000_000_000UL;
        protected ulong _keepAliveInterval = 75UL * 1_000_000_000UL;
        protected uint _keepAliveCount = 9;

        public virtual void Dispose() { }
    }

    /// <summary>Host representation of <c>udp-socket</c>.
    /// Implements <see cref="IUdpSocket"/> directly — same
    /// Result-returning pattern as
    /// <see cref="TcpSocket"/>.</summary>
    [WasiResource("udp-socket")]
    public class UdpSocket : IUdpSocket, IDisposable
    {
        public IpAddressFamily Family { get; }
        public UdpSocket(IpAddressFamily family) { Family = family; }

        public virtual IpAddressFamily AddressFamily() => Family;

        public virtual IPollable Subscribe() => new Pollable();

        public virtual Result<Unit, ErrorCode> FinishBind()
            => Result<Unit, ErrorCode>.FromOk(Unit.Value);

        /// <summary>Begin binding to a local address.</summary>
        public virtual Result<Unit, ErrorCode> StartBind(INetwork network,
            IpSocketAddress localAddress)
        {
            _pendingLocal = localAddress;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public IpSocketAddress? PendingLocal => _pendingLocal;
        protected IpSocketAddress? _pendingLocal;

        /// <summary>IP_TTL / IPV6_UNICAST_HOPS getter — UDP
        /// equivalent of TCP's <c>hop-limit</c>.</summary>
        public virtual Result<byte, ErrorCode> UnicastHopLimit()
            => Result<byte, ErrorCode>.FromOk(_hopLimit);

        public virtual Result<Unit, ErrorCode> SetUnicastHopLimit(byte value)
        {
            _hopLimit = value;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public virtual Result<ulong, ErrorCode> ReceiveBufferSize()
            => Result<ulong, ErrorCode>.FromOk(_receiveBufferSize);

        public virtual Result<Unit, ErrorCode> SetReceiveBufferSize(ulong value)
        {
            _receiveBufferSize = value;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public virtual Result<ulong, ErrorCode> SendBufferSize()
            => Result<ulong, ErrorCode>.FromOk(_sendBufferSize);

        public virtual Result<Unit, ErrorCode> SetSendBufferSize(ulong value)
        {
            _sendBufferSize = value;
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        public virtual Result<IpSocketAddress, ErrorCode> LocalAddress()
            => Result<IpSocketAddress, ErrorCode>.FromOk(
                new IpSocketAddress.IpSocketAddressIpv4(
                    new Ipv4SocketAddress {
                        Port = 0,
                        Address = (0, 0, 0, 0),
                    }));

        public virtual Result<IpSocketAddress, ErrorCode> RemoteAddress()
            => Result<IpSocketAddress, ErrorCode>.FromOk(
                new IpSocketAddress.IpSocketAddressIpv4(
                    new Ipv4SocketAddress {
                        Port = 0,
                        Address = (0, 0, 0, 0),
                    }));

        /// <summary>Yield the
        /// (incoming-datagram-stream, outgoing-datagram-stream)
        /// pair for this socket. <paramref name="remoteAddress"/>
        /// optionally pins the streams to a single peer (when
        /// Some); when None the socket is unconnected and any
        /// peer can send to it. Public-virtual return type uses
        /// the concrete subclasses so embeddings can hand back
        /// more-derived streams; the explicit-interface shim
        /// downcasts at the binding boundary.</summary>
        public virtual (IncomingDatagramStream, OutgoingDatagramStream)
            UdpStream(IpSocketAddress? remoteAddress)
            => (new IncomingDatagramStream(),
                new OutgoingDatagramStream());

        Result<(IIncomingDatagramStream, IOutgoingDatagramStream), ErrorCode>
            IUdpSocket.Stream(Option<IpSocketAddress> remoteAddress)
        {
            IpSocketAddress? addr = remoteAddress.HasValue
                ? remoteAddress.Value : null;
            var (inS, outS) = UdpStream(addr);
            return Result<(IIncomingDatagramStream, IOutgoingDatagramStream),
                          ErrorCode>.FromOk((inS, outS));
        }

        protected byte _hopLimit = 64;
        protected ulong _receiveBufferSize = 65_536;
        protected ulong _sendBufferSize = 65_536;

        public virtual void Dispose() { }
    }
}
