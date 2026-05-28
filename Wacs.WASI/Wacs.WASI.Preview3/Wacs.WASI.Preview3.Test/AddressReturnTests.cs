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
    /// Phase 5 Slice U coverage: the 36-byte
    /// <c>result&lt;ip-socket-address, error-code&gt;</c>
    /// retptr layout used by get-local-address /
    /// get-remote-address on both TCP and UDP. Verifies the
    /// encoder produces the canon-ABI wire format that the
    /// Slice T decoder would round-trip.
    /// </summary>
    public class AddressReturnTests
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

        // Decode the ip-socket-address from the 32-byte payload
        // section of the result. Used by tests to round-trip
        // through the encoder.
        private static IpSocketAddress DecodeFromResultPayload(
            ReadOnlySpan<byte> result, int offset = 4)
        {
            // result-disc at +0; inner variant disc at +offset
            // (+4 by default). Payload starts at +offset+4.
            byte innerDisc = result[offset];
            if (innerDisc == 0)
            {
                ushort port = (ushort)(
                    result[offset + 4] | (result[offset + 5] << 8));
                return IpSocketAddress.Ipv4(new Ipv4SocketAddress(
                    port,
                    new Ipv4Address(
                        result[offset + 6],
                        result[offset + 7],
                        result[offset + 8],
                        result[offset + 9])));
            }
            // ipv6
            ushort v6port = (ushort)(
                result[offset + 4] | (result[offset + 5] << 8));
            uint flowInfo = BinaryPrimitives.ReadUInt32LittleEndian(
                result.Slice(offset + 8, 4));
            // Inline the BE u16 reads — can't capture
            // ReadOnlySpan<byte> in a local function.
            var v6 = new Ipv6Address(
                (ushort)((result[offset + 12] << 8) | result[offset + 13]),
                (ushort)((result[offset + 14] << 8) | result[offset + 15]),
                (ushort)((result[offset + 16] << 8) | result[offset + 17]),
                (ushort)((result[offset + 18] << 8) | result[offset + 19]),
                (ushort)((result[offset + 20] << 8) | result[offset + 21]),
                (ushort)((result[offset + 22] << 8) | result[offset + 23]),
                (ushort)((result[offset + 24] << 8) | result[offset + 25]),
                (ushort)((result[offset + 26] << 8) | result[offset + 27]));
            uint scopeId = BinaryPrimitives.ReadUInt32LittleEndian(
                result.Slice(offset + 28, 4));
            return IpSocketAddress.Ipv6(new Ipv6SocketAddress(
                v6port, flowInfo, v6, scopeId));
        }

        [Fact]
        public void BindToRuntime_registers_address_getters()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            string m = WasiPreview3Host.SocketsTypesModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.get-local-address"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]tcp-socket.get-remote-address"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.get-local-address"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.get-remote-address"), out _));
        }

        [Fact]
        public void TcpSocket_get_local_address_writes_ipv4_at_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new TcpSocket(IpAddressFamily.Ipv4);
            sock.Bind(IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(0, Ipv4Address.Loopback)));
            int boundPort = sock.GetLocalAddress().V4.Port;
            var handle = host.TcpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeTcpSocketGetLocalAddress(
                handle, retptr, new StubRealloc());

            // result-disc = 0
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // ip-sock-addr disc = 0 (ipv4) at +4
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);

            // Round-trip decode.
            var bytes = new byte[36];
            dispatcher.Memory.AsSpan(retptr, 36).CopyTo(bytes);
            var decoded = DecodeFromResultPayload(bytes);
            Assert.Equal(IpAddressFamily.Ipv4, decoded.Family);
            Assert.Equal((ushort)boundPort, decoded.V4.Port);
            Assert.Equal(Ipv4Address.Loopback, decoded.V4.Address);
            sock.Dispose();
        }

        [Fact]
        public void TcpSocket_get_local_address_unbound_writes_invalid_state()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            using var sock = new TcpSocket(IpAddressFamily.Ipv4);
            var handle = host.TcpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeTcpSocketGetLocalAddress(
                handle, retptr, new StubRealloc());

            // disc = 1 (err), error-code = InvalidState
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Sockets.ErrorCode.InvalidState,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        [Fact]
        public void TcpSocket_get_remote_address_unconnected_writes_invalid_state()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new TcpSocket(IpAddressFamily.Ipv4);
            sock.Bind(IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(0, Ipv4Address.Loopback)));
            var handle = host.TcpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeTcpSocketGetRemoteAddress(
                handle, retptr, new StubRealloc());

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Sockets.ErrorCode.InvalidState,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
            sock.Dispose();
        }

        [Fact]
        public void TcpSocket_get_local_address_ipv6_writes_address_groups()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new TcpSocket(IpAddressFamily.Ipv6);
            sock.Bind(IpSocketAddress.Ipv6(
                new Ipv6SocketAddress(
                    0, 0, Ipv6Address.Loopback, 0)));
            var handle = host.TcpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeTcpSocketGetLocalAddress(
                handle, retptr, new StubRealloc());

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);

            var bytes = new byte[36];
            dispatcher.Memory.AsSpan(retptr, 36).CopyTo(bytes);
            var decoded = DecodeFromResultPayload(bytes);
            Assert.Equal(IpAddressFamily.Ipv6, decoded.Family);
            // ::1 — last group is 1, others 0.
            Assert.Equal(0, decoded.V6.Address.G0);
            Assert.Equal(1, decoded.V6.Address.G7);
            sock.Dispose();
        }

        [Fact]
        public void UdpSocket_get_local_address_after_bind_round_trips()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new UdpSocket(IpAddressFamily.Ipv4);
            sock.Bind(IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(0, Ipv4Address.Loopback)));
            int boundPort = sock.GetLocalAddress().V4.Port;
            var handle = host.UdpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeUdpSocketGetLocalAddress(
                handle, retptr, new StubRealloc());

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            var bytes = new byte[36];
            dispatcher.Memory.AsSpan(retptr, 36).CopyTo(bytes);
            var decoded = DecodeFromResultPayload(bytes);
            Assert.Equal((ushort)boundPort, decoded.V4.Port);
            sock.Dispose();
        }

        [Fact]
        public void UdpSocket_get_remote_address_unconnected_writes_invalid_state()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            using var sock = new UdpSocket(IpAddressFamily.Ipv4);
            var handle = host.UdpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeUdpSocketGetRemoteAddress(
                handle, retptr, new StubRealloc());

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Sockets.ErrorCode.InvalidState,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        [Fact]
        public void TcpSocket_bind_then_get_local_address_round_trips_through_decoder()
        {
            // Pure pipeline: TCP socket binds to a specific port,
            // the encoder writes the retptr, the Slice T decoder
            // pulls the same address back. End-to-end variant
            // encode + decode symmetry.
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var sock = new TcpSocket(IpAddressFamily.Ipv4);
            sock.Bind(IpSocketAddress.Ipv4(
                new Ipv4SocketAddress(0, Ipv4Address.Loopback)));
            var localAddr = sock.GetLocalAddress();
            var handle = host.TcpSocketHandles.Allocate(sock);

            const int retptr = 64;
            host.InvokeTcpSocketGetLocalAddress(
                handle, retptr, new StubRealloc());

            // Now read it back the way a guest would: pull the
            // ipv4 fields out and reconstruct via the Slice T
            // decoder. This proves the encode + the variant-arg
            // decode are inverse operations on the wire layout.
            var bytes = new byte[36];
            dispatcher.Memory.AsSpan(retptr, 36).CopyTo(bytes);
            // Reassemble the 12 flat slots from the variant
            // payload bytes — same layout but stored vs. flat.
            ushort port = (ushort)(bytes[8] | (bytes[9] << 8));
            byte a = bytes[10], b = bytes[11], c = bytes[12], d = bytes[13];
            var decoded = WasiPreview3Host
                .ReadIpSocketAddressFromSlots(
                    disc: bytes[4],
                    s1: port,
                    s2: a, s3: b, s4: c, s5: d,
                    s6: 0, s7: 0, s8: 0, s9: 0, s10: 0, s11: 0);
            Assert.Equal(localAddr.Family, decoded.Family);
            Assert.Equal(localAddr.V4.Port, decoded.V4.Port);
            Assert.Equal(localAddr.V4.Address, decoded.V4.Address);
            sock.Dispose();
        }

        [Fact]
        public void GetAddress_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            using var sock = new TcpSocket(IpAddressFamily.Ipv4);
            var handle = host.TcpSocketHandles.Allocate(sock);

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeTcpSocketGetLocalAddress(
                    handle, /* misaligned */ 7, new StubRealloc()));
            Assert.Contains("misaligned", ex.Message);
        }
    }
}
