// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Wacs.ComponentModel.CSharpEmit;
using Wacs.ComponentModel.Types;
using Wacs.ComponentModel.WIT;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Loads a harness <c>.dll</c> (produced by
    /// <c>WACS.ComponentModel.Harness.Lib</c>) and exposes its
    /// emitted shapes so the transpiler can have its output
    /// reference them at the CLR level: pre-registered named types
    /// for <c>ComponentExportsEmit</c> + the
    /// <c>I{World}</c> interface for the HarnessImpl wrapper class.
    ///
    /// <para>The harness emitter places its types in a configurable
    /// namespace (default
    /// <c>Wacs.ComponentModel.Harness.Generated</c>); we look up
    /// by SHORT NAME against the WIT contract's named-type list to
    /// stay namespace-agnostic.</para>
    /// </summary>
    internal sealed class HarnessAssemblyBinder
    {
        public Assembly Assembly { get; }
        public Type? WorldInterface { get; }
        public IReadOnlyDictionary<string, Type> NamedTypes { get; }

        private HarnessAssemblyBinder(Assembly asm, Type? worldInterface,
            IReadOnlyDictionary<string, Type> namedTypes)
        {
            Assembly = asm;
            WorldInterface = worldInterface;
            NamedTypes = namedTypes;
        }

        /// <summary>
        /// Load the harness assembly at <paramref name="path"/>,
        /// parse the embedded <c>_WitContract</c> static field to
        /// recover the harness's own world name + named-type list,
        /// then locate the <c>I{World}</c> interface and the
        /// named-type classes by short name. Returns null if the
        /// path doesn't exist or the assembly can't be loaded /
        /// doesn't carry a usable contract.
        ///
        /// <para>The harness's <em>own</em> contract — not the
        /// loaded component's WIT — is the source of truth here:
        /// the component might decode to a synthesized world name
        /// (e.g. "root" from the primary-section decoder), but the
        /// emitted interface in the assembly is named after the
        /// harness's authored world.</para>
        /// </summary>
        public static HarnessAssemblyBinder? TryLoad(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!File.Exists(path)) return null;

            Assembly asm;
            try { asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path)); }
            catch { return null; }

            // Walk public types into a short-name lookup.
            var byShortName = new Dictionary<string, Type>();
            foreach (var t in asm.GetExportedTypes())
            {
                if (!byShortName.ContainsKey(t.Name))
                    byShortName[t.Name] = t;
            }

            // Recover the harness's own contract from any type's
            // _WitContract static field (every harness emits one).
            string? contractText = null;
            foreach (var t in byShortName.Values)
            {
                var f = t.GetField("_WitContract",
                    BindingFlags.Public | BindingFlags.Static);
                if (f != null && f.FieldType == typeof(string))
                {
                    contractText = (string?)f.GetValue(null);
                    if (!string.IsNullOrEmpty(contractText)) break;
                }
            }
            if (string.IsNullOrEmpty(contractText)) return null;

            CtPackage? contract = null;
            try
            {
                var parsed = WitParser.Parse(contractText!);
                var converted = WitToTypes.Convert(parsed);
                WitResolver.Resolve(converted);
                contract = converted.FirstOrDefault(p => p.Worlds.Count > 0);
            }
            catch { contract = null; }
            if (contract == null) return null;

            var world = contract.Worlds.FirstOrDefault();
            if (world == null) return null;

            var ifaceName = "I" + NameMangler.ToPascalCase(world.Name);
            byShortName.TryGetValue(ifaceName, out var worldInterface);

            var namedTypes = new Dictionary<string, Type>();
            foreach (var nt in world.Types)
            {
                var pascal = NameMangler.ToPascalCase(nt.Name);
                if (byShortName.TryGetValue(pascal, out var clr))
                    namedTypes[nt.Name] = clr;
            }

            return new HarnessAssemblyBinder(asm, worldInterface, namedTypes);
        }
    }
}
