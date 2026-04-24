// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Text;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Emits a C# type reference for a <see cref="CtValType"/>.
    /// Matches wit-bindgen-csharp's mapping (confirmed against
    /// wit-bindgen 0.30.0 output for function-per-aggregate WITs).
    ///
    /// <para><b>Phase 1a.2 scope:</b> primitives plus structural
    /// aggregates (<c>list&lt;T&gt;</c>, <c>option&lt;T&gt;</c>,
    /// <c>result&lt;T, E&gt;</c>, <c>tuple&lt;…&gt;</c>). User-defined
    /// types (records, variants, enums, flags, resources, own/borrow)
    /// throw <see cref="NotImplementedException"/> — they ship in
    /// follow-up commits as the corresponding type emitters land.</para>
    ///
    /// <para><b>Position matters for <c>result&lt;T, E&gt;</c>:</b>
    /// wit-bindgen emits <c>result</c> asymmetrically — in the
    /// return position it elides to the <c>Ok</c> type (<c>Err</c>
    /// becomes a thrown exception in the Interop trampoline); in
    /// the parameter position it emits the full
    /// <c>Result&lt;Ok, Err&gt;</c> generic. Callers use
    /// <see cref="EmitParam"/> vs <see cref="EmitReturn"/>
    /// accordingly.</para>
    /// </summary>
    internal static class TypeRefEmit
    {
        /// <summary>
        /// Default emission — parameter-position. Use this when the
        /// type appears in a parameter slot; for return slots use
        /// <see cref="EmitReturn"/> so <c>result</c> unwraps.
        /// </summary>
        public static string Emit(CtValType type) => EmitParam(type);

        /// <summary>Parameter-position emission.</summary>
        public static string EmitParam(CtValType type)
        {
            return type switch
            {
                CtPrimType p => EmitPrim(p.Kind),
                CtListType l => EmitParam(l.Element) + "[]",
                CtOptionType o => EmitParam(o.Inner) + "?",
                CtResultType r => EmitResultParam(r),
                CtTupleType t => EmitTuple(t),
                // own<T> / borrow<T> — C# has no handle-type
                // distinction; the resource class itself carries
                // the handle internally, so both collapse to the
                // resource's class name. wit-bindgen 0.30.0 always
                // emits the fully-qualified `global::…` path for
                // resource-typed parameters / returns — even when
                // the reference is to the enclosing class or to a
                // same-interface resource. We mirror that.
                CtOwnType o => EmitResourceRefQualified(o.Resource),
                CtBorrowType b => EmitResourceRefQualified(b.Resource),
                // Same-interface refs emit unqualified; cross-interface
                // refs emit a fully qualified `global::...` path.
                // Bare identifier pointing at a resource type is
                // treated like <c>own&lt;R&gt;</c> — always qualify.
                CtTypeRef r => EmitTypeRefHandlingResource(r),
                _ => throw new NotImplementedException(
                    "CSharp emission for " + type.GetType().Name +
                    " is a Phase 1a.2 follow-up."),
            };
        }

        private static string EmitResourceRefQualified(CtValType inner)
        {
            if (inner is CtTypeRef r && r.Target != null)
            {
                var target = r.Target;
                while (target.Type is CtTypeRef innerRef
                       && innerRef.Target != null
                       && innerRef.Target != target)
                {
                    target = innerRef.Target;
                }
                if (target.Type is CtResourceType)
                    return EmitQualifiedPath(target);
            }
            return EmitParam(inner);
        }

        private static string EmitTypeRefHandlingResource(CtTypeRef r)
        {
            if (r.Target != null)
            {
                var target = r.Target;
                while (target.Type is CtTypeRef innerRef
                       && innerRef.Target != null
                       && innerRef.Target != target)
                {
                    target = innerRef.Target;
                }
                if (target.Type is CtResourceType)
                    return EmitQualifiedPath(target);
            }
            return EmitTypeRef(r);
        }

        /// <summary>
        /// Return-position emission. Differs from
        /// <see cref="EmitParam"/> only for <c>result</c>:
        /// <c>result&lt;T, E&gt;</c> unwraps to <c>T</c> (the trampoline
        /// throws <c>WitException</c> with the <c>E</c> payload on
        /// the err path); <c>result</c> with both sides elided
        /// returns <c>void</c> — see
        /// <see cref="FunctionEmit.EmitReturnType"/>.
        /// </summary>
        public static string EmitReturn(CtValType type)
        {
            if (type is CtResultType r)
            {
                if (r.Ok == null && r.Err == null)
                    return "void";
                if (r.Ok != null)
                    return EmitReturn(r.Ok);
                // result<_, E> — err-only — wit-bindgen emits `void`
                // (trampoline throws on err; ok path is implicit).
                return "void";
            }
            return EmitParam(type);
        }

        /// <summary>
        /// Emit a <see cref="CtTypeRef"/> either as a bare
        /// PascalCase name (same-interface) or as a
        /// <c>global::{WorldNs}.wit.imports.{pkg}.I{Iface}.{Name}</c>
        /// path (cross-interface). Follows the local-alias
        /// indirection that the WIT resolver leaves in place:
        /// when the target is a same-interface alias whose body is
        /// itself a CtTypeRef pointing elsewhere, traverse the
        /// chain to the real definition.
        /// </summary>
        internal static string EmitTypeRef(CtTypeRef r)
        {
            if (r.Target == null)
                return NameConventions.ToPascalCase(r.Name);

            var emittingIface = EmitAmbient.EmittingInterface;
            var target = r.Target;

            // Follow the resolver's local-alias chain: if the target
            // is a local alias in the current interface whose body is
            // a cross-interface CtTypeRef, traverse it.
            if (emittingIface != null && target.Owner == emittingIface
                && target.Type is CtTypeRef inner && inner.Target != null)
            {
                target = inner.Target;
            }

            // Type alias to a non-declarable body (primitive, list,
            // option, tuple, result, etc.) — inline the underlying
            // type at the use site rather than using the alias name.
            // Matches wit-bindgen: `type count = u32; record { x:
            // count }` emits `public readonly uint x;` not
            // `readonly Count x;`.
            if (target.Type != null && !IsDeclarableBody(target.Type))
                return EmitParam(target.Type);

            // Always-qualify mode (Interop files — siblings of the
            // interface, not nested inside it) → emit global:: path
            // regardless of same/cross-interface status.
            if (EmitAmbient.AlwaysQualifyTypeRefs && target.Owner != null)
            {
                return EmitCrossInterfaceRef(target);
            }

            // Cross-interface: target's owner differs from the
            // current emitting interface → emit fully qualified.
            if (emittingIface != null
                && target.Owner != null
                && target.Owner != emittingIface)
            {
                return EmitCrossInterfaceRef(target);
            }

            return NameConventions.ToPascalCase(target.Name);
        }

        /// <summary>True for bodies that get their own standalone
        /// C# class/struct/enum declaration — record, variant, enum,
        /// flags, resource. Everything else (primitives, lists, and
        /// aggregate combinators without a named declaration) inlines
        /// at the use site.</summary>
        private static bool IsDeclarableBody(CtValType body) =>
            body is CtRecordType || body is CtVariantType
            || body is CtEnumType || body is CtFlagsType
            || body is CtResourceType;

        /// <summary>
        /// Emit the fully-qualified <c>global::…</c> path for a
        /// named type. Interop files (which sit in a sibling static
        /// class, not inside the interface) use this for every
        /// named-type reference regardless of same-/cross-interface
        /// status — there's no nested scope to elide.
        /// </summary>
        internal static string EmitQualifiedPath(CtNamedType target) =>
            EmitCrossInterfaceRef(target);

        private static string EmitCrossInterfaceRef(CtNamedType target)
        {
            var ownerIface = target.Owner!;
            var worldNs = EmitAmbient.WorldNamespace ?? "UNSET_WORLD_NAMESPACE";

            // wit-bindgen treats any cross-interface reference as an
            // implicit import of the owning interface — even from an
            // export-side file. "wit.imports" matches the observed
            // 0.30.0 output.
            var sb = new System.Text.StringBuilder("global::");
            sb.Append(worldNs);
            sb.Append(".wit.imports.");
            sb.Append(ownerIface.Package!.Namespace);
            foreach (var seg in ownerIface.Package.Path)
            {
                sb.Append('.');
                sb.Append(seg);
            }
            var v = NameConventions.SanitizeVersion(ownerIface.Package.Version);
            if (v.Length > 0) sb.Append('.').Append(v);
            sb.Append(".I").Append(NameConventions.ToPascalCase(ownerIface.Name));
            sb.Append('.').Append(NameConventions.ToPascalCase(target.Name));
            return sb.ToString();
        }

        private static string EmitResultParam(CtResultType r)
        {
            // Result<Ok, Err> generic. Missing sides become the
            // world-shell `None` type (see CSharpEmitter world
            // shell emission).
            var sb = new StringBuilder("Result<");
            sb.Append(r.Ok != null ? EmitParam(r.Ok) : "None");
            sb.Append(", ");
            sb.Append(r.Err != null ? EmitParam(r.Err) : "None");
            sb.Append('>');
            return sb.ToString();
        }

        private static string EmitTuple(CtTupleType t)
        {
            // (T1, T2, ...) C# ValueTuple literal.
            var sb = new StringBuilder("(");
            for (int i = 0; i < t.Elements.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(EmitParam(t.Elements[i]));
            }
            sb.Append(')');
            return sb.ToString();
        }

        /// <summary>
        /// Primitive mapping, verified against wit-bindgen-csharp 0.30.0:
        /// <list type="bullet">
        /// <item><description><c>bool</c> → <c>bool</c></description></item>
        /// <item><description><c>s8</c>/<c>u8</c> → <c>sbyte</c>/<c>byte</c></description></item>
        /// <item><description><c>s16</c>/<c>u16</c> → <c>short</c>/<c>ushort</c></description></item>
        /// <item><description><c>s32</c>/<c>u32</c> → <c>int</c>/<c>uint</c></description></item>
        /// <item><description><c>s64</c>/<c>u64</c> → <c>long</c>/<c>ulong</c></description></item>
        /// <item><description><c>f32</c>/<c>f64</c> → <c>float</c>/<c>double</c></description></item>
        /// <item><description><c>char</c> → <c>uint</c> (wit treats char as unsigned 32-bit Unicode scalar)</description></item>
        /// <item><description><c>string</c> → <c>string</c></description></item>
        /// </list>
        /// </summary>
        public static string EmitPrim(CtPrim kind) => kind switch
        {
            CtPrim.Bool => "bool",
            CtPrim.S8 => "sbyte",
            CtPrim.U8 => "byte",
            CtPrim.S16 => "short",
            CtPrim.U16 => "ushort",
            CtPrim.S32 => "int",
            CtPrim.U32 => "uint",
            CtPrim.S64 => "long",
            CtPrim.U64 => "ulong",
            CtPrim.F32 => "float",
            CtPrim.F64 => "double",
            CtPrim.Char => "uint",
            CtPrim.String => "string",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}
