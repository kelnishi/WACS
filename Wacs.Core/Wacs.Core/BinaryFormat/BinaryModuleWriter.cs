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
using System.Text;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;

namespace Wacs.Core.Bin
{
    /// <summary>
    /// Round-trip binary serializer — emits a canonical wasm binary
    /// for a <see cref="Module"/> that <see cref="BinaryModuleParser"/>
    /// can re-parse to a structurally-equivalent module.
    ///
    /// <para>Companion to <see cref="Wacs.Core.Text.TextModuleWriter"/>:
    /// where that one targets parseable WAT, this one targets the binary
    /// wire format. Each section is emitted in spec order; custom
    /// sections (<c>name</c>, <c>metadata.code.branch_hint</c>) are
    /// emitted at the end when their data is present on the module
    /// (i.e. when the parser ran with <c>ParseCustomNames</c> /
    /// <c>ParseBranchHints</c> enabled).</para>
    /// </summary>
    public static class BinaryModuleWriter
    {
        private const uint MagicNumber = 0x6D736100; // '\0asm'
        private const uint Version = 1;

        /// <summary>Serialize a module to a fresh byte array.</summary>
        public static byte[] Write(Module module)
        {
            using var ms = new MemoryStream();
            WriteTo(ms, module);
            return ms.ToArray();
        }

        /// <summary>Serialize a module to the given stream. The stream
        /// must support writes; it is left open on return.</summary>
        public static void WriteTo(Stream stream, Module module)
        {
            using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            w.Write(MagicNumber);
            w.Write(Version);

            // Spec section order — same as BinaryModuleParser.SectionOrder.
            // Empty sections are simply skipped (omitting an optional
            // section is valid wasm).
            if (module.Types.Count > 0)
                WriteSection(w, SectionId.Type, inner => WriteTypeSection(inner, module));

            if (module.Imports.Length > 0)
                WriteSection(w, SectionId.Import, inner => WriteImportSection(inner, module));

            if (module.Funcs.Count > 0)
                WriteSection(w, SectionId.Function, inner => WriteFunctionSection(inner, module));

            if (module.Tables.Count > 0)
                WriteSection(w, SectionId.Table, inner => WriteTableSection(inner, module));

            if (module.Memories.Count > 0)
                WriteSection(w, SectionId.Memory, inner => WriteMemorySection(inner, module));

            // Tag section comes after Memory in section-order though its
            // SectionId (13) is the highest numeric id. SectionOrder in
            // the parser pins this position; the writer mirrors it.
            if (module.Tags.Count > 0)
                WriteSection(w, SectionId.Tag, inner => WriteTagSection(inner, module));

            if (module.Globals.Count > 0)
                WriteSection(w, SectionId.Global, inner => WriteGlobalSection(inner, module));

            if (module.Exports.Length > 0)
                WriteSection(w, SectionId.Export, inner => WriteExportSection(inner, module));

            if (module.StartIndex != FuncIdx.Default)
                WriteSection(w, SectionId.Start, inner => inner.WriteLeb128_u32(module.StartIndex.Value));

            if (module.Elements.Length > 0)
                WriteSection(w, SectionId.Element, inner => WriteElementSection(inner, module));

            // DataCount must be present whenever any code references
            // data segments via memory.init / data.drop. The parser
            // backfills DataCount=Datas.Length when no count section
            // was present and Datas is non-empty; here we only emit
            // the count section if the module declared one originally
            // (DataCount != uint.MaxValue is the sentinel for "absent").
            // After parse, FinalizeModule normalizes DataCount to
            // Datas.Length whenever the section was missing — so we
            // can't distinguish "was present" from "was absent but
            // backfilled" purely from the post-parse module. Heuristic:
            // emit DataCount iff Datas is non-empty (matches what most
            // toolchains emit) — empty Datas modules don't need it.
            if (module.Datas.Length > 0)
                WriteSection(w, SectionId.DataCount, inner =>
                    inner.WriteLeb128_u32((uint)module.Datas.Length));

            if (module.Funcs.Count > 0)
                WriteSection(w, SectionId.Code, inner => WriteCodeSection(inner, module));

            if (module.Datas.Length > 0)
                WriteSection(w, SectionId.Data, inner => WriteDataSection(inner, module));

            // Custom sections — only emitted when their structured form
            // is present on the module. Order is intentional: `name`
            // before `metadata.code.branch_hint`, matching wasm-tools
            // convention.
            if (module.Names != null)
                WriteCustomSection(w, "name", inner => CustomSectionEncoders.WriteNameSection(inner, module.Names));

            if (module.BranchHints != null)
                WriteCustomSection(w, "metadata.code.branch_hint",
                    inner => CustomSectionEncoders.WriteBranchHintSection(inner, module.BranchHints));

            // Optimistic in-band sidecar for WAT comments / annotations.
            // Lossy by design: source positions aren't preserved (binary
            // has no source line/col). Emitted only when at least one
            // table is populated.
            if ((module.Comments != null && module.Comments.Count > 0)
                || (module.Annotations != null && module.Annotations.Count > 0))
            {
                WriteCustomSection(w, "wacs.trivia",
                    inner => CustomSectionEncoders.WriteTriviaSection(inner, module));
            }
        }

        // ---- Section framing -------------------------------------------

        private static void WriteSection(BinaryWriter outer, SectionId id, System.Action<BinaryWriter> emit)
        {
            outer.Write((byte)id);
            outer.WriteLengthPrefixed(emit);
        }

        private static void WriteCustomSection(BinaryWriter outer, string name, System.Action<BinaryWriter> emit)
        {
            outer.Write((byte)SectionId.Custom);
            outer.WriteLengthPrefixed(inner =>
            {
                inner.WriteUtf8String(name);
                emit(inner);
            });
        }

        // ---- Per-section encoders --------------------------------------

        private static void WriteTypeSection(BinaryWriter w, Module module)
        {
            w.WriteLeb128_u32((uint)module.Types.Count);
            foreach (var rt in module.Types)
                TypeWriters.WriteRecursiveType(w, rt);
        }

        private static void WriteImportSection(BinaryWriter w, Module module)
        {
            w.WriteLeb128_u32((uint)module.Imports.Length);
            foreach (var imp in module.Imports)
            {
                w.WriteUtf8String(imp.ModuleName);
                w.WriteUtf8String(imp.Name);
                switch (imp.Desc)
                {
                    case Module.ImportDesc.FuncDesc fd:
                        w.Write((byte)ExternalKind.Function);
                        w.WriteLeb128_u32((uint)fd.TypeIndex.Value);
                        break;
                    case Module.ImportDesc.TableDesc td:
                        w.Write((byte)ExternalKind.Table);
                        TypeWriters.WriteTableType(w, td.TableDef);
                        break;
                    case Module.ImportDesc.MemDesc md:
                        w.Write((byte)ExternalKind.Memory);
                        TypeWriters.WriteMemoryType(w, md.MemDef);
                        break;
                    case Module.ImportDesc.GlobalDesc gd:
                        w.Write((byte)ExternalKind.Global);
                        TypeWriters.WriteGlobalType(w, gd.GlobalDef);
                        break;
                    case Module.ImportDesc.TagDesc tg:
                        w.Write((byte)ExternalKind.Tag);
                        TypeWriters.WriteTagType(w, tg.TagDef);
                        break;
                    default:
                        throw new InvalidDataException($"Unknown ImportDesc {imp.Desc?.GetType().Name}");
                }
            }
        }

        private static void WriteFunctionSection(BinaryWriter w, Module module)
        {
            w.WriteLeb128_u32((uint)module.Funcs.Count);
            foreach (var fn in module.Funcs)
                w.WriteLeb128_u32((uint)fn.TypeIndex.Value);
        }

        private static void WriteTableSection(BinaryWriter w, Module module)
        {
            w.WriteLeb128_u32((uint)module.Tables.Count);
            foreach (var t in module.Tables)
                TypeWriters.WriteTableType(w, t);
        }

        private static void WriteMemorySection(BinaryWriter w, Module module)
        {
            w.WriteLeb128_u32((uint)module.Memories.Count);
            foreach (var m in module.Memories)
                TypeWriters.WriteMemoryType(w, m);
        }

        private static void WriteTagSection(BinaryWriter w, Module module)
        {
            w.WriteLeb128_u32((uint)module.Tags.Count);
            foreach (var t in module.Tags)
                TypeWriters.WriteTagType(w, t);
        }

        private static void WriteGlobalSection(BinaryWriter w, Module module)
        {
            w.WriteLeb128_u32((uint)module.Globals.Count);
            foreach (var g in module.Globals)
            {
                TypeWriters.WriteGlobalType(w, g.Type);
                TypeWriters.WriteExpression(w, g.Initializer);
            }
        }

        private static void WriteExportSection(BinaryWriter w, Module module)
        {
            w.WriteLeb128_u32((uint)module.Exports.Length);
            foreach (var e in module.Exports)
            {
                w.WriteUtf8String(e.Name);
                switch (e.Desc)
                {
                    case Module.ExportDesc.FuncDesc fd:
                        w.Write((byte)ExternalKind.Function);
                        w.WriteLeb128_u32(fd.FunctionIndex.Value);
                        break;
                    case Module.ExportDesc.TableDesc td:
                        w.Write((byte)ExternalKind.Table);
                        w.WriteLeb128_u32(td.TableIndex.Value);
                        break;
                    case Module.ExportDesc.MemDesc md:
                        w.Write((byte)ExternalKind.Memory);
                        w.WriteLeb128_u32(md.MemoryIndex.Value);
                        break;
                    case Module.ExportDesc.GlobalDesc gd:
                        w.Write((byte)ExternalKind.Global);
                        w.WriteLeb128_u32(gd.GlobalIndex.Value);
                        break;
                    case Module.ExportDesc.TagDesc tg:
                        w.Write((byte)ExternalKind.Tag);
                        w.WriteLeb128_u32(tg.TagIndex.Value);
                        break;
                    default:
                        throw new InvalidDataException($"Unknown ExportDesc {e.Desc?.GetType().Name}");
                }
            }
        }

        private static void WriteElementSection(BinaryWriter w, Module module)
        {
            w.WriteLeb128_u32((uint)module.Elements.Length);
            foreach (var seg in module.Elements)
                WriteElementSegment(w, seg);
        }

        private static void WriteElementSegment(BinaryWriter w, Module.ElementSegment seg)
        {
            // Choose the wire form that round-trips. The parser supports
            // 8 flag variants (0..7); the writer picks the one whose
            // shape matches what's present on the segment. Specifically:
            //   * funcref-shortcut form requires Type == ValType.Func
            //     AND every initializer being a single ref.func.
            //   * Otherwise emit the expression-vector form (flags 4..7).
            bool useFuncShortcut = seg.Type == ValType.Func && IsAllRefFunc(seg);

            switch (seg.Mode)
            {
                case Module.ElementMode.ActiveMode am:
                    if (am.TableIndex.Value == 0 && useFuncShortcut)
                    {
                        w.WriteLeb128_u32((uint)ElementType.ActiveNoIndexWithElemKind);
                        TypeWriters.WriteExpression(w, am.Offset);
                        WriteFuncIdxVector(w, seg);
                    }
                    else if (useFuncShortcut)
                    {
                        w.WriteLeb128_u32((uint)ElementType.ActiveWithIndexAndElemKind);
                        w.WriteLeb128_u32(am.TableIndex.Value);
                        TypeWriters.WriteExpression(w, am.Offset);
                        WriteElemKind(w, seg.Type);
                        WriteFuncIdxVector(w, seg);
                    }
                    else if (am.TableIndex.Value == 0)
                    {
                        w.WriteLeb128_u32((uint)ElementType.ActiveNoIndexWithElemType);
                        TypeWriters.WriteExpression(w, am.Offset);
                        WriteExprVector(w, seg);
                    }
                    else
                    {
                        w.WriteLeb128_u32((uint)ElementType.ActiveWithIndexAndElemType);
                        w.WriteLeb128_u32(am.TableIndex.Value);
                        TypeWriters.WriteExpression(w, am.Offset);
                        ValTypeWriter.WriteRefType(w, seg.Type);
                        WriteExprVector(w, seg);
                    }
                    break;

                case Module.ElementMode.PassiveMode:
                    if (useFuncShortcut)
                    {
                        w.WriteLeb128_u32((uint)ElementType.PassiveWithElemKind);
                        WriteElemKind(w, seg.Type);
                        WriteFuncIdxVector(w, seg);
                    }
                    else
                    {
                        w.WriteLeb128_u32((uint)ElementType.PassiveWithElemType);
                        ValTypeWriter.WriteRefType(w, seg.Type);
                        WriteExprVector(w, seg);
                    }
                    break;

                case Module.ElementMode.DeclarativeMode:
                    if (useFuncShortcut)
                    {
                        w.WriteLeb128_u32((uint)ElementType.DeclarativeWithElemKind);
                        WriteElemKind(w, seg.Type);
                        WriteFuncIdxVector(w, seg);
                    }
                    else
                    {
                        w.WriteLeb128_u32((uint)ElementType.DeclarativeWithElemType);
                        ValTypeWriter.WriteRefType(w, seg.Type);
                        WriteExprVector(w, seg);
                    }
                    break;

                default:
                    throw new InvalidDataException($"Unknown ElementMode {seg.Mode?.GetType().Name}");
            }
        }

        private static bool IsAllRefFunc(Module.ElementSegment seg)
        {
            foreach (var expr in seg.Initializers)
            {
                // Func-shortcut form expects exactly: ref.func $x; end.
                // The parsed expression carries the trailing InstEnd.
                var insts = expr.Instructions;
                if (insts.Count < 1) return false;
                if (insts[0] is not Instructions.Reference.InstRefFunc) return false;
            }
            return true;
        }

        private static void WriteElemKind(BinaryWriter w, ValType elemKind)
        {
            // The parser only accepts 0x00 here (ValType.Func per the
            // legacy elemkind compatibility note in ElementSection.cs).
            // Round-trip safely by emitting the same byte.
            w.Write((byte)0x00);
        }

        private static void WriteFuncIdxVector(BinaryWriter w, Module.ElementSegment seg)
        {
            w.WriteLeb128_u32((uint)seg.Initializers.Length);
            foreach (var expr in seg.Initializers)
            {
                var inst = expr.Instructions[0] as Instructions.Reference.InstRefFunc;
                if (inst == null)
                    throw new InvalidDataException(
                        "Element segment initializer expected to be a single ref.func instruction");
                w.WriteLeb128_u32(inst.FunctionIndex.Value);
            }
        }

        private static void WriteExprVector(BinaryWriter w, Module.ElementSegment seg)
        {
            w.WriteLeb128_u32((uint)seg.Initializers.Length);
            foreach (var expr in seg.Initializers)
                TypeWriters.WriteExpression(w, expr);
        }

        private static void WriteCodeSection(BinaryWriter w, Module module)
        {
            w.WriteLeb128_u32((uint)module.Funcs.Count);
            foreach (var fn in module.Funcs)
            {
                w.WriteLengthPrefixed(body =>
                {
                    // Compress the flat Locals[] back into runs of
                    // (count, type). The parser stores locals
                    // compressed-on-the-wire and expands them at read
                    // time; the symmetric write groups consecutive
                    // identical types.
                    WriteCompressedLocals(body, fn.Locals);
                    TypeWriters.WriteInstructions(body, fn.Body.Instructions);
                });
            }
        }

        private static void WriteCompressedLocals(BinaryWriter w, ValType[] locals)
        {
            if (locals == null || locals.Length == 0)
            {
                w.WriteLeb128_u32(0u);
                return;
            }

            // Build the (count, type) runs first so we can length-prefix.
            var runs = new List<(uint count, ValType type)>();
            uint runCount = 1;
            ValType runType = locals[0];
            for (int i = 1; i < locals.Length; i++)
            {
                if (locals[i] == runType) { runCount++; continue; }
                runs.Add((runCount, runType));
                runType = locals[i];
                runCount = 1;
            }
            runs.Add((runCount, runType));

            w.WriteLeb128_u32((uint)runs.Count);
            foreach (var (count, type) in runs)
            {
                w.WriteLeb128_u32(count);
                ValTypeWriter.WriteValType(w, type, parseStorageType: false);
            }
        }

        private static void WriteDataSection(BinaryWriter w, Module module)
        {
            w.WriteLeb128_u32((uint)module.Datas.Length);
            foreach (var d in module.Datas)
                WriteDataSegment(w, d);
        }

        private static void WriteDataSegment(BinaryWriter w, Module.Data d)
        {
            switch (d.Mode)
            {
                case Module.DataMode.ActiveMode am when am.MemoryIndex.Value == 0:
                    w.WriteLeb128_u32((uint)Module.DataFlags.ActiveDefault);
                    TypeWriters.WriteExpression(w, am.Offset);
                    WriteByteVector(w, d.Init);
                    break;
                case Module.DataMode.ActiveMode am:
                    w.WriteLeb128_u32((uint)Module.DataFlags.ActiveExplicit);
                    w.WriteLeb128_u32(am.MemoryIndex.Value);
                    TypeWriters.WriteExpression(w, am.Offset);
                    WriteByteVector(w, d.Init);
                    break;
                case Module.DataMode.PassiveMode:
                    w.WriteLeb128_u32((uint)Module.DataFlags.Passive);
                    WriteByteVector(w, d.Init);
                    break;
                default:
                    throw new InvalidDataException($"Unknown DataMode {d.Mode?.GetType().Name}");
            }
        }

        private static void WriteByteVector(BinaryWriter w, byte[] data)
        {
            data ??= System.Array.Empty<byte>();
            w.WriteLeb128_u32((uint)data.Length);
            if (data.Length > 0)
                w.Write(data);
        }
    }
}
