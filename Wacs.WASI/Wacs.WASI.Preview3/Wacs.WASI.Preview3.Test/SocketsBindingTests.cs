// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
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
    /// Phase 5 Slice J coverage: socket resource tables +
    /// ip-name-lookup wire-up. The TCP/UDP method wire-ups
    /// ship in a follow-up slice alongside System.Net.Sockets-
    /// backed default impls.
    /// </summary>
    public class SocketsBindingTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        [Fact]
        public void BindToRuntime_registers_socket_drops_and_name_lookup()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            string m = WasiPreview3Host.SocketsTypesModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[resource-drop]tcp-socket"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[resource-drop]udp-socket"), out _));

            string l = WasiPreview3Host.IpNameLookupModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (l, "resolve-addresses"), out _));
        }

        [Fact]
        public void TcpSocketHandles_table_round_trips()
        {
            var host = new WasiPreview3Host();
            var stub = new StubTcpSocket();
            var handle = host.TcpSocketHandles.Allocate(stub);
            Assert.True(handle > 0);
            Assert.Same(stub, host.TcpSocketHandles.Get(handle));
            host.TcpSocketHandles.Drop(handle);
            Assert.Null(host.TcpSocketHandles.Get(handle));
        }

        [Fact]
        public void UdpSocketHandles_table_round_trips()
        {
            var host = new WasiPreview3Host();
            var stub = new StubUdpSocket();
            var handle = host.UdpSocketHandles.Allocate(stub);
            Assert.True(handle > 0);
            Assert.Same(stub, host.UdpSocketHandles.Get(handle));
            host.UdpSocketHandles.Drop(handle);
            Assert.Null(host.UdpSocketHandles.Get(handle));
        }

        // ---- ip-name-lookup wire-up ----------------------------------

        [Fact]
        public void InvokeResolveAddresses_empty_result_writes_ok_disc_zero_list()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    IpNameLookup = new StubNameLookup(
                        new List<IpAddress>()),
                })
            {
                Dispatcher = dispatcher,
            };
            var realloc = new StubRealloc(Array.Empty<int>());

            const int retptr = 64;
            host.InvokeResolveAddresses(0, 0, retptr, realloc);

            // 16-byte result<list<ip-address>, error-code> retptr.
            // disc=0 (ok) at +0; list-ptr/len at +4..12.
            var bytes = dispatcher.Memory.AsSpan(retptr, 12);
            Assert.Equal(0, bytes[0]);
            Assert.Equal(0,
                BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4)));
            Assert.Equal(0,
                BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8)));
        }

        [Fact]
        public void InvokeResolveAddresses_ipv4_addresses_write_canon_variant_layout()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    IpNameLookup = new StubNameLookup(new List<IpAddress>
                    {
                        IpAddress.Ipv4(new Ipv4Address(10, 0, 0, 1)),
                        IpAddress.Ipv4(new Ipv4Address(172, 16, 0, 1)),
                    }),
                })
            {
                Dispatcher = dispatcher,
            };
            var realloc = new StubRealloc(new[] { 256 });

            const int retptr = 64;
            // Stage the hostname in memory.
            var name = Encoding.UTF8.GetBytes("dummy");
            const int namePtr = 128;
            name.CopyTo(dispatcher.Memory.AsSpan(namePtr, name.Length));

            host.InvokeResolveAddresses(
                namePtr, name.Length, retptr, realloc);

            // disc=0 (ok) at +0; list-ptr/len at +4..12 within
            // the 16-byte result retptr.
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            var outer = dispatcher.Memory.AsSpan(retptr + 4, 8);
            int listPtr = BinaryPrimitives.ReadInt32LittleEndian(outer.Slice(0));
            int listCount = BinaryPrimitives.ReadInt32LittleEndian(outer.Slice(4));
            Assert.Equal(256, listPtr); // first realloc allocation
            Assert.Equal(2, listCount);

            // Entry 0: ipv4 10.0.0.1
            var entry0 = dispatcher.Memory.AsSpan(listPtr + 0, 18);
            Assert.Equal(0, entry0[0]); // disc = ipv4
            Assert.Equal(10, entry0[2]);
            Assert.Equal(0, entry0[3]);
            Assert.Equal(0, entry0[4]);
            Assert.Equal(1, entry0[5]);

            // Entry 1: ipv4 172.16.0.1
            var entry1 = dispatcher.Memory.AsSpan(listPtr + 18, 18);
            Assert.Equal(0, entry1[0]);
            Assert.Equal(172, entry1[2]);
            Assert.Equal(16, entry1[3]);
            Assert.Equal(0, entry1[4]);
            Assert.Equal(1, entry1[5]);
        }

        [Fact]
        public void InvokeResolveAddresses_ipv6_address_writes_be_u16_groups()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    IpNameLookup = new StubNameLookup(new List<IpAddress>
                    {
                        IpAddress.Ipv6(new Ipv6Address(
                            0x2001, 0x0db8, 0, 0, 0, 0, 0, 1)),
                    }),
                })
            {
                Dispatcher = dispatcher,
            };
            var realloc = new StubRealloc(new[] { 256 });

            const int retptr = 64;
            host.InvokeResolveAddresses(0, 0, retptr, realloc);

            var entry = dispatcher.Memory.AsSpan(256, 18);
            Assert.Equal(1, entry[0]); // disc = ipv6
            // BE u16 0x2001 at offset +2..4
            Assert.Equal(0x20, entry[2]);
            Assert.Equal(0x01, entry[3]);
            // BE u16 0x0db8 at offset +4..6
            Assert.Equal(0x0d, entry[4]);
            Assert.Equal(0xb8, entry[5]);
            // Final u16 = 0x0001 at offset +16..18
            Assert.Equal(0x00, entry[16]);
            Assert.Equal(0x01, entry[17]);
        }

        [Fact]
        public void InvokeResolveAddresses_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    IpNameLookup = new StubNameLookup(new List<IpAddress>()),
                })
            {
                Dispatcher = dispatcher,
            };
            var realloc = new StubRealloc(Array.Empty<int>());

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeResolveAddresses(0, 0, 5, realloc));
            Assert.Contains("misaligned", ex.Message);
        }

        [Fact]
        public void InvokeResolveAddresses_writes_error_code_on_resolver_failure()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    // The default NoNameLookup refuses with
                    // PermanentResolverFailure.
                    IpNameLookup = new NoNameLookup(),
                })
            {
                Dispatcher = dispatcher,
            };
            var realloc = new StubRealloc(Array.Empty<int>());

            const int retptr = 64;
            host.InvokeResolveAddresses(0, 0, retptr, realloc);

            // disc=1 (err) at +0; error-code variant at +4..16.
            // Per the Slice KK disc-map fix, this uses the
            // wasi:sockets/ip-name-lookup.error-code variant
            // (6 arms), not sockets/types — so
            // PermanentResolverFailure lands at wire disc 4,
            // not the C# enum's value 16.
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(4, dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        // ---- Test stubs ----------------------------------------------

        private sealed class StubNameLookup : IIpNameLookup
        {
            private readonly IReadOnlyList<IpAddress> _result;
            public StubNameLookup(IReadOnlyList<IpAddress> result)
            {
                _result = result;
            }
            public Task<IReadOnlyList<IpAddress>> ResolveAddressesAsync(
                string name, CancellationToken cancellationToken = default)
                => Task.FromResult(_result);
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
                        "StubRealloc out of allocations.");
                return _allocations[_next++];
            }
        }

        private sealed class StubTcpSocket : ITcpSocket
        {
            public void Bind(IpSocketAddress localAddress) { }
            public Task ConnectAsync(
                IpSocketAddress remoteAddress,
                CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
            public int Listen(AsyncDispatcher dispatcher) => 0;
            public (int futureHandle, Task SendCompletion) Send(
                AsyncDispatcher dispatcher, int streamHandle) =>
                (0, Task.CompletedTask);
            public (int streamHandle, int futureHandle, Task ReceiveCompletion)
                Receive(AsyncDispatcher dispatcher) =>
                (0, 0, Task.CompletedTask);
            public IpSocketAddress GetLocalAddress() => default;
            public IpSocketAddress GetRemoteAddress() => default;
            public bool GetIsListening() => false;
            public IpAddressFamily GetAddressFamily() => IpAddressFamily.Ipv4;
            public void SetListenBacklogSize(ulong value) { }
            public bool GetKeepAliveEnabled() => false;
            public void SetKeepAliveEnabled(bool value) { }
            public ulong GetKeepAliveIdleTime() => 0;
            public void SetKeepAliveIdleTime(ulong nanoseconds) { }
            public ulong GetKeepAliveInterval() => 0;
            public void SetKeepAliveInterval(ulong nanoseconds) { }
            public uint GetKeepAliveCount() => 0;
            public void SetKeepAliveCount(uint count) { }
            public byte GetHopLimit() => 0;
            public void SetHopLimit(byte value) { }
            public ulong GetReceiveBufferSize() => 0;
            public void SetReceiveBufferSize(ulong value) { }
            public ulong GetSendBufferSize() => 0;
            public void SetSendBufferSize(ulong value) { }
        }

        private sealed class StubUdpSocket : IUdpSocket
        {
            public void Bind(IpSocketAddress localAddress) { }
            public void Connect(IpSocketAddress remoteAddress) { }
            public void Disconnect() { }
            public Task SendAsync(
                byte[] data, IpSocketAddress? remoteAddress = null,
                CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
            public Task<(byte[] data, IpSocketAddress remoteAddress)>
                ReceiveAsync(
                    CancellationToken cancellationToken = default) =>
                Task.FromResult((Array.Empty<byte>(), default(IpSocketAddress)));
            public IpSocketAddress GetLocalAddress() => default;
            public IpSocketAddress GetRemoteAddress() => default;
            public IpAddressFamily GetAddressFamily() => IpAddressFamily.Ipv4;
            public byte GetUnicastHopLimit() => 0;
            public void SetUnicastHopLimit(byte value) { }
            public ulong GetReceiveBufferSize() => 0;
            public void SetReceiveBufferSize(ulong value) { }
            public ulong GetSendBufferSize() => 0;
            public void SetSendBufferSize(ulong value) { }
        }
    }
}
