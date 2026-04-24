// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Collections.Generic;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Walks a <see cref="CtWorldType"/>'s transitive type graph to
    /// answer yes/no questions about type-kind usage — used to
    /// decide which world-shell helper types to emit (e.g.,
    /// <c>Option&lt;T&gt;</c> only when some signature has
    /// <c>option&lt;T&gt;</c>, <c>InteropString</c> only when some
    /// signature has <c>string</c>).
    ///
    /// <para>The walk follows <see cref="CtExternInterfaceRef.Target"/>
    /// where bound, so resolved world refs pull in their referenced
    /// interfaces' types and functions. Unresolved refs are skipped
    /// — they can't contribute type usage we know about. For the
    /// hello-world shell case (pre-resolution), this means the
    /// helpers-emission behaviour is "conservative": helpers that
    /// would be needed after resolution aren't emitted before
    /// resolution, matching what wit-bindgen would produce for a
    /// world lacking the referenced packages.</para>
    /// </summary>
    internal static class TypeUsageScan
    {
        /// <summary>True if any signature reachable from the world uses <c>option&lt;T&gt;</c>.</summary>
        public static bool UsesOption(CtWorldType world)
            => ScanWorld(world, TypeKind.Option);

        /// <summary>True if any signature reachable from the world uses <c>string</c>.</summary>
        public static bool UsesString(CtWorldType world)
            => ScanWorld(world, TypeKind.String);

        /// <summary>True if any signature reachable from the world uses <c>result&lt;…&gt;</c>.</summary>
        public static bool UsesResult(CtWorldType world)
            => ScanWorld(world, TypeKind.Result);

        private enum TypeKind { Option, String, Result }

        private static bool ScanWorld(CtWorldType world, TypeKind kind)
        {
            var seen = new HashSet<CtInterfaceType>();
            foreach (var nt in world.Types)
                if (ScanVal(nt.Type, kind, seen)) return true;
            foreach (var imp in world.Imports)
                if (ScanExtern(imp.Spec, kind, seen)) return true;
            foreach (var exp in world.Exports)
                if (ScanExtern(exp.Spec, kind, seen)) return true;
            return false;
        }

        private static bool ScanExtern(CtExternType spec, TypeKind kind,
                                       HashSet<CtInterfaceType> seen)
        {
            return spec switch
            {
                CtExternFunc fn => ScanFuncSig(fn.Function, kind, seen),
                CtExternInlineInterface ii => ScanInterface(ii.Interface, kind, seen),
                CtExternInterfaceRef iref when iref.Target != null =>
                    ScanInterface(iref.Target, kind, seen),
                _ => false,
            };
        }

        private static bool ScanInterface(CtInterfaceType iface, TypeKind kind,
                                          HashSet<CtInterfaceType> seen)
        {
            if (!seen.Add(iface)) return false;
            foreach (var nt in iface.Types)
                if (ScanVal(nt.Type, kind, seen)) return true;
            foreach (var fn in iface.Functions)
                if (ScanFuncSig(fn.Type, kind, seen)) return true;
            return false;
        }

        private static bool ScanFuncSig(CtFunctionType sig, TypeKind kind,
                                        HashSet<CtInterfaceType> seen)
        {
            foreach (var p in sig.Params)
                if (ScanVal(p.Type, kind, seen)) return true;
            if (sig.Result != null && ScanVal(sig.Result, kind, seen)) return true;
            if (sig.NamedResults != null)
                foreach (var p in sig.NamedResults)
                    if (ScanVal(p.Type, kind, seen)) return true;
            return false;
        }

        private static bool ScanVal(CtValType t, TypeKind kind,
                                    HashSet<CtInterfaceType> seen)
        {
            switch (t)
            {
                case CtPrimType p:
                    return kind == TypeKind.String && p.Kind == CtPrim.String;
                case CtOptionType o:
                    return kind == TypeKind.Option
                        || ScanVal(o.Inner, kind, seen);
                case CtResultType r:
                    if (kind == TypeKind.Result) return true;
                    return (r.Ok != null && ScanVal(r.Ok, kind, seen))
                        || (r.Err != null && ScanVal(r.Err, kind, seen));
                case CtListType l:
                    return ScanVal(l.Element, kind, seen);
                case CtTupleType tup:
                    foreach (var el in tup.Elements)
                        if (ScanVal(el, kind, seen)) return true;
                    return false;
                case CtRecordType rec:
                    foreach (var f in rec.Fields)
                        if (ScanVal(f.Type, kind, seen)) return true;
                    return false;
                case CtVariantType v:
                    foreach (var c in v.Cases)
                        if (c.Payload != null && ScanVal(c.Payload, kind, seen))
                            return true;
                    return false;
                case CtOwnType own:
                    return ScanVal(own.Resource, kind, seen);
                case CtBorrowType bor:
                    return ScanVal(bor.Resource, kind, seen);
                case CtTypeRef tr:
                    // Follow resolved refs only; unresolved external
                    // refs can't be scanned without the target.
                    return tr.Target != null
                        && ScanVal(tr.Target.Type, kind, seen);
                default:
                    return false;
            }
        }
    }
}
