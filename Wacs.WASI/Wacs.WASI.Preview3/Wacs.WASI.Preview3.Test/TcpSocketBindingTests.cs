// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.CanonicalAbi;
using Wacs.WASI.Preview3.Sockets;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice O coverage: <see cref="TcpSocket"/>
    /// System.Net.Sockets-backed default + the static
    /// `create` + simple getter/setter wire-up. Bind /
    /// Connect / Listen / Send / Receive remain pending
    /// canon-async variant-arg lowering and stream-shape
    /// work.
    /// </summary>
    public class TcpSocketBindingTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        private sealed class StubRealloc : ICabiRealloc
        {
            private readonly int[] _allocations;
            private int _next;
            public StubRealloc(int[] allocations)
            {
                _allocations = allocations;
            }
            public int Allocate(int align, int size)
            {
                if (_next >= _allocations.Length)
                    throw new InvalidOperationException(
                        "StubRealloc out of pre-canned allocations.");
                return _allocations[_next++];
            }
        }

        // ---- TcpSocket impl directly --------------------------------

        [Fact]
        public void TcpSocket_ipv4_state_starts_unbound_with_correct_family()
        {
            using var sock = new TcpSocket(IpAddressFamily.Ipv4);
            Assert.Equal(IpAddressFamily.Ipv4, sock.GetAddressFamily());
            Assert.Equal(TcpSocket.State.Unbound, sock.CurrentState);
            Assert.False(sock.GetIsListening());
        }

        [Fact]
        public void TcpSocket_ipv6_state_starts_unbound()
        {
            using var sock = new TcpSocket(IpAddressFamily.Ipv6);
            Assert.Equal(IpAddressFamily.Ipv6, sock.GetAddressFamily());
            Assert.Equal(TcpSocket.State.Unbound, sock.CurrentState);
        }

        [Fact]
        public void TcpSocket_bind_loopback_transitions_to_bound()
        {
            using var sock = new TcpSocket(IpAddressFamily.Ipv4);
            sock.Bind(IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(0, Ipv4Address.Loopback)));
            Assert.Equal(TcpSocket.State.Bound, sock.CurrentState);

            var local = sock.GetLocalAddress();
            Assert.Equal(IpAddressFamily.Ipv4, local.Family);
            Assert.Equal(Ipv4Address.Loopback, local.V4.Address);
            // Port should be ephemeral (>0 after bind to port 0).
            Assert.True(local.V4.Port > 0);
        }

        [Fact]
        public void TcpSocket_bind_invalid_state_throws_invalid_state()
        {
            using var sock = new TcpSocket(IpAddressFamily.Ipv4);
            sock.Bind(IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(0, Ipv4Address.Loopback)));
            // Second bind from non-Unbound state → InvalidState.
            var ex = Assert.Throws<SocketsException>(() =>
                sock.Bind(IpSocketAddress.Ipv4(
                    new Ipv4SocketAddress(0, Ipv4Address.Loopback))));
            Assert.Equal(Sockets.ErrorCode.InvalidState, ex.Code);
        }

        [Fact]
        public void TcpSocket_get_remote_address_unbound_throws_invalid_state()
        {
            using var sock = new TcpSocket(IpAddressFamily.Ipv4);
            var ex = Assert.Throws<SocketsException>(
                () => sock.GetRemoteAddress());
            Assert.Equal(Sockets.ErrorCode.InvalidState, ex.Code);
        }

        [Fact]
        public void TcpSocket_buffer_size_round_trips()
        {
            using var sock = new TcpSocket(IpAddressFamily.Ipv4);
            sock.SetReceiveBufferSize(65536);
            sock.SetSendBufferSize(32768);
            // OS-level setsockopt rounds; just confirm non-zero.
            Assert.True(sock.GetReceiveBufferSize() > 0);
            Assert.True(sock.GetSendBufferSize() > 0);
        }

        [Fact]
        public void TcpSocket_hop_limit_round_trips()
        {
            using var sock = new TcpSocket(IpAddressFamily.Ipv4);
            sock.SetHopLimit(64);
            Assert.Equal(64, sock.GetHopLimit());
        }

        [Fact]
        public void TcpSocket_keep_alive_enabled_round_trips()
        {
            using var sock = new TcpSocket(IpAddressFamily.Ipv4);
            sock.SetKeepAliveEnabled(true);
            Assert.True(sock.GetKeepAliveEnabled());
            sock.SetKeepAliveEnabled(false);
            Assert.False(sock.GetKeepAliveEnabled());
        }

        // ConnectAsync is now implemented (Slice Q). The host-
        // function wire-up that lowers the ip-socket-address
        // variant argument is still pending; the impl itself
        // is exercised end-to-end through SendReceive tests.

        // ---- Wire-up integration ------------------------------------

        [Fact]
        public void BindToRuntime_registers_tcp_socket_simple_methods()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            string m = WasiPreview3Host.SocketsTypesModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[static]tcp-socket.create"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.get-address-family"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.get-is-listening"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.get-receive-buffer-size"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.set-receive-buffer-size"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.get-send-buffer-size"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.set-send-buffer-size"), out _));
        }

        [Fact]
        public void InvokeTcpSocketCreate_ipv4_returns_fresh_handle()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            const int retptr = 64;

            host.InvokeTcpSocketCreate(
                IpAddressFamily.Ipv4, retptr,
                new StubRealloc(Array.Empty<int>()));

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            int handle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.True(handle > 0);
            var sock = host.TcpSocketHandles.Get(handle);
            Assert.NotNull(sock);
            Assert.Equal(IpAddressFamily.Ipv4, sock!.GetAddressFamily());
        }

        [Fact]
        public void InvokeTcpSocketGetBufferSize_writes_result_at_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new TcpSocket(IpAddressFamily.Ipv4);
            sock.SetReceiveBufferSize(8192);
            var handle = host.TcpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeTcpSocketGetBufferSize(
                handle, retptr, new StubRealloc(Array.Empty<int>()),
                sendBuffer: false);

            // result-disc = 0 (ok) at +0
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // u64 value at +8 (payload offset 8 for align-8 result)
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 8, 8));
            Assert.True(value > 0);
        }

        [Fact]
        public void InvokeTcpSocketSetBufferSize_writes_disc_zero_on_success()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new TcpSocket(IpAddressFamily.Ipv4);
            var handle = host.TcpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeTcpSocketSetBufferSize(
                handle, value: 65536, retptr,
                new StubRealloc(Array.Empty<int>()),
                sendBuffer: true);

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.True(sock.GetSendBufferSize() > 0);
        }

        [Fact]
        public void InvokeTcpSocketCreate_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeTcpSocketCreate(
                    IpAddressFamily.Ipv4, /* misaligned */ 5,
                    new StubRealloc(Array.Empty<int>())));
            Assert.Contains("misaligned", ex.Message);
        }
    }
}
