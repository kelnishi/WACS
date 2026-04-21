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

namespace Wacs.Core.Text
{
    /// <summary>
    /// Parses a WebAssembly text-format source into a <see cref="Module"/>.
    /// Structurally-equivalent to <c>BinaryModuleParser.ParseWasm</c> — the
    /// resulting <see cref="Module"/> passes through the existing validation
    /// and instantiation pipeline unchanged.
    ///
    /// <para>Phase 1.3 scope: module shell + all sections at the structural
    /// level (types, imports, funcs signatures, tables, memories, globals,
    /// exports, start, elems, datas, tags). Function bodies and init
    /// expressions are parsed in Phase 1.4.</para>
    /// </summary>
    public static partial class TextModuleParser
    {
        public static Module ParseWat(string source)
        {
            var top = SExprParser.Parse(source);
            if (top.Count != 1 || !top[0].IsForm("module"))
                throw new FormatException("expected a single top-level (module ...) form; use ParseWast for .wast scripts");
            return ParseModule(top[0]);
        }

        public static Module ParseWat(Stream stream)
        {
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            return ParseWat(reader.ReadToEnd());
        }

        /// <summary>
        /// Parse a single <c>(module ...)</c> s-expression node. Exposed so a
        /// future WAST script parser (phase 1.5) can call this per embedded
        /// module without re-tokenizing.
        /// </summary>
        internal static Module ParseModule(SExpr moduleForm)
        {
            if (!moduleForm.IsForm("module"))
                throw new FormatException($"expected (module ...), got {moduleForm.Head}");

            var ctx = new TextParseContext();

            // First child is the `module` head; optional second child is an
            // $id; remaining children are sections.
            int i = 1;
            if (i < moduleForm.Children.Count
                && moduleForm.Children[i].Kind == SExprKind.Atom
                && moduleForm.Children[i].Token.Kind == TokenKind.Id)
            {
                // Module name — binary modules don't surface this anywhere
                // structural. Round-trip into Names is a follow-up.
                i++;
            }
            // Component-model extension: `(module definition $id …)` and
            // `(module instance $id $src)` — the "instance" form is an
            // instantiation reference, not a module definition. Skip
            // entirely; return an empty Module.
            if (i < moduleForm.Children.Count
                && moduleForm.Children[i].Kind == SExprKind.Atom
                && moduleForm.Children[i].Token.Kind == TokenKind.Keyword)
            {
                var mk = moduleForm.Children[i].AtomText();
                if (mk == "instance")
                {
                    FinalizeModule(ctx);
                    return ctx.Module;
                }
                if (mk == "definition")
                {
                    i++;
                    // $id may follow the marker
                    if (i < moduleForm.Children.Count
                        && moduleForm.Children[i].Kind == SExprKind.Atom
                        && moduleForm.Children[i].Token.Kind == TokenKind.Id) i++;
                }
            }

            // Pass 1: pre-declare every named entity across all namespaces
            // so forward references inside function bodies, elem / data
            // initializers, exports etc. resolve cleanly. Anonymous
            // entities get reserved slots so indices stay contiguous.
            int startOfSections = i;
            PreDeclareNames(ctx, moduleForm, startOfSections);

            // Pass 2: full parse. Section parsers no longer re-Declare
            // names — they look up the index pre-assigned in pass 1 and
            // just populate the Module's per-section collections.
            for (i = startOfSections; i < moduleForm.Children.Count; i++)
            {
                var form = moduleForm.Children[i];
                if (form.Kind != SExprKind.List)
                    throw new FormatException($"line {form.Token.Line}: expected section form, got atom");
                var head = form.Head;
                if (head == null || head.Kind != SExprKind.Atom)
                    throw new FormatException($"line {form.Token.Line}: section form must start with a keyword");

                // Module-level WAT annotations `(@name …)` — round-trip
                // metadata is deferred; for now we just skip them so the
                // module still parses.
                if (head.Token.Kind == TokenKind.Reserved && head.AtomText().StartsWith("@"))
                    continue;
                if (head.Token.Kind != TokenKind.Keyword)
                    throw new FormatException($"line {form.Token.Line}: section form must start with a keyword");

                var name = head.AtomText();
                switch (name)
                {
                    case "type":    ParseTypeForm(ctx, form); break;
                    case "rec":     ParseRecTypeForm(ctx, form); break;
                    case "import":  ParseImportForm(ctx, form); break;
                    case "func":    ParseFuncForm(ctx, form); break;
                    case "table":   ParseTableForm(ctx, form); break;
                    case "memory":  ParseMemoryForm(ctx, form); break;
                    case "global":  ParseGlobalForm(ctx, form); break;
                    case "export":  ParseExportForm(ctx, form); break;
                    case "start":   ParseStartForm(ctx, form); break;
                    case "elem":    ParseElemForm(ctx, form); break;
                    case "data":    ParseDataForm(ctx, form); break;
                    case "tag":     ParseTagForm(ctx, form); break;
                    default:
                        throw new FormatException($"line {form.Token.Line}: unknown module section '{name}'");
                }
            }

            FinalizeModule(ctx);
            return ctx.Module;
        }

        /// <summary>
        /// Pass 1: pre-register named entities in each namespace at the
        /// index they'll receive during pass 2. Lets forward references
        /// inside instruction bodies and initializers resolve cleanly.
        /// Walks in source order; index assignment mirrors what pass 2
        /// would do, so pre-scan's indices match pass 2's indices exactly.
        /// </summary>
        private static void PreDeclareNames(TextParseContext ctx, SExpr moduleForm, int sectionStart)
        {
            int typeIdx = 0, funcIdx = 0, tableIdx = 0, memIdx = 0,
                globalIdx = 0, elemIdx = 0, dataIdx = 0, tagIdx = 0;

            for (int i = sectionStart; i < moduleForm.Children.Count; i++)
            {
                var form = moduleForm.Children[i];
                if (form.Kind != SExprKind.List) continue;
                var head = form.Head;
                if (head == null || head.Kind != SExprKind.Atom) continue;
                // Skip (@annotation …) forms at pre-scan time.
                if (head.Token.Kind == TokenKind.Reserved && head.AtomText().StartsWith("@")) continue;
                if (head.Token.Kind != TokenKind.Keyword) continue;
                switch (head.AtomText())
                {
                    case "type":   PreRegisterNamed(ctx.Types, form, typeIdx++); break;
                    case "rec":
                    {
                        // Each inner (type $id? …) form consumes a type
                        // slot in pre-scan order.
                        for (int j = 1; j < form.Children.Count; j++)
                        {
                            var inner = form.Children[j];
                            if (inner.Kind == SExprKind.List && inner.IsForm("type"))
                                PreRegisterNamed(ctx.Types, inner, typeIdx++);
                        }
                        break;
                    }
                    case "import":
                    {
                        // (import "m" "n" (kind $id? ...))
                        if (form.Children.Count >= 4)
                        {
                            var desc = form.Children[3];
                            if (desc.Kind == SExprKind.List && desc.Head != null
                                && desc.Head.Token.Kind == TokenKind.Keyword)
                            {
                                switch (desc.Head.AtomText())
                                {
                                    case "func":   PreRegisterNamed(ctx.Funcs,  desc, funcIdx++); break;
                                    case "table":  PreRegisterNamed(ctx.Tables, desc, tableIdx++); break;
                                    case "memory": PreRegisterNamed(ctx.Mems,   desc, memIdx++); break;
                                    case "global": PreRegisterNamed(ctx.Globals,desc, globalIdx++); break;
                                    case "tag":    PreRegisterNamed(ctx.Tags,   desc, tagIdx++); break;
                                }
                            }
                        }
                        break;
                    }
                    case "func":   PreRegisterNamed(ctx.Funcs,  form, funcIdx++); break;
                    case "table":  PreRegisterNamed(ctx.Tables, form, tableIdx++); break;
                    case "memory": PreRegisterNamed(ctx.Mems,   form, memIdx++); break;
                    case "global": PreRegisterNamed(ctx.Globals,form, globalIdx++); break;
                    case "elem":   PreRegisterNamed(ctx.Elems,  form, elemIdx++); break;
                    case "data":   PreRegisterNamed(ctx.Datas,  form, dataIdx++); break;
                    case "tag":    PreRegisterNamed(ctx.Tags,   form, tagIdx++); break;
                }
            }
        }

        /// <summary>
        /// If <paramref name="form"/> has a <c>$id</c> atom immediately
        /// after its head, register it in <paramref name="table"/> at
        /// <paramref name="index"/>.
        /// </summary>
        private static void PreRegisterNamed(NameTable table, SExpr form, int index)
        {
            if (form.Children.Count >= 2
                && form.Children[1].Kind == SExprKind.Atom
                && form.Children[1].Token.Kind == TokenKind.Id)
            {
                table.PrereserveName(form.Children[1].AtomText(), index);
            }
        }

        private static void FinalizeModule(TextParseContext ctx)
        {
            // Post-parse wiring matching what the binary parser does in its
            // FinalizeModule — gets the Module to a state the runtime can
            // instantiate.
            var module = ctx.Module;

            // Assign FuncIdx to every defined function, starting after
            // imported function slots (spec index space ordering).
            int fIdx = module.ImportedFunctions.Count;
            foreach (var fn in module.Funcs)
                fn.Index = (Wacs.Core.Types.FuncIdx)fIdx++;

            // The binary parser defaults DataCount when no DataCount
            // section was present; mirror here so runtime instantiation
            // doesn't assert on uint.MaxValue.
            if (module.DataCount == uint.MaxValue)
                module.DataCount = (uint)module.Datas.Length;
        }

        // ---- Helpers shared across section parsers ------------------------

        /// <summary>
        /// Reads an optional leading $id atom from a form's children. Returns
        /// null if absent; returns the id lexeme (including the $) if
        /// present. Advances <paramref name="index"/> past the consumed atom.
        /// </summary>
        internal static string? TryReadIdAt(SExpr form, ref int index)
        {
            if (index >= form.Children.Count) return null;
            var child = form.Children[index];
            if (child.Kind != SExprKind.Atom) return null;
            if (child.Token.Kind != TokenKind.Id) return null;
            index++;
            return child.AtomText();
        }

        /// <summary>
        /// Assert there are no more children beyond <paramref name="index"/>.
        /// Used as a post-condition for forms that should be fully consumed.
        /// </summary>
        internal static void ExpectConsumed(SExpr form, int index, string formName)
        {
            if (index < form.Children.Count)
            {
                var extra = form.Children[index];
                throw new FormatException(
                    $"line {extra.Token.Line}: unexpected child in ({formName} …): {extra}");
            }
        }
    }
}
