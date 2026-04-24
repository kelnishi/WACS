// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;

namespace Wacs.ComponentModel.Runtime.Parser
{
    /// <summary>
    /// Discriminator for a component-level export's kind —
    /// matches the <c>sort</c> byte in the binary format. Points
    /// at one of the component's index spaces (function,
    /// component, instance, type, core-module, value).
    /// </summary>
    public enum ComponentSort : byte
    {
        /// <summary>Export is a core-sort (module / instance /
        /// type / func / global / table / memory). The next byte
        /// gives the core-sort variant; we don't decode further
        /// yet because real-world components don't export core
        /// sorts directly at the component level.</summary>
        CoreSort = 0,
        Func = 1,
        Value = 2,
        Type = 3,
        Component = 4,
        Instance = 5,
    }

    /// <summary>
    /// One entry in the component's export section: the
    /// publicly-visible name plus which component index-space
    /// slot it exposes.
    /// </summary>
    public sealed class ComponentExportEntry
    {
        /// <summary>Plain identifier or interface-scoped export
        /// name (<c>wasi:cli/run@0.2.3</c> etc.). Carries the
        /// full WIT-level string verbatim.</summary>
        public string Name { get; }

        /// <summary>The name's external-kind prefix byte —
        /// <c>0x00</c> plain, <c>0x01</c> interface (versioned),
        /// <c>0x02</c> — …. Preserved for fidelity; consumers
        /// that only care about plain names can ignore.</summary>
        public byte NameKind { get; }

        /// <summary>Which index-space the export comes from
        /// (Func / Type / Component / Instance / Value).</summary>
        public ComponentSort Sort { get; }

        /// <summary>Index into the component's sort-specific
        /// index space. For <see cref="ComponentSort.Func"/>,
        /// this is the component-function index (which a canon
        /// lift ties back to a core-module function).</summary>
        public uint Index { get; }

        /// <summary>When the export carries an explicit type
        /// ascription (common for world-level exports —
        /// <c>export foo: func(...) -> ...</c>), this is the
        /// type index. Null when absent.</summary>
        public uint? TypeAscription { get; }

        public ComponentExportEntry(string name, byte nameKind,
                                    ComponentSort sort, uint index,
                                    uint? typeAscription)
        {
            Name = name;
            NameKind = nameKind;
            Sort = sort;
            Index = index;
            TypeAscription = typeAscription;
        }
    }

    /// <summary>
    /// Decodes the component <c>export</c> section
    /// (<see cref="ComponentSectionId.Export"/> = 0x0B). Format:
    /// <code>
    /// exports ::= vec(export)
    /// export  ::= namespec:byte name:&lt;name&gt;
    ///             sort:byte idx:varuint32
    ///             type?:byte type-idx:varuint32
    /// </code>
    /// The <c>namespec</c> byte marks the name flavor
    /// (0x00 plain, 0x01 interface-scoped) and the optional
    /// type ascription is a 0x00/0x01 presence byte followed by
    /// the type index when present.
    /// </summary>
    public static class ExportSectionReader
    {
        /// <summary>Decode a raw export-section payload into
        /// typed entries. The payload is the post-size bytes
        /// the binary parser captured on
        /// <see cref="RawComponentSection"/>.</summary>
        public static List<ComponentExportEntry> Decode(byte[] payload)
        {
            var reader = new ComponentBinaryReader(payload);
            var count = reader.ReadVarU32();
            var entries = new List<ComponentExportEntry>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var nameKind = reader.ReadByte();
                var name = reader.ReadName();
                var sort = DecodeSort(reader);
                var index = reader.ReadVarU32();
                uint? typeAscription = null;
                if (!reader.AtEnd)
                {
                    var hasType = reader.ReadByte();
                    if (hasType == 0x01)
                        typeAscription = reader.ReadVarU32();
                    // hasType == 0x00 means no ascription, no
                    // further bytes to read for this entry.
                }
                entries.Add(new ComponentExportEntry(
                    name, nameKind, sort, index, typeAscription));
            }
            return entries;
        }

        /// <summary>Decode the <c>sort</c> byte (optionally
        /// consuming an extra byte for the core-sort variant).
        /// We don't structurally distinguish core-sort variants
        /// yet — real-world components export at the component
        /// level, not the core level.</summary>
        private static ComponentSort DecodeSort(ComponentBinaryReader reader)
        {
            var tag = reader.ReadByte();
            if (tag == (byte)ComponentSort.CoreSort)
            {
                // Core-sort variant byte follows; discard.
                reader.ReadByte();
                return ComponentSort.CoreSort;
            }
            return (ComponentSort)tag;
        }
    }
}
