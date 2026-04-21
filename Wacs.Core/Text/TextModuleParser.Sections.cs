// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Text
{
    public static partial class TextModuleParser
    {
        // ---- (import "mod" "name" desc) -----------------------------------

        private static void ParseImportForm(TextParseContext ctx, SExpr form)
        {
            int i = 1;
            if (i + 1 >= form.Children.Count)
                throw new FormatException($"line {form.Token.Line}: (import) missing module/name strings");
            var modTok = form.Children[i++].Token;
            var nameTok = form.Children[i++].Token;
            if (modTok.Kind != TokenKind.String || nameTok.Kind != TokenKind.String)
                throw new FormatException($"line {form.Token.Line}: (import) expects two strings");

            if (i >= form.Children.Count)
                throw new FormatException($"line {form.Token.Line}: (import) missing descriptor");
            var descForm = form.Children[i++];
            ExpectConsumed(form, i, "import");

            if (descForm.Kind != SExprKind.List || descForm.Head == null)
                throw new FormatException($"line {descForm.Token.Line}: (import) descriptor must be a form");

            var desc = ParseImportDesc(ctx, descForm);
            var import = new Module.Import
            {
                ModuleName = DecodeStringToUtf8(form.Lexer, modTok),
                Name = DecodeStringToUtf8(form.Lexer, nameTok),
                Desc = desc,
            };

            AppendImport(ctx, import);
        }

        private static Module.ImportDesc ParseImportDesc(TextParseContext ctx, SExpr form)
        {
            var head = form.Head!;
            var kind = head.AtomText();
            int i = 1;
            var name = TryReadIdAt(form, ref i);

            switch (kind)
            {
                case "func":
                {
                    int typeIdx = ParseExplicitTypeUse(ctx, form, ref i);
                    // TODO phase 1.4: allow inline (param ...) / (result ...)
                    // syntactic sugar instead of requiring (type $n).
                    ExpectConsumed(form, i, "func (import)");
                    ctx.Funcs.Declare(name);
                    return new Module.ImportDesc.FuncDesc { TypeIndex = (TypeIdx)typeIdx };
                }
                case "table":
                {
                    var (elementType, limits) = ParseTableTypeInline(ctx, form, ref i);
                    ExpectConsumed(form, i, "table (import)");
                    ctx.Tables.Declare(name);
                    return new Module.ImportDesc.TableDesc
                    {
                        TableDef = new TableType(elementType, limits)
                    };
                }
                case "memory":
                {
                    var limits = ParseLimitsInline(form, ref i);
                    ExpectConsumed(form, i, "memory (import)");
                    ctx.Mems.Declare(name);
                    return new Module.ImportDesc.MemDesc
                    {
                        MemDef = new MemoryType(limits)
                    };
                }
                case "global":
                {
                    var gt = ParseGlobalTypeInline(ctx, form, ref i);
                    ExpectConsumed(form, i, "global (import)");
                    ctx.Globals.Declare(name);
                    return new Module.ImportDesc.GlobalDesc { GlobalDef = gt };
                }
                case "tag":
                {
                    int typeIdx = ParseExplicitTypeUse(ctx, form, ref i);
                    ExpectConsumed(form, i, "tag (import)");
                    ctx.Tags.Declare(name);
                    return new Module.ImportDesc.TagDesc
                    {
                        TagDef = new TagType(TagTypeAttribute.Exception, (TypeIdx)typeIdx)
                    };
                }
                default:
                    throw new FormatException(
                        $"line {head.Token.Line}: unknown import descriptor '{kind}'");
            }
        }

        // ---- (func $id? typeuse (local …)* body) --------------------------

        private static void ParseFuncForm(TextParseContext ctx, SExpr form)
        {
            int i = 1;
            var name = TryReadIdAt(form, ref i);

            // Inline (import "mod" "name") abbreviation:
            //   (func $f (import "m" "n") typeuse) == (import "m" "n" (func $f typeuse))
            if (TryConsumeInlineImport(ctx, form, ref i, name, "func", out var importFor))
                return;

            // Inline (export "name") annotations may appear after the $id.
            // Each export expands to a free-standing Module.Export at the
            // resolved funcidx.
            var pendingExports = ConsumePendingExports(form, ref i);

            // Require an explicit (type $n) header. Inline typeuse (raw
            // param/result without a (type …) header) is deferred to a
            // follow-up pass that would synthesize a type into Module.Types.
            int typeIdx = ParseExplicitTypeUse(ctx, form, ref i);

            // Build a function-scope context for the body. Params come from
            // the referenced FunctionType; declared locals follow via
            // (local $name? T*) forms.
            var fctx = new TextFunctionContext(ctx);
            FunctionType ft = ctx.Module.Types[typeIdx];

            // Optional redundant (param $x T)* / (result T)* forms after the
            // (type $n) header — WAT spec: these must be structurally equal
            // to the referenced type, and their purpose is to introduce
            // $names for parameters (the type itself is anonymous). Pull
            // param $names from here; ignore result annotations (no naming).
            var paramNames = ConsumeRedundantParamResultForms(ctx, form, ref i);
            int paramCount = ft.ParameterTypes.Arity;
            for (int p = 0; p < paramCount; p++)
            {
                string? paramName = (paramNames != null && p < paramNames.Count) ? paramNames[p] : null;
                fctx.LocalNames.Add(paramName);
                fctx.LocalTypes.Add(ft.ParameterTypes.Types[p]);
            }

            // Consume local declarations: (local $name? T+) forms may appear
            // interleaved at the start of the body.
            while (i < form.Children.Count)
            {
                var child = form.Children[i];
                if (child.Kind != SExprKind.List || !child.IsForm("local")) break;
                ParseLocalForm(ctx, child, fctx);
                i++;
            }

            // Remaining children are the body — parse as an expression.
            var body = ParseExpressionBody(fctx, form, ref i, ft.ResultType.Arity, isStatic: false);

            var fn = new Module.Function
            {
                TypeIndex = (TypeIdx)typeIdx,
                Locals = fctx.LocalTypes.GetRange(ft.ParameterTypes.Arity,
                    fctx.LocalTypes.Count - ft.ParameterTypes.Arity).ToArray(),
                Body = body,
            };
            int funcIdx = ctx.Funcs.Declare(name);
            ctx.Module.Funcs.Add(fn);

            FlushExports(ctx, pendingExports, ExternalKind.Function, funcIdx);
        }

        /// <summary>
        /// After a <c>(type $n)</c> header, WAT lets you repeat
        /// <c>(param $id? T*)</c> and <c>(result T*)</c> forms for
        /// documentation and, critically, to bind <c>$id</c> names to
        /// function parameters (the type itself is anonymous). Reads and
        /// discards the redundant forms, returning just the param $names
        /// in declaration order (null for anonymous params).
        /// </summary>
        private static List<string?>? ConsumeRedundantParamResultForms(
            TextParseContext ctx, SExpr form, ref int i)
        {
            List<string?>? names = null;
            while (i < form.Children.Count)
            {
                var child = form.Children[i];
                if (child.Kind != SExprKind.List) break;
                if (child.IsForm("param"))
                {
                    names ??= new List<string?>();
                    CollectParamNames(child, names);
                    i++;
                    continue;
                }
                if (child.IsForm("result"))
                {
                    // No names to collect from result forms.
                    i++;
                    continue;
                }
                break;
            }
            return names;
        }

        private static void CollectParamNames(SExpr paramForm, List<string?> names)
        {
            int i = 1;
            // Named single-param form: (param $x T)
            if (i < paramForm.Children.Count
                && paramForm.Children[i].Kind == SExprKind.Atom
                && paramForm.Children[i].Token.Kind == TokenKind.Id)
            {
                names.Add(paramForm.Children[i].AtomText());
                return;
            }
            // Anonymous run — one entry per type child.
            for (; i < paramForm.Children.Count; i++)
                names.Add(null);
        }

        private static void ParseLocalForm(TextParseContext ctx, SExpr form, TextFunctionContext fctx)
        {
            int i = 1;
            // Named: (local $x T)  (single type)
            if (i < form.Children.Count
                && form.Children[i].Kind == SExprKind.Atom
                && form.Children[i].Token.Kind == TokenKind.Id)
            {
                var name = form.Children[i].AtomText();
                i++;
                if (i >= form.Children.Count)
                    throw new FormatException($"line {form.Token.Line}: (local $id T) missing type");
                var t = ParseValType(ctx, form.Children[i]);
                i++;
                ExpectConsumed(form, i, "local");
                fctx.LocalNames.Add(name);
                fctx.LocalTypes.Add(t);
                return;
            }
            // Anonymous: (local T*)
            for (; i < form.Children.Count; i++)
            {
                var t = ParseValType(ctx, form.Children[i]);
                fctx.LocalNames.Add(null);
                fctx.LocalTypes.Add(t);
            }
        }

        // ---- (table $id? inline-import? inline-export* ...) ---------------

        private static void ParseTableForm(TextParseContext ctx, SExpr form)
        {
            int i = 1;
            var name = TryReadIdAt(form, ref i);

            if (TryConsumeInlineImport(ctx, form, ref i, name, "table", out var _))
                return;

            var pendingExports = ConsumePendingExports(form, ref i);

            // Two forms: (table limits reftype) or (table reftype (elem …))
            // Phase 1.3 handles the first; the second (elem abbreviation) is
            // deferred to phase 1.4 because it writes an elem segment.
            var (elementType, limits) = ParseTableTypeInline(ctx, form, ref i);
            ExpectConsumed(form, i, "table");

            int idx = ctx.Tables.Declare(name);
            ctx.Module.Tables.Add(new TableType(elementType, limits));
            FlushExports(ctx, pendingExports, ExternalKind.Table, idx);
        }

        /// <summary>
        /// Parse an inline tabletype — limits followed by a reftype.
        /// </summary>
        private static (ValType element, Limits limits) ParseTableTypeInline(TextParseContext ctx, SExpr parent, ref int index)
        {
            var limits = ParseLimitsInline(parent, ref index);
            if (index >= parent.Children.Count)
                throw new FormatException($"line {parent.Token.Line}: tabletype missing element type");
            var elem = ParseValType(ctx, parent.Children[index]);
            if (!elem.IsRefType())
                throw new FormatException($"line {parent.Children[index].Token.Line}: table element must be a reftype");
            index++;
            return (elem, limits);
        }

        // ---- (memory ...) -------------------------------------------------

        private static void ParseMemoryForm(TextParseContext ctx, SExpr form)
        {
            int i = 1;
            var name = TryReadIdAt(form, ref i);

            if (TryConsumeInlineImport(ctx, form, ref i, name, "memory", out var _))
                return;

            var pendingExports = ConsumePendingExports(form, ref i);

            // (memory $id? (data …)) — data abbreviation is phase 1.4.
            var limits = ParseLimitsInline(form, ref i);
            ExpectConsumed(form, i, "memory");

            int idx = ctx.Mems.Declare(name);
            ctx.Module.Memories.Add(new MemoryType(limits));
            FlushExports(ctx, pendingExports, ExternalKind.Memory, idx);
        }

        /// <summary>
        /// Parse the numeric limits portion — <c>min [max] [shared]</c>, with
        /// an optional leading <c>i32</c>/<c>i64</c> address-type keyword
        /// (memory64 proposal).
        /// </summary>
        private static Limits ParseLimitsInline(SExpr parent, ref int index)
        {
            var addrType = AddrType.I32;
            if (index < parent.Children.Count
                && parent.Children[index].Kind == SExprKind.Atom
                && parent.Children[index].Token.Kind == TokenKind.Keyword)
            {
                var kw = parent.Children[index].AtomText();
                if (kw == "i32") { addrType = AddrType.I32; index++; }
                else if (kw == "i64") { addrType = AddrType.I64; index++; }
            }

            if (index >= parent.Children.Count)
                throw new FormatException($"line {parent.Token.Line}: limits missing minimum");
            long min = ParseUnsignedInt(parent.Children[index]);
            index++;

            long? max = null;
            if (index < parent.Children.Count
                && parent.Children[index].Kind == SExprKind.Atom
                && parent.Children[index].Token.Kind == TokenKind.Reserved)
            {
                max = ParseUnsignedInt(parent.Children[index]);
                index++;
            }
            bool shared = false;
            if (index < parent.Children.Count
                && parent.Children[index].Kind == SExprKind.Atom
                && parent.Children[index].Token.Kind == TokenKind.Keyword
                && parent.Children[index].AtomText() == "shared")
            {
                shared = true;
                index++;
            }
            return new Limits(addrType, min, max, shared);
        }

        // ---- (global ...) -------------------------------------------------

        private static void ParseGlobalForm(TextParseContext ctx, SExpr form)
        {
            int i = 1;
            var name = TryReadIdAt(form, ref i);

            if (TryConsumeInlineImport(ctx, form, ref i, name, "global", out var _))
                return;

            var pendingExports = ConsumePendingExports(form, ref i);

            var gt = ParseGlobalTypeInline(ctx, form, ref i);

            // Parse the initializer expression — a constant-folded sequence
            // terminating at end-of-form. Globals run in an empty-locals
            // context, so we synthesize a minimal TextFunctionContext.
            var initFctx = new TextFunctionContext(ctx);
            var init = ParseExpressionBody(initFctx, form, ref i, arity: 1, isStatic: true);

            int idx = ctx.Globals.Declare(name);
            // Module.Global's public ctor takes (GlobalType) and defaults the
            // initializer to Expression.Empty. To carry our parsed init, we
            // use the private binary-parser ctor pathway via reflection … or
            // simpler, construct with Expression.Empty then swap. The Global
            // class exposes Initializer as readonly. To set it we'd need to
            // either add an internal ctor or use reflection.
            // For phase 1.4 we just attach via a dedicated path — extend
            // the Global class below to accept an init expression.
            ctx.Module.Globals.Add(CreateGlobalWithInit(gt, init));
            FlushExports(ctx, pendingExports, ExternalKind.Global, idx);
        }

        /// <summary>
        /// Construct a <see cref="Module.Global"/> with both type and
        /// initializer populated. Routes through an internal Global
        /// constructor added for the text-parser path.
        /// </summary>
        private static Module.Global CreateGlobalWithInit(GlobalType gt, Expression init)
            => new Module.Global(gt, init);

        private static GlobalType ParseGlobalTypeInline(TextParseContext ctx, SExpr parent, ref int index)
        {
            if (index >= parent.Children.Count)
                throw new FormatException($"line {parent.Token.Line}: globaltype missing");
            var child = parent.Children[index];
            if (child.IsForm("mut"))
            {
                if (child.Children.Count != 2)
                    throw new FormatException($"line {child.Token.Line}: (mut …) must wrap one valtype");
                var vt = ParseValType(ctx, child.Children[1]);
                index++;
                return new GlobalType(vt, Mutability.Mutable);
            }
            var constType = ParseValType(ctx, child);
            index++;
            return new GlobalType(constType, Mutability.Immutable);
        }

        // ---- (export "name" (kind idx)) -----------------------------------

        private static void ParseExportForm(TextParseContext ctx, SExpr form)
        {
            if (form.Children.Count != 3)
                throw new FormatException($"line {form.Token.Line}: (export …) expects a string and a descriptor");
            var nameTok = form.Children[1].Token;
            if (nameTok.Kind != TokenKind.String)
                throw new FormatException($"line {nameTok.Line}: (export) first operand must be a string");
            var descForm = form.Children[2];
            if (descForm.Kind != SExprKind.List || descForm.Head == null)
                throw new FormatException($"line {descForm.Token.Line}: (export) descriptor must be a form");
            var name = DecodeStringToUtf8(form.Lexer, nameTok);
            var desc = ParseExportDesc(ctx, descForm);
            ctx.Module.Exports = AppendExport(ctx.Module.Exports, new Module.Export { Name = name, Desc = desc });
        }

        private static Module.ExportDesc ParseExportDesc(TextParseContext ctx, SExpr form)
        {
            var kind = form.Head!.AtomText();
            if (form.Children.Count != 2)
                throw new FormatException($"line {form.Token.Line}: ({kind} …) export descriptor needs one index");
            var refAtom = form.Children[1];
            switch (kind)
            {
                case "func":
                {
                    int idx = ResolveIndex(ctx.Funcs, refAtom, "func");
                    return new Module.ExportDesc.FuncDesc { FunctionIndex = (FuncIdx)idx };
                }
                case "table":
                {
                    int idx = ResolveIndex(ctx.Tables, refAtom, "table");
                    return new Module.ExportDesc.TableDesc { TableIndex = (TableIdx)idx };
                }
                case "memory":
                {
                    int idx = ResolveIndex(ctx.Mems, refAtom, "memory");
                    return new Module.ExportDesc.MemDesc { MemoryIndex = (MemIdx)idx };
                }
                case "global":
                {
                    int idx = ResolveIndex(ctx.Globals, refAtom, "global");
                    return new Module.ExportDesc.GlobalDesc { GlobalIndex = (GlobalIdx)idx };
                }
                case "tag":
                {
                    int idx = ResolveIndex(ctx.Tags, refAtom, "tag");
                    return new Module.ExportDesc.TagDesc { TagIndex = (TagIdx)idx };
                }
                default:
                    throw new FormatException(
                        $"line {form.Token.Line}: unknown export kind '{kind}'");
            }
        }

        // ---- (start $f) ---------------------------------------------------

        private static void ParseStartForm(TextParseContext ctx, SExpr form)
        {
            if (form.Children.Count != 2)
                throw new FormatException($"line {form.Token.Line}: (start …) expects one function reference");
            int idx = ResolveIndex(ctx.Funcs, form.Children[1], "func");
            ctx.Module.StartIndex = (FuncIdx)idx;
        }

        // ---- (tag $id? (type $n)) -----------------------------------------

        private static void ParseTagForm(TextParseContext ctx, SExpr form)
        {
            int i = 1;
            var name = TryReadIdAt(form, ref i);
            if (TryConsumeInlineImport(ctx, form, ref i, name, "tag", out var _))
                return;
            var pendingExports = ConsumePendingExports(form, ref i);
            int typeIdx = ParseExplicitTypeUse(ctx, form, ref i);
            ExpectConsumed(form, i, "tag");
            int idx = ctx.Tags.Declare(name);
            ctx.Module.Tags.Add(new TagType(TagTypeAttribute.Exception, (TypeIdx)typeIdx));
            FlushExports(ctx, pendingExports, ExternalKind.Tag, idx);
        }

        // ---- (elem …) / (data …) placeholders -----------------------------

        private static void ParseElemForm(TextParseContext ctx, SExpr form)
        {
            // Deferred to phase 1.4 — element initializers are expression
            // bodies. For phase 1.3 we reserve an index slot with an empty
            // passive segment so downstream order tracking stays coherent.
            int i = 1;
            var name = TryReadIdAt(form, ref i);
            ctx.Elems.Declare(name);
            // Do not populate ctx.Module.Elements — phase 1.4 will fill these
            // in with proper initializer expressions.
        }

        private static void ParseDataForm(TextParseContext ctx, SExpr form)
        {
            // Similar to (elem …) — offset expression + bytes decoded in
            // phase 1.4.
            int i = 1;
            var name = TryReadIdAt(form, ref i);
            ctx.Datas.Declare(name);
        }

        // ---- Helpers ------------------------------------------------------

        /// <summary>
        /// Resolve an index operand from the given namespace. Accepts
        /// <c>$name</c> (resolved via the name table) or a decimal literal.
        /// </summary>
        private static int ResolveIndex(NameTable table, SExpr atom, string ns)
        {
            if (atom.Kind != SExprKind.Atom)
                throw new FormatException($"line {atom.Token.Line}: expected {ns} index");
            var text = atom.AtomText();
            if (atom.Token.Kind == TokenKind.Id)
            {
                if (!table.TryResolve(text, out var idx))
                    throw new FormatException($"line {atom.Token.Line}: unknown {ns} {text}");
                return idx;
            }
            if (atom.Token.Kind != TokenKind.Reserved)
                throw new FormatException($"line {atom.Token.Line}: expected index or name, got {atom}");
            if (!uint.TryParse(text, out var n))
                throw new FormatException($"line {atom.Token.Line}: bad {ns} index '{text}'");
            return (int)n;
        }

        /// <summary>
        /// Parse an unsigned integer atom. Accepts decimal or 0x-prefixed
        /// hex. Allows underscores as digit separators per the WAT spec.
        /// </summary>
        private static long ParseUnsignedInt(SExpr atom)
        {
            if (atom.Kind != SExprKind.Atom)
                throw new FormatException($"line {atom.Token.Line}: expected integer literal");
            var raw = atom.AtomText().Replace("_", "");
            long value;
            if (raw.StartsWith("0x") || raw.StartsWith("0X"))
            {
                if (!long.TryParse(raw.Substring(2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
                    throw new FormatException($"line {atom.Token.Line}: bad hex literal '{atom.AtomText()}'");
            }
            else
            {
                if (!long.TryParse(raw, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
                    throw new FormatException($"line {atom.Token.Line}: bad integer '{atom.AtomText()}'");
            }
            if (value < 0)
                throw new FormatException($"line {atom.Token.Line}: expected unsigned, got {atom.AtomText()}");
            return value;
        }

        /// <summary>
        /// Decode a String-kind token into a UTF-8 .NET string.
        /// </summary>
        private static string DecodeStringToUtf8(Lexer lex, Token tok)
        {
            var bytes = lex.DecodeString(tok);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Insert an import into the module, preserving append order.
        /// </summary>
        private static void AppendImport(TextParseContext ctx, Module.Import import)
        {
            var existing = ctx.Module.Imports;
            var next = new Module.Import[existing.Length + 1];
            Array.Copy(existing, next, existing.Length);
            next[existing.Length] = import;
            ctx.Module.Imports = next;
        }

        private static Module.Export[] AppendExport(Module.Export[] existing, Module.Export e)
        {
            var next = new Module.Export[existing.Length + 1];
            Array.Copy(existing, next, existing.Length);
            next[existing.Length] = e;
            return next;
        }

        /// <summary>
        /// Look ahead for <c>(import "mod" "name")</c> as the next child after
        /// the optional id. If found, build the appropriate ImportDesc,
        /// append to module.Imports, declare in the right namespace, and
        /// return true — the outer form is now fully consumed and the
        /// caller should early-exit.
        /// </summary>
        private static bool TryConsumeInlineImport(
            TextParseContext ctx, SExpr form, ref int index,
            string? name, string kind, out Module.Import? import)
        {
            import = null;
            if (index >= form.Children.Count) return false;
            var child = form.Children[index];
            if (!child.IsForm("import")) return false;
            if (child.Children.Count != 3)
                throw new FormatException($"line {child.Token.Line}: inline (import) needs 2 strings");
            var modTok = child.Children[1].Token;
            var nameTok = child.Children[2].Token;
            if (modTok.Kind != TokenKind.String || nameTok.Kind != TokenKind.String)
                throw new FormatException($"line {child.Token.Line}: inline (import) expects two strings");
            index++;

            // The remainder of the outer form (typeuse / limits / reftype /
            // globaltype) is parsed as the import descriptor proper —
            // synthesize a pseudo-form matching the normal (import …) case.
            Module.ImportDesc desc;
            switch (kind)
            {
                case "func":
                {
                    int typeIdx = ParseExplicitTypeUse(ctx, form, ref index);
                    ExpectConsumed(form, index, "func (inline import)");
                    ctx.Funcs.Declare(name);
                    desc = new Module.ImportDesc.FuncDesc { TypeIndex = (TypeIdx)typeIdx };
                    break;
                }
                case "table":
                {
                    var (elemT, lims) = ParseTableTypeInline(ctx, form, ref index);
                    ExpectConsumed(form, index, "table (inline import)");
                    ctx.Tables.Declare(name);
                    desc = new Module.ImportDesc.TableDesc { TableDef = new TableType(elemT, lims) };
                    break;
                }
                case "memory":
                {
                    var lims = ParseLimitsInline(form, ref index);
                    ExpectConsumed(form, index, "memory (inline import)");
                    ctx.Mems.Declare(name);
                    desc = new Module.ImportDesc.MemDesc { MemDef = new MemoryType(lims) };
                    break;
                }
                case "global":
                {
                    var gt = ParseGlobalTypeInline(ctx, form, ref index);
                    ExpectConsumed(form, index, "global (inline import)");
                    ctx.Globals.Declare(name);
                    desc = new Module.ImportDesc.GlobalDesc { GlobalDef = gt };
                    break;
                }
                case "tag":
                {
                    int typeIdx = ParseExplicitTypeUse(ctx, form, ref index);
                    ExpectConsumed(form, index, "tag (inline import)");
                    ctx.Tags.Declare(name);
                    desc = new Module.ImportDesc.TagDesc { TagDef = new TagType(TagTypeAttribute.Exception, (TypeIdx)typeIdx) };
                    break;
                }
                default:
                    throw new InvalidOperationException($"inline import for kind '{kind}'");
            }
            import = new Module.Import
            {
                ModuleName = DecodeStringToUtf8(form.Lexer, modTok),
                Name = DecodeStringToUtf8(form.Lexer, nameTok),
                Desc = desc,
            };
            AppendImport(ctx, import);
            return true;
        }

        /// <summary>
        /// Consume zero or more <c>(export "name")</c> abbreviation children
        /// starting at <paramref name="index"/>, returning the export names.
        /// Caller resolves them to free-standing Export entries once the
        /// owning entity's index is known.
        /// </summary>
        private static List<string> ConsumePendingExports(SExpr form, ref int index)
        {
            var names = new List<string>();
            while (index < form.Children.Count)
            {
                var child = form.Children[index];
                if (!child.IsForm("export")) break;
                if (child.Children.Count != 2 || child.Children[1].Token.Kind != TokenKind.String)
                    throw new FormatException($"line {child.Token.Line}: inline (export …) expects a single string");
                names.Add(DecodeStringToUtf8(form.Lexer, child.Children[1].Token));
                index++;
            }
            return names;
        }

        private static void FlushExports(TextParseContext ctx, List<string> names, ExternalKind kind, int idx)
        {
            if (names.Count == 0) return;
            foreach (var name in names)
            {
                Module.ExportDesc desc = kind switch
                {
                    ExternalKind.Function => new Module.ExportDesc.FuncDesc { FunctionIndex = (FuncIdx)idx },
                    ExternalKind.Table    => new Module.ExportDesc.TableDesc { TableIndex = (TableIdx)idx },
                    ExternalKind.Memory   => new Module.ExportDesc.MemDesc  { MemoryIndex = (MemIdx)idx },
                    ExternalKind.Global   => new Module.ExportDesc.GlobalDesc { GlobalIndex = (GlobalIdx)idx },
                    ExternalKind.Tag      => new Module.ExportDesc.TagDesc  { TagIndex = (TagIdx)idx },
                    _ => throw new InvalidOperationException($"FlushExports: unknown kind {kind}")
                };
                ctx.Module.Exports = AppendExport(ctx.Module.Exports,
                    new Module.Export { Name = name, Desc = desc });
            }
        }
    }
}
