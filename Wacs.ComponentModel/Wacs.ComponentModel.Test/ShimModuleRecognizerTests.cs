// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Wacs.ComponentModel.Async;
using Wacs.Core;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Phase 3 Slice G3 coverage: <see cref="ShimModuleRecognizer"/>
    /// detects wit-component's canon-async shim by name section,
    /// extracts the (funcIdx → debug-name) map, and normalizes
    /// dotted spelling to wasmtime kebab.
    ///
    /// <para>Fixtures are minimal hand-built wasm binaries (just
    /// magic + version + custom name section) parsed via the
    /// public <see cref="BinaryModuleParser"/> API. That
    /// exercises the real name-section parser end-to-end without
    /// needing a full wit-component output.</para>
    /// </summary>
    public class ShimModuleRecognizerTests
    {
        // Build a minimal wasm binary with a name custom section
        // containing the supplied module name + function-name map.
        // Returns the parsed Module.
        private static Module MakeModuleWithNames(
            string moduleName, Dictionary<uint, string> funcNames)
        {
            var bytes = BuildWasmWithNameSection(moduleName, funcNames);
            // ParseCustomNames is opt-in; this is exactly the
            // scenario the recognizer needs it for.
            var prevParseNames = BinaryModuleParser.ParseCustomNames;
            BinaryModuleParser.ParseCustomNames = true;
            try
            {
                using var ms = new MemoryStream(bytes);
                return BinaryModuleParser.ParseWasm(ms);
            }
            finally
            {
                BinaryModuleParser.ParseCustomNames = prevParseNames;
            }
        }

        // Build the wasm wire bytes for: magic + version + custom
        // "name" section { ModuleName subsection, FuncName
        // subsection }. Conforms to the core-wasm spec §5.5.13.
        private static byte[] BuildWasmWithNameSection(
            string moduleName, Dictionary<uint, string> funcNames)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            // Magic + version (core-wasm).
            w.Write(new byte[] { 0x00, 0x61, 0x73, 0x6D });
            w.Write(new byte[] { 0x01, 0x00, 0x00, 0x00 });

            // Build the custom-section body in a buffer first so
            // we know its size.
            using var bodyMs = new MemoryStream();
            using var bodyW = new BinaryWriter(bodyMs);
            // Section name: "name" (length-prefixed UTF-8).
            WriteName(bodyW, "name");

            // ModuleName subsection (id=0).
            using var modSubMs = new MemoryStream();
            using var modSubW = new BinaryWriter(modSubMs);
            WriteName(modSubW, moduleName);
            WriteSubsection(bodyW, subId: 0, modSubMs.ToArray());

            // FuncName subsection (id=1): vec((funcIdx, name)).
            if (funcNames.Count > 0)
            {
                using var fnSubMs = new MemoryStream();
                using var fnSubW = new BinaryWriter(fnSubMs);
                WriteLeb128U32(fnSubW, (uint)funcNames.Count);
                foreach (var (idx, name) in funcNames.OrderBy(kv => kv.Key))
                {
                    WriteLeb128U32(fnSubW, idx);
                    WriteName(fnSubW, name);
                }
                WriteSubsection(bodyW, subId: 1, fnSubMs.ToArray());
            }

            var bodyBytes = bodyMs.ToArray();
            w.Write((byte)0x00); // Custom section id.
            WriteLeb128U32(w, (uint)bodyBytes.Length);
            w.Write(bodyBytes);
            return ms.ToArray();
        }

        private static void WriteName(BinaryWriter w, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            WriteLeb128U32(w, (uint)bytes.Length);
            w.Write(bytes);
        }

        private static void WriteSubsection(
            BinaryWriter w, byte subId, byte[] body)
        {
            w.Write(subId);
            WriteLeb128U32(w, (uint)body.Length);
            w.Write(body);
        }

        private static void WriteLeb128U32(BinaryWriter w, uint value)
        {
            do
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value != 0) b |= 0x80;
                w.Write(b);
            } while (value != 0);
        }

        // Build a wasm binary with a Type section + Import section
        // (one func import per imported (module, name) pair) and an
        // optional name section. Used for testing the structural
        // shim recognizer.
        private static byte[] BuildWasmWithImports(
            (string Module, string Name)[] imports,
            string? moduleName = null,
            Dictionary<uint, string>? funcNames = null)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(new byte[] { 0x00, 0x61, 0x73, 0x6D });
            w.Write(new byte[] { 0x01, 0x00, 0x00, 0x00 });

            // Type section (id=1): one void→void func type.
            using var typeMs = new MemoryStream();
            using var typeW = new BinaryWriter(typeMs);
            WriteLeb128U32(typeW, 1);    // 1 type entry
            typeW.Write((byte)0x60);     // func type tag
            WriteLeb128U32(typeW, 0);    // 0 params
            WriteLeb128U32(typeW, 0);    // 0 results
            WriteSection(w, sectionId: 1, body: typeMs.ToArray());

            // Import section (id=2).
            using var impMs = new MemoryStream();
            using var impW = new BinaryWriter(impMs);
            WriteLeb128U32(impW, (uint)imports.Length);
            foreach (var (mod, name) in imports)
            {
                WriteName(impW, mod);
                WriteName(impW, name);
                impW.Write((byte)0x00);   // import kind = func
                WriteLeb128U32(impW, 0);  // type index = 0
            }
            WriteSection(w, sectionId: 2, body: impMs.ToArray());

            // Optional custom "name" section.
            if (moduleName != null || (funcNames?.Count ?? 0) > 0)
            {
                using var nameMs = new MemoryStream();
                using var nameW = new BinaryWriter(nameMs);
                WriteName(nameW, "name");
                if (moduleName != null)
                {
                    using var modMs = new MemoryStream();
                    using var modW = new BinaryWriter(modMs);
                    WriteName(modW, moduleName);
                    WriteSubsection(nameW, subId: 0, modMs.ToArray());
                }
                if (funcNames is { Count: > 0 })
                {
                    using var fnMs = new MemoryStream();
                    using var fnW = new BinaryWriter(fnMs);
                    WriteLeb128U32(fnW, (uint)funcNames.Count);
                    foreach (var (idx, nm) in funcNames.OrderBy(kv => kv.Key))
                    {
                        WriteLeb128U32(fnW, idx);
                        WriteName(fnW, nm);
                    }
                    WriteSubsection(nameW, subId: 1, fnMs.ToArray());
                }
                WriteSection(w, sectionId: 0, body: nameMs.ToArray());
            }

            return ms.ToArray();
        }

        private static void WriteSection(BinaryWriter w, byte sectionId, byte[] body)
        {
            w.Write(sectionId);
            WriteLeb128U32(w, (uint)body.Length);
            w.Write(body);
        }

        private static Module ParseModule(byte[] bytes)
        {
            var prev = BinaryModuleParser.ParseCustomNames;
            BinaryModuleParser.ParseCustomNames = true;
            try
            {
                using var ms = new MemoryStream(bytes);
                return BinaryModuleParser.ParseWasm(ms);
            }
            finally
            {
                BinaryModuleParser.ParseCustomNames = prev;
            }
        }

        // ---- Module-name detection -------------------------------------

        [Fact]
        public void IsShimModule_recognizes_wit_component_shim_by_name()
        {
            var m = MakeModuleWithNames(
                ShimModuleRecognizer.ShimModuleName,
                new Dictionary<uint, string>());
            Assert.True(ShimModuleRecognizer.IsShimModule(m));
        }

        [Fact]
        public void IsShimModule_rejects_other_module_names()
        {
            var m = MakeModuleWithNames(
                "my-app", new Dictionary<uint, string>());
            Assert.False(ShimModuleRecognizer.IsShimModule(m));
        }

        [Fact]
        public void IsShimModule_returns_false_for_unnamed_module()
        {
            // A module whose name section is absent (or whose
            // module-name subsection is missing). Build one with
            // a function-name subsection only — the name section
            // exists but ModuleName is null.
            var bytes = BuildWasmWithNameSection(
                moduleName: string.Empty,
                funcNames: new Dictionary<uint, string> { { 0, "x" } });
            var prevParseNames = BinaryModuleParser.ParseCustomNames;
            BinaryModuleParser.ParseCustomNames = true;
            try
            {
                using var ms = new MemoryStream(bytes);
                var m = BinaryModuleParser.ParseWasm(ms);
                // Module-name subsection was written with empty
                // string — not "wit-component:shim" — so reject.
                Assert.False(ShimModuleRecognizer.IsShimModule(m));
            }
            finally
            {
                BinaryModuleParser.ParseCustomNames = prevParseNames;
            }
        }

        [Fact]
        public void IsShimModule_returns_false_for_null()
        {
            Assert.False(ShimModuleRecognizer.IsShimModule(null!));
        }

        // ---- Debug-name extraction + normalization ---------------------

        [Fact]
        public void NormalizeDebugName_converts_dots_to_dashes()
        {
            Assert.Equal("task-return",
                ShimModuleRecognizer.NormalizeDebugName("task.return"));
            Assert.Equal("waitable-set-wait",
                ShimModuleRecognizer.NormalizeDebugName("waitable-set.wait"));
            Assert.Equal("error-context-debug-message",
                ShimModuleRecognizer.NormalizeDebugName(
                    "error-context.debug-message"));
        }

        [Fact]
        public void NormalizeDebugName_preserves_already_kebab_names()
        {
            // wit-component might emit either form depending on
            // version; the normalizer should be idempotent.
            Assert.Equal("stream-new",
                ShimModuleRecognizer.NormalizeDebugName("stream-new"));
        }

        [Fact]
        public void NormalizeDebugName_handles_empty_string()
        {
            Assert.Equal(string.Empty,
                ShimModuleRecognizer.NormalizeDebugName(string.Empty));
        }

        [Fact]
        public void ExtractCanonOpNames_returns_normalized_map()
        {
            var shim = MakeModuleWithNames(
                ShimModuleRecognizer.ShimModuleName,
                new Dictionary<uint, string>
                {
                    { 0, "task.return" },
                    { 1, "stream.new" },
                    { 2, "waitable-set.wait" },
                });

            var names = ShimModuleRecognizer.ExtractCanonOpNames(shim);
            Assert.Equal(3, names.Count);
            Assert.Equal("task-return", names[0]);
            Assert.Equal("stream-new", names[1]);
            Assert.Equal("waitable-set-wait", names[2]);
        }

        [Fact]
        public void ExtractCanonOpNames_returns_empty_for_module_with_only_module_name()
        {
            // No function-name subsection: ExtractCanonOpNames
            // returns an empty dictionary rather than throwing.
            var m = MakeModuleWithNames(
                ShimModuleRecognizer.ShimModuleName,
                new Dictionary<uint, string>());
            var names = ShimModuleRecognizer.ExtractCanonOpNames(m);
            Assert.Empty(names);
        }

        // ---- Cross-validation against CanonOpRegistry ------------------

        // ---- Structural fallback (LooksLikeShimByStructure) -----------

        [Fact]
        public void LooksLikeShimByStructure_recognizes_imports_from_empty_module_with_digit_names()
        {
            var m = ParseModule(BuildWasmWithImports(new[]
            {
                ("", "0"),
                ("", "1"),
                ("", "2"),
            }));
            Assert.True(ShimModuleRecognizer.LooksLikeShimByStructure(m));
        }

        [Fact]
        public void LooksLikeShimByStructure_rejects_non_digit_names()
        {
            // Empty module, but names are descriptive — not the
            // wit-component integer convention.
            var m = ParseModule(BuildWasmWithImports(new[]
            {
                ("", "do-thing"),
                ("", "other"),
            }));
            Assert.False(ShimModuleRecognizer.LooksLikeShimByStructure(m));
        }

        [Fact]
        public void LooksLikeShimByStructure_rejects_named_modules()
        {
            // Normal imports from a named module — definitely
            // not a shim.
            var m = ParseModule(BuildWasmWithImports(new[]
            {
                ("env", "puts"),
                ("env", "exit"),
            }));
            Assert.False(ShimModuleRecognizer.LooksLikeShimByStructure(m));
        }

        [Fact]
        public void IsShimModule_uses_structural_fallback_when_name_section_stripped()
        {
            // Real wit-component output with the name section
            // stripped by wasm-opt or similar. Module name +
            // function names are gone, but the import shape
            // survives.
            var stripped = ParseModule(BuildWasmWithImports(
                new[] { ("", "0"), ("", "1") },
                moduleName: null,    // no module name
                funcNames: null));   // no function names
            Assert.True(ShimModuleRecognizer.IsShimModule(stripped));
        }

        [Fact]
        public void IsShimModule_returns_true_when_both_signals_present()
        {
            var full = ParseModule(BuildWasmWithImports(
                new[] { ("", "0") },
                moduleName: ShimModuleRecognizer.ShimModuleName,
                funcNames: new Dictionary<uint, string> { { 0, "task.return" } }));
            Assert.True(ShimModuleRecognizer.IsShimModule(full));
        }

        // ---- Stripped function-name subsection (hard limit) ------------

        [Fact]
        public void ExtractCanonOpNames_returns_empty_when_function_names_stripped()
        {
            // Module is structurally a shim but the function-name
            // subsection is gone — the per-shim canon-op identity
            // is unrecoverable. This is the documented hard limit:
            // embedders who strip canon-async-component names
            // break their hostability.
            var stripped = ParseModule(BuildWasmWithImports(
                new[] { ("", "0"), ("", "1") },
                moduleName: ShimModuleRecognizer.ShimModuleName,
                funcNames: null));
            // IsShimModule still recognizes it (by name + structure):
            Assert.True(ShimModuleRecognizer.IsShimModule(stripped));
            // But ExtractCanonOpNames returns an empty map — caller
            // surfaces "stripped names — cannot bind".
            Assert.Empty(ShimModuleRecognizer.ExtractCanonOpNames(stripped));
        }

        [Fact]
        public void ExtractCanonOpNames_extracted_names_match_registry()
        {
            // Synthesize a shim with every canon-op the dispatcher
            // supports. Each normalized debug name must be in the
            // registry — this is the end-to-end correctness check.
            var debugNames = new Dictionary<uint, string>();
            uint idx = 0;
            // Sample a few that cover dot-spellings + plain kebab.
            debugNames[idx++] = "task.return";
            debugNames[idx++] = "task.cancel";
            debugNames[idx++] = "stream.new";
            debugNames[idx++] = "stream.read";
            debugNames[idx++] = "stream.cancel-read";
            debugNames[idx++] = "future-new";
            debugNames[idx++] = "error-context.debug-message";
            debugNames[idx++] = "waitable-set.wait";
            debugNames[idx++] = "waitable-set.poll";
            debugNames[idx++] = "waitable.join";
            debugNames[idx++] = "backpressure.set";

            var shim = MakeModuleWithNames(
                ShimModuleRecognizer.ShimModuleName, debugNames);
            var normalized = ShimModuleRecognizer.ExtractCanonOpNames(shim);

            foreach (var (_, opName) in normalized.OrderBy(kv => kv.Key))
            {
                Assert.True(CanonOpRegistry.IsKnown(opName),
                    $"Normalized debug name '{opName}' should be a known canon op.");
            }
        }
    }
}
