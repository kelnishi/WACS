// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>
    /// Declares a companion dependency-injection assembly that a
    /// host-package contract assembly relies on at transpile /
    /// runtime resolution. Applied at the assembly level on the
    /// contract assembly (e.g. <c>Wacs.WASI.GFX</c>) and read by
    /// <c>HostPackageResolver</c> + <c>WasiPreview2RuntimeScope</c>
    /// to <c>Assembly.Load</c> the sibling automatically — before
    /// 1c, every new host family needed three redundant load
    /// mechanisms (ProjectReference, explicit
    /// <c>Assembly.Load</c> in the IBindable, and an entry in the
    /// CLI's <c>ResolveHostPackages</c>) because none alone
    /// covered every race.
    ///
    /// <para>Apply as a top-level assembly attribute. Multiple
    /// attributes on the same assembly stack — typical when one
    /// contract declares both a DI sibling and a backend sibling
    /// (rare but legal).</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly,
        AllowMultiple = true, Inherited = false)]
    public sealed class WacsDependencyInjectionSiblingAttribute
        : Attribute
    {
        /// <summary>The simple assembly name to <c>Assembly.Load</c>
        /// when the declaring assembly is reflectively scanned.
        /// Convention: a sibling DI package, e.g.
        /// <c>"Wacs.WASI.GFX.DependencyInjection"</c>.</summary>
        public string AssemblyName { get; }

        public WacsDependencyInjectionSiblingAttribute(string assemblyName)
        {
            AssemblyName = assemblyName ?? throw new ArgumentNullException(
                nameof(assemblyName));
        }
    }
}
