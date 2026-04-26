// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Component binary parser — preamble recognition + section
    /// enumeration. Exercised against tiny-component, a
    /// hand-built component produced by
    /// <c>wasm-tools component new</c> over a trivial
    /// <c>greet() -&gt; u32</c> world. That binary contains
    /// exactly the structure the parser needs to recognize:
    /// component preamble + embedded core module + canon/type/
    /// export sections.
    /// </summary>
    public class ComponentBinaryParserTests
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
        public void IsComponentHeader_recognises_component_preamble()
        {
            var path = FindTinyComponentPath();
            var header = File.ReadAllBytes(path).AsSpan(0, 8);
            Assert.True(ComponentBinaryParser.IsComponentHeader(header));
        }

        [Fact]
        public void IsComponentHeader_rejects_core_module_preamble()
        {
            // Core module preamble: \0asm + version=1 + layer=0.
            byte[] coreHeader = { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };
            Assert.False(ComponentBinaryParser.IsComponentHeader(coreHeader));
        }

        [Fact]
        public void IsComponentHeader_rejects_short_buffer()
        {
            byte[] tooShort = { 0x00, 0x61, 0x73, 0x6D };
            Assert.False(ComponentBinaryParser.IsComponentHeader(tooShort));
        }

        [Fact]
        public void Parse_tiny_component_yields_expected_sections()
        {
            var path = FindTinyComponentPath();
            using var stream = File.OpenRead(path);
            var component = ComponentBinaryParser.Parse(stream);

            // Sanity: at least one core module (the embedded
            // greet-returns-42 wasm) + at least a type + export
            // section for the component-level contract.
            Assert.True(component.CoreModuleCount >= 1,
                $"Expected at least 1 core module, got {component.CoreModuleCount}");

            var ids = component.RawSections.Select(s => s.Id).ToArray();
            Assert.Contains(ComponentSectionId.CoreModule, ids);
            Assert.Contains(ComponentSectionId.Type, ids);
            Assert.Contains(ComponentSectionId.Export, ids);

            // Every section has a non-negative payload size — the
            // parser correctly pulled the full byte range.
            Assert.All(component.RawSections,
                s => Assert.True(s.Size >= 0));
        }

        [Fact]
        public void CoreModuleBinaries_feed_directly_into_Wacs_Core_parser()
        {
            // The component's embedded core module section's
            // payload IS a complete core wasm binary — can be
            // parsed by Wacs.Core's Module.ParseWasm without
            // further adaptation. This is the gateway the
            // transpiler uses: one component → one-or-more core
            // modules → per-module transpilation.
            var path = FindTinyComponentPath();
            using var stream = File.OpenRead(path);
            var component = ComponentBinaryParser.Parse(stream);

            var coreBytes = component.CoreModuleBinaries.Single();
            using var coreStream = new MemoryStream(coreBytes);
            var coreModule = Wacs.Core.BinaryModuleParser.ParseWasm(coreStream);

            // tiny.wat defines one export: `greet`. Post-parse the
            // core module should expose that export among its
            // function list (after module finalization).
            Assert.NotNull(coreModule);
            Assert.NotEmpty(coreModule.Exports);
            Assert.Contains(coreModule.Exports, e => e.Name == "greet");
        }

        [Fact]
        public void CustomSections_decode_producers_metadata()
        {
            // tiny.component.wasm carries a `producers` custom
            // section written by wasm-tools. Verify we can read
            // it back as name + raw bytes.
            var path = FindTinyComponentPath();
            using var stream = File.OpenRead(path);
            var component = ComponentBinaryParser.Parse(stream);

            var customs = component.CustomSections.ToArray();
            Assert.NotEmpty(customs);
            Assert.Contains(customs, c => c.Name == "producers");
        }

        [Fact]
        public void Types_decode_greet_function_signature_as_void_to_u32()
        {
            // tiny-component's type section declares one type:
            // a function with no params returning u32 — the
            // component-level signature of `greet: func() -> u32`.
            var path = FindTinyComponentPath();
            using var stream = File.OpenRead(path);
            var component = ComponentBinaryParser.Parse(stream);

            var entry = Assert.Single(component.Types);
            var fn = Assert.IsType<ComponentFuncType>(entry);
            Assert.Empty(fn.Params);
            var result = Assert.Single(fn.Results);
            Assert.True(result.IsPrimitive);
            Assert.Equal(ComponentPrim.U32, result.Prim);
        }

        [Fact]
        public void Canons_decode_greet_lift_pointing_at_core_func_0()
        {
            // tiny-component's canon section declares one lift:
            // component function 0 is the lift of core function 0
            // (the `greet` export in the embedded core module).
            // With no string / realloc params, the option vec is
            // empty, and the type ascription points at the
            // component's single function type `() -> u32`.
            var path = FindTinyComponentPath();
            using var stream = File.OpenRead(path);
            var component = ComponentBinaryParser.Parse(stream);

            var entry = Assert.Single(component.Canons);
            var lift = Assert.IsType<CanonLift>(entry);
            Assert.Equal(0u, lift.CoreFuncIdx);
            Assert.Equal(0u, lift.TypeIdx);
            Assert.Empty(lift.Options);
        }

        [Fact]
        public void Exports_decode_component_level_greet_export()
        {
            // tiny-component's export section declares one
            // entry: `greet` pointing at component-func index 0.
            // The lift through canon wires it back to the core
            // module's greet function.
            var path = FindTinyComponentPath();
            using var stream = File.OpenRead(path);
            var component = ComponentBinaryParser.Parse(stream);

            var export = Assert.Single(component.Exports);
            Assert.Equal("greet", export.Name);
            Assert.Equal(ComponentSort.Func, export.Sort);
            Assert.Equal(0u, export.Index);
        }

        private static string FindNestedComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "nested-component", "wasm",
                                "nested.component.wasm");
        }

        [Fact]
        public void Parse_recognises_nested_component_section()
        {
            // nested-component fixture: outer component embeds
            // one inner component (section id=4) that defines
            // `inner-greet`, then uses instance + alias sections
            // to re-export it as `greet` at the outer level. The
            // parser surfaces nested components via NestedComponents
            // / NestedComponentBinaries; recursive parsing turns
            // each into a fully-functional ComponentModule.
            var path = FindNestedComponentPath();
            using var stream = File.OpenRead(path);
            var component = ComponentBinaryParser.Parse(stream);

            Assert.Equal(1, component.NestedComponentCount);
            var inner = Assert.Single(component.NestedComponents);

            // The outer has zero embedded core modules — the core
            // wasm lives entirely inside the nested component.
            Assert.Equal(0, component.CoreModuleCount);
            Assert.Equal(1, inner.CoreModuleCount);

            // The inner exposes its own canon-lifted export,
            // which the outer aliases up + re-exports as `greet`.
            Assert.Contains(inner.Exports, e => e.Name == "inner-greet");
            Assert.Contains(component.Exports, e => e.Name == "greet");
        }

        [Fact]
        public void Parse_surfaces_nested_instance_and_alias_targets()
        {
            // nested-component fixture wires the outer's `greet`
            // export through (instantiate component 0) +
            // (alias export 0 "inner-greet" (func)). Verifies the
            // newly-surfaced Instances + Aliases lists carry the
            // exact info downstream consumers need to resolve
            // outer exports through the composition tree.
            var path = FindNestedComponentPath();
            using var stream = File.OpenRead(path);
            var component = ComponentBinaryParser.Parse(stream);

            // (instance (instantiate 0)) — one InstantiateComponent
            // entry pointing at component-idx 0 with no args.
            var instance = Assert.Single(component.Instances);
            var inst = Assert.IsType<InstantiateComponent>(instance);
            Assert.Equal(0u, inst.ComponentIdx);
            Assert.Empty(inst.Args);

            // (alias export 0 "inner-greet" (func)) — one alias
            // entry of sort=Func targeting instance 0's
            // "inner-greet" export.
            var alias = Assert.Single(component.Aliases);
            Assert.Equal(AliasSort.Func, alias.Sort);
            Assert.Equal(AliasTargetKind.ComponentInstanceExport,
                alias.TargetKind);
            Assert.Equal(0u, alias.InstanceIdx);
            Assert.Equal("inner-greet", alias.ExportName);
        }

        private static string FindWasiRandomBytesComponentPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", "wasi-random-bytes-component", "wasm",
                                "rb.component.wasm");
        }

        [Fact]
        public void Parse_decodes_multi_core_module_composition()
        {
            // wit-component generates 3 core modules + a chain
            // of core-instance entries (instantiate + inline-
            // export bundles) for any component that imports an
            // aggregate-typed function. Verifies the new
            // CoreInstanceSectionReader handles both forms — the
            // 0x00 (instantiate moduleidx + with-clause args)
            // and 0x01 (inline-export bundle) — without
            // crashing on real wit-component output.
            var path = FindWasiRandomBytesComponentPath();
            using var stream = File.OpenRead(path);
            var component = ComponentBinaryParser.Parse(stream);

            // Spec.Test/components/fixtures/wasi-random-bytes-component:
            // 3 core modules (wit-component adapter + post-
            // return + user's core wasm).
            Assert.True(component.CoreModuleCount >= 3,
                $"Expected ≥3 core modules, got {component.CoreModuleCount}");

            // Multiple core-instance entries — wit-component's
            // typical instantiate/inline-export interleaving.
            Assert.True(component.CoreInstances.Count >= 3,
                $"Expected ≥3 core-instance entries, got "
                + component.CoreInstances.Count);

            // Both forms appear in real output: at least one
            // InstantiateCoreModule + at least one
            // InstantiateCoreInline.
            bool hasInstantiate = false, hasInline = false;
            foreach (var e in component.CoreInstances)
            {
                if (e is InstantiateCoreModule) hasInstantiate = true;
                if (e is InstantiateCoreInline) hasInline = true;
            }
            Assert.True(hasInstantiate,
                "Expected at least one InstantiateCoreModule entry");
            Assert.True(hasInline,
                "Expected at least one InstantiateCoreInline entry");

            // The instantiate entries' with-clauses target the
            // host-imported namespace; one of them should name
            // the wasi:random/random@0.2.3 instance.
            bool foundWasiArg = false;
            foreach (var e in component.CoreInstances)
            {
                if (e is InstantiateCoreModule ic)
                    foreach (var arg in ic.Args)
                        if (arg.Name.Contains("wasi:random/random"))
                        { foundWasiArg = true; break; }
            }
            Assert.True(foundWasiArg,
                "Expected an instantiate-arg targeting wasi:random/random");
        }

        [Fact]
        public void Parse_rejects_truncated_header()
        {
            // Valid magic + version but layer=0 → core module,
            // not a component. Parser should refuse rather than
            // silently treat as component.
            byte[] coreBytes = { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };
            using var stream = new MemoryStream(coreBytes);
            Assert.Throws<System.FormatException>(
                () => ComponentBinaryParser.Parse(stream));
        }
    }
}
