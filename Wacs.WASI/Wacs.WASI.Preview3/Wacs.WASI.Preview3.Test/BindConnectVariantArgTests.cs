// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
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
    /// Phase 5 Slice T coverage: <c>ip-socket-address</c>
    /// variant-arg flat-lowering for bind/connect. Validates
    /// the 12-slot decode (disc + 11 joined payload) and the
    /// full 14-param wire bindings for both tcp + udp.
    /// </summary>
    public class BindConnectVariantArgTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        private sealed class StubRealloc : ICabiRealloc
        {
            public int Allocate(int align, int size) =>
                throw new InvalidOperationException("unused");
        }

        // ---- ReadIpSocketAddressFromSlots (pure decode) -------------

        [Fact]
        public void Decode_ipv4_uses_first_5_slots()
        {
            var addr = WasiPreview3Host.ReadIpSocketAddressFromSlots(
                disc: 0, s1: 0x1F90, /* port 8080 */
                s2: 127, s3: 0, s4: 0, s5: 1,
                s6: 0, s7: 0, s8: 0, s9: 0, s10: 0, s11: 0);
            Assert.Equal(IpAddressFamily.Ipv4, addr.Family);
            Assert.Equal((ushort)8080, addr.V4.Port);
            Assert.Equal(Ipv4Address.Loopback, addr.V4.Address);
        }

        [Fact]
        public void Decode_ipv6_uses_all_11_payload_slots()
        {
            var addr = WasiPreview3Host.ReadIpSocketAddressFromSlots(
                disc: 1,
                s1: 0x01BB, // port 443
                s2: unchecked((int)0xDEADBEEF), // flow-info
                s3: 0x2001, s4: 0x0DB8,
                s5: 0, s6: 0, s7: 0, s8: 0, s9: 0, s10: 1,
                s11: 0x07);
            Assert.Equal(IpAddressFamily.Ipv6, addr.Family);
            Assert.Equal((ushort)443, addr.V6.Port);
            Assert.Equal(0xDEADBEEFu, addr.V6.FlowInfo);
            Assert.Equal(0x2001, addr.V6.Address.G0);
            Assert.Equal(0x0DB8, addr.V6.Address.G1);
            Assert.Equal(1, addr.V6.Address.G7);
            Assert.Equal(7u, addr.V6.ScopeId);
        }

        [Fact]
        public void Decode_invalid_disc_throws_invalid_argument()
        {
            var ex = Assert.Throws<SocketsException>(() =>
                WasiPreview3Host.ReadIpSocketAddressFromSlots(
                    disc: 99,
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
            Assert.Equal(
                Sockets.ErrorCode.InvalidArgument, ex.Code);
        }

        // ---- Wire registrations --------------------------------------

        [Fact]
        public void BindToRuntime_registers_bind_and_connect()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            string m = WasiPreview3Host.SocketsTypesModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.bind"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.connect"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.bind"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.connect"), out _));
        }

        // ---- TCP bind via the host delegate body --------------------

        [Fact]
        public void InvokeTcpSocketBind_loopback_ipv4_succeeds()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new TcpSocket(IpAddressFamily.Ipv4);
            var handle = host.TcpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeTcpSocketBind(
                handle,
                IpSocketAddress.Ipv4(
                    new Ipv4SocketAddress(0, Ipv4Address.Loopback)),
                retptr, new StubRealloc());

            // result-disc = 0 (ok)
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // Side-effect: the socket is in Bound state with a
            // resolved local endpoint.
            Assert.Equal(TcpSocket.State.Bound, sock.CurrentState);
            var local = sock.GetLocalAddress();
            Assert.Equal(Ipv4Address.Loopback, local.V4.Address);
            Assert.True(local.V4.Port > 0);
            sock.Dispose();
        }

        [Fact]
        public void InvokeTcpSocketBind_already_bound_writes_invalid_state()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new TcpSocket(IpAddressFamily.Ipv4);
            sock.Bind(IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(0, Ipv4Address.Loopback)));
            var handle = host.TcpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeTcpSocketBind(
                handle,
                IpSocketAddress.Ipv4(
                    new Ipv4SocketAddress(0, Ipv4Address.Loopback)),
                retptr, new StubRealloc());

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Sockets.ErrorCode.InvalidState,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
            sock.Dispose();
        }

        // ---- TCP connect via the host delegate body -----------------

        [Fact]
        public async Task InvokeTcpSocketConnect_loopback_ipv4_succeeds()
        {
            // Spin up a real .NET listener so the client's
            // connect actually completes.
            using var listener = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
            var acceptTask = listener.AcceptAsync();

            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var client = new TcpSocket(IpAddressFamily.Ipv4);
            var handle = host.TcpSocketHandles.Allocate(client);

            const int retptr = 64;
            host.InvokeTcpSocketConnect(
                handle,
                IpSocketAddress.Ipv4(new Ipv4SocketAddress(
                    (ushort)port, Ipv4Address.Loopback)),
                retptr, new StubRealloc());

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(TcpSocket.State.Connected, client.CurrentState);

            using var accepted = await acceptTask;
            Assert.NotNull(accepted);
            client.Dispose();
        }

        [Fact]
        public void InvokeTcpSocketConnect_no_listener_writes_connection_refused()
        {
            // Bind a port, then immediately close it — connecting
            // to it should map to ConnectionRefused (or similar).
            var probe = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var port = ((IPEndPoint)probe.LocalEndPoint!).Port;
            probe.Close();

            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var client = new TcpSocket(IpAddressFamily.Ipv4);
            var handle = host.TcpSocketHandles.Allocate(client);

            const int retptr = 64;
            host.InvokeTcpSocketConnect(
                handle,
                IpSocketAddress.Ipv4(new Ipv4SocketAddress(
                    (ushort)port, Ipv4Address.Loopback)),
                retptr, new StubRealloc());

            // disc = 1 (err); the exact code may be
            // ConnectionRefused on most platforms, but reset /
            // unreachable are also plausible — accept the family.
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            var errCode = (Sockets.ErrorCode)
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0];
            Assert.Contains(errCode, new[]
            {
                Sockets.ErrorCode.ConnectionRefused,
                Sockets.ErrorCode.ConnectionReset,
                Sockets.ErrorCode.RemoteUnreachable,
                Sockets.ErrorCode.Other,
            });
            client.Dispose();
        }

        // ---- UDP bind / connect -------------------------------------

        [Fact]
        public void InvokeUdpSocketBind_loopback_ipv4_succeeds()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new UdpSocket(IpAddressFamily.Ipv4);
            var handle = host.UdpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeUdpSocketBind(
                handle,
                IpSocketAddress.Ipv4(
                    new Ipv4SocketAddress(0, Ipv4Address.Loopback)),
                retptr, new StubRealloc());

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(UdpSocket.State.Bound, sock.CurrentState);
            sock.Dispose();
        }

        [Fact]
        public void InvokeUdpSocketConnect_after_bind_succeeds()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new UdpSocket(IpAddressFamily.Ipv4);
            sock.Bind(IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(0, Ipv4Address.Loopback)));
            var handle = host.UdpSocketHandles.Allocate(sock);

            const int retptr = 64;
            // Connect to any loopback port; UDP "connect" just
            // sets the default peer — no handshake or listener
            // required.
            host.InvokeUdpSocketConnect(
                handle,
                IpSocketAddress.Ipv4(new Ipv4SocketAddress(
                    9999, Ipv4Address.Loopback)),
                retptr, new StubRealloc());

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(UdpSocket.State.Connected, sock.CurrentState);
            sock.Dispose();
        }

        [Fact]
        public void InvokeUdpSocketConnect_unbound_writes_invalid_state()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new UdpSocket(IpAddressFamily.Ipv4);
            var handle = host.UdpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeUdpSocketConnect(
                handle,
                IpSocketAddress.Ipv4(new Ipv4SocketAddress(
                    9999, Ipv4Address.Loopback)),
                retptr, new StubRealloc());

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Sockets.ErrorCode.InvalidState,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
            sock.Dispose();
        }
    }
}
