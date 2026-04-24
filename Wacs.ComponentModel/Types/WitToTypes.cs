// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Collections.Generic;
using Wacs.ComponentModel.WIT;

namespace Wacs.ComponentModel.Types
{
    /// <summary>
    /// Converts a parsed <see cref="WitDocument"/> (syntactic AST) into
    /// the semantic <see cref="Types"/> hierarchy. Produces one
    /// <see cref="CtPackage"/> per top-level package declared in the
    /// document.
    ///
    /// <para>Single-file scope. Cross-file name resolution (binding
    /// <c>use</c> references to types defined in sibling WIT files or
    /// external packages) is a separate pass owned by a future
    /// <c>WitResolver</c> that consumes multiple documents. Within the
    /// current document, forward references are resolved in a second
    /// pass that walks every converted type and binds
    /// <see cref="CtTypeRef.Target"/> to the locally-declared named
    /// type when possible; unresolved refs keep
    /// <see cref="CtTypeRef.Target"/> null.</para>
    ///
    /// <para>Two passes are required because WIT allows forward
    /// references inside an interface: a resource can be declared
    /// before a variant that references it, and a function signature
    /// can reference types declared later in the same interface.</para>
    /// </summary>
    public static class WitToTypes
    {
        /// <summary>
        /// Convert a document. Empty-package documents (WIT files that
        /// declare only a bare <c>package</c> header with no
        /// interfaces/worlds, and files using the anonymous implicit
        /// package — see <see cref="WitPackage.HasExplicitBody"/>) produce
        /// a single package with empty interface and world lists.
        /// </summary>
        public static IReadOnlyList<CtPackage> Convert(WitDocument doc)
        {
            var result = new List<CtPackage>(doc.Packages.Count);
            foreach (var wp in doc.Packages)
            {
                result.Add(ConvertPackage(wp));
            }
            return result;
        }

        private static CtPackage ConvertPackage(WitPackage wp)
        {
            var pkgName = ConvertPackageName(wp.Name);

            // Two-pass within each interface / world: pass 1 collects
            // named type defs into a symbol table, pass 2 walks the
            // signature graph and resolves CtTypeRef.Target against
            // the symbol table.
            var ifaces = new List<CtInterfaceType>(wp.Interfaces.Count);
            foreach (var wi in wp.Interfaces)
            {
                ifaces.Add(ConvertInterface(wi, pkgName));
            }

            var worlds = new List<CtWorldType>(wp.Worlds.Count);
            foreach (var ww in wp.Worlds)
            {
                worlds.Add(ConvertWorld(ww, pkgName));
            }

            return new CtPackage(pkgName, ifaces, worlds);
        }

        private static CtPackageName ConvertPackageName(WitPackageName w)
        {
            return new CtPackageName(w.Namespace, w.Path.ToArray(),
                                     w.Version?.ToString());
        }

        // ---- Interface ------------------------------------------------------

        private static CtInterfaceType ConvertInterface(WitInterface wi,
                                                        CtPackageName? pkg)
        {
            var symbols = new Dictionary<string, CtNamedType>();
            var types = new List<CtNamedType>();
            var aliases = new List<CtNamedType>();
            var functions = new List<CtInterfaceFunction>();
            var uses = new List<CtUse>();

            // Pass 1: collect named type defs. The type bodies may
            // themselves reference other types (forward refs), so we
            // stage them with placeholder bodies and fill in during
            // pass 2.
            foreach (var item in wi.Items)
            {
                switch (item)
                {
                    case WitTypeDef td:
                    {
                        // Placeholder body — real body filled in pass 2.
                        // This indirection supports cycles (e.g., a
                        // resource method referencing a variant that
                        // references the resource itself).
                        var named = new CtNamedType(td.Name,
                                                    new CtTypeRef("__placeholder__"));
                        symbols[td.Name] = named;
                        types.Add(named);
                        break;
                    }
                    case WitUse use:
                    {
                        var converted = ConvertUse(use);
                        uses.Add(converted);
                        // Imported names enter the interface scope as
                        // unresolved type refs (external binding happens
                        // during cross-file resolution). Tracked as
                        // aliases so the resolver can bind them and the
                        // emitter can follow them for cross-interface
                        // qualifying — but they don't become nested
                        // types in the emitted interface.
                        foreach (var n in converted.Names)
                        {
                            var named = new CtNamedType(n.LocalName,
                                                        new CtTypeRef(n.LocalName));
                            symbols[n.LocalName] = named;
                            aliases.Add(named);
                        }
                        break;
                    }
                }
            }

            // Pass 2: convert type bodies with the symbol table in
            // scope. The body is assigned directly into the
            // already-registered CtNamedType so references stay stable.
            // CtNamedType.Type has an internal setter for exactly this.
            foreach (var item in wi.Items)
            {
                if (item is WitTypeDef td)
                {
                    var body = ConvertType(td.Type, symbols, td.Name);
                    symbols[td.Name].Type = body;
                }
            }

            // Pass 3: convert functions. They may reference any type
            // now declared; the symbol table is complete.
            foreach (var item in wi.Items)
            {
                if (item is WitFunction fn)
                {
                    var ftype = ConvertFunctionSignature(fn.Params, fn.Result,
                                                         fn.NamedResults, symbols);
                    functions.Add(new CtInterfaceFunction(fn.Name, ftype));
                }
            }

            var iface = new CtInterfaceType(pkg, wi.Name, types, functions,
                                            uses, aliases);

            // Back-link types + aliases to their owning interface.
            // Aliases' Owner points at the using interface (not the
            // declaring one) — the declaring interface's real type
            // is reached via the alias body's CtTypeRef.Target after
            // resolution.
            foreach (var nt in types) nt.Owner = iface;
            foreach (var nt in aliases) nt.Owner = iface;

            return iface;
        }

        // ---- World ---------------------------------------------------------

        private static CtWorldType ConvertWorld(WitWorld ww, CtPackageName? pkg)
        {
            var symbols = new Dictionary<string, CtNamedType>();
            var types = new List<CtNamedType>();
            var uses = new List<CtUse>();
            var imports = new List<CtWorldImport>();
            var exports = new List<CtWorldExport>();
            var includes = new List<CtWorldInclude>();

            // Pass 1: world-level type defs + uses (register symbols).
            foreach (var item in ww.Items)
            {
                switch (item)
                {
                    case WitWorldTypeDef wtd:
                    {
                        var placeholder = new CtNamedType(wtd.TypeDef.Name,
                                                          new CtTypeRef("__placeholder__"));
                        symbols[wtd.TypeDef.Name] = placeholder;
                        types.Add(placeholder);
                        break;
                    }
                    case WitWorldUse wu:
                    {
                        var converted = ConvertUse(wu.Use);
                        uses.Add(converted);
                        foreach (var n in converted.Names)
                        {
                            symbols[n.LocalName] =
                                new CtNamedType(n.LocalName,
                                                new CtTypeRef(n.LocalName));
                        }
                        break;
                    }
                }
            }

            // Pass 2: fill in world-level type bodies.
            foreach (var item in ww.Items)
            {
                if (item is WitWorldTypeDef wtd)
                {
                    var body = ConvertType(wtd.TypeDef.Type, symbols, wtd.TypeDef.Name);
                    symbols[wtd.TypeDef.Name].Type = body;
                }
            }

            // Pass 3: imports / exports / includes.
            foreach (var item in ww.Items)
            {
                switch (item)
                {
                    case WitWorldImport wi:
                        imports.Add(new CtWorldImport(wi.Spec.Name,
                                                     ConvertExtern(wi.Spec, symbols)));
                        break;
                    case WitWorldExport we:
                        exports.Add(new CtWorldExport(we.Spec.Name,
                                                     ConvertExtern(we.Spec, symbols)));
                        break;
                    case WitWorldInclude inc:
                        includes.Add(new CtWorldInclude(
                            inc.Path.Package != null
                                ? ConvertPackageName(inc.Path.Package)
                                : null,
                            inc.Path.InterfaceName));
                        break;
                }
            }

            return new CtWorldType(pkg, ww.Name, types, uses, imports, exports, includes);
        }

        // ---- Extern spec ---------------------------------------------------

        private static CtExternType ConvertExtern(WitExternSpec spec,
                                                  IReadOnlyDictionary<string, CtNamedType> symbols)
        {
            switch (spec)
            {
                case WitExternFunc ef:
                    return new CtExternFunc(
                        ConvertFunctionSignature(ef.Function.Params,
                                                 ef.Function.Result,
                                                 ef.Function.NamedResults,
                                                 symbols));
                case WitExternInterfaceRef eref:
                    return new CtExternInterfaceRef(
                        eref.Path.Package != null
                            ? ConvertPackageName(eref.Path.Package)
                            : null,
                        eref.Path.InterfaceName);
                case WitExternInlineInterface eii:
                    return new CtExternInlineInterface(
                        ConvertInterface(eii.Interface, null));
                default:
                    throw new InvalidOperationException(
                        "Unknown extern spec: " + spec.GetType().Name);
            }
        }

        // ---- Function ------------------------------------------------------

        private static CtFunctionType ConvertFunctionSignature(
            List<WitParam> wParams,
            WitType? wResult,
            List<WitParam>? wNamed,
            IReadOnlyDictionary<string, CtNamedType> symbols)
        {
            var pars = new List<CtFuncParam>(wParams.Count);
            foreach (var p in wParams)
                pars.Add(new CtFuncParam(p.Name, ConvertType(p.Type, symbols, null)));

            CtValType? result = wResult != null
                ? ConvertType(wResult, symbols, null)
                : null;

            List<CtFuncParam>? named = null;
            if (wNamed != null)
            {
                named = new List<CtFuncParam>(wNamed.Count);
                foreach (var p in wNamed)
                    named.Add(new CtFuncParam(p.Name, ConvertType(p.Type, symbols, null)));
            }

            return new CtFunctionType(pars, result, named);
        }

        // ---- Use -----------------------------------------------------------

        private static CtUse ConvertUse(WitUse u)
        {
            var names = new List<CtUsedName>(u.Names.Count);
            foreach (var n in u.Names)
                names.Add(new CtUsedName(n.Name, n.Alias));
            return new CtUse(
                u.Path.Package != null ? ConvertPackageName(u.Path.Package) : null,
                u.Path.InterfaceName,
                names);
        }

        // ---- Type ----------------------------------------------------------

        /// <summary>
        /// Convert a <see cref="WitType"/> to a <see cref="CtValType"/>,
        /// resolving name references against <paramref name="symbols"/>
        /// (the interface-level symbol table).
        ///
        /// <para>When a <see cref="WitTypeRef"/>, <see cref="WitOwnType"/>,
        /// or <see cref="WitBorrowType"/> names a type that's in
        /// <paramref name="symbols"/>, we emit a <see cref="CtTypeRef"/>
        /// with <see cref="CtTypeRef.Target"/> bound. Otherwise we emit
        /// an unresolved CtTypeRef — cross-file resolution can fix it
        /// up later.</para>
        /// </summary>
        private static CtValType ConvertType(WitType wt,
                                             IReadOnlyDictionary<string, CtNamedType> symbols,
                                             string? _contextName)
        {
            switch (wt)
            {
                case WitPrimType p:  return ConvertPrim(p.Kind);
                case WitListType l:  return new CtListType(ConvertType(l.Element, symbols, null));
                case WitOptionType o: return new CtOptionType(ConvertType(o.Inner, symbols, null));
                case WitResultType r:
                    return new CtResultType(
                        r.Ok != null ? ConvertType(r.Ok, symbols, null) : null,
                        r.Err != null ? ConvertType(r.Err, symbols, null) : null);
                case WitTupleType t:
                {
                    var els = new List<CtValType>(t.Elements.Count);
                    foreach (var el in t.Elements)
                        els.Add(ConvertType(el, symbols, null));
                    return new CtTupleType(els);
                }
                case WitRecordType rec:
                {
                    var fields = new List<CtField>(rec.Fields.Count);
                    foreach (var f in rec.Fields)
                        fields.Add(new CtField(f.Name, ConvertType(f.Type, symbols, null)));
                    return new CtRecordType(_contextName ?? "", fields);
                }
                case WitVariantType v:
                {
                    var cases = new List<CtVariantCase>(v.Cases.Count);
                    foreach (var c in v.Cases)
                        cases.Add(new CtVariantCase(
                            c.Name,
                            c.Payload != null ? ConvertType(c.Payload, symbols, null) : null));
                    return new CtVariantType(_contextName ?? "", cases);
                }
                case WitEnumType e:
                    return new CtEnumType(_contextName ?? "", e.Cases.ToArray());
                case WitFlagsType f:
                    return new CtFlagsType(_contextName ?? "", f.Flags.ToArray());
                case WitResourceType res:
                {
                    var methods = new List<CtResourceMethod>(res.Methods.Count);
                    foreach (var m in res.Methods)
                    {
                        var mft = ConvertFunctionSignature(m.Params, m.Result,
                                                            m.NamedResults, symbols);
                        methods.Add(new CtResourceMethod(
                            string.IsNullOrEmpty(m.Name) ? null : m.Name,
                            ConvertResourceMethodKind(m.Kind),
                            mft));
                    }
                    return new CtResourceType(_contextName ?? "", methods);
                }
                case WitOwnType own:
                    return new CtOwnType(ResolveRef(own.ResourceName, symbols));
                case WitBorrowType bor:
                    return new CtBorrowType(ResolveRef(bor.ResourceName, symbols));
                case WitTypeRef tr:
                    return ResolveRef(tr.Name, symbols);
                default:
                    throw new InvalidOperationException(
                        "Unknown WIT type: " + wt.GetType().Name);
            }
        }

        private static CtValType ResolveRef(string name,
                                            IReadOnlyDictionary<string, CtNamedType> symbols)
        {
            var r = new CtTypeRef(name);
            if (symbols.TryGetValue(name, out var target))
                r.Target = target;
            return r;
        }

        private static CtPrimType ConvertPrim(WitPrim k) => k switch
        {
            WitPrim.Bool => CtPrimType.Bool,
            WitPrim.S8 => CtPrimType.S8,
            WitPrim.S16 => CtPrimType.S16,
            WitPrim.S32 => CtPrimType.S32,
            WitPrim.S64 => CtPrimType.S64,
            WitPrim.U8 => CtPrimType.U8,
            WitPrim.U16 => CtPrimType.U16,
            WitPrim.U32 => CtPrimType.U32,
            WitPrim.U64 => CtPrimType.U64,
            WitPrim.F32 => CtPrimType.F32,
            WitPrim.F64 => CtPrimType.F64,
            WitPrim.Char => CtPrimType.Char,
            WitPrim.String => CtPrimType.String,
            _ => throw new InvalidOperationException("Unknown prim: " + k),
        };

        private static CtResourceMethodKind ConvertResourceMethodKind(
            WitResourceMethodKind k) => k switch
        {
            WitResourceMethodKind.Constructor => CtResourceMethodKind.Constructor,
            WitResourceMethodKind.Static => CtResourceMethodKind.Static,
            WitResourceMethodKind.Instance => CtResourceMethodKind.Instance,
            _ => throw new InvalidOperationException("Unknown resource method kind"),
        };
    }

}
