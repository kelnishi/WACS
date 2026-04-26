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
            {
                runtime.BindWasiInstance(
                    "wasi:sockets/tcp-create-socket@0.2.3",
                    factory, resources);
                runtime.BindWasiResource<TcpSocket>(
                    "wasi:sockets/tcp@0.2.3", resources);
            });

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
                runtime.BindWasiResource<TcpSocket>(
                    "wasi:sockets/tcp@0.2.3", resources);
                runtime.BindWasiResource<Pollable>(
                    "wasi:io/poll@0.2.3", resources);
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
            {
                runtime.BindWasiResource<TcpSocket>(
                    "wasi:sockets/tcp@0.2.3", resources);
                runtime.BindWasiResource<Network>(
                    "wasi:sockets/network@0.2.3", resources);
            });

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
            {
                runtime.BindWasiResource<TcpSocket>(
                    "wasi:sockets/tcp@0.2.3", resources);
                runtime.BindWasiResource<Network>(
                    "wasi:sockets/network@0.2.3", resources);
            });

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
            {
                runtime.BindWasiResource<TcpSocket>(
                    "wasi:sockets/tcp@0.2.3", resources);
            });

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
            {
                runtime.BindWasiResource<TcpSocket>(
                    "wasi:sockets/tcp@0.2.3", resources);
            });

            // Variant disc: ipv6 = 1
            Assert.Equal(1u, (uint)ci.Invoke(
                "ask-local-disc", (uint)handle)!);
            // Port: 443
            Assert.Equal(443u, (uint)ci.Invoke(
                "ask-local-port", (uint)handle)!);
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
            {
                runtime.BindWasiResource<TcpSocket>(
                    "wasi:sockets/tcp@0.2.3", resources);
            });

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
            {
                runtime.BindWasiResource<TcpSocket>(
                    "wasi:sockets/tcp@0.2.3", resources);
            });

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
            {
                runtime.BindWasiResource<TcpSocket>(
                    "wasi:sockets/tcp@0.2.3", resources);
            });

            Assert.Equal(0u, (uint)ci.Invoke("ask-listen", (uint)handle)!);
            Assert.Equal(1, sock.FinishBindCalls);
            Assert.Equal(1, sock.StartListenCalls);
            Assert.Equal(1, sock.FinishListenCalls);
            Assert.Equal(ShutdownType.Both, sock.LastShutdown);
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
