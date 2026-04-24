// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.ComponentModel.Runtime.Parser
{
    /// <summary>
    /// Primitive component-value-type codes. Encoded as signed
    /// LEB128 with the sign bit set (so the byte values are
    /// 0x73..0x7F in one-byte form). Match WIT's primitive
    /// spelling on a 1:1 basis.
    /// </summary>
    public enum ComponentPrim : int
    {
        Bool   = -0x01,
        S8     = -0x02,
        U8     = -0x03,
        S16    = -0x04,
        U16    = -0x05,
        S32    = -0x06,
        U32    = -0x07,
        S64    = -0x08,
        U64    = -0x09,
        F32    = -0x0A,
        F64    = -0x0B,
        Char   = -0x0C,
        String = -0x0D,
    }

    /// <summary>
    /// A component value type — either a primitive or a
    /// reference to a previously-declared type in the component's
    /// type table. Encoded as <c>varint33</c> — negative ↔
    /// primitive, non-negative ↔ <c>deftype</c>-table index.
    /// </summary>
    public readonly struct ComponentValType
    {
        /// <summary>True iff this slot is a primitive; false for
        /// a type-table reference.</summary>
        public bool IsPrimitive { get; }

        /// <summary>When <see cref="IsPrimitive"/>, the primitive
        /// kind. Undefined otherwise.</summary>
        public ComponentPrim Prim { get; }

        /// <summary>When !<see cref="IsPrimitive"/>, the type-table
        /// index. Undefined otherwise.</summary>
        public uint TypeIdx { get; }

        private ComponentValType(bool isPrim, ComponentPrim prim, uint idx)
        {
            IsPrimitive = isPrim;
            Prim = prim;
            TypeIdx = idx;
        }

        public static ComponentValType OfPrim(ComponentPrim p) =>
            new ComponentValType(true, p, 0);
        public static ComponentValType OfRef(uint idx) =>
            new ComponentValType(false, default, idx);
    }

    /// <summary>
    /// A deftype entry — one slot of the component's type table.
    /// v0 decodes function types (tag 0x40) and list types
    /// (tag 0x70). Other aggregate types (record, variant,
    /// option, result, tuple, enum, flags), resource types, and
    /// nested component / instance types land as opaque
    /// <see cref="RawDefType"/> entries so downstream consumers
    /// can at least see their presence without the parser
    /// choking.
    /// </summary>
    public abstract class DefTypeEntry { }

    /// <summary>
    /// <c>list&lt;T&gt;</c> type (tag 0x70). The element is any
    /// <see cref="ComponentValType"/> — primitive or a
    /// reference to another deftype.
    /// </summary>
    public sealed class ComponentListType : DefTypeEntry
    {
        public ComponentValType Element { get; }
        public ComponentListType(ComponentValType element) { Element = element; }
    }

    /// <summary>
    /// <c>option&lt;T&gt;</c> type (tag 0x6b). The inner is any
    /// <see cref="ComponentValType"/>. Lowers to a
    /// (discriminant-byte, payload-slot) pair where the payload
    /// is either zero-initialized (None) or holds T (Some).
    /// </summary>
    public sealed class ComponentOptionType : DefTypeEntry
    {
        public ComponentValType Inner { get; }
        public ComponentOptionType(ComponentValType inner) { Inner = inner; }
    }

    /// <summary>
    /// <c>result&lt;Ok, Err&gt;</c> type (tag 0x6a). Either side
    /// may be absent (encoded as the 0x00 "no value" prefix
    /// instead of a value type). Lowers to (discriminant-byte,
    /// payload-slot) where the payload slot is sized to the
    /// larger of Ok / Err's lowered widths.
    /// </summary>
    public sealed class ComponentResultType : DefTypeEntry
    {
        public ComponentValType? Ok { get; }
        public ComponentValType? Err { get; }
        public ComponentResultType(ComponentValType? ok, ComponentValType? err)
        {
            Ok = ok;
            Err = err;
        }
    }

    /// <summary>
    /// <c>tuple&lt;T1, T2, …&gt;</c> type (tag 0x6f). Flat
    /// positional vector of value types, laid out consecutively
    /// per each element's natural alignment. No discriminant.
    /// </summary>
    public sealed class ComponentTupleType : DefTypeEntry
    {
        public IReadOnlyList<ComponentValType> Elements { get; }
        public ComponentTupleType(IReadOnlyList<ComponentValType> elements)
        { Elements = elements; }
    }

    /// <summary>
    /// <c>enum { foo, bar, baz }</c> type (tag 0x6D) — a fixed
    /// set of named cases, no payloads. Stored as a single
    /// integer of width determined by case count: ≤256 → u8,
    /// ≤65536 → u16, else u32. Case names are intrinsic to the
    /// type's structural encoding; the type name itself only
    /// surfaces via the WIT-side metadata (the embedded
    /// <c>component-type:*</c> section).
    /// </summary>
    public sealed class ComponentEnumType : DefTypeEntry
    {
        public IReadOnlyList<string> Cases { get; }
        public ComponentEnumType(IReadOnlyList<string> cases) { Cases = cases; }
    }

    /// <summary>
    /// <c>flags { a, b, c }</c> type (tag 0x6E) — a bitfield
    /// where each named flag occupies one bit. Wire width is
    /// determined by flag count: ≤8 → u8, ≤16 → u16, ≤32 → u32,
    /// >32 → packed across multiple u32s (rare in practice).
    /// </summary>
    public sealed class ComponentFlagsType : DefTypeEntry
    {
        public IReadOnlyList<string> Flags { get; }
        public ComponentFlagsType(IReadOnlyList<string> flags) { Flags = flags; }
    }

    /// <summary>
    /// <c>record { f1: T1, f2: T2, … }</c> type (tag 0x72) —
    /// a fixed-arity heterogeneous product with named fields.
    /// Fields are laid out consecutively in declaration order,
    /// each at its natural alignment per the canonical-ABI rule.
    /// </summary>
    public sealed class ComponentRecordType : DefTypeEntry
    {
        public sealed class Field
        {
            public string Name { get; }
            public ComponentValType Type { get; }
            public Field(string name, ComponentValType type)
            { Name = name; Type = type; }
        }

        public IReadOnlyList<Field> Fields { get; }
        public ComponentRecordType(IReadOnlyList<Field> fields)
        { Fields = fields; }
    }

    /// <summary>
    /// <c>own&lt;R&gt;</c> handle type (tag 0x69). At the wire
    /// level, an own handle is a single i32 — the index into the
    /// component's per-resource handle table. Surfaces here so
    /// IsEmittable can recognize own-typed returns and the
    /// emitter can wrap the returned i32 in a generated C#
    /// resource class.
    /// </summary>
    public sealed class ComponentOwnType : DefTypeEntry
    {
        public uint TypeIdx { get; }
        public ComponentOwnType(uint typeIdx) { TypeIdx = typeIdx; }
    }

    /// <summary>
    /// <c>borrow&lt;R&gt;</c> handle type (tag 0x68). Same wire
    /// shape as <see cref="ComponentOwnType"/> but call-scoped —
    /// the binding surface invalidates the handle on return so
    /// the caller can't outlive the call.
    /// </summary>
    public sealed class ComponentBorrowType : DefTypeEntry
    {
        public uint TypeIdx { get; }
        public ComponentBorrowType(uint typeIdx) { TypeIdx = typeIdx; }
    }

    /// <summary>
    /// Fresh resource type definition (tag 0x3F). Carries no
    /// structure of its own — the type is opaque, identified
    /// solely by its slot. Optional destructor reference is
    /// preserved for fidelity but unused at the IL emit level
    /// (Dispose() routes through the canon resource.drop
    /// intrinsic).
    /// </summary>
    public sealed class ComponentResourceType : DefTypeEntry
    {
        public uint? DtorFuncIdx { get; }
        public ComponentResourceType(uint? dtorFuncIdx)
        { DtorFuncIdx = dtorFuncIdx; }
    }

    /// <summary>
    /// <c>variant { case_name [(T)] [refines other], … }</c>
    /// type (tag 0x71) — a tagged sum where each case carries an
    /// optional payload type. Wire layout: a discriminant byte
    /// (size determined by case count) followed by a payload
    /// slot aligned and sized to the worst-case payload across
    /// all cases.
    /// </summary>
    public sealed class ComponentVariantType : DefTypeEntry
    {
        public sealed class Case
        {
            public string Name { get; }
            public ComponentValType? Payload { get; }
            /// <summary>Optional <c>refines</c> reference — the
            /// case's discriminant inherits from another's. Rare;
            /// preserved for fidelity but unused at the IL emit
            /// level.</summary>
            public uint? RefinesIdx { get; }
            public Case(string name, ComponentValType? payload, uint? refinesIdx)
            { Name = name; Payload = payload; RefinesIdx = refinesIdx; }
        }

        public IReadOnlyList<Case> Cases { get; }
        public ComponentVariantType(IReadOnlyList<Case> cases)
        { Cases = cases; }
    }

    /// <summary>
    /// Function type (tag 0x40): ordered param list (each with a
    /// name) + ordered unnamed result list. Matches <c>functype</c>
    /// in the Component Model binary spec.
    /// </summary>
    public sealed class ComponentFuncType : DefTypeEntry
    {
        public sealed class Param
        {
            public string Name { get; }
            public ComponentValType Type { get; }
            public Param(string name, ComponentValType type)
            { Name = name; Type = type; }
        }

        public IReadOnlyList<Param> Params { get; }

        /// <summary>
        /// Unnamed results (most common form — one result or
        /// zero). Named results are a separate encoding that's
        /// rare in practice; not yet decoded.
        /// </summary>
        public IReadOnlyList<ComponentValType> Results { get; }

        public ComponentFuncType(
            IReadOnlyList<Param> @params,
            IReadOnlyList<ComponentValType> results)
        {
            Params = @params;
            Results = results;
        }
    }

    /// <summary>
    /// Fallback entry for deftype tags the v0 decoder doesn't
    /// recognize structurally yet. Preserves the tag byte and
    /// the raw payload so downstream tooling can at least
    /// report "type at idx N is a record / variant / …" even
    /// before the shape-specific decoder lands.
    /// </summary>
    public sealed class RawDefType : DefTypeEntry
    {
        public byte Tag { get; }
        public byte[] RemainingPayload { get; }

        public RawDefType(byte tag, byte[] remainingPayload)
        {
            Tag = tag;
            RemainingPayload = remainingPayload;
        }
    }

    /// <summary>
    /// Decodes the component <c>type</c> section
    /// (<see cref="ComponentSectionId.Type"/> = 0x07). v0 scope:
    /// function types with primitive params / results. Aggregate
    /// and resource types land as <see cref="RawDefType"/> so the
    /// type table's length + tag metadata is preserved even when
    /// the shape isn't yet structurally decoded.
    /// </summary>
    public static class TypeSectionReader
    {
        public const byte FuncTypeTag = 0x40;
        public const byte ListTypeTag = 0x70;
        public const byte OptionTypeTag = 0x6B;
        public const byte ResultTypeTag = 0x6A;
        public const byte TupleTypeTag = 0x6F;
        public const byte EnumTypeTag = 0x6D;
        public const byte FlagsTypeTag = 0x6E;
        public const byte VariantTypeTag = 0x71;
        public const byte RecordTypeTag = 0x72;
        public const byte BorrowTypeTag = 0x68;
        public const byte OwnTypeTag = 0x69;
        public const byte ResourceTypeTag = 0x3F;

        public static List<DefTypeEntry> Decode(byte[] payload)
        {
            var reader = new ComponentBinaryReader(payload);
            var count = reader.ReadVarU32();
            var entries = new List<DefTypeEntry>((int)count);
            for (uint i = 0; i < count; i++)
                entries.Add(DecodeEntry(reader));
            return entries;
        }

        private static DefTypeEntry DecodeEntry(ComponentBinaryReader r)
        {
            var tag = r.ReadByte();
            switch (tag)
            {
                case FuncTypeTag:
                    return DecodeFuncType(r);
                case ListTypeTag:
                    return new ComponentListType(DecodeValType(r));
                case OptionTypeTag:
                    return new ComponentOptionType(DecodeValType(r));
                case ResultTypeTag:
                    return DecodeResultType(r);
                case TupleTypeTag:
                    return DecodeTupleType(r);
                case EnumTypeTag:
                    return DecodeEnumType(r);
                case FlagsTypeTag:
                    return DecodeFlagsType(r);
                case RecordTypeTag:
                    return DecodeRecordType(r);
                case VariantTypeTag:
                    return DecodeVariantType(r);
                case OwnTypeTag:
                    return new ComponentOwnType(r.ReadVarU32());
                case BorrowTypeTag:
                    return new ComponentBorrowType(r.ReadVarU32());
                case ResourceTypeTag:
                {
                    // Optional dtor presence byte: 0x00 absent,
                    // 0x01 present + funcidx.
                    var hasDtor = r.ReadByte();
                    uint? dtor = null;
                    if (hasDtor == 0x01) dtor = r.ReadVarU32();
                    else if (hasDtor != 0x00)
                        throw new FormatException(
                            $"Unexpected resource dtor presence byte 0x{hasDtor:X2}.");
                    return new ComponentResourceType(dtor);
                }
                default:
                    // Unknown structural tag — capture the rest
                    // of the payload so the type slot occupies
                    // the right index in the table. Other
                    // aggregate decoders (record, variant, option,
                    // result, tuple, enum, flags, resource) land
                    // as their shapes are needed.
                    return new RawDefType(tag, System.Array.Empty<byte>());
            }
        }

        private static ComponentFuncType DecodeFuncType(ComponentBinaryReader r)
        {
            var paramCount = r.ReadVarU32();
            var pars = new ComponentFuncType.Param[paramCount];
            for (uint i = 0; i < paramCount; i++)
            {
                var name = r.ReadName();
                var ty = DecodeValType(r);
                pars[i] = new ComponentFuncType.Param(name, ty);
            }
            // Result encoding: 0x00 = single unnamed result,
            // 0x01 = named results vec. v0 handles 0x00 only.
            var resultKind = r.ReadByte();
            IReadOnlyList<ComponentValType> results;
            if (resultKind == 0x00)
            {
                results = new[] { DecodeValType(r) };
            }
            else if (resultKind == 0x01)
            {
                var n = r.ReadVarU32();
                var list = new ComponentValType[n];
                for (uint i = 0; i < n; i++)
                {
                    r.ReadName();    // result name (discarded for now)
                    list[i] = DecodeValType(r);
                }
                results = list;
            }
            else
            {
                throw new FormatException(
                    $"Unexpected functype result-kind byte 0x{resultKind:X2}.");
            }
            return new ComponentFuncType(pars, results);
        }

        /// <summary>Decode a result's optional sides. Each side
        /// is prefixed by a presence byte: 0x00 = absent, 0x01 =
        /// present (followed by a value type). Matches the spec's
        /// <c>result</c> encoding.</summary>
        private static ComponentResultType DecodeResultType(ComponentBinaryReader r)
        {
            var ok = DecodeOptionalValType(r);
            var err = DecodeOptionalValType(r);
            return new ComponentResultType(ok, err);
        }

        private static ComponentTupleType DecodeTupleType(ComponentBinaryReader r)
        {
            var n = r.ReadVarU32();
            var elements = new ComponentValType[n];
            for (uint i = 0; i < n; i++)
                elements[i] = DecodeValType(r);
            return new ComponentTupleType(elements);
        }

        private static ComponentEnumType DecodeEnumType(ComponentBinaryReader r)
        {
            var n = r.ReadVarU32();
            var labels = new string[n];
            for (uint i = 0; i < n; i++)
                labels[i] = r.ReadName();
            return new ComponentEnumType(labels);
        }

        private static ComponentFlagsType DecodeFlagsType(ComponentBinaryReader r)
        {
            var n = r.ReadVarU32();
            var labels = new string[n];
            for (uint i = 0; i < n; i++)
                labels[i] = r.ReadName();
            return new ComponentFlagsType(labels);
        }

        private static ComponentRecordType DecodeRecordType(ComponentBinaryReader r)
        {
            var n = r.ReadVarU32();
            var fields = new ComponentRecordType.Field[n];
            for (uint i = 0; i < n; i++)
            {
                var name = r.ReadName();
                var ty = DecodeValType(r);
                fields[i] = new ComponentRecordType.Field(name, ty);
            }
            return new ComponentRecordType(fields);
        }

        private static ComponentVariantType DecodeVariantType(ComponentBinaryReader r)
        {
            var n = r.ReadVarU32();
            var cases = new ComponentVariantType.Case[n];
            for (uint i = 0; i < n; i++)
            {
                var name = r.ReadName();
                var hasPayload = r.ReadByte();
                ComponentValType? payload = null;
                if (hasPayload == 0x01)
                    payload = DecodeValType(r);
                else if (hasPayload != 0x00)
                    throw new FormatException(
                        $"Variant case payload-presence byte 0x{hasPayload:X2} "
                        + "is invalid (expected 0x00 or 0x01).");
                var hasRefines = r.ReadByte();
                uint? refinesIdx = null;
                if (hasRefines == 0x01)
                    refinesIdx = r.ReadVarU32();
                else if (hasRefines != 0x00)
                    throw new FormatException(
                        $"Variant case refines-presence byte 0x{hasRefines:X2} "
                        + "is invalid.");
                cases[i] = new ComponentVariantType.Case(name, payload, refinesIdx);
            }
            return new ComponentVariantType(cases);
        }

        private static ComponentValType? DecodeOptionalValType(ComponentBinaryReader r)
        {
            var present = r.ReadByte();
            if (present == 0x00) return null;
            if (present != 0x01)
                throw new FormatException(
                    $"Unexpected optional-valtype presence byte 0x{present:X2}.");
            return DecodeValType(r);
        }

        /// <summary>Read a value type (signed LEB128) — negative
        /// maps to primitive, non-negative to type-table
        /// index.</summary>
        private static ComponentValType DecodeValType(ComponentBinaryReader r)
        {
            var v = r.ReadVarI33();
            if (v < 0)
                return ComponentValType.OfPrim((ComponentPrim)v);
            if (v > uint.MaxValue)
                throw new FormatException(
                    "Type-table index out of 32-bit range.");
            return ComponentValType.OfRef((uint)v);
        }
    }
}
