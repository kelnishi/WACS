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
    /// Phase 5 Slice P coverage: <see cref="UdpSocket"/>
    /// System.Net.Sockets-backed default + the static
    /// `create` + simple getter/setter wire-up.
    /// </summary>
    public class UdpSocketBindingTests
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
                    throw new InvalidOperationException("out");
                return _allocations[_next++];
            }
        }

        [Fact]
        public void UdpSocket_starts_unbound_with_correct_family()
        {
            using var sock = new UdpSocket(IpAddressFamily.Ipv4);
            Assert.Equal(IpAddressFamily.Ipv4, sock.GetAddressFamily());
            Assert.Equal(UdpSocket.State.Unbound, sock.CurrentState);
        }

        [Fact]
        public void UdpSocket_bind_loopback_transitions_to_bound()
        {
            using var sock = new UdpSocket(IpAddressFamily.Ipv4);
            sock.Bind(IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(0, Ipv4Address.Loopback)));
            Assert.Equal(UdpSocket.State.Bound, sock.CurrentState);
            var local = sock.GetLocalAddress();
            Assert.Equal(Ipv4Address.Loopback, local.V4.Address);
            Assert.True(local.V4.Port > 0);
        }

        [Fact]
        public void UdpSocket_buffer_size_round_trips()
        {
            using var sock = new UdpSocket(IpAddressFamily.Ipv4);
            sock.SetSendBufferSize(4096);
            sock.SetReceiveBufferSize(8192);
            Assert.True(sock.GetSendBufferSize() > 0);
            Assert.True(sock.GetReceiveBufferSize() > 0);
        }

        [Fact]
        public void UdpSocket_hop_limit_round_trips()
        {
            using var sock = new UdpSocket(IpAddressFamily.Ipv4);
            sock.SetUnicastHopLimit(32);
            Assert.Equal(32, sock.GetUnicastHopLimit());
        }

        [Fact]
        public void BindToRuntime_registers_udp_socket_simple_methods()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.SocketsTypesModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[static]udp-socket.create"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.get-address-family"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.get-unicast-hop-limit"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.set-unicast-hop-limit"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.get-receive-buffer-size"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.set-receive-buffer-size"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.get-send-buffer-size"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.set-send-buffer-size"), out _));
        }

        [Fact]
        public void InvokeUdpSocketCreate_ipv4_returns_fresh_handle()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            const int retptr = 64;
            host.InvokeUdpSocketCreate(
                IpAddressFamily.Ipv4, retptr,
                new StubRealloc(Array.Empty<int>()));
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            int handle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.True(handle > 0);
            Assert.NotNull(host.UdpSocketHandles.Get(handle));
        }

        [Fact]
        public void InvokeUdpSocketGetHopLimit_writes_result_at_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new UdpSocket(IpAddressFamily.Ipv4);
            sock.SetUnicastHopLimit(48);
            var handle = host.UdpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeUdpSocketGetHopLimit(
                handle, retptr, new StubRealloc(Array.Empty<int>()));

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(48, dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        [Fact]
        public void InvokeUdpSocketGetBufferSize_writes_result_at_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new UdpSocket(IpAddressFamily.Ipv4);
            sock.SetReceiveBufferSize(4096);
            var handle = host.UdpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeUdpSocketGetBufferSize(
                handle, retptr, new StubRealloc(Array.Empty<int>()),
                sendBuffer: false);

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 8, 8));
            Assert.True(value > 0);
        }
    }
}
