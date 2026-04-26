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

        /// <summary>Count of nested component sections — each
        /// holds another full <c>.component.wasm</c> binary. A
        /// non-zero count means this component composes other
        /// components via the instance + alias sections; the
        /// transpiler / interpreter need to recursively parse
        /// each before instantiating the outer.</summary>
        public int NestedComponentCount =>
            SectionsOf(ComponentSectionId.Component).Count();

        /// <summary>Raw bytes of each nested component section
        /// in file order. Companion to
        /// <see cref="CoreModuleBinaries"/> for the
        /// component-of-components case. Each payload is a
        /// complete component binary (preamble +
        /// version/layer=0x000d + sections) that can be
        /// recursively fed back through
        /// <see cref="Parser.ComponentBinaryParser.Parse"/>.
        /// </summary>
        public IEnumerable<byte[]> NestedComponentBinaries
        {
            get
            {
                foreach (var s in RawSections)
                    if (s.Id == ComponentSectionId.Component)
                        yield return s.Payload;
            }
        }

        /// <summary>Decode each nested component eagerly. Useful
        /// when downstream consumers need to walk the full
        /// composition tree (e.g. for resolving aliases that
        /// target sub-component exports). Each entry is itself a
        /// fully-functional <see cref="ComponentModule"/>.
        /// </summary>
        public IReadOnlyList<ComponentModule> NestedComponents
        {
            get
            {
                if (_nestedComponents != null) return _nestedComponents;
                var list = new List<ComponentModule>();
                foreach (var bytes in NestedComponentBinaries)
                {
                    using var ms = new System.IO.MemoryStream(bytes);
                    list.Add(Parser.ComponentBinaryParser.Parse(ms));
                }
                _nestedComponents = list;
                return list;
            }
        }
        private IReadOnlyList<ComponentModule>? _nestedComponents;

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

        /// <summary>
        /// Decoded deftype table — the component's type space.
        /// <see cref="Parser.CanonLift.TypeIdx"/> /
        /// <see cref="Parser.ComponentExportEntry.TypeAscription"/>
        /// index into this list.
        /// </summary>
        public IReadOnlyList<Parser.DefTypeEntry> Types
        {
            get
            {
                if (_types != null) return _types;
                // List positions align with the binary's
                // component-type index space — the index space
                // grows by Type-section entries AND by component-
                // level imports/exports of sort=Type (wit-component
                // uses these to introduce named type aliases).
                // Slots whose index corresponds to a non-Type-
                // section allocation get an opaque sentinel so
                // consumers indexing by type-idx don't go OOB.
                var map = ComponentTypeIndexToDef;
                var len = 0;
                foreach (var k in map.Keys)
                    if ((int)k + 1 > len) len = (int)k + 1;
                // The type-space "high-water mark" can also include
                // import/export type slots whose target didn't
                // resolve (rare). Re-walk RawSections to ensure
                // every allocation is counted.
                uint allocs = 0;
                foreach (var s in RawSections)
                {
                    switch (s.Id)
                    {
                        case Parser.ComponentSectionId.Type:
                            allocs += (uint)Parser.TypeSectionReader.Decode(s.Payload).Count;
                            break;
                        case Parser.ComponentSectionId.Import:
                            foreach (var e in Parser.ImportSectionReader.Decode(s.Payload))
                                if (e.Sort == Parser.ComponentSort.Type) allocs++;
                            break;
                        case Parser.ComponentSectionId.Export:
                            foreach (var e in Parser.ExportSectionReader.Decode(s.Payload))
                                if (e.Sort == Parser.ComponentSort.Type) allocs++;
                            break;
                    }
                }
                if ((int)allocs > len) len = (int)allocs;

                var list = new Parser.DefTypeEntry[len];
                var sentinel = new Parser.RawDefType(
                    0xFF, System.Array.Empty<byte>());
                for (int i = 0; i < len; i++)
                {
                    list[i] = map.TryGetValue((uint)i, out var entry)
                        ? entry : sentinel;
                }
                _types = list;
                return list;
            }
        }
        private IReadOnlyList<Parser.DefTypeEntry>? _types;

        /// <summary>
        /// Map from component-func index to the
        /// <see cref="Parser.CanonLift"/> that populated it.
        /// Absent keys correspond to slots allocated by Func-sort
        /// aliases or Func-kind exports (wasm-encoder bumps the
        /// component-func counter on every export call — see
        /// <c>ComponentBuilder::export</c> at
        /// <c>wasm-encoder</c>).
        ///
        /// <para>Why this exists: wit-component produces
        /// multi-export components with interleaved canon + alias
        /// + export sections. A flat "index into <see cref="Canons"/>
        /// by canon-list position" lookup breaks because the
        /// component-func index space grows between canons via
        /// each preceding export. This resolver walks
        /// <see cref="RawSections"/> in file order and builds the
        /// canonical map.</para>
        /// </summary>
        /// <summary>
        /// Map from component-type index to the
        /// <see cref="Parser.DefTypeEntry"/> that lives at that
        /// slot. Walks <see cref="RawSections"/> in file order to
        /// honor wit-component's convention of introducing named
        /// type aliases via component-level type imports
        /// (<c>(import "direction" (type (eq 0)))</c>) — without
        /// this resolver, type-section indices and func-signature
        /// type-refs misalign on any non-trivial fixture.
        ///
        /// <para>Each Type-section entry takes its own slot. Each
        /// component-level import / export of sort=Type also takes
        /// a slot, with the body resolving to the bounded source
        /// type. Resource sub-bounds (<c>SubResource</c>) leave
        /// the slot empty (the type is opaque).</para>
        /// </summary>
        public IReadOnlyDictionary<uint, Parser.DefTypeEntry> ComponentTypeIndexToDef
        {
            get
            {
                if (_componentTypeIndex != null) return _componentTypeIndex;
                var map = new Dictionary<uint, Parser.DefTypeEntry>();
                var aliases = new Dictionary<uint, uint>();
                uint typeIdx = 0;
                foreach (var s in RawSections)
                {
                    switch (s.Id)
                    {
                        case Parser.ComponentSectionId.Type:
                        {
                            var entries = Parser.TypeSectionReader.Decode(s.Payload);
                            foreach (var e in entries)
                            {
                                map[typeIdx] = e;
                                typeIdx++;
                            }
                            break;
                        }
                        case Parser.ComponentSectionId.Import:
                        {
                            var entries = Parser.ImportSectionReader.Decode(s.Payload);
                            foreach (var e in entries)
                            {
                                if (e.Sort != Parser.ComponentSort.Type) continue;
                                if (e.IsSubResource)
                                    map[typeIdx] = new Parser.ComponentResourceType(null);
                                else
                                    aliases[typeIdx] = e.Index;
                                typeIdx++;
                            }
                            break;
                        }
                        case Parser.ComponentSectionId.Export:
                        {
                            var entries = Parser.ExportSectionReader.Decode(s.Payload);
                            foreach (var e in entries)
                            {
                                if (e.Sort != Parser.ComponentSort.Type) continue;
                                aliases[typeIdx] = e.Index;
                                typeIdx++;
                            }
                            break;
                        }
                    }
                }
                // Resolve alias chains.
                foreach (var kv in aliases)
                {
                    var resolved = ResolveAlias(kv.Key, aliases, map);
                    if (resolved != null) map[kv.Key] = resolved;
                }
                _componentTypeIndex = map;
                return map;
            }
        }
        private IReadOnlyDictionary<uint, Parser.DefTypeEntry>? _componentTypeIndex;

        private static Parser.DefTypeEntry? ResolveAlias(
            uint idx,
            Dictionary<uint, uint> aliases,
            Dictionary<uint, Parser.DefTypeEntry> bodies)
        {
            var visited = new HashSet<uint>();
            while (true)
            {
                if (!visited.Add(idx)) return null;   // cycle
                if (bodies.TryGetValue(idx, out var body)) return body;
                if (!aliases.TryGetValue(idx, out var next)) return null;
                idx = next;
            }
        }

        public IReadOnlyDictionary<uint, Parser.CanonLift> ComponentFuncToCanon
        {
            get
            {
                if (_componentFuncToCanon != null) return _componentFuncToCanon;
                var map = new Dictionary<uint, Parser.CanonLift>();
                uint funcIdx = 0;
                foreach (var s in RawSections)
                {
                    switch (s.Id)
                    {
                        case Parser.ComponentSectionId.Canon:
                        {
                            var entries = Parser.CanonSectionReader.Decode(s.Payload);
                            foreach (var e in entries)
                            {
                                if (e is Parser.CanonLift lift)
                                {
                                    map[funcIdx] = lift;
                                    funcIdx++;
                                }
                                // canon lower adds to core-func
                                // space (not component); canon
                                // resource.* adds to core-func
                                // space as well. Neither
                                // increments funcIdx here.
                            }
                            break;
                        }
                        case Parser.ComponentSectionId.Alias:
                        {
                            var entries = Parser.AliasSectionReader.Decode(s.Payload);
                            foreach (var e in entries)
                                if (e.IsComponentFunc)
                                    funcIdx++;   // anonymous slot
                            break;
                        }
                        case Parser.ComponentSectionId.Export:
                        {
                            // wasm-encoder increments the
                            // component-func counter for every
                            // Func-kind export, allocating a new
                            // slot that shadows the source item.
                            // This slot is anonymous from our
                            // resolver's perspective — the
                            // canon behind the exported func is
                            // already in the map keyed by the
                            // export's `idx` field.
                            var entries = Parser.ExportSectionReader.Decode(s.Payload);
                            foreach (var e in entries)
                                if (e.Sort == Parser.ComponentSort.Func)
                                    funcIdx++;
                            break;
                        }
                        case Parser.ComponentSectionId.Import:
                        {
                            // Func imports also grow the
                            // component-func space. Deferred —
                            // no fixtures exercise imports yet,
                            // and decoding imports structurally
                            // needs an ImportSectionReader.
                            // Revisit once hello-world lands.
                            break;
                        }
                    }
                }
                _componentFuncToCanon = map;
                return map;
            }
        }
        private IReadOnlyDictionary<uint, Parser.CanonLift>? _componentFuncToCanon;
    }
}
