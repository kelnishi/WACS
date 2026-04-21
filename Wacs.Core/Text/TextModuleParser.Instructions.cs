// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.Instructions.Reference;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Text
{
    public static partial class TextModuleParser
    {
        // ---- Instruction-list entry points --------------------------------
        //
        // WAT bodies are sequences of instructions intermixing:
        //   - Plain form: keyword followed by its immediates as sibling atoms
        //   - Folded form: (op imms* innerInstr*) — inner instrs are folded
        //   - Block form: `block … end`, `loop … end`, `if … else? end`
        //     (each also has a folded variant)
        //
        // The list-parser walks a shared cursor over the outer form's child
        // nodes; block forms recursively delegate back into the list-parser.

        [System.Flags]
        private enum InstrStop
        {
            None = 0,
            End = 1,
            Else = 2,
            EndOrElse = End | Else,
        }

        /// <summary>
        /// Parse a flat run of instructions starting at <paramref name="i"/>
        /// inside <paramref name="parent"/>. Terminates at end-of-list or at
        /// a keyword atom matching <paramref name="stop"/>. Returns the
        /// accumulated instruction sequence.
        /// </summary>
        private static List<InstructionBase> ParseInstrList(
            TextFunctionContext fctx, SExpr parent, ref int i, InstrStop stop,
            out string? stopKeyword)
        {
            stopKeyword = null;
            var result = new List<InstructionBase>();
            while (i < parent.Children.Count)
            {
                var node = parent.Children[i];
                if (node.Kind == SExprKind.Atom)
                {
                    if (node.Token.Kind == TokenKind.Keyword)
                    {
                        var kw = node.AtomText();
                        if (stop != InstrStop.None)
                        {
                            if (kw == "end" && (stop & InstrStop.End) != 0)
                            {
                                stopKeyword = "end";
                                i++;
                                // Optional trailing label id — ignored (but consumed).
                                if (i < parent.Children.Count
                                    && parent.Children[i].Kind == SExprKind.Atom
                                    && parent.Children[i].Token.Kind == TokenKind.Id)
                                    i++;
                                return result;
                            }
                            if (kw == "else" && (stop & InstrStop.Else) != 0)
                            {
                                stopKeyword = "else";
                                return result;   // leave 'else' for caller
                            }
                        }
                        ParsePlainInstruction(fctx, parent, ref i, kw, result);
                        continue;
                    }
                    throw new FormatException(
                        $"line {node.Token.Line}: unexpected {node.Token.Kind} '{node.AtomText()}' in instruction list");
                }
                // Folded form
                ParseFoldedInstruction(fctx, node, result);
                i++;
            }
            return result;
        }

        // ---- Folded forms -------------------------------------------------

        private static void ParseFoldedInstruction(
            TextFunctionContext fctx, SExpr node, List<InstructionBase> output)
        {
            var head = node.Head;
            if (head == null || head.Kind != SExprKind.Atom || head.Token.Kind != TokenKind.Keyword)
                throw new FormatException($"line {node.Token.Line}: folded instruction must start with a keyword");

            var kw = head.AtomText();
            switch (kw)
            {
                case "block":
                case "loop":
                    ParseBlockFolded(fctx, node, kw, output);
                    return;
                case "if":
                    ParseIfFolded(fctx, node, output);
                    return;
            }

            // General folded form: (op imm* foldedInstr*)
            int ci = 1;
            var builder = BuildPlainInstruction(fctx, node, ref ci, kw, out var followingInstrsAreOperands);
            // The remaining children are operand instructions; they execute
            // before this instruction.
            if (followingInstrsAreOperands)
            {
                while (ci < node.Children.Count)
                {
                    var child = node.Children[ci];
                    if (child.Kind != SExprKind.List)
                        throw new FormatException(
                            $"line {child.Token.Line}: folded instructions can only contain folded operand sub-forms (no plain {child.AtomText()})");
                    ParseFoldedInstruction(fctx, child, output);
                    ci++;
                }
            }
            output.Add(builder);
        }

        private static void ParseBlockFolded(
            TextFunctionContext fctx, SExpr node, string kw, List<InstructionBase> output)
        {
            int i = 1;
            var label = TryConsumeLabelId(node, ref i);
            var blockType = ParseBlockType(fctx.Module, node, ref i);
            fctx.LabelStack.Add(label);
            try
            {
                var inner = new List<InstructionBase>();
                while (i < node.Children.Count)
                {
                    var child = node.Children[i++];
                    if (child.Kind != SExprKind.List)
                        throw new FormatException(
                            $"line {child.Token.Line}: block body inside folded form must use folded instructions");
                    ParseFoldedInstruction(fctx, child, inner);
                }
                // Append the implicit `end` for the inner sequence.
                inner.Add(new InstEnd());
                var seq = new InstructionSequence(inner);
                var block = kw == "block"
                    ? (InstructionBase)new InstBlock().Immediate(blockType, seq)
                    : new InstLoop().Immediate(blockType, seq);
                output.Add(block);
            }
            finally
            {
                fctx.LabelStack.RemoveAt(fctx.LabelStack.Count - 1);
            }
        }

        private static void ParseIfFolded(
            TextFunctionContext fctx, SExpr node, List<InstructionBase> output)
        {
            int i = 1;
            var label = TryConsumeLabelId(node, ref i);
            var blockType = ParseBlockType(fctx.Module, node, ref i);

            // Remaining children: zero or more condition folded-instrs,
            // then (then …), then optional (else …).
            var condChildren = new List<SExpr>();
            SExpr? thenForm = null, elseForm = null;
            while (i < node.Children.Count)
            {
                var child = node.Children[i++];
                if (child.Kind == SExprKind.List)
                {
                    if (child.IsForm("then")) { thenForm = child; break; }
                    condChildren.Add(child);
                    continue;
                }
                throw new FormatException(
                    $"line {child.Token.Line}: unexpected atom inside folded (if …)");
            }
            if (thenForm == null)
                throw new FormatException($"line {node.Token.Line}: (if …) missing (then …)");
            if (i < node.Children.Count)
            {
                var maybeElse = node.Children[i++];
                if (maybeElse.Kind == SExprKind.List && maybeElse.IsForm("else"))
                    elseForm = maybeElse;
                else
                    throw new FormatException($"line {maybeElse.Token.Line}: expected (else …) after (then …)");
            }
            if (i < node.Children.Count)
                throw new FormatException($"line {node.Children[i].Token.Line}: unexpected child after (else …)");

            // Emit condition operand instructions first, outside the label
            // scope (they don't see the if's label).
            foreach (var cc in condChildren)
                ParseFoldedInstruction(fctx, cc, output);

            // Push label for both then- and else-arms.
            fctx.LabelStack.Add(label);
            try
            {
                var thenInner = new List<InstructionBase>();
                int tj = 1;
                foreach (var _ in thenForm.Children) { /* count only */ }
                // (then instr*) — mixed instr list
                var thenInnerList = ParseInstrList(fctx, thenForm, ref tj, InstrStop.None, out _);
                thenInner.AddRange(thenInnerList);

                List<InstructionBase> elseInner;
                if (elseForm != null)
                {
                    var elseBody = new List<InstructionBase> { new InstElse() };
                    int ej = 1;
                    var els = ParseInstrList(fctx, elseForm, ref ej, InstrStop.None, out _);
                    elseBody.AddRange(els);
                    elseBody.Add(new InstEnd());
                    elseInner = elseBody;

                    // Combine: then-body then the else preamble, ending with End.
                    thenInner.AddRange(elseInner);
                }
                else
                {
                    thenInner.Add(new InstEnd());
                }

                // For InstIf.Immediate the contract is (ifSeq, elseSeq) — but
                // the binary-parser shape just stores two parallel Blocks.
                // We have been carrying a single linear sequence in thenInner;
                // feed it as the ifSeq and leave elseSeq empty — the inner
                // InstElse / InstEnd boundary markers communicate the split.
                var ifInst = new InstIf().Immediate(blockType,
                    new InstructionSequence(thenInner),
                    InstructionSequence.Empty);
                output.Add(ifInst);
            }
            finally
            {
                fctx.LabelStack.RemoveAt(fctx.LabelStack.Count - 1);
            }
        }

        // ---- Plain forms --------------------------------------------------

        /// <summary>
        /// Parse a plain-form instruction starting with keyword <paramref name="kw"/>
        /// at <paramref name="parent"/>[<paramref name="i"/>] (which must be
        /// the keyword atom — the caller passes the already-extracted text).
        /// Advances <paramref name="i"/> past the keyword and any immediates.
        /// </summary>
        private static void ParsePlainInstruction(
            TextFunctionContext fctx, SExpr parent, ref int i, string kw,
            List<InstructionBase> output)
        {
            // Block instructions break the "immediates after the keyword"
            // pattern — they open a new sub-list terminated by `end`.
            switch (kw)
            {
                case "block":
                case "loop":
                {
                    i++;   // consume keyword atom
                    var label = TryConsumeLabelId(parent, ref i);
                    var blockType = ParseBlockType(fctx.Module, parent, ref i);
                    fctx.LabelStack.Add(label);
                    List<InstructionBase> inner;
                    try
                    {
                        inner = ParseInstrList(fctx, parent, ref i, InstrStop.End, out _);
                    }
                    finally
                    {
                        fctx.LabelStack.RemoveAt(fctx.LabelStack.Count - 1);
                    }
                    inner.Add(new InstEnd());
                    var seq = new InstructionSequence(inner);
                    output.Add(kw == "block"
                        ? (InstructionBase)new InstBlock().Immediate(blockType, seq)
                        : new InstLoop().Immediate(blockType, seq));
                    return;
                }
                case "if":
                {
                    i++;
                    var label = TryConsumeLabelId(parent, ref i);
                    var blockType = ParseBlockType(fctx.Module, parent, ref i);
                    fctx.LabelStack.Add(label);
                    List<InstructionBase> thenBody, elseBody;
                    try
                    {
                        thenBody = ParseInstrList(fctx, parent, ref i, InstrStop.EndOrElse, out var stopKw);
                        if (stopKw == "else")
                        {
                            // Consume the 'else' keyword + its optional label
                            i++;
                            if (i < parent.Children.Count
                                && parent.Children[i].Kind == SExprKind.Atom
                                && parent.Children[i].Token.Kind == TokenKind.Id)
                                i++;
                            elseBody = new List<InstructionBase> { new InstElse() };
                            var rest = ParseInstrList(fctx, parent, ref i, InstrStop.End, out _);
                            elseBody.AddRange(rest);
                        }
                        else
                        {
                            elseBody = new List<InstructionBase>();
                        }
                    }
                    finally
                    {
                        fctx.LabelStack.RemoveAt(fctx.LabelStack.Count - 1);
                    }
                    thenBody.AddRange(elseBody);
                    thenBody.Add(new InstEnd());
                    var ifInst = new InstIf().Immediate(blockType,
                        new InstructionSequence(thenBody),
                        InstructionSequence.Empty);
                    output.Add(ifInst);
                    return;
                }
            }

            // Non-block instruction — keyword + optional immediate atoms.
            i++;   // consume keyword atom
            var built = BuildPlainInstructionImmediates(fctx, parent, ref i, kw);
            output.Add(built);
        }

        /// <summary>
        /// Construct an instruction instance from its keyword and immediate
        /// atoms in folded form. The caller provides the surrounding s-expr
        /// and a cursor pointing at the first potential immediate
        /// (<paramref name="ci"/> starts at 1 — the index right after the
        /// keyword head). Advances the cursor past consumed immediates and
        /// signals via <paramref name="followingInstrsAreOperands"/> whether
        /// remaining children are operand folded-instrs.
        /// </summary>
        private static InstructionBase BuildPlainInstruction(
            TextFunctionContext fctx, SExpr form, ref int ci, string kw,
            out bool followingInstrsAreOperands)
        {
            followingInstrsAreOperands = true;
            return BuildPlainInstructionImmediates(fctx, form, ref ci, kw);
        }

        /// <summary>
        /// Parse the immediates for the given keyword starting at
        /// <paramref name="parent"/>[<paramref name="i"/>] and return a fully
        /// configured instruction instance. Advances <paramref name="i"/>
        /// past the consumed immediates.
        /// </summary>
        private static InstructionBase BuildPlainInstructionImmediates(
            TextFunctionContext fctx, SExpr parent, ref int i, string kw)
        {
            // Fast path: no-immediate instructions (most numeric ops).
            // Look up the mnemonic via the registry; if the instruction
            // accepts no immediates, just return a fresh instance.
            switch (kw)
            {
                case "i32.const":
                    return new InstI32Const().Immediate(ReadImmS32(parent, ref i, kw));
                case "i64.const":
                {
                    long v = ReadImmS64(parent, ref i, kw);
                    return DecodeViaBinary(ByteCode.I64Const, w => w.WriteLeb128S64(v));
                }
                case "f32.const":
                {
                    float f = ReadImmF32(parent, ref i, kw);
                    return DecodeViaBinary(ByteCode.F32Const, w => w.WriteF32(f));
                }
                case "f64.const":
                {
                    double d = ReadImmF64(parent, ref i, kw);
                    return DecodeViaBinary(ByteCode.F64Const, w => w.WriteF64(d));
                }
                case "local.get":
                {
                    uint idx = ResolveLocalIdx(fctx, ReadImmIdxAtom(parent, ref i, kw));
                    return DecodeViaBinary(ByteCode.LocalGet, w => w.WriteLeb128U32(idx));
                }
                case "local.set":
                {
                    uint idx = ResolveLocalIdx(fctx, ReadImmIdxAtom(parent, ref i, kw));
                    return DecodeViaBinary(ByteCode.LocalSet, w => w.WriteLeb128U32(idx));
                }
                case "local.tee":
                {
                    uint idx = ResolveLocalIdx(fctx, ReadImmIdxAtom(parent, ref i, kw));
                    return DecodeViaBinary(ByteCode.LocalTee, w => w.WriteLeb128U32(idx));
                }
                case "global.get":
                {
                    uint idx = ResolveNamespaceIdx(fctx.Module.Globals, ReadImmIdxAtom(parent, ref i, kw), "global");
                    return DecodeViaBinary(ByteCode.GlobalGet, w => w.WriteLeb128U32(idx));
                }
                case "global.set":
                {
                    uint idx = ResolveNamespaceIdx(fctx.Module.Globals, ReadImmIdxAtom(parent, ref i, kw), "global");
                    return DecodeViaBinary(ByteCode.GlobalSet, w => w.WriteLeb128U32(idx));
                }
                case "call":
                {
                    uint idx = ResolveNamespaceIdx(fctx.Module.Funcs, ReadImmIdxAtom(parent, ref i, kw), "func");
                    return new InstCall().Immediate((FuncIdx)idx);
                }
                case "br":
                {
                    uint depth = ResolveLabel(fctx, ReadImmIdxAtom(parent, ref i, kw));
                    return DecodeViaBinary(ByteCode.Br, w => w.WriteLeb128U32(depth));
                }
                case "br_if":
                {
                    uint depth = ResolveLabel(fctx, ReadImmIdxAtom(parent, ref i, kw));
                    return DecodeViaBinary(ByteCode.BrIf, w => w.WriteLeb128U32(depth));
                }
                case "ref.null":
                {
                    // Operand is a heap-type shorthand.
                    var atom = ReadAtom(parent, ref i, kw);
                    var ht = ParseHeapTypeAtomForRefNull(atom);
                    // Binary encoding: the single byte encoding of the heap type.
                    return DecodeViaBinary(ByteCode.RefNull, w => w.Write((byte)ht));
                }
                case "ref.func":
                {
                    uint idx = ResolveNamespaceIdx(fctx.Module.Funcs, ReadImmIdxAtom(parent, ref i, kw), "func");
                    return DecodeViaBinary(ByteCode.RefFunc, w => w.WriteLeb128U32(idx));
                }
            }

            // Zero-immediate ops — look up by mnemonic. The factory produces
            // a ready instance; we don't need to parse further immediates.
            if (Mnemonics.TryLookup(kw, out var bc) && IsZeroImmediate(bc))
                return SpecFactory.Factory.CreateInstruction(bc);

            throw new NotSupportedException(
                $"line {parent.Token.Line}: instruction '{kw}' not yet supported by the text parser (phase 1.4 scope)");
        }

        /// <summary>
        /// Conservative allow-list for "this opcode has no immediates so we
        /// can return a fresh instance from the factory without setup". Any
        /// opcode that reads immediates in its Parse(BinaryReader) belongs
        /// out of this list.
        /// </summary>
        private static bool IsZeroImmediate(ByteCode bc)
        {
            // Admin / prefix bytes — never actual operations
            if (bc.x00 == OpCode.FB || bc.x00 == OpCode.FC || bc.x00 == OpCode.FD
                || bc.x00 == OpCode.FE || bc.x00 == OpCode.FF)
                return false;
            switch (bc.x00)
            {
                case OpCode.Unreachable:
                case OpCode.Nop:
                case OpCode.Return:
                case OpCode.Drop:
                case OpCode.RefIsNull:
                case OpCode.RefAsNonNull:
                case OpCode.RefEq:
                // i32 numeric (no immediates)
                case OpCode.I32Eqz:
                case OpCode.I32Eq: case OpCode.I32Ne:
                case OpCode.I32LtS: case OpCode.I32LtU:
                case OpCode.I32GtS: case OpCode.I32GtU:
                case OpCode.I32LeS: case OpCode.I32LeU:
                case OpCode.I32GeS: case OpCode.I32GeU:
                case OpCode.I32Clz: case OpCode.I32Ctz: case OpCode.I32Popcnt:
                case OpCode.I32Add: case OpCode.I32Sub: case OpCode.I32Mul:
                case OpCode.I32DivS: case OpCode.I32DivU:
                case OpCode.I32RemS: case OpCode.I32RemU:
                case OpCode.I32And: case OpCode.I32Or: case OpCode.I32Xor:
                case OpCode.I32Shl: case OpCode.I32ShrS: case OpCode.I32ShrU:
                case OpCode.I32Rotl: case OpCode.I32Rotr:
                // i64 numeric
                case OpCode.I64Eqz:
                case OpCode.I64Eq: case OpCode.I64Ne:
                case OpCode.I64LtS: case OpCode.I64LtU:
                case OpCode.I64GtS: case OpCode.I64GtU:
                case OpCode.I64LeS: case OpCode.I64LeU:
                case OpCode.I64GeS: case OpCode.I64GeU:
                case OpCode.I64Clz: case OpCode.I64Ctz: case OpCode.I64Popcnt:
                case OpCode.I64Add: case OpCode.I64Sub: case OpCode.I64Mul:
                case OpCode.I64DivS: case OpCode.I64DivU:
                case OpCode.I64RemS: case OpCode.I64RemU:
                case OpCode.I64And: case OpCode.I64Or: case OpCode.I64Xor:
                case OpCode.I64Shl: case OpCode.I64ShrS: case OpCode.I64ShrU:
                case OpCode.I64Rotl: case OpCode.I64Rotr:
                // f32 / f64
                case OpCode.F32Eq: case OpCode.F32Ne: case OpCode.F32Lt:
                case OpCode.F32Gt: case OpCode.F32Le: case OpCode.F32Ge:
                case OpCode.F64Eq: case OpCode.F64Ne: case OpCode.F64Lt:
                case OpCode.F64Gt: case OpCode.F64Le: case OpCode.F64Ge:
                case OpCode.F32Abs: case OpCode.F32Neg: case OpCode.F32Ceil:
                case OpCode.F32Floor: case OpCode.F32Trunc: case OpCode.F32Nearest:
                case OpCode.F32Sqrt: case OpCode.F32Add: case OpCode.F32Sub:
                case OpCode.F32Mul: case OpCode.F32Div: case OpCode.F32Min:
                case OpCode.F32Max: case OpCode.F32Copysign:
                case OpCode.F64Abs: case OpCode.F64Neg: case OpCode.F64Ceil:
                case OpCode.F64Floor: case OpCode.F64Trunc: case OpCode.F64Nearest:
                case OpCode.F64Sqrt: case OpCode.F64Add: case OpCode.F64Sub:
                case OpCode.F64Mul: case OpCode.F64Div: case OpCode.F64Min:
                case OpCode.F64Max: case OpCode.F64Copysign:
                // conversions
                case OpCode.I32WrapI64:
                case OpCode.I32TruncF32S: case OpCode.I32TruncF32U:
                case OpCode.I32TruncF64S: case OpCode.I32TruncF64U:
                case OpCode.I64ExtendI32S: case OpCode.I64ExtendI32U:
                case OpCode.I64TruncF32S: case OpCode.I64TruncF32U:
                case OpCode.I64TruncF64S: case OpCode.I64TruncF64U:
                case OpCode.F32ConvertI32S: case OpCode.F32ConvertI32U:
                case OpCode.F32ConvertI64S: case OpCode.F32ConvertI64U:
                case OpCode.F32DemoteF64:
                case OpCode.F64ConvertI32S: case OpCode.F64ConvertI32U:
                case OpCode.F64ConvertI64S: case OpCode.F64ConvertI64U:
                case OpCode.F64PromoteF32:
                case OpCode.I32ReinterpretF32: case OpCode.I64ReinterpretF64:
                case OpCode.F32ReinterpretI32: case OpCode.F64ReinterpretI64:
                case OpCode.I32Extend8S: case OpCode.I32Extend16S:
                case OpCode.I64Extend8S: case OpCode.I64Extend16S: case OpCode.I64Extend32S:
                    return true;
                default:
                    return false;
            }
        }

        // ---- Binary-delegation helper -------------------------------------

        /// <summary>
        /// Create an instruction of the given opcode and populate its
        /// immediates by synthesizing a binary byte stream and delegating to
        /// the existing <see cref="InstructionBase.Parse(BinaryReader)"/>.
        /// </summary>
        private static InstructionBase DecodeViaBinary(ByteCode code, Action<BinaryWriter> writeImmediates)
        {
            var inst = SpecFactory.Factory.CreateInstruction(code);
            var reader = WatBinaryEncoder.BuildReader(writeImmediates);
            return inst.Parse(reader);
        }

        // ---- Immediate readers --------------------------------------------

        private static SExpr ReadAtom(SExpr parent, ref int i, string kw)
        {
            if (i >= parent.Children.Count || parent.Children[i].Kind != SExprKind.Atom)
                throw new FormatException(
                    $"line {parent.Token.Line}: instruction '{kw}' expects an immediate atom");
            var a = parent.Children[i];
            i++;
            return a;
        }

        private static int ReadImmS32(SExpr parent, ref int i, string kw)
        {
            var a = ReadAtom(parent, ref i, kw);
            return ParseSignedInt32(a);
        }

        private static long ReadImmS64(SExpr parent, ref int i, string kw)
        {
            var a = ReadAtom(parent, ref i, kw);
            return ParseSignedInt64(a);
        }

        private static float ReadImmF32(SExpr parent, ref int i, string kw)
        {
            var a = ReadAtom(parent, ref i, kw);
            return ParseFloat32(a);
        }

        private static double ReadImmF64(SExpr parent, ref int i, string kw)
        {
            var a = ReadAtom(parent, ref i, kw);
            return ParseFloat64(a);
        }

        private static SExpr ReadImmIdxAtom(SExpr parent, ref int i, string kw) =>
            ReadAtom(parent, ref i, kw);

        // ---- Numeric literal parsers --------------------------------------
        //
        // Phase 1.4 scope: decimal and hex integers, decimal / hex floats
        // (including p-exponents), +/- sign, underscores as digit separators.
        // Deferred: inf / nan / nan:0x… payload literals. These show up in
        // spec f32/f64 tests and will need handling when we integrate with
        // the spec suite.

        private static int ParseSignedInt32(SExpr atom)
        {
            long v = ParseSignedInt64(atom);
            if (v > int.MaxValue || v < int.MinValue)
            {
                // Treat out-of-range as an unsigned modulo-32 wrap — this is
                // what i32.const -1 (== 0xFFFFFFFF) does when written as 4294967295.
                return unchecked((int)v);
            }
            return (int)v;
        }

        private static long ParseSignedInt64(SExpr atom)
        {
            if (atom.Kind != SExprKind.Atom)
                throw new FormatException($"line {atom.Token.Line}: expected integer literal");
            var text = atom.AtomText().Replace("_", "");
            int sign = 1;
            if (text.StartsWith("+")) text = text.Substring(1);
            else if (text.StartsWith("-")) { sign = -1; text = text.Substring(1); }

            ulong value;
            if (text.StartsWith("0x") || text.StartsWith("0X"))
            {
                if (!ulong.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                    throw new FormatException($"line {atom.Token.Line}: bad hex integer '{atom.AtomText()}'");
            }
            else
            {
                if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    throw new FormatException($"line {atom.Token.Line}: bad integer '{atom.AtomText()}'");
            }
            // Apply sign. Signed range check is caller's concern.
            if (sign == -1)
                return -unchecked((long)value);
            return unchecked((long)value);
        }

        private static float ParseFloat32(SExpr atom)
        {
            var text = atom.AtomText().Replace("_", "");
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                throw new FormatException($"line {atom.Token.Line}: bad f32 literal '{atom.AtomText()}' (phase 1.4 doesn't yet handle inf/nan/hex floats)");
            return f;
        }

        private static double ParseFloat64(SExpr atom)
        {
            var text = atom.AtomText().Replace("_", "");
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                throw new FormatException($"line {atom.Token.Line}: bad f64 literal '{atom.AtomText()}' (phase 1.4 doesn't yet handle inf/nan/hex floats)");
            return d;
        }

        // ---- Index resolution ---------------------------------------------

        private static uint ResolveLocalIdx(TextFunctionContext fctx, SExpr atom)
        {
            var text = atom.AtomText();
            if (atom.Token.Kind == TokenKind.Id)
            {
                if (!fctx.TryResolveLocal(text, out var idx))
                    throw new FormatException($"line {atom.Token.Line}: unknown local {text}");
                return (uint)idx;
            }
            if (!uint.TryParse(text, out var n))
                throw new FormatException($"line {atom.Token.Line}: bad local index '{text}'");
            return n;
        }

        private static uint ResolveNamespaceIdx(NameTable table, SExpr atom, string ns)
        {
            var text = atom.AtomText();
            if (atom.Token.Kind == TokenKind.Id)
            {
                if (!table.TryResolve(text, out var idx))
                    throw new FormatException($"line {atom.Token.Line}: unknown {ns} {text}");
                return (uint)idx;
            }
            if (!uint.TryParse(text, out var n))
                throw new FormatException($"line {atom.Token.Line}: bad {ns} index '{text}'");
            return n;
        }

        private static uint ResolveLabel(TextFunctionContext fctx, SExpr atom)
        {
            var text = atom.AtomText();
            if (atom.Token.Kind == TokenKind.Id)
            {
                if (!fctx.TryResolveLabel(text, out var depth))
                    throw new FormatException($"line {atom.Token.Line}: unknown label {text}");
                return (uint)depth;
            }
            if (!uint.TryParse(text, out var n))
                throw new FormatException($"line {atom.Token.Line}: bad label index '{text}'");
            return n;
        }

        // ---- Block-type + heap-type helpers -------------------------------

        private static string? TryConsumeLabelId(SExpr parent, ref int i)
        {
            if (i < parent.Children.Count
                && parent.Children[i].Kind == SExprKind.Atom
                && parent.Children[i].Token.Kind == TokenKind.Id)
            {
                var name = parent.Children[i].AtomText();
                i++;
                return name;
            }
            return null;
        }

        /// <summary>
        /// Parse an optional block-type annotation. Recognized forms:
        ///   - No annotation → <see cref="ValType.Empty"/>
        ///   - <c>(result T)</c> → <c>T</c>
        ///   - <c>(type $n)</c> → resolved typeidx (as a DefType-valued ValType)
        /// Multi-value forms (<c>(param …)</c> etc.) are not yet handled.
        /// </summary>
        private static ValType ParseBlockType(TextParseContext ctx, SExpr parent, ref int i)
        {
            if (i >= parent.Children.Count) return ValType.Empty;
            var child = parent.Children[i];
            if (child.Kind != SExprKind.List) return ValType.Empty;
            if (child.IsForm("result"))
            {
                if (child.Children.Count == 1)
                {
                    i++;
                    return ValType.Empty;
                }
                if (child.Children.Count != 2)
                    throw new NotSupportedException($"line {child.Token.Line}: multi-value (result …) block type not yet supported (phase 1.4)");
                var vt = ParseValType(ctx, child.Children[1]);
                i++;
                return vt;
            }
            if (child.IsForm("type"))
            {
                // `(type $n)` form — resolve to a type index. DefType is
                // encoded with sign bit NOT set (positive).
                if (child.Children.Count != 2)
                    throw new FormatException($"line {child.Token.Line}: (type …) block type needs one operand");
                var idxAtom = child.Children[1];
                int idx;
                if (idxAtom.Token.Kind == TokenKind.Id)
                {
                    if (!ctx.Types.TryResolve(idxAtom.AtomText(), out idx))
                        throw new FormatException($"line {idxAtom.Token.Line}: unknown type {idxAtom.AtomText()}");
                }
                else
                {
                    if (!int.TryParse(idxAtom.AtomText(), out idx))
                        throw new FormatException($"line {idxAtom.Token.Line}: bad type index");
                }
                i++;
                return (ValType)idx;
            }
            if (child.IsForm("param"))
                throw new NotSupportedException($"line {child.Token.Line}: inline (param …) block type not yet supported (phase 1.4)");
            return ValType.Empty;
        }

        /// <summary>
        /// Parse the heap-type operand of <c>ref.null</c>. Only the abstract
        /// heap-type forms are handled in phase 1.4; typeidx operands (<c>$t</c>)
        /// need a different binary encoding and are deferred.
        /// </summary>
        private static HeapType ParseHeapTypeAtomForRefNull(SExpr atom)
        {
            if (atom.Kind != SExprKind.Atom || atom.Token.Kind != TokenKind.Keyword)
                throw new NotSupportedException($"line {atom.Token.Line}: phase 1.4 ref.null only supports abstract heap-type operands");
            switch (atom.AtomText())
            {
                case "func":      return HeapType.Func;
                case "extern":    return HeapType.Extern;
                case "any":       return HeapType.Any;
                case "eq":        return HeapType.Eq;
                case "i31":       return HeapType.I31;
                case "struct":    return HeapType.Struct;
                case "array":     return HeapType.Array;
                case "exn":       return HeapType.Exn;
                case "noexn":     return HeapType.NoExn;
                case "nofunc":    return HeapType.NoFunc;
                case "noextern":  return HeapType.NoExtern;
                case "none":      return HeapType.None;
                default:
                    throw new FormatException($"line {atom.Token.Line}: unknown heap type '{atom.AtomText()}'");
            }
        }

        // ---- Public: parse a body / expression given an enclosing form ----

        /// <summary>
        /// Parse an instruction body inside <paramref name="form"/> starting
        /// at <paramref name="i"/> and running to end-of-form. Returns an
        /// <see cref="Expression"/> whose instruction sequence terminates
        /// with an <see cref="InstEnd"/>. Used for function bodies and for
        /// global / table / elem / data initializer expressions.
        /// </summary>
        internal static Expression ParseExpressionBody(TextFunctionContext fctx, SExpr form, ref int i, int arity, bool isStatic)
        {
            var instrs = ParseInstrList(fctx, form, ref i, InstrStop.None, out _);
            instrs.Add(new InstEnd());
            return new Expression(arity, new InstructionSequence(instrs), isStatic);
        }
    }
}
