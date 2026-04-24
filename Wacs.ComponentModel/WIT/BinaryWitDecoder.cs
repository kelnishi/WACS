// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Text;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.WIT
{
    /// <summary>
    /// Binary-side decoder for the <c>component-type:*</c> custom
    /// section that <c>wit-component</c> embeds in core wasm
    /// modules (and <c>componentize-dotnet</c> preserves in final
    /// component binaries). Parallel to the text-side
    /// <see cref="WitParser"/>, producing the same
    /// <see cref="CtPackage"/> universe downstream consumers
    /// already speak — the transpiler's emitter doesn't care
    /// which side produced the names.
    ///
    /// <para><b>Encoding shape:</b> the custom-section payload
    /// is itself a Component Model binary — a tiny component
    /// whose single type (tagged <c>0x41</c>) is a nested
    /// <c>ComponentType</c> describing the outer component's
    /// world. Imports / exports / type defs inside that nested
    /// type carry the WIT-level names the structural type
    /// section drops. Top-level export with name like
    /// <c>local:foo/bar@0.1.0</c> gives us the world's fully
    /// qualified name.</para>
    ///
    /// <para><b>Scope, v0:</b> handles the simple case of a
    /// single-world component with primitive / <c>list</c> /
    /// <c>option</c> / <c>result</c> / <c>tuple</c> / <c>record</c>
    /// / <c>enum</c> / <c>variant</c> / <c>flags</c> / <c>resource</c>
    /// types declared directly inside the world's ComponentType.
    /// Nested interfaces, outer aliases, cross-package
    /// <c>use</c> resolution, and <c>include</c> expansion are
    /// follow-ups (the text-side pipeline handles them today).</para>
    /// </summary>
    public static class BinaryWitDecoder
    {
        // Component declarator tags inside a ComponentType (0x41)
        // or InstanceType (0x42). Pulled straight from
        // wasm-encoder/src/component/types.rs.
        private const byte DeclCoreType = 0x00;
        private const byte DeclType     = 0x01;
        private const byte DeclAlias    = 0x02;
        private const byte DeclImport   = 0x03;
        private const byte DeclExport   = 0x04;

        // Type body tags (value-type universe within a Type decl).
        private const byte TagFuncType         = 0x40;
        private const byte TagComponentType    = 0x41;
        private const byte TagInstanceType     = 0x42;
        private const byte TagResourceDef      = 0x3F;

        // Sort bytes for ComponentTypeRef (import/export descriptor).
        private const byte SortCore      = 0x00;
        private const byte SortFunc      = 0x01;
        private const byte SortValue     = 0x02;
        private const byte SortType      = 0x03;
        private const byte SortComponent = 0x04;
        private const byte SortInstance  = 0x05;

        /// <summary>
        /// Decode a <c>component-type:*</c> custom section payload
        /// into a <see cref="CtPackage"/>. Returns <c>null</c> for
        /// inputs the decoder recognizes but can't structurally
        /// process (nested components with imports-of-instances,
        /// outer aliases, etc.) — the caller should fall back to
        /// whatever naming strategy they were using before the
        /// decoder existed.
        /// </summary>
        public static CtPackage? DecodeComponentType(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            var reader = new ComponentBinaryReader(payload);
            if (!TryReadPreamble(reader)) return null;

            // Walk sections, collecting the type table and the
            // top-level export list. Only the type + export
            // sections matter for name recovery.
            var typeTable = new List<NestedType>();
            var topExports = new List<TopLevelExport>();

            while (!reader.AtEnd)
            {
                var id = (ComponentSectionId)reader.ReadByte();
                var size = reader.ReadVarU32();
                var payloadBytes = reader.ReadBytes((int)size);
                switch (id)
                {
                    case ComponentSectionId.Type:
                        DecodeTypeSection(payloadBytes, typeTable);
                        break;
                    case ComponentSectionId.Export:
                        DecodeTopLevelExports(payloadBytes, topExports);
                        break;
                    // Canon, core modules / instances, aliases,
                    // instance section — ignored for name
                    // recovery. Custom sections (including the
                    // "wit-component-encoding" version marker)
                    // fall through here too.
                }
            }

            return BuildPackage(typeTable, topExports);
        }

        /// <summary>Verify magic + version + layer bytes of the
        /// nested component binary. Component layer must be 0x0001
        /// — a core-module layer (0x0000) in this slot means the
        /// producer stuck the wrong blob in the custom section.</summary>
        private static bool TryReadPreamble(ComponentBinaryReader r)
        {
            if (r.Remaining < 8) return false;
            var magic = r.ReadBytes(4);
            if (magic[0] != 0x00 || magic[1] != 0x61 ||
                magic[2] != 0x73 || magic[3] != 0x6D) return false;
            var versionLo = r.ReadByte();
            var versionHi = r.ReadByte();
            var layerLo = r.ReadByte();
            var layerHi = r.ReadByte();
            _ = versionLo; _ = versionHi;
            // Layer must be component (0x0001).
            return layerLo == 0x01 && layerHi == 0x00;
        }

        // -----------------------------------------------------------------
        // Nested-type representation. Each type section entry lands as
        // one of these; cross-references between entries resolve by
        // index into `typeTable`.
        // -----------------------------------------------------------------
        private abstract class NestedType { }

        /// <summary>A <c>ComponentType</c> or <c>InstanceType</c> —
        /// a bundle of declarators that together describe a world
        /// (ComponentType) or an interface instance (InstanceType).</summary>
        private sealed class NestedComponentOrInstance : NestedType
        {
            public bool IsInstance { get; }
            public IReadOnlyList<InnerDecl> Decls { get; }
            public NestedComponentOrInstance(bool isInstance, IReadOnlyList<InnerDecl> decls)
            { IsInstance = isInstance; Decls = decls; }
        }

        /// <summary>A component function signature.</summary>
        private sealed class NestedFunc : NestedType
        {
            public IReadOnlyList<NestedParam> Params { get; }
            public NestedValType? Result { get; }
            public IReadOnlyList<NestedParam>? NamedResults { get; }
            public NestedFunc(IReadOnlyList<NestedParam> pars,
                              NestedValType? result,
                              IReadOnlyList<NestedParam>? namedResults)
            { Params = pars; Result = result; NamedResults = namedResults; }
        }

        /// <summary>A defined value type entry from the type section —
        /// either a structural aggregate (record, variant, enum, …) or
        /// a resource declaration.</summary>
        private sealed class NestedDefType : NestedType
        {
            public NestedValType Type { get; }
            public NestedDefType(NestedValType t) { Type = t; }
        }

        /// <summary>A fresh resource type definition (tag 0x3F).
        /// Resource methods attach via separate export decls inside
        /// the containing instance/component type.</summary>
        private sealed class NestedResource : NestedType
        {
            // Resource definitions carry optional <c>dtor</c>
            // intrinsic refs etc.; we don't need them for naming.
        }

        private sealed class NestedParam
        {
            public string Name { get; }
            public NestedValType Type { get; }
            public NestedParam(string n, NestedValType t) { Name = n; Type = t; }
        }

        // -----------------------------------------------------------------
        // Nested value types — the defvaltype universe inside the
        // component-type binary. Mirrors the shape of CtValType but
        // keeps type-index references unresolved until we build the
        // final CtPackage.
        // -----------------------------------------------------------------
        private abstract class NestedValType { }
        private sealed class NestedPrim : NestedValType
        { public ComponentPrim Prim { get; } public NestedPrim(ComponentPrim p) { Prim = p; } }
        private sealed class NestedRef : NestedValType
        { public uint TypeIdx { get; } public NestedRef(uint i) { TypeIdx = i; } }
        private sealed class NestedListVT : NestedValType
        { public NestedValType Element { get; } public NestedListVT(NestedValType e) { Element = e; } }
        private sealed class NestedOptionVT : NestedValType
        { public NestedValType Inner { get; } public NestedOptionVT(NestedValType i) { Inner = i; } }
        private sealed class NestedResultVT : NestedValType
        {
            public NestedValType? Ok { get; }
            public NestedValType? Err { get; }
            public NestedResultVT(NestedValType? ok, NestedValType? err) { Ok = ok; Err = err; }
        }
        private sealed class NestedTupleVT : NestedValType
        { public IReadOnlyList<NestedValType> Elements { get; }
          public NestedTupleVT(IReadOnlyList<NestedValType> e) { Elements = e; } }
        private sealed class NestedRecordVT : NestedValType
        {
            public IReadOnlyList<(string Name, NestedValType Type)> Fields { get; }
            public NestedRecordVT(IReadOnlyList<(string, NestedValType)> f) { Fields = f; }
        }
        private sealed class NestedVariantVT : NestedValType
        {
            public IReadOnlyList<(string Name, NestedValType? Payload)> Cases { get; }
            public NestedVariantVT(IReadOnlyList<(string, NestedValType?)> c) { Cases = c; }
        }
        private sealed class NestedEnumVT : NestedValType
        { public IReadOnlyList<string> Cases { get; } public NestedEnumVT(IReadOnlyList<string> c) { Cases = c; } }
        private sealed class NestedFlagsVT : NestedValType
        { public IReadOnlyList<string> Flags { get; } public NestedFlagsVT(IReadOnlyList<string> f) { Flags = f; } }
        private sealed class NestedOwnVT : NestedValType
        { public uint TypeIdx { get; } public NestedOwnVT(uint i) { TypeIdx = i; } }
        private sealed class NestedBorrowVT : NestedValType
        { public uint TypeIdx { get; } public NestedBorrowVT(uint i) { TypeIdx = i; } }

        // -----------------------------------------------------------------
        // Inner declarators (inside a ComponentType/InstanceType body).
        // -----------------------------------------------------------------
        private abstract class InnerDecl { }
        private sealed class InnerType : InnerDecl
        { public NestedType Type { get; } public InnerType(NestedType t) { Type = t; } }
        private sealed class InnerExport : InnerDecl
        {
            public string Name { get; }
            public uint TypeIdx { get; }
            public byte SortByte { get; }   // kind the export targets
            public InnerExport(string n, uint idx, byte sort)
            { Name = n; TypeIdx = idx; SortByte = sort; }
        }
        private sealed class InnerImport : InnerDecl
        {
            public string Name { get; }
            public uint TypeIdx { get; }
            public byte SortByte { get; }
            public InnerImport(string n, uint idx, byte sort)
            { Name = n; TypeIdx = idx; SortByte = sort; }
        }
        private sealed class InnerAlias : InnerDecl
        {
            // Aliases carry their own payload (outer, instance-export,
            // core-instance-export). We retain them opaquely for now —
            // real resolution needs visibility into the enclosing
            // component's type table. Not exercised by Phase 1b
            // fixtures.
        }
        private sealed class InnerCoreType : InnerDecl
        {
            // Core types are irrelevant for WIT-level naming.
        }

        private sealed class TopLevelExport
        {
            public string Name { get; }
            public byte SortByte { get; }
            public uint Index { get; }
            public TopLevelExport(string n, byte sort, uint idx)
            { Name = n; SortByte = sort; Index = idx; }
        }

        // -----------------------------------------------------------------
        // Section decoders.
        // -----------------------------------------------------------------

        private static void DecodeTypeSection(byte[] payload, List<NestedType> table)
        {
            var r = new ComponentBinaryReader(payload);
            var count = r.ReadVarU32();
            for (uint i = 0; i < count; i++)
                table.Add(ReadType(r));
        }

        private static NestedType ReadType(ComponentBinaryReader r)
        {
            var tag = r.ReadByte();
            switch (tag)
            {
                case TagFuncType:
                    return ReadFuncType(r);
                case TagComponentType:
                    return ReadComponentOrInstance(r, isInstance: false);
                case TagInstanceType:
                    return ReadComponentOrInstance(r, isInstance: true);
                case TagResourceDef:
                    // Fresh resource type — 1 byte (dtor presence).
                    var hasDtor = r.ReadByte();
                    if (hasDtor == 0x01) r.ReadVarU32();    // dtor func idx
                    return new NestedResource();
                default:
                    return new NestedDefType(ReadValType(r, tag));
            }
        }

        private static NestedFunc ReadFuncType(ComponentBinaryReader r)
        {
            var paramCount = r.ReadVarU32();
            var pars = new NestedParam[paramCount];
            for (uint i = 0; i < paramCount; i++)
            {
                var name = r.ReadName();
                var ty = ReadValType(r);
                pars[i] = new NestedParam(name, ty);
            }
            var resultKind = r.ReadByte();
            NestedValType? single = null;
            IReadOnlyList<NestedParam>? named = null;
            if (resultKind == 0x00)
            {
                single = ReadValType(r);
            }
            else if (resultKind == 0x01)
            {
                var n = r.ReadVarU32();
                var list = new NestedParam[n];
                for (uint i = 0; i < n; i++)
                    list[i] = new NestedParam(r.ReadName(), ReadValType(r));
                named = list;
            }
            else
            {
                throw new FormatException(
                    $"Unexpected functype result-kind byte 0x{resultKind:X2} "
                    + "inside component-type binary.");
            }
            return new NestedFunc(pars, single, named);
        }

        private static NestedComponentOrInstance ReadComponentOrInstance(
            ComponentBinaryReader r, bool isInstance)
        {
            var declCount = r.ReadVarU32();
            var decls = new List<InnerDecl>((int)declCount);
            for (uint i = 0; i < declCount; i++)
                decls.Add(ReadInnerDecl(r));
            return new NestedComponentOrInstance(isInstance, decls);
        }

        private static InnerDecl ReadInnerDecl(ComponentBinaryReader r)
        {
            var kind = r.ReadByte();
            switch (kind)
            {
                case DeclCoreType:
                    // Skip: consume the following core-type body.
                    // Core type tags start at 0x50 (module type),
                    // 0x60 (composite) etc. We don't need them for
                    // WIT naming; walk past by calling
                    // ReadType-equivalent on it. For v0 we bail —
                    // if a component-type section contains core
                    // types the decoder returns null.
                    throw new FormatException(
                        "Core types in component-type sections are a "
                        + "v0 follow-up.");
                case DeclType:
                    return new InnerType(ReadType(r));
                case DeclAlias:
                    SkipAlias(r);
                    return new InnerAlias();
                case DeclImport:
                    return ReadImportOrExport(r, import: true);
                case DeclExport:
                    return ReadImportOrExport(r, import: false);
                default:
                    throw new FormatException(
                        $"Unknown component-type declarator tag 0x{kind:X2}.");
            }
        }

        private static InnerDecl ReadImportOrExport(
            ComponentBinaryReader r, bool import)
        {
            // Both component imports AND exports carry a 0x00
            // namespec prefix (per `encode_component_import_name`
            // / `encode_component_export_name` in wasm-encoder
            // 0.221+). Earlier encoders emitted bare names for
            // imports — if we ever decode pre-2024 components
            // we'd need to peek and conditionally consume.
            r.ReadByte();   // namespec; discard
            var name = r.ReadName();
            var sort = r.ReadByte();
            uint idx = 0;
            switch (sort)
            {
                case SortFunc:
                case SortInstance:
                case SortComponent:
                    idx = r.ReadVarU32();
                    break;
                case SortType:
                    // TypeBounds: 0x00 Eq(idx) | 0x01 SubResource.
                    var bound = r.ReadByte();
                    if (bound == 0x00)
                        idx = r.ReadVarU32();
                    else if (bound == 0x01)
                        idx = uint.MaxValue;   // sub-resource marker
                    else
                        throw new FormatException(
                            $"Unknown TypeBounds tag 0x{bound:X2}.");
                    break;
                case SortCore:
                    // Core-sort import/export — variant byte +
                    // index. We don't use these for naming; skip.
                    r.ReadByte();
                    r.ReadVarU32();
                    idx = 0;
                    break;
                case SortValue:
                    // Value kind — consume the ComponentValType
                    // that follows. Rare; unused by Phase 1b.
                    ReadValType(r);
                    idx = 0;
                    break;
                default:
                    throw new FormatException(
                        $"Unknown import/export sort byte 0x{sort:X2}.");
            }
            return import
                ? (InnerDecl)new InnerImport(name, idx, sort)
                : new InnerExport(name, idx, sort);
        }

        private static void SkipAlias(ComponentBinaryReader r)
        {
            // Alias: sort (1 or 2 bytes) + target.
            var sortByte = r.ReadByte();
            if (sortByte == SortCore)
                r.ReadByte();   // core-sort variant
            var target = r.ReadByte();
            switch (target)
            {
                case 0x00:   // instance-export
                case 0x01:   // core-instance-export
                    r.ReadVarU32();
                    r.ReadName();
                    break;
                case 0x02:   // outer alias
                    r.ReadVarU32();
                    r.ReadVarU32();
                    break;
                default:
                    throw new FormatException(
                        $"Unknown aliastarget tag 0x{target:X2} in "
                        + "component-type section.");
            }
        }

        // -----------------------------------------------------------------
        // Value type readers.
        // -----------------------------------------------------------------

        private static NestedValType ReadValType(ComponentBinaryReader r)
        {
            // ComponentValType = <varint33> for a primitive or
            // type-idx, OR a structural tag byte. The first byte
            // disambiguates: tags <= 0x72 (the encoding range for
            // defvaltypes) fit in one byte, while primitives and
            // type-refs are signed-LEB128 encoded.
            var peek = r.PeekByte();
            if (peek == TagFuncType || peek == TagComponentType
                || peek == TagInstanceType
                || IsDefvaltypeTag(peek))
            {
                r.ReadByte();    // consume tag
                return ReadValType(r, peek);
            }
            // Otherwise fall through to signed varint33 decode.
            var v = r.ReadVarI33();
            if (v < 0) return new NestedPrim((ComponentPrim)v);
            return new NestedRef((uint)v);
        }

        private static NestedValType ReadValType(
            ComponentBinaryReader r, byte tag)
        {
            switch (tag)
            {
                case 0x70:   // list
                    return new NestedListVT(ReadValType(r));
                case 0x6B:   // option
                    return new NestedOptionVT(ReadValType(r));
                case 0x6A:   // result
                    return new NestedResultVT(
                        ReadOptionalValType(r),
                        ReadOptionalValType(r));
                case 0x6F:   // tuple
                {
                    var n = r.ReadVarU32();
                    var elems = new NestedValType[n];
                    for (uint i = 0; i < n; i++) elems[i] = ReadValType(r);
                    return new NestedTupleVT(elems);
                }
                case 0x72:   // record
                {
                    var n = r.ReadVarU32();
                    var fields = new (string, NestedValType)[n];
                    for (uint i = 0; i < n; i++)
                        fields[i] = (r.ReadName(), ReadValType(r));
                    return new NestedRecordVT(fields);
                }
                case 0x71:   // variant
                {
                    var n = r.ReadVarU32();
                    var cases = new (string, NestedValType?)[n];
                    for (uint i = 0; i < n; i++)
                    {
                        var name = r.ReadName();
                        var hasPayload = r.ReadByte();
                        NestedValType? payload = null;
                        if (hasPayload == 0x01)
                            payload = ReadValType(r);
                        else if (hasPayload != 0x00)
                            throw new FormatException(
                                $"Variant case payload-presence byte 0x{hasPayload:X2} "
                                + "is invalid (expected 0x00 or 0x01).");
                        // Refines-index (optional). Spec says each
                        // case carries a refines u32 OR a 0x00 byte
                        // for no-refines. We consume the presence
                        // byte here.
                        var hasRefines = r.ReadByte();
                        if (hasRefines == 0x01) r.ReadVarU32();
                        else if (hasRefines != 0x00)
                            throw new FormatException(
                                $"Variant case refines-presence byte 0x{hasRefines:X2} "
                                + "is invalid.");
                        cases[i] = (name, payload);
                    }
                    return new NestedVariantVT(cases);
                }
                case 0x6D:   // enum
                {
                    var n = r.ReadVarU32();
                    var labels = new string[n];
                    for (uint i = 0; i < n; i++) labels[i] = r.ReadName();
                    return new NestedEnumVT(labels);
                }
                case 0x6E:   // flags
                {
                    var n = r.ReadVarU32();
                    var labels = new string[n];
                    for (uint i = 0; i < n; i++) labels[i] = r.ReadName();
                    return new NestedFlagsVT(labels);
                }
                case 0x69:   // own<T>
                    return new NestedOwnVT(r.ReadVarU32());
                case 0x68:   // borrow<T>
                    return new NestedBorrowVT(r.ReadVarU32());
                default:
                    throw new FormatException(
                        $"Unsupported valtype tag 0x{tag:X2} in "
                        + "component-type section.");
            }
        }

        private static NestedValType? ReadOptionalValType(ComponentBinaryReader r)
        {
            var present = r.ReadByte();
            if (present == 0x00) return null;
            if (present != 0x01)
                throw new FormatException(
                    $"Unexpected optional-valtype presence byte 0x{present:X2}.");
            return ReadValType(r);
        }

        /// <summary>True iff <paramref name="b"/> is one of the
        /// defvaltype tag bytes we decode structurally. Used to
        /// disambiguate the <c>&lt;valtype&gt;</c> encoding between
        /// a single-byte tag vs. a signed varint33 primitive/type-ref.</summary>
        private static bool IsDefvaltypeTag(byte b)
        {
            // Structural tags in the 0x68..0x72 range (own, borrow,
            // result, option, …). Primitives live in 0x73..0x7F
            // (negative LEB128) and type-refs are non-negative
            // varint33 — both outside this range.
            return b >= 0x68 && b <= 0x72;
        }

        private static void DecodeTopLevelExports(
            byte[] payload, List<TopLevelExport> output)
        {
            var r = new ComponentBinaryReader(payload);
            var count = r.ReadVarU32();
            for (uint i = 0; i < count; i++)
            {
                r.ReadByte();   // namespec
                var name = r.ReadName();
                var sort = r.ReadByte();
                if (sort == SortCore)
                    r.ReadByte();    // core-sort variant (unused)
                var idx = r.ReadVarU32();
                // Optional type ascription byte.
                if (!r.AtEnd)
                {
                    var hasType = r.ReadByte();
                    if (hasType == 0x01)
                        r.ReadVarU32();
                }
                output.Add(new TopLevelExport(name, sort, idx));
            }
        }

        // -----------------------------------------------------------------
        // CtPackage / CtWorld builder — project the nested
        // representation onto the Types-layer universe.
        // -----------------------------------------------------------------

        private static CtPackage? BuildPackage(
            List<NestedType> typeTable,
            List<TopLevelExport> topExports)
        {
            // wit-component's encoding nests TWICE:
            //
            //   outer (top-level) export: kind=Type, name="<local-world>",
            //                             idx=N → typeTable[N]
            //   typeTable[N] = ComponentType {
            //     decl 1: Type → nested ComponentType  (the actual world)
            //     decl 2: Export name="<ns>:<path>/<world>@<ver>"
            //             kind=Component, idx=0 (refers to the inner type)
            //   }
            //
            // We pull the qualified name from the inner export and
            // the world body from the inner ComponentType.
            NestedComponentOrInstance? wrapper = null;
            foreach (var e in topExports)
            {
                if (e.SortByte != SortType) continue;
                if (e.Index >= typeTable.Count) continue;
                if (typeTable[(int)e.Index] is NestedComponentOrInstance cit
                    && !cit.IsInstance)
                {
                    wrapper = cit;
                    break;
                }
            }
            if (wrapper == null) return null;

            // Walk the wrapper for (a) the inner ComponentType and
            // (b) the qualified name on its export decl.
            NestedComponentOrInstance? worldType = null;
            string? worldQualifiedName = null;
            int innerTypeCounter = 0;
            var innerTypes = new List<NestedType>();
            foreach (var d in wrapper.Decls)
            {
                if (d is InnerType it)
                {
                    innerTypes.Add(it.Type);
                    innerTypeCounter++;
                }
                else if (d is InnerExport ie && ie.SortByte == SortComponent)
                {
                    if (ie.TypeIdx < innerTypes.Count
                        && innerTypes[(int)ie.TypeIdx]
                            is NestedComponentOrInstance worldCt
                        && !worldCt.IsInstance)
                    {
                        worldType = worldCt;
                        worldQualifiedName = ie.Name;
                        break;
                    }
                }
            }
            if (worldType == null || worldQualifiedName == null) return null;

            var pkgName = ParseQualifiedName(worldQualifiedName, out var localName);
            if (pkgName == null) return null;

            var world = BuildWorld(pkgName, localName, worldType, typeTable);
            return new CtPackage(pkgName, Array.Empty<CtInterfaceType>(),
                                 new[] { world });
        }

        private static CtWorldType BuildWorld(
            CtPackageName pkg, string worldName,
            NestedComponentOrInstance comp,
            List<NestedType> outerTypeTable)
        {
            // Within the ComponentType body, declarators share an
            // index space — every Type-creating decl (Type,
            // Import-with-sort=Type, Alias-with-Type, CoreType,
            // Export-with-sort=Type) allocates a slot. typeIdx
            // references in valtypes resolve through this combined
            // space. Imports/exports of sort=Type also bind a NAME
            // to their slot.
            //
            // Build it up walking decls in order. typeSpace[i]
            // holds either a structural NestedType, or null for
            // an alias slot (whose name + target idx live in the
            // slot's parallel metadata).
            var typeSpace = new List<NestedType?>();
            var typeAliasTo = new Dictionary<int, uint>();   // idx → bound idx
            var typeNames = new Dictionary<uint, string>();
            var imports = new List<CtWorldImport>();
            var exports = new List<CtWorldExport>();

            // Pass 1: populate the type space + name bindings.
            foreach (var decl in comp.Decls)
            {
                switch (decl)
                {
                    case InnerType it:
                        typeSpace.Add(it.Type);
                        break;
                    case InnerImport ii when ii.SortByte == SortType:
                    {
                        var slot = (uint)typeSpace.Count;
                        if (ii.TypeIdx == uint.MaxValue)
                        {
                            // SubResource bound — fresh resource
                            // type whose body is the slot itself.
                            typeSpace.Add(new NestedResource());
                        }
                        else
                        {
                            typeSpace.Add(null);
                            typeAliasTo[(int)slot] = ii.TypeIdx;
                        }
                        typeNames[slot] = ii.Name;
                        break;
                    }
                    case InnerExport ie when ie.SortByte == SortType:
                    {
                        var slot = (uint)typeSpace.Count;
                        if (ie.TypeIdx == uint.MaxValue)
                        {
                            typeSpace.Add(new NestedResource());
                        }
                        else
                        {
                            typeSpace.Add(null);
                            typeAliasTo[(int)slot] = ie.TypeIdx;
                        }
                        typeNames[slot] = ie.Name;
                        break;
                    }
                    case InnerAlias _:
                    case InnerCoreType _:
                        typeSpace.Add(null);
                        break;
                    // Func / Instance / Component imports + exports
                    // don't allocate type slots — they reference
                    // existing types.
                }
            }

            // Pass 2: materialize named structural types. Resolve
            // each named slot through any alias chain to find the
            // structural NestedType, then convert to the Ct shape
            // with the bound name.
            var namedTypes = new List<CtNamedType>();
            foreach (var kv in typeNames)
            {
                if (TryResolveStructuralType(kv.Key, typeSpace,
                        typeAliasTo, out var t))
                {
                    if (t is NestedDefType def)
                    {
                        var ct = ConvertValType(def.Type, kv.Value,
                                                typeSpace, typeNames,
                                                typeAliasTo);
                        namedTypes.Add(new CtNamedType(kv.Value, ct));
                    }
                    else if (t is NestedResource)
                    {
                        namedTypes.Add(new CtNamedType(kv.Value,
                            new CtResourceType(kv.Value,
                                Array.Empty<CtResourceMethod>())));
                    }
                }
            }

            // Pass 3: function + interface imports/exports.
            foreach (var decl in comp.Decls)
            {
                switch (decl)
                {
                    case InnerExport ie when ie.SortByte == SortFunc:
                    {
                        var ctFn = ResolveFunc(ie.TypeIdx, typeSpace,
                                               typeNames, typeAliasTo);
                        if (ctFn != null)
                            exports.Add(new CtWorldExport(ie.Name,
                                new CtExternFunc(ctFn)));
                        break;
                    }
                    case InnerImport ii when ii.SortByte == SortFunc:
                    {
                        var ctFn = ResolveFunc(ii.TypeIdx, typeSpace,
                                               typeNames, typeAliasTo);
                        if (ctFn != null)
                            imports.Add(new CtWorldImport(ii.Name,
                                new CtExternFunc(ctFn)));
                        break;
                    }
                    case InnerExport ie when ie.SortByte == SortInstance:
                    {
                        exports.Add(new CtWorldExport(ie.Name,
                            new CtExternInterfaceRef(null, ie.Name)));
                        break;
                    }
                    case InnerImport ii when ii.SortByte == SortInstance:
                    {
                        imports.Add(new CtWorldImport(ii.Name,
                            new CtExternInterfaceRef(null, ii.Name)));
                        break;
                    }
                }
            }

            return new CtWorldType(pkg, worldName, namedTypes,
                Array.Empty<CtUse>(), imports, exports,
                Array.Empty<CtWorldInclude>());
        }

        /// <summary>Walk the alias chain at <paramref name="idx"/>
        /// until a slot with a structural <see cref="NestedType"/>
        /// is found.</summary>
        private static bool TryResolveStructuralType(
            uint idx, List<NestedType?> typeSpace,
            Dictionary<int, uint> typeAliasTo,
            out NestedType? result)
        {
            var visited = new HashSet<uint>();
            while (idx < typeSpace.Count)
            {
                if (!visited.Add(idx)) break;   // cycle guard
                var slot = typeSpace[(int)idx];
                if (slot != null) { result = slot; return true; }
                if (typeAliasTo.TryGetValue((int)idx, out var next)
                    && next != idx)
                {
                    idx = next;
                    continue;
                }
                break;
            }
            result = null;
            return false;
        }

        private static CtFunctionType? ResolveFunc(
            uint idx, List<NestedType?> typeSpace,
            Dictionary<uint, string> typeNames,
            Dictionary<int, uint> typeAliasTo)
        {
            if (!TryResolveStructuralType(idx, typeSpace, typeAliasTo,
                    out var t) || !(t is NestedFunc fn)) return null;
            var pars = new CtFuncParam[fn.Params.Count];
            for (int i = 0; i < pars.Length; i++)
                pars[i] = new CtFuncParam(fn.Params[i].Name,
                    ConvertValType(fn.Params[i].Type, "",
                                   typeSpace, typeNames, typeAliasTo));
            CtValType? single = fn.Result == null ? null
                : ConvertValType(fn.Result, "", typeSpace,
                                 typeNames, typeAliasTo);
            IReadOnlyList<CtFuncParam>? named = null;
            if (fn.NamedResults != null)
            {
                var list = new CtFuncParam[fn.NamedResults.Count];
                for (int i = 0; i < list.Length; i++)
                    list[i] = new CtFuncParam(fn.NamedResults[i].Name,
                        ConvertValType(fn.NamedResults[i].Type, "",
                                       typeSpace, typeNames, typeAliasTo));
                named = list;
            }
            return new CtFunctionType(pars, single, named);
        }

        /// <summary>Convert a nested value type to the typed
        /// <see cref="CtValType"/> universe, propagating
        /// <paramref name="nameHint"/> onto named aggregates
        /// when they're the direct target of a type-export.</summary>
        private static CtValType ConvertValType(
            NestedValType t, string nameHint,
            List<NestedType?> typeSpace,
            Dictionary<uint, string> typeNames,
            Dictionary<int, uint> typeAliasTo)
        {
            switch (t)
            {
                case NestedPrim p:
                    return PrimToCt(p.Prim);
                case NestedRef r:
                    // If the referenced slot carries a name,
                    // surface the symbolic CtTypeRef so downstream
                    // resolves through the named-types pool.
                    // Otherwise inline the structural body
                    // (anonymous structural types are common when
                    // wit-component factors out reusable shapes
                    // like option<string>).
                    if (typeNames.TryGetValue(r.TypeIdx, out var refName))
                        return new CtTypeRef(refName);
                    if (TryResolveStructuralType(r.TypeIdx, typeSpace,
                            typeAliasTo, out var refTy)
                        && refTy is NestedDefType refDef)
                        return ConvertValType(refDef.Type, "",
                            typeSpace, typeNames, typeAliasTo);
                    return new CtTypeRef("_Type_" + r.TypeIdx);
                case NestedListVT l:
                    return new CtListType(ConvertValType(l.Element, "",
                        typeSpace, typeNames, typeAliasTo));
                case NestedOptionVT o:
                    return new CtOptionType(ConvertValType(o.Inner, "",
                        typeSpace, typeNames, typeAliasTo));
                case NestedResultVT res:
                    return new CtResultType(
                        res.Ok == null ? null : ConvertValType(res.Ok, "",
                            typeSpace, typeNames, typeAliasTo),
                        res.Err == null ? null : ConvertValType(res.Err, "",
                            typeSpace, typeNames, typeAliasTo));
                case NestedTupleVT tup:
                {
                    var es = new CtValType[tup.Elements.Count];
                    for (int i = 0; i < es.Length; i++)
                        es[i] = ConvertValType(tup.Elements[i], "",
                            typeSpace, typeNames, typeAliasTo);
                    return new CtTupleType(es);
                }
                case NestedRecordVT rec:
                {
                    var fs = new CtField[rec.Fields.Count];
                    for (int i = 0; i < fs.Length; i++)
                        fs[i] = new CtField(rec.Fields[i].Name,
                            ConvertValType(rec.Fields[i].Type, "",
                                typeSpace, typeNames, typeAliasTo));
                    return new CtRecordType(nameHint, fs);
                }
                case NestedVariantVT vr:
                {
                    var cs = new CtVariantCase[vr.Cases.Count];
                    for (int i = 0; i < cs.Length; i++)
                        cs[i] = new CtVariantCase(vr.Cases[i].Name,
                            vr.Cases[i].Payload == null ? null
                                : ConvertValType(vr.Cases[i].Payload!, "",
                                    typeSpace, typeNames, typeAliasTo));
                    return new CtVariantType(nameHint, cs);
                }
                case NestedEnumVT en:
                    return new CtEnumType(nameHint, en.Cases);
                case NestedFlagsVT fl:
                    return new CtFlagsType(nameHint, fl.Flags);
                case NestedOwnVT ow:
                    return new CtOwnType(new CtTypeRef(
                        typeNames.TryGetValue(ow.TypeIdx, out var on)
                            ? on : "_Type_" + ow.TypeIdx));
                case NestedBorrowVT bo:
                    return new CtBorrowType(new CtTypeRef(
                        typeNames.TryGetValue(bo.TypeIdx, out var bn)
                            ? bn : "_Type_" + bo.TypeIdx));
                default:
                    throw new InvalidOperationException(
                        "Unsupported nested value type " + t.GetType().Name);
            }
        }

        private static CtValType PrimToCt(ComponentPrim p) => p switch
        {
            ComponentPrim.Bool   => CtPrimType.Bool,
            ComponentPrim.S8     => CtPrimType.S8,
            ComponentPrim.S16    => CtPrimType.S16,
            ComponentPrim.S32    => CtPrimType.S32,
            ComponentPrim.S64    => CtPrimType.S64,
            ComponentPrim.U8     => CtPrimType.U8,
            ComponentPrim.U16    => CtPrimType.U16,
            ComponentPrim.U32    => CtPrimType.U32,
            ComponentPrim.U64    => CtPrimType.U64,
            ComponentPrim.F32    => CtPrimType.F32,
            ComponentPrim.F64    => CtPrimType.F64,
            ComponentPrim.Char   => CtPrimType.Char,
            ComponentPrim.String => CtPrimType.String,
            _ => throw new NotSupportedException(
                "Unknown component primitive " + p),
        };

        /// <summary>Parse a world/interface export name like
        /// <c>local:os/os@0.1.0</c> into <c>(CtPackageName, localName)</c>.
        /// Accepts unversioned names (<c>local:os/os</c>) too.</summary>
        private static CtPackageName? ParseQualifiedName(
            string name, out string localName)
        {
            localName = name;
            // Find last '/'
            var slash = name.LastIndexOf('/');
            if (slash < 0) return null;
            var nsAndPath = name.Substring(0, slash);
            var tail = name.Substring(slash + 1);
            string? version = null;
            var at = tail.IndexOf('@');
            if (at >= 0)
            {
                version = tail.Substring(at + 1);
                tail = tail.Substring(0, at);
            }
            localName = tail;
            // namespace:path
            var colon = nsAndPath.IndexOf(':');
            if (colon < 0) return null;
            var ns = nsAndPath.Substring(0, colon);
            var rawPath = nsAndPath.Substring(colon + 1);
            var path = rawPath.Split(new[] { '/' },
                StringSplitOptions.RemoveEmptyEntries);
            return new CtPackageName(ns, path, version);
        }
    }
}
