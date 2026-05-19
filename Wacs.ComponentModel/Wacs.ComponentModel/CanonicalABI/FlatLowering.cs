// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using Wacs.ComponentModel.Runtime.Parser;

namespace Wacs.ComponentModel.CanonicalABI
{
    /// <summary>
    /// Canonical-ABI flat-lowering analysis: for any
    /// <see cref="ComponentValType"/> resolved against a
    /// component's type-section table, compute how many core
    /// values (i32-equivalent slots) it flattens to per the
    /// canon-ABI rules in
    /// <c>WebAssembly/component-model</c>'s
    /// <c>design/mvp/CanonicalABI.md</c>.
    ///
    /// <para>Used by the canon-async lift adapter to:</para>
    /// <list type="bullet">
    ///   <item>Pick the right delegate signature for
    ///     <c>task.return</c> / <c>context.{get,set}</c> /
    ///     <c>future.{read,write}</c>'s lowered host
    ///     entry points.</item>
    ///   <item>Decide when a result exceeds the flat-count
    ///     threshold and must be returned via the
    ///     return-area pointer convention (single i32, with
    ///     the actual struct laid out in memory at that
    ///     pointer).</item>
    /// </list>
    ///
    /// <para><b>Flat-lowering rules</b> (per spec):</para>
    /// <list type="bullet">
    ///   <item>Primitive (bool / sN / uN / fN / char / string):
    ///     1 core value, except <c>string</c> which is 2
    ///     (ptr, len).</item>
    ///   <item><c>list&lt;T&gt;</c>: 2 (ptr, count) — independent of T.</item>
    ///   <item><c>record</c> / <c>tuple</c>: sum of field/element
    ///     flat-counts.</item>
    ///   <item><c>variant { c1(T1), …, cN(TN) }</c>: 1 (disc) +
    ///     max(flat-count of each payload, treating
    ///     no-payload cases as 0).</item>
    ///   <item><c>enum</c>: 1 (disc only — no payloads).</item>
    ///   <item><c>flags</c>: ⌈ N / 32 ⌉ where N is the flag count.
    ///     ≤32 → 1 i32 bitfield.</item>
    ///   <item><c>option&lt;T&gt;</c>: 1 + flat-count(T).</item>
    ///   <item><c>result&lt;T, E&gt;</c>: 1 + max(flat-count(T),
    ///     flat-count(E)). Either side may be absent
    ///     (treated as 0).</item>
    ///   <item><c>own&lt;R&gt;</c> / <c>borrow&lt;R&gt;</c>:
    ///     1 (handle).</item>
    ///   <item><c>stream&lt;T&gt;</c> / <c>future&lt;T&gt;</c> /
    ///     <c>error-context</c>: 1 (handle — the data plane
    ///     lives in the dispatcher).</item>
    /// </list>
    ///
    /// <para>The current spec's threshold for return-area
    /// indirection is 1 result. (Single result returns as
    /// flat values; multiple results return through a single
    /// i32 pointer that addresses a struct in memory.) For
    /// <c>task.return rs</c>, <c>rs</c> is at most one
    /// resultlist entry, so the flat-count of the entry IS the
    /// delegate's parameter count.</para>
    /// </summary>
    public static class FlatLowering
    {
        /// <summary>
        /// Compute the canon-ABI flat-count for
        /// <paramref name="t"/>. Aggregate types are resolved
        /// recursively through <paramref name="types"/>
        /// (the component's type-section index space).
        ///
        /// <para>Returns <c>-1</c> when an aggregate references
        /// a typeidx not present in
        /// <paramref name="types"/> or a shape not yet
        /// recognized — the caller can use this as a "fall back
        /// to return-area indirection" sentinel.</para>
        /// </summary>
        public static int FlatCount(
            ComponentValType t,
            IReadOnlyList<DefTypeEntry>? types)
        {
            if (t.IsPrimitive)
            {
                // string: (ptr, len). All other primitives: 1.
                return t.Prim == ComponentPrim.String ? 2 : 1;
            }
            if (types == null || t.TypeIdx >= types.Count) return -1;
            var def = types[(int)t.TypeIdx];
            return FlatCountDef(def, types);
        }

        /// <summary>Flat-count for a top-level type-table entry.</summary>
        public static int FlatCountDef(
            DefTypeEntry def, IReadOnlyList<DefTypeEntry>? types)
        {
            switch (def)
            {
                case ComponentListType _:
                    return 2; // (ptr, count)
                case ComponentStreamType _:
                case ComponentFutureType _:
                case ComponentErrorContextType _:
                case ComponentOwnType _:
                case ComponentBorrowType _:
                case ComponentResourceType _:
                    return 1; // handle
                case ComponentEnumType _:
                    return 1; // disc only
                case ComponentFlagsType flags:
                    // ⌈ N / 32 ⌉
                    return (flags.Flags.Count + 31) / 32;
                case ComponentRecordType rec:
                    return SumFields(rec.Fields, types);
                case ComponentTupleType tup:
                    return SumElements(tup.Elements, types);
                case ComponentVariantType variant:
                    return 1 + MaxCasePayload(variant.Cases, types);
                case ComponentOptionType opt:
                    return 1 + FlatCount(opt.Inner, types);
                case ComponentResultType res:
                    int ok = res.Ok.HasValue
                        ? FlatCount(res.Ok.Value, types) : 0;
                    int err = res.Err.HasValue
                        ? FlatCount(res.Err.Value, types) : 0;
                    return 1 + (ok > err ? ok : err);
                case ComponentFuncType _:
                    // Function types don't have a value-shape
                    // flat-count — they're declared, not lowered.
                    return -1;
                case RawDefType _:
                    // Unknown to the parser; the caller's
                    // decision whether to fall back to
                    // return-area or fail.
                    return -1;
                default:
                    return -1;
            }
        }

        private static int SumFields(
            IReadOnlyList<ComponentRecordType.Field> fields,
            IReadOnlyList<DefTypeEntry>? types)
        {
            int total = 0;
            foreach (var f in fields)
            {
                int c = FlatCount(f.Type, types);
                if (c < 0) return -1;
                total += c;
            }
            return total;
        }

        private static int SumElements(
            IReadOnlyList<ComponentValType> elements,
            IReadOnlyList<DefTypeEntry>? types)
        {
            int total = 0;
            foreach (var e in elements)
            {
                int c = FlatCount(e, types);
                if (c < 0) return -1;
                total += c;
            }
            return total;
        }

        private static int MaxCasePayload(
            IReadOnlyList<ComponentVariantType.Case> cases,
            IReadOnlyList<DefTypeEntry>? types)
        {
            int max = 0;
            foreach (var c in cases)
            {
                if (c.Payload == null) continue;
                int n = FlatCount(c.Payload.Value, types);
                if (n < 0) return -1;
                if (n > max) max = n;
            }
            return max;
        }
    }
}
