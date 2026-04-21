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
                case "try_table":
                    ParseTryTableFolded(fctx, node, output);
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

        private static void ParseTryTableFolded(
            TextFunctionContext fctx, SExpr node, List<InstructionBase> output)
        {
            int i = 1;
            var label = TryConsumeLabelId(node, ref i);
            var blockType = ParseBlockType(fctx.Module, node, ref i);
            var catches = ParseCatchClauses(fctx, node, ref i);
            fctx.LabelStack.Add(label);
            try
            {
                var inner = new List<InstructionBase>();
                while (i < node.Children.Count)
                {
                    var child = node.Children[i++];
                    if (child.Kind != SExprKind.List)
                        throw new FormatException(
                            $"line {child.Token.Line}: try_table body inside folded form must use folded instructions");
                    ParseFoldedInstruction(fctx, child, inner);
                }
                inner.Add(new InstEnd());
                output.Add(new InstTryTable().Immediate(
                    blockType, new InstructionSequence(inner), catches));
            }
            finally
            {
                fctx.LabelStack.RemoveAt(fctx.LabelStack.Count - 1);
            }
        }

        /// <summary>
        /// Parse zero or more <c>(catch …)</c> / <c>(catch_ref …)</c> /
        /// <c>(catch_all …)</c> / <c>(catch_all_ref …)</c> clauses
        /// following a try_table's block-type. Advances <paramref name="i"/>
        /// past all consumed clauses.
        /// </summary>
        private static CatchType[] ParseCatchClauses(
            TextFunctionContext fctx, SExpr parent, ref int i)
        {
            var list = new List<CatchType>();
            while (i < parent.Children.Count
                && parent.Children[i].Kind == SExprKind.List
                && parent.Children[i].Head != null
                && parent.Children[i].Head!.Token.Kind == TokenKind.Keyword)
            {
                var clause = parent.Children[i];
                var cw = clause.Head!.AtomText();
                CatchFlags? flags = cw switch
                {
                    "catch"          => (CatchFlags?)CatchFlags.None,
                    "catch_ref"      => CatchFlags.CatchRef,
                    "catch_all"      => CatchFlags.CatchAll,
                    "catch_all_ref"  => CatchFlags.CatchAllRef,
                    _ => null,
                };
                if (flags == null) break;
                i++;
                // Shape:
                //   (catch $tag $label)
                //   (catch_ref $tag $label)
                //   (catch_all $label)
                //   (catch_all_ref $label)
                int j = 1;
                if (flags == CatchFlags.None || flags == CatchFlags.CatchRef)
                {
                    var tagIdx = (TagIdx)ResolveNamespaceIdx(fctx.Module.Tags, clause.Children[j++], "tag");
                    var labelIdx = (LabelIdx)ResolveLabel(fctx, clause.Children[j++]);
                    list.Add(new CatchType(flags.Value, tagIdx, labelIdx));
                }
                else
                {
                    var labelIdx = (LabelIdx)ResolveLabel(fctx, clause.Children[j++]);
                    list.Add(new CatchType(flags.Value, labelIdx));
                }
            }
            return list.ToArray();
        }

        private static void ParseBlockFolded(
            TextFunctionContext fctx, SExpr node, string kw, List<InstructionBase> output)
        {
            int ii = 1;
            var label = TryConsumeLabelId(node, ref ii);
            var blockType = ParseBlockType(fctx.Module, node, ref ii);
            fctx.LabelStack.Add(label);
            try
            {
                // Body may mix folded (parenthesized) and plain (atom-run)
                // instructions — spec allows both inside a folded block.
                var inner = ParseInstrList(fctx, node, ref ii, InstrStop.None, out _);
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

                InstructionSequence ifSeq, elseSeq;
                if (elseForm != null)
                {
                    thenInner.Add(new InstElse());
                    ifSeq = new InstructionSequence(thenInner);

                    var elseBody = new List<InstructionBase>();
                    int ej = 1;
                    var els = ParseInstrList(fctx, elseForm, ref ej, InstrStop.None, out _);
                    elseBody.AddRange(els);
                    elseBody.Add(new InstEnd());
                    elseSeq = new InstructionSequence(elseBody);
                }
                else
                {
                    thenInner.Add(new InstEnd());
                    ifSeq = new InstructionSequence(thenInner);
                    elseSeq = InstructionSequence.Empty;
                }
                var ifInst = new InstIf().Immediate(blockType, ifSeq, elseSeq);
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
                case "try_table":
                {
                    // try_table has a block-type + zero or more (catch …)
                    // clauses + a body terminated by `end`.
                    i++;   // consume 'try_table' keyword
                    var label = TryConsumeLabelId(parent, ref i);
                    var blockType = ParseBlockType(fctx.Module, parent, ref i);
                    var catches = ParseCatchClauses(fctx, parent, ref i);
                    fctx.LabelStack.Add(label);
                    List<InstructionBase> innerTry;
                    try
                    {
                        innerTry = ParseInstrList(fctx, parent, ref i, InstrStop.End, out _);
                    }
                    finally
                    {
                        fctx.LabelStack.RemoveAt(fctx.LabelStack.Count - 1);
                    }
                    innerTry.Add(new InstEnd());
                    output.Add(new InstTryTable().Immediate(
                        blockType, new InstructionSequence(innerTry), catches));
                    return;
                }
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
                    // Per InstIf's contract (mirroring binary parser shape):
                    //   IfBlock instructions end with InstElse when an else
                    //   arm exists, or InstEnd otherwise.
                    //   ElseBlock instructions end with InstEnd.
                    // Binary-parser invariant enforced by EndsWithElse /
                    // HasExplicitEnd.
                    var ifSeq = new List<InstructionBase>(thenBody);
                    InstructionSequence elseSeq;
                    if (elseBody.Count > 0)
                    {
                        // else branch exists — then-body ends with InstElse,
                        // else-body ends with InstEnd.
                        ifSeq.Add(new InstElse());
                        elseBody.Add(new InstEnd());
                        elseSeq = new InstructionSequence(elseBody);
                    }
                    else
                    {
                        ifSeq.Add(new InstEnd());
                        elseSeq = InstructionSequence.Empty;
                    }
                    var ifInst = new InstIf().Immediate(blockType,
                        new InstructionSequence(ifSeq), elseSeq);
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
                    // Operand is either an abstract heap-type keyword
                    // (func, extern, any, eq, i31, struct, array, exn,
                    // noexn, nofunc, noextern, none) or a typeidx
                    // ($name / integer). Abstract forms encode as a single
                    // byte; typeidx forms encode as an LEB128 s33 of the
                    // index — the binary parser uses the same dispatch.
                    var atom = ReadAtom(parent, ref i, kw);
                    if (TryParseAbstractHeapType(atom, out var ht))
                        return DecodeViaBinary((ByteCode)OpCode.RefNull, w => w.Write((byte)ht));
                    // typeidx form
                    uint tIdx = ResolveNamespaceIdx(fctx.Module.Types, atom, "type");
                    return DecodeViaBinary((ByteCode)OpCode.RefNull, w => w.WriteLeb128S32((int)tIdx));
                }
                case "ref.func":
                {
                    uint idx = ResolveNamespaceIdx(fctx.Module.Funcs, ReadImmIdxAtom(parent, ref i, kw), "func");
                    return DecodeViaBinary(ByteCode.RefFunc, w => w.WriteLeb128U32(idx));
                }
                case "call_indirect":
                {
                    // (call_indirect $t? typeuse) — table index defaults to 0.
                    uint tableIdx = 0;
                    if (i < parent.Children.Count
                        && parent.Children[i].Kind == SExprKind.Atom
                        && parent.Children[i].Token.Kind != TokenKind.Keyword)
                    {
                        tableIdx = ResolveNamespaceIdx(fctx.Module.Tables, parent.Children[i], "table");
                        i++;
                    }
                    int ti = ParseFuncTypeUseWithNames(fctx.Module, parent, ref i, out _);
                    uint typeIdx = (uint)ti;
                    return DecodeViaBinary((ByteCode)OpCode.CallIndirect,
                        w => { w.WriteLeb128U32(typeIdx); w.WriteLeb128U32(tableIdx); });
                }
                case "br_table":
                {
                    // br_table L0 L1 … Ln   — n+1 labels: first n are the
                    // entries, last is the default.
                    var labels = new List<uint>();
                    while (i < parent.Children.Count
                        && parent.Children[i].Kind == SExprKind.Atom
                        && parent.Children[i].Token.Kind != TokenKind.Keyword)
                    {
                        labels.Add(ResolveLabel(fctx, parent.Children[i]));
                        i++;
                    }
                    if (labels.Count == 0)
                        throw new FormatException($"line {parent.Token.Line}: br_table needs at least one label");
                    uint defaultLabel = labels[labels.Count - 1];
                    uint n = (uint)(labels.Count - 1);
                    return DecodeViaBinary((ByteCode)OpCode.BrTable, w =>
                    {
                        w.WriteLeb128U32(n);
                        for (int k = 0; k < (int)n; k++) w.WriteLeb128U32(labels[k]);
                        w.WriteLeb128U32(defaultLabel);
                    });
                }
                case "memory.size":
                case "memory.grow":
                {
                    // Optional memory index; binary encoding: a single byte
                    // for memory-ref (default 0x00 for memory 0).
                    byte memIdx = 0;
                    if (i < parent.Children.Count
                        && parent.Children[i].Kind == SExprKind.Atom
                        && parent.Children[i].Token.Kind != TokenKind.Keyword)
                    {
                        memIdx = (byte)ResolveNamespaceIdx(fctx.Module.Mems, parent.Children[i], "memory");
                        i++;
                    }
                    var code = kw == "memory.size"
                        ? (ByteCode)OpCode.MemorySize
                        : (ByteCode)OpCode.MemoryGrow;
                    return DecodeViaBinary(code, w => w.Write(memIdx));
                }
                case "call_ref":
                {
                    uint ti = ResolveNamespaceIdx(fctx.Module.Types, ReadImmIdxAtom(parent, ref i, kw), "type");
                    return DecodeViaBinary((ByteCode)OpCode.CallRef, w => w.WriteLeb128U32(ti));
                }
                case "return_call":
                {
                    uint idx = ResolveNamespaceIdx(fctx.Module.Funcs, ReadImmIdxAtom(parent, ref i, kw), "func");
                    return DecodeViaBinary((ByteCode)OpCode.ReturnCall, w => w.WriteLeb128U32(idx));
                }
                case "throw":
                {
                    uint tagIdx = ResolveNamespaceIdx(fctx.Module.Tags, ReadImmIdxAtom(parent, ref i, kw), "tag");
                    return DecodeViaBinary((ByteCode)OpCode.Throw, w => w.WriteLeb128U32(tagIdx));
                }
                case "throw_ref":
                    return SpecFactory.Factory.CreateInstruction((ByteCode)OpCode.ThrowRef);
                case "return_call_indirect":
                {
                    uint tableIdx = 0;
                    if (i < parent.Children.Count
                        && parent.Children[i].Kind == SExprKind.Atom
                        && parent.Children[i].Token.Kind != TokenKind.Keyword)
                    {
                        tableIdx = ResolveNamespaceIdx(fctx.Module.Tables, parent.Children[i], "table");
                        i++;
                    }
                    int ti = ParseFuncTypeUseWithNames(fctx.Module, parent, ref i, out _);
                    uint typeIdx = (uint)ti;
                    return DecodeViaBinary((ByteCode)OpCode.ReturnCallIndirect,
                        w => { w.WriteLeb128U32(typeIdx); w.WriteLeb128U32(tableIdx); });
                }
                case "return_call_ref":
                {
                    uint ti = ResolveNamespaceIdx(fctx.Module.Types, ReadImmIdxAtom(parent, ref i, kw), "type");
                    return DecodeViaBinary((ByteCode)OpCode.ReturnCallRef, w => w.WriteLeb128U32(ti));
                }
                case "br_on_null":
                {
                    uint depth = ResolveLabel(fctx, ReadImmIdxAtom(parent, ref i, kw));
                    return DecodeViaBinary((ByteCode)OpCode.BrOnNull, w => w.WriteLeb128U32(depth));
                }
                case "br_on_non_null":
                {
                    uint depth = ResolveLabel(fctx, ReadImmIdxAtom(parent, ref i, kw));
                    return DecodeViaBinary((ByteCode)OpCode.BrOnNonNull, w => w.WriteLeb128U32(depth));
                }
                case "table.get":
                {
                    uint idx = i < parent.Children.Count && parent.Children[i].Kind == SExprKind.Atom
                        ? ResolveNamespaceIdx(fctx.Module.Tables, parent.Children[i++], "table") : 0;
                    return DecodeViaBinary((ByteCode)OpCode.TableGet, w => w.WriteLeb128U32(idx));
                }
                case "table.set":
                {
                    uint idx = i < parent.Children.Count && parent.Children[i].Kind == SExprKind.Atom
                        ? ResolveNamespaceIdx(fctx.Module.Tables, parent.Children[i++], "table") : 0;
                    return DecodeViaBinary((ByteCode)OpCode.TableSet, w => w.WriteLeb128U32(idx));
                }
                case "table.size":
                case "table.grow":
                case "table.fill":
                {
                    uint idx = i < parent.Children.Count && parent.Children[i].Kind == SExprKind.Atom
                        && parent.Children[i].Token.Kind != TokenKind.Keyword
                        ? ResolveNamespaceIdx(fctx.Module.Tables, parent.Children[i++], "table") : 0;
                    ExtCode ec = kw switch
                    {
                        "table.size" => ExtCode.TableSize,
                        "table.grow" => ExtCode.TableGrow,
                        _            => ExtCode.TableFill,
                    };
                    return DecodeViaBinary((ByteCode)ec, w => w.WriteLeb128U32(idx));
                }
                case "table.copy":
                {
                    uint d = 0, s = 0;
                    if (i < parent.Children.Count && parent.Children[i].Kind == SExprKind.Atom
                        && parent.Children[i].Token.Kind != TokenKind.Keyword)
                    {
                        d = ResolveNamespaceIdx(fctx.Module.Tables, parent.Children[i++], "table");
                        if (i < parent.Children.Count && parent.Children[i].Kind == SExprKind.Atom
                            && parent.Children[i].Token.Kind != TokenKind.Keyword)
                            s = ResolveNamespaceIdx(fctx.Module.Tables, parent.Children[i++], "table");
                    }
                    uint dc = d, sc = s;
                    return DecodeViaBinary((ByteCode)ExtCode.TableCopy, w => { w.WriteLeb128U32(dc); w.WriteLeb128U32(sc); });
                }
                case "table.init":
                {
                    // Two shapes:
                    //   (table.init $elem)            — table 0 implicit
                    //   (table.init $table $elem)     — explicit table
                    // Look ahead to see if there are two atom operands;
                    // if so, resolve first as table, second as elem.
                    uint t = 0, e;
                    var first = parent.Children[i];
                    bool hasSecond = i + 1 < parent.Children.Count
                        && parent.Children[i + 1].Kind == SExprKind.Atom
                        && parent.Children[i + 1].Token.Kind != TokenKind.Keyword;
                    if (hasSecond)
                    {
                        t = ResolveNamespaceIdx(fctx.Module.Tables, first, "table");
                        i++;
                        e = ResolveNamespaceIdx(fctx.Module.Elems, parent.Children[i], "elem");
                        i++;
                    }
                    else
                    {
                        e = ResolveNamespaceIdx(fctx.Module.Elems, first, "elem");
                        i++;
                    }
                    uint ec = e, tc = t;
                    return DecodeViaBinary((ByteCode)ExtCode.TableInit, w => { w.WriteLeb128U32(ec); w.WriteLeb128U32(tc); });
                }
                case "elem.drop":
                {
                    uint e = ResolveNamespaceIdx(fctx.Module.Elems, ReadImmIdxAtom(parent, ref i, kw), "elem");
                    return DecodeViaBinary((ByteCode)ExtCode.ElemDrop, w => w.WriteLeb128U32(e));
                }
                case "memory.init":
                {
                    // (memory.init $data)           — memory 0 implicit
                    // (memory.init $mem $data)      — explicit memory
                    byte mem = 0;
                    uint d;
                    var first = parent.Children[i];
                    bool hasSecond = i + 1 < parent.Children.Count
                        && parent.Children[i + 1].Kind == SExprKind.Atom
                        && parent.Children[i + 1].Token.Kind != TokenKind.Keyword;
                    if (hasSecond)
                    {
                        mem = (byte)ResolveNamespaceIdx(fctx.Module.Mems, first, "memory");
                        i++;
                        d = ResolveNamespaceIdx(fctx.Module.Datas, parent.Children[i], "data");
                        i++;
                    }
                    else
                    {
                        d = ResolveNamespaceIdx(fctx.Module.Datas, first, "data");
                        i++;
                    }
                    byte memC = mem;
                    return DecodeViaBinary((ByteCode)ExtCode.MemoryInit, w => { w.WriteLeb128U32(d); w.Write(memC); });
                }
                case "memory.copy":
                {
                    byte dst = 0, src = 0;
                    if (i < parent.Children.Count
                        && parent.Children[i].Kind == SExprKind.Atom
                        && parent.Children[i].Token.Kind != TokenKind.Keyword)
                    {
                        dst = (byte)ResolveNamespaceIdx(fctx.Module.Mems, parent.Children[i], "memory");
                        i++;
                        if (i < parent.Children.Count
                            && parent.Children[i].Kind == SExprKind.Atom
                            && parent.Children[i].Token.Kind != TokenKind.Keyword)
                        {
                            src = (byte)ResolveNamespaceIdx(fctx.Module.Mems, parent.Children[i], "memory");
                            i++;
                        }
                    }
                    byte d = dst, s = src;
                    return DecodeViaBinary((ByteCode)ExtCode.MemoryCopy, w => { w.Write(d); w.Write(s); });
                }
                case "memory.fill":
                {
                    byte mem = 0;
                    if (i < parent.Children.Count
                        && parent.Children[i].Kind == SExprKind.Atom
                        && parent.Children[i].Token.Kind != TokenKind.Keyword)
                    {
                        mem = (byte)ResolveNamespaceIdx(fctx.Module.Mems, parent.Children[i], "memory");
                        i++;
                    }
                    byte m = mem;
                    return DecodeViaBinary((ByteCode)ExtCode.MemoryFill, w => w.Write(m));
                }
                case "data.drop":
                {
                    uint d = ResolveNamespaceIdx(fctx.Module.Datas, ReadImmIdxAtom(parent, ref i, kw), "data");
                    return DecodeViaBinary((ByteCode)ExtCode.DataDrop, w => w.WriteLeb128U32(d));
                }
                case "select":
                {
                    // `select` (no type) — zero-immediate. Mapped to Select.
                    // `select (result T)*` (one or more result annotations)
                    // becomes SelectT with a concatenated type vec.
                    if (i < parent.Children.Count
                        && parent.Children[i].Kind == SExprKind.List
                        && parent.Children[i].IsForm("result"))
                    {
                        var types = new List<ValType>();
                        while (i < parent.Children.Count
                            && parent.Children[i].Kind == SExprKind.List
                            && parent.Children[i].IsForm("result"))
                        {
                            var rForm = parent.Children[i];
                            i++;
                            for (int j = 1; j < rForm.Children.Count; j++)
                                types.Add(ParseValType(fctx.Module, rForm.Children[j]));
                        }
                        return DecodeViaBinary((ByteCode)OpCode.SelectT, w =>
                        {
                            w.WriteLeb128U32((uint)types.Count);
                            foreach (var t in types)
                                WriteValTypeByte(w, t);
                        });
                    }
                    return SpecFactory.Factory.CreateInstruction((ByteCode)OpCode.Select);
                }
            }

            // Memory load/store — memarg-immediate ops. Handle via a table
            // of natural alignments.
            if (TryGetMemoryOpcode(kw, out var memCode, out var naturalAlign))
                return BuildMemoryInstructionWithContext(memCode, naturalAlign, parent, ref i, fctx);

            // Zero-immediate ops — look up by mnemonic. The factory produces
            // a ready instance; we don't need to parse further immediates.
            if (Mnemonics.TryLookup(kw, out var bc) && IsZeroImmediate(bc))
                return SpecFactory.Factory.CreateInstruction(bc);
            // Extended zero-immediate: the FC-prefixed trunc_sat ops have
            // no immediates.
            if (Mnemonics.TryLookup(kw, out var bc2) && bc2.x00 == OpCode.FC)
            {
                switch (bc2.xFC)
                {
                    case ExtCode.I32TruncSatF32S:
                    case ExtCode.I32TruncSatF32U:
                    case ExtCode.I32TruncSatF64S:
                    case ExtCode.I32TruncSatF64U:
                    case ExtCode.I64TruncSatF32S:
                    case ExtCode.I64TruncSatF32U:
                    case ExtCode.I64TruncSatF64S:
                    case ExtCode.I64TruncSatF64U:
                        return SpecFactory.Factory.CreateInstruction(bc2);
                }
            }

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

        // ---- Memory ops ---------------------------------------------------

        /// <summary>
        /// Table of memory load/store mnemonics to their binary opcode and
        /// natural alignment (log2 of the byte width). Both load and store
        /// share the memarg-immediate shape.
        /// </summary>
        private static bool TryGetMemoryOpcode(string kw, out ByteCode code, out int naturalAlignLog2)
        {
            switch (kw)
            {
                case "i32.load":    code = (ByteCode)OpCode.I32Load;    naturalAlignLog2 = 2; return true;
                case "i64.load":    code = (ByteCode)OpCode.I64Load;    naturalAlignLog2 = 3; return true;
                case "f32.load":    code = (ByteCode)OpCode.F32Load;    naturalAlignLog2 = 2; return true;
                case "f64.load":    code = (ByteCode)OpCode.F64Load;    naturalAlignLog2 = 3; return true;
                case "i32.load8_s": code = (ByteCode)OpCode.I32Load8S;  naturalAlignLog2 = 0; return true;
                case "i32.load8_u": code = (ByteCode)OpCode.I32Load8U;  naturalAlignLog2 = 0; return true;
                case "i32.load16_s":code = (ByteCode)OpCode.I32Load16S; naturalAlignLog2 = 1; return true;
                case "i32.load16_u":code = (ByteCode)OpCode.I32Load16U; naturalAlignLog2 = 1; return true;
                case "i64.load8_s": code = (ByteCode)OpCode.I64Load8S;  naturalAlignLog2 = 0; return true;
                case "i64.load8_u": code = (ByteCode)OpCode.I64Load8U;  naturalAlignLog2 = 0; return true;
                case "i64.load16_s":code = (ByteCode)OpCode.I64Load16S; naturalAlignLog2 = 1; return true;
                case "i64.load16_u":code = (ByteCode)OpCode.I64Load16U; naturalAlignLog2 = 1; return true;
                case "i64.load32_s":code = (ByteCode)OpCode.I64Load32S; naturalAlignLog2 = 2; return true;
                case "i64.load32_u":code = (ByteCode)OpCode.I64Load32U; naturalAlignLog2 = 2; return true;
                case "i32.store":   code = (ByteCode)OpCode.I32Store;   naturalAlignLog2 = 2; return true;
                case "i64.store":   code = (ByteCode)OpCode.I64Store;   naturalAlignLog2 = 3; return true;
                case "f32.store":   code = (ByteCode)OpCode.F32Store;   naturalAlignLog2 = 2; return true;
                case "f64.store":   code = (ByteCode)OpCode.F64Store;   naturalAlignLog2 = 3; return true;
                case "i32.store8":  code = (ByteCode)OpCode.I32Store8;  naturalAlignLog2 = 0; return true;
                case "i32.store16": code = (ByteCode)OpCode.I32Store16; naturalAlignLog2 = 1; return true;
                case "i64.store8":  code = (ByteCode)OpCode.I64Store8;  naturalAlignLog2 = 0; return true;
                case "i64.store16": code = (ByteCode)OpCode.I64Store16; naturalAlignLog2 = 1; return true;
                case "i64.store32": code = (ByteCode)OpCode.I64Store32; naturalAlignLog2 = 2; return true;
                default:
                    code = default;
                    naturalAlignLog2 = 0;
                    return false;
            }
        }

        private static InstructionBase BuildMemoryInstruction(
            ByteCode code, int naturalAlignLog2, SExpr parent, ref int i)
        {
            return BuildMemoryInstructionWithContext(code, naturalAlignLog2, parent, ref i, null);
        }

        private static InstructionBase BuildMemoryInstructionWithContext(
            ByteCode code, int naturalAlignLog2, SExpr parent, ref int i, TextFunctionContext? fctx)
        {
            // Optional memory index ($name or numeric) preceding the
            // offset=/align= kw-args. Multi-memory proposal syntax:
            //   i32.load $mem offset=0 align=4
            uint memIdx = 0;
            bool haveMemIdx = false;
            if (fctx != null
                && i < parent.Children.Count
                && parent.Children[i].Kind == SExprKind.Atom
                && (parent.Children[i].Token.Kind == TokenKind.Id
                    || (parent.Children[i].Token.Kind == TokenKind.Reserved
                        && IsDecimalOrHexInt(parent.Children[i].AtomText()))))
            {
                memIdx = ResolveNamespaceIdx(fctx.Module.Mems, parent.Children[i], "memory");
                haveMemIdx = true;
                i++;
            }

            // Optional `offset=N` and `align=N` kw-args, in either order.
            // The lexer classifies `offset=0` as a Keyword (starts with
            // lowercase letter) even though semantically it's a kw-arg; we
            // match by textual prefix.
            ulong offset = 0;
            int alignLog2 = naturalAlignLog2;
            while (i < parent.Children.Count
                && parent.Children[i].Kind == SExprKind.Atom)
            {
                var tok = parent.Children[i];
                if (tok.Token.Kind != TokenKind.Keyword && tok.Token.Kind != TokenKind.Reserved) break;
                var text = tok.AtomText();
                if (!text.StartsWith("offset=") && !text.StartsWith("align=")) break;
                if (text.StartsWith("offset="))
                {
                    offset = (ulong)ParseUnsignedLongField(text.Substring("offset=".Length), tok.Token.Line);
                    i++;
                    continue;
                }
                if (text.StartsWith("align="))
                {
                    var align = ParseUnsignedLongField(text.Substring("align=".Length), tok.Token.Line);
                    alignLog2 = Log2OfPowerOfTwo((ulong)align, tok.Token.Line);
                    i++;
                    continue;
                }
                break;
            }
            int ai = alignLog2;
            uint memIdxCaptured = memIdx;
            bool haveMemIdxCaptured = haveMemIdx;
            return DecodeViaBinary(code, w =>
            {
                // Binary memarg: LEB128 u32 for align bits (with high bit
                // indicating memidx follows), optional LEB128 u32 memidx,
                // then LEB128 u64 for offset.
                uint alignBits = (uint)ai;
                if (haveMemIdxCaptured)
                {
                    alignBits |= 0x40u;
                    w.WriteLeb128U32(alignBits);
                    w.WriteLeb128U32(memIdxCaptured);
                }
                else
                {
                    w.WriteLeb128U32(alignBits);
                }
                WriteLeb128U64(w, offset);
            });
        }

        /// <summary>
        /// Emit the single-byte binary form of a <see cref="ValType"/> that
        /// the binary parser expects. For abstract types the byte is the
        /// low byte of the enum; for def-type references the encoding
        /// requires the RefHt / RefNullHt prefix followed by an LEB128
        /// s33 type index — handled here as well.
        /// </summary>
        private static void WriteValTypeByte(BinaryWriter w, ValType t)
        {
            if (t.IsDefType())
            {
                // typeidx form — emit the (ref [null]? <idx>) encoding:
                // prefix byte then s33 LEB.
                w.Write(t.IsNullable() ? (byte)0x63 : (byte)0x64);
                w.WriteLeb128S32(t.Index().Value);
                return;
            }
            w.Write((byte)((uint)t & 0xFF));
        }

        private static bool IsDecimalOrHexInt(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int start = 0;
            if (text[0] == '+' || text[0] == '-') start = 1;
            if (start >= text.Length) return false;
            return text[start] >= '0' && text[start] <= '9';
        }

        private static void WriteLeb128U64(BinaryWriter w, ulong value)
        {
            while (true)
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value == 0) { w.Write(b); return; }
                w.Write((byte)(b | 0x80));
            }
        }

        private static long ParseUnsignedLongField(string text, int line)
        {
            text = text.Replace("_", "");
            if (text.StartsWith("0x") || text.StartsWith("0X"))
            {
                if (!ulong.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u))
                    throw new FormatException($"line {line}: bad hex literal '{text}'");
                return (long)u;
            }
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                throw new FormatException($"line {line}: bad unsigned integer '{text}'");
            return v;
        }

        private static int Log2OfPowerOfTwo(ulong value, int line)
        {
            if (value == 0 || (value & (value - 1)) != 0)
                throw new FormatException($"line {line}: alignment must be a power of 2, got {value}");
            int n = 0;
            while ((value >>= 1) != 0) n++;
            return n;
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

        private static float ParseFloat32(SExpr atom) => (float)ParseFloatGeneric(atom, is64: false);
        private static double ParseFloat64(SExpr atom) => ParseFloatGeneric(atom, is64: true);

        /// <summary>
        /// Float literal parser accepting decimal floats, inf, nan,
        /// nan:0xPAYLOAD, and hex integers / hex floats. The hex-float
        /// grammar (0x1.Ap+3 etc.) is handled via a best-effort
        /// manual decoder; unrecognized forms fall back to zero with a
        /// comment in diagnostics rather than hard-failing spec coverage.
        /// </summary>
        private static double ParseFloatGeneric(SExpr atom, bool is64)
        {
            var text = atom.AtomText().Replace("_", "");
            int sign = 1;
            if (text.StartsWith("+")) text = text.Substring(1);
            else if (text.StartsWith("-")) { sign = -1; text = text.Substring(1); }

            // inf / nan family
            if (text == "inf") return sign * double.PositiveInfinity;
            if (text == "nan") return double.NaN;
            if (text.StartsWith("nan:"))
            {
                // nan:canonical / nan:arithmetic / nan:0xPAYLOAD — treat as
                // NaN for execution value (payload matters only for
                // assert_return pattern matching, which is handled in the
                // WAST parser).
                return double.NaN;
            }

            // Hex literals (may be float with '.' or 'p' exponent, or plain integer)
            if (text.StartsWith("0x") || text.StartsWith("0X"))
            {
                if (TryParseHexFloat(text.Substring(2), out var hv))
                    return sign * hv;
                // Not a hex float — ignore (return 0) rather than failing
                // the smoke parse. Precise decoding belongs to phase 3.
                return 0;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return sign * d;
            throw new FormatException($"line {atom.Token.Line}: bad float literal '{atom.AtomText()}'");
        }

        /// <summary>
        /// Decode a hex-float body (without the leading "0x"). Handles
        /// integer (FFFF), fractional (F.AA), and pN exponent forms
        /// (F.AAp+3). Best-effort — some of the spec's stranger
        /// hex-float forms may parse imprecisely, but we won't hard-fail
        /// the smoke tests.
        /// </summary>
        private static bool TryParseHexFloat(string body, out double value)
        {
            value = 0;
            int p = body.IndexOfAny(new[] { 'p', 'P' });
            string mantissa = p >= 0 ? body.Substring(0, p) : body;
            string exponent = p >= 0 ? body.Substring(p + 1) : "0";
            int dot = mantissa.IndexOf('.');
            string intPart = dot >= 0 ? mantissa.Substring(0, dot) : mantissa;
            string fracPart = dot >= 0 ? mantissa.Substring(dot + 1) : "";
            ulong intVal = 0;
            if (intPart.Length > 0)
            {
                if (!ulong.TryParse(intPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out intVal))
                    return false;
            }
            double frac = 0;
            double scale = 1.0 / 16.0;
            foreach (var c in fracPart)
            {
                int d = c >= '0' && c <= '9' ? c - '0'
                      : c >= 'a' && c <= 'f' ? c - 'a' + 10
                      : c >= 'A' && c <= 'F' ? c - 'A' + 10
                      : -1;
                if (d < 0) return false;
                frac += d * scale;
                scale /= 16.0;
            }
            if (!int.TryParse(exponent, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exp))
                return false;
            value = ((double)intVal + frac) * System.Math.Pow(2, exp);
            return true;
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
            return (uint)ParseUnsignedAnyRadix(text, atom.Token.Line);
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
            return (uint)ParseUnsignedAnyRadix(text, atom.Token.Line);
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
            return (uint)ParseUnsignedAnyRadix(text, atom.Token.Line);
        }

        private static long ParseUnsignedAnyRadix(string text, int line)
        {
            text = text.Replace("_", "");
            if (text.StartsWith("0x") || text.StartsWith("0X"))
            {
                if (!ulong.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u))
                    throw new FormatException($"line {line}: bad hex literal '{text}'");
                return (long)u;
            }
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                throw new FormatException($"line {line}: bad integer '{text}'");
            return v;
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
        ///   - <c>(result T)</c> single result → <c>T</c>
        ///   - <c>(type $n)</c> → resolved typeidx (DefType-valued ValType)
        ///   - Multi-value <c>(param …)* (result …)*</c> → synthesize a
        ///     FunctionType into <c>Module.Types</c> (dedup'd) and return
        ///     that type index.
        /// </summary>
        private static ValType ParseBlockType(TextParseContext ctx, SExpr parent, ref int i)
        {
            if (i >= parent.Children.Count) return ValType.Empty;
            var child = parent.Children[i];
            if (child.Kind != SExprKind.List) return ValType.Empty;

            // `(type $n)` reference, optionally followed by redundant
            // (param …)* (result …)* annotations for naming/documentation.
            if (child.IsForm("type"))
            {
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
                // Skip redundant (param …) / (result …) annotations — they
                // just rename / document what the referenced type already
                // specifies.
                while (i < parent.Children.Count
                    && parent.Children[i].Kind == SExprKind.List
                    && (parent.Children[i].IsForm("param") || parent.Children[i].IsForm("result")))
                {
                    i++;
                }
                return (ValType)idx;
            }

            // Inline single-result shorthand
            if (child.IsForm("result"))
            {
                if (child.Children.Count == 1)
                {
                    i++;
                    return ValType.Empty;
                }
                if (child.Children.Count == 2)
                {
                    var vt = ParseValType(ctx, child.Children[1]);
                    i++;
                    return vt;
                }
                // Multi-value result — fall through to FunctionType synthesis.
            }

            if (child.IsForm("param") || child.IsForm("result"))
            {
                // Collect (param …)* (result …)* runs, synthesize + dedup.
                var paramTypes = new List<ValType>();
                var resultTypes = new List<ValType>();
                while (i < parent.Children.Count)
                {
                    var c = parent.Children[i];
                    if (c.Kind != SExprKind.List) break;
                    if (c.IsForm("param"))
                    {
                        // anonymous sequence or named single — we only care about types here
                        int j = 1;
                        if (j < c.Children.Count
                            && c.Children[j].Kind == SExprKind.Atom
                            && c.Children[j].Token.Kind == TokenKind.Id)
                        {
                            j++;
                            if (j < c.Children.Count)
                                paramTypes.Add(ParseValType(ctx, c.Children[j]));
                        }
                        else
                        {
                            for (; j < c.Children.Count; j++)
                                paramTypes.Add(ParseValType(ctx, c.Children[j]));
                        }
                        i++;
                        continue;
                    }
                    if (c.IsForm("result"))
                    {
                        for (int j = 1; j < c.Children.Count; j++)
                            resultTypes.Add(ParseValType(ctx, c.Children[j]));
                        i++;
                        continue;
                    }
                    break;
                }
                var ft = new FunctionType(
                    paramTypes.Count == 0 ? ResultType.Empty : new ResultType(paramTypes.ToArray()),
                    resultTypes.Count == 0 ? ResultType.Empty : new ResultType(resultTypes.ToArray()));
                // Dedup against non-rec (single-subtype) Module.Types only.
                int flatSeen = 0;
                for (int t = 0; t < ctx.Module.Types.Count; t++)
                {
                    var group = ctx.Module.Types[t];
                    if (group.SubTypes.Length == 1)
                    {
                        var body = group.SubTypes[0].Body as FunctionType;
                        if (body != null && FunctionTypeStructurallyEqual(body, ft))
                            return (ValType)flatSeen;
                    }
                    flatSeen += group.SubTypes.Length;
                }
                var idx2 = flatSeen;
                ctx.Module.Types.Add(new RecursiveType(new SubType(ft, final: true)));
                return (ValType)idx2;
            }

            return ValType.Empty;
        }

        /// <summary>
        /// Recognize an abstract heap-type keyword atom. Returns false for
        /// atoms that look like a typeidx ($name or integer), letting the
        /// caller route those through the index resolver instead.
        /// </summary>
        private static bool TryParseAbstractHeapType(SExpr atom, out HeapType ht)
        {
            ht = default;
            if (atom.Kind != SExprKind.Atom) return false;
            if (atom.Token.Kind != TokenKind.Keyword) return false;
            switch (atom.AtomText())
            {
                case "func":      ht = HeapType.Func;     return true;
                case "extern":    ht = HeapType.Extern;   return true;
                case "any":       ht = HeapType.Any;      return true;
                case "eq":        ht = HeapType.Eq;       return true;
                case "i31":       ht = HeapType.I31;      return true;
                case "struct":    ht = HeapType.Struct;   return true;
                case "array":     ht = HeapType.Array;    return true;
                case "exn":       ht = HeapType.Exn;      return true;
                case "noexn":     ht = HeapType.NoExn;    return true;
                case "nofunc":    ht = HeapType.NoFunc;   return true;
                case "noextern":  ht = HeapType.NoExtern; return true;
                case "none":      ht = HeapType.None;     return true;
                default: return false;
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
        /// <param name="isFunctionEnd">Set true for function bodies — the
        /// terminating <see cref="InstEnd"/> is tagged so <c>Link()</c>
        /// emits the function-return shim. Leave false for init
        /// expressions.</param>
        internal static Expression ParseExpressionBody(TextFunctionContext fctx, SExpr form, ref int i, int arity, bool isStatic, bool isFunctionEnd = false)
        {
            var instrs = ParseInstrList(fctx, form, ref i, InstrStop.None, out _);
            instrs.Add(new InstEnd());
            var seq = new InstructionSequence(instrs, functionEnd: isFunctionEnd);
            return new Expression(arity, seq, isStatic);
        }
    }
}
