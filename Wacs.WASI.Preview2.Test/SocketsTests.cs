// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

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
