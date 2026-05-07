// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.WASI.Preview2.HostBinding
{
    /// <summary>
    /// Per-component-instance registry of resource handle
    /// tables, keyed by C# <see cref="Type"/>. The auto-binder
    /// looks up tables here when emitting allocate-on-return
    /// wrappers (own&lt;T&gt; results) and lookup-on-call
    /// wrappers (borrow&lt;T&gt; / own&lt;T&gt; params).
    ///
    /// <para>One <see cref="ResourceContext"/> instance lives
    /// for the lifetime of a <c>ComponentInstance</c>; the
    /// configureImports callback receives it via
    /// <see cref="WasiInterfaceBinder"/>'s extension API so
    /// host bindings can share table state across multiple
    /// interfaces (e.g. wasi:io/streams returning streams that
    /// wasi:cli/stdout consumes).</para>
    /// </summary>
    public sealed class ResourceContext
    {
        private readonly Dictionary<Type, ResourceTable> _tables = new();

        /// <summary>Get-or-create the handle table for
        /// <paramref name="resourceType"/>. Stable identity —
        /// repeated calls for the same type return the same
        /// table.</summary>
        public ResourceTable TableFor(Type resourceType)
        {
            if (resourceType == null)
                throw new ArgumentNullException(nameof(resourceType));
            if (!_tables.TryGetValue(resourceType, out var t))
            {
                t = new ResourceTable();
                _tables[resourceType] = t;
            }
            return t;
        }

        /// <summary>Generic shorthand for
        /// <see cref="TableFor(Type)"/>. Lets binding code write
        /// <c>resources.Table&lt;Pollable&gt;()</c> without the
        /// typeof noise. Named <c>Table</c> rather than
        /// <c>TableFor</c> to avoid an
        /// <see cref="System.Reflection.AmbiguousMatchException"/>
        /// when the legacy binder reflects on
        /// <c>nameof(ResourceContext.TableFor)</c>.</summary>
        public ResourceTable Table<T>() => TableFor(typeof(T));

        /// <summary>Number of distinct resource types tracked.
        /// Diagnostic hook for tests that want to inspect the
        /// composition of a component's host-side state.</summary>
        public int TypeCount => _tables.Count;
    }
}
