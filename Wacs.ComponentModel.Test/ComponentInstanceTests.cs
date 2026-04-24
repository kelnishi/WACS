// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Phase 1c v0 tests for <see cref="ComponentInstance"/> —
    /// the interpreter-side companion to the AOT transpiler.
    /// Fixtures here are the same .component.wasm binaries the
    /// transpiler tests exercise; running them through both
    /// engines and verifying outputs match is the dual-engine
    /// equivalence gate the scope plan calls out.
    /// </summary>
    public class ComponentInstanceTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        [Fact]
        public void Invoke_returns_primitive_export_value()
        {
            // tiny-component exports `greet: func() -> u32` whose
            // core returns 42. Through the interpreter that's a
            // straight pass-through with the WIT-decoded primitive
            // mapping (i32 → uint via the U32 case).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "tiny-component", "tiny.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal(42u, ci.Invoke("greet"));
        }

        [Fact]
        public void Invoke_forwards_primitive_args_to_core()
        {
            // add-component exports `add(x: u32, y: u32) -> u32`.
            // Lower path boxes (7, 35) as ints, ships through
            // CreateInvoker, lifts the i32 result back as uint.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "add-component", "ad.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal(42u, ci.Invoke("add", 7u, 35u));
        }

        [Fact]
        public void Invoke_lifts_string_return_via_StringMarshal()
        {
            // string-return-component exports `hello: func() -> string`.
            // Core returns a retArea pointer; ComponentInstance
            // reads (strPtr, strLen) from the exported memory and
            // routes the bytes through StringMarshal.LiftUtf8.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "string-return-component", "h.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal("Hello", ci.Invoke("hello"));
        }

        [Fact]
        public void Invoke_lifts_f64_with_bit_precision()
        {
            // Same f64 round-trip as the transpiler test —
            // verifies the interpreter doesn't lose bits going
            // through the boxed-object path.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "f64-component", "p.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal(3.141592653589793, ci.Invoke("pi"));
        }

        [Fact]
        public void Invoke_lifts_u64_full_64_bits()
        {
            // u64 sentinel value 0x0123456789ABCDEF — the lift
            // path mustn't truncate to int.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "u64-component", "u.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal(0x0123456789ABCDEFUL, ci.Invoke("big"));
        }

        [Fact]
        public void Invoke_lifts_list_of_u8_via_ListMarshal()
        {
            // list-return-component exports `bytes() -> list<u8>`.
            // Same retArea + ListMarshal.LiftPrim<byte> path the
            // transpiler's IL takes, just dispatched via runtime
            // reflection (MakeGenericMethod) instead of an
            // emitted Newobj/Callvirt sequence.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "list-return-component", "l.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            var value = (byte[])ci.Invoke("bytes")!;
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, value);
        }

        [Fact]
        public void Invoke_lifts_option_u32_some_branch()
        {
            // option-return-component's core writes
            // `[disc=1, padding, payload=42]` to the retArea, so
            // the lift takes the Some branch and returns
            // (uint?)42. Mirrors
            // TranspileSingleModule_lifts_option_u32_some_return.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "option-return-component", "o.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal((uint?)42u, ci.Invoke("find"));
        }

        [Fact]
        public void Invoke_lifts_option_u32_none_branch()
        {
            // option-none-component: disc=0, payload undefined.
            // The lift returns Nullable<uint>.HasValue=false
            // without touching the payload bytes.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "option-none-component", "n.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Null(ci.Invoke("missing"));
        }

        [Fact]
        public void Invoke_dual_engine_equivalence_with_transpiler()
        {
            // The cheapest cross-check: run the same fixture
            // through the interpreter and assert it matches the
            // value the transpiler-emitted IL produces (here
            // baked in inline since the transpiler-test project
            // is a separate assembly). add-component is the
            // simplest fixture exercising both param-lower and
            // primitive-result-lift in a single call.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "add-component", "ad.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);

            // Transpiler test asserts add(7, 35) → 42; mirror it
            // through the interpreter.
            Assert.Equal(42u, ci.Invoke("add", 7u, 35u));
            Assert.Equal(0u, ci.Invoke("add", 0u, 0u));
            Assert.Equal(uint.MaxValue,
                ci.Invoke("add", uint.MaxValue - 100u, 100u));
        }
    }
}
