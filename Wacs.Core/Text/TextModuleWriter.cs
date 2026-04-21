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
    /// Round-trip WAT renderer — emits a canonical, parser-friendly
    /// representation of a <see cref="Module"/> that
    /// <see cref="TextModuleParser"/> can re-parse to a structurally-
    /// equivalent <see cref="Module"/>.
    ///
    /// <para>Distinct from <see cref="ModuleRenderer"/>, which is
    /// debug/display-oriented (stack annotations, <c>(;id;)</c> comments,
    /// etc.). This writer targets parseability, not visual polish.</para>
    /// </summary>
    public static class TextModuleWriter
    {
        public static string Write(Module module)
        {
            var sb = new StringBuilder();
            using var w = new StringWriter(sb);
            WriteTo(w, module);
            return sb.ToString();
        }

        public static void WriteTo(TextWriter w, Module module)
        {
            w.WriteLine("(module");
            var indent = "  ";

            // Types
            for (int t = 0; t < module.Types.Count; t++)
            {
                var ft = module.Types[t].SubTypes[0].Body as FunctionType;
                if (ft == null)
                {
                    // GC struct/array — phase 2 scope doesn't cover these.
                    // Emit a bare comment placeholder so the index still
                    // lines up on round-trip.
                    w.WriteLine($"{indent};; (type ...) (GC non-func body not supported by round-trip)");
                    continue;
                }
                w.Write($"{indent}(type (func");
                WriteParams(w, ft.ParameterTypes);
                WriteResults(w, ft.ResultType);
                w.WriteLine("))");
            }

            // Imports
            foreach (var import in module.Imports)
                WriteImport(w, module, import, indent);

            // Functions (defined; imports skipped — handled above)
            int fimportCount = module.ImportedFunctions.Count;
            for (int i = 0; i < module.Funcs.Count; i++)
                WriteFunc(w, module, module.Funcs[i], fimportCount + i, indent);

            // Tables / Memories / Globals (defined)
            int timportCount = module.ImportedTables.Count;
            foreach (var table in module.Tables)
                WriteTable(w, table, indent);

            int mimportCount = module.ImportedMems.Count;
            foreach (var mem in module.Memories)
                WriteMemory(w, mem, indent);

            int gimportCount = module.ImportedGlobals.Count;
            foreach (var g in module.Globals)
                WriteGlobal(w, module, g, indent);

            // Exports
            foreach (var e in module.Exports)
                WriteExport(w, e, indent);

            // Start
            if (module.StartIndex != FuncIdx.Default)
                w.WriteLine($"{indent}(start {module.StartIndex.Value})");

            // Elem / Data — phase 2 leaves these as comments since Phase 1
            // doesn't fully populate them.
            foreach (var _ in module.Elements)
                w.WriteLine($"{indent};; (elem …) (round-trip not supported in phase 2)");
            foreach (var _ in module.Datas)
                w.WriteLine($"{indent};; (data …) (round-trip not supported in phase 2)");

            w.WriteLine(")");
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

        private static void WriteFunc(TextWriter w, Module m, Module.Function fn, int absIdx, string indent)
        {
            w.Write($"{indent}(func (type {fn.TypeIndex.Value})");
            if (fn.Locals != null && fn.Locals.Length > 0)
            {
                w.Write(" (local");
                foreach (var t in fn.Locals)
                    w.Write($" {ToWatValType(t)}");
                w.Write(")");
            }
            w.WriteLine();
            // Body
            WriteInstructionSeq(w, fn.Body.Instructions, indent + "  ", trimTrailingEnd: true);
            w.WriteLine($"{indent})");
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
    }
}
