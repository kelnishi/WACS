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

        /// <summary>
        /// Raw bytes of each embedded core-module section in
        /// file order. Each entry is a complete core wasm binary
        /// (starts with <c>\0asm</c> magic + version 1) that can
        /// be fed directly to <c>Wacs.Core.Modules.Module.ParseWasm</c>
        /// for transpilation / interpretation.
        /// </summary>
        public IEnumerable<byte[]> CoreModuleBinaries
        {
            get
            {
                foreach (var s in RawSections)
                    if (s.Id == ComponentSectionId.CoreModule)
                        yield return s.Payload;
            }
        }

        /// <summary>
        /// Decoded custom sections, parsed on demand. Useful for
        /// inspecting <c>component-type:*</c> (the embedded WIT
        /// text), <c>producers</c>, and user-defined annotations.
        /// </summary>
        public IEnumerable<Parser.CustomSection> CustomSections
        {
            get
            {
                foreach (var s in RawSections)
                    if (s.Id == Parser.ComponentSectionId.Custom)
                        yield return Parser.CustomSection.FromRaw(s);
            }
        }

        /// <summary>
        /// Decoded component-level exports — <c>(name, sort,
        /// index)</c> triples from the export section. The
        /// transpiler / interpreter matches these against the
        /// canon-section entries to know which core functions
        /// each export lifts.
        /// </summary>
        public IReadOnlyList<Parser.ComponentExportEntry> Exports
        {
            get
            {
                if (_exports != null) return _exports;
                var list = new List<Parser.ComponentExportEntry>();
                foreach (var s in RawSections)
                    if (s.Id == Parser.ComponentSectionId.Export)
                        list.AddRange(Parser.ExportSectionReader.Decode(s.Payload));
                _exports = list;
                return list;
            }
        }
        private IReadOnlyList<Parser.ComponentExportEntry>? _exports;

        /// <summary>
        /// Decoded canon-section entries — lifts, lowers, and
        /// resource-op intrinsics. The component's function
        /// index space is populated by these entries; matching
        /// them against <see cref="Exports"/> tells you which
        /// core function each export's lift resolves to.
        /// </summary>
        public IReadOnlyList<Parser.CanonEntry> Canons
        {
            get
            {
                if (_canons != null) return _canons;
                var list = new List<Parser.CanonEntry>();
                foreach (var s in RawSections)
                    if (s.Id == Parser.ComponentSectionId.Canon)
                        list.AddRange(Parser.CanonSectionReader.Decode(s.Payload));
                _canons = list;
                return list;
            }
        }
        private IReadOnlyList<Parser.CanonEntry>? _canons;
    }
}
