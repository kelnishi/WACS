// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using Wacs.ComponentModel.Types;
using Wacs.ComponentModel.WIT;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Compile-time WIT contract validator. Given the harness's
    /// embedded <c>_WitContract</c> text and the component binary's
    /// decoded WIT, diffs the worlds structurally and surfaces every
    /// mismatch as a typed message — same shape interpreter consumers
    /// would see if they validated at <c>LoadFrom</c> time.
    ///
    /// <para>v0 scope: world name + export set + per-export signature
    /// (params + return) compared via deep structural equality on the
    /// resolved <see cref="CtValType"/> tree. Imports are not validated
    /// (the harness contract typically asserts what the embedder
    /// invokes, not what the component imports). Resources, interface
    /// re-exports, and use-deduplication are follow-ups.</para>
    /// </summary>
    public static class WitContractCompare
    {
        /// <summary>
        /// Validate that <paramref name="actualWit"/> (decoded from the
        /// component binary's <c>component-type:*</c> custom section)
        /// matches the contract described by <paramref name="expectedWitText"/>
        /// (the harness's embedded <c>_WitContract</c> string).
        /// Returns a list of mismatch messages; empty list means match.
        /// </summary>
        public static IReadOnlyList<string> Diff(
            string expectedWitText, CtPackage actualWit)
        {
            if (expectedWitText == null) throw new ArgumentNullException(nameof(expectedWitText));
            if (actualWit == null) throw new ArgumentNullException(nameof(actualWit));

            var diffs = new List<string>();

            CtPackage expectedPkg;
            try
            {
                var parsed = WitParser.Parse(expectedWitText);
                var converted = WitToTypes.Convert(parsed);
                WitResolver.Resolve(converted);
                expectedPkg = converted.FirstOrDefault(p => p.Worlds.Count > 0)
                    ?? throw new InvalidOperationException(
                        "Harness _WitContract parses but contains no worlds.");
            }
            catch (Exception ex)
            {
                diffs.Add($"harness _WitContract failed to parse: {ex.Message}");
                return diffs;
            }

            // The actual package may come pre-resolved (custom-section
            // path runs WitResolver elsewhere) or unresolved (primary-
            // section decode produces CtTypeRefs without Target set).
            // Resolve here idempotently so TypesEqual's Deref hop
            // finds the structural body on both sides.
            WitResolver.Resolve(new[] { actualWit });

            // WitResolver only binds CtTypeRefs declared inside
            // interfaces. The primary-section decoder places named
            // types in `world.Types`, which WitResolver ignores.
            // Walk the world's named types and bind any matching
            // CtTypeRefs in the world's export / import signatures.
            BindWorldLevelTypeRefs(actualWit);
            BindWorldLevelTypeRefs(expectedPkg);

            var expectedWorld = expectedPkg.Worlds.FirstOrDefault();
            var actualWorld = actualWit.Worlds.FirstOrDefault();
            if (expectedWorld == null)
            {
                diffs.Add("harness _WitContract has no world.");
                return diffs;
            }
            if (actualWorld == null)
            {
                diffs.Add("component WIT has no world.");
                return diffs;
            }

            // World names: kebab-case match. Exception: when the
            // decoder couldn't recover the actual world name from
            // the binary and synthesized "root" (the primary-section
            // fallback path's default), treat it as a wildcard
            // rather than reporting a name mismatch — the export-
            // shape comparison below is the actual validity signal.
            bool actualIsSynthesizedRoot = string.Equals(actualWorld.Name, "root", StringComparison.Ordinal);
            if (!actualIsSynthesizedRoot
                && !string.Equals(expectedWorld.Name, actualWorld.Name, StringComparison.Ordinal))
            {
                diffs.Add($"world name: harness expects '{expectedWorld.Name}', "
                    + $"component declares '{actualWorld.Name}'.");
            }

            // Export set comparison. Index by name; flag adds + drops
            // + signature mismatches separately so the user can act on
            // each.
            var expectedExports = expectedWorld.Exports
                .Where(e => e.Spec is CtExternFunc)
                .ToDictionary(e => e.Name, e => ((CtExternFunc)e.Spec).Function);
            var actualExports = actualWorld.Exports
                .Where(e => e.Spec is CtExternFunc)
                .ToDictionary(e => e.Name, e => ((CtExternFunc)e.Spec).Function);

            foreach (var (name, expectedFn) in expectedExports)
            {
                if (!actualExports.TryGetValue(name, out var actualFn))
                {
                    diffs.Add($"export '{name}': declared in harness, missing from component.");
                    continue;
                }
                CompareFunctionSignatures("export", name, expectedFn, actualFn, diffs);
            }
            foreach (var name in actualExports.Keys)
            {
                if (!expectedExports.ContainsKey(name))
                    diffs.Add($"export '{name}': present in component, not declared in harness.");
            }

            // Imports comparison: every WIT-declared import must be
            // present in the binary with matching signature. The
            // reverse direction (component imports not in WIT) is
            // intentionally NOT a hard mismatch — wit-bindgen
            // auto-bundles WASI imports the harness can't realistically
            // pre-declare, and the user supplies them via the
            // LoadFrom `bindImports` callback. v0 only checks
            // inline-Func imports; interface-reference imports
            // (`import wasi:io/poll@0.2.0`) are reported as a
            // currently-unvalidated shape so the user knows the
            // check isn't covering them.
            var expectedFnImports = expectedWorld.Imports
                .Where(p => p.Spec is CtExternFunc)
                .ToDictionary(p => p.Name, p => ((CtExternFunc)p.Spec).Function);
            var actualFnImports = actualWorld.Imports
                .Where(p => p.Spec is CtExternFunc)
                .ToDictionary(p => p.Name, p => ((CtExternFunc)p.Spec).Function);

            foreach (var (name, expectedFn) in expectedFnImports)
            {
                if (!actualFnImports.TryGetValue(name, out var actualFn))
                {
                    diffs.Add($"import '{name}': declared in harness, missing from component.");
                    continue;
                }
                CompareFunctionSignatures("import", name, expectedFn, actualFn, diffs);
            }

            // Surface harness-declared interface imports as
            // "not validated" so the user knows the contract
            // includes shapes outside the v0 diff. Avoids silent
            // partial-coverage.
            foreach (var p in expectedWorld.Imports)
            {
                if (p.Spec is CtExternInterfaceRef)
                    diffs.Add($"import '{p.Name}' (interface ref): v0 validator does not yet compare interface-import shapes.");
            }

            return diffs;
        }

        private static void CompareFunctionSignatures(
            string kind, string portName, CtFunctionType expected, CtFunctionType actual,
            List<string> diffs)
        {
            if (expected.Params.Count != actual.Params.Count)
            {
                diffs.Add($"{kind} '{portName}': param arity differs "
                    + $"(harness {expected.Params.Count}, component {actual.Params.Count}).");
                return;
            }

            for (int i = 0; i < expected.Params.Count; i++)
            {
                var ep = expected.Params[i];
                var ap = actual.Params[i];
                if (!TypesEqual(ep.Type, ap.Type))
                {
                    diffs.Add($"{kind} '{portName}' param {i} ('{ep.Name}' / '{ap.Name}'): "
                        + $"type differs (harness {Describe(ep.Type)}, component {Describe(ap.Type)}).");
                }
            }

            var er = expected.Result;
            var ar = actual.Result;
            if (er == null && ar != null)
                diffs.Add($"{kind} '{portName}': harness expects no return, component returns {Describe(ar)}.");
            else if (er != null && ar == null)
                diffs.Add($"{kind} '{portName}': harness expects return {Describe(er)}, component returns none.");
            else if (er != null && ar != null && !TypesEqual(er, ar))
                diffs.Add($"{kind} '{portName}': return type differs "
                    + $"(harness {Describe(er)}, component {Describe(ar)}).");
        }

        /// <summary>
        /// Deep structural equality on <see cref="CtValType"/>.
        /// Derefs <see cref="CtTypeRef"/> on both sides so the
        /// comparison runs on the underlying shape, not the alias.
        /// </summary>
        private static bool TypesEqual(CtValType a, CtValType b)
        {
            a = Deref(a);
            b = Deref(b);
            if (a.GetType() != b.GetType()) return false;
            switch (a)
            {
                case CtPrimType pa when b is CtPrimType pb:
                    return pa.Kind == pb.Kind;
                case CtListType la when b is CtListType lb:
                    return TypesEqual(la.Element, lb.Element);
                case CtOptionType oa when b is CtOptionType ob:
                    return TypesEqual(oa.Inner, ob.Inner);
                case CtResultType ra when b is CtResultType rb:
                    return NullableTypesEqual(ra.Ok, rb.Ok) && NullableTypesEqual(ra.Err, rb.Err);
                case CtTupleType ta when b is CtTupleType tb:
                    if (ta.Elements.Count != tb.Elements.Count) return false;
                    for (int i = 0; i < ta.Elements.Count; i++)
                        if (!TypesEqual(ta.Elements[i], tb.Elements[i])) return false;
                    return true;
                case CtRecordType reca when b is CtRecordType recb:
                    if (reca.Fields.Count != recb.Fields.Count) return false;
                    for (int i = 0; i < reca.Fields.Count; i++)
                    {
                        if (!string.Equals(reca.Fields[i].Name, recb.Fields[i].Name, StringComparison.Ordinal))
                            return false;
                        if (!TypesEqual(reca.Fields[i].Type, recb.Fields[i].Type)) return false;
                    }
                    return true;
                case CtVariantType va when b is CtVariantType vb:
                    if (va.Cases.Count != vb.Cases.Count) return false;
                    for (int i = 0; i < va.Cases.Count; i++)
                    {
                        if (!string.Equals(va.Cases[i].Name, vb.Cases[i].Name, StringComparison.Ordinal))
                            return false;
                        if (!NullableTypesEqual(va.Cases[i].Payload, vb.Cases[i].Payload))
                            return false;
                    }
                    return true;
                default:
                    // Conservative — unknown types compare unequal so
                    // the validator surfaces the gap instead of silently
                    // passing a mismatch.
                    return false;
            }
        }

        private static bool NullableTypesEqual(CtValType? a, CtValType? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return TypesEqual(a, b);
        }

        private static CtValType Deref(CtValType t)
        {
            while (t is CtTypeRef r && r.Target != null)
                t = r.Target.Type;
            return t;
        }

        /// <summary>
        /// Walk every world in <paramref name="pkg"/> and bind
        /// unresolved <see cref="CtTypeRef"/>s in the world's import
        /// / export signatures against the world's own
        /// <see cref="CtWorldType.Types"/> entries. Closes the gap
        /// <see cref="WitResolver"/> leaves for primary-section-
        /// decoded packages where named types live at world scope
        /// rather than inside interfaces.
        /// </summary>
        private static void BindWorldLevelTypeRefs(CtPackage pkg)
        {
            foreach (var world in pkg.Worlds)
            {
                if (world.Types.Count == 0) continue;
                var byName = world.Types.ToDictionary(n => n.Name, n => n);
                foreach (var port in world.Exports)
                    BindPort(port.Spec, byName);
                foreach (var port in world.Imports)
                    BindPort(port.Spec, byName);
                foreach (var nt in world.Types)
                    BindTypeRefsIn(nt.Type, byName);
            }
        }

        private static void BindPort(CtExternType spec, IDictionary<string, CtNamedType> byName)
        {
            if (spec is CtExternFunc fn)
            {
                foreach (var p in fn.Function.Params)
                    BindTypeRefsIn(p.Type, byName);
                if (fn.Function.Result != null)
                    BindTypeRefsIn(fn.Function.Result, byName);
                if (fn.Function.NamedResults != null)
                    foreach (var r in fn.Function.NamedResults)
                        BindTypeRefsIn(r.Type, byName);
            }
            // Inline interfaces / interface refs at world scope:
            // not yet bound here. Surface as "not validated" in v0.
        }

        private static void BindTypeRefsIn(CtValType t, IDictionary<string, CtNamedType> byName)
        {
            switch (t)
            {
                case CtTypeRef r when r.Target == null:
                    if (byName.TryGetValue(r.Name, out var target))
                        r.Target = target;
                    return;
                case CtListType l: BindTypeRefsIn(l.Element, byName); return;
                case CtOptionType o: BindTypeRefsIn(o.Inner, byName); return;
                case CtResultType res:
                    if (res.Ok != null) BindTypeRefsIn(res.Ok, byName);
                    if (res.Err != null) BindTypeRefsIn(res.Err, byName);
                    return;
                case CtTupleType tup:
                    foreach (var e in tup.Elements) BindTypeRefsIn(e, byName);
                    return;
                case CtRecordType rec:
                    foreach (var f in rec.Fields) BindTypeRefsIn(f.Type, byName);
                    return;
                case CtVariantType v:
                    foreach (var c in v.Cases)
                        if (c.Payload != null) BindTypeRefsIn(c.Payload, byName);
                    return;
            }
        }

        /// <summary>
        /// Render a <see cref="CtValType"/> as a short WIT-ish form for
        /// diagnostic messages. Not a round-trippable serializer —
        /// purely human-readable.
        /// </summary>
        private static string Describe(CtValType t)
        {
            t = Deref(t);
            return t switch
            {
                CtPrimType p => p.Kind.ToString().ToLowerInvariant(),
                CtListType l => $"list<{Describe(l.Element)}>",
                CtOptionType o => $"option<{Describe(o.Inner)}>",
                CtResultType r => "result<" +
                    (r.Ok == null ? "_" : Describe(r.Ok)) + ", " +
                    (r.Err == null ? "_" : Describe(r.Err)) + ">",
                CtTupleType tup => "tuple<" + string.Join(", ", tup.Elements.Select(Describe)) + ">",
                CtRecordType rec => $"record {rec.Name}",
                CtVariantType v => $"variant {v.Name}",
                _ => t.GetType().Name,
            };
        }
    }
}
