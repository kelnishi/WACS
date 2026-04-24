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
    /// One entry in the component's import section. Like exports,
    /// these carry a name + sort (which index space the import
    /// populates) + index/typebounds. Used by wit-component as a
    /// backdoor to introduce named types at the outer component
    /// level (e.g. <c>(import "direction" (type (eq 0)))</c>
    /// names a structural enum so canon-lift function signatures
    /// can reference it by name).
    /// </summary>
    public sealed class ComponentImportEntry
    {
        public string Name { get; }
        public byte NameKind { get; }
        public ComponentSort Sort { get; }

        /// <summary>
        /// For sort=Func/Instance/Component/Module: the index in
        /// the relevant index space. For sort=Type: the bounded
        /// typeIdx (encoded as <c>TypeBounds.Eq(idx)</c>) — the
        /// import allocates a NEW type-space slot whose body
        /// equals the indicated existing type.
        /// </summary>
        public uint Index { get; }

        /// <summary>True iff sort=Type and the bounds are
        /// <c>SubResource</c> (a fresh resource type rather than
        /// an Eq alias). When true, <see cref="Index"/> is unused.</summary>
        public bool IsSubResource { get; }

        public ComponentImportEntry(string name, byte nameKind,
                                    ComponentSort sort, uint index,
                                    bool isSubResource)
        {
            Name = name;
            NameKind = nameKind;
            Sort = sort;
            Index = index;
            IsSubResource = isSubResource;
        }
    }

    /// <summary>
    /// Decodes the component <c>import</c> section
    /// (<see cref="ComponentSectionId.Import"/> = 0x0A). Format
    /// mirrors the export section's external-kind grammar — name
    /// prefix byte (<c>0x00</c> plain), name string, sort + index
    /// (with <c>TypeBounds</c> sub-encoding for Type-kind imports).
    /// </summary>
    public static class ImportSectionReader
    {
        public static List<ComponentImportEntry> Decode(byte[] payload)
        {
            var reader = new ComponentBinaryReader(payload);
            var count = reader.ReadVarU32();
            var entries = new List<ComponentImportEntry>((int)count);
            for (uint i = 0; i < count; i++)
                entries.Add(DecodeEntry(reader));
            return entries;
        }

        private static ComponentImportEntry DecodeEntry(ComponentBinaryReader r)
        {
            var nameKind = r.ReadByte();
            var name = r.ReadName();
            var sort = DecodeSort(r);
            uint idx = 0;
            bool subResource = false;
            switch (sort)
            {
                case ComponentSort.Type:
                {
                    // TypeBounds: 0x00 Eq(idx) | 0x01 SubResource
                    var bound = r.ReadByte();
                    if (bound == 0x00)
                        idx = r.ReadVarU32();
                    else if (bound == 0x01)
                        subResource = true;
                    else
                        throw new System.FormatException(
                            $"Unknown TypeBounds tag 0x{bound:X2} in import.");
                    break;
                }
                case ComponentSort.Func:
                case ComponentSort.Instance:
                case ComponentSort.Component:
                    idx = r.ReadVarU32();
                    break;
                case ComponentSort.CoreSort:
                    idx = r.ReadVarU32();
                    break;
                case ComponentSort.Value:
                    // Value imports follow a ComponentValType —
                    // not exercised by Phase 1b fixtures.
                    break;
            }
            return new ComponentImportEntry(name, nameKind, sort, idx,
                                            subResource);
        }

        private static ComponentSort DecodeSort(ComponentBinaryReader r)
        {
            var tag = r.ReadByte();
            if (tag == (byte)ComponentSort.CoreSort)
            {
                r.ReadByte();   // core variant byte; discarded
                return ComponentSort.CoreSort;
            }
            return (ComponentSort)tag;
        }
    }
}
