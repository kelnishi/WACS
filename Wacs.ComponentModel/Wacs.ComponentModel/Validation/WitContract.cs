// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Wacs.ComponentModel.Runtime;

// WitContract.FromAssembly walks assembly resources + reflects
// over [WitSource]-tagged types to rebuild the WIT shape the
// bindings were generated against. The resource probe path
// (Assembly.GetManifestResourceStream / .GetManifestResourceNames)
// is trim-safe in principle but the reflective WitSource walk is
// not. Suppressed file-wide here; the public FromAssembly entry
// is annotated [RequiresUnreferencedCode].
#pragma warning disable IL2026, IL2070
using Wacs.ComponentModel.Types;
using Wacs.ComponentModel.WIT;

namespace Wacs.ComponentModel.Validation
{
    /// <summary>
    /// A flattened, validation-ready view of a WIT spec — one
    /// <see cref="ImportEntry"/> per (module, entity) pair the
    /// guest is expected to import. The
    /// <see cref="Linker"/> matches its binding manifest
    /// against this list.
    ///
    /// <para>Construct via:
    /// <list type="bullet">
    /// <item><see cref="FromText"/> for ad-hoc WIT strings (one
    /// document; no <c>use</c> resolution across files)</item>
    /// <item><see cref="FromDirectory"/> for a vendored WIT
    /// tree like <c>Wacs.WASI.Preview2/wit/</c> — recurses,
    /// resolves cross-package use chains</item>
    /// <item><see cref="FromBindingTypes"/> to reflect over
    /// generated <c>I*</c> interface types decorated with
    /// <see cref="WitSourceAttribute"/> — re-extracts the WIT
    /// contract directly from the bindings the host exposes,
    /// no separate spec needed.</item>
    /// </list></para>
    /// </summary>
    public sealed class WitContract
    {
        public IReadOnlyList<ImportEntry> Imports { get; }

        public WitContract(IReadOnlyList<ImportEntry> imports)
        {
            Imports = imports ?? Array.Empty<ImportEntry>();
        }

        // -------- builders --------------------------------------

        public static WitContract FromText(string witText)
        {
            if (witText == null) throw new ArgumentNullException(
                nameof(witText));
            var doc = WitParser.Parse(witText);
            var packages = WitToTypes.Convert(doc);
            WitResolver.Resolve(packages);
            return BuildFromPackages(packages);
        }

#if !WACS_SOURCEGEN
        public static WitContract FromDirectory(string directory)
        {
            if (directory == null) throw new ArgumentNullException(
                nameof(directory));
            var packages = WitLoader.LoadDirectoryTree(directory);
            WitResolver.Resolve(packages);
            return BuildFromPackages(packages);
        }
#endif

        /// <summary>
        /// Build a contract from WIT files embedded as
        /// <c>EmbeddedResource</c> in <paramref name="assembly"/>.
        /// Filters resources whose name starts with
        /// <paramref name="resourcePrefix"/> (default
        /// <c>"wit/"</c> — matches the convention WACS.WASI.Preview2
        /// ships with).
        ///
        /// <para>Resources are grouped by directory (the path
        /// under the prefix) so headerless WIT files attribute to
        /// the package declared by their sibling, mirroring how
        /// <see cref="WitLoader.LoadDirectoryTree"/> resolves a
        /// disk tree.</para>
        ///
        /// <para>Lets a host validate against the WIT contract a
        /// shipped binding package was built from, without needing
        /// the original <c>.wit</c> files on disk:
        /// <code>
        /// var contract = WitContract.FromAssembly(
        ///     typeof(CliBindings).Assembly);
        /// linker.Validate(contract);
        /// </code></para>
        /// </summary>
        [RequiresUnreferencedCode("Walks the assembly's manifest " +
            "resources and reflects over [WitSource]-tagged types to " +
            "rebuild the WIT contract. AOT consumers should ship the " +
            "WIT bytes alongside their build-time-generated typed " +
            "harness rather than reflecting from the bindings.")]
        public static WitContract FromAssembly(Assembly assembly,
            string resourcePrefix = "wit/")
        {
            if (assembly == null) throw new ArgumentNullException(
                nameof(assembly));
            // Two paths:
            // 1. Source-gen / componentize-dotnet style: WIT files
            //    are baked as embedded resources under
            //    "wit/<rel>.wit" — parse them and build the
            //    contract from the WIT text.
            // 2. WACS-transpiled .dll style: no embedded WIT
            //    resources, but [WitSource]-tagged interfaces are
            //    present courtesy of ExportInterfaceEmit. Walk
            //    those via FromBindingTypes.
            //
            // Try the embedded-WIT path first; if it surfaces
            // nothing, fall back to FromBindingTypes — this is
            // the Phase B chain mode entry point that lets a
            // transpiled .dll serve as a host-package.
            var resources = ReadEmbeddedWit(assembly, resourcePrefix);
            if (resources.Count > 0)
                return BuildFromPackages(
                    LoadAssemblyPackages(assembly, resourcePrefix));
            return FromBindingTypes(assembly.GetExportedTypes());
        }

        /// <summary>
        /// Load all <see cref="CtPackage"/>s from
        /// <paramref name="assembly"/>'s embedded WIT resources,
        /// resolved (cross-package <c>use</c> chains filled in).
        /// Useful when the caller wants to walk the package
        /// graph directly — e.g. to pick a specific world via
        /// <see cref="FromWorld"/>.
        /// </summary>
        public static IReadOnlyList<CtPackage> LoadAssemblyPackages(
            Assembly assembly, string resourcePrefix = "wit/")
        {
            var sources = ReadEmbeddedWit(assembly, resourcePrefix);
            if (sources.Count == 0)
                throw new InvalidOperationException(
                    "Assembly " + assembly.GetName().Name
                    + " has no embedded WIT resources under prefix '"
                    + resourcePrefix + "'.");

            var byDir = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var (key, text) in sources)
            {
                var rel = key.Substring(resourcePrefix.Length);
                int slash = rel.LastIndexOf('/');
                var dir = slash < 0 ? "" : rel.Substring(0, slash);
                if (!byDir.TryGetValue(dir, out var list))
                {
                    list = new List<string>();
                    byDir[dir] = list;
                }
                list.Add(text);
            }

            var allPackages = new List<CtPackage>();
            foreach (var group in byDir.Values)
            {
                var docs = new List<WitDocument>(group.Count);
                foreach (var text in group)
                    docs.Add(WitParser.Parse(text));
                allPackages.AddRange(WitLoader.MergeDocuments(docs));
            }
            var packages = MergeByQualifiedName(allPackages);
            WitResolver.Resolve(packages);
            return packages;
        }

        /// <summary>Enumerate the embedded WIT resources from
        /// <paramref name="assembly"/> as (logical-name, text)
        /// pairs. Useful for inspection / re-emission;
        /// <see cref="FromAssembly"/> is the path most
        /// validation callers want.</summary>
        public static IReadOnlyList<(string Name, string Text)>
            ReadEmbeddedWit(Assembly assembly,
                string resourcePrefix = "wit/")
        {
            if (assembly == null) throw new ArgumentNullException(
                nameof(assembly));
            var result = new List<(string, string)>();
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(resourcePrefix,
                    StringComparison.Ordinal)) continue;
                if (!name.EndsWith(".wit",
                    StringComparison.OrdinalIgnoreCase)) continue;
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null) continue;
                using var reader = new System.IO.StreamReader(
                    stream, System.Text.Encoding.UTF8);
                result.Add((name, reader.ReadToEnd()));
            }
            return result;
        }

        // Pure-string MergeByQualifiedName — duplicates the
        // logic in WitLoader so the source-gen side (which
        // can't call WitLoader's IO methods) and this assembly-
        // resource path both stay in lockstep.
        private static List<CtPackage> MergeByQualifiedName(
            IReadOnlyList<CtPackage> packages)
        {
            var byKey = new Dictionary<string, (CtPackageName Name,
                List<CtInterfaceType> Ifaces, List<CtWorldType> Worlds)>();
            foreach (var pkg in packages)
            {
                var key = pkg.Name.ToString();
                if (!byKey.TryGetValue(key, out var acc))
                {
                    acc = (pkg.Name,
                        new List<CtInterfaceType>(),
                        new List<CtWorldType>());
                    byKey[key] = acc;
                }
                acc.Ifaces.AddRange(pkg.Interfaces);
                acc.Worlds.AddRange(pkg.Worlds);
                byKey[key] = acc;
            }
            var result = new List<CtPackage>();
            foreach (var v in byKey.Values)
                result.Add(new CtPackage(v.Name, v.Ifaces, v.Worlds));
            return result;
        }

        public static WitContract FromPackages(
            IReadOnlyList<CtPackage> packages)
        {
            if (packages == null) throw new ArgumentNullException(
                nameof(packages));
            return BuildFromPackages(packages);
        }

        /// <summary>
        /// Build a contract for a specific WIT <em>world</em>'s
        /// imports. Walks the world's
        /// <see cref="CtWorldType.Imports"/>, recursively
        /// expanding <see cref="CtWorldType.Includes"/>. Exports
        /// are skipped — they're what the guest provides, not
        /// what the host has to bind.
        ///
        /// <para>This is the right entry point for validating
        /// against a specific component-model world rather than
        /// "every interface in every package the WIT tree
        /// defines." For WASI 0.2.3 hosts targeting CLI guests,
        /// validate against <c>wasi:cli/imports</c>;
        /// HTTP-proxy hosts validate against
        /// <c>wasi:http/proxy</c>.</para>
        /// </summary>
        /// <param name="packages">All packages whose
        /// interfaces the world's imports may reference. Must
        /// have been through <see cref="WitResolver.Resolve"/>.</param>
        /// <param name="worldQualifiedName">The world's
        /// canonical name (e.g.
        /// <c>"wasi:cli/imports@0.2.3"</c>).</param>
        public static WitContract FromWorld(
            IReadOnlyList<CtPackage> packages,
            string worldQualifiedName)
        {
            if (packages == null) throw new ArgumentNullException(
                nameof(packages));
            if (worldQualifiedName == null)
                throw new ArgumentNullException(
                    nameof(worldQualifiedName));

            var world = FindWorld(packages, worldQualifiedName);
            if (world == null)
                throw new InvalidOperationException(
                    "World '" + worldQualifiedName
                    + "' not found in supplied packages.");

            var imports = new List<ImportEntry>();
            var visited = new HashSet<string>();
            CollectWorldImports(world, packages, imports, visited);
            return new WitContract(imports);
        }

        private static CtWorldType? FindWorld(
            IReadOnlyList<CtPackage> packages,
            string qualifiedName)
        {
            foreach (var pkg in packages)
            {
                foreach (var w in pkg.Worlds)
                {
                    // Accept both QualifiedName forms:
                    //   <ns>:<pkg>@<ver>/<world>   (CtWorldType.QualifiedName)
                    //   <ns>:<pkg>/<world>@<ver>   (wasm-wire canonical)
                    if (w.QualifiedName == qualifiedName)
                        return w;
                    if (WorldWireForm(w) == qualifiedName)
                        return w;
                }
            }
            return null;
        }

        private static string WorldWireForm(CtWorldType w)
        {
            var pkg = w.Package;
            if (pkg == null) return w.Name;
            var path = string.Join(":", pkg.Path);
            var ver = string.IsNullOrEmpty(pkg.Version)
                ? "" : "@" + pkg.Version;
            return pkg.Namespace + ":" + path + "/" + w.Name + ver;
        }

        private static void CollectWorldImports(CtWorldType world,
            IReadOnlyList<CtPackage> packages,
            List<ImportEntry> imports,
            HashSet<string> visited)
        {
            if (!visited.Add(world.QualifiedName))
                return;   // include cycle — bail.

            // Recursively expand `include other-world;` —
            // included worlds' imports/exports splice in.
            foreach (var inc in world.Includes)
            {
                var includedName = inc.Package == null
                    ? (world.Package?.ToString() ?? "") + "/" + inc.WorldName
                    : inc.Package.ToString() + "/" + inc.WorldName;
                var includedWorld = FindWorld(packages, includedName);
                if (includedWorld != null)
                    CollectWorldImports(includedWorld, packages,
                        imports, visited);
            }

            foreach (var imp in world.Imports)
                CollectExternImport(imp.Name, imp.Spec,
                    world.Package, imports);
        }

        private static void CollectExternImport(string portName,
            CtExternType spec, CtPackageName? worldPkg,
            List<ImportEntry> imports)
        {
            switch (spec)
            {
                case CtExternInterfaceRef iref:
                    var iface = iref.Target;
                    if (iface == null) return;   // unresolved
                    var module = ModuleNameFor(iface);
                    foreach (var fn in iface.Functions)
                        imports.Add(BuildEntry(module, fn.Name,
                            fn.Type));
                    foreach (var t in iface.Types)
                    {
                        if (t.Type is CtResourceType res)
                        {
                            foreach (var m in res.Methods)
                            {
                                var entity = ResourceMethodEntity(
                                    res.Name, m);
                                imports.Add(BuildEntry(module,
                                    entity, m.Function, m.Kind));
                            }
                        }
                    }
                    break;

                case CtExternFunc inlineFn:
                    // Inline function imported at world level —
                    // the module is the world's package, the
                    // entity is the import's port name.
                    var pkgPart = worldPkg == null ? ""
                        : worldPkg.ToString();
                    imports.Add(BuildEntry(pkgPart,
                        StripKeywordShadow(portName)!,
                        inlineFn.Function));
                    break;

                case CtExternInlineInterface inlineIface:
                    var inlineModule = ModuleNameFor(
                        inlineIface.Interface);
                    foreach (var fn in inlineIface.Interface.Functions)
                        imports.Add(BuildEntry(inlineModule,
                            StripKeywordShadow(fn.Name)!,
                            fn.Type));
                    break;
            }
        }

        // Match the canonical wasm import-string form used by
        // BuildFromPackages: <ns>:<pkg-path>/<iface>@<ver>.
        private static string ModuleNameFor(CtInterfaceType iface)
        {
            var pkg = iface.Package;
            if (pkg == null) return iface.Name;
            var path = string.Join(":", pkg.Path);
            var ver = string.IsNullOrEmpty(pkg.Version)
                ? "" : "@" + pkg.Version;
            return pkg.Namespace + ":" + path + "/"
                + iface.Name + ver;
        }

        /// <summary>
        /// Build a contract by reflecting over generated host
        /// interface types decorated with
        /// <see cref="WitSourceAttribute"/>. Each interface's
        /// methods produce one
        /// <see cref="ImportEntry"/>; resource interfaces and
        /// free-function interfaces are both walked.
        ///
        /// <para>Use this when the bindings are themselves the
        /// authoritative spec — the generated interfaces carry
        /// the WIT-text fragments the source generator
        /// embedded.</para>
        /// </summary>
        public static WitContract FromBindingTypes(params Type[] types)
        {
            if (types == null) throw new ArgumentNullException(
                nameof(types));
            var imports = new List<ImportEntry>();
            foreach (var t in types)
                CollectFromType(t, imports);
            return new WitContract(imports);
        }

        // -------- core builder ----------------------------------

        private static WitContract BuildFromPackages(
            IReadOnlyList<CtPackage> packages)
        {
            var imports = new List<ImportEntry>();
            // Pre-compute the set of interfaces that appear as
            // imports in at least one world (transitively through
            // `include other-world`). Anything that's only ever a
            // guest export (e.g. `wasi:cli/run`,
            // `wasi:http/incoming-handler`) is skipped — hosts
            // don't bind exports. If the package tree declares no
            // worlds, fall back to including every interface.
            var importableIfaces = ComputeImportableInterfaces(
                packages);
            bool scopeToImports = importableIfaces != null;
            foreach (var pkg in packages)
            {
                foreach (var iface in pkg.Interfaces)
                {
                    if (scopeToImports
                        && !importableIfaces!.Contains(iface))
                        continue;
                    // WASM imports use the WASI-canonical form
                    // <ns>:<pkg>/<iface>@<ver> (version at the
                    // end), not the WIT QualifiedName's
                    // <ns>:<pkg>@<ver>/<iface>. Match the wire
                    // form so contract module names align with
                    // bind-side keys.
                    var pkgName = iface.Package!;
                    var path = string.Join(":", pkgName.Path);
                    var module = pkgName.Namespace + ":"
                        + path + "/" + iface.Name
                        + (string.IsNullOrEmpty(pkgName.Version)
                            ? ""
                            : "@" + pkgName.Version);
                    foreach (var fn in iface.Functions)
                    {
                        imports.Add(BuildEntry(module,
                            StripKeywordShadow(fn.Name)!,
                            fn.Type));
                    }
                    foreach (var t in iface.Types)
                    {
                        if (t.Type is CtResourceType res)
                        {
                            // [resource-drop] is always bound;
                            // not a contract entry — bookkeeping
                            // handled by ResourceTable.Drop.
                            // Resource methods otherwise are
                            // [method]X.foo / [static]X.foo /
                            // [constructor]X.
                            foreach (var m in res.Methods)
                            {
                                var entity = ResourceMethodEntity(
                                    res.Name, m);
                                imports.Add(BuildEntry(module,
                                    entity, m.Function, m.Kind));
                            }
                        }
                    }
                }
            }
            return new WitContract(imports);
        }

        // Determine which interfaces should appear in a
        // FromAssembly / FromDirectory contract. Three buckets:
        //   - imported by some world → include (host binds it)
        //   - referenced only as exports across all worlds →
        //     exclude (guest implements it; host doesn't bind)
        //   - not referenced by any world (orphan) → include
        //     conservatively (no signal to exclude)
        //
        // Returns null if the package tree declares no worlds
        // at all — caller falls back to "include everything"
        // (preserves the pre-scoping FromText one-shot behavior).
        private static HashSet<CtInterfaceType>?
            ComputeImportableInterfaces(
                IReadOnlyList<CtPackage> packages)
        {
            bool sawWorld = false;
            foreach (var pkg in packages)
            {
                if (pkg.Worlds.Count > 0)
                {
                    sawWorld = true;
                    break;
                }
            }
            if (!sawWorld) return null;

            var imported = new HashSet<CtInterfaceType>();
            var referenced = new HashSet<CtInterfaceType>();
            var visited = new HashSet<string>();
            foreach (var pkg in packages)
                foreach (var w in pkg.Worlds)
                    CollectWorldRefs(w, packages,
                        imported, referenced, visited);

            var result = new HashSet<CtInterfaceType>(imported);
            foreach (var pkg in packages)
                foreach (var iface in pkg.Interfaces)
                    if (!referenced.Contains(iface))
                        result.Add(iface);  // orphan
            return result;
        }

        private static void CollectWorldRefs(
            CtWorldType world,
            IReadOnlyList<CtPackage> packages,
            HashSet<CtInterfaceType> imported,
            HashSet<CtInterfaceType> referenced,
            HashSet<string> visited)
        {
            if (!visited.Add(world.QualifiedName))
                return;

            foreach (var inc in world.Includes)
            {
                var includedName = inc.Package == null
                    ? (world.Package?.ToString() ?? "") + "/" + inc.WorldName
                    : inc.Package.ToString() + "/" + inc.WorldName;
                var included = FindWorld(packages, includedName);
                if (included != null)
                    CollectWorldRefs(included, packages,
                        imported, referenced, visited);
            }

            foreach (var imp in world.Imports)
                AddIfaceRef(imp.Spec, imported, referenced);
            foreach (var exp in world.Exports)
                AddIfaceRef(exp.Spec, null, referenced);
        }

        private static void AddIfaceRef(CtExternType spec,
            HashSet<CtInterfaceType>? imported,
            HashSet<CtInterfaceType> referenced)
        {
            switch (spec)
            {
                case CtExternInterfaceRef iref when iref.Target != null:
                    imported?.Add(iref.Target);
                    referenced.Add(iref.Target);
                    break;
                case CtExternInlineInterface inline:
                    imported?.Add(inline.Interface);
                    referenced.Add(inline.Interface);
                    break;
            }
        }

        private static string ResourceMethodEntity(string resName,
            CtResourceMethod m)
        {
            // The WIT source-level `%`-keyword-shadow prefix
            // (e.g. `%stream` to use the keyword "stream" as
            // an identifier) is not part of the canonical
            // wasm-import-string entity name; the bindings
            // register the bare name without it.
            var name = StripKeywordShadow(m.Name);
            var rn = StripKeywordShadow(resName);
            return m.Kind switch
            {
                CtResourceMethodKind.Constructor =>
                    "[constructor]" + rn,
                CtResourceMethodKind.Static =>
                    "[static]" + rn + "." + name,
                _ => "[method]" + rn + "." + name,
            };
        }

        private static string? StripKeywordShadow(string? s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s![0] == '%' ? s.Substring(1) : s;
        }

        // Canon-ABI flat-form lowering for host-imported funcs.
        // Spec: MAX_FLAT_PARAMS = 16, MAX_FLAT_RESULTS = 1.
        // - If lowered param-flat > 16 → all params packed into
        //   a single i32 pointer (indirect params).
        // - If lowered return-flat > 1 → result returned through
        //   a trailing i32 retArea param, function returns void.
        // <paramref name="resourceMethodKind"/> is null for free
        // functions; instance methods take an implicit self
        // handle, constructors return one.
        private const int MaxFlatParams = 16;
        private const int MaxFlatResults = 1;

        private static ImportEntry BuildEntry(string module,
            string name, CtFunctionType fn,
            CtResourceMethodKind? resourceMethodKind = null)
        {
            int paramSlots = 0;
            if (resourceMethodKind == CtResourceMethodKind.Instance)
                paramSlots += 1;
            foreach (var p in fn.Params)
                paramSlots += FlatSlotCount(p.Type);

            int returnSlots;
            if (resourceMethodKind == CtResourceMethodKind.Constructor)
                returnSlots = 1;
            else if (fn.HasNoResult)
                returnSlots = 0;
            else
                returnSlots = FlatSlotCount(fn.Result!);

            // Apply MAX_FLAT_PARAMS — note this happens BEFORE
            // the retArea hoist, so a func with 16 flat param
            // slots + retArea ends up as 1 ptr (indirect) + 1
            // retArea = 2 params on the wire.
            if (paramSlots > MaxFlatParams)
                paramSlots = 1;

            if (returnSlots > MaxFlatResults)
            {
                paramSlots += 1;
                returnSlots = 0;
            }
            return new ImportEntry(module, name,
                paramSlots, returnSlots);
        }

        private static int FlatSlotCount(CtValType t)
        {
            // Coarse: every WIT type lowers to ≥1 flat slot.
            // Strings, lists, options, results, tuples have
            // multi-slot expansions per the canon ABI:
            //   string  → 2 (ptr, len)
            //   list<T> → 2 (ptr, len)
            //   option<T> with align ≤ 4 → 1 + flat(T)
            //   tuple<a,b,...> → sum(flat(elem))
            //   record → sum of flat fields
            //   variant → 1 (disc) + max-payload-flat
            //   own/borrow/resource ref → 1 (handle)
            //   primitive → 1
            // The contract's role is import-presence + arity
            // sanity, not exact ABI match — exact match needs
            // the same canon-lowering machinery the runtime
            // uses; out of scope for v0 validation.
            return t switch
            {
                CtPrimType p when p.Kind == CtPrim.String => 2,
                CtListType => 2,
                CtOptionType o => 1 + FlatSlotCount(o.Inner),
                CtResultType r =>
                    1 + System.Math.Max(
                        r.Ok != null ? FlatSlotCount(r.Ok) : 0,
                        r.Err != null ? FlatSlotCount(r.Err) : 0),
                CtTupleType tp => tp.Elements.Sum(FlatSlotCount),
                CtRecordType rec => rec.Fields.Sum(
                    f => FlatSlotCount(f.Type)),
                CtVariantType v =>
                    1 + (v.Cases.Count == 0 ? 0
                        : v.Cases.Max(c =>
                            c.Payload == null ? 0
                                : FlatSlotCount(c.Payload))),
                CtTypeRef r when r.Target?.Type != null =>
                    FlatSlotCount(r.Target.Type),
                _ => 1,
            };
        }

        // -------- attribute reflection (for FromBindingTypes) ----

        private static void CollectFromType(Type type,
            List<ImportEntry> imports)
        {
            if (type == null) return;
            // The interface itself carries the (Package,
            // Interface) header. Its methods carry per-method
            // [WitSource] with WIT text + Item key.
            var ifaceAttr = type.GetCustomAttribute<WitSourceAttribute>();
            if (ifaceAttr == null) return;
            string module = ifaceAttr.Package != null
                && ifaceAttr.Interface != null
                ? ifaceAttr.Package + "/" + ifaceAttr.Interface
                : (ifaceAttr.Interface ?? "");

            foreach (var m in type.GetMethods(BindingFlags.Public
                | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var ws = m.GetCustomAttribute<WitSourceAttribute>();
                if (ws == null) continue;
                var item = ws.Item ?? KebabCase(m.Name);

                // Use C# method arity as a rough proxy for
                // canon-lowered flat-slot count. Strings, lists
                // etc. get expanded by the binder; reflect that
                // by counting method's parameter wire types.
                int paramSlots = m.GetParameters()
                    .Sum(p => FlatSlotCountForClrType(p.ParameterType));
                int returnSlots = m.ReturnType == typeof(void)
                    ? 0
                    : FlatSlotCountForClrType(m.ReturnType);

                if (paramSlots > MaxFlatParams)
                    paramSlots = 1;
                if (returnSlots > MaxFlatResults)
                {
                    paramSlots += 1;
                    returnSlots = 0;
                }

                imports.Add(new ImportEntry(module, item,
                    paramSlots, returnSlots));
            }
        }

        private static int FlatSlotCountForClrType(Type t)
        {
            if (t == typeof(string)) return 2;
            if (t.IsArray) return 2;
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(Option<>))
                    return 1 + FlatSlotCountForClrType(
                        t.GetGenericArguments()[0]);
                if (def == typeof(Result<,>))
                    return 1 + System.Math.Max(
                        FlatSlotCountForClrType(t.GetGenericArguments()[0]),
                        FlatSlotCountForClrType(t.GetGenericArguments()[1]));
                if (def == typeof(ValueTuple<,>)
                    || def == typeof(ValueTuple<,,>)
                    || def == typeof(ValueTuple<,,,>)
                    || def == typeof(ValueTuple<,,,,>))
                    return t.GetGenericArguments()
                        .Sum(FlatSlotCountForClrType);
            }
            return 1;
        }

        private static string KebabCase(string pascal)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (char.IsUpper(c) && i > 0)
                    sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }

    /// <summary>One WIT-declared import the guest expects to be
    /// bound. <see cref="Module"/> is the WIT-qualified
    /// interface name (e.g. <c>"wasi:io/streams@0.2.3"</c>);
    /// <see cref="Entity"/> is the entity name within that
    /// module (e.g. <c>"[method]input-stream.read"</c> or
    /// <c>"poll"</c>). Param / return counts are the
    /// canon-lowered flat-slot estimates the validator
    /// compares against the runtime's recorded
    /// <see cref="Wacs.Core.Types.FunctionType"/>.</summary>
    public sealed class ImportEntry
    {
        public string Module { get; }
        public string Entity { get; }
        public int ExpectedParamCount { get; }
        public int ExpectedReturnCount { get; }

        public ImportEntry(string module, string entity,
            int paramCount, int returnCount)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            Entity = entity ?? throw new ArgumentNullException(nameof(entity));
            ExpectedParamCount = paramCount;
            ExpectedReturnCount = returnCount;
        }

        public override string ToString() =>
            Module + "/" + Entity + " (params=" + ExpectedParamCount
            + ", returns=" + ExpectedReturnCount + ")";
    }
}
