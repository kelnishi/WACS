// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Reflection;
using Wacs.Core.Runtime;
using Wacs.HostBindings.Abstractions;

namespace Wacs.Transpiler.Hosting
{
    /// <summary>
    /// Ergonomic <see cref="WasmRuntime"/> extensions for
    /// <see cref="IBindable"/>-based host packages.
    ///
    /// <para>Two flavors:</para>
    /// <list type="bullet">
    ///   <item><description><see cref="UseHostPackages(WasmRuntime, string[])"/>
    ///   takes explicit assembly names or paths and binds every
    ///   <see cref="IBindable"/> with a parameterless ctor each
    ///   one ships. This is the named-import shape — embedders
    ///   that know which packages they want.</description></item>
    ///   <item><description><see cref="AutoDiscoverHostPackages(WasmRuntime, AppDomain)"/>
    ///   walks every loaded assembly in the supplied
    ///   <see cref="AppDomain"/> looking for the
    ///   <see cref="WasiHostPackageAttribute"/> marker and binds
    ///   each tagged package's <see cref="IBindable"/> types. The
    ///   "I have whatever's loaded" shape — useful for plugin
    ///   hosts, but yields control of which packages get wired
    ///   to the load context.</description></item>
    /// </list>
    /// </summary>
    public static class HostPackageExtensions
    {
        /// <summary>
        /// Load each named assembly (file path or assembly name —
        /// resolved by <see cref="BindingLoader.LoadFromAssembly(string)"/>)
        /// and bind every <see cref="IBindable"/> it contains.
        /// </summary>
        public static List<IBindable> UseHostPackages(
            this WasmRuntime runtime,
            params string[] namesOrPaths)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            var bindings = new List<IBindable>();
            if (namesOrPaths == null) return bindings;
            foreach (var n in namesOrPaths)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                var loaded = BindingLoader.LoadFromAssembly(n);
                foreach (var b in loaded)
                {
                    b.BindToRuntime(runtime);
                    bindings.Add(b);
                }
            }
            return bindings;
        }

        /// <summary>
        /// Scan every loaded assembly in <paramref name="domain"/>
        /// (defaulting to <see cref="AppDomain.CurrentDomain"/>)
        /// for the <see cref="WasiHostPackageAttribute"/> marker
        /// and bind every <see cref="IBindable"/> the tagged
        /// assemblies expose.
        /// </summary>
        public static List<IBindable> AutoDiscoverHostPackages(
            this WasmRuntime runtime,
            AppDomain? domain = null)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            domain ??= AppDomain.CurrentDomain;
            var bindings = new List<IBindable>();
            foreach (var asm in domain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                if (asm.GetCustomAttribute<WasiHostPackageAttribute>() == null)
                    continue;
                var loaded = BindingLoader.LoadFromAssembly(asm);
                foreach (var b in loaded)
                {
                    b.BindToRuntime(runtime);
                    bindings.Add(b);
                }
            }
            return bindings;
        }
    }
}
