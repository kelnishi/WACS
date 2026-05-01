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
    /// One arg supplied to a component instantiation. Pairs a
    /// name with a sort + index referencing the parent
    /// component's index space (e.g. "memory" → core-memory 0,
    /// or "stream" → component-instance 2). The sort byte uses
    /// the same encoding as <see cref="ComponentSort"/>.
    /// </summary>
    public sealed class ComponentInstantiateArg
    {
        public string Name { get; }
        public ComponentSort Sort { get; }
        public uint Index { get; }

        public ComponentInstantiateArg(string name, ComponentSort sort, uint index)
        {
            Name = name;
            Sort = sort;
            Index = index;
        }
    }

    /// <summary>
    /// One inline export bundled into an instance built by the
    /// 0x01 form of <c>instance-expr</c>. Each names an existing
    /// item from the surrounding component as part of the new
    /// instance's surface — the lighter alternative to wrapping
    /// items in a separate sub-component.
    /// </summary>
    public sealed class ComponentInlineExport
    {
        public string Name { get; }
        public ComponentSort Sort { get; }
        public uint Index { get; }

        public ComponentInlineExport(string name, ComponentSort sort, uint index)
        {
            Name = name;
            Sort = sort;
            Index = index;
        }
    }

    /// <summary>One component-instance entry.</summary>
    public abstract class ComponentInstanceEntry { }

    /// <summary>
    /// <c>(instantiate componentidx arg*)</c>: produce a fresh
    /// instance of the component at <see cref="ComponentIdx"/>,
    /// supplying the named <see cref="Args"/> as imports.
    /// </summary>
    public sealed class InstantiateComponent : ComponentInstanceEntry
    {
        public uint ComponentIdx { get; }
        public IReadOnlyList<ComponentInstantiateArg> Args { get; }

        public InstantiateComponent(uint componentIdx,
            IReadOnlyList<ComponentInstantiateArg> args)
        {
            ComponentIdx = componentIdx;
            Args = args;
        }
    }

    /// <summary>
    /// <c>(instantiate (export*))</c>: produce a fresh instance
    /// whose surface is the supplied bundle of inline exports
    /// drawn from the surrounding component's index spaces.
    /// </summary>
    public sealed class InstantiateInline : ComponentInstanceEntry
    {
        public IReadOnlyList<ComponentInlineExport> Exports { get; }

        public InstantiateInline(IReadOnlyList<ComponentInlineExport> exports)
        {
            Exports = exports;
        }
    }

    /// <summary>
    /// Decodes the component <c>instance</c> section
    /// (<see cref="ComponentSectionId.Instance"/> = 0x05). Pairs
    /// with <see cref="AliasSectionReader"/>: instance entries
    /// allocate component-instance slots; alias entries reach
    /// into those instances' export tables to populate the
    /// surrounding component's index spaces.
    /// </summary>
    public static class InstanceSectionReader
    {
        public static List<ComponentInstanceEntry> Decode(byte[] payload)
        {
            var reader = new ComponentBinaryReader(payload);
            var count = reader.ReadVarU32();
            var entries = new List<ComponentInstanceEntry>((int)count);
            for (uint i = 0; i < count; i++)
                entries.Add(DecodeEntry(reader));
            return entries;
        }

        private static ComponentInstanceEntry DecodeEntry(ComponentBinaryReader r)
        {
            var tag = r.ReadByte();
            switch (tag)
            {
                case 0x00:
                {
                    var componentIdx = r.ReadVarU32();
                    var argCount = r.ReadVarU32();
                    var args = new ComponentInstantiateArg[argCount];
                    for (uint i = 0; i < argCount; i++)
                    {
                        var name = r.ReadName();
                        var sort = ReadSort(r);
                        var idx = r.ReadVarU32();
                        args[i] = new ComponentInstantiateArg(name, sort, idx);
                    }
                    return new InstantiateComponent(componentIdx, args);
                }
                case 0x01:
                {
                    var exportCount = r.ReadVarU32();
                    var exports = new ComponentInlineExport[exportCount];
                    for (uint i = 0; i < exportCount; i++)
                    {
                        var name = r.ReadName();
                        var sort = ReadSort(r);
                        var idx = r.ReadVarU32();
                        exports[i] = new ComponentInlineExport(name, sort, idx);
                    }
                    return new InstantiateInline(exports);
                }
                default:
                    throw new FormatException(
                        $"Unknown instance-expr tag 0x{tag:X2}. "
                        + "Expected 0x00 (instantiate component) or "
                        + "0x01 (inline-export bundle).");
            }
        }

        private static ComponentSort ReadSort(ComponentBinaryReader r)
        {
            var b = r.ReadByte();
            // Core sort (0x00) has a follow-up sub-tag byte; the
            // component-side sorts are 0x01..0x05 directly. We
            // only surface component-side here since args/inline-
            // exports for component-instance instantiation never
            // reach into the core sub-space (that's
            // core-instance's job — section 2, distinct reader).
            if (b == 0x00)
            {
                // Skip the core-sort sub-tag byte; expose as a
                // sentinel "core" via sort=Func with an indication
                // would require expanding the ComponentSort enum.
                // For now: throw, since component-instance args
                // typically don't use core sort.
                _ = r.ReadByte();
                throw new NotSupportedException(
                    "Core-sort sortidx in component instance args "
                    + "is a follow-up — current fixtures don't "
                    + "exercise this path.");
            }
            return (ComponentSort)b;
        }
    }
}
