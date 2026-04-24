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
    /// v0 decodes only function types (tag 0x40). Aggregate
    /// types (record, variant, list, option, result, tuple,
    /// enum, flags), resource types, and nested component /
    /// instance types land as opaque <see cref="RawDefType"/>
    /// entries so downstream consumers can at least see their
    /// presence without the parser choking.
    /// </summary>
    public abstract class DefTypeEntry { }

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
                default:
                    // Unknown structural tag — capture the rest
                    // of the payload so the type slot occupies
                    // the right index in the table. In practice
                    // this shouldn't happen for tiny-component;
                    // aggregate/resource decoders land as they're
                    // needed for larger components.
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
