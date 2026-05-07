// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Reflection;
using Wacs.ComponentModel.Runtime;
using Wacs.Core;
using Wacs.Core.Runtime;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Mixed-engine composition tests at the component boundary —
    /// a transpiled component consuming an interpreter component as
    /// a typed host bundle, and the inverse direction (interpreter
    /// runtime importing a transpiled component's exports).
    /// </summary>
    public class ComponentBridgeTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        // [WitSource]-tagged interface mirroring tiny-component's
        // freestanding `greet: func() -> u32` export. Tag matches
        // what wit-bindgen-csharp / ExportInterfaceEmit would
        // produce — the Item carries the wasm export name.
        [WitSourceAttribute("interface tiny",
            Package = "local:tiny@1.0.0", Interface = "tiny")]
        public interface ITinyExports
        {
            [WitSourceAttribute("greet: func() -> u32",
                Package = "local:tiny@1.0.0", Interface = "tiny",
                Item = "greet")]
            uint Greet();
        }

        // Bundle wrapping the typed interface — matches the shape
        // direct-linked transpiled components consume as their
        // host bundle. One ctor param per interface property.
        public sealed class TinyBundle
        {
            public ITinyExports Tiny { get; }
            public TinyBundle(ITinyExports tiny) { Tiny = tiny; }
        }

        [Fact]
        public void AsTypedInterface_routes_method_calls_to_ComponentInstance_Invoke()
        {
            // Forward direction: an interpreter ComponentInstance
            // exposed as a [WitSource]-tagged C# interface. The
            // proxy's Greet() call routes to ci.Invoke("greet").
            // Proves the typed seam works for the simplest free-
            // function shape.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "tiny-component", "tiny.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);

            var proxy = ComponentBridge.AsTypedInterface<ITinyExports>(ci);

            Assert.NotNull(proxy);
            Assert.Equal(42u, proxy.Greet());
        }

        [Fact]
        public void AsHostBundle_constructs_bundle_with_proxied_interfaces()
        {
            // The transpiled-component consumer side: a host bundle
            // (one ctor param per [WitSource]-tagged interface)
            // built from an interpreter component. This is the
            // shape a transpiled component's generated Module ctor
            // takes as its `object hostBundle` argument.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "tiny-component", "tiny.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);

            var bundle = (TinyBundle)ComponentBridge.AsHostBundle(
                ci, typeof(TinyBundle));

            Assert.NotNull(bundle);
            Assert.NotNull(bundle.Tiny);
            // The proxy on the bundle's property is a working
            // bridge into the interpreter component.
            Assert.Equal(42u, bundle.Tiny.Greet());
        }

        [Fact]
        public void BindAsImports_registers_typed_methods_as_host_functions()
        {
            // Inverse direction: a typed exports object (here a
            // hand-written impl, in production it's
            // result.ModuleClass from a transpiled component) gets
            // bound onto a fresh runtime as host functions, so an
            // interpreted component can import them by their
            // wit-qualified (module, entity) name.
            var runtime = new WasmRuntime();
            var impl = new TinyImpl();
            var bound = ComponentBridge.BindAsImports(
                runtime, impl, typeof(ITinyExports));
            Assert.Equal(1, bound);

            // Verify the binding landed at the (Package + "/" +
            // Interface, Item) name the bridge derives from the
            // [WitSource] attribute.
            var found = false;
            foreach (var (m, e) in runtime.EnumerateBoundEntities())
            {
                if (m == "local:tiny@1.0.0/tiny" && e == "greet")
                {
                    found = true;
                    break;
                }
            }
            Assert.True(found,
                "Expected (local:tiny@1.0.0/tiny, greet) binding "
                + "after BindAsImports.");
        }

        // Trivial in-test impl of the tiny interface. Stands in for
        // a transpiled component's IExports object in tests.
        private sealed class TinyImpl : ITinyExports
        {
            public uint Greet() => 42u;
        }
    }
}
