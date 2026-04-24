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
