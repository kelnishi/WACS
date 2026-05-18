// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.Text;
using Wacs.Core.Utilities;

namespace Wacs.Core.Bin
{
    /// <summary>
    /// Encoders for the structured custom sections that the binary
    /// parser optionally captures: <c>name</c> (when
    /// <see cref="BinaryModuleParser.ParseCustomNames"/> is set) and
    /// <c>metadata.code.branch_hint</c> (when
    /// <see cref="BinaryModuleParser.ParseBranchHints"/> is set).
    /// </summary>
    internal static class CustomSectionEncoders
    {
        // ---- Name section ----------------------------------------------

        public static void WriteNameSection(BinaryWriter w, Module.NameSection names)
        {
            // Each subsection is length-prefixed and identified by a
            // single-byte id. Wire order matches NameSubsectionId enum
            // (0..9) — the parser reads them in arbitrary order but
            // most toolchains emit them sequentially.
            if (names.ModuleName != null)
                WriteSubsection(w, NameSubsectionIdValue(NameSubKind.ModuleName), inner =>
                    inner.WriteUtf8String(names.ModuleName.Name));

            if (names.FunctionNames != null)
                WriteSubsection(w, NameSubsectionIdValue(NameSubKind.FuncName), inner =>
                    WriteNameMap(inner, names.FunctionNames.Names));

            if (names.LocalNames != null)
                WriteSubsection(w, NameSubsectionIdValue(NameSubKind.LocalName), inner =>
                    WriteIndirectNameMap(inner, names.LocalNames.Names));

            if (names.LabelNames != null)
                WriteSubsection(w, NameSubsectionIdValue(NameSubKind.LabelName), inner =>
                    WriteIndirectNameMap(inner, names.LabelNames.Names));

            if (names.TypeNames != null)
                WriteSubsection(w, NameSubsectionIdValue(NameSubKind.TypeName), inner =>
                    WriteNameMap(inner, names.TypeNames.Names));

            if (names.TableNames != null)
                WriteSubsection(w, NameSubsectionIdValue(NameSubKind.TableName), inner =>
                    WriteNameMap(inner, names.TableNames.Names));

            if (names.MemoryNames != null)
                WriteSubsection(w, NameSubsectionIdValue(NameSubKind.MemoryName), inner =>
                    WriteNameMap(inner, names.MemoryNames.Names));

            if (names.GlobalNames != null)
                WriteSubsection(w, NameSubsectionIdValue(NameSubKind.GlobalName), inner =>
                    WriteNameMap(inner, names.GlobalNames.Names));

            if (names.ElementSegNames != null)
                WriteSubsection(w, NameSubsectionIdValue(NameSubKind.ElementSegName), inner =>
                    WriteNameMap(inner, names.ElementSegNames.Names));

            if (names.DataSegNames != null)
                WriteSubsection(w, NameSubsectionIdValue(NameSubKind.DataSegName), inner =>
                    WriteNameMap(inner, names.DataSegNames.Names));

            if (names.FieldNames != null)
                WriteSubsection(w, NameSubsectionIdValue(NameSubKind.FieldName), inner =>
                    WriteIndirectNameMap(inner, names.FieldNames.Names));

            if (names.TagNames != null)
                WriteSubsection(w, NameSubsectionIdValue(NameSubKind.TagName), inner =>
                    WriteNameMap(inner, names.TagNames.Names));
        }

        private enum NameSubKind : byte
        {
            ModuleName = 0,
            FuncName = 1,
            LocalName = 2,
            LabelName = 3,
            TypeName = 4,
            TableName = 5,
            MemoryName = 6,
            GlobalName = 7,
            ElementSegName = 8,
            DataSegName = 9,
            FieldName = 10,
            TagName = 11
        }

        private static byte NameSubsectionIdValue(NameSubKind kind) => (byte)kind;

        private static void WriteSubsection(BinaryWriter w, byte id, System.Action<BinaryWriter> emit)
        {
            w.Write(id);
            w.WriteLengthPrefixed(emit);
        }

        private static void WriteNameMap(BinaryWriter w, Module.NameMap map)
        {
            // Spec requires entries sorted ascending by index. The parser
            // tolerates arbitrary order, but emitting sorted keeps the
            // output canonical and matches what every toolchain does.
            var entries = map.NameAssocMap
                .OrderBy(kv => kv.Key)
                .ToArray();
            w.WriteLeb128_u32((uint)entries.Length);
            foreach (var kv in entries)
            {
                w.WriteLeb128_u32(kv.Key);
                w.WriteUtf8String(kv.Value);
            }
        }

        private static void WriteIndirectNameMap(BinaryWriter w, Module.IndirectNameMap map)
        {
            var entries = map.IndirectNameAssocMap
                .OrderBy(kv => kv.Key)
                .ToArray();
            w.WriteLeb128_u32((uint)entries.Length);
            foreach (var kv in entries)
            {
                w.WriteLeb128_u32(kv.Key);
                WriteNameMap(w, kv.Value);
            }
        }

        // ---- Branch-hint section --------------------------------------

        public static void WriteBranchHintSection(BinaryWriter w, Module.BranchHintMap hints)
        {
            // Wire shape mirrors ParseBranchHintSection:
            //   funcs : vec(func_hint)
            //   func_hint ::= func_idx:u32  hints:vec(hint)
            //   hint      ::= byte_offset:u32  data:vec(byte)
            //
            // Sort outer entries by funcidx and inner entries by
            // byte-offset for canonical output.
            var byFunc = hints.ByFuncIndex
                .OrderBy(kv => kv.Key)
                .ToArray();
            w.WriteLeb128_u32((uint)byFunc.Length);
            foreach (var fnEntry in byFunc)
            {
                w.WriteLeb128_u32(fnEntry.Key);
                var fnHints = fnEntry.Value
                    .OrderBy(kv => kv.Key)
                    .ToArray();
                w.WriteLeb128_u32((uint)fnHints.Length);
                foreach (var h in fnHints)
                {
                    w.WriteLeb128_u32(h.Value.ByteOffset);
                    var raw = h.Value.RawData ?? System.Array.Empty<byte>();
                    w.WriteLeb128_u32((uint)raw.Length);
                    if (raw.Length > 0)
                        w.Write(raw);
                }
            }
        }

        // ---- WACS trivia section --------------------------------------

        // `wacs.trivia` is an optimistic in-band carrier for WAT
        // comments (`;;` / `(;…;)`) and `(@name …)` annotations so
        // round-tripping WAT → wasm → WAT keeps the human-authored
        // trivia. Not part of any spec; ignored by other engines.
        // Best-effort: source positions aren't serialized (the binary
        // form has no source), and re-parse position assignments are
        // approximate.
        //
        // Payload:
        //   comments:    vec(entry<vec(comment)>)
        //   annotations: vec(entry<vec(annotation)>)
        // entry<T> ::= kind:u8  index:s32  instructionIndex:s32  payload:T
        // comment    ::= trivia_kind:u8  is_trailing:u8  text:utf8-str
        // annotation ::= name:utf8-str  payload:utf8-str

        public static void WriteTriviaSection(BinaryWriter w, Module module)
        {
            WriteTriviaTable(w, module.Comments, WriteCommentEntry);
            WriteTriviaTable(w, module.Annotations, WriteAnnotationEntry);
        }

        private static void WriteTriviaTable<T>(
            BinaryWriter w,
            Dictionary<ModuleElementRef, List<T>>? table,
            System.Action<BinaryWriter, T> writeItem)
        {
            if (table == null || table.Count == 0)
            {
                w.WriteLeb128_u32(0);
                return;
            }
            // Sort by key for canonical output. ModuleElementRef has no
            // built-in comparer; tuple-sort lexicographically.
            var sorted = table
                .OrderBy(kv => (byte)kv.Key.Kind)
                .ThenBy(kv => kv.Key.Index)
                .ThenBy(kv => kv.Key.InstructionIndex)
                .ToArray();
            w.WriteLeb128_u32((uint)sorted.Length);
            foreach (var kv in sorted)
            {
                w.Write((byte)kv.Key.Kind);
                w.WriteLeb128_s32(kv.Key.Index);
                w.WriteLeb128_s32(kv.Key.InstructionIndex);
                w.WriteLeb128_u32((uint)kv.Value.Count);
                foreach (var item in kv.Value)
                    writeItem(w, item);
            }
        }

        private static void WriteCommentEntry(BinaryWriter w, WatComment c)
        {
            w.Write((byte)c.Kind);
            w.Write(c.IsTrailing ? (byte)1 : (byte)0);
            w.WriteUtf8String(c.Text ?? string.Empty);
        }

        private static void WriteAnnotationEntry(BinaryWriter w, WatAnnotation a)
        {
            w.WriteUtf8String(a.Name ?? string.Empty);
            w.WriteUtf8String(a.Payload ?? string.Empty);
        }

        public static void ParseTriviaSection(BinaryReader reader, Module module)
        {
            uint commentEntries = reader.ReadLeb128_u32();
            for (uint i = 0; i < commentEntries; i++)
            {
                var owner = ReadOwner(reader);
                uint count = reader.ReadLeb128_u32();
                for (uint j = 0; j < count; j++)
                {
                    var kind = (TriviaKind)reader.ReadByte();
                    bool isTrailing = reader.ReadByte() != 0;
                    var text = reader.ReadUtf8String();
                    module.AddComment(owner, new WatComment
                    {
                        Kind = kind,
                        IsTrailing = isTrailing,
                        Text = text,
                    });
                }
            }

            uint annotationEntries = reader.ReadLeb128_u32();
            for (uint i = 0; i < annotationEntries; i++)
            {
                var owner = ReadOwner(reader);
                uint count = reader.ReadLeb128_u32();
                for (uint j = 0; j < count; j++)
                {
                    var name = reader.ReadUtf8String();
                    var payload = reader.ReadUtf8String();
                    module.AddAnnotation(owner, new WatAnnotation
                    {
                        Name = name,
                        Payload = payload,
                    });
                }
            }
        }

        private static ModuleElementRef ReadOwner(BinaryReader reader)
        {
            var kind = (ModuleElementKind)reader.ReadByte();
            int index = reader.ReadLeb128_s32();
            int instructionIndex = reader.ReadLeb128_s32();
            return new ModuleElementRef(kind, index, instructionIndex);
        }
    }
}
