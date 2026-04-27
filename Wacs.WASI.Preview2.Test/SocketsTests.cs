// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;
using Wacs.WASI.Preview2.Sockets;
using Wacs.WASI.Preview2;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    public class SocketsTests
    {
        [Fact]
        public void InstanceNetworkSource_returns_fresh_Network_each_call()
        {
            // Conservative defaults: each call hands back a new
            // Network instance. Subclasses can override to share
            // a singleton capability or tag with policy state.
            var src = new InstanceNetworkSource();
            var a = src.InstanceNetwork();
            var b = src.InstanceNetwork();
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.NotSame(a, b);
        }

        [Fact]
        public void TcpCreateSocket_returns_Ok_handle_through_canon_lower()
        {
            // Fixture: try-create(family) → calls
            // create-tcp-socket(family). On Ok, the wrapper
            // writes (disc=0, handle) at retArea. Fixture reads
            // disc, drops the handle if Ok, returns disc as u32.
            // Expected: 0 (Ok).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-tcp-create-component", "tcp.component.wasm"));
            var resources = new ResourceContext();
            var factory = new TcpCreateSocket();

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources, tcpCreate: factory)
                    .BindToRuntime(runtime));

            // Ipv4 = 0
            Assert.Equal(0u, (uint)ci.Invoke("try-create", 0u)!);
            // Table empty after drop.
            Assert.Equal(0, resources.TableFor(typeof(TcpSocket)).Count);
        }

        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        [Fact]
        public void TcpSocket_subscribe_yields_pollable_handle_address_family_returns_enum()
        {
            // Fixture: ask-family(handle) calls
            // tcp-socket.address-family(handle) → ip-address-family
            // and tcp-socket.subscribe(handle) → pollable;
            // drops the pollable; returns the family disc as u32.
            // Stub creates an Ipv6 socket → expected 1.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-tcp-method-component", "tcpmethod.component.wasm"));
            var resources = new ResourceContext();
            var sock = new TcpSocket(IpAddressFamily.Ipv6);
            int handle = resources.TableFor(typeof(TcpSocket))
                .Allocate(sock);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new SocketsBindings(resources).BindToRuntime(runtime);
                new IoBindings(resources).BindToRuntime(runtime);
            });

            Assert.Equal((uint)IpAddressFamily.Ipv6,
                (uint)ci.Invoke("ask-family", (uint)handle)!);
            // Pollable allocated by subscribe was dropped by the
            // guest; only the original tcp-socket remains.
            Assert.Equal(0, resources.TableFor(typeof(Pollable)).Count);
            Assert.Equal(1, resources.TableFor(typeof(TcpSocket)).Count);
        }

        private sealed class CapturingTcpSocket : TcpSocket
        {
            public CapturingTcpSocket() : base(IpAddressFamily.Ipv4) { }
            public Network? CapturedNetwork;
            public IpSocketAddress? CapturedAddress;
            public override void StartBind(Network network,
                IpSocketAddress localAddress)
            {
                CapturedNetwork = network;
                CapturedAddress = localAddress;
            }
        }

        [Fact]
        public void TcpSocket_start_bind_decodes_ipv4_variant_param()
        {
            // Fixture: ask-bind(handle, net) calls
            //   start-bind(network, ipv4(127.0.0.1:9000))
            // by passing 12 i32 wire slots for the variant param
            // (1 disc=0 + 11 payload slots; only s1..s5 are used
            // for the ipv4 case). Stub captures the resolved
            // IpSocketAddress; test asserts the round-tripped
            // ipv4 fields.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-tcp-startbind-component", "tcpstartbind.component.wasm"));
            var resources = new ResourceContext();
            var sock = new CapturingTcpSocket();
            var net = new Network();
            int hSock = resources.TableFor(typeof(TcpSocket))
                .Allocate(sock);
            int hNet = resources.TableFor(typeof(Network))
                .Allocate(net);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources).BindToRuntime(runtime));

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-bind", (uint)hSock, (uint)hNet)!);
            Assert.Same(net, sock.CapturedNetwork);
            Assert.IsType<Ipv4SocketAddress>(sock.CapturedAddress);
            var v4 = (Ipv4SocketAddress)sock.CapturedAddress!;
            Assert.Equal(9000, v4.Port);
            Assert.Equal(new byte[] { 127, 0, 0, 1 }, v4.Address);
        }

        [Fact]
        public void TcpSocket_start_bind_decodes_ipv6_variant_param()
        {
            // Same fixture, ipv6 export. Wire passes disc=1 +
            // 11 payload slots (port, flow, 8× u16 address,
            // scope). Stub captures the IpSocketAddress; assert
            // it's an Ipv6SocketAddress with port=443, [::1].
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-tcp-startbind-component", "tcpstartbind.component.wasm"));
            var resources = new ResourceContext();
            var sock = new CapturingTcpSocket();
            var net = new Network();
            int hSock = resources.TableFor(typeof(TcpSocket))
                .Allocate(sock);
            int hNet = resources.TableFor(typeof(Network))
                .Allocate(net);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources).BindToRuntime(runtime));

            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-bind-v6", (uint)hSock, (uint)hNet)!);
            Assert.IsType<Ipv6SocketAddress>(sock.CapturedAddress);
            var v6 = (Ipv6SocketAddress)sock.CapturedAddress!;
            Assert.Equal(443, v6.Port);
            Assert.Equal(0u, v6.FlowInfo);
            Assert.Equal(new ushort[] { 0, 0, 0, 0, 0, 0, 0, 1 },
                v6.Address);
            Assert.Equal(0u, v6.ScopeId);
        }

        private sealed class FixedAddrTcpSocket : TcpSocket
        {
            public FixedAddrTcpSocket() : base(IpAddressFamily.Ipv4) { }
            public override IpSocketAddress LocalAddress()
                => new Ipv4SocketAddress(8080,
                    new byte[] { 192, 168, 1, 42 });
        }

        private sealed class Ipv6TcpSocket : TcpSocket
        {
            public Ipv6TcpSocket() : base(IpAddressFamily.Ipv6) { }
            public override IpSocketAddress LocalAddress()
                => new Ipv6SocketAddress(443, 0,
                    new ushort[] {
                        0x2001, 0x0DB8, 0, 0,
                        0, 0, 0, 0x0001 },
                    0);
        }

        [Fact]
        public void TcpSocket_local_address_writes_ipv4_variant_payload()
        {
            // Fixture: ask-local-disc / ask-local-port /
            // ask-local-ipv4 each call local-address, read
            // different fields out of the variant wire form:
            //   retArea+0: outer disc
            //   retArea+4: variant disc (0=ipv4)
            //   retArea+8: port (u16)
            //   retArea+10..+13: ipv4 4-byte address
            // Stub returns 192.168.1.42:8080 (ipv4).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-tcp-localaddr-component", "tcplocaladdr.component.wasm"));
            var resources = new ResourceContext();
            var sock = new FixedAddrTcpSocket();
            int handle = resources.TableFor(typeof(TcpSocket))
                .Allocate(sock);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources).BindToRuntime(runtime));

            // Variant disc: ipv4 = 0
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-local-disc", (uint)handle)!);
            // Port: 8080
            Assert.Equal(8080u, (uint)ci.Invoke(
                "ask-local-port", (uint)handle)!);
            // IPv4 packed: 192.168.1.42 = 0xC0A8012A
            Assert.Equal(0xC0A8012Au, (uint)ci.Invoke(
                "ask-local-ipv4", (uint)handle)!);
        }

        [Fact]
        public void TcpSocket_local_address_writes_ipv6_variant_disc()
        {
            // Same fixture as the ipv4 test but with an ipv6
            // socket — only the variant disc is reachable
            // through the ipv4-shape WAT, so we just verify
            // that the binder routes to the ipv6 case (disc=1).
            // The full ipv6 fixture would need a separate WAT
            // reading the wider payload offsets.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-tcp-localaddr-component", "tcplocaladdr.component.wasm"));
            var resources = new ResourceContext();
            var sock = new Ipv6TcpSocket();
            int handle = resources.TableFor(typeof(TcpSocket))
                .Allocate(sock);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources).BindToRuntime(runtime));

            // Variant disc: ipv6 = 1
            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-local-disc", (uint)handle)!);
            // Port: 443
            Assert.Equal(443u, (uint)ci.Invoke(
                "ask-local-port", (uint)handle)!);
        }

        [Fact]
        public void TcpSocket_accept_returns_tuple_of_three_resource_handles()
        {
            // Fixture: ask-accept(handle) calls accept(),
            // reads three i32 handles from retArea+4/+8/+12,
            // counts non-zero handles, drops all three, returns
            // the count.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-tcp-accept-component", "tcpaccept.component.wasm"));
            var resources = new ResourceContext();
            var sock = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = resources.TableFor(typeof(TcpSocket))
                .Allocate(sock);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new SocketsBindings(resources).BindToRuntime(runtime);
                new StreamBindings(resources).BindToRuntime(runtime);
            });

            // Three non-zero handles allocated by accept.
            Assert.Equal(3u, (uint)ci.Invoke(
                "ask-accept", (uint)handle)!);
            // After drop: only the original tcp-socket handle
            // remains; the new tcp-socket allocated by accept
            // is dropped, and the streams tables are clean.
            Assert.Equal(1, resources.TableFor(typeof(TcpSocket)).Count);
            Assert.Equal(0, resources.TableFor(typeof(InputStream)).Count);
            Assert.Equal(0, resources.TableFor(typeof(OutputStream)).Count);
        }

        [Fact]
        public void TcpSocket_finish_connect_returns_input_output_pair()
        {
            // Fixture: ask-connect(handle) calls finish-connect,
            // reads (in_handle, out_handle) at retArea+4/+8,
            // counts non-zero handles, drops both, returns
            // the count.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-tcp-accept-component", "tcpaccept.component.wasm"));
            var resources = new ResourceContext();
            var sock = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = resources.TableFor(typeof(TcpSocket))
                .Allocate(sock);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new SocketsBindings(resources).BindToRuntime(runtime);
                new StreamBindings(resources).BindToRuntime(runtime);
            });

            Assert.Equal(2u, (uint)ci.Invoke(
                "ask-connect", (uint)handle)!);
            Assert.Equal(0, resources.TableFor(typeof(InputStream)).Count);
            Assert.Equal(0, resources.TableFor(typeof(OutputStream)).Count);
        }

        [Fact]
        public void TcpSocket_keep_alive_idle_time_and_count_round_trip()
        {
            // Fixture: ask-keepalive(handle) walks
            //   set-keep-alive-idle-time(1000)  (u64 param)
            //   set-keep-alive-count(5)          (u32 param)
            //   keep-alive-count()               (result<u32, _>)
            // and returns the count u32 from retArea+4.
            // Verifies u32 result-wrapped getter wire shape.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-tcp-keepalive-component", "tcpkeepalive.component.wasm"));
            var resources = new ResourceContext();
            var sock = new TcpSocket(IpAddressFamily.Ipv4);
            int handle = resources.TableFor(typeof(TcpSocket))
                .Allocate(sock);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources).BindToRuntime(runtime));

            Assert.Equal(5u, (uint)ci.Invoke(
                "ask-keepalive", (uint)handle)!);
            Assert.Equal(1000UL, sock.KeepAliveIdleTime());
            Assert.Equal(5u, sock.KeepAliveCount());
        }

        private sealed class OptionsTcpSocket : TcpSocket
        {
            public OptionsTcpSocket() : base(IpAddressFamily.Ipv4)
            {
                _listening = true;
            }
        }

        [Fact]
        public void TcpSocket_options_round_trip_through_result_primitive_returns()
        {
            // Fixture: ask-options(handle) walks
            //   is-listening() -> bool   (bare, no result)
            //   set-hop-limit(42)        (u8 param + result<_,_>)
            //   hop-limit()              (result<u8, _>)
            //   set-keep-alive-enabled(true) (bool param + result<_,_>)
            //   keep-alive-enabled()     (result<bool, _>)
            // and packs (kae<<16) | (hop<<8) | listening into a u32.
            // Stub starts in listening state → expect:
            //   listening=1, hop=42, kae=1 → 0x012a01.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-tcp-options-component", "tcpoptions.component.wasm"));
            var resources = new ResourceContext();
            var sock = new OptionsTcpSocket();
            int handle = resources.TableFor(typeof(TcpSocket))
                .Allocate(sock);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources).BindToRuntime(runtime));

            uint result = (uint)ci.Invoke("ask-options", (uint)handle)!;
            Assert.Equal(1u, result & 0xff);
            Assert.Equal(42u, (result >> 8) & 0xff);
            Assert.Equal(1u, (result >> 16) & 0xff);
            // Stub state should now reflect the setter calls.
            Assert.Equal((byte)42, sock.HopLimit());
            Assert.True(sock.KeepAliveEnabled());
        }

        private sealed class ListeningTcpSocket : TcpSocket
        {
            public int FinishBindCalls;
            public int StartListenCalls;
            public int FinishListenCalls;
            public ShutdownType LastShutdown;
            public ListeningTcpSocket() : base(IpAddressFamily.Ipv4) { }
            public override void FinishBind() => FinishBindCalls++;
            public override void StartListen() => StartListenCalls++;
            public override void FinishListen() => FinishListenCalls++;
            public override void Shutdown(ShutdownType how)
                => LastShutdown = how;
        }

        [Fact]
        public void TcpSocket_listen_lifecycle_threads_void_results_with_enum_param()
        {
            // Fixture: ask-listen(handle) walks finish-bind →
            // start-listen → finish-listen → shutdown(both),
            // summing the outer-disc bytes from each retArea.
            // Always-Ok = 0; expected return: 0. Stub records
            // each call so we verify the wire dispatch hit each
            // method and threaded the shutdown-type enum
            // correctly.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-tcp-listen-component", "tcplisten.component.wasm"));
            var resources = new ResourceContext();
            var sock = new ListeningTcpSocket();
            int handle = resources.TableFor(typeof(TcpSocket))
                .Allocate(sock);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources).BindToRuntime(runtime));

            Assert.Equal(0u, (uint)ci.Invoke("ask-listen", (uint)handle)!);
            Assert.Equal(1, sock.FinishBindCalls);
            Assert.Equal(1, sock.StartListenCalls);
            Assert.Equal(1, sock.FinishListenCalls);
            Assert.Equal(ShutdownType.Both, sock.LastShutdown);
        }

        private sealed class CapturingOutStream : OutgoingDatagramStream
        {
            public OutgoingDatagram[]? LastSent;
            public override ulong Send(OutgoingDatagram[] datagrams)
            {
                LastSent = datagrams;
                return (ulong)datagrams.Length;
            }
        }

        [Fact]
        public void OutgoingDatagramStream_send_decodes_list_of_record_param()
        {
            // Fixture: ask-send(handle) builds 1 outgoing-datagram
            // (data="ping", remote=Some(ipv4(127.0.0.1:7))) and
            // calls send. Stub captures the array; test asserts
            // every field round-tripped through the wire decode.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-udp-send-component", "udpsend.component.wasm"));
            var resources = new ResourceContext();
            var stream = new CapturingOutStream();
            int handle = resources.TableFor(typeof(OutgoingDatagramStream))
                .Allocate(stream);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources).BindToRuntime(runtime));

            // send returns count; stub returns input count = 1.
            Assert.Equal(1UL, (ulong)ci.Invoke(
                "ask-send", (uint)handle)!);
            Assert.NotNull(stream.LastSent);
            Assert.Single(stream.LastSent!);
            var dg = stream.LastSent![0];
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes("ping"),
                dg.Data);
            Assert.IsType<Ipv4SocketAddress>(dg.RemoteAddress);
            var v4 = (Ipv4SocketAddress)dg.RemoteAddress!;
            Assert.Equal(7, v4.Port);
            Assert.Equal(new byte[] { 127, 0, 0, 1 }, v4.Address);
        }

        private sealed class FixedDatagramStream : IncomingDatagramStream
        {
            public override IncomingDatagram[] Receive(ulong maxResults)
                => new[] {
                    new IncomingDatagram(
                        new byte[] { 0x68, 0x69, 0x21 },   // "hi!"
                        new Ipv4SocketAddress(53,
                            new byte[] { 8, 8, 8, 8 })),
                    new IncomingDatagram(
                        new byte[] { 0x6f, 0x6b },         // "ok"
                        new Ipv4SocketAddress(80,
                            new byte[] { 1, 1, 1, 1 })),
                };
        }

        [Fact]
        public void IncomingDatagramStream_receive_writes_list_of_record()
        {
            // Fixture: ask-recv-count / ask-recv-first-data-len /
            // ask-recv-first-data-byte / ask-recv-first-port each
            // call receive(4) and read different fields from
            // the returned list. Stub yields 2 datagrams:
            //   #0: "hi!" from 8.8.8.8:53
            //   #1: "ok" from 1.1.1.1:80
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-udp-receive-component", "udprecv.component.wasm"));
            var resources = new ResourceContext();
            var stream = new FixedDatagramStream();
            int handle = resources.TableFor(typeof(IncomingDatagramStream))
                .Allocate(stream);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources).BindToRuntime(runtime));

            // Count: 2 datagrams
            Assert.Equal(2u, (uint)ci.Invoke(
                "ask-recv-count", (uint)handle)!);
            // First data-len: 3 ("hi!")
            Assert.Equal(3u, (uint)ci.Invoke(
                "ask-recv-first-data-len", (uint)handle)!);
            // First data byte: 'h' = 0x68
            Assert.Equal(0x68u, (uint)ci.Invoke(
                "ask-recv-first-data-byte", (uint)handle)!);
            // First port: 53
            Assert.Equal(53u, (uint)ci.Invoke(
                "ask-recv-first-port", (uint)handle)!);
        }

        private sealed class CapturingUdpSocket : UdpSocket
        {
            public IpSocketAddress? CapturedRemote;
            public bool RemoteWasNull;
            public CapturingUdpSocket() : base(IpAddressFamily.Ipv4) { }
            public override (IncomingDatagramStream, OutgoingDatagramStream)
                UdpStream(IpSocketAddress? remoteAddress)
            {
                CapturedRemote = remoteAddress;
                RemoteWasNull = remoteAddress == null;
                return (new IncomingDatagramStream(),
                    new OutgoingDatagramStream());
            }
        }

        [Fact]
        public void UdpSocket_stream_with_none_remote_address()
        {
            // Fixture: ask-stream-none(handle) calls
            // %stream(None) — passes option-disc=0 + 12 zero
            // payload slots. Returns count of non-zero handles
            // (2). Stub asserts the option decoded to null.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-udp-stream-component", "udpstream.component.wasm"));
            var resources = new ResourceContext();
            var sock = new CapturingUdpSocket();
            int handle = resources.TableFor(typeof(UdpSocket))
                .Allocate(sock);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources).BindToRuntime(runtime));

            Assert.Equal(2u, (uint)ci.Invoke(
                "ask-stream-none", (uint)handle)!);
            Assert.True(sock.RemoteWasNull);
            Assert.Null(sock.CapturedRemote);
        }

        [Fact]
        public void UdpSocket_stream_with_some_ipv4_remote_address()
        {
            // Same fixture, ask-stream-some passes
            // option<ip-socket-address> = Some(ipv4(127.0.0.1:5000)).
            // Wire: option-disc=1, variant-disc=0, port=5000,
            // 4 address bytes, then 7 don't-care padding slots.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-udp-stream-component", "udpstream.component.wasm"));
            var resources = new ResourceContext();
            var sock = new CapturingUdpSocket();
            int handle = resources.TableFor(typeof(UdpSocket))
                .Allocate(sock);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources).BindToRuntime(runtime));

            Assert.Equal(2u, (uint)ci.Invoke(
                "ask-stream-some", (uint)handle)!);
            Assert.False(sock.RemoteWasNull);
            Assert.IsType<Ipv4SocketAddress>(sock.CapturedRemote);
            var v4 = (Ipv4SocketAddress)sock.CapturedRemote!;
            Assert.Equal(5000, v4.Port);
            Assert.Equal(new byte[] { 127, 0, 0, 1 }, v4.Address);
        }

        private sealed class StubNameLookup : IIpNameLookup
        {
            public string LastName = "";

            [WasiErrorResult]
            [WasiMethodName("resolve-addresses")]
            public ResolveAddressStream ResolveAddresses(Network network,
                string name)
            {
                LastName = name;
                return new StubResolveStream();
            }
        }

        private sealed class StubResolveStream : ResolveAddressStream
        {
            private bool _yielded;
            public override IpAddressEnumerationItem? ResolveNextAddress()
            {
                if (_yielded) return null;
                _yielded = true;
                return new Ipv4Address(new byte[] { 93, 184, 215, 14 });
            }
        }

        [Fact]
        public void IpNameLookup_resolves_addresses_and_yields_ipv4_address()
        {
            // Fixture: ask-lookup(net) → resolve-addresses
            // (net, "example.com"), then resolve-next-address;
            // packs (stream-nonzero, option-disc, variant-disc,
            // 4 address bytes) into a u32. Stub returns
            // 93.184.215.14 for example.com.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-ip-lookup-component", "iplookup.component.wasm"));
            var resources = new ResourceContext();
            var lookup = new StubNameLookup();
            var net = new Network();
            int hNet = resources.TableFor(typeof(Network))
                .Allocate(net);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new SocketsBindings(resources, ipNameLookup: lookup)
                    .BindToRuntime(runtime));

            uint result = (uint)ci.Invoke("ask-lookup", (uint)hNet)!;
            // bit 0: stream non-zero
            Assert.Equal(1u, result & 0x1);
            // bit 1: Some
            Assert.Equal(1u, (result >> 1) & 0x1);
            // bit 2-3: ipv4 = 0
            Assert.Equal(0u, (result >> 2) & 0x3);
            // bytes 8..31: 93, 184, 215 (the 4th, 14, would
            // collide with the high bit on test machines that
            // sign-extend; we check the first three).
            Assert.Equal(93u, (result >> 8) & 0xff);
            Assert.Equal(184u, (result >> 16) & 0xff);
            Assert.Equal(215u, (result >> 24) & 0xff);
            Assert.Equal("example.com", lookup.LastName);
        }

        [Fact]
        public void Network_handle_is_allocatable_via_resource_table()
        {
            // The network resource is a marker — no methods. The
            // table-allocation path (which subclasses every
            // [WasiResource] type) should work without any
            // method bindings.
            var table = new ResourceTable();
            var net = new Network();
            var h = table.Allocate(net);
            Assert.True(h > 0);
            Assert.True(table.Drop(h));
            Assert.Equal(0, table.Count);
        }
    }
}
