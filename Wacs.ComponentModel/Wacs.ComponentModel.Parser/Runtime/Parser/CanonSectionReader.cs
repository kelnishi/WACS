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
    /// A single canon-section entry — describes one of the
    /// component-level functions produced by canonicalization.
    /// <c>canon lift</c> takes a core-wasm function and produces
    /// a component-level one (wrapping it with lift code for the
    /// canonical-ABI conversions). <c>canon lower</c> goes the
    /// other way. Resource ops (<c>resource.new/drop/rep</c>)
    /// generate handle-table accessors.
    /// </summary>
    public abstract class CanonEntry { }

    /// <summary>
    /// <c>canon lift</c>: lift a core function at index
    /// <see cref="CoreFuncIdx"/> into a component function with
    /// the component type at <see cref="TypeIdx"/>. Canonical
    /// options (<see cref="Options"/>) specify memory / realloc /
    /// post-return / string-encoding. The transpiler consumes
    /// these to generate the adapter IL that converts between
    /// C#-level types (<c>string</c>, <c>byte[]</c>, …) and the
    /// flat core-wasm parameter list.
    /// </summary>
    public sealed class CanonLift : CanonEntry
    {
        public uint CoreFuncIdx { get; }
        public uint TypeIdx { get; }
        public IReadOnlyList<CanonOption> Options { get; }

        public CanonLift(uint coreFuncIdx, uint typeIdx,
                         IReadOnlyList<CanonOption> options)
        {
            CoreFuncIdx = coreFuncIdx;
            TypeIdx = typeIdx;
            Options = options;
        }
    }

    /// <summary>
    /// <c>canon lower</c>: lower a component function at index
    /// <see cref="FuncIdx"/> into a core-wasm function. Used
    /// for imports — the component calls the host via the
    /// lowered core function.
    /// </summary>
    public sealed class CanonLower : CanonEntry
    {
        public uint FuncIdx { get; }
        public IReadOnlyList<CanonOption> Options { get; }

        public CanonLower(uint funcIdx, IReadOnlyList<CanonOption> options)
        {
            FuncIdx = funcIdx;
            Options = options;
        }
    }

    /// <summary>
    /// <c>canon resource.new</c> / <c>resource.drop</c> /
    /// <c>resource.rep</c> — handle-table intrinsics generated
    /// for a particular resource type.
    /// </summary>
    public sealed class CanonResourceOp : CanonEntry
    {
        public enum Kind { New, Drop, Rep }
        public Kind Op { get; }
        public uint ResourceTypeIdx { get; }

        public CanonResourceOp(Kind op, uint resourceTypeIdx)
        {
            Op = op;
            ResourceTypeIdx = resourceTypeIdx;
        }
    }

    /// <summary>
    /// A canonical-ABI option — controls how a lift / lower
    /// performs the conversion. Several are just tag bytes with
    /// no payload (<c>string-encoding</c> variants,
    /// <c>async</c>); others carry an index into the relevant
    /// core-wasm sort (<c>memory</c>, <c>realloc</c>,
    /// <c>post-return</c>, <c>callback</c>).
    /// </summary>
    public sealed class CanonOption
    {
        public enum Kind : byte
        {
            StringUtf8 = 0x00,
            StringUtf16 = 0x01,
            StringLatin1OrUtf16 = 0x02,
            Memory = 0x03,
            Realloc = 0x04,
            PostReturn = 0x05,
            Async = 0x06,
            Callback = 0x07,
        }

        public Kind OptionKind { get; }
        /// <summary>Non-null for memory / realloc / post-return
        /// / callback — the index into the relevant core-wasm
        /// sort (memory idx or func idx).</summary>
        public uint? Index { get; }

        public CanonOption(Kind kind, uint? index)
        {
            OptionKind = kind;
            Index = index;
        }
    }

    /// <summary>
    /// Decodes the component <c>canon</c> section
    /// (<see cref="ComponentSectionId.Canon"/> = 0x08). One
    /// entry per canonicalization — every lift adds to the
    /// component function space, every lower adds to the core
    /// function space, every resource op adds a handle-table
    /// intrinsic.
    /// </summary>
    public static class CanonSectionReader
    {
        public static List<CanonEntry> Decode(byte[] payload)
        {
            var reader = new ComponentBinaryReader(payload);
            var count = reader.ReadVarU32();
            var entries = new List<CanonEntry>((int)count);
            for (uint i = 0; i < count; i++)
                entries.Add(DecodeEntry(reader));
            return entries;
        }

        private static CanonEntry DecodeEntry(ComponentBinaryReader r)
        {
            var opcode = r.ReadByte();
            switch (opcode)
            {
                case 0x00:
                {
                    var sub = r.ReadByte();
                    if (sub != 0x00)
                        throw new FormatException(
                            $"Unexpected canon-lift sub-opcode 0x{sub:X2}.");
                    var f = r.ReadVarU32();
                    var opts = DecodeOptions(r);
                    var t = r.ReadVarU32();
                    return new CanonLift(f, t, opts);
                }
                case 0x01:
                {
                    var sub = r.ReadByte();
                    if (sub != 0x00)
                        throw new FormatException(
                            $"Unexpected canon-lower sub-opcode 0x{sub:X2}.");
                    var f = r.ReadVarU32();
                    var opts = DecodeOptions(r);
                    return new CanonLower(f, opts);
                }
                case 0x02:
                    return new CanonResourceOp(
                        CanonResourceOp.Kind.New, r.ReadVarU32());
                case 0x03:
                    return new CanonResourceOp(
                        CanonResourceOp.Kind.Drop, r.ReadVarU32());
                case 0x04:
                    return new CanonResourceOp(
                        CanonResourceOp.Kind.Rep, r.ReadVarU32());
                default:
                    throw new FormatException(
                        $"Unsupported canon opcode 0x{opcode:X2}. "
                        + "Async / thread intrinsics (0x05+) are a "
                        + "Phase 1b follow-up.");
            }
        }

        private static IReadOnlyList<CanonOption> DecodeOptions(
            ComponentBinaryReader r)
        {
            var count = r.ReadVarU32();
            if (count == 0) return System.Array.Empty<CanonOption>();
            var opts = new CanonOption[count];
            for (uint i = 0; i < count; i++)
                opts[i] = DecodeOption(r);
            return opts;
        }

        private static CanonOption DecodeOption(ComponentBinaryReader r)
        {
            var tag = (CanonOption.Kind)r.ReadByte();
            uint? idx = null;
            switch (tag)
            {
                case CanonOption.Kind.Memory:
                case CanonOption.Kind.Realloc:
                case CanonOption.Kind.PostReturn:
                case CanonOption.Kind.Callback:
                    idx = r.ReadVarU32();
                    break;
                case CanonOption.Kind.StringUtf8:
                case CanonOption.Kind.StringUtf16:
                case CanonOption.Kind.StringLatin1OrUtf16:
                case CanonOption.Kind.Async:
                    break;
                default:
                    throw new FormatException(
                        $"Unsupported canon option tag 0x{(byte)tag:X2}.");
            }
            return new CanonOption(tag, idx);
        }
    }
}
