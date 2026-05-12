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
            var (lex, top, trivia) = SExprParser.ParseWithTrivia(source);
            if (top.Count != 1 || !top[0].IsForm("module"))
                throw new FormatException("expected a single top-level (module ...) form; use ParseWast for .wast scripts");
            return ParseModule(top[0], lex, trivia);
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
        internal static Module ParseModule(SExpr moduleForm) =>
            ParseModule(moduleForm, lexer: null, trivia: null);

        /// <summary>
        /// Trivia-aware overload. When <paramref name="trivia"/> is
        /// non-null, the parser drains the side-band list into
        /// <see cref="Module.Comments"/> as it walks each module-level
        /// form. <c>(@…)</c> annotations route into
        /// <see cref="Module.Annotations"/> instead of being silently
        /// dropped.
        /// </summary>
        internal static Module ParseModule(
            SExpr moduleForm, Lexer? lexer, List<TriviaToken>? trivia)
        {
            if (!moduleForm.IsForm("module"))
                throw new FormatException($"expected (module ...), got {moduleForm.Head}");

            var ctx = new TextParseContext();
            if (lexer != null) ctx.Lexer = lexer;
            if (trivia != null) ctx.Trivia = trivia;

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

            // Pass 1a: pre-declare every named entity across all namespaces
            // so forward references inside function bodies, elem / data
            // initializers, exports etc. resolve cleanly.
            int startOfSections = i;
            PreDeclareNames(ctx, moduleForm, startOfSections);

            // Drain trivia from BEFORE the (module form opener — those
            // top-of-file comments attach to module-level, not to the
            // first section. The per-section drain handles everything
            // inside the module body.
            ctx.DrainTriviaBefore(moduleForm.Token.Start, ModuleElementRef.ModuleLevel);

            // Pass 1b: fully parse all explicit (type …) and (rec (type)…)
            // forms and add them to Module.Types. Matches the binary
            // encoder's convention of emitting explicit types first;
            // inline typeuses from pass 2 get synthesized AFTER.
            PrePopulateTypes(ctx, moduleForm, startOfSections);

            // Pass 2: full parse. Section parsers no longer re-Declare
            // names — they look up the index pre-assigned in pass 1 and
            // just populate the Module's per-section collections.
            //
            // Per-section index counters mirror the pre-scan in
            // PreDeclareNames so each form's ModuleElementRef matches
            // the index the section parser assigns. Used to attach
            // leading trivia (comments before the form's opening
            // paren) to the correct section element.
            int sTypeIdx = 0, sImportIdx = 0, sFuncIdx = 0, sTableIdx = 0,
                sMemIdx = 0, sGlobalIdx = 0, sExportIdx = 0,
                sElemIdx = 0, sDataIdx = 0, sTagIdx = 0;

            for (i = startOfSections; i < moduleForm.Children.Count; i++)
            {
                var form = moduleForm.Children[i];
                if (form.Kind != SExprKind.List)
                    throw new FormatException($"line {form.Token.Line}: expected section form, got atom");
                var head = form.Head;
                if (head == null || head.Kind != SExprKind.Atom)
                    throw new FormatException($"line {form.Token.Line}: section form must start with a keyword");

                // Module-level WAT annotations `(@name …)`. Pass 3
                // captures them onto Module.Annotations so the writer
                // can re-emit. Branch_hint specifically: per spec the
                // annotation must appear inside a function body;
                // module-level use is malformed. Reject when branch-
                // hint parsing is on so embedders see the diagnostic
                // instead of silently mis-emitting; tolerate when off
                // so existing inputs don't regress.
                if (head.Token.Kind == TokenKind.Reserved && head.AtomText().StartsWith("@"))
                {
                    string annotName = head.AtomText();
                    if (BinaryModuleParser.ParseBranchHints
                        && annotName == "@metadata.code.branch_hint")
                        throw new FormatException(
                            $"line {head.Token.Line}: @metadata.code.branch_hint annotation: not in a function");

                    // Capture into Module.Annotations at module-level.
                    // The payload is the raw text between the
                    // annotation name and the closing paren — opaque
                    // to us, but the writer can re-emit it verbatim.
                    if (ctx.Lexer != null)
                    {
                        string payload = SliceAnnotationPayload(ctx.Lexer, form);
                        ctx.Module.AddAnnotation(ModuleElementRef.ModuleLevel,
                            new WatAnnotation
                            {
                                Name = annotName.TrimStart('@'),
                                Payload = payload,
                                Line = head.Token.Line,
                                Column = head.Token.Column,
                            });
                    }
                    continue;
                }
                if (head.Token.Kind != TokenKind.Keyword)
                    throw new FormatException($"line {form.Token.Line}: section form must start with a keyword");

                var name = head.AtomText();
                // Determine the owner for any leading trivia BEFORE
                // we walk into the section parser. The index counter
                // for the matching strata is what the section parser
                // is about to consume.
                ModuleElementRef owner = name switch
                {
                    "type"   => new ModuleElementRef(ModuleElementKind.Type,    sTypeIdx),
                    "rec"    => new ModuleElementRef(ModuleElementKind.Type,    sTypeIdx),
                    "import" => new ModuleElementRef(ModuleElementKind.Import,  sImportIdx),
                    "func"   => new ModuleElementRef(ModuleElementKind.Function,sFuncIdx),
                    "table"  => new ModuleElementRef(ModuleElementKind.Table,   sTableIdx),
                    "memory" => new ModuleElementRef(ModuleElementKind.Memory,  sMemIdx),
                    "global" => new ModuleElementRef(ModuleElementKind.Global,  sGlobalIdx),
                    "export" => new ModuleElementRef(ModuleElementKind.Export,  sExportIdx),
                    "start"  => new ModuleElementRef(ModuleElementKind.Start),
                    "elem"   => new ModuleElementRef(ModuleElementKind.Element, sElemIdx),
                    "data"   => new ModuleElementRef(ModuleElementKind.Data,    sDataIdx),
                    "tag"    => new ModuleElementRef(ModuleElementKind.Tag,     sTagIdx),
                    _        => ModuleElementRef.ModuleLevel,
                };
                ctx.DrainTriviaBefore(form.Token.Start, owner);

                switch (name)
                {
                    // Explicit type forms were populated in pass 1b.
                    case "type":    sTypeIdx++; break;
                    case "rec":
                    {
                        // A rec wrapper consumes one type slot per
                        // inner (type …) child.
                        for (int j = 1; j < form.Children.Count; j++)
                            if (form.Children[j].Kind == SExprKind.List
                                && form.Children[j].IsForm("type"))
                                sTypeIdx++;
                        break;
                    }
                    case "import":  ParseImportForm(ctx, form); sImportIdx++; break;
                    case "func":    ParseFuncForm(ctx, form); sFuncIdx++; break;
                    case "table":   ParseTableForm(ctx, form); sTableIdx++; break;
                    case "memory":  ParseMemoryForm(ctx, form); sMemIdx++; break;
                    case "global":  ParseGlobalForm(ctx, form); sGlobalIdx++; break;
                    case "export":  ParseExportForm(ctx, form); sExportIdx++; break;
                    case "start":   ParseStartForm(ctx, form); break;
                    case "elem":    ParseElemForm(ctx, form); sElemIdx++; break;
                    case "data":    ParseDataForm(ctx, form); sDataIdx++; break;
                    case "tag":     ParseTagForm(ctx, form); sTagIdx++; break;
                    default:
                        throw new FormatException($"line {form.Token.Line}: unknown module section '{name}'");
                }
            }

            // Comments past the last section / before the closing `)`
            // attach to module-level as trailing trivia.
            ctx.DrainRemainingTrivia();

            FinalizeModule(ctx);
            return ctx.Module;
        }

        /// <summary>
        /// Reconstruct the raw payload text of a <c>(@name payload…)</c>
        /// annotation form. The slice runs from just after the
        /// annotation name's lexeme through (but not including) the
        /// closing paren of the form. Whitespace and inner tokens are
        /// preserved verbatim — the writer round-trips them as-is.
        /// </summary>
        private static string SliceAnnotationPayload(Lexer lexer, SExpr form)
        {
            // form.Children[0] is the `@name` atom. The form's opening
            // paren is form.Token. We need everything between the end
            // of the name atom and the matching `)`. Easiest: walk to
            // the last source position covered by any child, and
            // slice from name-end to one char before the closing `)`.
            var name = form.Children[0].Token;
            int payloadStart = name.Start + name.Length;
            int payloadEnd = SourceEnd(form);
            int len = payloadEnd - payloadStart;
            if (len <= 0) return string.Empty;
            string raw = lexer.Source.Substring(payloadStart, len);
            return raw.TrimStart();
        }

        /// <summary>
        /// The 1-past-the-end source position of an SExpr node. For an
        /// atom that's <c>Token.Start + Token.Length</c>; for a list
        /// it's one past the position of the closing <c>)</c>, which
        /// we approximate via the deepest descendant's end.
        /// </summary>
        private static int SourceEnd(SExpr node)
        {
            if (node.Kind == SExprKind.Atom)
                return node.Token.Start + node.Token.Length;
            int end = node.Token.Start + 1; // past the '('
            foreach (var c in node.Children)
            {
                int ce = SourceEnd(c);
                if (ce > end) end = ce;
            }
            return end;
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
        /// Pass 1b: parse every explicit <c>(type …)</c> and <c>(rec …)</c>
        /// form up front so Module.Types is fully populated with declared
        /// types before pass 2 starts synthesizing inline typeuses. This
        /// matches the binary encoder's convention of emitting explicit
        /// types first.
        /// </summary>
        private static void PrePopulateTypes(TextParseContext ctx, SExpr moduleForm, int sectionStart)
        {
            for (int i = sectionStart; i < moduleForm.Children.Count; i++)
            {
                var form = moduleForm.Children[i];
                if (form.Kind != SExprKind.List) continue;
                var head = form.Head;
                if (head == null || head.Kind != SExprKind.Atom
                    || head.Token.Kind != TokenKind.Keyword) continue;
                switch (head.AtomText())
                {
                    case "type": ParseTypeForm(ctx, form); break;
                    case "rec":  ParseRecTypeForm(ctx, form); break;
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

            // Mirror the binary parser's ref.func declaration walk so
            // (elem declare func $f) actually flips $f.ElementDeclared.
            // Without this, every ref.func inside a function body fails
            // validation with "func N is not fully declared".
            BinaryModuleParser.PropagateRefFuncDeclarations(module);

            // Synthesize the `name` custom section from per-function
            // $name identifiers so binary serializers can round-trip
            // the names back out. Without this, `wacs inspect
            // --dump-wasm` strips every $name. The synthesis runs
            // only when at least one function carries a name; pure-
            // numeric modules leave Module.Names null.
            SynthesizeNameSection(module);
        }

        /// <summary>
        /// Populate <see cref="Module.Names"/> from per-function
        /// <see cref="Module.Function.Id"/> so the binary writer can
        /// emit a `name` custom section. The convention matches
        /// what wabt / wasm-tools produces: names are stored without
        /// the leading `$`. Indices include imported functions
        /// (spec name-section index space).
        /// </summary>
        private static void SynthesizeNameSection(Module module)
        {
            // Walk imported + defined functions in spec order. For
            // imports we don't have a Function.Id on the import
            // descriptor (that's only on Module.Function objects),
            // so they get the import's entity name as a name-section
            // label — matches what tools emit for round-trip.
            var funcNames = new System.Collections.Generic.Dictionary<uint, string>();
            uint idx = 0;
            foreach (var imp in module.Imports)
            {
                if (imp.Desc is Module.ImportDesc.FuncDesc)
                {
                    // Use the import's "name" half as the name-section
                    // label for the imported function.
                    if (!string.IsNullOrEmpty(imp.Name))
                        funcNames[idx] = imp.Name;
                    idx++;
                }
            }
            foreach (var fn in module.Funcs)
            {
                // Strip the leading `$` (WAT identifier marker) so the
                // emitted name section matches wabt-style output.
                if (!string.IsNullOrEmpty(fn.Id))
                {
                    string raw = fn.Id;
                    if (raw.Length > 1 && raw[0] == '$') raw = raw.Substring(1);
                    funcNames[idx] = raw;
                }
                idx++;
            }

            if (funcNames.Count == 0) return;

            module.Names ??= new Module.NameSection();
            module.Names.FunctionNames = new Module.NameSubsection.FuncNameSubsection
            {
                Names = new Module.NameMap { NameAssocMap = funcNames },
            };
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
