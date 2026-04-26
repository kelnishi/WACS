// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
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
