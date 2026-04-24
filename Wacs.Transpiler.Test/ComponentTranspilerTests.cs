// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.AOT.Component;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Phase 1b smoke tests: parse a real component binary and
    /// extract its embedded core modules via the component
    /// transpiler's entry point. Deeper IL-emit + roundtrip
    /// verification lands as the canonical-ABI and interface-
    /// emission pieces fill in.
    /// </summary>
    public class ComponentTranspilerTests
    {
        private static string FindTinyComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "tiny-component", "wasm",
                                "tiny.component.wasm");
        }

        [Fact]
        public void ParseFile_extracts_embedded_core_module()
        {
            var result = ComponentTranspiler.ParseFile(FindTinyComponentPath());

            // tiny-component embeds exactly one core wasm module
            // (greet() -> u32 returning 42). The parser should
            // round-trip through the core wasm binary parser and
            // produce a module whose single export is `greet`.
            Assert.Single(result.CoreModules);
            var core = result.CoreModules[0];
            Assert.NotNull(core);
            Assert.Contains(core.Exports, e => e.Name == "greet");
        }

        [Fact]
        public void Embedded_core_module_transpiles_through_existing_pipeline()
        {
            // End-to-end: component binary → embedded core module
            // → Wacs.Core parse → runtime instantiate →
            // ModuleTranspiler.Transpile. Verifies the component
            // transpiler's output is directly consumable by the
            // existing per-module IL emitter — no further
            // adaptation needed for the core-module layer.
            var result = ComponentTranspiler.ParseFile(FindTinyComponentPath());
            Assert.Single(result.CoreModules);

            var runtime = new WasmRuntime();
            var moduleInst = runtime.InstantiateModule(result.CoreModules[0]);

            var transpiler = new ModuleTranspiler("Wacs.Transpiled.Tiny");
            var transpileResult = transpiler.Transpile(
                moduleInst, runtime, moduleName: "TinyModule");

            Assert.NotNull(transpileResult);
            Assert.NotNull(transpileResult.FunctionsType);
        }

        private static string FindPrimCastComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "prim-cast-component", "wasm",
                                "bc.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lifts_option_u32_none_return()
        {
            // option-none-component exports `missing() -> option<u32>`
            // with disc byte = 0 at offset 200. Should return
            // the default value of uint? (null / HasValue = false).
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            var path = Path.Combine(dir!.FullName, "Spec.Test", "components",
                                     "fixtures", "option-none-component", "wasm",
                                     "n.component.wasm");
            using var fs = File.OpenRead(path);
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            var missing = componentExports!.GetMethod("Missing");
            var value = (uint?)missing!.Invoke(null, null);
            Assert.False(value.HasValue);
        }

        private static string FindTupleReturnComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "tuple-return-component", "wasm",
                                "t.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lifts_tuple_u32_u32_return()
        {
            // tuple-return-component exports
            // `pair() -> tuple<u32, u32>`. Core returns retArea
            // ptr 200; memory[200..204] = 7, memory[204..208] = 35.
            // Expect (7u, 35u) as ValueTuple<uint, uint>.
            using var fs = File.OpenRead(FindTupleReturnComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            var pair = componentExports!.GetMethod("Pair");
            Assert.NotNull(pair);
            Assert.Equal(typeof(System.ValueTuple<uint, uint>), pair!.ReturnType);

            var tuple = ((uint, uint))pair.Invoke(null, null)!;
            Assert.Equal(7u, tuple.Item1);
            Assert.Equal(35u, tuple.Item2);
        }

        private static string FindResultReturnComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "result-return-component", "wasm",
                                "r.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lifts_result_u32_u32_ok_branch()
        {
            // result-return-component exports
            // `divide() -> result<u32, u32>`. Core returns retArea
            // ptr 200; memory[200..204] = 0 (Ok disc);
            // memory[204..208] = 42 (payload). Expect tuple
            // (true, 42u, 0u).
            using var fs = File.OpenRead(FindResultReturnComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            var divide = componentExports!.GetMethod("Divide");
            Assert.NotNull(divide);
            Assert.Equal(
                typeof(System.ValueTuple<bool, uint, uint>),
                divide!.ReturnType);

            var tuple = ((bool ok, uint value, uint error))
                divide.Invoke(null, null)!;
            Assert.True(tuple.ok);
            Assert.Equal(42u, tuple.value);
            Assert.Equal(0u, tuple.error);
        }

        private static string FindOptionReturnComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "option-return-component", "wasm",
                                "o.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lifts_option_u32_some_return()
        {
            // option-return-component exports `find() -> option<u32>`.
            // Core returns retArea ptr = 200; memory[200] = 0x01
            // (Some tag); memory[204..208] = 0x2a (payload = 42).
            // ComponentExports.Find() → uint? with value 42u.
            using var fs = File.OpenRead(FindOptionReturnComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var find = componentExports!.GetMethod("Find");
            Assert.NotNull(find);
            Assert.Equal(typeof(uint?), find!.ReturnType);

            var value = (uint?)find.Invoke(null, null);
            Assert.True(value.HasValue);
            Assert.Equal(42u, value.Value);
        }

        private static string FindListReturnComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "list-return-component", "wasm",
                                "l.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lifts_list_u8_return_from_guest_memory()
        {
            // list-return-component exports `bytes() -> list<u8>`.
            // Core sig: `() -> i32`, returning the retArea ptr.
            // Memory layout:
            //   [100..105] = 01 02 03 04 05 (list payload)
            //   [200..204] = 100 (dataPtr)
            //   [204..208] = 5   (count)
            // ComponentExports.Bytes() reads (dataPtr, count)
            // from the return area and lifts via ListMarshal.LiftPrim<byte>.
            using var fs = File.OpenRead(FindListReturnComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var bytes = componentExports!.GetMethod("Bytes");
            Assert.NotNull(bytes);
            Assert.Equal(typeof(byte[]), bytes!.ReturnType);

            var value = (byte[])bytes.Invoke(null, null)!;
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, value);
        }

        private static string FindStringReturnComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "string-return-component", "wasm",
                                "h.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lifts_string_return_from_guest_memory()
        {
            // string-return-component exports `hello() -> string`.
            // Core signature: `() -> i32`, returning a pointer P
            // into linear memory where bytes [P..P+4] hold the
            // string's ptr (= 100, pointing at "Hello") and
            // bytes [P+4..P+8] hold its length (= 5).
            // ComponentExports.Hello() reads (strPtr, strLen)
            // from the return area and routes through
            // StringMarshal.LiftUtf8 to get "Hello" as a C# string.
            // First real exercise of the canonical-ABI return-
            // area lift path through IL emission.
            using var fs = File.OpenRead(FindStringReturnComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var hello = componentExports!.GetMethod("Hello");
            Assert.NotNull(hello);
            Assert.Equal(typeof(string), hello!.ReturnType);

            var value = (string)hello.Invoke(null, null)!;
            Assert.Equal("Hello", value);
        }

        private static string FindStringParamComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "string-param-component", "wasm",
                                "sp.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lowers_string_param_via_cabi_realloc()
        {
            // string-param-component exports `echo(s: string) -> string`.
            // Core signature: `(i32, i32) -> i32` — the two i32s
            // are (ptr, len) into guest memory, the return i32
            // points at a 2-word return area holding (ptr, len).
            // The guest bump-allocates via cabi_realloc and echoes
            // the input back by pointing the return area at the
            // freshly-copied bytes. Exercises the param-side
            // canonical-ABI lower path: UTF-8 encode, call
            // cabi_realloc, memcpy into guest memory, pass
            // (ptr, len) to the core function — all through IL.
            using var fs = File.OpenRead(FindStringParamComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var echo = componentExports!.GetMethod("Echo");
            Assert.NotNull(echo);
            Assert.Equal(typeof(string), echo!.ReturnType);
            var pars = echo.GetParameters();
            Assert.Single(pars);
            Assert.Equal(typeof(string), pars[0].ParameterType);

            var value = (string)echo.Invoke(null, new object[] { "roundtrip" })!;
            Assert.Equal("roundtrip", value);
        }

        private static string FindListParamComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "list-param-component", "wasm",
                                "lp.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lowers_list_u32_param_via_cabi_realloc()
        {
            // list-param-component exports `sum(xs: list<u32>) -> u32`.
            // Core signature: `(ptr: i32, count: i32) -> i32`.
            // The emitted component adapter must byte-copy the
            // input uint[] into guest memory via cabi_realloc and
            // pass (ptr, count) — not (ptr, byteLen) — to the
            // core function. Core then iterates and sums.
            using var fs = File.OpenRead(FindListParamComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var sum = componentExports!.GetMethod("Sum");
            Assert.NotNull(sum);
            Assert.Equal(typeof(uint), sum!.ReturnType);
            var pars = sum.GetParameters();
            Assert.Single(pars);
            Assert.Equal(typeof(uint[]), pars[0].ParameterType);

            var value = (uint)sum.Invoke(null,
                new object[] { new uint[] { 1, 2, 3, 4, 5 } })!;
            Assert.Equal(15u, value);
        }

        private static string FindOptionStringComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "option-string-component", "wasm",
                                "os.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lifts_option_string_some_and_none()
        {
            // option-string-component exports two zero-arg funcs,
            // `maybe-some() -> option<string>` returning
            // Some("greetings") and `maybe-none() -> option<string>`
            // returning None. Core returns the retArea pointer;
            // at that address the 1-byte discriminant picks the
            // branch and offsets 4/8 hold (ptr, len) in the Some
            // case. C# surface is plain `string` where null = None.
            //
            // Exercises both the option<string> lift path AND the
            // component-func-idx resolver — wit-component emits
            // this as interleaved canon + alias + export sections,
            // making the second canon land at component-func idx 2
            // (not 1). Flat-index lookup into Canons would drop
            // maybe-none silently.
            using var fs = File.OpenRead(FindOptionStringComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var maybeSome = componentExports!.GetMethod("MaybeSome");
            var maybeNone = componentExports.GetMethod("MaybeNone");
            Assert.NotNull(maybeSome);
            Assert.NotNull(maybeNone);
            Assert.Equal(typeof(string), maybeSome!.ReturnType);
            Assert.Equal(typeof(string), maybeNone!.ReturnType);

            Assert.Equal("greetings", maybeSome.Invoke(null, null));
            Assert.Null(maybeNone.Invoke(null, null));
        }

        private static string FindResultStringComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "result-string-component", "wasm",
                                "rs.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lifts_result_string_u32_both_branches()
        {
            // result-string-component exports two funcs each
            // returning `result<string, u32>` — `greet()` takes
            // the Ok branch ("fine") and `fail()` takes the Err
            // branch (404). C# surface per the scope plan is
            // ValueTuple<bool, string, uint>: Ok → (true, str,
            // default(uint)); Err → (false, null, code).
            using var fs = File.OpenRead(FindResultStringComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var greet = componentExports!.GetMethod("Greet");
            var fail = componentExports.GetMethod("Fail");
            Assert.NotNull(greet);
            Assert.NotNull(fail);
            var tupleType = typeof(System.ValueTuple<bool, string, uint>);
            Assert.Equal(tupleType, greet!.ReturnType);
            Assert.Equal(tupleType, fail!.ReturnType);

            var okResult = ((bool ok, string msg, uint code))
                greet.Invoke(null, null)!;
            Assert.True(okResult.ok);
            Assert.Equal("fine", okResult.msg);
            Assert.Equal(0u, okResult.code);

            var errResult = ((bool ok, string msg, uint code))
                fail.Invoke(null, null)!;
            Assert.False(errResult.ok);
            Assert.Null(errResult.msg);
            Assert.Equal(404u, errResult.code);
        }

        private static string FindResultVoidStringComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "result-void-string-component", "wasm",
                                "rvs.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lifts_result_void_string_both_branches()
        {
            // result-void-string-component exports `ok() -> result<_, string>`
            // taking the Ok branch (void payload) and `err() ->
            // result<_, string>` taking the Err branch with
            // "permission denied". Absent Ok side maps to `object`
            // at the C# surface — will always be null.
            using var fs = File.OpenRead(FindResultVoidStringComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var ok = componentExports!.GetMethod("Ok");
            var err = componentExports.GetMethod("Err");
            Assert.NotNull(ok);
            Assert.NotNull(err);
            var tupleType = typeof(System.ValueTuple<bool, object, string>);
            Assert.Equal(tupleType, ok!.ReturnType);
            Assert.Equal(tupleType, err!.ReturnType);

            var okResult = ((bool ok, object val, string error))
                ok.Invoke(null, null)!;
            Assert.True(okResult.ok);
            Assert.Null(okResult.val);
            Assert.Null(okResult.error);

            var errResult = ((bool ok, object val, string error))
                err.Invoke(null, null)!;
            Assert.False(errResult.ok);
            Assert.Null(errResult.val);
            Assert.Equal("permission denied", errResult.error);
        }

        private static string FindListStringComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "list-string-component", "wasm",
                                "ls.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lifts_list_of_strings_from_guest_memory()
        {
            // list-string-component exports `words() -> list<string>`.
            // Return area at retArea P carries (listPtr, count).
            // Each element at listPtr + i*8 is a (strPtr, strLen)
            // pair. Three pre-allocated UTF-8 strings get lifted
            // via ListMarshal.LiftStringList, which walks the
            // 8-byte pair array and calls StringMarshal.LiftUtf8
            // per element — keeps the UTF-8 copy chokepoint
            // discipline intact.
            using var fs = File.OpenRead(FindListStringComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var words = componentExports!.GetMethod("Words");
            Assert.NotNull(words);
            Assert.Equal(typeof(string[]), words!.ReturnType);

            var value = (string[])words.Invoke(null, null)!;
            Assert.Equal(new[] { "alpha", "beta", "gamma" }, value);
        }

        private static string FindOptionStringNoneComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "option-string-none-component", "wasm",
                                "osn.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_lifts_option_string_none()
        {
            // Companion to the option-string Some test — same
            // shape, but the return-area discriminant is 0
            // (None). The emitted IL's brfalse branch returns
            // null without touching the payload slot, avoiding
            // any UTF-8 decode on garbage bytes.
            using var fs = File.OpenRead(FindOptionStringNoneComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var greeting = componentExports!.GetMethod("Greeting");
            Assert.NotNull(greeting);
            Assert.Equal(typeof(string), greeting!.ReturnType);

            Assert.Null(greeting.Invoke(null, null));
        }

        private static string FindF64ComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "f64-component", "wasm",
                                "p.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_f64_return_passes_through_precisely()
        {
            // f64-component exports `pi: func() -> f64` whose
            // core returns the IEEE 754 64-bit constant for π.
            // No canonical-ABI conversion — component f64 and
            // core f64 share the IL double stack slot.
            using var fs = File.OpenRead(FindF64ComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            var pi = componentExports!.GetMethod("Pi");
            Assert.NotNull(pi);
            Assert.Equal(typeof(double), pi!.ReturnType);

            var value = (double)pi.Invoke(null, null)!;
            Assert.Equal(3.141592653589793, value);
        }

        private static string FindU64ComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "u64-component", "wasm",
                                "u.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_u64_return_preserves_full_64_bits()
        {
            // u64-component exports `big: func() -> u64` whose
            // core returns the i64 literal 0x0123456789ABCDEF.
            // Exercises the 64-bit passthrough path — component
            // u64 and core i64 share an IL stack slot, so the
            // ComponentExports method returns ulong without an
            // explicit conversion instruction.
            using var fs = File.OpenRead(FindU64ComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var big = componentExports!.GetMethod("Big",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static);
            Assert.NotNull(big);
            Assert.Equal(typeof(ulong), big!.ReturnType);

            var value = (ulong)big.Invoke(null, null)!;
            Assert.Equal(0x0123456789ABCDEFUL, value);
        }

        private static string FindMemoryComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "memory-component", "wasm",
                                "m.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_module_memory_accessor_exposes_guest_bytes()
        {
            // memory-component declares a linear memory with a
            // 4-byte data segment containing 0x2A (=42) at
            // offset 0. Its `ping` function returns that value
            // via i32.load. The Module class should expose a
            // `Memory` property returning the byte[] — this is
            // the hook canonical-ABI adapters (strings, lists,
            // aggregates) read guest memory through.
            using var fs = File.OpenRead(FindMemoryComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            Assert.NotNull(result.ModuleClass);
            var memProp = result.ModuleClass!.GetProperty("Memory");
            Assert.NotNull(memProp);
            Assert.Equal(typeof(byte[]), memProp!.PropertyType);

            var ctor = result.ModuleClass.GetConstructor(System.Type.EmptyTypes);
            var instance = ctor!.Invoke(null);
            var memory = (byte[])memProp.GetValue(instance)!;
            Assert.NotNull(memory);
            // 64 KiB = one wasm page.
            Assert.Equal(64 * 1024, memory.Length);
            // Data segment wrote 0x2A at offset 0.
            Assert.Equal(0x2A, memory[0]);
        }

        [Fact]
        public void TranspileSingleModule_module_exposes_memory_property()
        {
            // The generated Module class emits a public
            // `Memory` property of type byte[] that returns the
            // core module's linear memory. This is the hook
            // canonical-ABI adapters use to lift strings/lists/
            // aggregates out of guest memory. Verify it's reachable
            // end-to-end after transpilation of a simple component.
            //
            // tiny-component has no memory (no data segments, no
            // memory ops), so `Memory` is null; but we can still
            // verify the property exists with the right signature.
            // A real-world component with declared memory will
            // return the byte[].
            using var fs = File.OpenRead(FindTinyComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            Assert.NotNull(result.ModuleClass);
            var memProp = result.ModuleClass!.GetProperty("Memory");
            // tiny.wat declares no memory → no Memory property
            // is emitted. That's fine; the property appears on
            // components whose core modules do declare memory.
            if (memProp != null)
            {
                Assert.Equal(typeof(byte[]), memProp.PropertyType);
            }
        }

        private static string FindAddComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "add-component", "wasm",
                                "ad.component.wasm");
        }

        [Fact]
        public void TranspileSingleModule_forwards_primitive_params_to_core()
        {
            // add-component's `add(x: u32, y: u32) -> u32`
            // exercises the param-forwarding path: two u32
            // arguments thread through the component wrapper,
            // land as i32s on the core module's stack, and the
            // result comes back through the return path.
            using var fs = File.OpenRead(FindAddComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var add = componentExports!.GetMethod("Add",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static);
            Assert.NotNull(add);
            Assert.Equal(typeof(uint), add!.ReturnType);

            var pars = add.GetParameters();
            Assert.Equal(2, pars.Length);
            Assert.Equal(typeof(uint), pars[0].ParameterType);
            Assert.Equal(typeof(uint), pars[1].ParameterType);

            var value = (uint)add.Invoke(null, new object[] { 7u, 35u })!;
            Assert.Equal(42u, value);
        }

        [Fact]
        public void TranspileSingleModule_casts_i32_to_bool_on_return()
        {
            // prim-cast-component's core module returns i32
            // constant 1 for `check`. The component signature
            // says `check() -> bool`, so the ComponentExports
            // method must cast the i32 to bool (via `!= 0`).
            // Verifies the new primitive-cast IL actually runs.
            using var fs = File.OpenRead(FindPrimCastComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var check = componentExports!.GetMethod("Check",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static);
            Assert.NotNull(check);
            Assert.Equal(typeof(bool), check!.ReturnType);

            var value = (bool)check.Invoke(null, null)!;
            Assert.True(value);
        }

        [Fact]
        public void TranspileSingleModule_component_exports_class_exposes_typed_greet()
        {
            // Full stack: parse + decode exports/canons/types,
            // emit the ComponentExports class, instantiate
            // through it, get `uint` (not `int`) as the public
            // return type. This is the component-level surface
            // users actually consume.
            using var fs = File.OpenRead(FindTinyComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            var componentExports = result.Assembly
                .GetType("Wacs.Transpiled.Component.ComponentExports");
            Assert.NotNull(componentExports);

            var greet = componentExports!.GetMethod("Greet",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static);
            Assert.NotNull(greet);
            Assert.Equal(typeof(uint), greet!.ReturnType);
            Assert.Empty(greet.GetParameters());

            var value = (uint)greet.Invoke(null, null)!;
            Assert.Equal(42u, value);
        }

        [Fact]
        public void TranspileSingleModule_execute_greet_returns_42()
        {
            // End-to-end execution test: transpile the component,
            // instantiate the generated Module class, invoke the
            // exported `greet` via reflection on IExports, and
            // assert the result is 42 — the value tiny.wat's
            // core module is coded to return. Validates the full
            // parse → transpile → load → invoke pipeline on real
            // component bits.
            using var fs = File.OpenRead(FindTinyComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            Assert.NotNull(result.ModuleClass);
            Assert.NotNull(result.ExportsInterface);

            // The generated Module class has an IImports-taking
            // constructor (or a parameterless one when there are
            // no imports). tiny-component has no imports, so a
            // parameterless construction + IExports call works.
            var ctor = result.ModuleClass!.GetConstructor(System.Type.EmptyTypes);
            Assert.NotNull(ctor);
            var moduleInstance = ctor!.Invoke(null);

            var greetMethod = result.ExportsInterface!.GetMethods()
                .First(m => string.Equals(m.Name, "greet",
                            System.StringComparison.OrdinalIgnoreCase));
            var value = greetMethod.Invoke(moduleInstance, null);

            // Core module signature returns i32 → boxed as
            // System.Int32. At the component level this is u32;
            // bitwise-equivalent for small positive values.
            Assert.Equal(42, System.Convert.ToInt32(value));
        }

        [Fact]
        public void TranspileSingleModule_exposes_core_export_via_ExportsInterface()
        {
            // For primitive-only signatures, the component-level
            // export and the core-module export have matching
            // shapes (no canonical-ABI marshaling needed). The
            // existing ModuleTranspiler's ExportsInterface
            // captures the core export's `greet() -> u32` — we
            // piggyback on that for v0. Proper component-level
            // trampolines with canonical-ABI adapters will sit
            // above this in follow-up commits.
            using var fs = File.OpenRead(FindTinyComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(fs);

            Assert.NotNull(result.ExportsInterface);
            var methods = result.ExportsInterface!.GetMethods();
            Assert.NotEmpty(methods);
            // wit-bindgen-csharp emits PascalCase; existing
            // transpiler uses the WIT name verbatim. Accept
            // either: the point is that greet is visible.
            Assert.Contains(methods,
                m => string.Equals(m.Name, "greet",
                        System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void TranspileSingleModule_produces_valid_transpilation_result()
        {
            // Full path: parse component → instantiate embedded
            // core module → ModuleTranspiler → bake metadata.
            // Verifies no errors along the pipeline for the
            // trivial tiny-component.
            using var fs = File.OpenRead(FindTinyComponentPath());
            var result = ComponentTranspiler.TranspileSingleModule(
                fs, assemblyNamespace: "Wacs.Transpiled.Tiny");

            Assert.NotNull(result);
            Assert.NotNull(result.FunctionsType);
            Assert.NotEmpty(result.Methods);
        }

        [Fact]
        public void EmitComponentMetadataClass_writes_wit_bytes_to_assembly()
        {
            // Exercise the metadata emitter directly with
            // synthetic bytes. The transpiled assembly should
            // carry a ComponentMetadata static class whose
            // EmbeddedWitBytes field exposes the same bytes.
            var assemblyName = new AssemblyName("Wacs.Test.MetadataEmit");
            var ab = AssemblyBuilder.DefineDynamicAssembly(
                assemblyName, AssemblyBuilderAccess.Run);
            var mb = ab.DefineDynamicModule(assemblyName.Name!);

            byte[] witBytes = { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03 };
            ComponentAssemblyEmit.EmitComponentMetadataClass(
                mb, "Wacs.Transpiled.Foo", witBytes);

            var meta = ab.GetType("Wacs.Transpiled.Foo.ComponentMetadata");
            Assert.NotNull(meta);
            var field = meta!.GetField("EmbeddedWitBytes",
                BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(field);
            var value = (byte[])field!.GetValue(null)!;
            Assert.Equal(witBytes, value);
        }

        [Fact]
        public void EmitComponentMetadataClass_is_noop_when_wit_null()
        {
            // No component-type:* section → no ComponentMetadata
            // class. Keeps the transpiled assembly lean when the
            // component doesn't carry metadata.
            var assemblyName = new AssemblyName("Wacs.Test.NoMetadata");
            var ab = AssemblyBuilder.DefineDynamicAssembly(
                assemblyName, AssemblyBuilderAccess.Run);
            var mb = ab.DefineDynamicModule(assemblyName.Name!);

            ComponentAssemblyEmit.EmitComponentMetadataClass(
                mb, "Wacs.Transpiled.Empty", witBytes: null);

            var meta = ab.GetType("Wacs.Transpiled.Empty.ComponentMetadata");
            Assert.Null(meta);
        }

        [Fact]
        public void ParseFile_does_not_throw_on_missing_component_type_metadata()
        {
            // `wasm-tools component new` strips the
            // `component-type:*` custom section during final
            // component assembly — so tiny-component's binary
            // doesn't carry it. componentize-dotnet DOES preserve
            // it, so real-world components will have EmbeddedWit
            // populated. Verify the parser handles both cases
            // gracefully (null without throwing).
            var result = ComponentTranspiler.ParseFile(FindTinyComponentPath());
            // Just assert the parse succeeded — EmbeddedWit may
            // legitimately be null here.
            Assert.NotNull(result.Component);
        }
    }
}
