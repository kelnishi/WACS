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
