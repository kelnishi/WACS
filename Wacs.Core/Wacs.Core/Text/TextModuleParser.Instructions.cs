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

            // Pending hint from a `(@metadata.code.branch_hint "\xx")`
            // annotation on the previous sibling. Attached to the
            // next-emitted `if` / `br_if` instruction. Per the
            // proposal, "duplicate annotation" (two in a row) and
            // "invalid target" (next instruction isn't if/br_if) are
            // both errors. Only collected when Module.ParseBranchHints
            // is on; otherwise annotations are silently skipped to
            // save parse work for interpreter consumers.
            BranchHint? pendingHint = null;
            int pendingHintLine = 0;

            while (i < parent.Children.Count)
            {
                var node = parent.Children[i];

                // Annotation: `(@name args…)` — folded form whose head
                // is a Reserved token starting with '@'. Recognized
                // shapes are intercepted here; unrecognized shapes
                // silently skipped (forward-compat with future
                // annotations the parser doesn't yet know about).
                if (node.Kind == SExprKind.List && IsAnnotationNode(node))
                {
                    HandleAnnotation(node, ref pendingHint, ref pendingHintLine);
                    i++;
                    continue;
                }

                int beforeCount = result.Count;
                // Capture source position of the form we're about to
                // parse. After ParsePlainInstruction / ParseFoldedInstruction
                // returns, every newly-added InstructionBase in
                // result[beforeCount..] is stamped with this token's
                // (line, col, offset). For folded forms that emit
                // multiple instructions (operands + operator), they
                // share the outermost form's source position —
                // accurate when on a single line, a fair approximation
                // otherwise. Consumed by `WasmStackTrace` to resolve
                // traps to source coords without re-rendering the
                // module.
                int sourceLine = node.Token.Line;
                int sourceCol = node.Token.Column;
                int sourceOffset = node.Token.Start;
                if (node.Kind == SExprKind.Atom)
                {
                    if (node.Token.Kind == TokenKind.Keyword)
                    {
                        var kw = node.AtomText();
                        if (stop != InstrStop.None)
                        {
                            if (kw == "end" && (stop & InstrStop.End) != 0)
                            {
                                if (pendingHint.HasValue)
                                    throw new FormatException(
                                        $"line {pendingHintLine}: @metadata.code.branch_hint annotation: invalid target (no following if/br_if before 'end')");
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
                                if (pendingHint.HasValue)
                                    throw new FormatException(
                                        $"line {pendingHintLine}: @metadata.code.branch_hint annotation: invalid target (no following if/br_if before 'else')");
                                stopKeyword = "else";
                                return result;   // leave 'else' for caller
                            }
                        }
                        ParsePlainInstruction(fctx, parent, ref i, kw, result);
                    }
                    else
                    {
                        throw new FormatException(
                            $"line {node.Token.Line}: unexpected {node.Token.Kind} '{node.AtomText()}' in instruction list");
                    }
                }
                else
                {
                    // Folded form
                    ParseFoldedInstruction(fctx, node, result);
                    i++;
                }

                // Memoize source positions on every instruction the
                // form emitted. Lazy-allocates Module.SourcePositions
                // on first call; later look-ups via the side-table
                // give O(1) resolution from inst → source coords.
                if (result.Count > beforeCount)
                {
                    var pos = new SourcePos(sourceLine, sourceCol, sourceOffset);
                    var module = fctx.Module.Module;
                    for (int k = beforeCount; k < result.Count; k++)
                        module.RecordSourcePosition(result[k], pos);
                }

                // If a hint is pending, attach it to the if/br_if among
                // the newly-added instructions. Folded forms (e.g.
                // (if (cond) (then ...) (else ...))) emit the operand
                // sub-instructions first, then the if itself; we walk
                // forward and attach to the first hint-eligible match.
                if (pendingHint.HasValue)
                {
                    InstructionBase? target = null;
                    for (int k = beforeCount; k < result.Count; k++)
                    {
                        if (result[k] is InstIf || result[k] is InstBranchIf)
                        {
                            target = result[k];
                            break;
                        }
                    }
                    if (target == null)
                        throw new FormatException(
                            $"line {pendingHintLine}: @metadata.code.branch_hint annotation: invalid target (preceding instruction not if/br_if)");

                    var hints = EnsureBranchHints(fctx);
                    hints.ByInstruction[target] = pendingHint.Value;
                    pendingHint = null;
                }
            }

            if (pendingHint.HasValue)
                throw new FormatException(
                    $"line {pendingHintLine}: @metadata.code.branch_hint annotation: invalid target (end of instruction list)");

            return result;
        }

        // ---- Annotation helpers --------------------------------------------

        private static bool IsAnnotationNode(SExpr node)
        {
            if (node.Kind != SExprKind.List) return false;
            var head = node.Head;
            return head != null
                && head.Kind == SExprKind.Atom
                && head.Token.Kind == TokenKind.Reserved
                && head.AtomText().StartsWith("@");
        }

        private static void HandleAnnotation(
            SExpr node, ref BranchHint? pendingHint, ref int pendingHintLine)
        {
            var head = node.Head!;
            var name = head.AtomText();

            // Other annotation shapes (round-trip metadata, custom
            // tooling) are silently passed through — they don't
            // affect IL emission.
            if (name != "@metadata.code.branch_hint") return;

            // Gate on the parser feature flag. When off, drop the
            // annotation on the floor — interpreter consumers don't
            // need branch hints, no point spending parse work.
            if (!BinaryModuleParser.ParseBranchHints) return;

            if (pendingHint.HasValue)
                throw new FormatException(
                    $"line {node.Token.Line}: @metadata.code.branch_hint annotation: duplicate annotation");

            // Payload: a single string atom whose decoded bytes are
            // the hint data. Spec pins length = 1 today, but the
            // proposal encodes it as vec(byte) so future revisions
            // can extend.
            if (node.Children.Count != 2)
                throw new FormatException(
                    $"line {node.Token.Line}: @metadata.code.branch_hint annotation: expected exactly one string payload");
            var payloadNode = node.Children[1];
            if (payloadNode.Kind != SExprKind.Atom || payloadNode.Token.Kind != TokenKind.String)
                throw new FormatException(
                    $"line {payloadNode.Token.Line}: @metadata.code.branch_hint annotation: payload must be a string literal");

            var data = node.Lexer.DecodeString(payloadNode.Token);
            // Synthetic ByteOffset = 0; the WAT path uses ByInstruction
            // for lookup, the offset is unused.
            pendingHint = new BranchHint(0, data);
            pendingHintLine = node.Token.Line;
        }

        private static Module.BranchHintMap EnsureBranchHints(TextFunctionContext fctx)
        {
            var module = fctx.Module.Module;
            if (module.BranchHints == null)
                module.BranchHints = new Module.BranchHintMap();
            return module.BranchHints;
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
                case "resume":
                    ParseResumeFolded(fctx, node, output, isThrow: false);
                    return;
                case "resume_throw":
                    ParseResumeFolded(fctx, node, output, isThrow: true);
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
        /// Folded form of <c>resume</c> and <c>resume_throw</c>:
        /// <c>(resume $ct (on $tag $label)* operand*)</c> /
        /// <c>(resume_throw $ct $tag (on $tag $label)* operand*)</c>.
        /// Lowers to the binary form via <c>DecodeViaBinary</c>.
        /// </summary>
        private static void ParseResumeFolded(
            TextFunctionContext fctx, SExpr node, List<InstructionBase> output, bool isThrow)
        {
            int i = 1;
            uint contTypeIdx = ResolveNamespaceIdx(
                fctx.Module.Types, node.Children[i++], "type");
            uint throwTagIdx = 0;
            if (isThrow)
            {
                throwTagIdx = ResolveNamespaceIdx(
                    fctx.Module.Tags, node.Children[i++], "tag");
            }

            // Collect on-tag handler clauses: (on $tag $label) and
            // (on $tag $label switch).
            var handlers = new List<(uint tag, uint label, bool onSwitch)>();
            while (i < node.Children.Count
                   && node.Children[i].Kind == SExprKind.List
                   && node.Children[i].Head != null
                   && node.Children[i].Head!.Token.Kind == TokenKind.Keyword
                   && node.Children[i].Head!.AtomText() == "on")
            {
                var clause = node.Children[i++];
                if (clause.Children.Count < 3)
                    throw new FormatException(
                        $"line {clause.Token.Line}: (on …) expects $tag $label [switch]");
                uint tagIdx = ResolveNamespaceIdx(
                    fctx.Module.Tags, clause.Children[1], "tag");
                uint labelIdx = ResolveLabel(fctx, clause.Children[2]);
                bool onSwitch = clause.Children.Count > 3
                                && clause.Children[3].Kind == SExprKind.Atom
                                && clause.Children[3].AtomText() == "switch";
                handlers.Add((tagIdx, labelIdx, onSwitch));
            }

            // Remaining children are operand sub-forms — they
            // execute first so their results land on the operand
            // stack below the resume's immediates.
            while (i < node.Children.Count)
            {
                var child = node.Children[i++];
                if (child.Kind != SExprKind.List)
                    throw new FormatException(
                        $"line {child.Token.Line}: folded resume operand must be a sub-form");
                ParseFoldedInstruction(fctx, child, output);
            }

            var inst = DecodeViaBinary(
                isThrow ? (ByteCode)OpCode.ResumeThrow : (ByteCode)OpCode.Resume,
                w =>
                {
                    w.WriteLeb128U32(contTypeIdx);
                    if (isThrow) w.WriteLeb128U32(throwTagIdx);
                    w.WriteLeb128U32((uint)handlers.Count);
                    foreach (var (tag, label, onSwitch) in handlers)
                    {
                        w.Write((byte)(onSwitch ? 0x01 : 0x00));
                        w.WriteLeb128U32(tag);
                        w.WriteLeb128U32(label);
                    }
                });
            output.Add(inst);
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

                InstructionSequence ifSeq;
                InstIf ifInst;
                if (elseForm != null)
                {
                    thenInner.Add(new InstElse());
                    ifSeq = new InstructionSequence(thenInner);

                    var elseBody = new List<InstructionBase>();
                    int ej = 1;
                    var els = ParseInstrList(fctx, elseForm, ref ej, InstrStop.None, out _);
                    elseBody.AddRange(els);
                    elseBody.Add(new InstEnd());
                    // NOTE: elseBody intentionally does not start with
                    // InstElse — the InstElse divider sits at the end of
                    // thenInner, per the binary parser's shape.
                    var elseSeq = new InstructionSequence(elseBody);
                    ifInst = (InstIf)new InstIf().Immediate(blockType, ifSeq, elseSeq);
                }
                else
                {
                    thenInner.Add(new InstEnd());
                    ifSeq = new InstructionSequence(thenInner);
                    // No-else path: route through the dedicated overload
                    // so ElseBlock stays at Block.Empty (type Empty),
                    // matching the binary parser. Crucial for the
                    // validator to catch missing-else type-mismatch errors.
                    ifInst = (InstIf)new InstIf().Immediate(blockType, ifSeq);
                }
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
                    bool hasElse;
                    try
                    {
                        thenBody = ParseInstrList(fctx, parent, ref i, InstrStop.EndOrElse, out var stopKw);
                        hasElse = stopKw == "else";
                        if (hasElse)
                        {
                            i++;
                            if (i < parent.Children.Count
                                && parent.Children[i].Kind == SExprKind.Atom
                                && parent.Children[i].Token.Kind == TokenKind.Id)
                                i++;
                            // ElseBlock body does NOT include a leading
                            // InstElse — the InstElse sits at the end of
                            // the IfBlock body as the divider marker
                            // (matches the binary parser's shape).
                            elseBody = ParseInstrList(fctx, parent, ref i, InstrStop.End, out _);
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
                    // The "has else" decision is based on whether the
                    // source had an `else` keyword — an empty else body
                    // (`if ... else end`) still counts as an else arm.
                    var ifSeq = new List<InstructionBase>(thenBody);
                    InstIf ifInst;
                    if (hasElse)
                    {
                        ifSeq.Add(new InstElse());
                        elseBody.Add(new InstEnd());
                        var elseSeq = new InstructionSequence(elseBody);
                        ifInst = (InstIf)new InstIf().Immediate(blockType,
                            new InstructionSequence(ifSeq), elseSeq);
                    }
                    else
                    {
                        ifSeq.Add(new InstEnd());
                        // No-else: leave ElseBlock at Block.Empty so the
                        // validator catches missing-else type mismatches.
                        ifInst = (InstIf)new InstIf().Immediate(blockType,
                            new InstructionSequence(ifSeq));
                    }
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
                case "cont.new":
                {
                    uint ti = ResolveNamespaceIdx(fctx.Module.Types, ReadImmIdxAtom(parent, ref i, kw), "type");
                    return DecodeViaBinary((ByteCode)OpCode.ContNew, w => w.WriteLeb128U32(ti));
                }
                case "cont.bind":
                {
                    uint ti1 = ResolveNamespaceIdx(fctx.Module.Types, ReadImmIdxAtom(parent, ref i, kw), "type");
                    uint ti2 = ResolveNamespaceIdx(fctx.Module.Types, ReadImmIdxAtom(parent, ref i, kw), "type");
                    return DecodeViaBinary((ByteCode)OpCode.ContBind,
                        w => { w.WriteLeb128U32(ti1); w.WriteLeb128U32(ti2); });
                }
                case "suspend":
                {
                    uint tagIdx = ResolveNamespaceIdx(fctx.Module.Tags, ReadImmIdxAtom(parent, ref i, kw), "tag");
                    return DecodeViaBinary((ByteCode)OpCode.Suspend, w => w.WriteLeb128U32(tagIdx));
                }
                case "switch":
                {
                    uint ti = ResolveNamespaceIdx(fctx.Module.Types, ReadImmIdxAtom(parent, ref i, kw), "type");
                    uint tagIdx = ResolveNamespaceIdx(fctx.Module.Tags, ReadImmIdxAtom(parent, ref i, kw), "tag");
                    return DecodeViaBinary((ByteCode)OpCode.Switch,
                        w => { w.WriteLeb128U32(ti); w.WriteLeb128U32(tagIdx); });
                }
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

            // Atomic memarg-carrying ops (threads proposal). Natural
            // alignment is the exact access width; the rest of the memarg
            // plumbing is identical to non-atomic memory ops.
            if (TryGetAtomicMemoryOpcode(kw, out var atomCode, out var atomNaturalAlign))
                return BuildMemoryInstructionWithContext(atomCode, atomNaturalAlign, parent, ref i, fctx);

            // atomic.fence — no memarg, but the binary form carries a
            // reserved 0x00 byte. Synthesize it so the factory's Parse
            // succeeds.
            if (kw == "atomic.fence")
                return DecodeViaBinary((ByteCode)AtomCode.AtomicFence, w => w.Write((byte)0));

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

            // GC (FB prefix). Dispatch by immediate shape: typeidx, typeidx
            // + dataidx/elemidx/fieldidx/count, heaptype (with nullable
            // bit), heaptype-pair-with-flags-byte (br_on_cast variants), or
            // zero-immediate (ref.i31 / i31.get_* / array.len /
            // any.convert_extern / extern.convert_any).
            if (Mnemonics.TryLookup(kw, out var bcGc) && bcGc.x00 == OpCode.FB)
                return ParseGcInstruction(bcGc, parent, ref i, fctx);

            // SIMD (FD prefix). The factory has every SimdCode wired; the
            // text parser distinguishes immediate shapes here:
            //   - memarg-only   : v128.load/store/load_splat/load_extend/load_zero
            //   - memarg + lane : v128.load*_lane, v128.store*_lane
            //   - 16 lane bytes : i8x16.shuffle
            //   - V128 literal  : v128.const
            //   - lane index    : {i,f}{8x16,16x8,32x4,64x2}.{extract,replace}_lane
            //   - zero-immediate: everything else (arithmetic/comparison/etc.)
            if (Mnemonics.TryLookup(kw, out var bcSimd) && bcSimd.x00 == OpCode.FD)
                return ParseSimdInstruction(bcSimd, parent, ref i, fctx);

            throw new NotSupportedException(
                $"line {parent.Token.Line}: instruction '{kw}' not yet supported by the text parser (phase 1.4 scope)");
        }

        // ---- GC -----------------------------------------------------------

        private static InstructionBase ParseGcInstruction(
            ByteCode bc, SExpr parent, ref int i, TextFunctionContext fctx)
        {
            var gc = bc.xFB;
            switch (gc)
            {
                // Zero-immediate
                case GcCode.RefI31:
                case GcCode.I31GetS:
                case GcCode.I31GetU:
                case GcCode.AnyConvertExtern:
                case GcCode.ExternConvertAny:
                case GcCode.ArrayLen:
                    return SpecFactory.Factory.CreateInstruction(bc);

                // typeidx
                case GcCode.StructNew:
                case GcCode.StructNewDefault:
                case GcCode.ArrayNew:
                case GcCode.ArrayNewDefault:
                case GcCode.ArrayGet:
                case GcCode.ArrayGetS:
                case GcCode.ArrayGetU:
                case GcCode.ArraySet:
                case GcCode.ArrayFill:
                {
                    uint t = ResolveNamespaceIdx(fctx.Module.Types,
                        ReadImmIdxAtom(parent, ref i, gc.GetMnemonic()), "type");
                    return DecodeViaBinary(bc, w => w.WriteLeb128U32(t));
                }

                // typeidx + count
                case GcCode.ArrayNewFixed:
                {
                    uint t = ResolveNamespaceIdx(fctx.Module.Types,
                        ReadImmIdxAtom(parent, ref i, "array.new_fixed"), "type");
                    long n = ParseUnsignedInt(ReadImmIdxAtom(parent, ref i, "array.new_fixed"));
                    uint nC = (uint)n;
                    return DecodeViaBinary(bc, w => { w.WriteLeb128U32(t); w.WriteLeb128U32(nC); });
                }

                // typeidx + dataidx
                case GcCode.ArrayNewData:
                case GcCode.ArrayInitData:
                {
                    uint t = ResolveNamespaceIdx(fctx.Module.Types,
                        ReadImmIdxAtom(parent, ref i, gc.GetMnemonic()), "type");
                    uint d = ResolveNamespaceIdx(fctx.Module.Datas,
                        ReadImmIdxAtom(parent, ref i, gc.GetMnemonic()), "data");
                    return DecodeViaBinary(bc, w => { w.WriteLeb128U32(t); w.WriteLeb128U32(d); });
                }

                // typeidx + elemidx
                case GcCode.ArrayNewElem:
                case GcCode.ArrayInitElem:
                {
                    uint t = ResolveNamespaceIdx(fctx.Module.Types,
                        ReadImmIdxAtom(parent, ref i, gc.GetMnemonic()), "type");
                    uint e = ResolveNamespaceIdx(fctx.Module.Elems,
                        ReadImmIdxAtom(parent, ref i, gc.GetMnemonic()), "elem");
                    return DecodeViaBinary(bc, w => { w.WriteLeb128U32(t); w.WriteLeb128U32(e); });
                }

                // typeidx + typeidx (array.copy dst src)
                case GcCode.ArrayCopy:
                {
                    uint dt = ResolveNamespaceIdx(fctx.Module.Types,
                        ReadImmIdxAtom(parent, ref i, "array.copy"), "type");
                    uint st = ResolveNamespaceIdx(fctx.Module.Types,
                        ReadImmIdxAtom(parent, ref i, "array.copy"), "type");
                    return DecodeViaBinary(bc, w => { w.WriteLeb128U32(dt); w.WriteLeb128U32(st); });
                }

                // typeidx + fieldidx (struct.get / struct.set)
                case GcCode.StructGet:
                case GcCode.StructGetS:
                case GcCode.StructGetU:
                case GcCode.StructSet:
                {
                    var tAtom = ReadImmIdxAtom(parent, ref i, gc.GetMnemonic());
                    uint t = ResolveNamespaceIdx(fctx.Module.Types, tAtom, "type");
                    var fAtom = ReadImmIdxAtom(parent, ref i, gc.GetMnemonic());
                    uint f;
                    if (fAtom.Token.Kind == TokenKind.Id)
                    {
                        if (!fctx.Module.StructFieldNames.TryGetValue((int)t, out var ftab)
                            || !ftab.TryResolve(fAtom.AtomText(), out var fIdx))
                            throw new FormatException(
                                $"line {fAtom.Token.Line}: unknown field {fAtom.AtomText()} in struct type {t}");
                        f = (uint)fIdx;
                    }
                    else
                    {
                        f = (uint)ParseUnsignedInt(fAtom);
                    }
                    return DecodeViaBinary(bc, w => { w.WriteLeb128U32(t); w.WriteLeb128U32(f); });
                }

                // ref.test / ref.cast — single reftype
                case GcCode.RefTest:
                case GcCode.RefTestNull:
                case GcCode.RefCast:
                case GcCode.RefCastNull:
                {
                    var (heapByte, nullable) = ParseRefTypeForCast(fctx.Module, parent, ref i, gc.GetMnemonic());
                    // Pick the right opcode variant based on parsed nullability.
                    var actualBc = nullable
                        ? (gc == GcCode.RefTest || gc == GcCode.RefTestNull
                            ? (ByteCode)GcCode.RefTestNull : (ByteCode)GcCode.RefCastNull)
                        : (gc == GcCode.RefTest || gc == GcCode.RefTestNull
                            ? (ByteCode)GcCode.RefTest : (ByteCode)GcCode.RefCast);
                    return DecodeViaBinary(actualBc, w => WriteHeapTypeBytes(w, heapByte));
                }

                // br_on_cast / br_on_cast_fail — flags byte + label + 2 reftypes
                case GcCode.BrOnCast:
                case GcCode.BrOnCastFail:
                {
                    uint label = (uint)ResolveLabel(fctx,
                        ReadImmIdxAtom(parent, ref i, gc.GetMnemonic()));
                    var (h1, n1) = ParseRefTypeForCast(fctx.Module, parent, ref i, gc.GetMnemonic());
                    var (h2, n2) = ParseRefTypeForCast(fctx.Module, parent, ref i, gc.GetMnemonic());
                    byte flags = 0;
                    if (n1) flags |= 0b01; // CastFlags.NullEmpty
                    if (n2) flags |= 0b10; // CastFlags.EmptyNull
                    return DecodeViaBinary(bc, w =>
                    {
                        w.Write(flags);
                        w.WriteLeb128U32(label);
                        WriteHeapTypeBytes(w, h1);
                        WriteHeapTypeBytes(w, h2);
                    });
                }
            }
            throw new NotSupportedException(
                $"line {parent.Token.Line}: GC instruction '{bc.GetMnemonic()}' has no WAT dispatch yet");
        }

        /// <summary>
        /// Parse a (ref null? heaptype) form OR a bare abstract-heaptype
        /// keyword (e.g. <c>funcref</c>) used as a reftype shorthand. Returns
        /// the heaptype (as a value to be written via
        /// <see cref="WriteHeapTypeBytes"/>) plus its nullability bit.
        /// </summary>
        private static (ValType heapType, bool nullable) ParseRefTypeForCast(
            TextParseContext ctx, SExpr parent, ref int i, string opName)
        {
            if (i >= parent.Children.Count)
                throw new FormatException(
                    $"line {parent.Token.Line}: {opName} expects a reftype operand");
            var child = parent.Children[i];
            i++;

            // List form: (ref null? <heap>)
            if (child.Kind == SExprKind.List && child.IsForm("ref"))
            {
                int j = 1;
                bool nullable = false;
                if (j < child.Children.Count
                    && child.Children[j].Kind == SExprKind.Atom
                    && child.Children[j].Token.Kind == TokenKind.Keyword
                    && child.Children[j].AtomText() == "null")
                {
                    nullable = true;
                    j++;
                }
                if (j >= child.Children.Count)
                    throw new FormatException(
                        $"line {child.Token.Line}: (ref …) missing heap type");
                var ht = ParseHeapType(ctx, child.Children[j]);
                return (ht, nullable);
            }

            // Atom form: bare reftype keyword (funcref/externref/anyref/etc.)
            if (child.Kind == SExprKind.Atom)
            {
                if (TryParseRefShorthand(child.AtomText(), out var rt))
                {
                    bool nullable = (rt & ValType.Nullable) != 0;
                    // The shorthand ValType already encodes the abstract
                    // heaptype; WriteHeapTypeBytes pulls the heap byte via
                    // GetHeapType().
                    return (rt, nullable);
                }
            }
            throw new FormatException(
                $"line {child.Token.Line}: {opName} expects a reftype operand");
        }

        /// <summary>
        /// Encode a heaptype as the binary parser expects: either a single
        /// negative byte (abstract heap types) or a non-negative LEB128 s33
        /// (typeidx).
        /// </summary>
        private static void WriteHeapTypeBytes(BinaryWriter w, ValType heapType)
        {
            // For abstract heap types the low byte of the ValType IS the
            // SLEB128 negative byte the binary parser expects.
            if (heapType.IsDefType())
            {
                int idx = heapType.Index().Value;
                WriteLeb128S33(w, idx);
                return;
            }
            // Encode the abstract heaptype byte directly.
            byte b = (byte)((uint)heapType.GetHeapType());
            w.Write(b);
        }

        private static void WriteLeb128S33(BinaryWriter w, int value)
        {
            // Signed LEB128, fits in 33 bits.
            bool more = true;
            int v = value;
            while (more)
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                bool signBit = (b & 0x40) != 0;
                if ((v == 0 && !signBit) || (v == -1 && signBit))
                    more = false;
                else
                    b |= 0x80;
                w.Write(b);
            }
        }

        // ---- SIMD ---------------------------------------------------------

        private static InstructionBase ParseSimdInstruction(
            ByteCode bc, SExpr parent, ref int i, TextFunctionContext fctx)
        {
            var sc = bc.xFD;

            // memarg-only memory ops
            if (TryGetSimdMemoryNaturalAlign(sc, out var memAlign))
                return BuildMemoryInstructionWithContext(bc, memAlign, parent, ref i, fctx);

            // memarg + 1-byte lane index (load*_lane / store*_lane)
            if (TryGetSimdLaneMemoryNaturalAlign(sc, out var laneMemAlign))
                return BuildSimdLaneMemoryInstruction(bc, laneMemAlign, parent, ref i, fctx);

            // v128.const — typed lane literals → 16-byte vector
            if (sc == SimdCode.V128Const)
                return BuildV128ConstInstruction(bc, parent, ref i);

            // i8x16.shuffle — 16 lane indices (each 0..31)
            if (sc == SimdCode.I8x16Shuffle)
                return BuildShuffleInstruction(bc, parent, ref i);

            // {i,f}{shape}.{extract,replace}_lane — single lane-index byte
            if (TryGetSimdLaneOpMaxLane(sc, out var maxLane))
                return BuildSimdLaneInstruction(bc, maxLane, parent, ref i);

            // Everything else: zero-immediate; factory has the instance ready.
            return SpecFactory.Factory.CreateInstruction(bc);
        }

        private static bool TryGetSimdMemoryNaturalAlign(SimdCode sc, out int naturalAlignLog2)
        {
            // Natural alignment matches the binary parser's MemArg conventions.
            switch (sc)
            {
                case SimdCode.V128Load:        naturalAlignLog2 = 4; return true; // 16-byte
                case SimdCode.V128Store:       naturalAlignLog2 = 4; return true;
                case SimdCode.V128Load8x8S:
                case SimdCode.V128Load8x8U:
                case SimdCode.V128Load16x4S:
                case SimdCode.V128Load16x4U:
                case SimdCode.V128Load32x2S:
                case SimdCode.V128Load32x2U:   naturalAlignLog2 = 3; return true; // 8-byte
                case SimdCode.V128Load8Splat:  naturalAlignLog2 = 0; return true;
                case SimdCode.V128Load16Splat: naturalAlignLog2 = 1; return true;
                case SimdCode.V128Load32Splat: naturalAlignLog2 = 2; return true;
                case SimdCode.V128Load64Splat: naturalAlignLog2 = 3; return true;
                case SimdCode.V128Load32Zero:  naturalAlignLog2 = 2; return true;
                case SimdCode.V128Load64Zero:  naturalAlignLog2 = 3; return true;
                default:                       naturalAlignLog2 = 0; return false;
            }
        }

        private static bool TryGetSimdLaneMemoryNaturalAlign(SimdCode sc, out int naturalAlignLog2)
        {
            switch (sc)
            {
                case SimdCode.V128Load8Lane:
                case SimdCode.V128Store8Lane:  naturalAlignLog2 = 0; return true;
                case SimdCode.V128Load16Lane:
                case SimdCode.V128Store16Lane: naturalAlignLog2 = 1; return true;
                case SimdCode.V128Load32Lane:
                case SimdCode.V128Store32Lane: naturalAlignLog2 = 2; return true;
                case SimdCode.V128Load64Lane:
                case SimdCode.V128Store64Lane: naturalAlignLog2 = 3; return true;
                default:                       naturalAlignLog2 = 0; return false;
            }
        }

        private static bool TryGetSimdLaneOpMaxLane(SimdCode sc, out int maxLane)
        {
            // Lane count = 128 / shape-bit-width. The validator also enforces
            // this; we duplicate it here to give a clean parse-time error.
            switch (sc)
            {
                case SimdCode.I8x16ExtractLaneS:
                case SimdCode.I8x16ExtractLaneU:
                case SimdCode.I8x16ReplaceLane:  maxLane = 16; return true;
                case SimdCode.I16x8ExtractLaneS:
                case SimdCode.I16x8ExtractLaneU:
                case SimdCode.I16x8ReplaceLane:  maxLane = 8;  return true;
                case SimdCode.I32x4ExtractLane:
                case SimdCode.I32x4ReplaceLane:
                case SimdCode.F32x4ExtractLane:
                case SimdCode.F32x4ReplaceLane:  maxLane = 4;  return true;
                case SimdCode.I64x2ExtractLane:
                case SimdCode.I64x2ReplaceLane:
                case SimdCode.F64x2ExtractLane:
                case SimdCode.F64x2ReplaceLane:  maxLane = 2;  return true;
                default:                         maxLane = 0;  return false;
            }
        }

        private static InstructionBase BuildSimdLaneMemoryInstruction(
            ByteCode bc, int naturalAlignLog2, SExpr parent, ref int i, TextFunctionContext fctx)
        {
            // Syntax: <memidx>? offset=N? align=N? <laneidx> [folded operand…]
            //
            // The lane index is ALWAYS the last bare integer before any
            // folded sub-form. Earlier bare integers are the optional
            // multi-memory memidx (the natural-alignment memory is index 0).
            // Wabt also accepts a leading lane index when no memidx is
            // present — handled by inspecting how many bare integers
            // appear before the next non-bare-int token.
            uint memIdx = 0;
            bool haveMemIdx = false;
            ulong offset = 0;
            int alignLog2 = naturalAlignLog2;
            byte? laneIndex = null;

            // Walk forward over the immediate atoms (memidx + kw-args + lane),
            // stopping at any List (folded operand) or unrelated keyword.
            // Collect bare-int atoms in order; the LAST is the lane index,
            // anything earlier is memidx.
            var bareInts = new List<SExpr>();
            while (i < parent.Children.Count
                && parent.Children[i].Kind == SExprKind.Atom)
            {
                var tok = parent.Children[i];
                // kw-arg?
                if (tok.Token.Kind == TokenKind.Keyword || tok.Token.Kind == TokenKind.Reserved)
                {
                    var text = tok.AtomText();
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
                }
                // bare unsigned integer (decimal or hex)?
                if (IsBareUnsignedIntAtom(tok))
                {
                    bareInts.Add(tok);
                    i++;
                    continue;
                }
                // memidx as $name (Id token)?
                if (tok.Token.Kind == TokenKind.Id && !haveMemIdx && bareInts.Count == 0)
                {
                    memIdx = ResolveNamespaceIdx(fctx.Module.Mems, tok, "memory");
                    haveMemIdx = true;
                    i++;
                    continue;
                }
                break;
            }

            // The last bare-int is the lane; anything earlier is memidx.
            if (bareInts.Count == 0)
                throw new FormatException(
                    $"line {parent.Token.Line}: {bc.GetMnemonic()} missing lane index");
            var laneAtom = bareInts[bareInts.Count - 1];
            var laneVal = ParseUnsignedLongField(laneAtom.AtomText(), laneAtom.Token.Line);
            if (laneVal > 255)
                throw new FormatException(
                    $"line {laneAtom.Token.Line}: lane index {laneVal} out of range");
            laneIndex = (byte)laneVal;
            if (bareInts.Count > 1)
            {
                if (haveMemIdx)
                    throw new FormatException(
                        $"line {parent.Token.Line}: {bc.GetMnemonic()} has duplicate memory index");
                if (bareInts.Count > 2)
                    throw new FormatException(
                        $"line {parent.Token.Line}: {bc.GetMnemonic()} too many bare integer operands");
                var memAtom = bareInts[0];
                memIdx = ResolveNamespaceIdx(fctx.Module.Mems, memAtom, "memory");
                haveMemIdx = true;
            }

            int ai = alignLog2;
            uint memIdxCap = memIdx;
            bool haveMemIdxCap = haveMemIdx;
            byte lane = laneIndex.Value;
            return DecodeViaBinary(bc, w =>
            {
                uint alignBits = (uint)ai;
                if (haveMemIdxCap)
                {
                    alignBits |= 0x40u;
                    w.WriteLeb128U32(alignBits);
                    w.WriteLeb128U32(memIdxCap);
                }
                else
                {
                    w.WriteLeb128U32(alignBits);
                }
                WriteLeb128U64(w, offset);
                w.Write(lane);
            });
        }

        private static InstructionBase BuildSimdLaneInstruction(
            ByteCode bc, int maxLane, SExpr parent, ref int i)
        {
            if (i >= parent.Children.Count
                || parent.Children[i].Kind != SExprKind.Atom
                || !IsBareUnsignedIntAtom(parent.Children[i]))
                throw new FormatException(
                    $"line {parent.Token.Line}: {bc.GetMnemonic()} expects a lane index");
            var laneAtom = parent.Children[i];
            var idx = ParseUnsignedLongField(laneAtom.AtomText(), laneAtom.Token.Line);
            if (idx >= (uint)maxLane)
                throw new FormatException(
                    $"line {laneAtom.Token.Line}: lane index {idx} out of range (max {maxLane - 1})");
            i++;
            byte lane = (byte)idx;
            return DecodeViaBinary(bc, w => w.Write(lane));
        }

        private static InstructionBase BuildShuffleInstruction(ByteCode bc, SExpr parent, ref int i)
        {
            // 16 lane indices, each 0..31.
            var lanes = new byte[16];
            for (int k = 0; k < 16; k++)
            {
                if (i >= parent.Children.Count
                    || parent.Children[i].Kind != SExprKind.Atom
                    || !IsBareUnsignedIntAtom(parent.Children[i]))
                    throw new FormatException(
                        $"line {parent.Token.Line}: i8x16.shuffle expects 16 lane indices, got {k}");
                var atom = parent.Children[i];
                var idx = ParseUnsignedLongField(atom.AtomText(), atom.Token.Line);
                if (idx >= 32)
                    throw new FormatException(
                        $"line {atom.Token.Line}: shuffle lane index {idx} out of range (max 31)");
                lanes[k] = (byte)idx;
                i++;
            }
            return DecodeViaBinary(bc, w => w.Write(lanes));
        }

        private static InstructionBase BuildV128ConstInstruction(ByteCode bc, SExpr parent, ref int i)
        {
            // (v128.const shape lane0 lane1 ...) where shape ∈ {i8x16, i16x8,
            // i32x4, i64x2, f32x4, f64x2}. Each lane is a numeric literal in
            // the shape's lane type. Encoded as 16 little-endian bytes.
            if (i >= parent.Children.Count
                || parent.Children[i].Kind != SExprKind.Atom
                || parent.Children[i].Token.Kind != TokenKind.Keyword)
                throw new FormatException(
                    $"line {parent.Token.Line}: v128.const expects a shape keyword");
            var shape = parent.Children[i].AtomText();
            i++;

            using var ms = new MemoryStream(16);
            using var w = new BinaryWriter(ms);
            switch (shape)
            {
                case "i8x16":
                    for (int k = 0; k < 16; k++)
                    {
                        var atom = ConsumeLaneAtom(parent, ref i, shape, k);
                        w.Write((byte)ParseSignedLaneByte(atom));
                    }
                    break;
                case "i16x8":
                    for (int k = 0; k < 8; k++)
                    {
                        var atom = ConsumeLaneAtom(parent, ref i, shape, k);
                        w.Write((ushort)ParseSignedLaneShort(atom));
                    }
                    break;
                case "i32x4":
                    for (int k = 0; k < 4; k++)
                    {
                        var atom = ConsumeLaneAtom(parent, ref i, shape, k);
                        w.Write((int)ParseSignedLaneInt(atom));
                    }
                    break;
                case "i64x2":
                    for (int k = 0; k < 2; k++)
                    {
                        var atom = ConsumeLaneAtom(parent, ref i, shape, k);
                        w.Write((long)ParseSignedLaneLong(atom));
                    }
                    break;
                case "f32x4":
                    for (int k = 0; k < 4; k++)
                    {
                        var atom = ConsumeLaneAtom(parent, ref i, shape, k);
                        var bits = ParseFloatLaneBits32(atom);
                        w.Write(bits);
                    }
                    break;
                case "f64x2":
                    for (int k = 0; k < 2; k++)
                    {
                        var atom = ConsumeLaneAtom(parent, ref i, shape, k);
                        var bits = ParseFloatLaneBits64(atom);
                        w.Write(bits);
                    }
                    break;
                default:
                    throw new FormatException(
                        $"line {parent.Token.Line}: v128.const unknown shape '{shape}'");
            }
            w.Flush();
            var bytes = ms.ToArray();
            if (bytes.Length != 16)
                throw new FormatException(
                    $"line {parent.Token.Line}: v128.const internal error: encoded {bytes.Length} bytes");
            return DecodeViaBinary(bc, bw => bw.Write(bytes));
        }

        private static SExpr ConsumeLaneAtom(SExpr parent, ref int i, string shape, int k)
        {
            if (i >= parent.Children.Count || parent.Children[i].Kind != SExprKind.Atom)
                throw new FormatException(
                    $"line {parent.Token.Line}: v128.const {shape} expected lane {k} literal");
            var atom = parent.Children[i];
            i++;
            return atom;
        }

        private static long ParseSignedLaneByte(SExpr atom)
        {
            var s = ParseSignedInt64(atom);
            if (s >= -128 && s <= 255) return s;
            throw new FormatException($"line {atom.Token.Line}: i8 lane literal {s} out of range");
        }

        private static long ParseSignedLaneShort(SExpr atom)
        {
            var s = ParseSignedInt64(atom);
            if (s >= short.MinValue && s <= ushort.MaxValue) return s;
            throw new FormatException($"line {atom.Token.Line}: i16 lane literal {s} out of range");
        }

        private static long ParseSignedLaneInt(SExpr atom)
        {
            var s = ParseSignedInt64(atom);
            if (s >= int.MinValue && s <= uint.MaxValue) return s;
            throw new FormatException($"line {atom.Token.Line}: i32 lane literal {s} out of range");
        }

        private static long ParseSignedLaneLong(SExpr atom) => ParseSignedInt64(atom);

        private static uint ParseFloatLaneBits32(SExpr atom)
        {
            FloatLiteralBits.Parse(atom.AtomText().Replace("_", ""), out var f32Bits, out _);
            return f32Bits;
        }

        private static ulong ParseFloatLaneBits64(SExpr atom)
        {
            FloatLiteralBits.Parse(atom.AtomText().Replace("_", ""), out _, out var f64Bits);
            return f64Bits;
        }

        private static bool IsBareUnsignedIntAtom(SExpr atom)
        {
            if (atom.Kind != SExprKind.Atom) return false;
            if (atom.Token.Kind != TokenKind.Reserved) return false;
            return IsDecimalOrHexInt(atom.AtomText());
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

        /// <summary>
        /// Memarg-carrying atomic ops (threads proposal). Excludes
        /// <c>atomic.fence</c>, which takes no memarg. The
        /// <paramref name="naturalAlignLog2"/> matches the access width
        /// exactly — atomic ops require <c>align == width</c> at
        /// validation, not the <c>align ≤ width</c> rule used by
        /// non-atomic ops.
        /// </summary>
        private static bool TryGetAtomicMemoryOpcode(string kw, out ByteCode code, out int naturalAlignLog2)
        {
            switch (kw)
            {
                // Loads
                case "i32.atomic.load":       code = (ByteCode)AtomCode.I32AtomicLoad;      naturalAlignLog2 = 2; return true;
                case "i64.atomic.load":       code = (ByteCode)AtomCode.I64AtomicLoad;      naturalAlignLog2 = 3; return true;
                case "i32.atomic.load8_u":    code = (ByteCode)AtomCode.I32AtomicLoad8U;    naturalAlignLog2 = 0; return true;
                case "i32.atomic.load16_u":   code = (ByteCode)AtomCode.I32AtomicLoad16U;   naturalAlignLog2 = 1; return true;
                case "i64.atomic.load8_u":    code = (ByteCode)AtomCode.I64AtomicLoad8U;    naturalAlignLog2 = 0; return true;
                case "i64.atomic.load16_u":   code = (ByteCode)AtomCode.I64AtomicLoad16U;   naturalAlignLog2 = 1; return true;
                case "i64.atomic.load32_u":   code = (ByteCode)AtomCode.I64AtomicLoad32U;   naturalAlignLog2 = 2; return true;
                // Stores
                case "i32.atomic.store":      code = (ByteCode)AtomCode.I32AtomicStore;     naturalAlignLog2 = 2; return true;
                case "i64.atomic.store":      code = (ByteCode)AtomCode.I64AtomicStore;     naturalAlignLog2 = 3; return true;
                case "i32.atomic.store8":     code = (ByteCode)AtomCode.I32AtomicStore8;    naturalAlignLog2 = 0; return true;
                case "i32.atomic.store16":    code = (ByteCode)AtomCode.I32AtomicStore16;   naturalAlignLog2 = 1; return true;
                case "i64.atomic.store8":     code = (ByteCode)AtomCode.I64AtomicStore8;    naturalAlignLog2 = 0; return true;
                case "i64.atomic.store16":    code = (ByteCode)AtomCode.I64AtomicStore16;   naturalAlignLog2 = 1; return true;
                case "i64.atomic.store32":    code = (ByteCode)AtomCode.I64AtomicStore32;   naturalAlignLog2 = 2; return true;
                // Wait/notify
                case "memory.atomic.notify":  code = (ByteCode)AtomCode.MemoryAtomicNotify; naturalAlignLog2 = 2; return true;
                case "memory.atomic.wait32":  code = (ByteCode)AtomCode.MemoryAtomicWait32; naturalAlignLog2 = 2; return true;
                case "memory.atomic.wait64":  code = (ByteCode)AtomCode.MemoryAtomicWait64; naturalAlignLog2 = 3; return true;
                // RMW add
                case "i32.atomic.rmw.add":    code = (ByteCode)AtomCode.I32AtomicRmwAdd;    naturalAlignLog2 = 2; return true;
                case "i64.atomic.rmw.add":    code = (ByteCode)AtomCode.I64AtomicRmwAdd;    naturalAlignLog2 = 3; return true;
                case "i32.atomic.rmw8.add_u": code = (ByteCode)AtomCode.I32AtomicRmw8AddU;  naturalAlignLog2 = 0; return true;
                case "i32.atomic.rmw16.add_u":code = (ByteCode)AtomCode.I32AtomicRmw16AddU; naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw8.add_u": code = (ByteCode)AtomCode.I64AtomicRmw8AddU;  naturalAlignLog2 = 0; return true;
                case "i64.atomic.rmw16.add_u":code = (ByteCode)AtomCode.I64AtomicRmw16AddU; naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw32.add_u":code = (ByteCode)AtomCode.I64AtomicRmw32AddU; naturalAlignLog2 = 2; return true;
                // RMW sub
                case "i32.atomic.rmw.sub":    code = (ByteCode)AtomCode.I32AtomicRmwSub;    naturalAlignLog2 = 2; return true;
                case "i64.atomic.rmw.sub":    code = (ByteCode)AtomCode.I64AtomicRmwSub;    naturalAlignLog2 = 3; return true;
                case "i32.atomic.rmw8.sub_u": code = (ByteCode)AtomCode.I32AtomicRmw8SubU;  naturalAlignLog2 = 0; return true;
                case "i32.atomic.rmw16.sub_u":code = (ByteCode)AtomCode.I32AtomicRmw16SubU; naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw8.sub_u": code = (ByteCode)AtomCode.I64AtomicRmw8SubU;  naturalAlignLog2 = 0; return true;
                case "i64.atomic.rmw16.sub_u":code = (ByteCode)AtomCode.I64AtomicRmw16SubU; naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw32.sub_u":code = (ByteCode)AtomCode.I64AtomicRmw32SubU; naturalAlignLog2 = 2; return true;
                // RMW and
                case "i32.atomic.rmw.and":    code = (ByteCode)AtomCode.I32AtomicRmwAnd;    naturalAlignLog2 = 2; return true;
                case "i64.atomic.rmw.and":    code = (ByteCode)AtomCode.I64AtomicRmwAnd;    naturalAlignLog2 = 3; return true;
                case "i32.atomic.rmw8.and_u": code = (ByteCode)AtomCode.I32AtomicRmw8AndU;  naturalAlignLog2 = 0; return true;
                case "i32.atomic.rmw16.and_u":code = (ByteCode)AtomCode.I32AtomicRmw16AndU; naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw8.and_u": code = (ByteCode)AtomCode.I64AtomicRmw8AndU;  naturalAlignLog2 = 0; return true;
                case "i64.atomic.rmw16.and_u":code = (ByteCode)AtomCode.I64AtomicRmw16AndU; naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw32.and_u":code = (ByteCode)AtomCode.I64AtomicRmw32AndU; naturalAlignLog2 = 2; return true;
                // RMW or
                case "i32.atomic.rmw.or":     code = (ByteCode)AtomCode.I32AtomicRmwOr;     naturalAlignLog2 = 2; return true;
                case "i64.atomic.rmw.or":     code = (ByteCode)AtomCode.I64AtomicRmwOr;     naturalAlignLog2 = 3; return true;
                case "i32.atomic.rmw8.or_u":  code = (ByteCode)AtomCode.I32AtomicRmw8OrU;   naturalAlignLog2 = 0; return true;
                case "i32.atomic.rmw16.or_u": code = (ByteCode)AtomCode.I32AtomicRmw16OrU;  naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw8.or_u":  code = (ByteCode)AtomCode.I64AtomicRmw8OrU;   naturalAlignLog2 = 0; return true;
                case "i64.atomic.rmw16.or_u": code = (ByteCode)AtomCode.I64AtomicRmw16OrU;  naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw32.or_u": code = (ByteCode)AtomCode.I64AtomicRmw32OrU;  naturalAlignLog2 = 2; return true;
                // RMW xor
                case "i32.atomic.rmw.xor":    code = (ByteCode)AtomCode.I32AtomicRmwXor;    naturalAlignLog2 = 2; return true;
                case "i64.atomic.rmw.xor":    code = (ByteCode)AtomCode.I64AtomicRmwXor;    naturalAlignLog2 = 3; return true;
                case "i32.atomic.rmw8.xor_u": code = (ByteCode)AtomCode.I32AtomicRmw8XorU;  naturalAlignLog2 = 0; return true;
                case "i32.atomic.rmw16.xor_u":code = (ByteCode)AtomCode.I32AtomicRmw16XorU; naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw8.xor_u": code = (ByteCode)AtomCode.I64AtomicRmw8XorU;  naturalAlignLog2 = 0; return true;
                case "i64.atomic.rmw16.xor_u":code = (ByteCode)AtomCode.I64AtomicRmw16XorU; naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw32.xor_u":code = (ByteCode)AtomCode.I64AtomicRmw32XorU; naturalAlignLog2 = 2; return true;
                // RMW xchg
                case "i32.atomic.rmw.xchg":   code = (ByteCode)AtomCode.I32AtomicRmwXchg;   naturalAlignLog2 = 2; return true;
                case "i64.atomic.rmw.xchg":   code = (ByteCode)AtomCode.I64AtomicRmwXchg;   naturalAlignLog2 = 3; return true;
                case "i32.atomic.rmw8.xchg_u":code = (ByteCode)AtomCode.I32AtomicRmw8XchgU; naturalAlignLog2 = 0; return true;
                case "i32.atomic.rmw16.xchg_u":code = (ByteCode)AtomCode.I32AtomicRmw16XchgU;naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw8.xchg_u":code = (ByteCode)AtomCode.I64AtomicRmw8XchgU; naturalAlignLog2 = 0; return true;
                case "i64.atomic.rmw16.xchg_u":code = (ByteCode)AtomCode.I64AtomicRmw16XchgU;naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw32.xchg_u":code = (ByteCode)AtomCode.I64AtomicRmw32XchgU;naturalAlignLog2 = 2; return true;
                // Cmpxchg
                case "i32.atomic.rmw.cmpxchg":code = (ByteCode)AtomCode.I32AtomicRmwCmpxchg;naturalAlignLog2 = 2; return true;
                case "i64.atomic.rmw.cmpxchg":code = (ByteCode)AtomCode.I64AtomicRmwCmpxchg;naturalAlignLog2 = 3; return true;
                case "i32.atomic.rmw8.cmpxchg_u":code = (ByteCode)AtomCode.I32AtomicRmw8CmpxchgU; naturalAlignLog2 = 0; return true;
                case "i32.atomic.rmw16.cmpxchg_u":code = (ByteCode)AtomCode.I32AtomicRmw16CmpxchgU;naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw8.cmpxchg_u":code = (ByteCode)AtomCode.I64AtomicRmw8CmpxchgU; naturalAlignLog2 = 0; return true;
                case "i64.atomic.rmw16.cmpxchg_u":code = (ByteCode)AtomCode.I64AtomicRmw16CmpxchgU;naturalAlignLog2 = 1; return true;
                case "i64.atomic.rmw32.cmpxchg_u":code = (ByteCode)AtomCode.I64AtomicRmw32CmpxchgU;naturalAlignLog2 = 2; return true;

                default:
                    code = default;
                    naturalAlignLog2 = 0;
                    return false;
            }
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

        private static float ParseFloat32(SExpr atom)
        {
            try
            {
                FloatLiteralBits.Parse(atom.AtomText(), out var f32Bits, out _);
                return BitConverter.Int32BitsToSingle(unchecked((int)f32Bits));
            }
            catch (FormatException e)
            {
                throw new FormatException(
                    $"line {atom.Token.Line}: {e.Message} (literal '{atom.AtomText()}')");
            }
        }

        private static double ParseFloat64(SExpr atom)
        {
            try
            {
                FloatLiteralBits.Parse(atom.AtomText(), out _, out var f64Bits);
                return BitConverter.Int64BitsToDouble(unchecked((long)f64Bits));
            }
            catch (FormatException e)
            {
                throw new FormatException(
                    $"line {atom.Token.Line}: {e.Message} (literal '{atom.AtomText()}')");
            }
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
                // Dedup against non-rec Module.Types entries only.
                int flatSeen = 0;
                for (int t = 0; t < ctx.Module.Types.Count; t++)
                {
                    var group = ctx.Module.Types[t];
                    if (!ctx.TypesFromRec[t] && group.SubTypes.Length == 1)
                    {
                        var body = group.SubTypes[0].Body as FunctionType;
                        if (body != null && FunctionTypeStructurallyEqual(body, ft))
                            return (ValType)flatSeen;
                    }
                    flatSeen += group.SubTypes.Length;
                }
                var idx2 = flatSeen;
                ctx.Module.Types.Add(new RecursiveType(new SubType(ft, final: true)));
                ctx.TypesFromRec.Add(false);
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
