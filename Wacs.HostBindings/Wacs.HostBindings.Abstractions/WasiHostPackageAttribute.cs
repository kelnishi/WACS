// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.HostBindings.Abstractions
{
    /// <summary>
    /// Assembly-level marker that flags an assembly as a WACS host
    /// package — i.e. a library shipping at least one type
    /// implementing <c>IBindable</c> with a parameterless ctor. The
    /// runtime's auto-discovery extension
    /// (<c>runtime.AutoDiscoverHostPackages()</c>) scans loaded
    /// assemblies for this attribute, then activates and binds every
    /// <c>IBindable</c> it finds.
    ///
    /// <para>The CLI's <c>--bind</c> flag and the explicit
    /// <c>runtime.UseHostPackages(name1, name2, …)</c> path don't
    /// require this attribute — they activate every <c>IBindable</c>
    /// with a parameterless ctor regardless. The attribute is for the
    /// AppDomain-scan path: it lets a consumer wire every host package
    /// they have on the load path with one call, without enumerating
    /// each by name.</para>
    ///
    /// <para><b>Apply at the assembly level only:</b>
    /// <code>[assembly: WasiHostPackage]</code>
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiHostPackageAttribute : Attribute
    {
        /// <summary>
        /// Optional human-readable label for the package, surfaced in
        /// verbose CLI output. Defaults to the assembly's simple name
        /// when null.
        /// </summary>
        public string? Label { get; }

        public WasiHostPackageAttribute() { }

        public WasiHostPackageAttribute(string label)
        {
            Label = label;
        }
    }
}
