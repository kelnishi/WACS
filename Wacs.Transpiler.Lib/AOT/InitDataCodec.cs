// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Wacs.Core;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.Instructions.Reference;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.GC;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using WasmExpression = Wacs.Core.Types.Expression;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Serializer / deserializer for <see cref="ModuleInitData"/> using the
    /// versioned binary format specified in <c>InitDataFormat.md</c>.
    ///
    /// <para>The transpiled <c>.dll</c> embeds the codec output in a static
    /// <see cref="byte"/> array on a generated class; the runtime
    /// <c>Initialize</c> path decodes it on first use. Fully self-contained
    /// — no process-local registries, no re-parse of WASM bytes.</para>
    ///
    /// <para>Phase 1 implementation: scalar + array-of-primitive sections.
    /// <c>Expression</c>, <c>DefType</c>, and ref-typed <c>Value</c>
    /// payloads throw <see cref="NotImplementedException"/> from their
    /// write/read paths until phases 2 and 3 land.</para>
    /// </summary>
    public static class InitDataCodec
    {
        // =================================================================
        // Format header constants
        // =================================================================
        private static readonly byte[] Magic =
            { (byte)'W', (byte)'A', (byte)'C', (byte)'S',
              (byte)'I', (byte)'N', (byte)'I', (byte)'T' };

        /// <summary>
        /// Codec format version, major. A decoder rejects files whose major
        /// exceeds its highest supported major. Bump on breaking changes.
        /// </summary>
        public const byte VersionMajor = 1;

        /// <summary>
        /// Codec format version, minor. Additive — new optional sections
        /// bump this. A v1.N decoder reads v1.M files for any M ≤ N.
        /// </summary>
        public const byte VersionMinor = 0;

        // =================================================================
        // Section tags — see InitDataFormat.md
        // =================================================================
        internal enum Section : byte
        {
            Memories               = 0x01,
            Tables                 = 0x02,
            Globals                = 0x03,
            FuncTypeHashes         = 0x04,
            FuncTypeSuperHashes    = 0x05,
            TypeHashes             = 0x06,
            TypeIsFunc             = 0x07,
            ActiveDataSegments     = 0x08,
            ActiveElementSegments  = 0x09,
            GcElementValues        = 0x0A,
            DeferredElemGlobals    = 0x0B,
            StartFuncIndex         = 0x0C,
            SegmentBaseIds         = 0x0D,
            ActiveElemIndices      = 0x0E,
            ActiveDataIndices      = 0x0F,
            DeferredGlobalInits    = 0x10,
            DeferredDataOffsets    = 0x11,
            SavedDataSegments      = 0x12,
            Counts                 = 0x13,
            GcGlobalInits          = 0x14,
            LocalTagTypes          = 0x15,
            DefTypeTable           = 0x16,
        }

        // =================================================================
        // Public entry points
        // =================================================================

        /// <summary>
        /// Serialize <paramref name="data"/> to bytes. Output is always a
        /// valid v1.<see cref="VersionMinor"/> payload, deserializable by
        /// any v1.N (N ≥ <see cref="VersionMinor"/>) decoder.
        /// </summary>
        public static byte[] Encode(ModuleInitData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            // ---- Header ----------------------------------------------------
            w.Write(Magic);
            w.Write(VersionMajor);
            w.Write(VersionMinor);
            w.Write((ushort)0); // reserved

            // ---- Sections --------------------------------------------------
            WriteSection(w, Section.Memories,              bw => WriteMemories(bw, data));
            WriteSection(w, Section.Tables,                bw => WriteTables(bw, data));
            WriteSection(w, Section.Globals,               bw => WriteGlobals(bw, data));
            WriteSection(w, Section.FuncTypeHashes,        bw => WriteOptionalIntArray(bw, data.FuncTypeHashes));
            WriteSection(w, Section.FuncTypeSuperHashes,   bw => WriteOptionalIntJagged(bw, data.FuncTypeSuperHashes));
            WriteSection(w, Section.TypeHashes,            bw => WriteOptionalIntArray(bw, data.TypeHashes));
            WriteSection(w, Section.TypeIsFunc,            bw => WriteOptionalBoolArray(bw, data.TypeIsFunc));
            WriteSection(w, Section.ActiveDataSegments,    bw => WriteActiveDataSegments(bw, data));
            WriteSection(w, Section.ActiveElementSegments, bw => WriteActiveElementSegments(bw, data));
            WriteSection(w, Section.GcElementValues,       bw => WriteGcElementValues(bw, data));
            WriteSection(w, Section.DeferredElemGlobals,   bw => WriteDeferredElemGlobals(bw, data));
            WriteSection(w, Section.StartFuncIndex,        bw => WriteVarInt32(bw, data.StartFuncIndex));
            WriteSection(w, Section.SegmentBaseIds,        bw => { WriteVarInt32(bw, data.DataSegmentBaseId); WriteVarInt32(bw, data.ElemSegmentBaseId); });
            WriteSection(w, Section.ActiveElemIndices,     bw => WriteIntArray(bw, data.ActiveElemIndices));
            WriteSection(w, Section.ActiveDataIndices,     bw => WriteIntArray(bw, data.ActiveDataIndices));
            WriteSection(w, Section.DeferredGlobalInits,   bw => WriteDeferredGlobalInits(bw, data));
            WriteSection(w, Section.DeferredDataOffsets,   bw => WriteDeferredDataOffsets(bw, data));
            WriteSection(w, Section.SavedDataSegments,     bw => WriteSavedDataSegments(bw, data));
            WriteSection(w, Section.Counts,                bw => WriteCounts(bw, data));
            WriteSection(w, Section.GcGlobalInits,         bw => WriteGcGlobalInits(bw, data));
            WriteSection(w, Section.LocalTagTypes,         bw => WriteLocalTagTypes(bw, data));
            WriteSection(w, Section.DefTypeTable,          bw => WriteDefTypeTable(bw, data));

            w.Flush();
            return ms.ToArray();
        }

        /// <summary>
        /// Deserialize bytes produced by <see cref="Encode"/> back into a
        /// <see cref="ModuleInitData"/>. Throws <see cref="InvalidDataException"/>
        /// on format errors or version mismatch.
        /// </summary>
        public static ModuleInitData Decode(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            // ---- Header ----------------------------------------------------
            var magic = r.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length)
                throw new InvalidDataException("InitData: truncated header");
            for (int i = 0; i < Magic.Length; i++)
                if (magic[i] != Magic[i])
                    throw new InvalidDataException("InitData: magic bytes mismatch");

            byte major = r.ReadByte();
            byte minor = r.ReadByte();
            ushort reserved = r.ReadUInt16();
            if (major > VersionMajor)
                throw new InvalidDataException(
                    $"InitData: format v{major}.{minor} is newer than this decoder (v{VersionMajor}.{VersionMinor}+)");
            if (reserved != 0)
                throw new InvalidDataException($"InitData: reserved header field must be zero, got 0x{reserved:X4}");

            var data = new ModuleInitData();

            // ---- Sections --------------------------------------------------
            while (ms.Position < ms.Length)
            {
                byte tag = r.ReadByte();
                uint length = ReadVarUInt32(r);
                long end = ms.Position + length;
                if (end > ms.Length)
                    throw new InvalidDataException($"InitData: section 0x{tag:X2} extends past EOF");

                switch ((Section)tag)
                {
                    case Section.Memories:              ReadMemories(r, data); break;
                    case Section.Tables:                ReadTables(r, data); break;
                    case Section.Globals:               ReadGlobals(r, data); break;
                    case Section.FuncTypeHashes:        data.FuncTypeHashes = ReadOptionalIntArray(r); break;
                    case Section.FuncTypeSuperHashes:   data.FuncTypeSuperHashes = ReadOptionalIntJagged(r); break;
                    case Section.TypeHashes:            data.TypeHashes = ReadOptionalIntArray(r); break;
                    case Section.TypeIsFunc:            data.TypeIsFunc = ReadOptionalBoolArray(r); break;
                    case Section.ActiveDataSegments:    ReadActiveDataSegments(r, data); break;
                    case Section.ActiveElementSegments: ReadActiveElementSegments(r, data); break;
                    case Section.GcElementValues:       ReadGcElementValues(r, data); break;
                    case Section.DeferredElemGlobals:   ReadDeferredElemGlobals(r, data); break;
                    case Section.StartFuncIndex:        data.StartFuncIndex = ReadVarInt32(r); break;
                    case Section.SegmentBaseIds:        data.DataSegmentBaseId = ReadVarInt32(r); data.ElemSegmentBaseId = ReadVarInt32(r); break;
                    case Section.ActiveElemIndices:     data.ActiveElemIndices = ReadIntArray(r); break;
                    case Section.ActiveDataIndices:     data.ActiveDataIndices = ReadIntArray(r); break;
                    case Section.DeferredGlobalInits:   ReadDeferredGlobalInits(r, data); break;
                    case Section.DeferredDataOffsets:   ReadDeferredDataOffsets(r, data); break;
                    case Section.SavedDataSegments:     ReadSavedDataSegments(r, data); break;
                    case Section.Counts:                ReadCounts(r, data); break;
                    case Section.GcGlobalInits:         ReadGcGlobalInits(r, data); break;
                    case Section.LocalTagTypes:         ReadLocalTagTypes(r, data); break;
                    case Section.DefTypeTable:          ReadDefTypeTable(r, data); break;
                    default:
                        // Unknown tag — skip for forward compat.
                        ms.Position = end;
                        break;
                }

                if (ms.Position != end)
                    throw new InvalidDataException(
                        $"InitData: section 0x{tag:X2} reader consumed {ms.Position - (end - length)} bytes, expected {length}");
            }

            return data;
        }

        // =================================================================
        // Section writers (public for testing)
        // =================================================================

        internal static void WriteSection(BinaryWriter outer, Section tag, Action<BinaryWriter> writePayload)
        {
            using var tmp = new MemoryStream();
            using var inner = new BinaryWriter(tmp, Encoding.UTF8, leaveOpen: true);
            writePayload(inner);
            inner.Flush();
            var bytes = tmp.ToArray();

            outer.Write((byte)tag);
            WriteVarUInt32(outer, (uint)bytes.Length);
            outer.Write(bytes);
        }

        // ---- Memories -----------------------------------------------------
        internal static void WriteMemories(BinaryWriter w, ModuleInitData data)
        {
            WriteVarUInt32(w, (uint)data.Memories.Length);
            foreach (var (min, max) in data.Memories)
            {
                WriteVarUInt64(w, (ulong)min);
                if (max.HasValue) { w.Write((byte)1); WriteVarUInt64(w, (ulong)max.Value); }
                else              { w.Write((byte)0); }
            }
        }

        internal static void ReadMemories(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            var arr = new (long, long?)[count];
            for (int i = 0; i < count; i++)
            {
                long min = (long)ReadVarUInt64(r);
                byte hasMax = r.ReadByte();
                long? max = hasMax == 1 ? (long?)(long)ReadVarUInt64(r) : null;
                arr[i] = (min, max);
            }
            data.Memories = arr;
        }

        // ---- Tables -------------------------------------------------------
        internal static void WriteTables(BinaryWriter w, ModuleInitData data)
        {
            WriteVarUInt32(w, (uint)data.Tables.Length);
            foreach (var (min, max, elemType, initExpr) in data.Tables)
            {
                WriteVarUInt64(w, (ulong)min);
                WriteVarUInt64(w, (ulong)max);
                WriteValType(w, elemType);
                if (initExpr == null) { w.Write((byte)0); }
                else                   { w.Write((byte)1); WriteExpression(w, initExpr); }
            }
        }

        internal static void ReadTables(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            var arr = new (long, long, ValType, WasmExpression?)[count];
            for (int i = 0; i < count; i++)
            {
                long min = (long)ReadVarUInt64(r);
                long max = (long)ReadVarUInt64(r);
                ValType elemType = ReadValType(r);
                byte hasInit = r.ReadByte();
                WasmExpression? initExpr = hasInit == 1 ? ReadExpression(r) : null;
                arr[i] = (min, max, elemType, initExpr);
            }
            data.Tables = arr;
        }

        // ---- Globals ------------------------------------------------------
        internal static void WriteGlobals(BinaryWriter w, ModuleInitData data)
        {
            WriteVarUInt32(w, (uint)data.Globals.Length);
            foreach (var (type, mut, init) in data.Globals)
            {
                WriteValType(w, type);
                w.Write((byte)mut);
                WriteValue(w, init);
            }
        }

        internal static void ReadGlobals(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            var arr = new (ValType, Mutability, Value)[count];
            for (int i = 0; i < count; i++)
            {
                ValType type = ReadValType(r);
                Mutability mut = (Mutability)r.ReadByte();
                Value init = ReadValue(r);
                arr[i] = (type, mut, init);
            }
            data.Globals = arr;
        }

        // ---- Optional int arrays (FuncTypeHashes, TypeHashes) ------------
        internal static void WriteOptionalIntArray(BinaryWriter w, int[]? arr)
        {
            if (arr == null) { w.Write((byte)0); return; }
            w.Write((byte)1);
            WriteVarUInt32(w, (uint)arr.Length);
            foreach (int v in arr) w.Write(v);
        }

        internal static int[]? ReadOptionalIntArray(BinaryReader r)
        {
            byte present = r.ReadByte();
            if (present == 0) return null;
            uint count = ReadVarUInt32(r);
            var arr = new int[count];
            for (int i = 0; i < count; i++) arr[i] = r.ReadInt32();
            return arr;
        }

        internal static void WriteOptionalIntJagged(BinaryWriter w, int[][]? arr)
        {
            if (arr == null) { w.Write((byte)0); return; }
            w.Write((byte)1);
            WriteVarUInt32(w, (uint)arr.Length);
            foreach (var inner in arr)
            {
                WriteVarUInt32(w, (uint)inner.Length);
                foreach (int v in inner) w.Write(v);
            }
        }

        internal static int[][]? ReadOptionalIntJagged(BinaryReader r)
        {
            byte present = r.ReadByte();
            if (present == 0) return null;
            uint outerCount = ReadVarUInt32(r);
            var outer = new int[outerCount][];
            for (int i = 0; i < outerCount; i++)
            {
                uint innerCount = ReadVarUInt32(r);
                var inner = new int[innerCount];
                for (int j = 0; j < innerCount; j++) inner[j] = r.ReadInt32();
                outer[i] = inner;
            }
            return outer;
        }

        internal static void WriteOptionalBoolArray(BinaryWriter w, bool[]? arr)
        {
            if (arr == null) { w.Write((byte)0); return; }
            w.Write((byte)1);
            WriteVarUInt32(w, (uint)arr.Length);
            foreach (bool b in arr) w.Write((byte)(b ? 1 : 0));
        }

        internal static bool[]? ReadOptionalBoolArray(BinaryReader r)
        {
            byte present = r.ReadByte();
            if (present == 0) return null;
            uint count = ReadVarUInt32(r);
            var arr = new bool[count];
            for (int i = 0; i < count; i++) arr[i] = r.ReadByte() != 0;
            return arr;
        }

        // ---- Plain int arrays --------------------------------------------
        internal static void WriteIntArray(BinaryWriter w, int[] arr)
        {
            WriteVarUInt32(w, (uint)arr.Length);
            foreach (int v in arr) WriteVarInt32(w, v);
        }

        internal static int[] ReadIntArray(BinaryReader r)
        {
            uint count = ReadVarUInt32(r);
            var arr = new int[count];
            for (int i = 0; i < count; i++) arr[i] = ReadVarInt32(r);
            return arr;
        }

        // ---- ActiveDataSegments ------------------------------------------
        internal static void WriteActiveDataSegments(BinaryWriter w, ModuleInitData data)
        {
            WriteVarUInt32(w, (uint)data.ActiveDataSegments.Length);
            foreach (var (memIdx, offset, segId) in data.ActiveDataSegments)
            {
                WriteVarInt32(w, memIdx);
                WriteVarInt32(w, offset);
                WriteVarInt32(w, segId);
            }
        }

        internal static void ReadActiveDataSegments(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            var arr = new (int, int, int)[count];
            for (int i = 0; i < count; i++)
                arr[i] = (ReadVarInt32(r), ReadVarInt32(r), ReadVarInt32(r));
            data.ActiveDataSegments = arr;
        }

        // ---- ActiveElementSegments ---------------------------------------
        internal static void WriteActiveElementSegments(BinaryWriter w, ModuleInitData data)
        {
            WriteVarUInt32(w, (uint)data.ActiveElementSegments.Length);
            foreach (var (tableIdx, offset, funcIndices) in data.ActiveElementSegments)
            {
                WriteVarInt32(w, tableIdx);
                WriteVarInt32(w, offset);
                WriteVarUInt32(w, (uint)funcIndices.Length);
                foreach (int f in funcIndices) WriteVarInt32(w, f);
            }
        }

        internal static void ReadActiveElementSegments(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            var arr = new (int, int, int[])[count];
            for (int i = 0; i < count; i++)
            {
                int tableIdx = ReadVarInt32(r);
                int offset = ReadVarInt32(r);
                uint fCount = ReadVarUInt32(r);
                var fs = new int[fCount];
                for (int j = 0; j < fCount; j++) fs[j] = ReadVarInt32(r);
                arr[i] = (tableIdx, offset, fs);
            }
            data.ActiveElementSegments = arr;
        }

        // ---- GcElementValues ---------------------------------------------
        internal static void WriteGcElementValues(BinaryWriter w, ModuleInitData data)
        {
            WriteVarUInt32(w, (uint)data.GcElementValues.Count);
            foreach (var kv in data.GcElementValues)
            {
                WriteVarInt32(w, kv.Key.segIdx);
                WriteVarInt32(w, kv.Key.slotIdx);
                WriteValue(w, kv.Value);
            }
        }

        internal static void ReadGcElementValues(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            var dict = new Dictionary<(int, int), Value>((int)count);
            for (int i = 0; i < count; i++)
            {
                int segIdx = ReadVarInt32(r);
                int slotIdx = ReadVarInt32(r);
                Value v = ReadValue(r);
                dict[(segIdx, slotIdx)] = v;
            }
            data.GcElementValues = dict;
        }

        // ---- DeferredElemGlobals -----------------------------------------
        internal static void WriteDeferredElemGlobals(BinaryWriter w, ModuleInitData data)
        {
            WriteVarUInt32(w, (uint)data.DeferredElemGlobals.Count);
            foreach (var (elemSegIdx, slotIdx, globalIdx) in data.DeferredElemGlobals)
            {
                WriteVarInt32(w, elemSegIdx);
                WriteVarInt32(w, slotIdx);
                WriteVarInt32(w, globalIdx);
            }
        }

        internal static void ReadDeferredElemGlobals(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            var list = new List<(int, int, int)>((int)count);
            for (int i = 0; i < count; i++)
                list.Add((ReadVarInt32(r), ReadVarInt32(r), ReadVarInt32(r)));
            data.DeferredElemGlobals = list;
        }

        // ---- DeferredGlobalInits / DeferredDataOffsets -------------------
        internal static void WriteDeferredGlobalInits(BinaryWriter w, ModuleInitData data)
        {
            WriteVarUInt32(w, (uint)data.DeferredGlobalInits.Count);
            foreach (var (globalIdx, initializer) in data.DeferredGlobalInits)
            {
                WriteVarInt32(w, globalIdx);
                WriteExpression(w, initializer);
            }
        }

        internal static void ReadDeferredGlobalInits(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            var list = new List<(int, WasmExpression)>((int)count);
            for (int i = 0; i < count; i++)
                list.Add((ReadVarInt32(r), ReadExpression(r)));
            data.DeferredGlobalInits = list;
        }

        internal static void WriteDeferredDataOffsets(BinaryWriter w, ModuleInitData data)
        {
            WriteVarUInt32(w, (uint)data.DeferredDataOffsets.Count);
            foreach (var (dataSegIdx, offsetExpr) in data.DeferredDataOffsets)
            {
                WriteVarInt32(w, dataSegIdx);
                WriteExpression(w, offsetExpr);
            }
        }

        internal static void ReadDeferredDataOffsets(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            var list = new List<(int, WasmExpression)>((int)count);
            for (int i = 0; i < count; i++)
                list.Add((ReadVarInt32(r), ReadExpression(r)));
            data.DeferredDataOffsets = list;
        }

        // ---- SavedDataSegments --------------------------------------------
        internal static void WriteSavedDataSegments(BinaryWriter w, ModuleInitData data)
        {
            WriteVarUInt32(w, (uint)data.SavedDataSegments.Count);
            foreach (var kv in data.SavedDataSegments)
            {
                WriteVarInt32(w, kv.Key);
                WriteVarUInt32(w, (uint)kv.Value.Length);
                w.Write(kv.Value);
            }
        }

        internal static void ReadSavedDataSegments(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            var dict = new Dictionary<int, byte[]>((int)count);
            for (int i = 0; i < count; i++)
            {
                int segId = ReadVarInt32(r);
                uint n = ReadVarUInt32(r);
                dict[segId] = r.ReadBytes((int)n);
            }
            data.SavedDataSegments = dict;
        }

        // ---- Counts -------------------------------------------------------
        internal static void WriteCounts(BinaryWriter w, ModuleInitData data)
        {
            WriteVarInt32(w, data.ImportFuncCount);
            WriteVarInt32(w, data.TotalFuncCount);
            WriteVarInt32(w, data.ImportedTagCount);
        }

        internal static void ReadCounts(BinaryReader r, ModuleInitData data)
        {
            data.ImportFuncCount = ReadVarInt32(r);
            data.TotalFuncCount = ReadVarInt32(r);
            data.ImportedTagCount = ReadVarInt32(r);
        }

        // ---- GcGlobalInits ------------------------------------------------
        internal static void WriteGcGlobalInits(BinaryWriter w, ModuleInitData data)
        {
            WriteVarUInt32(w, (uint)data.GcGlobalInits.Count);
            foreach (var init in data.GcGlobalInits)
            {
                WriteVarInt32(w, init.GlobalIndex);
                WriteVarInt32(w, init.TypeIndex);
                WriteVarInt32(w, init.InitKind);
                WriteVarUInt32(w, (uint)init.Params.Length);
                foreach (long p in init.Params) WriteVarInt64(w, p);
                WriteVarInt32(w, init.ElementValType);
            }
        }

        internal static void ReadGcGlobalInits(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            var list = new List<GcGlobalInit>((int)count);
            for (int i = 0; i < count; i++)
            {
                var init = new GcGlobalInit
                {
                    GlobalIndex = ReadVarInt32(r),
                    TypeIndex = ReadVarInt32(r),
                    InitKind = ReadVarInt32(r),
                };
                uint pCount = ReadVarUInt32(r);
                init.Params = new long[pCount];
                for (int j = 0; j < pCount; j++) init.Params[j] = ReadVarInt64(r);
                init.ElementValType = ReadVarInt32(r);
                list.Add(init);
            }
            data.GcGlobalInits = list;
        }

        // ---- LocalTagTypes + DefTypeTable (phase 3) ----------------------
        internal static void WriteLocalTagTypes(BinaryWriter w, ModuleInitData data)
        {
            // Phase 1: LocalTagTypes is expected to hold null entries (the
            // transpiler populates DefType null at transpile time today).
            // Phase 3 will encode deftype refs; for now emit count and
            // "null" markers for each slot.
            WriteVarUInt32(w, (uint)data.LocalTagTypes.Length);
            foreach (var def in data.LocalTagTypes)
            {
                w.Write((byte)(def == null ? 0 : 1));
                if (def != null)
                    throw new NotImplementedException(
                        "InitDataCodec: LocalTagTypes with non-null DefType lands in phase 3.");
            }
        }

        internal static void ReadLocalTagTypes(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            var arr = new DefType[count];
            for (int i = 0; i < count; i++)
            {
                byte present = r.ReadByte();
                if (present != 0)
                    throw new NotImplementedException(
                        "InitDataCodec: LocalTagTypes with non-null DefType lands in phase 3.");
                arr[i] = null!;
            }
            data.LocalTagTypes = arr;
        }

        internal static void WriteDefTypeTable(BinaryWriter w, ModuleInitData data)
        {
            // Phase 1: empty table (DefType encoding lands in phase 3).
            WriteVarUInt32(w, 0);
        }

        internal static void ReadDefTypeTable(BinaryReader r, ModuleInitData data)
        {
            uint count = ReadVarUInt32(r);
            if (count != 0)
                throw new NotImplementedException(
                    "InitDataCodec: DefTypeTable decoding lands in phase 3.");
        }

        // =================================================================
        // Expression / Value / DefType encoding (phase 2+ stubs)
        // =================================================================

        // =================================================================
        // Expression codec (phase 2)
        //
        // Supports the WASM "constant expression" subset plus WASM 3.0
        // constant arithmetic. GC-constant ops (struct.new / array.new*
        // / ref.i31 / *_convert_*) land in phase 3 alongside DefType.
        //
        // Encoding: varuint32 count, then count × { op-tag: u8, op-specific
        // immediate bytes }. Tag is a private byte assigned below, NOT the
        // WASM binary opcode — this way the codec is decoupled from spec
        // opcode changes. Unknown tags cause decode failure (we don't get
        // forward-compat per-op; spec additions bump the codec minor
        // version and decoders ignore unknown sections).
        // =================================================================

        internal enum InitOp : byte
        {
            End             = 0x01,
            I32Const        = 0x02,
            I64Const        = 0x03,
            F32Const        = 0x04,
            F64Const        = 0x05,
            GlobalGet       = 0x06,
            RefNull         = 0x07,
            RefFunc         = 0x08,
            I32Add          = 0x09,
            I32Sub          = 0x0A,
            I32Mul          = 0x0B,
            I64Add          = 0x0C,
            I64Sub          = 0x0D,
            I64Mul          = 0x0E,
            // 0x10–0x17 reserved for phase 3 GC ops (StructNew, ArrayNew*,
            // RefI31, AnyConvertExtern, ExternConvertAny).
        }

        internal static void WriteExpression(BinaryWriter w, WasmExpression expr)
        {
            var insts = expr.Instructions._instructions;
            WriteVarUInt32(w, (uint)insts.Count);
            foreach (var inst in insts) WriteInitInstruction(w, inst);
        }

        internal static WasmExpression ReadExpression(BinaryReader r)
        {
            uint count = ReadVarUInt32(r);
            var list = new List<InstructionBase>((int)count);
            for (int i = 0; i < count; i++) list.Add(ReadInitInstruction(r));
            // Arity = 1 matches Expression.ParseInitializer; init exprs
            // always produce a single value. True arity varies (e.g. the
            // 0-arity table.init expression) but isn't observed at load
            // time — the codec's consumers (deferred inits / data offsets)
            // all evaluate the expression as a single-value producer.
            return new WasmExpression(1, new InstructionSequence(list), isStatic: true);
        }

        private static void WriteInitInstruction(BinaryWriter w, InstructionBase inst)
        {
            switch (inst)
            {
                case InstEnd _:
                    w.Write((byte)InitOp.End);
                    return;
                case InstI32Const i32:
                    w.Write((byte)InitOp.I32Const);
                    WriteVarInt32(w, i32.Value);
                    return;
                case InstI64Const i64:
                    w.Write((byte)InitOp.I64Const);
                    WriteVarInt64(w, i64.FetchImmediate(null!));
                    return;
                case InstF32Const f32:
                    w.Write((byte)InitOp.F32Const);
                    w.Write(f32.FetchImmediate(null!));
                    return;
                case InstF64Const f64:
                    w.Write((byte)InitOp.F64Const);
                    w.Write(f64.FetchImmediate(null!));
                    return;
                case InstGlobalGet gg:
                    w.Write((byte)InitOp.GlobalGet);
                    WriteVarUInt32(w, (uint)gg.GetIndex());
                    return;
                case InstRefNull rn:
                    w.Write((byte)InitOp.RefNull);
                    WriteVarInt32(w, (int)rn.RefType);
                    return;
                case InstRefFunc rf:
                    w.Write((byte)InitOp.RefFunc);
                    WriteVarUInt32(w, rf.FunctionIndex.Value);
                    return;
                case InstI32BinOp bin32 when bin32.Op == OpCode.I32Add: w.Write((byte)InitOp.I32Add); return;
                case InstI32BinOp bin32 when bin32.Op == OpCode.I32Sub: w.Write((byte)InitOp.I32Sub); return;
                case InstI32BinOp bin32 when bin32.Op == OpCode.I32Mul: w.Write((byte)InitOp.I32Mul); return;
                case InstI64BinOp bin64 when bin64.Op == OpCode.I64Add: w.Write((byte)InitOp.I64Add); return;
                case InstI64BinOp bin64 when bin64.Op == OpCode.I64Sub: w.Write((byte)InitOp.I64Sub); return;
                case InstI64BinOp bin64 when bin64.Op == OpCode.I64Mul: w.Write((byte)InitOp.I64Mul); return;
            }
            throw new NotSupportedException(
                $"InitDataCodec: constant-expression op '{inst.GetType().Name}' (opcode {inst.Op}) " +
                "isn't in the phase-2 set. GC-constant ops land in phase 3; anything else is a codec gap.");
        }

        private static InstructionBase ReadInitInstruction(BinaryReader r)
        {
            byte tagByte = r.ReadByte();
            var tag = (InitOp)tagByte;
            switch (tag)
            {
                case InitOp.End:
                    return new InstEnd();
                case InitOp.I32Const:
                {
                    int v = ReadVarInt32(r);
                    return InstI32Const.Inst.Immediate(v);
                }
                case InitOp.I64Const:
                {
                    long v = ReadVarInt64(r);
                    // InstI64Const.Value is internal — round-trip through
                    // Parse() with a tiny byte buffer holding just the
                    // LEB-encoded i64. The buffer is never truncated since
                    // ReadLeb128_s64 stops at the LSB-clear terminator.
                    var inst = new InstI64Const();
                    InvokeParseWithWasmLeb(inst, signedValue: v, writeSignedLeb64: true);
                    return inst;
                }
                case InitOp.F32Const:
                {
                    float v = r.ReadSingle();
                    var inst = new InstF32Const();
                    using var ms = new MemoryStream();
                    using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) w.Write(v);
                    ms.Position = 0;
                    using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
                    return inst.Parse(br);
                }
                case InitOp.F64Const:
                {
                    double v = r.ReadDouble();
                    var inst = new InstF64Const();
                    using var ms = new MemoryStream();
                    using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) w.Write(v);
                    ms.Position = 0;
                    using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
                    return inst.Parse(br);
                }
                case InitOp.GlobalGet:
                {
                    uint idx = ReadVarUInt32(r);
                    var inst = new InstGlobalGet();
                    InvokeParseWithWasmLeb(inst, unsignedValue: idx, writeUnsignedLeb32: true);
                    return inst;
                }
                case InitOp.RefNull:
                {
                    int typeRaw = ReadVarInt32(r);
                    return new InstRefNull().Immediate((ValType)typeRaw);
                }
                case InitOp.RefFunc:
                {
                    uint idx = ReadVarUInt32(r);
                    var inst = new InstRefFunc();
                    InvokeParseWithWasmLeb(inst, unsignedValue: idx, writeUnsignedLeb32: true);
                    return inst;
                }
                case InitOp.I32Add: return InstI32BinOp.I32Add;
                case InitOp.I32Sub: return InstI32BinOp.I32Sub;
                case InitOp.I32Mul: return InstI32BinOp.I32Mul;
                case InitOp.I64Add: return InstI64BinOp.I64Add;
                case InitOp.I64Sub: return InstI64BinOp.I64Sub;
                case InitOp.I64Mul: return InstI64BinOp.I64Mul;
            }
            throw new InvalidDataException($"InitDataCodec: unknown init-op tag 0x{tagByte:X2}");
        }

        /// <summary>
        /// Round-trip a single u32 / s64 immediate through the instruction's
        /// <see cref="InstructionBase.Parse"/> by synthesizing a WASM-binary
        /// <see cref="BinaryReader"/> over the LEB-encoded bytes. Used for
        /// instruction types whose immediate fields aren't public-settable.
        /// </summary>
        private static void InvokeParseWithWasmLeb(
            InstructionBase inst,
            long signedValue = 0,
            uint unsignedValue = 0,
            bool writeSignedLeb64 = false,
            bool writeUnsignedLeb32 = false)
        {
            using var ms = new MemoryStream();
            if (writeSignedLeb64) WriteSignedLeb64(ms, signedValue);
            else if (writeUnsignedLeb32) WriteUnsignedLeb32(ms, unsignedValue);
            ms.Position = 0;
            using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            inst.Parse(br);
        }

        private static void WriteUnsignedLeb32(Stream s, uint v)
        {
            while (v >= 0x80) { s.WriteByte((byte)((v & 0x7F) | 0x80)); v >>= 7; }
            s.WriteByte((byte)v);
        }

        private static void WriteSignedLeb64(Stream s, long v)
        {
            bool more = true;
            while (more)
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                bool sign = (b & 0x40) != 0;
                more = !((v == 0 && !sign) || (v == -1 && sign));
                if (more) b |= 0x80;
                s.WriteByte(b);
            }
        }

        // Value-encoding kind tags. See InitDataFormat.md §VALUE_ENCODING.
        private const byte ValueKindScalar      = 0;
        private const byte ValueKindNullRef     = 1;
        private const byte ValueKindFuncRef     = 2; // funcref with opaque idx
        private const byte ValueKindExternRef   = 3; // externref with opaque idx
        private const byte ValueKindI31         = 4;
        private const byte ValueKindV128        = 5; // 16 raw bytes
        // Tags 6+ reserved for StructRef / ArrayRef in a future phase.

        internal static void WriteValue(BinaryWriter w, Value v)
        {
            WriteVarInt32(w, (int)v.Type);
            switch (v.Type)
            {
                case ValType.I32:
                case ValType.I64:
                case ValType.F32:
                case ValType.F64:
                    w.Write(ValueKindScalar);
                    w.Write((ulong)v.Data.Int64);
                    return;

                case ValType.V128:
                    w.Write(ValueKindV128);
                    // V128 is stored via a VecRef GcRef. Extract the 16 bytes
                    // as two ulongs (MV128 overlays U64x2_0 / U64x2_1 at 0 /
                    // 8). Zero-init fallback when the GcRef isn't present.
                    if (v.GcRef is VecRef vec)
                    {
                        w.Write(vec.V128.U64x2_0);
                        w.Write(vec.V128.U64x2_1);
                    }
                    else
                    {
                        w.Write(0UL);
                        w.Write(0UL);
                    }
                    return;

                case ValType.I31:
                case ValType.I31NN:
                    w.Write(ValueKindI31);
                    WriteVarInt32(w, (int)v.Data.Ptr);
                    return;
            }

            // Reference types (FuncRef / ExternRef / nullables / typed refs).
            if (v.Type.IsRefType())
            {
                if (v.IsNullRef)
                {
                    w.Write(ValueKindNullRef);
                    return;
                }

                // Concrete reference — we encode the idx. For structrefs and
                // arrayrefs with real GC payloads, the GcRef itself carries
                // per-object state that isn't in Data.Ptr — those cases
                // aren't handled in the phase-3 codec (they're extremely
                // rare in transpiled init data; the transpiler's current
                // PrepareInitData only ever emits RefI31 + null + funcidx
                // values). Defer struct/array-value serialization to a
                // later phase.
                if (v.GcRef is I31Ref i31)
                {
                    w.Write(ValueKindI31);
                    WriteVarInt32(w, i31.Value);
                    return;
                }

                // Externref / funcref with an opaque idx. Type determines
                // which — both use Data.Ptr; the Type itself is already
                // encoded in the prefix.
                bool isExtern = v.Type.Matches(ValType.ExternRef, null!)
                             || (v.Type & ~ValType.NullableRef) == ValType.ExternRef;
                w.Write(isExtern ? ValueKindExternRef : ValueKindFuncRef);
                WriteVarInt64(w, v.Data.Ptr);
                return;
            }

            throw new NotSupportedException(
                $"InitDataCodec: Value encoding for type {v.Type} (GcRef={v.GcRef?.GetType().Name ?? "null"}) " +
                "isn't in the phase-3 set. Struct/array GC values require codec extension — " +
                "the only transpiler-side producer today is RefI31, which is covered.");
        }

        internal static Value ReadValue(BinaryReader r)
        {
            int typeRaw = ReadVarInt32(r);
            var type = (ValType)typeRaw;
            byte kind = r.ReadByte();

            switch (kind)
            {
                case ValueKindScalar:
                {
                    ulong bits = r.ReadUInt64();
                    Value v = default;
                    v.Type = type;
                    v.Data.Int64 = unchecked((long)bits);
                    return v;
                }
                case ValueKindNullRef:
                {
                    return Value.Null(type);
                }
                case ValueKindFuncRef:
                {
                    long idx = ReadVarInt64(r);
                    return new Value(type, idx, null);
                }
                case ValueKindExternRef:
                {
                    long idx = ReadVarInt64(r);
                    return new Value(type, idx, null);
                }
                case ValueKindI31:
                {
                    int i = ReadVarInt32(r);
                    int masked = i & 0x7FFF_FFFF;
                    return new Value(type, masked, new I31Ref(masked));
                }
                case ValueKindV128:
                {
                    ulong lo = r.ReadUInt64();
                    ulong hi = r.ReadUInt64();
                    // V128(ulong, ulong) overload is implicit via tuple; use
                    // the two-ulong ctor to reconstruct the value directly.
                    var v128 = new V128(lo, hi);
                    Value v = default;
                    v.Type = ValType.V128;
                    v.GcRef = new VecRef(v128);
                    return v;
                }
            }
            throw new InvalidDataException(
                $"InitDataCodec: unknown value kind tag 0x{kind:X2} for type {type}");
        }

        // =================================================================
        // Primitive LEB helpers
        // =================================================================

        internal static void WriteValType(BinaryWriter w, ValType t) => WriteVarInt32(w, (int)t);
        internal static ValType ReadValType(BinaryReader r) => (ValType)ReadVarInt32(r);

        internal static void WriteVarUInt32(BinaryWriter w, uint v)
        {
            while (v >= 0x80) { w.Write((byte)((v & 0x7F) | 0x80)); v >>= 7; }
            w.Write((byte)v);
        }

        internal static uint ReadVarUInt32(BinaryReader r)
        {
            uint result = 0;
            int shift = 0;
            while (true)
            {
                byte b = r.ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift > 35) throw new InvalidDataException("varuint32 too long");
            }
        }

        internal static void WriteVarInt32(BinaryWriter w, int v)
        {
            bool more = true;
            while (more)
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                bool sign = (b & 0x40) != 0;
                more = !((v == 0 && !sign) || (v == -1 && sign));
                if (more) b |= 0x80;
                w.Write(b);
            }
        }

        internal static int ReadVarInt32(BinaryReader r)
        {
            int result = 0;
            int shift = 0;
            byte b;
            do
            {
                b = r.ReadByte();
                result |= (b & 0x7F) << shift;
                shift += 7;
                if (shift > 35) throw new InvalidDataException("varint32 too long");
            } while ((b & 0x80) != 0);
            if (shift < 32 && (b & 0x40) != 0)
                result |= -(1 << shift);
            return result;
        }

        internal static void WriteVarUInt64(BinaryWriter w, ulong v)
        {
            while (v >= 0x80) { w.Write((byte)((v & 0x7F) | 0x80)); v >>= 7; }
            w.Write((byte)v);
        }

        internal static ulong ReadVarUInt64(BinaryReader r)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                byte b = r.ReadByte();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift > 70) throw new InvalidDataException("varuint64 too long");
            }
        }

        internal static void WriteVarInt64(BinaryWriter w, long v)
        {
            bool more = true;
            while (more)
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                bool sign = (b & 0x40) != 0;
                more = !((v == 0 && !sign) || (v == -1 && sign));
                if (more) b |= 0x80;
                w.Write(b);
            }
        }

        internal static long ReadVarInt64(BinaryReader r)
        {
            long result = 0;
            int shift = 0;
            byte b;
            do
            {
                b = r.ReadByte();
                result |= (long)(b & 0x7F) << shift;
                shift += 7;
                if (shift > 70) throw new InvalidDataException("varint64 too long");
            } while ((b & 0x80) != 0);
            if (shift < 64 && (b & 0x40) != 0)
                result |= -(1L << shift);
            return result;
        }
    }
}
