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
        public static WitContract FromAssembly(Assembly assembly,
            string resourcePrefix = "wit/")
        {
            if (assembly == null) throw new ArgumentNullException(
                nameof(assembly));
            var sources = ReadEmbeddedWit(assembly, resourcePrefix);
            if (sources.Count == 0)
                throw new InvalidOperationException(
                    "Assembly " + assembly.GetName().Name
                    + " has no embedded WIT resources under prefix '"
                    + resourcePrefix + "'. Add "
                    + "<EmbeddedResource Include=\"wit\\**\\*.wit\" "
                    + "LogicalName=\"wit/%(RecursiveDir)%(Filename)%(Extension)\" /> "
                    + "to the project, or pass a different prefix.");

            // Group by directory under the prefix — mirrors
            // WitLoader.LoadDirectoryTree's behavior so
            // headerless siblings attribute to the named package
            // in their directory.
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
            return BuildFromPackages(packages);
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
            foreach (var pkg in packages)
            {
                foreach (var iface in pkg.Interfaces)
                {
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
                        imports.Add(BuildEntry(module, fn.Name,
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

        private static string ResourceMethodEntity(string resName,
            CtResourceMethod m)
        {
            return m.Kind switch
            {
                CtResourceMethodKind.Constructor =>
                    "[constructor]" + resName,
                CtResourceMethodKind.Static =>
                    "[static]" + resName + "." + m.Name,
                _ => "[method]" + resName + "." + m.Name,
            };
        }

        // Wire-shape arity is the canon-lowered "flat" form.
        // The validator checks param-count + return-count
        // equality; exact per-slot ValType matching is a
        // follow-up. <paramref name="resourceMethodKind"/>
        // is null for free functions; instance methods take
        // an implicit self handle, constructors return one.
        private static ImportEntry BuildEntry(string module,
            string name, CtFunctionType fn,
            CtResourceMethodKind? resourceMethodKind = null)
        {
            int paramSlots = 0;
            // [method]X.foo — receiver passes its own handle
            // as the first wire slot.
            if (resourceMethodKind == CtResourceMethodKind.Instance)
                paramSlots += 1;
            foreach (var p in fn.Params)
                paramSlots += FlatSlotCount(p.Type);

            // Canon-ABI direction: host-imported functions cap
            // at MAX_FLAT_RESULTS = 1. Anything wider lowers to
            // a retArea pointer (1 trailing param) + void
            // return. result<...>, list<X>, tuple<X,Y>,
            // option<...> with multi-slot inner — all use
            // retArea.
            int rawReturnSlots;
            bool resultReturn = false;
            if (resourceMethodKind == CtResourceMethodKind.Constructor)
            {
                // [constructor]X — implicit own<X> return,
                // 1 handle slot.
                rawReturnSlots = 1;
            }
            else if (fn.HasNoResult)
            {
                rawReturnSlots = 0;
            }
            else
            {
                rawReturnSlots = FlatSlotCount(fn.Result!);
                // WACS convention: any result<...> return uses
                // the retArea pointer pattern, even for
                // result<_, _> where the canon-ABI flat form
                // would be a single i32 disc. Matches the
                // bindings' uniform Write* helper signatures.
                resultReturn = ResolveBody(fn.Result!)
                    is CtResultType;
            }

            int returnSlots;
            if (!resultReturn && rawReturnSlots <= 1)
            {
                returnSlots = rawReturnSlots;
            }
            else
            {
                // Hoist to retArea — host receives an extra i32
                // trailing param, returns void.
                paramSlots += 1;
                returnSlots = 0;
            }
            return new ImportEntry(module, name,
                paramSlots, returnSlots);
        }

        // Follow CtTypeRef chains to the underlying body —
        // matches HostInterfaceEmit.ResolveTarget. Used for
        // shape-classification predicates like "is this
        // ultimately a result<...>?".
        private static CtValType ResolveBody(CtValType t)
        {
            while (t is CtTypeRef r && r.Target?.Type != null
                && !ReferenceEquals(r.Target.Type, t))
            {
                t = r.Target.Type;
            }
            return t;
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
                bool returnsResult =
                    m.ReturnType.IsGenericType
                    && m.ReturnType.GetGenericTypeDefinition()
                        == typeof(Result<,>);
                int returnSlots;
                if (m.ReturnType == typeof(void))
                    returnSlots = 0;
                else if (returnsResult)
                {
                    paramSlots += 1; // retArea
                    returnSlots = 0;
                }
                else
                    returnSlots = FlatSlotCountForClrType(m.ReturnType);

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
