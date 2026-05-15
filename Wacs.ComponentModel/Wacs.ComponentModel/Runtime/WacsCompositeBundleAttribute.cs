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
    /// Marks a class as a composite host bundle — a typed aggregate
    /// that forwards <c>[WitSource]</c>-interface properties from
    /// the Preview2 base bundle plus one sibling-family bundle
    /// (wasi-nn, wasi-gfx, …) through one CLR object.
    ///
    /// <para>The transpiler's <c>HostPackageResolver</c> and the
    /// runtime's <c>WasiPreview2RuntimeScope</c> discover composite
    /// bundles by scanning the AppDomain for this attribute, picking
    /// the highest-<see cref="Priority"/> attributed type whose
    /// resolved instance is available in the DI container. This
    /// replaces the previous hardcoded cascade keyed on qualified
    /// type names — new sibling families ship a composite tagged
    /// with this attribute and no edits to the Preview2 resolver are
    /// required.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class,
        AllowMultiple = false, Inherited = false)]
    public sealed class WacsCompositeBundleAttribute : Attribute
    {
        /// <summary>Stable family identifier — used for telemetry,
        /// diagnostics, and to disambiguate two composites that
        /// share a Priority. Convention: lowercase kebab-case
        /// matching the sibling WASI proposal name
        /// (e.g. <c>"wasi-nn"</c>, <c>"wasi-gfx"</c>).</summary>
        public string Family { get; }

        /// <summary>Resolution-precedence weight. The discovery
        /// walk picks the highest priority among attributed types
        /// whose CLR type is loaded and whose DI registration is
        /// available. Ties break by Family (lexicographic) for
        /// determinism. Default 0; sibling-family composites
        /// typically use 10 to outrank any future debug / test
        /// composite registered at 0.</summary>
        public int Priority { get; set; } = 0;

        public WacsCompositeBundleAttribute(string family)
        {
            Family = family ?? throw new ArgumentNullException(
                nameof(family));
        }
    }
}
