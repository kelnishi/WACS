// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using ComponentSectionId = Wacs.ComponentModel.Runtime.Parser.ComponentSectionId;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Indexes the typed <c>[WitSource]</c>-tagged interfaces of one
    /// or more host-package assemblies into a <c>(module, entity) →
    /// MethodInfo</c> lookup that the component transpiler queries
    /// at each guest <c>call $import</c> site. A hit lets
    /// <see cref="DirectLinkedImportEmit"/> lower the call to inline
    /// IL (typed <c>callvirt</c>); a miss falls back to the existing
    /// runtime delegate-table dispatch.
    ///
    /// <para>Module / entity strings are the canonical wasm import-
    /// string form, matching what
    /// <c>WitContract.BuildFromPackages</c> emits and what the
    /// component-binary parser surfaces:
    /// <list type="bullet">
    /// <item>Module: <c>"&lt;ns&gt;:&lt;pkg&gt;/&lt;iface&gt;@&lt;ver&gt;"</c>
    /// (e.g. <c>"wasi:cli/exit@0.2.3"</c>) — version at the end,
    /// matching the wasm-encoded import string.</item>
    /// <item>Entity: <c>"&lt;name&gt;"</c> for free functions,
    /// <c>"[method]&lt;res&gt;.&lt;name&gt;"</c> /
    /// <c>"[static]&lt;res&gt;.&lt;name&gt;"</c> /
    /// <c>"[constructor]&lt;res&gt;"</c> for resource methods.</item>
    /// </list></para>
    ///
    /// <para>The <see cref="WitSourceAttribute"/> stores the
    /// package as <c>"wasi:cli@0.2.3"</c> (version mid-string —
    /// matches WIT-text qualified-name form). The resolver
    /// rewrites this to the wire form so the lookup matches the
    /// guest's import strings.</para>
    /// </summary>
    public sealed class HostPackageResolver
    {
        public IReadOnlyList<Assembly> HostPackages { get; }

        public IReadOnlyDictionary<(string Module, string Entity), Binding>
            Bindings => _bindings;

        /// <summary>The set of typed interface types the resolver
        /// found across all host packages, in stable order. The
        /// generated component class needs one ctor reference per
        /// distinct interface (either via a packed bundle or as
        /// individual ctor params).</summary>
        public IReadOnlyList<Type> InterfaceTypes { get; }

        /// <summary>Subset of <see cref="InterfaceTypes"/> that
        /// represents WIT resource interfaces (i.e. those whose
        /// <see cref="Wacs.ComponentModel.Runtime.WitSourceAttribute.Item"/>
        /// names a resource, not <c>null</c> or a free-function entity).
        /// Direct-linked emit checks this set when a typed interface
        /// appears as a CLR param to recognize own&lt;R&gt;/borrow&lt;R&gt;
        /// shapes — the wasm wire is a single i32 handle that the
        /// IL resolves via <see cref="ThinContext.Resources"/>.</summary>
        public IReadOnlyCollection<Type> ResourceInterfaceTypes { get; }

        /// <summary>True when <paramref name="t"/> is a typed resource
        /// interface from the loaded host packages.</summary>
        public bool IsResourceInterface(Type t)
            => _resourceInterfaceTypes.Contains(t);

        /// <summary>The aggregate "bundle" type the host package
        /// ships, if one exists — a class whose public read-only
        /// properties expose each typed interface. v0 recognizes
        /// <c>Wacs.WASI.Preview2.DependencyInjection.WasiPreview2Bundle</c>
        /// by qualified name; future host packages can opt in by
        /// shipping a class with the same shape (one property per
        /// <c>[WitSource]</c>-tagged interface, all named with the
        /// interface's name minus the leading <c>I</c>).</summary>
        public Type? PreferredBundleType { get; }

        /// <summary>The host package's resource-resolver type — a
        /// class with a public method
        /// <c>object GetResource(System.Type resourceInterface, int handle)</c>
        /// that maps a wasm resource handle to the typed instance
        /// the bound interface method is invoked on. Direct-linked
        /// resource-method import IL emits a callvirt against this
        /// method. Null when no resource bindings are present or
        /// when the host package supplies no resolver convention.</summary>
        public Type? PreferredResourcesType { get; }

        private readonly Dictionary<(string Module, string Entity), Binding>
            _bindings;
        // Parallel index keyed by (module-without-@version, entity).
        // Lets WASI guests built against a newer point release of the
        // spec (e.g. wasi:io/error@0.2.6) resolve against a host
        // package shipping an older one (wasi:io/error@0.2.3) — the
        // wasm Component Model treats minor revisions of WASI as
        // ABI-stable, so wasmtime / jco / wasmer all do the same
        // version-tolerant match. Exact lookup wins; stripped is a
        // fallback so a component declaring a specific version
        // still binds to the matching host first when both are
        // registered.
        private readonly Dictionary<(string Module, string Entity), Binding>
            _bindingsByStrippedModule;
        private readonly HashSet<Type> _resourceInterfaceTypes;

        // Concrete impl class for each resource interface — used by
        // direct-link emit's SourceGen-shape constructor path
        // (interface-side `void Create(args)` instance method, no
        // static factory). The bindgen contract is: the resource impl
        // class implements the interface AND has a public parameter-
        // less ctor. Discovered lazily on first lookup; cached.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Type?>
            _resourceImplCache = new();

        private HostPackageResolver(
            IReadOnlyList<Assembly> hostPackages,
            Dictionary<(string Module, string Entity), Binding> bindings,
            IReadOnlyList<Type> interfaceTypes,
            HashSet<Type> resourceInterfaceTypes,
            Type? preferredBundleType,
            Type? preferredResourcesType)
        {
            HostPackages = hostPackages;
            _bindings = bindings;
            _bindingsByStrippedModule = BuildStrippedIndex(bindings);
            InterfaceTypes = interfaceTypes;
            _resourceInterfaceTypes = resourceInterfaceTypes;
            ResourceInterfaceTypes = resourceInterfaceTypes;
            PreferredBundleType = preferredBundleType;
            PreferredResourcesType = preferredResourcesType;
        }

        /// <summary>
        /// Find a concrete class implementing <paramref name="resourceInterface"/>
        /// with a public parameterless constructor. Used by direct-link
        /// emit's SourceGen-shape constructor path: the bindgen pattern
        /// is `Activator.CreateInstance(impl)` followed by a
        /// `void Create(args)` instance call, then
        /// `Resources.AllocateResource(typeof(IFace), instance)`.
        /// First match wins (stable order across HostPackages); cached.
        /// Returns false if no such class exists.
        /// </summary>
        public bool TryFindResourceImpl(Type resourceInterface,
            out Type implType)
        {
            var cached = _resourceImplCache.GetOrAdd(resourceInterface, t =>
            {
                // First: walk the explicit HostPackages list. Caller-
                // supplied; matches the existing FromAssemblies
                // contract.
                var hit = SearchForImpl(t,
                    HostPackages.Select(a => (a, true)));
                if (hit != null) return hit;

                // Fallback: walk the AppDomain. WACS.NN's typed
                // resource interfaces live in `Wacs.WASI.NN`, but the
                // SourceGen-shape impl classes (`Tensor`, `Graph`,
                // `GraphExecutionContext`) live in
                // `Wacs.WASI.NN.DependencyInjection` — a sibling
                // assembly the embedder may not have explicitly
                // listed in HostPackages. The DI assembly is loaded
                // at runtime by WasiPreview2RuntimeScope's
                // ReflectivelyAddWasiNN before transpilation runs,
                // so it's present in AppDomain even if not in
                // HostPackages. Mirrors FindBundleType's three-tier
                // search (gap 23, round-17 verification).
                hit = SearchForImpl(t,
                    AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => !a.IsDynamic)
                        .Select(a => (a, false)));
                return hit;
            });
            implType = cached!;
            return cached != null;
        }

        // Walk an assembly stream looking for the first non-abstract
        // class that implements `iface` and has a public
        // parameterless ctor. The bool tag distinguishes
        // HostPackages (where `GetExportedTypes` is the right call,
        // matching the historical contract) from AppDomain
        // assemblies (where `GetTypes` covers internal types we
        // might still want to find — but we keep the same export
        // filter for consistency with the cached behavior).
        private static Type? SearchForImpl(Type iface,
            IEnumerable<(Assembly Asm, bool _IsHostPackage)> source)
        {
            foreach (var (asm, _) in source)
            {
                Type[] types;
                try { types = asm.GetExportedTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types
                        .Where(x => x != null)
                        .Select(x => x!)
                        .ToArray();
                }
                catch
                {
                    // AppDomain assemblies may throw NotSupportedException
                    // (collectable assemblies, dynamic-but-not-flagged,
                    // etc.). Skip them — the resolver's whole-AppDomain
                    // search is a fallback, not a hard requirement, so a
                    // few skips won't mask the impl class as long as the
                    // DI assembly itself loads cleanly.
                    continue;
                }
                foreach (var ct in types)
                {
                    if (ct.IsInterface || ct.IsAbstract) continue;
                    if (!iface.IsAssignableFrom(ct)) continue;
                    if (ct.GetConstructor(Type.EmptyTypes) == null) continue;
                    return ct;
                }
            }
            return null;
        }

        public bool TryResolve(string module, string entity,
            out Binding binding)
        {
            // Exact (module-with-@version, entity) match first.
            if (_bindings.TryGetValue((module, entity), out binding!))
                return true;
            // Version-tolerant fallback: strip the trailing
            // @<version> from the requested module and look up
            // again. Honors wasm Component Model's stability
            // contract for WASI minor revisions — the same
            // (interface, function) is ABI-equivalent across patch
            // versions of WASI 0.2.x.
            string stripped = StripVersion(module);
            if (stripped.Length != module.Length
                && _bindingsByStrippedModule.TryGetValue(
                    (stripped, entity), out binding!))
                return true;
            return false;
        }

        // Strip a trailing `@<version>` from a wire module string.
        // The version is everything after the last `@` — wasm
        // Component Model wire modules are `<ns>:<pkg>/<iface>@<ver>`,
        // single `@`, no `@` inside any segment.
        private static string StripVersion(string module)
        {
            int at = module.LastIndexOf('@');
            return at < 0 ? module : module.Substring(0, at);
        }

        // Build the secondary lookup index. Multiple bindings sharing
        // the same (stripped-module, entity) — e.g. when a host
        // assembly registers both 0.2.3 and 0.2.6 versions of the
        // same interface — collapse to the first registration; the
        // exact-match path catches the other.
        private static Dictionary<(string, string), Binding>
            BuildStrippedIndex(
                Dictionary<(string Module, string Entity), Binding> src)
        {
            var dst = new Dictionary<(string, string), Binding>(src.Count);
            foreach (var kv in src)
            {
                var stripped = StripVersion(kv.Key.Module);
                if (stripped.Length == kv.Key.Module.Length) continue;
                var k = (stripped, kv.Key.Entity);
                if (!dst.ContainsKey(k)) dst[k] = kv.Value;
            }
            return dst;
        }

        /// <summary>
        /// Walk the component's canon-lower options and apply each
        /// matching binding's <see cref="Binding.StringEncoding"/>.
        /// For each (module, entity) core-module import that has a
        /// matching <c>canon lower (string-encoding=...)</c>, the
        /// binding's encoding is updated from the canon-lower's
        /// option (defaults to UTF-8 if no option declared).
        ///
        /// <para>Walks the typical wit-component / componentize-dotnet
        /// shape: one InstantiateCoreModule with args naming each
        /// import-module, each arg pointing at an
        /// InstantiateCoreInline whose exports name the canon-
        /// lowered core-funcs. Multi-module composites and other
        /// shapes are handled best-effort — unmatched imports keep
        /// their default UTF-8 encoding.</para>
        /// </summary>
        public void ApplyImportCanonOptions(
            Wacs.ComponentModel.Runtime.ComponentModule component)
        {
            if (component == null) return;

            // 1. Walk Canon section to map core-func-idx →
            //    CanonLower (only canon-lower entries care for
            //    options; canon resource.* also bumps the index but
            //    contributes no options).
            var canonLowerByCoreFunc = new Dictionary<uint, CanonLower>();
            uint coreFuncIdx = 0;
            foreach (var s in component.RawSections)
            {
                switch (s.Id)
                {
                    case ComponentSectionId.Alias:
                    {
                        var entries = AliasSectionReader.Decode(s.Payload);
                        foreach (var a in entries)
                        {
                            if (a.Sort == AliasSort.CoreSort
                                && a.CoreKind == CoreAliasKind.Func)
                                coreFuncIdx++;
                        }
                        break;
                    }
                    case ComponentSectionId.Canon:
                    {
                        var entries = CanonSectionReader.Decode(s.Payload);
                        foreach (var e in entries)
                        {
                            if (e is CanonLower lower)
                            {
                                canonLowerByCoreFunc[coreFuncIdx] = lower;
                                coreFuncIdx++;
                            }
                            else if (e is CanonResourceOp)
                            {
                                coreFuncIdx++;
                            }
                        }
                        break;
                    }
                }
            }

            // 2. Walk core-instances. The typical shape:
            //    InstantiateCoreModule has args naming each import-
            //    module; each arg.InstanceIdx → InstantiateCoreInline
            //    whose exports name the entity → core-func mapping.
            var coreInsts = component.CoreInstances;
            foreach (var inst in coreInsts)
            {
                if (!(inst is InstantiateCoreModule icm)) continue;
                foreach (var arg in icm.Args)
                {
                    var moduleName = arg.Name;
                    if (arg.InstanceIdx >= coreInsts.Count) continue;
                    var inner = coreInsts[(int)arg.InstanceIdx];
                    if (!(inner is InstantiateCoreInline inline)) continue;
                    foreach (var exp in inline.Exports)
                    {
                        if (exp.Sort != CoreSort.Func) continue;
                        if (!canonLowerByCoreFunc.TryGetValue(
                                exp.Index, out var lower)) continue;
                        var encoding = ResolveStringEncoding(lower.Options);
                        if (_bindings.TryGetValue(
                                (moduleName, exp.Name),
                                out var binding))
                            binding.StringEncoding = encoding;
                    }
                }
            }
        }

        // Pick the canon-lower's string encoding; defaults to UTF-8
        // when no string-encoding option is declared (matches
        // CanonicalABI.md's "if no option, utf8" rule).
        private static CanonOption.Kind ResolveStringEncoding(
            IReadOnlyList<CanonOption> options)
        {
            foreach (var opt in options)
            {
                switch (opt.OptionKind)
                {
                    case CanonOption.Kind.StringUtf8:
                    case CanonOption.Kind.StringUtf16:
                    case CanonOption.Kind.StringLatin1OrUtf16:
                        return opt.OptionKind;
                }
            }
            return CanonOption.Kind.StringUtf8;
        }

        /// <summary>
        /// Build a resolver from one or more host-package assemblies.
        /// When <paramref name="bundleType"/> is non-null, that type
        /// is used as <see cref="PreferredBundleType"/> verbatim
        /// (skip auto-discovery). Otherwise the resolver searches
        /// for <c>WasiPreview2Bundle</c> in the loaded assemblies,
        /// the AppDomain, and via fresh <c>Assembly.Load</c>.
        ///
        /// <para><paramref name="resourcesType"/> supplies the
        /// resource-resolver type used by direct-linked resource-
        /// method import IL. Pass null to skip — resource methods
        /// then fall back to the legacy delegate dispatch.</para>
        /// </summary>
        public static HostPackageResolver FromAssemblies(
            IReadOnlyList<Assembly> assemblies,
            Type? bundleType = null,
            Type? resourcesType = null)
        {
            if (assemblies == null) throw new ArgumentNullException(
                nameof(assemblies));

            // 1c: pre-load any DI sibling declared on the explicit
            // contract assemblies (or on already-loaded AppDomain
            // assemblies) via [WacsDependencyInjectionSibling]. Runs
            // before the [WitSource] walk so any sibling-side
            // [WitSource] interfaces are visible if a caller routes
            // the DI assembly through FromAssemblies, and before
            // TryFindResourceImpl is ever called so the AppDomain
            // walk finds the SourceGen-shape impl classes
            // (Context / Surface / Tensor / …).
            LoadDeclaredSiblings(assemblies);

            var bindings = new Dictionary<(string, string), Binding>();
            var interfaceTypes = new List<Type>();
            var seenIface = new HashSet<Type>();
            var resourceInterfaceTypes = new HashSet<Type>();

            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetExportedTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types
                        .Where(t => t != null)
                        .Select(t => t!)
                        .ToArray();
                }

                foreach (var type in types)
                {
                    if (!type.IsInterface) continue;
                    var ifaceAttr = type
                        .GetCustomAttribute<WitSourceAttribute>();
                    if (ifaceAttr == null) continue;
                    if (string.IsNullOrEmpty(ifaceAttr.Package)
                        || string.IsNullOrEmpty(ifaceAttr.Interface))
                        continue;

                    var module = ToWireModule(ifaceAttr.Package!,
                        ifaceAttr.Interface!);
                    var resourceName = ifaceAttr.Item;  // null on
                                                        // free-function
                                                        // interfaces

                    bool addedAny = false;
                    // Walk both instance + static methods. Default
                    // static interface methods (C# 8+) carry the
                    // [WitSource] for `[static]X.foo` and
                    // `[constructor]X` shapes; instance methods
                    // carry `[method]X.foo` and free functions.
                    foreach (var method in type.GetMethods(
                        BindingFlags.Public | BindingFlags.Instance
                        | BindingFlags.Static
                        | BindingFlags.DeclaredOnly))
                    {
                        var ws = method
                            .GetCustomAttribute<WitSourceAttribute>();
                        if (ws == null) continue;

                        var (entity, kind) = WireEntityFor(ws.Item,
                            method.Name, resourceName,
                            method.IsStatic);
                        if (entity == null) continue;

                        var b = new Binding(module, entity, type,
                            method, kind, resourceName);
                        // First binding wins on conflict — duplicate
                        // (module, entity) pairs across host packages
                        // would be ambiguous anyway and surface as
                        // a distinct error elsewhere.
                        if (!bindings.ContainsKey((module, entity)))
                            bindings[(module, entity)] = b;
                        addedAny = true;
                    }

                    if (addedAny && seenIface.Add(type))
                        interfaceTypes.Add(type);

                    // An interface tagged with [WitSource(Item="<resource-name>")]
                    // (i.e. resourceName non-null and not already a method-prefixed
                    // wire-form name) IS a resource interface — own<R>/borrow<R>
                    // params with this CLR type lower to a single i32 handle.
                    if (resourceName != null
                        && !resourceName.StartsWith("[", StringComparison.Ordinal))
                        resourceInterfaceTypes.Add(type);
                }
            }

            // v0 bundle convention: caller may supply the bundle
            // type explicitly (custom host packages). Otherwise the
            // resolver auto-discovers WasiPreview2Bundle from the
            // loaded assemblies / AppDomain / Assembly.Load. Returns
            // null when neither path finds a candidate; the
            // direct-linked emit path then falls back to the legacy
            // delegate dispatch.
            Type? bundle = bundleType ?? FindWasiPreview2Bundle(assemblies);

            // Auto-discover resources only when the resolver also
            // auto-discovered the bundle AND it's the WASI Preview 2
            // bundle. A caller-supplied custom bundle gets no
            // implicit resources class — it must opt in via the
            // explicit resourcesType parameter, otherwise mismatched
            // discovery would force a 3-arg ctor on a custom Module
            // class that only takes 2.
            Type? resources = resourcesType;
            if (resources == null && bundleType == null && bundle != null
                && (bundle.FullName ==
                        "Wacs.WASI.Preview2.DependencyInjection.WasiPreview2Bundle"
                    || bundle.FullName ==
                        "Wacs.WASI.NN.DependencyInjection.WasiPreview2NNBundle"
                    || bundle.FullName ==
                        "Wacs.WASI.GFX.DependencyInjection.WasiPreview2GfxBundle"))
            {
                // Both the pure-Preview2 and the composite bundle
                // route resources through WasiPreview2Resources —
                // wasi-nn's resource handles (graph / context /
                // tensor / error) ride the same per-instance
                // ResourceContext as Preview2's pollables /
                // streams / etc.
                resources = FindWasiPreview2Resources(assemblies);
            }

            return new HostPackageResolver(assemblies, bindings,
                interfaceTypes, resourceInterfaceTypes,
                bundle, resources);
        }

        // wasi:cli@0.2.3 + exit  →  wasi:cli/exit@0.2.3
        private static string ToWireModule(string packageQualified,
            string interfaceName)
        {
            int at = packageQualified.LastIndexOf('@');
            string pkgPath = at < 0
                ? packageQualified
                : packageQualified.Substring(0, at);
            string version = at < 0
                ? string.Empty
                : packageQualified.Substring(at);   // includes '@'
            return pkgPath + "/" + interfaceName + version;
        }

        // Map a WitSource Item ("get-environment" or
        // "pollable.ready") to the wasm-import entity string.
        // Free-function:        "get-environment" →
        //   ("get-environment", null)
        // Resource instance:    "pollable.ready"  →
        //   ("[method]pollable.ready", Instance)
        // The resource name is supplied by the enclosing
        // [WitSource] attribute on the interface (Item field on
        // resource interfaces).
        //
        // Map [WitSource(Item=...)] → wasm wire entity name +
        // resource-method kind. Three Item shapes appear in the
        // wild:
        //
        //   1. Wire form already ("[method]X.foo", "[static]X.foo",
        //      "[constructor]X") — pass through.
        //   2. WitHostInterfaceGenerator's "X.foo" form — needs
        //      kind disambiguation:
        //         - "X.constructor" + IsStatic   → [constructor]X
        //         - "X.foo"          + IsStatic   → [static]X.foo
        //         - "X.foo"          + !IsStatic → [method]X.foo
        //   3. Free-function "name" — pass through as the entity.
        //
        // The IsStatic signal is required to disambiguate (2) since
        // the source generator emits both static and instance
        // methods on the same interface with the same dotted Item
        // shape.
        private static (string? Entity,
                ResourceMethodKind? Kind) WireEntityFor(
            string? item, string clrMethodName,
            string? resourceName, bool isStatic = false)
        {
            if (string.IsNullOrEmpty(item))
            {
                // Method has no [WitSource] Item — fall back to
                // kebab-cased C# name. Used for free functions
                // when the source generator omits the Item field.
                return (KebabCase(clrMethodName), null);
            }

            var i = item!;
            if (i.StartsWith("[constructor]",
                StringComparison.Ordinal)
                || i.StartsWith("[static]", StringComparison.Ordinal)
                || i.StartsWith("[method]", StringComparison.Ordinal))
            {
                // Already in wire form.
                if (i.StartsWith("[constructor]",
                    StringComparison.Ordinal))
                    return (i, ResourceMethodKind.Constructor);
                if (i.StartsWith("[static]",
                    StringComparison.Ordinal))
                    return (i, ResourceMethodKind.Static);
                return (i, ResourceMethodKind.Instance);
            }

            int dot = i.IndexOf('.');
            if (dot > 0)
            {
                var resPart = i.Substring(0, dot);
                var namePart = i.Substring(dot + 1);

                // "X.constructor" → [constructor]X. The source
                // generator emits constructors as regular interface
                // methods (instance shape) because C# default
                // static interface methods aren't always practical;
                // distinguish by the `.constructor` Item suffix
                // alone, not IsStatic.
                if (namePart == "constructor")
                    return ("[constructor]" + resPart,
                        ResourceMethodKind.Constructor);

                // Static method on a resource: "X.name" + IsStatic
                // → [static]X.name (no `this` on the wire).
                if (isStatic)
                    return ("[static]" + i, ResourceMethodKind.Static);

                // Resource instance method: "X.name" + !IsStatic
                // → [method]X.name (leading i32 handle on the wire).
                return ("[method]" + i, ResourceMethodKind.Instance);
            }

            // Free function — Item == entity name (kebab-case).
            // resourceName != null shouldn't happen for a free-
            // function shape but tolerate it.
            return (i, null);
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

        // Walk the loaded assemblies (and their referenced
        // assemblies) looking for a bundle type that satisfies the
        // collected bindings. Prefers a sibling-family composite
        // (wasi-nn, wasi-gfx, …) when one is loaded — its forwarding
        // properties cover both Preview2 and the sibling [WitSource]
        // interfaces. Falls back to the Preview2-only bundle when
        // no composite is on the load path; returns null when even
        // Preview2 isn't loadable — direct-link emit then routes
        // through the legacy delegate dispatch.
        private static Type? FindWasiPreview2Bundle(
            IReadOnlyList<Assembly> assemblies)
        {
            // Attribute-driven discovery: scan every loaded assembly
            // for types carrying [WacsCompositeBundle]. Sort by
            // Priority desc then Family asc and return the winner.
            // The 1c sibling-load runs in FromAssemblies above, so
            // by the time we hit this scan every declared DI sibling
            // is already in the AppDomain.
            var winner = FindBestComposite(assemblies);
            if (winner != null) return winner;

            // Fall back to Preview2-only — for components without
            // wasi:nn or wasi-gfx imports.
            const string preview2Name =
                "Wacs.WASI.Preview2.DependencyInjection.WasiPreview2Bundle";
            return FindBundleType(preview2Name, assemblies,
                fallbackAssembly: "Wacs.WASI.Preview2.DependencyInjection");
        }

        // Attribute-driven composite-bundle scan. Walks `assemblies`
        // first (the caller-supplied set), then AppDomain (so a
        // sibling DI assembly auto-loaded by reference is still
        // visible). Highest Priority wins; ties break by Family asc
        // for deterministic ordering across runs.
        // Walks the declared-sibling attributes on every assembly
        // in `assemblies` (plus any sibling-tagged assembly already
        // loaded into the AppDomain) and Assembly.Load()s each
        // declared sibling. Idempotent; quiet on failure (a missing
        // sibling DLL is not fatal — the resolver simply finds
        // fewer impls and direct-link emit falls back to delegate
        // dispatch). Called by FindWasiPreview2Bundle before the
        // composite walk; also re-callable from external callers
        // that want sibling-load semantics without going through
        // FromAssemblies.
        internal static void LoadDeclaredSiblings(
            IReadOnlyList<Assembly> assemblies)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void LoadFromAttrs(Assembly asm)
            {
                var attrs = asm.GetCustomAttributes(
                    typeof(Wacs.ComponentModel.Runtime
                        .WacsDependencyInjectionSiblingAttribute),
                    inherit: false);
                foreach (var raw in attrs)
                {
                    var attr = (Wacs.ComponentModel.Runtime
                        .WacsDependencyInjectionSiblingAttribute)raw;
                    if (!seen.Add(attr.AssemblyName)) continue;
                    try { Assembly.Load(attr.AssemblyName); }
                    catch { /* sibling not on disk — non-fatal */ }
                }
            }
            foreach (var asm in assemblies) LoadFromAttrs(asm);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                LoadFromAttrs(asm);
            }
        }

        internal static Type? FindBestComposite(
            IReadOnlyList<Assembly> assemblies)
        {
            var seen = new HashSet<Type>();
            var candidates = new List<(Type Type, int Priority, string Family)>();
            void Inspect(Assembly asm)
            {
                if (asm.IsDynamic) return;
                Type[] types;
                try { types = asm.GetExportedTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types
                        .Where(t => t != null)
                        .Select(t => t!)
                        .ToArray();
                }
                foreach (var t in types)
                {
                    if (!seen.Add(t)) continue;
                    var attr = t.GetCustomAttribute<
                        Wacs.ComponentModel.Runtime
                            .WacsCompositeBundleAttribute>(
                                inherit: false);
                    if (attr == null) continue;
                    candidates.Add((t, attr.Priority, attr.Family));
                }
            }
            foreach (var asm in assemblies) Inspect(asm);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                Inspect(asm);

            return SelectBestComposite(candidates);
        }

        /// <summary>
        /// Sort-only pick from a candidate list. Highest Priority
        /// wins; ties break by Family ascending (lexicographic).
        /// Returns null when the input is empty. Extracted from
        /// <see cref="FindBestComposite"/> so tests can exercise the
        /// selection rule without registering attributed types into
        /// the AppDomain (which would pollute every other resolver
        /// run in the same test process).
        /// </summary>
        internal static Type? SelectBestComposite(
            IReadOnlyList<(Type Type, int Priority, string Family)> candidates)
        {
            if (candidates.Count == 0) return null;
            var sorted = candidates.ToList();
            sorted.Sort((a, b) =>
            {
                int p = b.Priority.CompareTo(a.Priority);
                return p != 0 ? p
                    : string.Compare(a.Family, b.Family,
                        StringComparison.Ordinal);
            });
            return sorted[0].Type;
        }

        private static Type? FindBundleType(string qualifiedName,
            IReadOnlyList<Assembly> assemblies,
            string fallbackAssembly)
        {
            foreach (var asm in assemblies)
            {
                var t = asm.GetType(qualifiedName, false);
                if (t != null) return t;
            }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                var t = asm.GetType(qualifiedName, false);
                if (t != null) return t;
            }
            try
            {
                var asm = Assembly.Load(fallbackAssembly);
                return asm.GetType(qualifiedName, false);
            }
            catch
            {
                return null;
            }
        }

        // Companion to FindWasiPreview2Bundle — resolves the
        // resources bridge that satisfies the
        // GetResource/AllocateResource convention. Shipped in the
        // same DI package so a single Assembly.Load covers both.
        private static Type? FindWasiPreview2Resources(
            IReadOnlyList<Assembly> assemblies)
        {
            const string resourcesQualifiedName =
                "Wacs.WASI.Preview2.DependencyInjection.WasiPreview2Resources";

            foreach (var asm in assemblies)
            {
                var t = asm.GetType(resourcesQualifiedName, false);
                if (t != null) return t;
            }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                var t = asm.GetType(resourcesQualifiedName, false);
                if (t != null) return t;
            }
            try
            {
                var asm = Assembly.Load(
                    "Wacs.WASI.Preview2.DependencyInjection");
                return asm.GetType(resourcesQualifiedName, false);
            }
            catch
            {
                return null;
            }
        }

        public enum ResourceMethodKind
        {
            Instance,
            Static,
            Constructor,
        }

        public sealed class Binding
        {
            public string Module { get; }
            public string Entity { get; }
            public Type InterfaceType { get; }
            public MethodInfo Method { get; }
            public ResourceMethodKind? ResourceKind { get; }
            public string? ResourceName { get; }

            /// <summary>
            /// Canon-ABI string encoding for any string parameter
            /// or return on this import. Defaults to UTF-8 — the
            /// canonical default. Set explicitly when the wasm
            /// component declares <c>canon lower (string-encoding=
            /// utf16)</c> or <c>latin1+utf16</c>.
            /// </summary>
            public CanonOption.Kind StringEncoding { get; set; }

            public bool IsFreeFunction => ResourceKind == null;
            public bool IsResourceMethod => ResourceKind.HasValue;

            public Binding(string module, string entity,
                Type interfaceType, MethodInfo method,
                ResourceMethodKind? resourceKind,
                string? resourceName,
                CanonOption.Kind stringEncoding =
                    CanonOption.Kind.StringUtf8)
            {
                Module = module;
                Entity = entity;
                InterfaceType = interfaceType;
                Method = method;
                ResourceKind = resourceKind;
                ResourceName = resourceName;
                StringEncoding = stringEncoding;
            }

            public override string ToString() =>
                Module + "/" + Entity + " → "
                + InterfaceType.Name + "." + Method.Name;
        }
    }
}
