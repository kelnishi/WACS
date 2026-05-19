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
            try
            {
                var ep = ToIpEndPoint(localAddress);
                _socket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress, true);
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
        {
            throw new SocketsException(
                ErrorCode.NotSupported,
                "tcp-socket.listen: pending stream<tcp-socket> " +
                "return shape.");
        }

        public (int futureHandle, Task SendCompletion) Send(
            AsyncDispatcher dispatcher, int streamHandle)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            if (_state != State.Connected)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "tcp-socket.send: socket is not in the " +
                    $"connected state (state = {_state}).");
            var futureHandle = dispatcher.FutureNew(typeIdx: 0);
            var buffer = dispatcher.GetByteStreamBuffer(streamHandle);
            if (buffer == null)
            {
                dispatcher.FutureWrite(futureHandle,
                    new SocketsException(
                        ErrorCode.InvalidArgument,
                        $"stream handle {streamHandle} not allocated"));
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
            if (_state != State.Connected)
                throw new SocketsException(
                    ErrorCode.InvalidState,
                    "tcp-socket.receive: socket is not in the " +
                    $"connected state (state = {_state}).");
            var streamHandle = dispatcher.StreamNew(typeIdx: 0);
            var futureHandle = dispatcher.FutureNew(typeIdx: 0);

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
        public ulong GetKeepAliveIdleTime()
        {
            throw new SocketsException(
                ErrorCode.NotSupported,
                "tcp-socket.get-keep-alive-idle-time: not exposed by " +
                "the .NET Socket API.");
        }
        public void SetKeepAliveIdleTime(ulong nanoseconds)
        {
            throw new SocketsException(
                ErrorCode.NotSupported,
                "tcp-socket.set-keep-alive-idle-time: not exposed by " +
                "the .NET Socket API.");
        }
        public ulong GetKeepAliveInterval()
        {
            throw new SocketsException(
                ErrorCode.NotSupported,
                "tcp-socket.get-keep-alive-interval: not exposed.");
        }
        public void SetKeepAliveInterval(ulong nanoseconds)
        {
            throw new SocketsException(
                ErrorCode.NotSupported,
                "tcp-socket.set-keep-alive-interval: not exposed.");
        }
        public uint GetKeepAliveCount()
        {
            throw new SocketsException(
                ErrorCode.NotSupported,
                "tcp-socket.get-keep-alive-count: not exposed.");
        }
        public void SetKeepAliveCount(uint count)
        {
            throw new SocketsException(
                ErrorCode.NotSupported,
                "tcp-socket.set-keep-alive-count: not exposed.");
        }

        public byte GetHopLimit()
        {
            EnsureNotClosed();
            var level = _family == IpAddressFamily.Ipv4
                ? SocketOptionLevel.IP
                : SocketOptionLevel.IPv6;
            var option = _family == IpAddressFamily.Ipv4
                ? SocketOptionName.IpTimeToLive
                : SocketOptionName.HopLimit;
            return (byte)(int)(_socket.GetSocketOption(level, option) ?? 0);
        }
        public void SetHopLimit(byte value)
        {
            EnsureNotClosed();
            var level = _family == IpAddressFamily.Ipv4
                ? SocketOptionLevel.IP
                : SocketOptionLevel.IPv6;
            var option = _family == IpAddressFamily.Ipv4
                ? SocketOptionName.IpTimeToLive
                : SocketOptionName.HopLimit;
            _socket.SetSocketOption(level, option, (int)value);
        }

        public void SetListenBacklogSize(ulong value)
        {
            // .NET's Socket.Listen takes the backlog as a Listen
            // argument; there's no separate setter. Stash the
            // value (no-op for now — listen() isn't wired yet).
            _backlogHint = (int)Math.Min(value, int.MaxValue);
        }
        private int _backlogHint = 32;
        internal int BacklogHint => _backlogHint;

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
