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
                // structural, but render / debugging code reads Module.Names
                // or similar. Ignored at Phase 1.3; Phase 1.6 will thread it
                // into the Names section if/when we need round-trip fidelity.
                i++;
            }

            // Single-pass section dispatch. Order of section forms in a WAT
            // source does not have to match binary section order — WAT permits
            // interleaving; we collect per-section and flush at the end. For
            // now we dispatch immediately and preserve declaration order; the
            // spec allows this.
            for (; i < moduleForm.Children.Count; i++)
            {
                var form = moduleForm.Children[i];
                if (form.Kind != SExprKind.List)
                    throw new FormatException($"line {form.Token.Line}: expected section form, got atom");
                var head = form.Head;
                if (head == null || head.Kind != SExprKind.Atom || head.Token.Kind != TokenKind.Keyword)
                    throw new FormatException($"line {form.Token.Line}: section form must start with a keyword");

                var name = head.AtomText();
                switch (name)
                {
                    case "type":    ParseTypeForm(ctx, form); break;
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

        private static void FinalizeModule(TextParseContext ctx)
        {
            // Placeholder — post-parse wiring (assigning Function.Index,
            // linking Codes, running the same FinalizeModule the binary
            // parser runs) lives here. Phase 1.4 will populate most of it
            // once instruction bodies are parsed.
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
