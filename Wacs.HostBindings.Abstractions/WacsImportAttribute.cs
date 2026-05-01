// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;

namespace Wacs.HostBindings
{
    /// <summary>
    /// Marks a static method as the implementation of a wasm import. Consumed
    /// by <c>WACS.HostBindings.SourceGen</c> at consumer build time: the
    /// generator scans referenced assemblies for these attributes and emits
    /// a <c>GeneratedHostImports</c> class implementing the transpiled
    /// module's <c>IImports</c> interface, with each method body delegating
    /// to the matched binding.
    ///
    /// <para>Annotated method shape: <c>public static T Method(WacsHostMemory mem,
    /// [TState state,] T1 arg1, ... TN argN)</c>. The <c>state</c> parameter is
    /// optional; when present, the source generator threads it through from a
    /// constructor argument on <c>GeneratedHostImports</c>. Multiple bindings
    /// can share the same state type; bindings that don't take state are bound
    /// purely from <c>(mem, args)</c>.</para>
    ///
    /// <para>Module + name match the wasm import section's
    /// <c>(module, field)</c> tuple verbatim. WASI Preview 1 imports use
    /// <c>module = "wasi_snapshot_preview1"</c>; custom hosts pick their
    /// own module name.</para>
    ///
    /// <para>This attribute has no runtime effect by itself — it only
    /// influences the source generator at consumer compile time.
    /// AOT-compatible by construction (no reflection at runtime).</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class WacsImportAttribute : Attribute
    {
        public string Module { get; }
        public string Name { get; }

        public WacsImportAttribute(string module, string name)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }
}
