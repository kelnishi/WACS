// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Wacs.Core.Attributes;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Text
{
    /// <summary>
    /// Consolidated WAT renderer for <see cref="Module"/>. Default
    /// <see cref="TextWriterStyle.Canonical"/> emits a parser-friendly
    /// flat form that <see cref="TextModuleParser"/> can re-parse to a
    /// structurally equivalent <see cref="Module"/>.
    /// <see cref="TextWriterStyle.StackAnnotated"/> layers on debug
    /// annotations (per-function id, <c>;; label = @N</c>, left-margin
    /// stack-state comments) — what the legacy <c>ModuleRenderer</c>
    /// used to emit. Future passes will add a folded / S-expression
    /// style and comment / annotation round-trip.
    /// </summary>
    public static class TextModuleWriter
    {
        /// <summary>Two-space module-body indent.</summary>
        public const string Indent2Space = "  ";

        public static string Write(Module module) =>
            Write(module, TextWriterOptions.Canonical);

        public static string Write(Module module, TextWriterOptions options)
        {
            var sb = new StringBuilder();
            using var w = new StringWriter(sb);
            WriteTo(w, module, options);
            return sb.ToString();
        }

        public static void WriteTo(TextWriter w, Module module) =>
            WriteTo(w, module, TextWriterOptions.Canonical);

        public static void WriteTo(TextWriter w, Module module, TextWriterOptions options)
        {
            // Module-level LEADING comments (before the first section).
            // These appear at the very top of the round-tripped source,
            // matching where the parser captured them in module-level
            // attachment.
            EmitLeading(w, module, ModuleElementRef.ModuleLevel, indent: "");

            w.WriteLine("(module");
            var indent = Indent2Space;

            // Module-level annotations sit at the top of the body, on
            // their own lines, before any sections.
            EmitAnnotations(w, module, ModuleElementRef.ModuleLevel, indent);

            // Types — function, struct, array, plus sub-typed and
            // rec-grouped variants.
            for (int t = 0; t < module.Types.Count; t++)
            {
                EmitLeading(w, module, new ModuleElementRef(ModuleElementKind.Type, t), indent);
                WriteRecursiveType(w, module.Types[t], indent);
            }

            // Imports
            for (int ii = 0; ii < module.Imports.Length; ii++)
            {
                EmitLeading(w, module, new ModuleElementRef(ModuleElementKind.Import, ii), indent);
                WriteImport(w, module, module.Imports[ii], indent);
            }

            // Functions (defined; imports skipped — handled above)
            int fimportCount = module.ImportedFunctions.Count;
            for (int i = 0; i < module.Funcs.Count; i++)
            {
                EmitLeading(w, module, new ModuleElementRef(ModuleElementKind.Function, i), indent);
                WriteFunc(w, module, module.Funcs[i], fimportCount + i, indent, options);
            }

            // Tables / Memories / Globals (defined)
            for (int ti = 0; ti < module.Tables.Count; ti++)
            {
                EmitLeading(w, module, new ModuleElementRef(ModuleElementKind.Table, ti), indent);
                WriteTable(w, module.Tables[ti], indent);
            }

            for (int mi = 0; mi < module.Memories.Count; mi++)
            {
                EmitLeading(w, module, new ModuleElementRef(ModuleElementKind.Memory, mi), indent);
                WriteMemory(w, module.Memories[mi], indent);
            }

            for (int gi = 0; gi < module.Globals.Count; gi++)
            {
                EmitLeading(w, module, new ModuleElementRef(ModuleElementKind.Global, gi), indent);
                WriteGlobal(w, module, module.Globals[gi], indent);
            }

            // Tags (exception-handling). Imported tags are surfaced via
            // the import section; defined tags emit here in declaration
            // order matching what the parser captured.
            for (int tgi = 0; tgi < module.Tags.Count; tgi++)
            {
                EmitLeading(w, module, new ModuleElementRef(ModuleElementKind.Tag, tgi), indent);
                WriteTag(w, module.Tags[tgi], indent);
            }

            // Exports
            for (int ei = 0; ei < module.Exports.Length; ei++)
            {
                EmitLeading(w, module, new ModuleElementRef(ModuleElementKind.Export, ei), indent);
                WriteExport(w, module.Exports[ei], indent);
            }

            // Start
            if (module.StartIndex != FuncIdx.Default)
            {
                EmitLeading(w, module, new ModuleElementRef(ModuleElementKind.Start), indent);
                w.WriteLine($"{indent}(start {module.StartIndex.Value})");
            }

            for (int eli = 0; eli < module.Elements.Length; eli++)
            {
                EmitLeading(w, module, new ModuleElementRef(ModuleElementKind.Element, eli), indent);
                WriteElementSegment(w, module.Elements[eli], indent);
            }
            for (int di = 0; di < module.Datas.Length; di++)
            {
                EmitLeading(w, module, new ModuleElementRef(ModuleElementKind.Data, di), indent);
                WriteDataSegment(w, module.Datas[di], indent);
            }

            w.WriteLine(")");
        }

        // ---- Trivia + annotation re-emission ------------------------------

        /// <summary>
        /// Emit any leading comments attached to <paramref name="owner"/>
        /// — each on its own line, indented to match the element about
        /// to follow. Comments are emitted in source order. No-op when
        /// the module carries no <c>Comments</c> table or no entries
        /// for this owner.
        /// </summary>
        private static void EmitLeading(
            TextWriter w, Module module, ModuleElementRef owner, string indent)
        {
            if (module.Comments == null) return;
            if (!module.Comments.TryGetValue(owner, out var list) || list.Count == 0)
                return;
            foreach (var c in list)
            {
                // Re-emit with original delimiters intact. Trailing
                // comments would land on the same line as their anchor
                // — Pass 3 attaches everything as leading; the
                // distinction is exercised in later passes.
                w.WriteLine($"{indent}{c.Text}");
            }
        }

        /// <summary>
        /// Emit any <c>(@name payload…)</c> annotations attached to
        /// <paramref name="owner"/>. Each annotation re-emits on its
        /// own line.
        /// </summary>
        private static void EmitAnnotations(
            TextWriter w, Module module, ModuleElementRef owner, string indent)
        {
            if (module.Annotations == null) return;
            if (!module.Annotations.TryGetValue(owner, out var list) || list.Count == 0)
                return;
            foreach (var a in list)
            {
                if (string.IsNullOrEmpty(a.Payload))
                    w.WriteLine($"{indent}(@{a.Name})");
                else
                    w.WriteLine($"{indent}(@{a.Name} {a.Payload})");
            }
        }

        // ---- Section writers ---------------------------------------------

        private static void WriteImport(TextWriter w, Module m, Module.Import imp, string indent)
        {
            w.Write($"{indent}(import \"{Escape(imp.ModuleName)}\" \"{Escape(imp.Name)}\" ");
            switch (imp.Desc)
            {
                case Module.ImportDesc.FuncDesc fd:
                    w.Write($"(func (type {fd.TypeIndex.Value}))");
                    break;
                case Module.ImportDesc.TableDesc td:
                    w.Write("(table ");
                    WriteLimits(w, td.TableDef.Limits);
                    w.Write($" {ToWatValType(td.TableDef.ElementType)})");
                    break;
                case Module.ImportDesc.MemDesc md:
                    w.Write("(memory ");
                    WriteLimits(w, md.MemDef.Limits);
                    w.Write(")");
                    break;
                case Module.ImportDesc.GlobalDesc gd:
                    w.Write("(global ");
                    WriteGlobalType(w, gd.GlobalDef);
                    w.Write(")");
                    break;
                case Module.ImportDesc.TagDesc tg:
                    w.Write($"(tag (type {tg.TagDef.TypeIndex.Value}))");
                    break;
            }
            w.WriteLine(")");
        }

        private static void WriteFunc(
            TextWriter w, Module m, Module.Function fn, int absIdx, string indent,
            TextWriterOptions options)
        {
            // Stack-annotated mode delegates to Function.RenderText —
            // it walks the body with a StackRenderer to produce the
            // left-margin stack-state comments and ;; label = @N
            // annotations that the diagnostic dump expects. The
            // StreamWriter wrap is necessary because that path was
            // written against StreamWriter rather than the more
            // general TextWriter abstraction we use everywhere else.
            if (options.Style == TextWriterStyle.StackAnnotated)
            {
                bool prevRenderStack = fn.RenderStack;
                fn.RenderStack = true;
                try
                {
                    using var ms = new MemoryStream();
                    using (var sw = new StreamWriter(ms, new UTF8Encoding(false), -1, leaveOpen: true))
                    {
                        fn.RenderText(sw, m, indent);
                        sw.Flush();
                    }
                    w.Write(Encoding.UTF8.GetString(ms.ToArray()));
                }
                finally
                {
                    fn.RenderStack = prevRenderStack;
                }
                return;
            }

            w.Write($"{indent}(func (type {fn.TypeIndex.Value})");
            if (fn.Locals != null && fn.Locals.Length > 0)
            {
                w.Write(" (local");
                foreach (var t in fn.Locals)
                    w.Write($" {ToWatValType(t)}");
                w.Write(")");
            }
            w.WriteLine();
            // Body — folded vs flat depends on the requested style.
            // Folded mode currently folds at the top level only;
            // instructions inside block / loop / if bodies render flat
            // (recursive folding lands in a follow-up pass).
            if (options.Style == TextWriterStyle.Folded)
                WriteFoldedInstructionSeq(w, fn.Body.Instructions, indent + Indent2Space);
            else
                WriteInstructionSeq(w, fn.Body.Instructions, indent + Indent2Space, trimTrailingEnd: true);
            w.WriteLine($"{indent})");
        }

        /// <summary>
        /// Fold an instruction sequence into S-expression form where
        /// possible. Operates as a single linear pass with a stack of
        /// rendered operand fragments:
        ///   1. Leaves (consume=0, produce=1) — e.g. <c>i32.const N</c>,
        ///      <c>local.get N</c> — push their rendered form onto the
        ///      pending stack.
        ///   2. Operators (consume&gt;0, produce&gt;0) pop their operands
        ///      and wrap as <c>(op (operand1) (operand2))</c>.
        ///   3. Effectful ops (produce=0) emit the folded form as a
        ///      stand-alone line.
        ///   4. Anything the folder can't handle (control flow, block
        ///      shapes, opcodes outside <see cref="OpcodeArity"/>'s
        ///      table) flushes the pending stack as flat lines and
        ///      emits the instruction flat.
        ///
        /// <para>Inner block bodies are emitted flat in this pass —
        /// folding into nested blocks is a follow-up.</para>
        /// </summary>
        private static void WriteFoldedInstructionSeq(
            TextWriter w, InstructionSequence seq, string indent)
        {
            int count = seq.Count;
            if (count > 0 && seq[count - 1] is InstEnd) count--;

            var pending = new System.Collections.Generic.Stack<string>();
            for (int i = 0; i < count; i++)
            {
                var inst = seq[i]!;
                if (IsChainBreaker(inst)
                    || !OpCodes.OpcodeArity.TryGet(inst, out int consume, out int produce))
                {
                    DrainPendingAsFlat(w, pending, indent);
                    // Block bodies stay flat — falls back through the
                    // existing flat-emit machinery, which recurses.
                    WriteInstruction(w, inst, indent);
                    continue;
                }
                if (pending.Count < consume)
                {
                    // Not enough operands to fold this op — drain and
                    // emit flat. Happens when a value comes from a
                    // chain-breaker earlier (e.g. a call result that
                    // we couldn't fold into).
                    DrainPendingAsFlat(w, pending, indent);
                    WriteInstruction(w, inst, indent);
                    continue;
                }

                // Pop `consume` operands; the topmost stack entry is
                // the LAST operand in source-order (right-hand side).
                var ops = new string[consume];
                for (int k = consume - 1; k >= 0; k--) ops[k] = pending.Pop();
                string opText = RenderInstruction(inst);
                string folded = consume == 0
                    ? $"({opText})"
                    : $"({opText} {string.Join(" ", ops)})";

                if (produce > 0)
                {
                    pending.Push(folded);
                }
                else
                {
                    // Effectful instruction — emit on its own line.
                    w.WriteLine($"{indent}{folded}");
                }
            }

            DrainPendingAsFlat(w, pending, indent);
        }

        /// <summary>
        /// Instructions whose presence forces a chain break in folded
        /// mode: block-shaped forms, branches, returns, calls,
        /// unreachable, throw — anything whose result the folder
        /// can't safely treat as a pure operand.
        /// </summary>
        private static bool IsChainBreaker(InstructionBase inst) =>
            inst is InstBlock or InstLoop or InstIf or InstElse or InstEnd
                 or InstTryTable
                 or InstBranch or InstBranchIf or InstBranchTable
                 or InstReturn or InstUnreachable
                 or InstCall or InstCallIndirect
                 or InstThrow or InstThrowRef
                 or Wacs.Core.Instructions.InstReturnCall
                 or Wacs.Core.Instructions.InstReturnCallIndirect
                 or Wacs.Core.Instructions.Reference.InstCallRef
                 or Wacs.Core.Instructions.InstReturnCallRef;

        /// <summary>
        /// Emit any operands still on the pending stack as standalone
        /// flat lines, in source order (oldest first). Used at chain
        /// boundaries and at the end of a function body.
        /// </summary>
        private static void DrainPendingAsFlat(
            TextWriter w, System.Collections.Generic.Stack<string> pending, string indent)
        {
            if (pending.Count == 0) return;
            var arr = pending.ToArray();
            System.Array.Reverse(arr);
            foreach (var s in arr) w.WriteLine($"{indent}{s}");
            pending.Clear();
        }

        // ---- Partial-render entry points ----------------------------------

        /// <summary>
        /// Render a single function from <paramref name="module"/>. The
        /// <paramref name="index"/> is the absolute function index
        /// (imported functions are addressable too, but they have no
        /// body — passing one returns the empty string).
        /// </summary>
        public static string WriteFunction(
            Module module, FuncIdx index, string indent = "",
            TextWriterOptions? options = null)
        {
            options ??= TextWriterOptions.Canonical;
            int importCount = module.ImportedFunctions.Count;
            int defIdx = (int)index.Value - importCount;
            if (defIdx < 0 || defIdx >= module.Funcs.Count)
                return string.Empty;

            var sb = new StringBuilder();
            using var w = new StringWriter(sb);
            WriteFunc(w, module, module.Funcs[defIdx], (int)index.Value, indent, options);
            return sb.ToString();
        }

        private static void WriteTable(TextWriter w, TableType t, string indent)
        {
            w.Write($"{indent}(table ");
            WriteLimits(w, t.Limits);
            w.WriteLine($" {ToWatValType(t.ElementType)})");
        }

        private static void WriteMemory(TextWriter w, MemoryType m, string indent)
        {
            w.Write($"{indent}(memory ");
            WriteLimits(w, m.Limits);
            w.WriteLine(")");
        }

        private static void WriteGlobal(TextWriter w, Module m, Module.Global g, string indent)
        {
            w.Write($"{indent}(global ");
            WriteGlobalType(w, g.Type);
            w.Write(" ");
            WriteInitExpr(w, g.Initializer);
            w.WriteLine(")");
        }

        private static void WriteExport(TextWriter w, Module.Export e, string indent)
        {
            w.Write($"{indent}(export \"{Escape(e.Name)}\" ");
            switch (e.Desc)
            {
                case Module.ExportDesc.FuncDesc fd:   w.Write($"(func {fd.FunctionIndex.Value})"); break;
                case Module.ExportDesc.TableDesc td:  w.Write($"(table {td.TableIndex.Value})"); break;
                case Module.ExportDesc.MemDesc md:    w.Write($"(memory {md.MemoryIndex.Value})"); break;
                case Module.ExportDesc.GlobalDesc gd: w.Write($"(global {gd.GlobalIndex.Value})"); break;
                case Module.ExportDesc.TagDesc tg:    w.Write($"(tag {tg.TagIndex.Value})"); break;
            }
            w.WriteLine(")");
        }

        // ---- Types / shared fragments -------------------------------------

        private static void WriteParams(TextWriter w, ResultType rt)
        {
            if (rt.Arity == 0) return;
            w.Write(" (param");
            foreach (var t in rt.Types)
                w.Write($" {ToWatValType(t)}");
            w.Write(")");
        }

        private static void WriteResults(TextWriter w, ResultType rt)
        {
            if (rt.Arity == 0) return;
            w.Write(" (result");
            foreach (var t in rt.Types)
                w.Write($" {ToWatValType(t)}");
            w.Write(")");
        }

        private static void WriteGlobalType(TextWriter w, GlobalType gt)
        {
            if (gt.Mutability == Mutability.Mutable)
                w.Write($"(mut {ToWatValType(gt.ContentType)})");
            else
                w.Write(ToWatValType(gt.ContentType));
        }

        private static void WriteLimits(TextWriter w, Limits l)
        {
            if (l.AddressType == AddrType.I64)
                w.Write("i64 ");
            w.Write(l.Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (l.Maximum.HasValue)
                w.Write(" " + l.Maximum.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (l.Shared)
                w.Write(" shared");
        }

        /// <summary>
        /// Render a ValType into its WAT form. Handles abstract types via
        /// their <c>[WatToken]</c> attribute and DefType references via
        /// <c>(ref $idx)</c>.
        /// </summary>
        private static string ToWatValType(ValType t)
        {
            if (t.IsDefType())
            {
                var nullable = t.IsNullable() ? "null " : "";
                return $"(ref {nullable}{t.Index().Value})";
            }
            // Map sentinel cases.
            switch (t)
            {
                case ValType.I32:  return "i32";
                case ValType.I64:  return "i64";
                case ValType.F32:  return "f32";
                case ValType.F64:  return "f64";
                case ValType.V128: return "v128";
                case ValType.FuncRef:   return "funcref";
                case ValType.ExternRef: return "externref";
                case ValType.Any:       return "anyref";
                case ValType.Eq:        return "eqref";
                case ValType.I31:       return "i31ref";
                case ValType.Struct:    return "structref";
                case ValType.Array:     return "arrayref";
                case ValType.NoFunc:    return "nullfuncref";
                case ValType.NoExtern:  return "nullexternref";
                case ValType.None:      return "nullref";
                case ValType.Exn:       return "exnref";
                case ValType.NoExn:     return "nullexnref";
                default: return t.ToWat();
            }
        }

        // ---- Instructions -------------------------------------------------

        private static void WriteInstructionSeq(
            TextWriter w, InstructionSequence seq, string indent, bool trimTrailingEnd)
        {
            int count = seq.Count;
            if (trimTrailingEnd && count > 0 && seq[count - 1] is InstEnd)
                count--;
            for (int i = 0; i < count; i++)
                WriteInstruction(w, seq[i]!, indent);
        }

        private static void WriteInstruction(TextWriter w, InstructionBase inst, string indent)
        {
            // Block instructions recursively render their inner sequences.
            switch (inst)
            {
                case InstBlock ib: WriteBlockForm(w, ib, "block", indent); return;
                case InstLoop il:  WriteBlockForm(w, il, "loop", indent); return;
                case InstIf iif:   WriteIfForm(w, iif, indent); return;
                case InstElse: return;  // handled inside InstIf
                case InstEnd:   return; // trailing end already trimmed
            }

            // Plain instructions. Many instruction classes override
            // RenderText(null) to emit their immediates, but several common
            // ones (LocalGet/Set/Tee, GlobalGet/Set, Call, Br, BrIf) do
            // not. Render those by their public accessors; fall back to
            // RenderText otherwise.
            w.WriteLine($"{indent}{RenderInstruction(inst)}");
        }

        private static string RenderInstruction(InstructionBase inst)
        {
            // Variable ops — IVarInstruction exposes GetIndex().
            if (inst is IVarInstruction varI)
                return $"{inst.Op.GetMnemonic()} {varI.GetIndex()}";

            // Call — ICallInstruction or reach in via reflection isn't
            // ideal; check concrete type.
            if (inst is InstCall callI)
                return $"call {callI.X.Value}";
            if (inst is InstBranch br)
                return $"br {br.Label}";
            if (inst is InstBranchIf brIf)
                return $"br_if {brIf.Label}";

            // Constants use RenderText overrides already.
            return inst.RenderText(null);
        }

        private static void WriteBlockForm(TextWriter w, IBlockInstruction blk, string keyword, string indent)
        {
            w.Write($"{indent}{keyword}");
            WriteBlockType(w, blk.BlockType);
            w.WriteLine();
            var body = blk.GetBlock(0).Instructions;
            WriteInstructionSeq(w, body, indent + "  ", trimTrailingEnd: true);
            w.WriteLine($"{indent}end");
        }

        private static void WriteIfForm(TextWriter w, InstIf iif, string indent)
        {
            w.Write($"{indent}if");
            WriteBlockType(w, iif.BlockType);
            w.WriteLine();
            // Then-block: GetBlock(0). Ends with InstElse when there's an
            // else arm, or InstEnd otherwise. Strip the trailing marker.
            var thenSeq = iif.GetBlock(0).Instructions;
            int thenCount = thenSeq.Count;
            bool hasElse = ((IBlockInstruction)iif).Count == 2;
            if (thenCount > 0 && (thenSeq[thenCount - 1] is InstElse || thenSeq[thenCount - 1] is InstEnd))
                thenCount--;
            for (int i = 0; i < thenCount; i++)
                WriteInstruction(w, thenSeq[i]!, indent + "  ");
            if (hasElse)
            {
                w.WriteLine($"{indent}else");
                var elseSeq = iif.GetBlock(1).Instructions;
                int elseCount = elseSeq.Count;
                if (elseCount > 0 && elseSeq[elseCount - 1] is InstEnd) elseCount--;
                for (int i = 0; i < elseCount; i++)
                    WriteInstruction(w, elseSeq[i]!, indent + "  ");
            }
            w.WriteLine($"{indent}end");
        }

        private static void WriteBlockType(TextWriter w, ValType bt)
        {
            if (bt == ValType.Empty) return;
            if (bt.IsDefType())
            {
                w.Write($" (type {bt.Index().Value})");
                return;
            }
            w.Write($" (result {ToWatValType(bt)})");
        }

        private static void WriteInitExpr(TextWriter w, Expression expr)
        {
            // Emit inline folded form of the initializer — pragmatic: init
            // expressions are typically a single const + end. Walk the
            // sequence (skipping the trailing end) and emit each.
            var insts = expr.Instructions;
            int count = insts.Count;
            if (count > 0 && insts[count - 1] is InstEnd) count--;
            if (count == 0)
            {
                w.Write("(unreachable)");
                return;
            }
            // Folded shape: (i32.const 42) / (ref.null func) / etc.
            for (int i = 0; i < count; i++)
            {
                if (i > 0) w.Write(" ");
                w.Write($"({insts[i]!.RenderText(null)})");
            }
        }

        // ---- Escape helper -----------------------------------------------

        private static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 4);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\r': sb.Append("\\r"); break;
                    default:
                        if (c < 0x20 || c == 0x7F)
                            sb.AppendFormat("\\{0:x2}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // ---- Pass 4 fidelity: full elem / data / tag / GC type ------------

        /// <summary>
        /// Emit a top-level <c>(tag …)</c> form for a defined tag.
        /// The current TagType wraps a function-signature type index;
        /// the canonical shape is <c>(tag (type N))</c>.
        /// </summary>
        private static void WriteTag(TextWriter w, TagType tag, string indent)
        {
            w.WriteLine($"{indent}(tag (type {tag.TypeIndex.Value}))");
        }

        /// <summary>
        /// Emit a <see cref="RecursiveType"/>. Single sub with no super
        /// types renders bare (<c>(type (func …))</c> / <c>(type
        /// (struct …))</c> / <c>(type (array …))</c>). Multiple subs
        /// or a non-final / supered sub renders inside a
        /// <c>(rec …)</c> wrapper.
        /// </summary>
        private static void WriteRecursiveType(TextWriter w, RecursiveType rt, string indent)
        {
            // Single-sub, final, no supers: emit the bare (type …) form
            // matching what the binary encoder produces.
            if (rt.SubTypes.Length == 1
                && rt.SubTypes[0].Final
                && rt.SubTypes[0].SuperTypeIndexes.Length == 0)
            {
                w.Write($"{indent}(type ");
                WriteCompositeBody(w, rt.SubTypes[0].Body);
                w.WriteLine(")");
                return;
            }

            // Otherwise wrap in a (rec …) group.
            w.WriteLine($"{indent}(rec");
            var sub = indent + Indent2Space;
            foreach (var st in rt.SubTypes)
            {
                w.Write($"{sub}(type ");
                WriteSubTypeBody(w, st);
                w.WriteLine(")");
            }
            w.WriteLine($"{indent})");
        }

        /// <summary>
        /// Emit the body of a <c>(type …)</c> form: either a bare
        /// <c>(func …)</c> / <c>(struct …)</c> / <c>(array …)</c> when
        /// the sub is final + no super, or a <c>(sub …)</c> wrapper
        /// otherwise.
        /// </summary>
        private static void WriteSubTypeBody(TextWriter w, SubType st)
        {
            if (st.Final && st.SuperTypeIndexes.Length == 0)
            {
                WriteCompositeBody(w, st.Body);
                return;
            }
            w.Write(st.Final ? "(sub final" : "(sub");
            foreach (var sup in st.SuperTypeIndexes)
                w.Write($" {sup.Value}");
            w.Write(' ');
            WriteCompositeBody(w, st.Body);
            w.Write(')');
        }

        /// <summary>
        /// Emit the inside of a <c>(type …)</c> body — one of the
        /// three composite shapes: function, struct, or array.
        /// </summary>
        private static void WriteCompositeBody(TextWriter w, CompositeType body)
        {
            switch (body)
            {
                case FunctionType ft:
                    w.Write("(func");
                    WriteParams(w, ft.ParameterTypes);
                    WriteResults(w, ft.ResultType);
                    w.Write(')');
                    break;
                case StructType st:
                    w.Write("(struct");
                    foreach (var f in st.FieldTypes)
                    {
                        w.Write(" (field ");
                        WriteFieldType(w, f);
                        w.Write(')');
                    }
                    w.Write(')');
                    break;
                case ArrayType at:
                    w.Write("(array ");
                    WriteFieldType(w, at.ElementType);
                    w.Write(')');
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unknown CompositeType {body?.GetType().Name}");
            }
        }

        /// <summary>
        /// Emit a <see cref="FieldType"/>: <c>(mut T)</c> when mutable,
        /// bare <c>T</c> when immutable. Packed storage types
        /// (<c>i8</c>, <c>i16</c>) round-trip via <see cref="ToWatValType"/>.
        /// </summary>
        private static void WriteFieldType(TextWriter w, FieldType ft)
        {
            if (ft.Mut == Mutability.Mutable)
                w.Write($"(mut {ToWatValType(ft.StorageType)})");
            else
                w.Write(ToWatValType(ft.StorageType));
        }

        // ---- Element segments ---------------------------------------------

        /// <summary>
        /// Emit a single <c>(elem …)</c> top-level form. Three modes
        /// (active, passive, declarative) cross four representations
        /// (func-shortcut vs reftype-expr-vector; default vs explicit
        /// table index), giving the eight wire-form combinations the
        /// binary parser handles. The text writer collapses to the
        /// most concise WAT that re-parses to the same segment.
        /// </summary>
        private static void WriteElementSegment(
            TextWriter w, Module.ElementSegment seg, string indent)
        {
            // Detect the func-shortcut subform: all initializers are a
            // single ref.func, and the segment type is FuncRef-family.
            // If yes, we can emit the compact `func 0 1 2` form.
            bool useFuncShortcut = IsAllRefFunc(seg);

            w.Write($"{indent}(elem");

            switch (seg.Mode)
            {
                case Module.ElementMode.PassiveMode:
                    // (elem reftype (item ...) (item ...))
                    if (useFuncShortcut)
                    {
                        w.Write(" func");
                        AppendFuncIdxList(w, seg);
                    }
                    else
                    {
                        w.Write(' ');
                        w.Write(ToWatValType(seg.Type));
                        AppendInitExprList(w, seg);
                    }
                    break;

                case Module.ElementMode.DeclarativeMode:
                    if (useFuncShortcut)
                    {
                        w.Write(" declare func");
                        AppendFuncIdxList(w, seg);
                    }
                    else
                    {
                        w.Write(" declare ");
                        w.Write(ToWatValType(seg.Type));
                        AppendInitExprList(w, seg);
                    }
                    break;

                case Module.ElementMode.ActiveMode am:
                {
                    if (am.TableIndex.Value != 0)
                        w.Write($" (table {am.TableIndex.Value})");
                    w.Write($" (offset{am.Offset.ToWat()})");
                    if (useFuncShortcut)
                    {
                        w.Write(" func");
                        AppendFuncIdxList(w, seg);
                    }
                    else
                    {
                        w.Write(' ');
                        w.Write(ToWatValType(seg.Type));
                        AppendInitExprList(w, seg);
                    }
                    break;
                }

                default:
                    throw new InvalidDataException(
                        $"Unknown ElementMode {seg.Mode?.GetType().Name}");
            }

            w.WriteLine(')');
        }

        private static bool IsAllRefFunc(Module.ElementSegment seg)
        {
            // Func-shortcut only applies to FuncRef-family element
            // types; otherwise the reftype must be spelled explicitly.
            if (seg.Type != ValType.Func && seg.Type != ValType.FuncRef)
                return false;
            foreach (var expr in seg.Initializers)
            {
                var insts = expr.Instructions;
                if (insts.Count < 1) return false;
                if (insts[0] is not Wacs.Core.Instructions.Reference.InstRefFunc)
                    return false;
            }
            return true;
        }

        private static void AppendFuncIdxList(TextWriter w, Module.ElementSegment seg)
        {
            foreach (var expr in seg.Initializers)
            {
                var rf = expr.Instructions[0]
                    as Wacs.Core.Instructions.Reference.InstRefFunc;
                if (rf == null)
                    throw new InvalidDataException(
                        "Element initializer was expected to be ref.func");
                w.Write($" {rf.FunctionIndex.Value}");
            }
        }

        private static void AppendInitExprList(TextWriter w, Module.ElementSegment seg)
        {
            foreach (var expr in seg.Initializers)
            {
                // (item (instr…)) form.
                w.Write(" (item");
                w.Write(expr.ToWat());
                w.Write(')');
            }
        }

        // ---- Data segments ------------------------------------------------

        /// <summary>
        /// Emit a single <c>(data …)</c> top-level form. Three modes:
        /// active-default-memory, active-explicit-memory, and passive.
        /// The byte payload is emitted via
        /// <see cref="Wacs.Core.Utilities.BytesEncoder.EncodeToWatString"/>
        /// (the existing canonical escape).
        /// </summary>
        private static void WriteDataSegment(
            TextWriter w, Module.Data data, string indent)
        {
            string bytes = Wacs.Core.Utilities.BytesEncoder
                .EncodeToWatString(data.Init ?? System.Array.Empty<byte>());

            w.Write($"{indent}(data");
            switch (data.Mode)
            {
                case Module.DataMode.PassiveMode:
                    w.Write(' ');
                    w.Write(bytes);
                    break;
                case Module.DataMode.ActiveMode am:
                    if (am.MemoryIndex.Value != 0)
                        w.Write($" (memory {am.MemoryIndex.Value})");
                    w.Write($" (offset{am.Offset.ToWat()}) ");
                    w.Write(bytes);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unknown DataMode {data.Mode?.GetType().Name}");
            }
            w.WriteLine(')');
        }
    }
}
