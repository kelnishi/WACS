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
