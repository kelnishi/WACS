// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.Linq;
using Wacs.ComponentModel.Runtime.Parser;

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>
    /// Parsed Component Model binary. Mirrors
    /// <see cref="Wacs.Core.Modules.Module"/> on the core-wasm
    /// side — the top-level container that downstream consumers
    /// (transpiler, interpreter) feed into.
    ///
    /// <para>Phase 1b v0: holds the raw section list from the
    /// binary parser. Structured per-section decoders land
    /// incrementally — core-module section first (the embedded
    /// core wasm that the transpiler compiles), then type / import
    /// / export / canon for the component-level contract, then
    /// instance / alias / start for multi-component composition.</para>
    /// </summary>
    public sealed class ComponentModule
    {
        /// <summary>Raw section blocks in file order. Custom
        /// sections (producers, name, <c>wit-component</c> metadata)
        /// are retained in full; structured sections get parsed
        /// into shape-specific fields on demand.</summary>
        public IReadOnlyList<RawComponentSection> RawSections { get; }

        public ComponentModule(IReadOnlyList<RawComponentSection> rawSections)
        {
            RawSections = rawSections;
        }

        /// <summary>Filter raw sections by tag. Useful for quick
        /// queries like "which core modules did this component
        /// embed?" without stepping through the full list.</summary>
        public IEnumerable<RawComponentSection> SectionsOf(
            ComponentSectionId id)
        {
            foreach (var s in RawSections)
                if (s.Id == id) yield return s;
        }

        /// <summary>Convenience: count of core-module sections
        /// (each is a complete embedded core wasm binary). The
        /// transpiler lowers each through <c>ModuleTranspiler</c>;
        /// hello-world-style components typically have exactly
        /// one.</summary>
        public int CoreModuleCount =>
            SectionsOf(ComponentSectionId.CoreModule).Count();
    }
}
