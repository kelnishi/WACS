// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>
    /// End-to-end resource handle tests — covers
    /// own&lt;T&gt; allocation, [method]T binding,
    /// [resource-drop]T disposal.
    /// </summary>
    public class ResourceTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        [WasiResource("thing")]
        public sealed class Thing : System.IDisposable
        {
            public uint Value { get; }
            public bool Disposed { get; private set; }
            public Thing(uint value) { Value = value; }
            public uint GetValue() => Value;
            public void Dispose() { Disposed = true; }
        }

        public sealed class StubThings
        {
            public Thing Make() => new Thing(42u);
        }

        [Fact]
        public void Resource_full_round_trip_make_call_drop()
        {
            // Component: imports `make`, `[method]thing.get-value`,
            // and `[resource-drop]thing`. The exported `trip`
            // calls make → handle, get-value(handle) → value,
            // drop(handle), returns value. Verifies the full
            // host-side resource lifecycle plus the table is
            // empty after drop.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-resource-component", "res.component.wasm"));
            var resources = new ResourceContext();
            var things = new StubThings();
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiInstance("local:res/things", things, resources);
                runtime.BindWasiResource<Thing>("local:res/things", resources);
            });

            var v = (uint)ci.Invoke("trip")!;
            Assert.Equal(42u, v);

            // After trip: table should be empty (drop was called).
            Assert.Equal(0, resources.TableFor(typeof(Thing)).Count);
        }

        [Fact]
        public void ResourceTable_drop_disposes_idisposable_instance()
        {
            // Direct unit test: alloc a Thing, drop, verify
            // Disposed flag flipped.
            var t = new Thing(1u);
            var table = new ResourceTable();
            var h = table.Allocate(t);
            Assert.False(t.Disposed);
            Assert.True(table.Drop(h));
            Assert.True(t.Disposed);
            Assert.Equal(0, table.Count);
        }

        [Fact]
        public void ResourceTable_drop_on_already_dropped_handle_is_silent()
        {
            // Canonical-ABI semantics: drop on an already-dropped
            // handle is allowed (returns "didn't do anything"
            // rather than throwing). Mirrors what the wasm
            // canonical ABI mandates.
            var table = new ResourceTable();
            var h = table.Allocate(new Thing(1u));
            Assert.True(table.Drop(h));
            Assert.False(table.Drop(h));
        }
    }
}
