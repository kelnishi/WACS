// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>Tests for unit-result wire shape — bare WIT
    /// <c>result&lt;_, _&gt;</c> on a resource method. The
    /// flat-return form is just an i32 disc (always 0 = Ok);
    /// the host method returns void and the binding emits 0
    /// unconditionally.</summary>
    public class UnitResultTests
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
        public class Thing : IDisposable
        {
            public bool DoItCalled;
            public bool SetNameCalled;
            public string? CapturedName;

            public virtual void DoIt() { DoItCalled = true; }
            public virtual void SetName(string? name)
            {
                SetNameCalled = true;
                CapturedName = name;
            }
            public virtual void Dispose() { }
        }

        private sealed class ThingBindings : IBindable
        {
            private const string Ns = "test:unit/iface@1.0.0";
            private readonly ResourceContext _resources;
            public ThingBindings(ResourceContext resources)
                => _resources = resources;

            public void BindToRuntime(WasmRuntime runtime)
            {
                var things = _resources.Table<Thing>();

                runtime.BindHostFunction<Action<ExecContext, int>>(
                    (Ns, "[resource-drop]thing"),
                    (_, h) => things.Drop(h));

                // do-it() -> result<_, _> — flat-return i32 disc.
                runtime.BindHostFunction<Func<ExecContext, int, int>>(
                    (Ns, "[method]thing.do-it"),
                    (_, h) => { ((Thing)things.Get(h)).DoIt(); return 0; });

                // set-name(option<string>) -> result<_, _>.
                // Wire: handle + opt-disc + ptr + len → flat i32.
                runtime.BindHostFunction<Func<ExecContext, int, int, int, int, int>>(
                    (Ns, "[method]thing.set-name"),
                    (ctx, h, optDisc, ptr, len) =>
                    {
                        string? name = optDisc == 0 ? null
                            : ctx.ReadUtf8String(ptr, len);
                        ((Thing)things.Get(h)).SetName(name);
                        return 0;
                    });
            }
        }

        [Fact]
        public void DoIt_returns_flat_ok_disc_and_invokes_host()
        {
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-unit-result-component",
                "unitresult.component.wasm"));
            var resources = new ResourceContext();
            var thing = new Thing();
            int hThing = resources.TableFor(typeof(Thing))
                .Allocate(thing);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new ThingBindings(resources).BindToRuntime(runtime));

            // do-it returns bare result; the wat layer just
            // forwards the wire i32. 0 = Ok.
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-do-it", (uint)hThing)!);
            Assert.True(thing.DoItCalled);
        }

        [Fact]
        public void SetName_with_option_string_param_and_unit_result()
        {
            // Combines option<string> param decode with the
            // bare-result flat-return shape — three wire
            // params (handle + opt-disc + ptr + len) -> one
            // wire return (i32 disc).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-unit-result-component",
                "unitresult.component.wasm"));
            var resources = new ResourceContext();
            var thing = new Thing();
            int hThing = resources.TableFor(typeof(Thing))
                .Allocate(thing);

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new ThingBindings(resources).BindToRuntime(runtime));

            // None
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-name-none", (uint)hThing)!);
            Assert.True(thing.SetNameCalled);
            Assert.Null(thing.CapturedName);

            // Some("Alice")
            thing.SetNameCalled = false;
            Assert.Equal(0u, (uint)ci.Invoke(
                "ask-set-name-some", (uint)hThing)!);
            Assert.True(thing.SetNameCalled);
            Assert.Equal("Alice", thing.CapturedName);
        }
    }
}
