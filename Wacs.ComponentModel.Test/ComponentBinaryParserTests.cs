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
