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
    /// Per the Component Model binary format, core-sort tags
    /// distinguish the kind of item in a core-inline-export
    /// bundle. (Note: the
    /// <see cref="AliasSectionReader.CoreAliasKind"/> enum
    /// covers a similar space for core-sort aliases — both
    /// derive from Binary.md's <c>core-sort</c> production but
    /// the values differ slightly.)
    /// </summary>
    public enum CoreSort : byte
    {
        Func = 0x00,
        Table = 0x01,
        Memory = 0x02,
        Global = 0x03,
        Type = 0x10,
        Module = 0x11,
        Instance = 0x12,
    }

    /// <summary>One core-instance entry in section id=2.</summary>
    public abstract class CoreInstanceEntry { }

    /// <summary>
    /// <c>(instantiate core-moduleidx arg*)</c>: produce a
    /// fresh core-instance by instantiating
    /// <see cref="ModuleIdx"/> with the named
    /// <see cref="Args"/> as imports. Each arg references an
    /// existing core-instance whose exports satisfy a named
    /// import-module of the inner module.
    /// </summary>
    public sealed class InstantiateCoreModule : CoreInstanceEntry
    {
        public uint ModuleIdx { get; }
        public IReadOnlyList<CoreInstantiateArg> Args { get; }

        public InstantiateCoreModule(uint moduleIdx,
            IReadOnlyList<CoreInstantiateArg> args)
        {
            ModuleIdx = moduleIdx;
            Args = args;
        }
    }

    /// <summary>One arg supplied to a core-module instantiation
    /// — pairs an import-module name with the core-instance
    /// whose exports satisfy that module's imports. The instance
    /// reference is the only allowed sort here per the spec.</summary>
    public sealed class CoreInstantiateArg
    {
        public string Name { get; }
        public uint InstanceIdx { get; }

        public CoreInstantiateArg(string name, uint instanceIdx)
        {
            Name = name;
            InstanceIdx = instanceIdx;
        }
    }

    /// <summary>
    /// <c>(instantiate (export*))</c>: produce a virtual
    /// core-instance whose surface is the supplied bundle of
    /// inline exports, each drawn from a core-space slot in
    /// the surrounding component (canon-lowered core-funcs are
    /// the typical source).
    /// </summary>
    public sealed class InstantiateCoreInline : CoreInstanceEntry
    {
        public IReadOnlyList<CoreInlineExport> Exports { get; }

        public InstantiateCoreInline(IReadOnlyList<CoreInlineExport> exports)
        {
            Exports = exports;
        }
    }

    /// <summary>One inline export — names an item in the
    /// surrounding component's core space under a fresh name
    /// the new instance will expose.</summary>
    public sealed class CoreInlineExport
    {
        public string Name { get; }
        public CoreSort Sort { get; }
        public uint Index { get; }

        public CoreInlineExport(string name, CoreSort sort, uint index)
        {
            Name = name;
            Sort = sort;
            Index = index;
        }
    }

    /// <summary>
    /// Decodes the <c>core-instance</c> section
    /// (<see cref="ComponentSectionId.CoreInstance"/> = 0x02).
    /// Pairs with the canon section + core-alias entries — the
    /// instantiation order in this section drives the actual
    /// composition of the component's core-wasm pieces.
    /// </summary>
    public static class CoreInstanceSectionReader
    {
        public static List<CoreInstanceEntry> Decode(byte[] payload)
        {
            var reader = new ComponentBinaryReader(payload);
            var count = reader.ReadVarU32();
            var entries = new List<CoreInstanceEntry>((int)count);
            for (uint i = 0; i < count; i++)
                entries.Add(DecodeEntry(reader));
            return entries;
        }

        private static CoreInstanceEntry DecodeEntry(ComponentBinaryReader r)
        {
            var tag = r.ReadByte();
            switch (tag)
            {
                case 0x00:
                {
                    var moduleIdx = r.ReadVarU32();
                    var argCount = r.ReadVarU32();
                    var args = new CoreInstantiateArg[argCount];
                    for (uint i = 0; i < argCount; i++)
                    {
                        var name = r.ReadName();
                        // The arg's sort byte is fixed at 0x12
                        // (instance) per Binary.md — every
                        // core-instantiate-arg references an
                        // existing core-instance.
                        var sortByte = r.ReadByte();
                        if (sortByte != 0x12)
                            throw new FormatException(
                                $"Unexpected sort 0x{sortByte:X2} "
                                + "in core-instantiate-arg; only "
                                + "0x12 (instance) is permitted.");
                        var instIdx = r.ReadVarU32();
                        args[i] = new CoreInstantiateArg(name, instIdx);
                    }
                    return new InstantiateCoreModule(moduleIdx, args);
                }
                case 0x01:
                {
                    var exportCount = r.ReadVarU32();
                    var exports = new CoreInlineExport[exportCount];
                    for (uint i = 0; i < exportCount; i++)
                    {
                        var name = r.ReadName();
                        var sort = (CoreSort)r.ReadByte();
                        var idx = r.ReadVarU32();
                        exports[i] = new CoreInlineExport(name, sort, idx);
                    }
                    return new InstantiateCoreInline(exports);
                }
                default:
                    throw new FormatException(
                        $"Unknown core-instance-expr tag 0x{tag:X2}. "
                        + "Expected 0x00 (instantiate module) or "
                        + "0x01 (inline-export bundle).");
            }
        }
    }
}
