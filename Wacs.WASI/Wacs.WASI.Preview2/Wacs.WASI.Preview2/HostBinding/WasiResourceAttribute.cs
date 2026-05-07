// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.Preview2.HostBinding
{
    /// <summary>
    /// Marks a class as a host-side WASI resource type — the
    /// C# representation of a WIT <c>resource T</c>. Instances
    /// are stored in a per-component handle table; the wire
    /// form of <c>own&lt;T&gt;</c> and <c>borrow&lt;T&gt;</c>
    /// is an i32 handle indexing into that table.
    ///
    /// <para>The auto-binder uses this attribute to disambiguate
    /// resource-returning host methods from
    /// record-of-primitives-returning ones — both look like
    /// "host method returns a class with public fields", but
    /// resources need handle-table allocation while records
    /// get inline canonical-ABI layout.</para>
    ///
    /// <para>Implementing <see cref="System.IDisposable"/> on a
    /// resource class is recommended — the guest's
    /// <c>resource.drop</c> calls translate to <c>Dispose</c>
    /// on the host side.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiResourceAttribute : Attribute
    {
        /// <summary>WIT resource type name (kebab-case). The
        /// auto-binder uses this to register methods under
        /// <c>[method]Name.method-name</c> in the same
        /// namespace as the parent interface. When omitted, the
        /// binder kebab-cases the C# class name.</summary>
        public string? WitName { get; }

        public WasiResourceAttribute(string? witName = null)
        {
            WitName = witName;
        }
    }
}
