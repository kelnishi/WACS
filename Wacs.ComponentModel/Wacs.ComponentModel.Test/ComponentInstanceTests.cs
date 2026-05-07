// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
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
        public void Invoke_lifts_list_of_string()
        {
            // list-string-component: words() -> list<string>
            // Each element is a (strPtr, strLen) pair routed
            // through ListMarshal.LiftStringList → 3-element
            // string array.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "list-string-component", "ls.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal(new[] { "alpha", "beta", "gamma" },
                (string[])ci.Invoke("words")!);
        }

        [Fact]
        public void Invoke_lifts_option_string_some_branch()
        {
            // option-string-component: greeting() -> option<string>
            // (Some branch — fixture's static data has
            // disc=1, payload=(ptr,len) for "greetings").
            // That's the FIRST export ("maybe-some" — the
            // option-string-component is multi-export).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "option-string-component", "os.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal("greetings", ci.Invoke("maybe-some"));
            Assert.Null(ci.Invoke("maybe-none"));
        }

        [Fact]
        public void Invoke_lifts_tuple_of_prims()
        {
            // tuple-return-component: pair() -> tuple<u32, u32>
            var bytes = File.ReadAllBytes(FindFixturePath(
                "tuple-return-component", "t.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            var pair = ((uint, uint))ci.Invoke("pair")!;
            Assert.Equal(7u, pair.Item1);
            Assert.Equal(35u, pair.Item2);
        }

        [Fact]
        public void Invoke_lifts_result_prim_prim_ok_branch()
        {
            // result-return-component: divide() -> result<u32, u32>
            // Fixture takes the Ok branch with payload = 5.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "result-return-component", "r.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            var ok = ((bool, uint, uint))ci.Invoke("divide")!;
            Assert.True(ok.Item1);
            Assert.Equal(42u, ok.Item2);
            Assert.Equal(0u, ok.Item3);
        }

        [Fact]
        public void Invoke_lifts_result_string_u32_both_branches()
        {
            // result-string-component:
            //   greet() → Ok("fine")
            //   fail()  → Err(404)
            var bytes = File.ReadAllBytes(FindFixturePath(
                "result-string-component", "rs.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);

            var greetTuple = ((bool, string, uint))ci.Invoke("greet")!;
            Assert.True(greetTuple.Item1);
            Assert.Equal("fine", greetTuple.Item2);
            Assert.Equal(0u, greetTuple.Item3);

            var failTuple = ((bool, string, uint))ci.Invoke("fail")!;
            Assert.False(failTuple.Item1);
            Assert.Null(failTuple.Item2);
            Assert.Equal(404u, failTuple.Item3);
        }

        [Fact]
        public void Invoke_lifts_enum_as_underlying_integer()
        {
            // enum-component: current() -> direction
            // Without a decoded WIT name on Invoke's side the
            // lift returns the raw discriminant byte (the
            // transpiler's same fallback).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "enum-component", "en.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal((byte)2, ci.Invoke("current"));
        }

        [Fact]
        public void Invoke_lifts_flags_as_underlying_bitmask()
        {
            // flags-component: default-perms() -> permissions
            // 3 flags → byte; payload = 5 = read | execute.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "flags-component", "fl.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal((byte)5, ci.Invoke("default-perms"));
        }

        [Fact]
        public void Invoke_lowers_string_param_via_cabi_realloc()
        {
            // string-param-component: echo(s: string) -> string
            // Round-trip exercises both lower (UTF-8 encode +
            // cabi_realloc + memcpy) and lift (StringMarshal).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "string-param-component", "sp.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal("roundtrip", ci.Invoke("echo", "roundtrip"));
        }

        [Fact]
        public void Invoke_lowers_list_u32_param_via_cabi_realloc()
        {
            // list-param-component: sum(xs: list<u32>) -> u32
            // Marshals the input array's bytes into guest memory
            // via cabi_realloc + ListMarshal.CopyArrayToGuest,
            // pushes (ptr, count), core sums on its side.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "list-param-component", "lp.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal(15u,
                ci.Invoke("sum", new uint[] { 1, 2, 3, 4, 5 }));
        }

        [Fact]
        public void Invoke_lifts_record_as_field_dictionary()
        {
            // record-component: origin() -> point { x: u32, y: u32 }
            // Surfaces as IReadOnlyDictionary<string, object>.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "record-component", "rc.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            var fields = (IReadOnlyDictionary<string, object>)
                ci.Invoke("origin")!;
            Assert.Equal(7u, fields["x"]);
            Assert.Equal(11u, fields["y"]);
        }

        [Fact]
        public void Invoke_lifts_variant_as_tag_payload_tuple()
        {
            // variant-component: lookup() -> lookup-result
            // Returns the Found(42) case (tag=2, payload=42).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "variant-component", "vt.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            var (tag, payload) = ((byte, object?))ci.Invoke("lookup")!;
            Assert.Equal((byte)2, tag);
            Assert.Equal(42u, payload);
        }

        [Fact]
        public void Invoke_lifts_variant_with_string_payload()
        {
            // variant-string-component: describe() -> status
            // where `variant status { idle, denied(string), ok }`.
            // The fixture returns denied("denied") (tag=1).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "variant-string-component", "vs.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            var (tag, payload) = ((byte, object?))ci.Invoke("describe")!;
            Assert.Equal((byte)1, tag);
            Assert.Equal("denied", payload);
        }

        [Fact]
        public void Invoke_lifts_variant_with_list_payload()
        {
            // variant-list-component: discover() -> scan
            // where `variant scan { empty, found(list<u32>) }`.
            // Fixture returns found([10,20,30]) (tag=1).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "variant-list-component", "vl.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            var (tag, payload) = ((byte, object?))ci.Invoke("discover")!;
            Assert.Equal((byte)1, tag);
            Assert.Equal(new uint[] { 10, 20, 30 }, (uint[])payload!);
        }

        [Fact]
        public void Invoke_lifts_variant_with_record_payload()
        {
            // variant-record-component: locate() -> shape
            // where `variant shape { empty, dot(point) }` and
            // `record point { x: u32, y: u32 }`. Fixture returns
            // dot({x:7, y:11}) (tag=1). Without dynamic-type
            // emission the record surfaces as a string→object
            // dictionary inside the variant payload slot.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "variant-record-component", "vr.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            var (tag, payload) = ((byte, object?))ci.Invoke("locate")!;
            Assert.Equal((byte)1, tag);
            var dict = (IReadOnlyDictionary<string, object>)payload!;
            Assert.Equal(7u, dict["x"]);
            Assert.Equal(11u, dict["y"]);
        }

        [Fact]
        public void Invoke_calls_through_host_provided_wasi_random_u64_import()
        {
            // Phase 3 v0: smallest possible WASI Preview 2
            // host-import slice. The fixture imports
            // wasi:random/random.get-random-u64 and exposes a
            // `pick` export that just calls through. The host
            // hands a stub via configureImports — the runtime
            // resolves the inner core wasm's import by exact
            // (module, name) match (wit-component's
            // instantiate-with passes the host-provided instance
            // straight through under the same namespace name).
            // Primitive-only signature (no aggregates) means the
            // canon-lower section is effectively pass-through;
            // aggregates (string, list) are a follow-up that
            // needs real lowering wiring.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-random-u64-component", "rand.component.wasm"));
            const ulong expected = 0xDEADBEEF_CAFEF00DUL;
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindHostFunction<System.Func<Wacs.Core.Runtime.ExecContext, ulong>>(
                    ("wasi:random/random@0.2.3", "get-random-u64"),
                    _ => expected);
            });
            Assert.Equal(expected, ci.Invoke("pick"));
        }

        [Fact]
        public void Invoke_lifts_compact_utf16_latin1_branch()
        {
            // compact-utf16-component (string-encoding=latin1+utf16):
            // `latin` returns a buffer with the high-bit CLEAR
            // → Latin-1 decode, len = byte count.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "compact-utf16-component", "clu.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal("Hi", ci.Invoke("latin"));
        }

        [Fact]
        public void Invoke_lifts_compact_utf16_utf16_branch()
        {
            // Companion: `wide` returns a buffer with the high-
            // bit SET → UTF-16 decode, len & ~tag = u16 code units.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "compact-utf16-component", "clu.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal("Hi", ci.Invoke("wide"));
        }

        [Fact]
        public void Invoke_resolves_through_two_level_nested_components()
        {
            // 2-level composer chain: outer → middle → innermost.
            // Innermost has the real core module + canon lift;
            // middle and outer are composer-mode wrappers that
            // alias-re-export through. Recursive Instantiate
            // turns each composer into a ComponentInstance whose
            // Invoke delegates to the next level down. Verifies
            // composer-of-composer falls out cleanly from the
            // existing recursion.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "nested-component-2level", "multi.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal("Hi!", ci.Invoke("greet"));
        }

        [Fact]
        public void Invoke_resolves_through_nested_component_alias()
        {
            // nested-component fixture: outer composes one inner
            // component, alias-re-exporting its `inner-greet`
            // export as `greet`. The interpreter detects no core
            // modules + nested components, takes the composer
            // path: recursively instantiates each nested
            // component, walks Instances + Aliases to map outer
            // component-func indices through to (inner instance,
            // export name) pairs. Invoke routes through the chain.
            // The inner's `inner-greet` returns "Hi!" via its
            // canon-lifted UTF-8 string return.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "nested-component", "nested.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal("Hi!", ci.Invoke("greet"));
        }

        [Fact]
        public void Invoke_round_trips_utf16_string_param()
        {
            // utf16-string-param-component: echo(s) -> string
            // with string-encoding=utf16. Tests the lower path:
            // C# string → EncodeUtf16 → cabi_realloc(align=2) →
            // (ptr, codeUnits) on the wasm stack. Guest echoes
            // back the same (ptr, codeUnits) at retArea; the
            // lift decodes via LiftUtf16 to round-trip.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "utf16-string-param-component", "u16p.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal("Hello", ci.Invoke("echo", "Hello"));
            Assert.Equal("café", ci.Invoke("echo", "café"));
        }

        [Fact]
        public void Invoke_lifts_utf16_string_via_canon_option()
        {
            // utf16-string-component: greet() -> string with the
            // canon-lift's string-encoding=utf16 option set.
            // Component fixture stores "Hi" as UTF-16LE
            // (4 bytes / 2 code units). The interpreter reads the
            // option from the canon entry on Invoke and dispatches
            // the lift through StringMarshal.LiftUtf16.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "utf16-string-component", "u16.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal("Hi", ci.Invoke("greet"));
        }

        [Fact]
        public void Invoke_lifts_own_resource_as_raw_handle()
        {
            // resource-component: make() -> own<counter>
            // Without dynamic-type emission, the interpreter
            // surfaces the i32 handle directly. The fixture
            // returns 1.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "resource-component", "rsrc.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            Assert.Equal(1, ci.Invoke("make"));
        }

        [Fact]
        public void Invoke_lowers_borrow_resource_param_as_raw_handle()
        {
            // resource-component: inspect(c: borrow<counter>) -> u32
            // Caller passes the raw i32 handle directly; core
            // returns 42 regardless of which handle came in.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "resource-component", "rsrc.component.wasm"));
            var ci = ComponentInstance.Instantiate(bytes);
            var handle = (int)ci.Invoke("make")!;
            Assert.Equal(42u, ci.Invoke("inspect", handle));
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
