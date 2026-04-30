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
        private readonly HashSet<Type> _resourceInterfaceTypes;

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
            InterfaceTypes = interfaceTypes;
            _resourceInterfaceTypes = resourceInterfaceTypes;
            ResourceInterfaceTypes = resourceInterfaceTypes;
            PreferredBundleType = preferredBundleType;
            PreferredResourcesType = preferredResourcesType;
        }

        public bool TryResolve(string module, string entity,
            out Binding binding)
        {
            return _bindings.TryGetValue((module, entity), out binding!);
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

            return new HostPackageResolver(assemblies, bindings,
                interfaceTypes, resourceInterfaceTypes,
                bundle, resourcesType);
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
        // assemblies) looking for WasiPreview2Bundle. The DI
        // package is a separate assembly from Wacs.WASI.Preview2;
        // when --wasip2 is on, the user's host project usually
        // references both, so a Type.GetType across loaded asms
        // suffices. Returns null if the bundle isn't loadable.
        private static Type? FindWasiPreview2Bundle(
            IReadOnlyList<Assembly> assemblies)
        {
            const string bundleQualifiedName =
                "Wacs.WASI.Preview2.DependencyInjection.WasiPreview2Bundle";

            // First — check assemblies the resolver was handed.
            foreach (var asm in assemblies)
            {
                var t = asm.GetType(bundleQualifiedName, false);
                if (t != null) return t;
            }

            // Then — check the AppDomain (the DI assembly may
            // already be loaded by the host process).
            foreach (var asm in AppDomain.CurrentDomain
                .GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                var t = asm.GetType(bundleQualifiedName, false);
                if (t != null) return t;
            }

            // Last — try Assembly.Load by name. Catches the case
            // where the DI assembly is on disk but not yet loaded.
            try
            {
                var asm = Assembly.Load(
                    "Wacs.WASI.Preview2.DependencyInjection");
                return asm.GetType(bundleQualifiedName, false);
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
