// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.ComponentModel.Runtime.Parser
{
    /// <summary>
    /// Kinds an alias can produce. Matches the <c>sort</c> byte in
    /// the Component Model binary format — <c>0x00</c> is the core
    /// umbrella (next byte narrows to core-func / core-table /
    /// core-memory / …); <c>0x01..0x05</c> are the component-level
    /// spaces.
    /// </summary>
    public enum AliasSort : byte
    {
        /// <summary>Alias produces a core-space item; inspect
        /// <see cref="ComponentAliasEntry.CoreKind"/> for the
        /// variant (func/table/memory/global/module/instance/type).</summary>
        CoreSort = 0,
        /// <summary>Alias produces a component function.</summary>
        Func = 1,
        Value = 2,
        Type = 3,
        Component = 4,
        Instance = 5,
    }

    /// <summary>Variant byte for core-sort aliases — which core
    /// index space the alias populates.</summary>
    public enum CoreAliasKind : byte
    {
        Func = 0,
        Table = 1,
        Memory = 2,
        Global = 3,
        Module = 4,
        Instance = 5,
        Type = 6,
    }

    /// <summary>Aliastarget kinds. <c>InstanceExport</c> covers
    /// both component-instance (0x00) and core-instance (0x01)
    /// exports — they differ only in which index space they
    /// reference. <c>Outer</c> reaches up the lexical component
    /// chain.</summary>
    public enum AliasTargetKind : byte
    {
        ComponentInstanceExport = 0x00,
        CoreInstanceExport = 0x01,
        Outer = 0x02,
    }

    /// <summary>
    /// A single alias entry's sort + target descriptor. Surfaces
    /// the aliastarget payload so downstream consumers can
    /// resolve aliases through the composition tree (sub-component
    /// instantiation needs <see cref="InstanceIdx"/> +
    /// <see cref="ExportName"/> to bind).
    /// </summary>
    public sealed class ComponentAliasEntry
    {
        public AliasSort Sort { get; }
        /// <summary>Non-null iff <see cref="Sort"/> is
        /// <see cref="AliasSort.CoreSort"/>; selects which core
        /// space the alias populates.</summary>
        public CoreAliasKind? CoreKind { get; }
        public AliasTargetKind TargetKind { get; }
        /// <summary>For instance-export targets: which instance
        /// is the alias drawn from. <c>null</c> for outer
        /// aliases.</summary>
        public uint? InstanceIdx { get; }
        /// <summary>For instance-export targets: which named
        /// export of that instance the alias resolves to.
        /// <c>null</c> for outer aliases.</summary>
        public string? ExportName { get; }
        /// <summary>For outer aliases: how many components to
        /// climb. <c>null</c> for instance-export targets.</summary>
        public uint? OuterCount { get; }
        /// <summary>For outer aliases: index in the outer
        /// space. <c>null</c> for instance-export targets.</summary>
        public uint? OuterIdx { get; }

        public ComponentAliasEntry(AliasSort sort, CoreAliasKind? coreKind,
            AliasTargetKind targetKind, uint? instanceIdx,
            string? exportName, uint? outerCount, uint? outerIdx)
        {
            Sort = sort;
            CoreKind = coreKind;
            TargetKind = targetKind;
            InstanceIdx = instanceIdx;
            ExportName = exportName;
            OuterCount = outerCount;
            OuterIdx = outerIdx;
        }

        /// <summary>True iff this alias adds to the COMPONENT
        /// function index space. Happens when
        /// <see cref="Sort"/> is <see cref="AliasSort.Func"/>. The
        /// core-func case (<c>CoreSort + CoreFunc</c>) populates
        /// the core func space instead.</summary>
        public bool IsComponentFunc => Sort == AliasSort.Func;
    }

    /// <summary>
    /// Decodes the component <c>alias</c> section
    /// (<see cref="ComponentSectionId.Alias"/> = 0x06). Each
    /// entry is <c>sort:&lt;sort&gt; target:&lt;aliastarget&gt;</c>.
    /// The decoder needs to know which index-space each alias
    /// populates so the component-func-idx resolver can account
    /// for them (wit-component emits multi-export components
    /// with interleaved canon + alias sections that make the
    /// component-func idx space non-contiguous otherwise).
    /// </summary>
    public static class AliasSectionReader
    {
        public static List<ComponentAliasEntry> Decode(byte[] payload)
        {
            var reader = new ComponentBinaryReader(payload);
            var count = reader.ReadVarU32();
            var entries = new List<ComponentAliasEntry>((int)count);
            for (uint i = 0; i < count; i++)
                entries.Add(DecodeEntry(reader));
            return entries;
        }

        private static ComponentAliasEntry DecodeEntry(ComponentBinaryReader r)
        {
            var sortByte = r.ReadByte();
            CoreAliasKind? coreKind = null;
            AliasSort sort;
            if (sortByte == (byte)AliasSort.CoreSort)
            {
                coreKind = (CoreAliasKind)r.ReadByte();
                sort = AliasSort.CoreSort;
            }
            else
            {
                sort = (AliasSort)sortByte;
            }

            var targetTag = r.ReadByte();
            switch (targetTag)
            {
                case 0x00:    // component-instance export
                {
                    var instIdx = r.ReadVarU32();
                    var name = r.ReadName();
                    return new ComponentAliasEntry(sort, coreKind,
                        AliasTargetKind.ComponentInstanceExport,
                        instIdx, name, null, null);
                }
                case 0x01:    // core-instance export
                {
                    var instIdx = r.ReadVarU32();
                    var name = r.ReadName();
                    return new ComponentAliasEntry(sort, coreKind,
                        AliasTargetKind.CoreInstanceExport,
                        instIdx, name, null, null);
                }
                case 0x02:    // outer alias
                {
                    var outerCount = r.ReadVarU32();
                    var outerIdx = r.ReadVarU32();
                    return new ComponentAliasEntry(sort, coreKind,
                        AliasTargetKind.Outer,
                        null, null, outerCount, outerIdx);
                }
                default:
                    throw new FormatException(
                        $"Unknown aliastarget tag 0x{targetTag:X2}. "
                        + "Expected 0x00/0x01/0x02.");
            }
        }
    }
}
