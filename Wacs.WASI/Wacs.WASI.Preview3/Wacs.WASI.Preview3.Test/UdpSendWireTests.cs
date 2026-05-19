// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
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
    /// Phase 5 Slice FF coverage: udp-socket.send. Flat-lowered
    /// wire signature totals 17 i32 slots (self + list<u8> ptr/len
    /// + option<ip-socket-address> 13 slots + retArea), reached
    /// via a custom UdpSendDelegate to clear System.Action's
    /// 16-arg limit.
    /// </summary>
    public class UdpSendWireTests
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

        // Stub IUdpSocket that records the SendAsync arguments
        // — bypasses System.Net.Sockets so we can verify the
        // wire decoder against deterministic inputs.
        private sealed class RecordingUdpSocket : IUdpSocket
        {
            public byte[]? LastData;
            public IpSocketAddress? LastRemote;
            public SocketsException? Throw;

            public Task SendAsync(
                byte[] data,
                IpSocketAddress? remoteAddress = null,
                CancellationToken cancellationToken = default)
            {
                if (Throw != null) throw Throw;
                LastData = data;
                LastRemote = remoteAddress;
                return Task.CompletedTask;
            }

            public Task<(byte[] data, IpSocketAddress remoteAddress)>
                ReceiveAsync(CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public void Bind(IpSocketAddress localAddress) { }
            public void Connect(IpSocketAddress remoteAddress) { }
            public void Disconnect() { }
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
        public void BindToRuntime_registers_udp_send()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.SocketsTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]udp-socket.send"), out var addr));
            // Verify the wire arity: 17 i32 params, 0 results.
            // (self + dataPtr + dataLen + optDisc + addrDisc +
            //  s1..s11 + retptr = 17 params; result<_, error-code>
            //  is hoisted via retArea, so 0 flat results.)
            var ft = runtime.GetFunctionType(addr);
            Assert.Equal(17, ft.ParameterTypes.Types.Length);
            Assert.Empty(ft.ResultType.Types);
        }

        [Fact]
        public void InvokeUdpSocketSend_with_none_remote_uses_connected_peer()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var stub = new RecordingUdpSocket();
            int handle = host.UdpSocketHandles.Allocate(stub);
            var realloc = new SequentialRealloc(1024);

            const int dataPtr = 64;
            byte[] payload = new byte[] { 0x11, 0x22, 0x33 };
            payload.CopyTo(dispatcher.Memory.AsSpan(dataPtr, payload.Length));
            const int retptr = 128;

            host.InvokeUdpSocketSend(
                handle,
                dataPtr: dataPtr, dataLen: payload.Length,
                optDisc: 0, addrDisc: 0,
                s1: 0, s2: 0, s3: 0, s4: 0, s5: 0,
                s6: 0, s7: 0, s8: 0, s9: 0, s10: 0, s11: 0,
                retptr: retptr, realloc: realloc);

            // result-disc = 0 (ok) at +0.
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // Stub received the data with no remote.
            Assert.Equal(payload, stub.LastData);
            Assert.Null(stub.LastRemote);
        }

        [Fact]
        public void InvokeUdpSocketSend_with_ipv4_remote_decodes_address()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var stub = new RecordingUdpSocket();
            int handle = host.UdpSocketHandles.Allocate(stub);
            var realloc = new SequentialRealloc(1024);

            const int dataPtr = 64;
            byte[] payload = new byte[] { 0xAA };
            payload.CopyTo(dispatcher.Memory.AsSpan(dataPtr, 1));
            const int retptr = 128;

            // ipv4 192.168.1.1:5353 via flat slots:
            //   addrDisc = 0
            //   s1 = port (5353), s2..s5 = octets
            host.InvokeUdpSocketSend(
                handle,
                dataPtr: dataPtr, dataLen: 1,
                optDisc: 1,            // some
                addrDisc: 0,           // ipv4
                s1: 5353,
                s2: 192, s3: 168, s4: 1, s5: 1,
                s6: 0, s7: 0, s8: 0, s9: 0, s10: 0, s11: 0,
                retptr: retptr, realloc: realloc);

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.NotNull(stub.LastRemote);
            Assert.Equal(IpAddressFamily.Ipv4, stub.LastRemote!.Value.Family);
            Assert.Equal(5353, stub.LastRemote.Value.V4.Port);
            Assert.Equal(192, stub.LastRemote.Value.V4.Address.A);
            Assert.Equal(168, stub.LastRemote.Value.V4.Address.B);
            Assert.Equal(1, stub.LastRemote.Value.V4.Address.C);
            Assert.Equal(1, stub.LastRemote.Value.V4.Address.D);
        }

        [Fact]
        public void InvokeUdpSocketSend_with_ipv6_remote_decodes_groups_and_scope()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var stub = new RecordingUdpSocket();
            int handle = host.UdpSocketHandles.Allocate(stub);
            var realloc = new SequentialRealloc(1024);

            const int dataPtr = 64;
            byte[] payload = new byte[] { 1, 2, 3, 4 };
            payload.CopyTo(dispatcher.Memory.AsSpan(dataPtr, payload.Length));
            const int retptr = 128;

            // ipv6 ::1, port 0xABCD, flow-info 0xCAFEBABE, scope-id 7
            host.InvokeUdpSocketSend(
                handle,
                dataPtr: dataPtr, dataLen: payload.Length,
                optDisc: 1,
                addrDisc: 1,            // ipv6
                s1: 0xABCD,
                s2: unchecked((int)0xCAFEBABE),
                s3: 0, s4: 0, s5: 0, s6: 0,
                s7: 0, s8: 0, s9: 0, s10: 1,
                s11: 7,
                retptr: retptr, realloc: realloc);

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.NotNull(stub.LastRemote);
            Assert.Equal(IpAddressFamily.Ipv6, stub.LastRemote!.Value.Family);
            Assert.Equal(0xABCD, stub.LastRemote.Value.V6.Port);
            Assert.Equal(0xCAFEBABEu, stub.LastRemote.Value.V6.FlowInfo);
            Assert.Equal(1, stub.LastRemote.Value.V6.Address.G7);
            Assert.Equal(7u, stub.LastRemote.Value.V6.ScopeId);
        }

        [Fact]
        public void InvokeUdpSocketSend_send_failure_writes_error_code()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var stub = new RecordingUdpSocket
            {
                Throw = new SocketsException(
                    Sockets.ErrorCode.AddressNotBindable,
                    "no route"),
            };
            int handle = host.UdpSocketHandles.Allocate(stub);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeUdpSocketSend(
                handle,
                dataPtr: 0, dataLen: 0,
                optDisc: 0, addrDisc: 0,
                s1: 0, s2: 0, s3: 0, s4: 0, s5: 0,
                s6: 0, s7: 0, s8: 0, s9: 0, s10: 0, s11: 0,
                retptr: retptr, realloc: realloc);

            // disc=1 (err) at +0; error-code at +4.
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Sockets.ErrorCode.AddressNotBindable,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }
    }
}
