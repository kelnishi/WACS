// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Linq;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.Harness.Lib
{
    /// <summary>
    /// Canonical-ABI memory layout helpers. Per
    /// <see href="https://github.com/WebAssembly/component-model/blob/main/design/mvp/CanonicalABI.md"/>:
    /// every WIT type has a deterministic size + alignment, and
    /// aggregates (record / variant / tuple) compute theirs from the
    /// constituent fields with a small set of rules. This module owns
    /// those rules so the lift / lower emitters can ask
    /// "what offset is field N at?" without re-deriving the math.
    ///
    /// <para>v0 covers the subset the richer-spike fixture exercises:
    /// primitives, records of primitives, variants of unit / primitive /
    /// record cases. Strings, lists, options, results, tuples, flags,
    /// enums, and inline-interface types throw at emit-time so the
    /// gap is loud, not silently mis-laid-out.</para>
    /// </summary>
    internal static class CanonicalAbi
    {
        public static int SizeOf(CtValType t) => Layout(t).Size;
        public static int AlignOf(CtValType t) => Layout(t).Align;

        public static (int Size, int Align) Layout(CtValType t)
        {
            // Named types come through as CtTypeRef → CtNamedType.Type.
            // Layout depends on the underlying shape, not the alias.
            t = Deref(t);
            switch (t)
            {
                case CtPrimType prim:
                    return prim.Kind switch
                    {
                        CtPrim.Bool or CtPrim.S8 or CtPrim.U8 => (1, 1),
                        CtPrim.S16 or CtPrim.U16 => (2, 2),
                        CtPrim.S32 or CtPrim.U32 or CtPrim.Char or CtPrim.F32 => (4, 4),
                        CtPrim.S64 or CtPrim.U64 or CtPrim.F64 => (8, 8),
                        // String: (ptr, len) — two i32s in memory.
                        CtPrim.String => (8, 4),
                        _ => throw new NotSupportedException($"Unhandled primitive {prim.Kind}."),
                    };

                case CtListType:
                    // list<T> in memory is (ptr, count) — two i32s.
                    // The element array's body lives at ptr; size +
                    // alignment of the list-typed field itself is
                    // the ptr+count pair, independent of T's size.
                    return (8, 4);

                case CtRecordType rec:
                    return LayoutRecord(rec);

                case CtVariantType variant:
                    return LayoutVariant(variant);

                case CtEnumType en:
                    {
                        // Enum is a payload-less variant — disc width
                        // (1 / 2 / 4) based on case count.
                        var w = VariantDiscSize(en.Cases.Count);
                        return (w, w);
                    }

                case CtFlagsType fl:
                    {
                        // Flags pack one bit per flag, rounded up to
                        // byte / ushort / uint storage. ≤ 8 → 1 byte,
                        // ≤ 16 → 2 bytes, else 4.
                        var w = FlagsByteWidth(fl.Flags.Count);
                        return (w, w);
                    }

                case CtOptionType opt:
                    {
                        // option<T> = variant { none, some(T) }.
                        // 1-byte disc + payload at align_up(1, T_align)
                        // + size T_size, total aligned to max(disc, T_align).
                        var (innerSize, innerAlign) = Layout(opt.Inner);
                        int payloadOffset = AlignUp(1, innerAlign);
                        int totalAlign = Math.Max(1, innerAlign);
                        int totalSize = AlignUp(payloadOffset + innerSize, totalAlign);
                        return (totalSize, totalAlign);
                    }

                case CtResultType res:
                    {
                        // result<T, E> = variant { ok(T?), err(E?) }.
                        // Same shape as a 2-case variant; either side
                        // may be elided (treated as size 0 / align 1).
                        int okSize = 0, okAlign = 1, errSize = 0, errAlign = 1;
                        if (res.Ok != null) (okSize, okAlign) = Layout(res.Ok);
                        if (res.Err != null) (errSize, errAlign) = Layout(res.Err);
                        int maxCaseAlign = Math.Max(okAlign, errAlign);
                        int maxCaseSize = Math.Max(okSize, errSize);
                        int payloadOffset = AlignUp(1, maxCaseAlign);
                        int totalAlign = Math.Max(1, maxCaseAlign);
                        int totalSize = AlignUp(payloadOffset + maxCaseSize, totalAlign);
                        return (totalSize, totalAlign);
                    }

                case CtTupleType tup:
                    return LayoutTuple(tup);

                // own<R> / borrow<R> / a bare resource reference all
                // serialize to a single i32 handle on the wasm side.
                case CtResourceType:
                case CtOwnType:
                case CtBorrowType:
                    return (4, 4);

                default:
                    throw new NotSupportedException(
                        $"CanonicalAbi.Layout: {t.GetType().Name} not supported in harness v0.2.");
            }
        }

        /// <summary>
        /// Field offsets for a record, indexed in declaration order.
        /// Records lay out fields in source order, each padded up to
        /// its own alignment from the running offset.
        /// </summary>
        public static int[] RecordFieldOffsets(CtRecordType rec)
        {
            var offsets = new int[rec.Fields.Count];
            int running = 0;
            for (int i = 0; i < rec.Fields.Count; i++)
            {
                var f = rec.Fields[i];
                var (_, align) = Layout(f.Type);
                running = AlignUp(running, align);
                offsets[i] = running;
                running += SizeOf(f.Type);
            }
            return offsets;
        }

        /// <summary>
        /// Discriminator byte-width: 1 / 2 / 4 byte based on case count
        /// (per spec — variants with ≤ 256 cases fit a u8).
        /// </summary>
        public static int VariantDiscSize(int caseCount)
        {
            if (caseCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(caseCount));
            if (caseCount <= 256) return 1;
            if (caseCount <= 65536) return 2;
            return 4;
        }

        /// <summary>
        /// Flags backing byte width: ≤ 8 bits → 1 byte, ≤ 16 → 2 bytes,
        /// ≤ 32 → 4 bytes. Larger flag sets aren't currently supported
        /// by the harness emitter.
        /// </summary>
        public static int FlagsByteWidth(int flagCount)
        {
            if (flagCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(flagCount));
            if (flagCount <= 8) return 1;
            if (flagCount <= 16) return 2;
            if (flagCount <= 32) return 4;
            throw new NotSupportedException(
                $"Harness emitter v0 supports flag sets up to 32 bits (got {flagCount}).");
        }

        /// <summary>
        /// Payload offset within a variant — the byte at which the
        /// payload (if any) starts. Discriminator at 0, padded up to
        /// the max-case alignment.
        /// </summary>
        public static int VariantPayloadOffset(CtVariantType variant)
        {
            int discSize = VariantDiscSize(variant.Cases.Count);
            int maxCaseAlign = MaxCaseAlign(variant);
            return AlignUp(discSize, maxCaseAlign);
        }

        private static int MaxCaseAlign(CtVariantType variant)
        {
            int max = 1; // discriminator-only fallback
            foreach (var c in variant.Cases)
            {
                if (c.Payload != null)
                {
                    var (_, align) = Layout(c.Payload);
                    if (align > max) max = align;
                }
            }
            return max;
        }

        private static (int Size, int Align) LayoutRecord(CtRecordType rec)
        {
            int running = 0;
            int maxAlign = 1;
            foreach (var f in rec.Fields)
            {
                var (size, align) = Layout(f.Type);
                if (align > maxAlign) maxAlign = align;
                running = AlignUp(running, align);
                running += size;
            }
            running = AlignUp(running, maxAlign);
            return (running, maxAlign);
        }

        private static (int Size, int Align) LayoutTuple(CtTupleType tup)
        {
            int running = 0;
            int maxAlign = 1;
            foreach (var e in tup.Elements)
            {
                var (size, align) = Layout(e);
                if (align > maxAlign) maxAlign = align;
                running = AlignUp(running, align);
                running += size;
            }
            running = AlignUp(running, maxAlign);
            return (running, maxAlign);
        }

        /// <summary>
        /// Per-element offsets for a tuple in declaration order. Same
        /// rule as <see cref="RecordFieldOffsets"/>: positional packing
        /// with per-element alignment padding.
        /// </summary>
        public static int[] TupleElementOffsets(CtTupleType tup)
        {
            var offsets = new int[tup.Elements.Count];
            int running = 0;
            for (int i = 0; i < tup.Elements.Count; i++)
            {
                var (size, align) = Layout(tup.Elements[i]);
                running = AlignUp(running, align);
                offsets[i] = running;
                running += size;
            }
            return offsets;
        }

        private static (int Size, int Align) LayoutVariant(CtVariantType variant)
        {
            int discSize = VariantDiscSize(variant.Cases.Count);
            int maxCaseAlign = MaxCaseAlign(variant);
            int maxCaseSize = variant.Cases
                .Where(c => c.Payload != null)
                .Select(c => SizeOf(c.Payload!))
                .DefaultIfEmpty(0)
                .Max();
            int payloadOffset = AlignUp(discSize, maxCaseAlign);
            int totalAlign = Math.Max(discSize, maxCaseAlign);
            int totalSize = AlignUp(payloadOffset + maxCaseSize, totalAlign);
            return (totalSize, totalAlign);
        }

        public static int AlignUp(int value, int align)
        {
            return (value + align - 1) & ~(align - 1);
        }

        /// <summary>
        /// Resolve a <see cref="CtTypeRef"/> chain to its underlying
        /// structural type. Layout + emission both work on the
        /// structural shape, not the alias.
        /// </summary>
        public static CtValType Deref(CtValType t)
        {
            while (t is CtTypeRef r && r.Target != null)
                t = r.Target.Type;
            return t;
        }
    }
}
