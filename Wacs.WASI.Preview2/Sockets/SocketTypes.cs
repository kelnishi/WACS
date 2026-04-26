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
        [WasiMethodName("address-family")]
        public virtual IpAddressFamily AddressFamily() => Family;

        /// <summary>Yield a pollable that fires when this
        /// socket has work to do. v0 default returns an
        /// always-ready Pollable; concrete implementations
        /// subclass for real I/O readiness.</summary>
        public virtual Pollable Subscribe() => new Pollable();

        /// <summary>Complete a previously-issued <c>start-bind</c>
        /// call. Default impl is a no-op since v0 doesn't yet
        /// model the start/finish split.</summary>
        [WasiErrorResult]
        [WasiMethodName("finish-bind")]
        public virtual void FinishBind() { }

        /// <summary>Begin transitioning to the listening state.
        /// Pairs with <see cref="FinishListen"/>.</summary>
        [WasiErrorResult]
        [WasiMethodName("start-listen")]
        public virtual void StartListen() { }

        /// <summary>Complete the listen transition.</summary>
        [WasiErrorResult]
        [WasiMethodName("finish-listen")]
        public virtual void FinishListen() { }

        /// <summary>Initiate a connection-shutdown of the
        /// requested direction. The base impl is a no-op
        /// stub.</summary>
        [WasiErrorResult]
        public virtual void Shutdown(ShutdownType how) { }

        /// <summary>True iff this socket has transitioned into
        /// the listening state. Bare <c>bool</c> return — no
        /// result wrapping per WIT.</summary>
        [WasiMethodName("is-listening")]
        public virtual bool IsListening() => _listening;

        /// <summary>Per-socket setter for the listen backlog.
        /// v0 just records the value.</summary>
        [WasiErrorResult]
        [WasiMethodName("set-listen-backlog-size")]
        public virtual void SetListenBacklogSize(ulong value)
            => ListenBacklogSize = value;

        public ulong ListenBacklogSize { get; protected set; } = 128;

        /// <summary>SO_KEEPALIVE getter.</summary>
        [WasiErrorResult]
        [WasiMethodName("keep-alive-enabled")]
        public virtual bool KeepAliveEnabled() => _keepAliveEnabled;

        [WasiErrorResult]
        [WasiMethodName("set-keep-alive-enabled")]
        public virtual void SetKeepAliveEnabled(bool value)
            => _keepAliveEnabled = value;

        /// <summary>IP_TTL / IPV6_UNICAST_HOPS getter.</summary>
        [WasiErrorResult]
        [WasiMethodName("hop-limit")]
        public virtual byte HopLimit() => _hopLimit;

        [WasiErrorResult]
        [WasiMethodName("set-hop-limit")]
        public virtual void SetHopLimit(byte value)
            => _hopLimit = value;

        /// <summary>SO_RCVBUF getter.</summary>
        [WasiErrorResult]
        [WasiMethodName("receive-buffer-size")]
        public virtual ulong ReceiveBufferSize() => _receiveBufferSize;

        [WasiErrorResult]
        [WasiMethodName("set-receive-buffer-size")]
        public virtual void SetReceiveBufferSize(ulong value)
            => _receiveBufferSize = value;

        /// <summary>SO_SNDBUF getter.</summary>
        [WasiErrorResult]
        [WasiMethodName("send-buffer-size")]
        public virtual ulong SendBufferSize() => _sendBufferSize;

        [WasiErrorResult]
        [WasiMethodName("set-send-buffer-size")]
        public virtual void SetSendBufferSize(ulong value)
            => _sendBufferSize = value;

        protected bool _listening;
        protected bool _keepAliveEnabled;
        protected byte _hopLimit = 64;
        protected ulong _receiveBufferSize = 65_536;
        protected ulong _sendBufferSize = 65_536;

        public virtual void Dispose() { }
    }

    /// <summary>Host representation of <c>udp-socket</c>.
    /// Same marker pattern as <see cref="TcpSocket"/>.</summary>
    [WasiResource("udp-socket")]
    public class UdpSocket : IDisposable
    {
        public IpAddressFamily Family { get; }
        public UdpSocket(IpAddressFamily family) { Family = family; }

        [WasiMethodName("address-family")]
        public virtual IpAddressFamily AddressFamily() => Family;

        public virtual Pollable Subscribe() => new Pollable();

        [WasiErrorResult]
        [WasiMethodName("finish-bind")]
        public virtual void FinishBind() { }

        /// <summary>IP_TTL / IPV6_UNICAST_HOPS getter — UDP
        /// equivalent of TCP's <c>hop-limit</c>.</summary>
        [WasiErrorResult]
        [WasiMethodName("unicast-hop-limit")]
        public virtual byte UnicastHopLimit() => _hopLimit;

        [WasiErrorResult]
        [WasiMethodName("set-unicast-hop-limit")]
        public virtual void SetUnicastHopLimit(byte value)
            => _hopLimit = value;

        [WasiErrorResult]
        [WasiMethodName("receive-buffer-size")]
        public virtual ulong ReceiveBufferSize() => _receiveBufferSize;

        [WasiErrorResult]
        [WasiMethodName("set-receive-buffer-size")]
        public virtual void SetReceiveBufferSize(ulong value)
            => _receiveBufferSize = value;

        [WasiErrorResult]
        [WasiMethodName("send-buffer-size")]
        public virtual ulong SendBufferSize() => _sendBufferSize;

        [WasiErrorResult]
        [WasiMethodName("set-send-buffer-size")]
        public virtual void SetSendBufferSize(ulong value)
            => _sendBufferSize = value;

        protected byte _hopLimit = 64;
        protected ulong _receiveBufferSize = 65_536;
        protected ulong _sendBufferSize = 65_536;

        public virtual void Dispose() { }
    }
}
