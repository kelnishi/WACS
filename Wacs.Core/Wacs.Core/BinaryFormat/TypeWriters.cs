// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.IO;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;

namespace Wacs.Core.Bin
{
    /// <summary>
    /// Binary encoders for the value-shaped types that appear inside the
    /// wasm sections: <see cref="Limits"/>, <see cref="GlobalType"/>,
    /// <see cref="TableType"/>, <see cref="MemoryType"/>,
    /// <see cref="TagType"/>, <see cref="FunctionType"/>,
    /// <see cref="ResultType"/>, <see cref="FieldType"/>, plus the GC
    /// composite/recursive group shape.
    ///
    /// <para>Each method mirrors the equivalent <c>Parse(BinaryReader)</c>
    /// on the target type so binary round-trip is byte-identical.</para>
    /// </summary>
    internal static class TypeWriters
    {
        // ---- Limits / shared building blocks ---------------------------

        public static void WriteLimits(BinaryWriter w, Limits limits)
        {
            // Match the LimitsFlag bit layout exactly. The byte carries:
            //  bit 0 = has-max, bit 1 = shared (requires has-max),
            //  bit 2 = 64-bit address space.
            byte flags = 0;
            if (limits.AddressType == AddrType.I64) flags |= (byte)LimitsFlag.Is64Bit;
            if (limits.Maximum.HasValue)            flags |= (byte)LimitsFlag.HasMax;
            if (limits.Shared)                      flags |= (byte)LimitsFlag.IsShared;
            w.Write(flags);

            if (limits.AddressType == AddrType.I64)
            {
                w.WriteLeb128_u64((ulong)limits.Minimum);
                if (limits.Maximum.HasValue)
                    w.WriteLeb128_u64((ulong)limits.Maximum.Value);
            }
            else
            {
                w.WriteLeb128_u32((uint)limits.Minimum);
                if (limits.Maximum.HasValue)
                    w.WriteLeb128_u32((uint)limits.Maximum.Value);
            }
        }

        // ---- Global / Memory / Tag / Table -----------------------------

        public static void WriteGlobalType(BinaryWriter w, GlobalType g)
        {
            ValTypeWriter.WriteValType(w, g.ContentType, parseStorageType: false);
            // The shared-everything-aware byte mirrors MutabilityParser.
            // Bit 0 = mutable, bit 1 = shared, bit 2 = thread_local.
            byte flags = 0;
            if (g.Mutability == Mutability.Mutable) flags |= 0x01;
            if (g.Shared)                            flags |= 0x02;
            if (g.ThreadLocal)                       flags |= 0x04;
            w.Write(flags);
        }

        public static void WriteMemoryType(BinaryWriter w, MemoryType m) =>
            WriteLimits(w, m.Limits);

        public static void WriteTagType(BinaryWriter w, TagType t)
        {
            w.Write((byte)t.Attribute);
            w.WriteLeb128_u32((uint)t.TypeIndex.Value);
        }

        public static void WriteTableType(BinaryWriter w, TableType t)
        {
            // The GC-extended TableType has a second wire form prefixed
            // by 0x40 0x00 carrying an init expression. We emit the
            // legacy form when no explicit init expression is present
            // (the parser's default constructor synthesizes a
            // `ref.null` init for legacy modules), otherwise the
            // GC-extended form so non-null init exprs survive the
            // round-trip.
            if (NeedsExtendedForm(t))
            {
                w.Write((byte)0x40);
                w.Write((byte)0x00);
                ValTypeWriter.WriteRefType(w, t.ElementType);
                WriteLimits(w, t.Limits);
                WriteExpression(w, t.Init);
                return;
            }
            ValTypeWriter.WriteRefType(w, t.ElementType);
            WriteLimits(w, t.Limits);
        }

        /// <summary>
        /// Detect whether the table init expression is the synthesized
        /// `ref.null elemType end` that the parser inserts for legacy
        /// tables, vs. a real user-provided init that must round-trip
        /// through the 0x40 0x00 extended form.
        /// </summary>
        private static bool NeedsExtendedForm(TableType t)
        {
            var insts = t.Init.Instructions;
            // Legacy synthesized init: { ref.null <elemType> } (no end,
            // arity=1 with a single instruction). The parser only emits
            // the extended form for tables that carried an explicit
            // initializer in the binary, so the simplest rule is:
            // anything more complex than a single ref.null wins the
            // extended form.
            if (insts.Count != 1)
                return true;
            var only = insts[0];
            return only is not Wacs.Core.Instructions.Reference.InstRefNull;
        }

        // ---- FunctionType / ResultType ---------------------------------

        public static void WriteFunctionType(BinaryWriter w, FunctionType ft)
        {
            WriteResultType(w, ft.ParameterTypes);
            WriteResultType(w, ft.ResultType);
        }

        public static void WriteResultType(BinaryWriter w, ResultType rt)
        {
            w.WriteLeb128_u32((uint)rt.Types.Length);
            foreach (var t in rt.Types)
                ValTypeWriter.WriteValType(w, t, parseStorageType: false);
        }

        // ---- GC composite / recursive / sub / field --------------------

        public static void WriteFieldType(BinaryWriter w, FieldType ft)
        {
            // FieldType accepts the packed I8/I16 tokens. The parser
            // sets parseStorageType:true; the writer must mirror.
            ValTypeWriter.WriteValType(w, ft.StorageType, parseStorageType: true);
            w.Write((byte)ft.Mut);
        }

        public static void WriteCompositeType(BinaryWriter w, CompositeType ct)
        {
            switch (ct)
            {
                case FunctionType fn:
                    w.Write((byte)CompType.FuncFt);
                    WriteFunctionType(w, fn);
                    break;
                case StructType st:
                    w.Write((byte)CompType.StructSt);
                    w.WriteLeb128_u32((uint)st.FieldTypes.Length);
                    foreach (var f in st.FieldTypes) WriteFieldType(w, f);
                    break;
                case ArrayType at:
                    w.Write((byte)CompType.ArrayAt);
                    WriteFieldType(w, at.ElementType);
                    break;
                default:
                    throw new InvalidDataException($"Unknown CompositeType {ct?.GetType().Name}");
            }
        }

        public static void WriteSubType(BinaryWriter w, SubType st, bool inRecGroup)
        {
            // The "rec st" outer wrapper is handled by the caller. Inside,
            // a sub-with-supers always emits the explicit sub/sub-final
            // header. A final sub with no supers can fall through to the
            // bare composite-type encoding (the parser accepts both).
            if (st.SuperTypeIndexes.Length > 0)
            {
                w.Write(st.Final ? (byte)RecType.SubFinalXCt : (byte)RecType.SubXCt);
                w.WriteLeb128_u32((uint)st.SuperTypeIndexes.Length);
                foreach (var idx in st.SuperTypeIndexes)
                    w.WriteLeb128_u32((uint)idx.Value);
                WriteCompositeType(w, st.Body);
                return;
            }

            if (!st.Final)
            {
                // Non-final sub with no supers — must use the sub/sub-final
                // header to convey final=false; the bare composite encoding
                // implies final=true.
                w.Write((byte)RecType.SubXCt);
                w.WriteLeb128_u32(0u);
                WriteCompositeType(w, st.Body);
                return;
            }

            WriteCompositeType(w, st.Body);
        }

        public static void WriteRecursiveType(BinaryWriter w, RecursiveType rt)
        {
            if (rt.SubTypes.Length == 1)
            {
                // A single sub round-trips through the parser's "naked
                // sub or comptype" branch — emit just the sub.
                WriteSubType(w, rt.SubTypes[0], inRecGroup: false);
                return;
            }

            // True recursion group.
            w.Write((byte)RecType.RecSt);
            w.WriteLeb128_u32((uint)rt.SubTypes.Length);
            foreach (var sub in rt.SubTypes)
                WriteSubType(w, sub, inRecGroup: true);
        }

        // ---- Expression / InstructionSequence --------------------------

        public static void WriteExpression(BinaryWriter w, Expression expr)
        {
            // Parsed expressions retain their trailing InstEnd; writing
            // the sequence verbatim preserves it. Each instruction emits
            // its opcode prefix and then its immediates.
            WriteInstructions(w, expr.Instructions);
        }

        public static void WriteInstructions(BinaryWriter w, InstructionSequence seq)
        {
            for (int i = 0; i < seq.Count; i++)
                WriteInstruction(w, seq[i]!);
        }

        /// <summary>
        /// Write a single instruction: opcode (1 byte + LEB128 sub-opcode
        /// for FB/FC/FD/FE families), then immediates via
        /// <see cref="Wacs.Core.Instructions.InstructionBase.RenderBinary"/>,
        /// then — for block instructions — the inner sequence(s).
        /// </summary>
        public static void WriteInstruction(BinaryWriter w, Wacs.Core.Instructions.InstructionBase inst)
        {
            WriteOpcode(w, inst.Op);
            inst.RenderBinary(w);

            // Block-shaped instructions: the inner sequence(s) follow
            // the block-type immediate. Their parsed sequences already
            // include the terminating InstEnd (and InstElse for if/else),
            // so emitting the sequence verbatim is the right inverse.
            switch (inst)
            {
                case Wacs.Core.Instructions.InstBlock blk:
                    WriteInstructions(w, blk.GetBlock(0).Instructions);
                    break;
                case Wacs.Core.Instructions.InstLoop lp:
                    WriteInstructions(w, lp.GetBlock(0).Instructions);
                    break;
                case Wacs.Core.Instructions.InstIf ifi:
                    // If with else: first block has trailing InstElse,
                    // second block has trailing InstEnd. The parser
                    // splits them into two sequences; we emit them
                    // back-to-back. If with no else: only one sequence,
                    // already terminated by InstEnd.
                    WriteInstructions(w, ifi.GetBlock(0).Instructions);
                    if (((Wacs.Core.Instructions.IBlockInstruction)ifi).Count == 2)
                        WriteInstructions(w, ifi.GetBlock(1).Instructions);
                    break;
                case Wacs.Core.Instructions.InstTryTable tt:
                    WriteInstructions(w, tt.GetBlock(0).Instructions);
                    break;
            }
        }

        // ---- Opcode emission --------------------------------------------

        /// <summary>
        /// Inverse of the prefix-byte switch in <c>BinaryModuleParser.ParseInstruction</c>:
        ///  - bytes other than 0xFB/0xFC/0xFD/0xFE → single opcode byte
        ///  - <c>0xFB gccode:leb128_u32</c>
        ///  - <c>0xFC extcode:leb128_u32</c>
        ///  - <c>0xFD simdcode:leb128_u32</c>
        ///  - <c>0xFE atomcode:leb128_u32</c>
        /// </summary>
        public static void WriteOpcode(BinaryWriter w, Wacs.Core.OpCodes.ByteCode op)
        {
            switch (op.x00)
            {
                case Wacs.Core.OpCodes.OpCode.FB:
                    w.Write((byte)0xFB);
                    w.WriteLeb128_u32((uint)op.xFB);
                    break;
                case Wacs.Core.OpCodes.OpCode.FC:
                    w.Write((byte)0xFC);
                    w.WriteLeb128_u32((uint)op.xFC);
                    break;
                case Wacs.Core.OpCodes.OpCode.FD:
                    w.Write((byte)0xFD);
                    w.WriteLeb128_u32((uint)op.xFD);
                    break;
                case Wacs.Core.OpCodes.OpCode.FE:
                    w.Write((byte)0xFE);
                    w.WriteLeb128_u32((uint)op.xFE);
                    break;
                default:
                    w.Write((byte)op.x00);
                    break;
            }
        }
    }
}
