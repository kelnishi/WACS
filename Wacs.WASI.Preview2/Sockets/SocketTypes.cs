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

    /// <summary>WIT enum
    /// <c>wasi:sockets/tcp.shutdown-type</c>:
    /// <code>enum shutdown-type { receive, send, both }</code>
    /// </summary>
    public enum ShutdownType : byte
    {
        Receive = 0,
        Send = 1,
        Both = 2,
    }

    /// <summary>WIT variant
    /// <c>wasi:sockets/network.ip-socket-address</c>. Wire
    /// form: 1-byte variant disc + 3-byte align padding +
    /// 28-byte payload (max of the two case sizes). Total
    /// 32 bytes, align 4. Two case classes mirror the WIT
    /// shape; the binder dispatches on runtime type at
    /// canon-lower time.</summary>
    public abstract class IpSocketAddress
    {
        public IpAddressFamily Family { get; }
        public ushort Port { get; }
        protected IpSocketAddress(IpAddressFamily family, ushort port)
        {
            Family = family;
            Port = port;
        }
    }

    /// <summary>IPv4 case of <see cref="IpSocketAddress"/>:
    /// <code>record ipv4-socket-address { port: u16, address: tuple&lt;u8, u8, u8, u8&gt; }</code>
    /// Wire size 6 (port:2 + address:4), align 2.</summary>
    public sealed class Ipv4SocketAddress : IpSocketAddress
    {
        /// <summary>4-octet IPv4 address in network order
        /// (a.b.c.d → bytes[0..3]).</summary>
        public byte[] Address { get; }

        public Ipv4SocketAddress(ushort port, byte[] address)
            : base(IpAddressFamily.Ipv4, port)
        {
            if (address == null || address.Length != 4)
                throw new ArgumentException(
                    "IPv4 address must be exactly 4 bytes.",
                    nameof(address));
            Address = address;
        }
    }

    /// <summary>IPv6 case of <see cref="IpSocketAddress"/>:
    /// <code>record ipv6-socket-address {
    ///     port: u16, flow-info: u32,
    ///     address: tuple&lt;u16, u16, u16, u16, u16, u16, u16, u16&gt;,
    ///     scope-id: u32,
    /// }</code>
    /// Wire size 28 (port:2 + pad:2 + flow:4 + addr:16 +
    /// scope:4), align 4.</summary>
    public sealed class Ipv6SocketAddress : IpSocketAddress
    {
        public uint FlowInfo { get; }
        /// <summary>8-group IPv6 address (each u16 in network
        /// order).</summary>
        public ushort[] Address { get; }
        public uint ScopeId { get; }

        public Ipv6SocketAddress(ushort port, uint flowInfo,
            ushort[] address, uint scopeId)
            : base(IpAddressFamily.Ipv6, port)
        {
            if (address == null || address.Length != 8)
                throw new ArgumentException(
                    "IPv6 address must be exactly 8 u16 groups.",
                    nameof(address));
            FlowInfo = flowInfo;
            Address = address;
            ScopeId = scopeId;
        }
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

        /// <summary>Return the socket's address family. WIT's
        /// <c>address-family() -&gt; ip-address-family</c> —
        /// no result wrapping; bare enum return.</summary>
        public virtual IpAddressFamily AddressFamily() => Family;

        /// <summary>Yield a pollable that fires when this
        /// socket has work to do. v0 default returns an
        /// always-ready Pollable; concrete implementations
        /// subclass for real I/O readiness.</summary>
        public virtual Pollable Subscribe() => new Pollable();

        /// <summary>Begin binding to <paramref name="localAddress"/>.
        /// Pairs with <see cref="FinishBind"/>. Default
        /// captures the address; concrete impls invoke real
        /// socket APIs.</summary>
        public virtual void StartBind(Network network,
            IpSocketAddress localAddress)
            => _pendingLocal = localAddress;

        /// <summary>Begin connecting to
        /// <paramref name="remoteAddress"/>.</summary>
        public virtual void StartConnect(Network network,
            IpSocketAddress remoteAddress)
            => _pendingRemote = remoteAddress;

        public IpSocketAddress? PendingLocal => _pendingLocal;
        public IpSocketAddress? PendingRemote => _pendingRemote;
        protected IpSocketAddress? _pendingLocal;
        protected IpSocketAddress? _pendingRemote;

        /// <summary>Complete a previously-issued <c>start-bind</c>
        /// call. Default impl is a no-op since v0 doesn't yet
        /// model the start/finish split.</summary>
        public virtual void FinishBind() { }

        /// <summary>Begin transitioning to the listening state.
        /// Pairs with <see cref="FinishListen"/>.</summary>
        public virtual void StartListen() { }

        /// <summary>Complete the listen transition.</summary>
        public virtual void FinishListen() { }

        /// <summary>Complete a previously-issued
        /// <c>start-connect</c> call. Returns the
        /// (input-stream, output-stream) pair for the freshly
        /// established connection — guests use these to read
        /// and write the TCP byte stream. Default returns a
        /// pair of stub streams.</summary>
        public virtual (InputStream, OutputStream) FinishConnect()
            => (new InputStream(), new OutputStream());

        /// <summary>Accept a pending incoming connection.
        /// Returns the new <c>tcp-socket</c> + its
        /// (input-stream, output-stream) pair. Default returns
        /// stub instances.</summary>
        public virtual (TcpSocket, InputStream, OutputStream) Accept()
            => (new TcpSocket(Family),
                new InputStream(), new OutputStream());

        /// <summary>Initiate a connection-shutdown of the
        /// requested direction. The base impl is a no-op
        /// stub.</summary>
        public virtual void Shutdown(ShutdownType how) { }

        /// <summary>Local address the socket is bound to.
        /// Default returns 0.0.0.0:0 — concrete subclasses
        /// override after a real bind.</summary>
        public virtual IpSocketAddress LocalAddress()
            => new Ipv4SocketAddress(0, new byte[] { 0, 0, 0, 0 });

        /// <summary>Remote (peer) address the socket is
        /// connected to.</summary>
        public virtual IpSocketAddress RemoteAddress()
            => new Ipv4SocketAddress(0, new byte[] { 0, 0, 0, 0 });

        /// <summary>True iff this socket has transitioned into
        /// the listening state. Bare <c>bool</c> return — no
        /// result wrapping per WIT.</summary>
        public virtual bool IsListening() => _listening;

        /// <summary>Per-socket setter for the listen backlog.
        /// v0 just records the value.</summary>
        public virtual void SetListenBacklogSize(ulong value)
            => ListenBacklogSize = value;

        public ulong ListenBacklogSize { get; protected set; } = 128;

        /// <summary>SO_KEEPALIVE getter.</summary>
        public virtual bool KeepAliveEnabled() => _keepAliveEnabled;

        public virtual void SetKeepAliveEnabled(bool value)
            => _keepAliveEnabled = value;

        /// <summary>SO_KEEPALIVE idle-time getter (nanoseconds
        /// before the first keep-alive probe).</summary>
        public virtual ulong KeepAliveIdleTime() => _keepAliveIdleTime;

        public virtual void SetKeepAliveIdleTime(ulong value)
            => _keepAliveIdleTime = value;

        public virtual ulong KeepAliveInterval() => _keepAliveInterval;

        public virtual void SetKeepAliveInterval(ulong value)
            => _keepAliveInterval = value;

        public virtual uint KeepAliveCount() => _keepAliveCount;

        public virtual void SetKeepAliveCount(uint value)
            => _keepAliveCount = value;

        /// <summary>IP_TTL / IPV6_UNICAST_HOPS getter.</summary>
        public virtual byte HopLimit() => _hopLimit;

        public virtual void SetHopLimit(byte value)
            => _hopLimit = value;

        /// <summary>SO_RCVBUF getter.</summary>
        public virtual ulong ReceiveBufferSize() => _receiveBufferSize;

        public virtual void SetReceiveBufferSize(ulong value)
            => _receiveBufferSize = value;

        /// <summary>SO_SNDBUF getter.</summary>
        public virtual ulong SendBufferSize() => _sendBufferSize;

        public virtual void SetSendBufferSize(ulong value)
            => _sendBufferSize = value;

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
    /// Same marker pattern as <see cref="TcpSocket"/>.</summary>
    [WasiResource("udp-socket")]
    public class UdpSocket : IDisposable
    {
        public IpAddressFamily Family { get; }
        public UdpSocket(IpAddressFamily family) { Family = family; }

        public virtual IpAddressFamily AddressFamily() => Family;

        public virtual Pollable Subscribe() => new Pollable();

        public virtual void FinishBind() { }

        /// <summary>Begin binding to a local address.</summary>
        public virtual void StartBind(Network network,
            IpSocketAddress localAddress)
            => _pendingLocal = localAddress;

        public IpSocketAddress? PendingLocal => _pendingLocal;
        protected IpSocketAddress? _pendingLocal;

        /// <summary>IP_TTL / IPV6_UNICAST_HOPS getter — UDP
        /// equivalent of TCP's <c>hop-limit</c>.</summary>
        public virtual byte UnicastHopLimit() => _hopLimit;

        public virtual void SetUnicastHopLimit(byte value)
            => _hopLimit = value;

        public virtual ulong ReceiveBufferSize() => _receiveBufferSize;

        public virtual void SetReceiveBufferSize(ulong value)
            => _receiveBufferSize = value;

        public virtual ulong SendBufferSize() => _sendBufferSize;

        public virtual void SetSendBufferSize(ulong value)
            => _sendBufferSize = value;

        public virtual IpSocketAddress LocalAddress()
            => new Ipv4SocketAddress(0, new byte[] { 0, 0, 0, 0 });

        public virtual IpSocketAddress RemoteAddress()
            => new Ipv4SocketAddress(0, new byte[] { 0, 0, 0, 0 });

        /// <summary>Yield the
        /// (incoming-datagram-stream, outgoing-datagram-stream)
        /// pair for this socket. <paramref name="remoteAddress"/>
        /// optionally pins the streams to a single peer (when
        /// non-null); when null the socket is unconnected and
        /// any peer can send to it. Default returns stub
        /// streams.</summary>
        public virtual (IncomingDatagramStream, OutgoingDatagramStream)
            UdpStream(IpSocketAddress? remoteAddress)
            => (new IncomingDatagramStream(),
                new OutgoingDatagramStream());

        protected byte _hopLimit = 64;
        protected ulong _receiveBufferSize = 65_536;
        protected ulong _sendBufferSize = 65_536;

        public virtual void Dispose() { }
    }
}
