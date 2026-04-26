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
