// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Linq;
using System.Text;
using Wacs.Core.Bin;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Stack-trace parity: WAT-parsed modules synthesize a `name`
    /// custom section so the names round-trip out through
    /// <see cref="BinaryModuleWriter"/>, and re-parsing the binary
    /// recovers them.
    /// </summary>
    public class NameSectionRoundTripTests
    {
        private static Module ParseWat(string wat)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wat));
            return TextModuleParser.ParseWat(ms);
        }

        [Fact]
        public void WatParse_SynthesizesNameSection_FromFunctionDollarIds()
        {
            const string wat = @"(module
              (func $alpha)
              (func $beta)
              (func))";  // unnamed — should not appear in the name map
            var module = ParseWat(wat);

            Assert.NotNull(module.Names);
            Assert.NotNull(module.Names!.FunctionNames);

            // Two named functions. The leading `$` is stripped per
            // wabt convention (the name section stores raw names).
            var nameMap = module.Names.FunctionNames!.Names.NameAssocMap;
            Assert.Equal("alpha", nameMap[0]);
            Assert.Equal("beta",  nameMap[1]);
            Assert.False(nameMap.ContainsKey(2));   // unnamed func absent
        }

        [Fact]
        public void NameFreeModule_LeavesNamesNull()
        {
            // Nothing carries a $name → Module.Names stays null
            // (lazy-allocation invariant preserved).
            const string wat = "(module (func) (func))";
            var module = ParseWat(wat);
            Assert.Null(module.Names);
        }

        [Fact]
        public void LabelNames_ParsedAsIndirectMap()
        {
            // Extended-name-section §Label Names: labels are an
            // *indirect* name map keyed by funcidx, then labelidx
            // — not a flat name map. Hand-craft a minimal wasm whose
            // `name` custom section carries only the label-names
            // subsection, then verify the parser interprets the wire
            // shape as IndirectNameMap.
            byte[] bytes = BuildWasmWithLabelNamesSubsection(
                (funcIdx: 0u, new[] { (idx: 0u, name: "outer"), (idx: 1u, name: "inner") }));

            bool prev = BinaryModuleParser.ParseCustomNames;
            BinaryModuleParser.ParseCustomNames = true;
            try
            {
                using var ms = new MemoryStream(bytes);
                var module = BinaryModuleParser.ParseWasm(ms);

                Assert.NotNull(module.Names);
                Assert.NotNull(module.Names!.LabelNames);
                var labels = module.Names.LabelNames!.Names;
                Assert.Equal("outer", labels[0].NameAssocMap[0]);
                Assert.Equal("inner", labels[0].NameAssocMap[1]);
            }
            finally
            {
                BinaryModuleParser.ParseCustomNames = prev;
            }
        }

        [Fact]
        public void FieldNames_ParsedAsIndirectMap()
        {
            // GC spec §Field Names: id 10, indirect map keyed by
            // typeidx, then fieldidx. Same pattern as label-names but
            // grouped per struct type.
            byte[] indirect = EncodeIndirectMap(
                (7u, new[] { (0u, "x"), (1u, "y") }));
            byte[] bytes = BuildWasmWithSubsection(subsectionId: 10, indirect);

            bool prev = BinaryModuleParser.ParseCustomNames;
            BinaryModuleParser.ParseCustomNames = true;
            try
            {
                using var ms = new MemoryStream(bytes);
                var module = BinaryModuleParser.ParseWasm(ms);

                Assert.NotNull(module.Names);
                Assert.NotNull(module.Names!.FieldNames);
                var fields = module.Names.FieldNames!.Names;
                Assert.Equal("x", fields[7].NameAssocMap[0]);
                Assert.Equal("y", fields[7].NameAssocMap[1]);
            }
            finally
            {
                BinaryModuleParser.ParseCustomNames = prev;
            }
        }

        [Fact]
        public void TagNames_ParsedAsFlatMap()
        {
            // Exception-handling spec §Tag Names: id 11, flat name map.
            byte[] flat = EncodeFlatMap(new[] { (0u, "oom"), (1u, "io") });
            byte[] bytes = BuildWasmWithSubsection(subsectionId: 11, flat);

            bool prev = BinaryModuleParser.ParseCustomNames;
            BinaryModuleParser.ParseCustomNames = true;
            try
            {
                using var ms = new MemoryStream(bytes);
                var module = BinaryModuleParser.ParseWasm(ms);

                Assert.NotNull(module.Names);
                Assert.NotNull(module.Names!.TagNames);
                var tags = module.Names.TagNames!.Names.NameAssocMap;
                Assert.Equal("oom", tags[0]);
                Assert.Equal("io",  tags[1]);
            }
            finally
            {
                BinaryModuleParser.ParseCustomNames = prev;
            }
        }

        private static byte[] BuildWasmWithLabelNamesSubsection(
            params (uint funcIdx, (uint idx, string name)[] entries)[] groups)
        {
            byte[] indirect = EncodeIndirectMap(groups);
            return BuildWasmWithSubsection(subsectionId: 3, indirect);
        }

        private static byte[] EncodeIndirectMap(
            params (uint key, (uint idx, string name)[] entries)[] groups)
        {
            using var inner = new MemoryStream();
            using var iw = new BinaryWriter(inner, Encoding.UTF8, leaveOpen: true);
            WriteLeb128U32(iw, (uint)groups.Length);
            foreach (var g in groups)
            {
                WriteLeb128U32(iw, g.key);
                WriteLeb128U32(iw, (uint)g.entries.Length);
                foreach (var (idx, name) in g.entries)
                {
                    WriteLeb128U32(iw, idx);
                    WriteUtf8(iw, name);
                }
            }
            iw.Flush();
            return inner.ToArray();
        }

        private static byte[] EncodeFlatMap((uint idx, string name)[] entries)
        {
            using var inner = new MemoryStream();
            using var iw = new BinaryWriter(inner, Encoding.UTF8, leaveOpen: true);
            WriteLeb128U32(iw, (uint)entries.Length);
            foreach (var (idx, name) in entries)
            {
                WriteLeb128U32(iw, idx);
                WriteUtf8(iw, name);
            }
            iw.Flush();
            return inner.ToArray();
        }

        private static byte[] BuildWasmWithSubsection(byte subsectionId, byte[] payload)
        {
            using var sub = new MemoryStream();
            using (var sw = new BinaryWriter(sub, Encoding.UTF8, leaveOpen: true))
            {
                sw.Write(subsectionId);
                WriteLeb128U32(sw, (uint)payload.Length);
                sw.Write(payload);
            }
            byte[] nameSectionPayload = sub.ToArray();

            using var cs = new MemoryStream();
            using (var cw = new BinaryWriter(cs, Encoding.UTF8, leaveOpen: true))
            {
                WriteUtf8(cw, "name");
                cw.Write(nameSectionPayload);
            }
            byte[] customBody = cs.ToArray();

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(new byte[] { 0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00 });
                w.Write((byte)0); // custom section id
                WriteLeb128U32(w, (uint)customBody.Length);
                w.Write(customBody);
            }
            return ms.ToArray();
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

        private static void WriteUtf8(BinaryWriter w, string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            WriteLeb128U32(w, (uint)bytes.Length);
            w.Write(bytes);
        }

        [Fact]
        public void RoundTrip_WatToBinaryToWasmFuncIdSurvives()
        {
            // Full loop: WAT → BinaryModuleWriter → BinaryModuleParser.
            // After re-parse the function Ids should be populated
            // (via the binary parser's PatchNames step, which the
            // ParseCustomNames flag gates).
            const string wat = @"(module
              (func $first)
              (func $second))";
            var first = ParseWat(wat);
            byte[] bytes = BinaryModuleWriter.Write(first);

            // The binary parser's name-section parsing is opt-in.
            // Force it on for this test — production code should
            // do the same when name fidelity matters.
            bool prev = BinaryModuleParser.ParseCustomNames;
            BinaryModuleParser.ParseCustomNames = true;
            try
            {
                using var ms = new MemoryStream(bytes);
                var second = BinaryModuleParser.ParseWasm(ms);

                Assert.NotNull(second.Names);
                Assert.Equal("first",
                    second.Names!.FunctionNames!.Names.NameAssocMap[0]);
                Assert.Equal("second",
                    second.Names.FunctionNames!.Names.NameAssocMap[1]);

                // PatchNames re-stamps Function.Id from the parsed
                // section, using the convention `{name}|{idx}`.
                Assert.Contains("first",  second.Funcs[0].Id);
                Assert.Contains("second", second.Funcs[1].Id);
            }
            finally
            {
                BinaryModuleParser.ParseCustomNames = prev;
            }
        }
    }
}
