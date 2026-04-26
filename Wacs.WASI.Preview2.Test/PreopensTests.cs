// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Filesystem;
using Wacs.WASI.Preview2.HostBinding;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>
    /// Tests for wasi:filesystem/preopens — exercises the new
    /// list&lt;tuple&lt;resource, string&gt;&gt; canon-lower
    /// wrapper. The descriptor resource itself is a marker
    /// (no methods) in v0; this test verifies the array
    /// shape gets through.
    /// </summary>
    public class PreopensTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        private sealed class StubPreopens : IPreopens
        {
            private readonly (Descriptor, string)[] _dirs;
            public StubPreopens((Descriptor, string)[] dirs) { _dirs = dirs; }
            public (Descriptor, string)[] GetDirectories() => _dirs;
        }

        [Fact]
        public void GetDirectories_returns_count_through_resource_pair_array()
        {
            // Stub returns 3 (descriptor, path) pairs; fixture
            // reads count from retArea+4, drops all descriptor
            // handles, returns the count.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-preopens-component", "po.component.wasm"));
            var resources = new ResourceContext();
            var stub = new StubPreopens(new[]
            {
                (new Descriptor("/"), "/"),
                (new Descriptor("/tmp"), "/tmp"),
                (new Descriptor("/home"), "/home"),
            });

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiInstance(
                    "wasi:filesystem/preopens@0.2.3", stub, resources);
                runtime.BindWasiResource<Descriptor>(
                    "wasi:filesystem/types@0.2.3", resources);
            });

            Assert.Equal(3u, (uint)ci.Invoke("count")!);
            // After all drops the table should be empty.
            Assert.Equal(0, resources.TableFor(typeof(Descriptor)).Count);
        }

        [Fact]
        public void GetDirectories_zero_descriptors_returns_zero()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-preopens-component", "po.component.wasm"));
            var resources = new ResourceContext();
            var stub = new StubPreopens(System.Array.Empty<(Descriptor, string)>());

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiInstance(
                    "wasi:filesystem/preopens@0.2.3", stub, resources);
                runtime.BindWasiResource<Descriptor>(
                    "wasi:filesystem/types@0.2.3", resources);
            });

            Assert.Equal(0u, (uint)ci.Invoke("count")!);
        }
    }
}
