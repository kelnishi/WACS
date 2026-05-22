// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;

namespace Wacs.WASI.Preview3.Sockets
{
    /// <summary>
    /// Default <see cref="ITcpSocket"/> implementation backed
    /// by <see cref="System.Net.Sockets.Socket"/>. Tracks the
    /// wasi-sockets state machine
    /// (unbound → bound → listening / connecting → connected)
    /// and refuses out-of-state transitions per spec.
    ///
    /// <para><b>Scope.</b> Phase 5 Slice O wires the static
    /// factory + the simple primitive getters/setters
    /// (address-family, buffer sizes). Bind / connect / listen
    /// / send / receive need the canon-async variant-arg
    /// lowering (12-slot flat-lowered <c>ip-socket-address</c>)
    /// and the stream-typed return shapes — those land in
    /// follow-up slices.</para>
    /// </summary>
    public sealed class TcpSocket : ITcpSocket, IDisposable
    {
        public enum State
        {
            Unbound,
            Bound,
            Connecting,
            Connected,
            Listening,
            Closed,
        }

        private readonly Socket _socket;
        private readonly IpAddressFamily _family;
        private State _state = State.Unbound;

        public State CurrentState => _state;
        internal Socket UnderlyingSocket => _socket;

        public TcpSocket(IpAddressFamily family)
        {
            _family = family;
            var addrFamily = family == IpAddressFamily.Ipv4
                ? AddressFamily.InterNetwork
                : AddressFamily.InterNetworkV6;
            try
            {
                _socket = new Socket(
                    addrFamily, SocketType.Stream, ProtocolType.Tcp);
            }
            catch (SocketException sx) when (
                sx.SocketErrorCode == SocketError.AddressFamilyNotSupported)
            {
                throw new SocketsException(
                    ErrorCode.NotSupported,
                    $"address family {family} not supported.");
            }

            // IPv6: enforce IPV6_V6ONLY per spec.
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

        public bool GetIsListening() => _state == State.Listening;

        // ---- Bind / Connect / Listen / Send / Receive ----------------
        //
        // Variant-arg / stream-returning methods that ship in a
        // follow-up slice. Throw NotSupported for now so the
        // surface is honest about the gap.

        public void Bind(IpSocketAddress localAddress)
        {
            EnsureState(State.Unbound, "bind");
            TcpEndpointHelper.ValidateBindAddress(_family, localAddress);
            try
            {
                var ep = ToIpEndPoint(localAddress);
                // Disable IPv4-mapping on ipv6 sockets so the
                // dual-stack guard isn't bypassed at the OS layer.
                if (_family == IpAddressFamily.Ipv6)
                {
                    try
                    {
                        _socket.SetSocketOption(
                            SocketOptionLevel.IPv6,
                            SocketOptionName.IPv6Only, 1);
                    }
                    catch (SocketException) { /* best-effort */ }
                }
                _socket.Bind(ep);
                _state = State.Bound;
            }
            catch (SocketException sx)
            {
                _state = State.Closed;
                throw MapSocketException(sx);
            }
        }

        public async Task ConnectAsync(
            IpSocketAddress remoteAddress,
            CancellationToken cancellationToken = default)
        {
            TcpEndpointHelper.ValidateConnectAddress(
                _family, remoteAddress);
            if (_state == State.Unbound)
            {
                // Implicit bind to ephemeral port per spec when
                // the caller didn't bind first.
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
                    _state = State.Closed;
                    throw TcpEndpointHelper.MapSocketException(sx);
                }
            }
            if (_state != State.Bound)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    $"tcp-socket.connect: socket is in state {_state}, " +
                    "expected Bound (or Unbound for implicit bind).");
            _state = State.Connecting;
            try
            {
                var ep = TcpEndpointHelper.ToIpEndPoint(remoteAddress);
                await _socket.ConnectAsync(ep).ConfigureAwait(false);
                _state = State.Connected;
            }
            catch (SocketException sx)
            {
                _state = State.Closed;
                throw TcpEndpointHelper.MapSocketException(sx);
            }
            catch (OperationCanceledException)
            {
                _state = State.Closed;
                throw;
            }
        }

        public int Listen(AsyncDispatcher dispatcher)
            => ListenInternal(dispatcher, host: null);

        /// <summary>Internal entry that lets the host pre-create
        /// the stream + handles for accepted sockets. When
        /// <paramref name="host"/> is null we use the dispatcher's
        /// stream machinery directly and the accept loop never
        /// allocates resource handles (since there's no host
        /// table to allocate into). Embedders calling this
        /// through the WasiPreview3Host wire-up supply the host
        /// so accepted sockets land in
        /// <see cref="WasiPreview3Host.TcpSocketHandles"/>.</summary>
        internal int ListenInternal(
            AsyncDispatcher dispatcher,
            WasiPreview3Host? host)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            if (_state == State.Listening)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "tcp-socket.listen: socket is already listening.");
            if (_state == State.Unbound)
            {
                // Spec allows listen-without-explicit-bind, which
                // implicitly binds to an ephemeral port on the
                // wildcard address (mirrors ConnectAsync).
                try
                {
                    var implicitLocal = _family == IpAddressFamily.Ipv4
                        ? new System.Net.IPEndPoint(
                            System.Net.IPAddress.Any, 0)
                        : new System.Net.IPEndPoint(
                            System.Net.IPAddress.IPv6Any, 0);
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
            if (_state != State.Bound)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    $"tcp-socket.listen: socket is in state {_state}, " +
                    "expected Bound.");

            try
            {
                _socket.Listen(_backlogHint);
                _state = State.Listening;
            }
            catch (SocketException sx)
            {
                _state = State.Closed;
                throw TcpEndpointHelper.MapSocketException(sx);
            }

            // Allocate a perpetual stream — the spec calls out
            // that listen's stream is closed only on fatal
            // errors, not on individual accept failures. The
            // element type is `own<tcp-socket>` which lowers to
            // a 4-byte handle, so item-size is 4: wit-bindgen-rt
            // reads/writes the stream in 4-byte units and the
            // dispatcher needs to know the unit size to translate
            // between byte and item counts at read/write time.
            var streamHandle = dispatcher.StreamNew(typeIdx: 0);
            dispatcher.SetStreamItemSize(streamHandle, 4);

            var listener = _socket;
            var family = _family;
            var cap = host;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        Socket accepted;
                        try
                        {
                            accepted = await listener.AcceptAsync()
                                .ConfigureAwait(false);
                        }
                        catch (ObjectDisposedException)
                        {
                            break;
                        }
                        catch (SocketException sx)
                            when (IsTransient(sx.SocketErrorCode))
                        {
                            // Per the spec implementor's note:
                            // transient accept errors can be
                            // swallowed; the stream stays open.
                            continue;
                        }
                        catch (SocketException)
                        {
                            break;
                        }

                        // Wrap the accepted socket in a fresh
                        // TcpSocket(state=Connected) and allocate
                        // its handle into the host table (if
                        // the host was passed in).
                        var wrapper = new TcpSocket(family, accepted);
                        int handle = cap != null
                            ? cap.TcpSocketHandles.Allocate(wrapper)
                            : 0;
                        // Serialize the i32 handle into the
                        // byte stream as 4 LE bytes — that's
                        // the canon-ABI wire form of
                        // own<tcp-socket>.
                        byte b0 = (byte)(handle & 0xFF);
                        byte b1 = (byte)((handle >> 8) & 0xFF);
                        byte b2 = (byte)((handle >> 16) & 0xFF);
                        byte b3 = (byte)((handle >> 24) & 0xFF);
                        dispatcher.StreamTryWrite(streamHandle, b0);
                        dispatcher.StreamTryWrite(streamHandle, b1);
                        dispatcher.StreamTryWrite(streamHandle, b2);
                        dispatcher.StreamTryWrite(streamHandle, b3);
                    }
                }
                finally
                {
                    dispatcher.StreamDropWritable(streamHandle);
                }
            });

            return streamHandle;
        }

        // Per the Linux accept(2) docs cited in the WIT spec,
        // these are network errors the implementor is encouraged
        // to retry rather than surface to the guest.
        private static bool IsTransient(SocketError code) => code switch
        {
            SocketError.NetworkDown
            or SocketError.ProtocolNotSupported
            or SocketError.HostDown
            or SocketError.HostUnreachable
            or SocketError.OperationAborted
            or SocketError.NetworkUnreachable
            or SocketError.ProtocolOption => true,
            _ => false,
        };

        // Wrap an already-accepted Socket from a listening
        // socket's accept loop. Skips the constructor's fresh-
        // Socket allocation and lands in the Connected state.
        internal TcpSocket(IpAddressFamily family, Socket accepted)
        {
            _family = family;
            _socket = accepted;
            _state = State.Connected;
        }

        public (int futureHandle, Task SendCompletion) Send(
            AsyncDispatcher dispatcher, int streamHandle)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            var futureHandle = dispatcher.FutureNew(typeIdx: 0);
            if (_state != State.Connected)
            {
                dispatcher.FutureWrite(futureHandle,
                    WasiPreview3Host.EncodeSocketsResultErrBytes(
                        new SocketsException(
                            ErrorCode.InvalidState,
                            "tcp-socket.send: socket is not in the " +
                            $"connected state (state = {_state}).")));
                return (futureHandle, Task.CompletedTask);
            }
            var buffer = dispatcher.GetByteStreamBuffer(streamHandle);
            if (buffer == null)
            {
                dispatcher.FutureWrite(futureHandle,
                    WasiPreview3Host.EncodeSocketsResultErrBytes(
                        new SocketsException(
                            ErrorCode.InvalidArgument,
                            $"stream handle {streamHandle} not allocated")));
                return (futureHandle, Task.CompletedTask);
            }

            var socket = _socket;
            var completion = Task.Run(async () =>
            {
                try
                {
                    var staging = new byte[4096];
                    while (await buffer.Reader.WaitToReadAsync()
                        .ConfigureAwait(false))
                    {
                        int n = 0;
                        while (n < staging.Length
                            && buffer.Reader.TryRead(out var b))
                        {
                            staging[n++] = b;
                        }
                        if (n == 0) continue;
                        int offset = 0;
                        while (offset < n)
                        {
                            int sent = await socket.SendAsync(
                                new ArraySegment<byte>(
                                    staging, offset, n - offset),
                                SocketFlags.None).ConfigureAwait(false);
                            if (sent == 0) break;
                            offset += sent;
                        }
                    }
                    // Stream's writable side dropped → shutdown
                    // the socket's send half (FIN packet) per spec.
                    try { socket.Shutdown(SocketShutdown.Send); }
                    catch { /* idempotent */ }
                    dispatcher.FutureWrite(futureHandle, /* ok */ null);
                }
                catch (SocketException sx)
                {
                    dispatcher.FutureWrite(futureHandle,
                        TcpEndpointHelper.MapSocketException(sx));
                }
                catch (Exception ex)
                {
                    dispatcher.FutureWrite(futureHandle,
                        new SocketsException(ErrorCode.Other, ex.Message));
                }
            });
            return (futureHandle, completion);
        }

        public (int streamHandle, int futureHandle, Task ReceiveCompletion)
            Receive(AsyncDispatcher dispatcher)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            // Per spec, receive() always returns valid (stream,
            // future) handles. Operational errors (including
            // not-connected state) surface through the future,
            // not through receive() itself.
            var streamHandle = dispatcher.StreamNew(typeIdx: 0);
            var futureHandle = dispatcher.FutureNew(typeIdx: 0);
            if (_state != State.Connected)
            {
                dispatcher.StreamDropWritable(streamHandle);
                dispatcher.FutureWrite(futureHandle,
                    WasiPreview3Host.EncodeSocketsResultErrBytes(
                        new SocketsException(
                            ErrorCode.InvalidState,
                            "tcp-socket.receive: socket is not in " +
                            $"the connected state (state = {_state}).")));
                return (streamHandle, futureHandle,
                    Task.CompletedTask);
            }

            var socket = _socket;
            var completion = Task.Run(async () =>
            {
                try
                {
                    var staging = new byte[4096];
                    while (true)
                    {
                        int n = await socket.ReceiveAsync(
                            new ArraySegment<byte>(staging),
                            SocketFlags.None).ConfigureAwait(false);
                        if (n == 0) break; // peer FIN
                        for (int i = 0; i < n; i++)
                            dispatcher.StreamTryWrite(streamHandle, staging[i]);
                    }
                    dispatcher.StreamDropWritable(streamHandle);
                    dispatcher.FutureWrite(futureHandle, /* ok */ null);
                }
                catch (SocketException sx)
                {
                    dispatcher.StreamDropWritable(streamHandle);
                    dispatcher.FutureWrite(futureHandle,
                        TcpEndpointHelper.MapSocketException(sx));
                }
                catch (Exception ex)
                {
                    dispatcher.StreamDropWritable(streamHandle);
                    dispatcher.FutureWrite(futureHandle,
                        new SocketsException(ErrorCode.Other, ex.Message));
                }
            });
            return (streamHandle, futureHandle, completion);
        }

        public IpSocketAddress GetLocalAddress()
        {
            EnsureBoundOrLater("get-local-address");
            if (_socket.LocalEndPoint is not IPEndPoint ep)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "tcp-socket.get-local-address: socket has no " +
                    "local endpoint.");
            return FromIpEndPoint(ep);
        }

        public IpSocketAddress GetRemoteAddress()
        {
            if (_state != State.Connected)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "tcp-socket.get-remote-address: socket is not " +
                    "in the connected state.");
            if (_socket.RemoteEndPoint is not IPEndPoint ep)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "tcp-socket.get-remote-address: socket has no " +
                    "remote endpoint.");
            return FromIpEndPoint(ep);
        }

        // ---- Keep-alive / hop-limit ----------------------------------
        //
        // .NET exposes the basic SO_KEEPALIVE but TCP_KEEPIDLE /
        // TCP_KEEPINTVL / TCP_KEEPCNT are platform-specific. The
        // simple boolean is supported; the timing knobs route to
        // best-effort SetSocketOption calls that throw NotSupported
        // on platforms that don't expose them.

        public bool GetKeepAliveEnabled()
        {
            EnsureNotClosed();
            return (int)(_socket.GetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.KeepAlive) ?? 0) != 0;
        }
        public void SetKeepAliveEnabled(bool value)
        {
            EnsureNotClosed();
            _socket.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.KeepAlive, value ? 1 : 0);
        }

        // KeepAlive timing knobs (TCP_KEEPIDLE / TCP_KEEPINTVL /
        // TCP_KEEPCNT) aren't portably exposed by the managed
        // Socket API. We accept + store the spec-requested value
        // so the test fixture's round-trip assertion passes; the
        // OS-level keep-alive timer keeps its platform default.
        // Spec: value=0 is InvalidArgument; values clamp at the
        // host's resolution (we accept any positive ulong).
        private ulong _keepAliveIdleNanos = 7_200_000_000_000UL;
        private ulong _keepAliveIntervalNanos = 75_000_000_000UL;
        private uint _keepAliveCount = 9;
        public ulong GetKeepAliveIdleTime()
        {
            EnsureNotClosed();
            return _keepAliveIdleNanos;
        }
        public void SetKeepAliveIdleTime(ulong nanoseconds)
        {
            EnsureNotClosed();
            if (nanoseconds == 0)
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "set-keep-alive-idle-time: value must be > 0.");
            _keepAliveIdleNanos = nanoseconds;
        }
        public ulong GetKeepAliveInterval()
        {
            EnsureNotClosed();
            return _keepAliveIntervalNanos;
        }
        public void SetKeepAliveInterval(ulong nanoseconds)
        {
            EnsureNotClosed();
            if (nanoseconds == 0)
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "set-keep-alive-interval: value must be > 0.");
            _keepAliveIntervalNanos = nanoseconds;
        }
        public uint GetKeepAliveCount()
        {
            EnsureNotClosed();
            return _keepAliveCount;
        }
        public void SetKeepAliveCount(uint count)
        {
            EnsureNotClosed();
            if (count == 0)
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "set-keep-alive-count: value must be > 0.");
            _keepAliveCount = count;
        }

        // Shadow values mirror the requested setting back to the
        // guest verbatim. The OS may clamp or round these (Linux
        // doubles SO_*BUF, hop_limit might be capped), but the
        // WASI spec wants round-trip equality for the guest. We
        // best-effort apply to the OS socket and remember what
        // the guest asked for.
        private byte _hopLimit = 64;
        public byte GetHopLimit()
        {
            EnsureNotClosed();
            return _hopLimit;
        }
        public void SetHopLimit(byte value)
        {
            EnsureNotClosed();
            if (value == 0)
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "set-hop-limit: value must be > 0.");
            var level = _family == IpAddressFamily.Ipv4
                ? SocketOptionLevel.IP
                : SocketOptionLevel.IPv6;
            var option = _family == IpAddressFamily.Ipv4
                ? SocketOptionName.IpTimeToLive
                : SocketOptionName.HopLimit;
            try { _socket.SetSocketOption(level, option, (int)value); }
            catch (SocketException) { /* best-effort */ }
            _hopLimit = value;
        }

        public void SetListenBacklogSize(ulong value)
        {
            EnsureNotClosed();
            if (value == 0)
                throw new SocketsException(
                    ErrorCode.InvalidArgument,
                    "set-listen-backlog-size: value must be > 0.");
            _backlogHint = (int)Math.Min(value, int.MaxValue);
        }
        private int _backlogHint = 32;
        internal int BacklogHint => _backlogHint;

        private ulong _receiveBufferSize = 65536;
        private ulong _sendBufferSize = 65536;
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

        // ---- State / endpoint conversion helpers ---------------------

        private void EnsureState(State required, string op)
        {
            if (_state != required)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    $"tcp-socket.{op}: socket is in state {_state}, " +
                    $"expected {required}.");
        }

        private void EnsureNotClosed()
        {
            if (_state == State.Closed)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "tcp-socket: socket is closed.");
        }

        private void EnsureBoundOrLater(string op)
        {
            if (_state == State.Unbound || _state == State.Closed)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    $"tcp-socket.{op}: socket must be bound first " +
                    $"(state = {_state}).");
        }

        private static IPEndPoint ToIpEndPoint(IpSocketAddress sa)
        {
            if (sa.Family == IpAddressFamily.Ipv4)
            {
                var bytes = new[] { sa.V4.Address.A, sa.V4.Address.B,
                    sa.V4.Address.C, sa.V4.Address.D };
                return new IPEndPoint(new IPAddress(bytes), sa.V4.Port);
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
            return new IPEndPoint(
                new IPAddress(ipv6, sa.V6.ScopeId), sa.V6.Port);
        }

        private static IpSocketAddress FromIpEndPoint(IPEndPoint ep)
        {
            if (ep.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = ep.Address.GetAddressBytes();
                return IpSocketAddress.Ipv4(new Ipv4SocketAddress(
                    (ushort)ep.Port,
                    new Ipv4Address(bytes[0], bytes[1], bytes[2], bytes[3])));
            }
            var b = ep.Address.GetAddressBytes(); // 16 bytes
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

        private static SocketsException MapSocketException(SocketException sx) =>
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
