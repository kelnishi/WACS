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
