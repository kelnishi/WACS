// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>Tests for [WasiUnitResult] — bare WIT
    /// <c>result&lt;_, _&gt;</c> on a resource method. The
    /// wire shape is flat-return i32 (just the disc); the
    /// host method returns void and the binder fills 0 (Ok)
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

            [WasiUnitResult]
            [WasiMethodName("do-it")]
            public virtual void DoIt() { DoItCalled = true; }

            [WasiUnitResult]
            [WasiMethodName("set-name")]
            public virtual void SetName(
                [WasiOptionalParam] string? name)
            {
                SetNameCalled = true;
                CapturedName = name;
            }

            public virtual void Dispose() { }
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
            {
                runtime.BindWasiResource<Thing>(
                    "test:unit/iface@1.0.0", resources);
            });

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
            {
                runtime.BindWasiResource<Thing>(
                    "test:unit/iface@1.0.0", resources);
            });

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
