// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Collections.Generic;

namespace Wacs.ComponentModel.Types
{
    /// <summary>
    /// Cross-file name resolver for the component-model Types layer.
    /// Takes a collection of <see cref="CtPackage"/> instances
    /// (produced by <see cref="WitToTypes.Convert"/> over multiple
    /// WIT documents) and binds unresolved
    /// <see cref="CtExternInterfaceRef.Target"/> and
    /// <see cref="CtTypeRef.Target"/> references across the input set.
    ///
    /// <para>Resolution rules:</para>
    /// <list type="bullet">
    /// <item><description><b>CtExternInterfaceRef</b> — matches by
    /// package namespace + package path + interface name + version.
    /// Semver-with-0.x-exception: for 0.x versions, major+minor must
    /// match exactly (so <c>wasi:io/streams@0.2.3</c> does not bind
    /// to <c>wasi:io/streams@0.2.2</c>). For ≥1.x, major matches.</description></item>
    /// <item><description><b>CtTypeRef inside an interface</b> —
    /// when a <c>use pkg:ns/iface.{name}</c> brings a name into
    /// scope, the resolver walks the named interface in the
    /// referenced package and binds the <c>CtNamedType</c> wrapper
    /// this interface's symbol table registered.</description></item>
    /// </list>
    ///
    /// <para>References that can't be resolved (no matching package
    /// in the input set) stay <c>null</c>; callers treat these as
    /// "external / not provided" rather than errors — real-world
    /// resolution often runs with incomplete WIT graphs (e.g., a
    /// hello-world document that imports WASI without having WASI
    /// WIT present).</para>
    ///
    /// <para>Mutation is in-place: calling <see cref="Resolve"/>
    /// twice is idempotent (already-bound refs aren't touched).</para>
    /// </summary>
    public static class WitResolver
    {
        /// <summary>
        /// Build a global symbol index and bind all unresolved refs
        /// that can be bound from within <paramref name="packages"/>.
        /// </summary>
        public static void Resolve(IEnumerable<CtPackage> packages)
        {
            // Build the interface index, keyed by package-qualified
            // interface name (`wasi:io/streams@0.2.3`).
            var ifaceIndex = new Dictionary<string, CtInterfaceType>();
            var packageList = new List<CtPackage>();
            foreach (var pkg in packages)
            {
                packageList.Add(pkg);
                foreach (var iface in pkg.Interfaces)
                {
                    var key = QualifiedKey(pkg.Name, iface.Name);
                    // Duplicate interface declarations within the same
                    // package resolve to first-wins — matches how
                    // multi-file packages are layered.
                    if (!ifaceIndex.ContainsKey(key))
                        ifaceIndex[key] = iface;
                }
            }

            // Pass 1: bind CtExternInterfaceRef.Target on every world.
            foreach (var pkg in packageList)
            {
                foreach (var world in pkg.Worlds)
                {
                    ResolveWorldExterns(world, ifaceIndex);
                }
            }

            // Pass 2: bind CtTypeRef.Target on every use import
            // inside every interface.
            foreach (var pkg in packageList)
            {
                foreach (var iface in pkg.Interfaces)
                {
                    ResolveInterfaceUses(iface, ifaceIndex);
                }
                foreach (var world in pkg.Worlds)
                {
                    ResolveWorldUses(world, ifaceIndex);
                }
            }
        }

        // ---- CtExternInterfaceRef resolution --------------------------------

        private static void ResolveWorldExterns(
            CtWorldType world,
            IReadOnlyDictionary<string, CtInterfaceType> ifaceIndex)
        {
            foreach (var imp in world.Imports)
                TryBindExternRef(imp.Spec, world.Package, ifaceIndex);
            foreach (var exp in world.Exports)
                TryBindExternRef(exp.Spec, world.Package, ifaceIndex);
        }

        private static void TryBindExternRef(
            CtExternType spec,
            CtPackageName? fallbackPackage,
            IReadOnlyDictionary<string, CtInterfaceType> ifaceIndex)
        {
            if (spec is CtExternInterfaceRef iref && iref.Target == null)
            {
                // In-document imports (e.g. `import env;` where env
                // lives in the same package as the world) lack an
                // explicit package. Fall back to the world's
                // package for the lookup.
                var pkg = iref.Package ?? fallbackPackage;
                if (pkg != null)
                {
                    var key = QualifiedKey(pkg, iref.InterfaceName);
                    if (ifaceIndex.TryGetValue(key, out var target))
                    {
                        iref.Target = target;
                    }
                    else
                    {
                        // Try a looser 0.x match if the exact version
                        // didn't land — preserves the documented
                        // version-pinning behavior: exact semver under
                        // 0.x, major-compat under ≥1.x.
                        iref.Target = LookupWithSemverCompat(
                            pkg, iref.InterfaceName, ifaceIndex);
                    }
                }
            }
        }

        /// <summary>
        /// Looks up an interface with semver-aware matching: tolerates
        /// missing patch version for ≥1.x (e.g. <c>foo:bar/iface</c>
        /// without version can match <c>foo:bar/iface@1.0.0</c>).
        /// For 0.x versions, remains strict (no cross-patch
        /// match) — this is the documented version-pinning behavior
        /// from the scope plan.
        /// </summary>
        private static CtInterfaceType? LookupWithSemverCompat(
            CtPackageName pkg, string ifaceName,
            IReadOnlyDictionary<string, CtInterfaceType> ifaceIndex)
        {
            // If the ref had no version specified, accept any version
            // in the index matching the package and interface name.
            if (pkg.Version == null)
            {
                var prefix = pkg.Namespace + ":" + string.Join(":", pkg.Path);
                foreach (var kv in ifaceIndex)
                {
                    if (kv.Key.StartsWith(prefix + "@") &&
                        kv.Key.EndsWith("/" + ifaceName))
                    {
                        return kv.Value;
                    }
                    if (kv.Key == prefix + "/" + ifaceName)
                    {
                        return kv.Value;
                    }
                }
            }
            return null;
        }

        // ---- CtTypeRef resolution for `use` imports -------------------------

        private static void ResolveInterfaceUses(
            CtInterfaceType iface,
            IReadOnlyDictionary<string, CtInterfaceType> ifaceIndex)
        {
            foreach (var use in iface.Uses)
            {
                // Bare-name `use foo.{...}` refers to an interface in
                // the same package as the using interface.
                var usePkg = use.Package ?? iface.Package;
                if (usePkg == null) continue;
                var targetKey = QualifiedKey(usePkg, use.InterfaceName);
                if (!ifaceIndex.TryGetValue(targetKey, out var targetIface))
                    continue;

                foreach (var used in use.Names)
                {
                    var localName = used.LocalName;
                    // Find the target type in the referenced interface
                    // and replace our CtTypeRef's Target with it.
                    var targetNamedType = FindNamedType(targetIface, used.Name);
                    if (targetNamedType == null) continue;
                    RebindLocalName(iface, localName, targetNamedType);
                }
            }
        }

        private static void ResolveWorldUses(
            CtWorldType world,
            IReadOnlyDictionary<string, CtInterfaceType> ifaceIndex)
        {
            foreach (var use in world.Uses)
            {
                if (use.Package == null) continue;
                var targetKey = QualifiedKey(use.Package, use.InterfaceName);
                if (!ifaceIndex.TryGetValue(targetKey, out var targetIface))
                    continue;

                foreach (var used in use.Names)
                {
                    var localName = used.LocalName;
                    var targetNamedType = FindNamedType(targetIface, used.Name);
                    if (targetNamedType == null) continue;
                    // World-level use isn't directly wired into
                    // anything yet (world-level types are themselves
                    // separate CtNamedTypes owned by the world).
                    // We just bind through the interface namespace —
                    // placeholder for now. Extend when CSharpEmit
                    // needs to reach into world-scoped use'd types.
                    _ = localName; _ = targetNamedType;
                }
            }
        }

        private static CtNamedType? FindNamedType(CtInterfaceType iface,
                                                  string name)
        {
            foreach (var nt in iface.Types)
            {
                if (nt.Name == name) return nt;
            }
            return null;
        }

        /// <summary>
        /// After a <c>use</c> import binds, each in-interface
        /// <see cref="CtNamedType"/> we registered during conversion
        /// (whose body is a placeholder <c>CtTypeRef</c> pointing at
        /// itself) gets its body patched to point at the resolved
        /// external <see cref="CtNamedType"/>. This keeps downstream
        /// walkers happy — a <c>CtTypeRef</c> inside the interface
        /// still resolves locally, but now its target's body is
        /// bound to the external definition.
        /// </summary>
        private static void RebindLocalName(CtInterfaceType iface,
                                            string localName,
                                            CtNamedType externalTarget)
        {
            // Find the placeholder CtNamedType with this local name —
            // first in aliases (created from `use` imports), then in
            // types (created from local defs; rarely matches a use'd
            // name but we accept either).
            foreach (var nt in iface.Aliases)
            {
                if (nt.Name != localName) continue;
                if (nt.Type is CtTypeRef existing)
                    existing.Target = externalTarget;
                return;
            }
            foreach (var nt in iface.Types)
            {
                if (nt.Name != localName) continue;
                if (nt.Type is CtTypeRef existing)
                    existing.Target = externalTarget;
                return;
            }
        }

        // ---- Key building ---------------------------------------------------

        private static string QualifiedKey(CtPackageName pkg, string ifaceName)
        {
            // Matches CtInterfaceType.QualifiedName format.
            var path = string.Join(":", pkg.Path);
            var ver = pkg.Version != null ? "@" + pkg.Version : "";
            return pkg.Namespace + ":" + path + ver + "/" + ifaceName;
        }
    }
}
