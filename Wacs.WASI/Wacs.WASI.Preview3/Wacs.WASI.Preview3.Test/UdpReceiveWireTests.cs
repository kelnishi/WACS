// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading;
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
    /// Phase 5 Slice Y coverage: udp-socket.receive wire-up.
    /// 44-byte 4-aligned retptr for
    /// <c>result&lt;tuple&lt;list&lt;u8&gt;,
    /// ip-socket-address&gt;, error-code&gt;</c>. Verifies the
    /// list-payload encoding through ICabiRealloc and the
    /// ip-socket-address-at-offset-12 layout (decoupled from the
    /// standalone result&lt;ip-socket-address, error-code&gt;
    /// encoder from Slice U).
    /// </summary>
    public class UdpReceiveWireTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        private sealed class SequentialRealloc : ICabiRealloc
        {
            private int _next;
            public SequentialRealloc(int baseAddr) { _next = baseAddr; }
            public int Allocate(int align, int size)
            {
                _next = (_next + (align - 1)) & ~(align - 1);
                int ptr = _next;
                _next += size;
                return ptr;
            }
        }

        // Stub IUdpSocket that returns a fixed (data, source) tuple
        // from ReceiveAsync — bypasses System.Net.Sockets so we
        // can drive the wire encoder independently of host network
        // state.
        private sealed class StubReceiveUdpSocket : IUdpSocket
        {
            private readonly byte[] _data;
            private readonly IpSocketAddress _source;
            public StubReceiveUdpSocket(byte[] data, IpSocketAddress source)
            {
                _data = data; _source = source;
            }

            public Task<(byte[] data, IpSocketAddress remoteAddress)>
                ReceiveAsync(CancellationToken cancellationToken = default)
                => Task.FromResult((_data, _source));

            public void Bind(IpSocketAddress localAddress)
                => throw new NotImplementedException();
            public void Connect(IpSocketAddress remoteAddress)
                => throw new NotImplementedException();
            public void Disconnect() => throw new NotImplementedException();
            public Task SendAsync(byte[] data,
                IpSocketAddress? remoteAddress = null,
                CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public IpSocketAddress GetLocalAddress()
                => throw new NotImplementedException();
            public IpSocketAddress GetRemoteAddress()
                => throw new NotImplementedException();
            public IpAddressFamily GetAddressFamily()
                => _source.Family;
            public byte GetUnicastHopLimit() => 64;
            public void SetUnicastHopLimit(byte value) { }
            public ulong GetReceiveBufferSize() => 0;
            public void SetReceiveBufferSize(ulong value) { }
            public ulong GetSendBufferSize() => 0;
            public void SetSendBufferSize(ulong value) { }
        }

        // Stub that throws — exercises the err path through the
        // 44-byte retptr's error-code variant section.
        private sealed class ThrowingReceiveUdpSocket : IUdpSocket
        {
            public Task<(byte[] data, IpSocketAddress remoteAddress)>
                ReceiveAsync(CancellationToken cancellationToken = default)
                => Task.FromException<(byte[], IpSocketAddress)>(
                    new SocketsException(
                        Sockets.ErrorCode.Timeout,
                        "no datagram available"));

            public void Bind(IpSocketAddress localAddress)
                => throw new NotImplementedException();
            public void Connect(IpSocketAddress remoteAddress)
                => throw new NotImplementedException();
            public void Disconnect() => throw new NotImplementedException();
            public Task SendAsync(byte[] data,
                IpSocketAddress? remoteAddress = null,
                CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public IpSocketAddress GetLocalAddress()
                => throw new NotImplementedException();
            public IpSocketAddress GetRemoteAddress()
                => throw new NotImplementedException();
            public IpAddressFamily GetAddressFamily()
                => IpAddressFamily.Ipv4;
            public byte GetUnicastHopLimit() => 64;
            public void SetUnicastHopLimit(byte value) { }
            public ulong GetReceiveBufferSize() => 0;
            public void SetReceiveBufferSize(ulong value) { }
            public ulong GetSendBufferSize() => 0;
            public void SetSendBufferSize(ulong value) { }
        }

        [Fact]
        public void BindToRuntime_registers_udp_receive()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.SocketsTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.receive"), out _));
        }

        [Fact]
        public void InvokeUdpSocketReceive_ok_ipv4_encodes_data_and_source()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            byte[] payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x42 };
            var source = IpSocketAddress.Ipv4(new Ipv4SocketAddress(
                port: 0x1234,
                address: new Ipv4Address(192, 168, 1, 1)));
            var stub = new StubReceiveUdpSocket(payload, source);
            int handle = host.UdpSocketHandles.Allocate(stub);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeUdpSocketReceive(handle, retptr, realloc);

            var m = dispatcher.Memory;
            // result-disc = 0 (ok)
            Assert.Equal(0, m.AsSpan(retptr, 1)[0]);
            // list-ptr / list-len at +4..12
            int listPtr = BinaryPrimitives.ReadInt32LittleEndian(
                m.AsSpan(retptr + 4, 4));
            int listLen = BinaryPrimitives.ReadInt32LittleEndian(
                m.AsSpan(retptr + 8, 4));
            Assert.Equal(payload.Length, listLen);
            byte[] decoded = new byte[listLen];
            m.AsSpan(listPtr, listLen).CopyTo(decoded);
            Assert.Equal(payload, decoded);

            // ip-socket-address at +12..44 — variant disc + ipv4
            //  +12: variant disc = 0 (ipv4)
            //  +16..18: port LE
            //  +18..22: 4 octets
            Assert.Equal(0, m.AsSpan(retptr + 12, 1)[0]);
            Assert.Equal(0x1234,
                (m.AsSpan(retptr + 16, 1)[0])
                | (m.AsSpan(retptr + 17, 1)[0] << 8));
            Assert.Equal(192, m.AsSpan(retptr + 18, 1)[0]);
            Assert.Equal(168, m.AsSpan(retptr + 19, 1)[0]);
            Assert.Equal(1, m.AsSpan(retptr + 20, 1)[0]);
            Assert.Equal(1, m.AsSpan(retptr + 21, 1)[0]);
        }

        [Fact]
        public void InvokeUdpSocketReceive_ok_ipv6_encodes_be_groups_and_scope()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            byte[] payload = new byte[] { 1, 2, 3 };
            // ::1 with flow-info 0xCAFEBABE and scope-id 42
            var source = IpSocketAddress.Ipv6(new Ipv6SocketAddress(
                port: 0x8888,
                flowInfo: 0xCAFEBABE,
                address: new Ipv6Address(
                    0, 0, 0, 0, 0, 0, 0, 1),
                scopeId: 42));
            var stub = new StubReceiveUdpSocket(payload, source);
            int handle = host.UdpSocketHandles.Allocate(stub);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeUdpSocketReceive(handle, retptr, realloc);

            var m = dispatcher.Memory;
            Assert.Equal(0, m.AsSpan(retptr, 1)[0]);
            // ip-sock-addr disc = 1 (ipv6)
            Assert.Equal(1, m.AsSpan(retptr + 12, 1)[0]);
            // port LE at +16..18
            Assert.Equal(0x8888,
                (m.AsSpan(retptr + 16, 1)[0])
                | (m.AsSpan(retptr + 17, 1)[0] << 8));
            // flow-info LE at +20..24
            uint flow = BinaryPrimitives.ReadUInt32LittleEndian(
                m.AsSpan(retptr + 20, 4));
            Assert.Equal(0xCAFEBABEu, flow);
            // BE address groups: 7 zero groups + 0x0001 in the
            // last (slot 7)
            for (int slot = 0; slot < 7; slot++)
            {
                Assert.Equal(0, m.AsSpan(retptr + 24 + slot * 2, 1)[0]);
                Assert.Equal(0, m.AsSpan(retptr + 25 + slot * 2, 1)[0]);
            }
            Assert.Equal(0x00, m.AsSpan(retptr + 24 + 14, 1)[0]);
            Assert.Equal(0x01, m.AsSpan(retptr + 25 + 14, 1)[0]);
            // scope-id LE at +40..44
            Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(
                m.AsSpan(retptr + 40, 4)));
        }

        [Fact]
        public void InvokeUdpSocketReceive_err_writes_error_code_at_offset_4()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var stub = new ThrowingReceiveUdpSocket();
            int handle = host.UdpSocketHandles.Allocate(stub);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeUdpSocketReceive(handle, retptr, realloc);

            // result-disc = 1 (err)
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // error-code at +4 (variant disc)
            Assert.Equal((byte)Sockets.ErrorCode.Timeout,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        [Fact]
        public void InvokeUdpSocketReceive_zero_length_payload_writes_null_ptr()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var source = IpSocketAddress.Ipv4(new Ipv4SocketAddress(
                port: 0,
                address: new Ipv4Address(0, 0, 0, 0)));
            var stub = new StubReceiveUdpSocket(
                Array.Empty<byte>(), source);
            int handle = host.UdpSocketHandles.Allocate(stub);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeUdpSocketReceive(handle, retptr, realloc);

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // Empty payload — pointer convention: 0 / len 0.
            int listPtr = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            int listLen = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 8, 4));
            Assert.Equal(0, listPtr);
            Assert.Equal(0, listLen);
        }

        [Fact]
        public void InvokeUdpSocketReceive_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var source = IpSocketAddress.Ipv4(new Ipv4SocketAddress(
                port: 0,
                address: new Ipv4Address(0, 0, 0, 0)));
            var stub = new StubReceiveUdpSocket(
                Array.Empty<byte>(), source);
            int handle = host.UdpSocketHandles.Allocate(stub);
            var realloc = new SequentialRealloc(1024);

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeUdpSocketReceive(
                    handle, /* not 4-aligned */ 13, realloc));
            Assert.Contains("misaligned", ex.Message);
        }
    }
}
