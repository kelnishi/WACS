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
    /// Marker on a <c>partial class</c> that asks the
    /// <c>CompositeBundleGenerator</c> to fill in the
    /// forwarding-properties body of a composite bundle.
    /// Replaces the hand-written
    /// <c>WasiPreview2GfxBundle</c> / <c>WasiPreview2NNBundle</c>
    /// boilerplate (each ~90 lines of forwarders) — annotate a
    /// short partial with this attribute and the source generator
    /// emits the rest at compile time.
    ///
    /// <para>The generated partial:</para>
    /// <list type="number">
    /// <item>Stamps the class with
    /// <see cref="WacsCompositeBundleAttribute"/> (using
    /// <see cref="Family"/> + <see cref="Priority"/>) so the
    /// runtime's discovery walk finds it.</item>
    /// <item>Adds private readonly fields for the
    /// <see cref="Base"/> and <see cref="Sibling"/> bundle
    /// instances.</item>
    /// <item>Emits a constructor that takes both bundles,
    /// null-checks them, and stashes them.</item>
    /// <item>Emits a forwarding property per public read-only
    /// property on each underlying bundle (the composite
    /// satisfies every binding either bundle contributes through
    /// one CLR object).</item>
    /// <item>Emits direct accessors
    /// <see cref="BaseAccessor"/> + <see cref="SiblingAccessor"/>
    /// so reflective consumers (ComponentMainHost) can unwrap to
    /// either side when needed.</item>
    /// </list>
    ///
    /// <para>v1 phase 1 deferred item 1h. Combinatorial explosion
    /// solved: a 3-way Preview2 + NN + GFX composite is one
    /// partial-class declaration away.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class,
        AllowMultiple = false, Inherited = false)]
    public sealed class WacsCompositeBundleGenAttribute : Attribute
    {
        /// <summary>The base bundle (typically
        /// <c>WasiPreview2Bundle</c>) the composite forwards.
        /// Source-gen reads every public read-only property
        /// here and emits a forwarder.</summary>
        public Type Base { get; }

        /// <summary>The sibling-family bundle (wasi-gfx /
        /// wasi-nn / future) the composite forwards. Same
        /// forwarding rules as <see cref="Base"/>.</summary>
        public Type Sibling { get; }

        /// <summary>The <c>family</c> value that lands on the
        /// generated <see cref="WacsCompositeBundleAttribute"/>.
        /// Convention: lowercase kebab-case matching the sibling
        /// WASI proposal name (e.g. <c>"wasi-gfx"</c>).</summary>
        public string Family { get; set; } = "";

        /// <summary>The <c>priority</c> value that lands on the
        /// generated <see cref="WacsCompositeBundleAttribute"/>.
        /// Defaults to 10 — same precedence the hand-written
        /// composites used in v0.</summary>
        public int Priority { get; set; } = 10;

        /// <summary>Public property name for the direct base
        /// accessor (e.g. <c>"Preview2"</c>). Lets reflective
        /// consumers fetch the base bundle from the composite
        /// without going through forwarders.</summary>
        public string BaseAccessor { get; set; } = "Base";

        /// <summary>Public property name for the direct sibling
        /// accessor (e.g. <c>"Gfx"</c>, <c>"Nn"</c>).</summary>
        public string SiblingAccessor { get; set; } = "Sibling";

        public WacsCompositeBundleGenAttribute(
            Type baseType, Type siblingType)
        {
            Base = baseType ?? throw new ArgumentNullException(
                nameof(baseType));
            Sibling = siblingType ?? throw new ArgumentNullException(
                nameof(siblingType));
        }
    }
}
