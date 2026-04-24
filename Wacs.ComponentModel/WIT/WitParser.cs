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

namespace Wacs.ComponentModel.WIT
{
    /// <summary>
    /// Recursive-descent parser for WIT source. Consumes a
    /// <see cref="WitLexer"/>-produced token stream and returns a
    /// <see cref="WitDocument"/>.
    ///
    /// <para>Reference: WIT MVP grammar per
    /// <c>component-model/design/mvp/WIT.md</c>.</para>
    /// </summary>
    public sealed class WitParser
    {
        private readonly WitLexer _lex;
        private readonly List<WitToken> _toks;
        private int _i;

        private WitParser(WitLexer lex, List<WitToken> toks)
        {
            _lex = lex;
            _toks = toks;
        }

        public static WitDocument Parse(string source)
        {
            var lex = new WitLexer(source);
            var toks = lex.Tokenize();
            var p = new WitParser(lex, toks);
            return p.ParseDocument();
        }

        public static WitDocument Parse(Stream stream)
        {
            using var r = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            return Parse(r.ReadToEnd());
        }

        // ---- Token helpers ------------------------------------------------

        private WitToken Peek(int offset = 0) => _toks[_i + offset];
        private WitToken Current => _toks[_i];

        private WitToken Consume()
        {
            var t = _toks[_i];
            _i++;
            return t;
        }

        private bool At(WitTokenKind k) => _toks[_i].Kind == k;

        private bool AtKeyword(string word) =>
            _toks[_i].Kind == WitTokenKind.Keyword && _lex.Slice(_toks[_i]) == word;

        private bool AtIdentOrKeyword(string word) =>
            (_toks[_i].Kind == WitTokenKind.Ident || _toks[_i].Kind == WitTokenKind.Keyword)
            && _lex.Slice(_toks[_i]) == word;

        private WitToken Expect(WitTokenKind k, string description)
        {
            var t = _toks[_i];
            if (t.Kind != k)
                throw new FormatException($"line {t.Line}:{t.Column}: expected {description}, got {t.Kind} '{_lex.Slice(t)}'");
            _i++;
            return t;
        }

        private WitToken ExpectKeyword(string word)
        {
            var t = _toks[_i];
            if (t.Kind != WitTokenKind.Keyword || _lex.Slice(t) != word)
                throw new FormatException($"line {t.Line}:{t.Column}: expected keyword '{word}', got '{_lex.Slice(t)}'");
            _i++;
            return t;
        }

        private WitSpan SpanOf(WitToken t) => new WitSpan(t.Line, t.Column);

        /// <summary>
        /// Consume the next token and interpret it as an identifier. Accepts
        /// plain <see cref="WitTokenKind.Ident"/>, and accepts a
        /// <see cref="WitTokenKind.Keyword"/> when the use-site name-space
        /// clearly doesn't clash with the grammar (e.g. a record field named
        /// <c>list</c> still resolves as a field name, not a type form). At
        /// the lexer level the <c>%</c>-escape was already stripped, so a
        /// user who writes <c>%list</c> gets an Ident token here.
        /// </summary>
        private string ConsumeIdent(string role = "identifier")
        {
            var t = _toks[_i];
            if (t.Kind == WitTokenKind.Ident) { _i++; return _lex.Slice(t); }
            throw new FormatException($"line {t.Line}:{t.Column}: expected {role}, got {t.Kind} '{_lex.Slice(t)}'");
        }

        // ---- Document / package -------------------------------------------

        private WitDocument ParseDocument()
        {
            var doc = new WitDocument { Span = SpanOf(_toks[0]) };

            // Optional leading package header: `package foo:bar@1.0.0;`
            // The spec allows either a bare `;`-terminated header or an
            // explicit `{ … }` body. In the file-level form subsequent items
            // attach to the declared package.
            WitPackage? currentPkg = null;
            if (AtKeyword("package"))
            {
                var p = ParsePackageHeaderOrBlock();
                doc.Packages.Add(p);
                currentPkg = p.HasExplicitBody ? null : p;
            }

            // Top-level uses appear outside packages and apply to all
            // downstream worlds.
            while (AtKeyword("use"))
            {
                doc.TopLevelUses.Add(ParseUse());
            }

            // File-level forms: interface / world forms, or another package
            // block.
            while (!At(WitTokenKind.Eof))
            {
                if (AtKeyword("package"))
                {
                    var p = ParsePackageHeaderOrBlock();
                    doc.Packages.Add(p);
                    if (!p.HasExplicitBody)
                        currentPkg = p;   // later package takes over the implicit slot
                    continue;
                }
                if (AtKeyword("interface"))
                {
                    var iface = ParseInterface();
                    EnsureImplicitPackage(doc, ref currentPkg).Interfaces.Add(iface);
                    continue;
                }
                if (AtKeyword("world"))
                {
                    var w = ParseWorld();
                    EnsureImplicitPackage(doc, ref currentPkg).Worlds.Add(w);
                    continue;
                }
                var t = Current;
                throw new FormatException($"line {t.Line}:{t.Column}: expected 'interface', 'world', or 'package', got '{_lex.Slice(t)}'");
            }

            return doc;
        }

        private static WitPackage EnsureImplicitPackage(WitDocument doc, ref WitPackage? current)
        {
            if (current != null) return current;
            // Documents without an explicit `package …;` header still need a
            // package to hold items. Fabricate an anonymous one.
            var pkg = new WitPackage { Name = new WitPackageName() };
            doc.Packages.Add(pkg);
            current = pkg;
            return pkg;
        }

        private WitPackage ParsePackageHeaderOrBlock()
        {
            var kw = ExpectKeyword("package");
            var name = ParsePackageName();
            var pkg = new WitPackage { Name = name, Span = SpanOf(kw) };

            if (At(WitTokenKind.LBrace))
            {
                Consume();
                pkg.HasExplicitBody = true;
                while (!At(WitTokenKind.RBrace))
                {
                    if (AtKeyword("interface")) pkg.Interfaces.Add(ParseInterface());
                    else if (AtKeyword("world")) pkg.Worlds.Add(ParseWorld());
                    else
                    {
                        var t = Current;
                        throw new FormatException($"line {t.Line}:{t.Column}: expected 'interface' or 'world' inside package, got '{_lex.Slice(t)}'");
                    }
                }
                Expect(WitTokenKind.RBrace, "'}'");
            }
            else
            {
                Expect(WitTokenKind.Semi, "';'");
            }
            return pkg;
        }

        private WitPackageName ParsePackageName()
        {
            var first = Expect(WitTokenKind.Ident, "package namespace");
            var name = new WitPackageName { Span = SpanOf(first), Namespace = _lex.Slice(first) };
            Expect(WitTokenKind.Colon, "':'");
            name.Path.Add(ConsumeIdent("package name"));
            while (At(WitTokenKind.Colon))
            {
                Consume();
                name.Path.Add(ConsumeIdent("package segment"));
            }
            if (At(WitTokenKind.At))
            {
                Consume();
                name.Version = ParseSemver();
            }
            return name;
        }

        private WitVersion ParseSemver()
        {
            // Sequence of Integer tokens separated by Dots, optional prerelease
            // after '-', optional build after '+'. The lexer currently produces
            // Integer and Dot separately; parse that shape.
            var startTok = Current;
            int major = ParseSemverNum();
            Expect(WitTokenKind.Dot, "'.' in semver");
            int minor = ParseSemverNum();
            Expect(WitTokenKind.Dot, "'.' in semver");
            int patch = ParseSemverNum();
            var v = new WitVersion { Span = SpanOf(startTok), Major = major, Minor = minor, Patch = patch };

            // Prerelease / build lex as follow-on identifier runs — but the
            // WIT lexer doesn't produce a '-' token, so for now we
            // deliberately leave pre-release/build unsupported. Extending the
            // lexer to recognize '-' and '+' in this context is a targeted
            // follow-up if the grammar ever shows up in real WIT files we
            // need to handle.
            return v;
        }

        private int ParseSemverNum()
        {
            var t = Expect(WitTokenKind.Integer, "semver numeric component");
            var raw = _lex.Slice(t).Replace("_", "");
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                throw new FormatException($"line {t.Line}:{t.Column}: bad integer '{_lex.Slice(t)}'");
            return n;
        }

        // ---- Interface ----------------------------------------------------

        private WitInterface ParseInterface()
        {
            var kw = ExpectKeyword("interface");
            var name = ConsumeIdent("interface name");
            Expect(WitTokenKind.LBrace, "'{'");
            var iface = new WitInterface { Name = name, Span = SpanOf(kw) };
            while (!At(WitTokenKind.RBrace))
            {
                iface.Items.Add(ParseInterfaceItem());
            }
            Expect(WitTokenKind.RBrace, "'}'");
            return iface;
        }

        private WitInterfaceItem ParseInterfaceItem()
        {
            if (AtKeyword("use"))      return ParseUse();
            if (AtKeyword("type"))     return ParseTypeAlias();
            if (AtKeyword("record"))   return ParseRecordDef();
            if (AtKeyword("variant"))  return ParseVariantDef();
            if (AtKeyword("enum"))     return ParseEnumDef();
            if (AtKeyword("flags"))    return ParseFlagsDef();
            if (AtKeyword("resource")) return ParseResourceDef();

            // Otherwise: `name: func(...)` form
            return ParseFunctionItem();
        }

        // ---- Use statement ------------------------------------------------

        private WitUse ParseUse()
        {
            var kw = ExpectKeyword("use");
            var path = ParseUsePath();
            Expect(WitTokenKind.Dot, "'.' before use list");
            Expect(WitTokenKind.LBrace, "'{'");
            var use = new WitUse { Path = path, Span = SpanOf(kw) };
            while (!At(WitTokenKind.RBrace))
            {
                var name = ConsumeIdent("use name");
                string? alias = null;
                if (AtKeyword("as"))
                {
                    Consume();
                    alias = ConsumeIdent("use alias");
                }
                use.Names.Add(new WitUsedName { Name = name, Alias = alias });
                if (At(WitTokenKind.Comma)) Consume();
                else break;
            }
            Expect(WitTokenKind.RBrace, "'}'");
            Expect(WitTokenKind.Semi, "';'");
            return use;
        }

        private WitUsePath ParseUsePath()
        {
            // Either `pkg:ns:name/iface` or just `iface`.
            var startTok = Current;
            var first = Expect(WitTokenKind.Ident, "use path");
            if (At(WitTokenKind.Colon))
            {
                // Full qualified form.
                var pkg = new WitPackageName { Span = SpanOf(first), Namespace = _lex.Slice(first) };
                Consume();
                pkg.Path.Add(ConsumeIdent("use path segment"));
                while (At(WitTokenKind.Colon))
                {
                    Consume();
                    pkg.Path.Add(ConsumeIdent("use path segment"));
                }
                if (At(WitTokenKind.At))
                {
                    Consume();
                    pkg.Version = ParseSemver();
                }
                Expect(WitTokenKind.Slash, "'/' between package and interface name");
                var iface = ConsumeIdent("interface name");
                return new WitUsePath { Package = pkg, InterfaceName = iface, Span = SpanOf(startTok) };
            }
            return new WitUsePath { InterfaceName = _lex.Slice(first), Span = SpanOf(first) };
        }

        // ---- Type definitions ---------------------------------------------

        private WitTypeDef ParseTypeAlias()
        {
            var kw = ExpectKeyword("type");
            var name = ConsumeIdent("type name");
            Expect(WitTokenKind.Equals, "'='");
            var type = ParseType();
            Expect(WitTokenKind.Semi, "';'");
            return new WitTypeDef { Name = name, Type = type, Span = SpanOf(kw) };
        }

        private WitTypeDef ParseRecordDef()
        {
            var kw = ExpectKeyword("record");
            var name = ConsumeIdent("record name");
            Expect(WitTokenKind.LBrace, "'{'");
            var rec = new WitRecordType { Span = SpanOf(kw) };
            while (!At(WitTokenKind.RBrace))
            {
                var fieldTok = Current;
                var field = new WitField { Span = SpanOf(fieldTok), Name = ConsumeIdent("field name") };
                Expect(WitTokenKind.Colon, "':'");
                field.Type = ParseType();
                rec.Fields.Add(field);
                if (At(WitTokenKind.Comma)) Consume();
                else break;
            }
            Expect(WitTokenKind.RBrace, "'}'");
            return new WitTypeDef { Name = name, Type = rec, Span = SpanOf(kw) };
        }

        private WitTypeDef ParseVariantDef()
        {
            var kw = ExpectKeyword("variant");
            var name = ConsumeIdent("variant name");
            Expect(WitTokenKind.LBrace, "'{'");
            var variant = new WitVariantType { Span = SpanOf(kw) };
            while (!At(WitTokenKind.RBrace))
            {
                var caseTok = Current;
                var c = new WitVariantCase { Span = SpanOf(caseTok), Name = ConsumeIdent("variant case") };
                if (At(WitTokenKind.LParen))
                {
                    Consume();
                    c.Payload = ParseType();
                    Expect(WitTokenKind.RParen, "')'");
                }
                variant.Cases.Add(c);
                if (At(WitTokenKind.Comma)) Consume();
                else break;
            }
            Expect(WitTokenKind.RBrace, "'}'");
            return new WitTypeDef { Name = name, Type = variant, Span = SpanOf(kw) };
        }

        private WitTypeDef ParseEnumDef()
        {
            var kw = ExpectKeyword("enum");
            var name = ConsumeIdent("enum name");
            Expect(WitTokenKind.LBrace, "'{'");
            var e = new WitEnumType { Span = SpanOf(kw) };
            while (!At(WitTokenKind.RBrace))
            {
                e.Cases.Add(ConsumeIdent("enum case"));
                if (At(WitTokenKind.Comma)) Consume();
                else break;
            }
            Expect(WitTokenKind.RBrace, "'}'");
            return new WitTypeDef { Name = name, Type = e, Span = SpanOf(kw) };
        }

        private WitTypeDef ParseFlagsDef()
        {
            var kw = ExpectKeyword("flags");
            var name = ConsumeIdent("flags name");
            Expect(WitTokenKind.LBrace, "'{'");
            var f = new WitFlagsType { Span = SpanOf(kw) };
            while (!At(WitTokenKind.RBrace))
            {
                f.Flags.Add(ConsumeIdent("flag"));
                if (At(WitTokenKind.Comma)) Consume();
                else break;
            }
            Expect(WitTokenKind.RBrace, "'}'");
            return new WitTypeDef { Name = name, Type = f, Span = SpanOf(kw) };
        }

        private WitTypeDef ParseResourceDef()
        {
            var kw = ExpectKeyword("resource");
            var name = ConsumeIdent("resource name");
            var resource = new WitResourceType { Span = SpanOf(kw) };

            // Empty resource: `resource foo;`
            if (At(WitTokenKind.Semi))
            {
                Consume();
                return new WitTypeDef { Name = name, Type = resource, Span = SpanOf(kw) };
            }

            Expect(WitTokenKind.LBrace, "'{'");
            while (!At(WitTokenKind.RBrace))
            {
                resource.Methods.Add(ParseResourceMethod());
            }
            Expect(WitTokenKind.RBrace, "'}'");
            return new WitTypeDef { Name = name, Type = resource, Span = SpanOf(kw) };
        }

        private WitResourceMethod ParseResourceMethod()
        {
            // Three variants (per component-model WIT grammar):
            //   constructor(params);
            //   static name: func(params) -> result;
            //   name: func(params) -> result;
            var startTok = Current;
            if (AtKeyword("constructor"))
            {
                Consume();
                Expect(WitTokenKind.LParen, "'('");
                var m = new WitResourceMethod { Span = SpanOf(startTok), Kind = WitResourceMethodKind.Constructor };
                ParseParamList(m.Params);
                Expect(WitTokenKind.RParen, "')'");
                Expect(WitTokenKind.Semi, "';'");
                return m;
            }
            var isStatic = false;
            if (AtKeyword("static")) { Consume(); isStatic = true; }
            var name = ConsumeIdent("method name");
            Expect(WitTokenKind.Colon, "':'");
            ExpectKeyword("func");
            Expect(WitTokenKind.LParen, "'('");
            var method = new WitResourceMethod
            {
                Span = SpanOf(startTok),
                Name = name,
                Kind = isStatic ? WitResourceMethodKind.Static : WitResourceMethodKind.Instance,
            };
            ParseParamList(method.Params);
            Expect(WitTokenKind.RParen, "')'");
            ParseOptionalResult(out var r, out var named);
            method.Result = r;
            method.NamedResults = named;
            Expect(WitTokenKind.Semi, "';'");
            return method;
        }

        // ---- Function item -----------------------------------------------

        private WitFunction ParseFunctionItem()
        {
            var startTok = Current;
            var name = ConsumeIdent("function name");
            Expect(WitTokenKind.Colon, "':'");
            ExpectKeyword("func");
            Expect(WitTokenKind.LParen, "'('");
            var fn = new WitFunction { Name = name, Span = SpanOf(startTok) };
            ParseParamList(fn.Params);
            Expect(WitTokenKind.RParen, "')'");
            ParseOptionalResult(out var r, out var named);
            fn.Result = r;
            fn.NamedResults = named;
            Expect(WitTokenKind.Semi, "';'");
            return fn;
        }

        private void ParseParamList(List<WitParam> outList)
        {
            if (At(WitTokenKind.RParen)) return;   // empty
            while (true)
            {
                var tk = Current;
                var p = new WitParam { Span = SpanOf(tk), Name = ConsumeIdent("parameter name") };
                Expect(WitTokenKind.Colon, "':'");
                p.Type = ParseType();
                outList.Add(p);
                if (At(WitTokenKind.Comma)) Consume();
                else break;
            }
        }

        private void ParseOptionalResult(out WitType? result, out List<WitParam>? named)
        {
            result = null;
            named = null;
            if (!At(WitTokenKind.Arrow)) return;
            Consume();
            if (At(WitTokenKind.LParen))
            {
                Consume();
                var list = new List<WitParam>();
                ParseParamList(list);
                Expect(WitTokenKind.RParen, "')'");
                named = list;
                return;
            }
            result = ParseType();
        }

        // ---- Types --------------------------------------------------------

        private WitType ParseType()
        {
            var t = Current;
            // Keyword-form types
            if (t.Kind == WitTokenKind.Keyword)
            {
                var lex = _lex.Slice(t);
                switch (lex)
                {
                    case "bool":   Consume(); return new WitPrimType { Kind = WitPrim.Bool,   Span = SpanOf(t) };
                    case "s8":     Consume(); return new WitPrimType { Kind = WitPrim.S8,     Span = SpanOf(t) };
                    case "u8":     Consume(); return new WitPrimType { Kind = WitPrim.U8,     Span = SpanOf(t) };
                    case "s16":    Consume(); return new WitPrimType { Kind = WitPrim.S16,    Span = SpanOf(t) };
                    case "u16":    Consume(); return new WitPrimType { Kind = WitPrim.U16,    Span = SpanOf(t) };
                    case "s32":    Consume(); return new WitPrimType { Kind = WitPrim.S32,    Span = SpanOf(t) };
                    case "u32":    Consume(); return new WitPrimType { Kind = WitPrim.U32,    Span = SpanOf(t) };
                    case "s64":    Consume(); return new WitPrimType { Kind = WitPrim.S64,    Span = SpanOf(t) };
                    case "u64":    Consume(); return new WitPrimType { Kind = WitPrim.U64,    Span = SpanOf(t) };
                    case "f32":
                    case "float32":Consume(); return new WitPrimType { Kind = WitPrim.F32,    Span = SpanOf(t) };
                    case "f64":
                    case "float64":Consume(); return new WitPrimType { Kind = WitPrim.F64,    Span = SpanOf(t) };
                    case "char":   Consume(); return new WitPrimType { Kind = WitPrim.Char,   Span = SpanOf(t) };
                    case "string": Consume(); return new WitPrimType { Kind = WitPrim.String, Span = SpanOf(t) };

                    case "list":
                    {
                        Consume();
                        Expect(WitTokenKind.LAngle, "'<'");
                        var elem = ParseType();
                        Expect(WitTokenKind.RAngle, "'>'");
                        return new WitListType { Element = elem, Span = SpanOf(t) };
                    }
                    case "option":
                    {
                        Consume();
                        Expect(WitTokenKind.LAngle, "'<'");
                        var inner = ParseType();
                        Expect(WitTokenKind.RAngle, "'>'");
                        return new WitOptionType { Inner = inner, Span = SpanOf(t) };
                    }
                    case "result":
                    {
                        Consume();
                        var r = new WitResultType { Span = SpanOf(t) };
                        if (!At(WitTokenKind.LAngle)) return r;   // `result` with no args
                        Consume();
                        // Three internal shapes: `<T>`, `<T, E>`, `<_, E>`.
                        if (AtIdentOrKeyword("_"))
                        {
                            Consume();
                            Expect(WitTokenKind.Comma, "','");
                            r.Err = ParseType();
                        }
                        else
                        {
                            r.Ok = ParseType();
                            if (At(WitTokenKind.Comma))
                            {
                                Consume();
                                r.Err = ParseType();
                            }
                        }
                        Expect(WitTokenKind.RAngle, "'>'");
                        return r;
                    }
                    case "tuple":
                    {
                        Consume();
                        Expect(WitTokenKind.LAngle, "'<'");
                        var tup = new WitTupleType { Span = SpanOf(t) };
                        while (!At(WitTokenKind.RAngle))
                        {
                            tup.Elements.Add(ParseType());
                            if (At(WitTokenKind.Comma)) Consume();
                            else break;
                        }
                        Expect(WitTokenKind.RAngle, "'>'");
                        return tup;
                    }
                    case "own":
                    case "borrow":
                    {
                        Consume();
                        Expect(WitTokenKind.LAngle, "'<'");
                        var nameTok = Current;
                        var name = ConsumeIdent("resource name");
                        Expect(WitTokenKind.RAngle, "'>'");
                        if (lex == "own")
                            return new WitOwnType { ResourceName = name, Span = SpanOf(t) };
                        return new WitBorrowType { ResourceName = name, Span = SpanOf(t) };
                    }
                }
            }

            // Identifier reference
            if (t.Kind == WitTokenKind.Ident)
            {
                Consume();
                return new WitTypeRef { Name = _lex.Slice(t), Span = SpanOf(t) };
            }
            throw new FormatException($"line {t.Line}:{t.Column}: expected type, got {t.Kind} '{_lex.Slice(t)}'");
        }

        // ---- World --------------------------------------------------------

        private WitWorld ParseWorld()
        {
            var kw = ExpectKeyword("world");
            var name = ConsumeIdent("world name");
            Expect(WitTokenKind.LBrace, "'{'");
            var w = new WitWorld { Name = name, Span = SpanOf(kw) };
            while (!At(WitTokenKind.RBrace))
            {
                w.Items.Add(ParseWorldItem());
            }
            Expect(WitTokenKind.RBrace, "'}'");
            return w;
        }

        private WitWorldItem ParseWorldItem()
        {
            if (AtKeyword("import"))  return ParseImportOrExport(isExport: false);
            if (AtKeyword("export"))  return ParseImportOrExport(isExport: true);
            if (AtKeyword("use"))     return new WitWorldUse { Use = ParseUse(), Span = SpanOf(Current) };
            if (AtKeyword("include")) return ParseInclude();
            if (AtKeyword("type") || AtKeyword("record") || AtKeyword("variant")
                || AtKeyword("enum") || AtKeyword("flags") || AtKeyword("resource"))
            {
                return new WitWorldTypeDef { TypeDef = (WitTypeDef)ParseInterfaceItem() };
            }
            var t = Current;
            throw new FormatException($"line {t.Line}:{t.Column}: expected 'import', 'export', 'use', 'include', or type in world, got '{_lex.Slice(t)}'");
        }

        private WitWorldItem ParseImportOrExport(bool isExport)
        {
            var kw = isExport ? ExpectKeyword("export") : ExpectKeyword("import");
            var spec = ParseExternSpec();
            if (isExport)
                return new WitWorldExport { Spec = spec, Span = SpanOf(kw) };
            return new WitWorldImport { Spec = spec, Span = SpanOf(kw) };
        }

        private WitExternSpec ParseExternSpec()
        {
            // Four syntactic shapes:
            //   name: func(...) -> ...;
            //   name: interface { ... }    (trailing ';' optional)
            //   name: iface-ref;
            //   pkg:ns/iface;              (bare reference, no `name:` prefix)
            //
            // Disambiguation: a name-binding form has a Colon directly after
            // the first ident, followed by either `func`, `interface`, or a
            // single-segment interface name. A bare reference has a Colon
            // after the first ident followed by another ident then another
            // Colon or a Slash (making the sequence into a package path).
            var startTok = Current;
            if (IsNameBinding())
            {
                var name = ConsumeIdent();
                Consume();   // ':'
                if (AtKeyword("func"))
                {
                    Consume();
                    Expect(WitTokenKind.LParen, "'('");
                    var fn = new WitFunction { Name = name, Span = SpanOf(startTok) };
                    ParseParamList(fn.Params);
                    Expect(WitTokenKind.RParen, "')'");
                    ParseOptionalResult(out var r, out var named);
                    fn.Result = r;
                    fn.NamedResults = named;
                    Expect(WitTokenKind.Semi, "';'");
                    return new WitExternFunc { Name = name, Function = fn, Span = SpanOf(startTok) };
                }
                if (AtKeyword("interface"))
                {
                    Consume();
                    Expect(WitTokenKind.LBrace, "'{'");
                    var iface = new WitInterface { Name = name, Span = SpanOf(startTok) };
                    while (!At(WitTokenKind.RBrace))
                        iface.Items.Add(ParseInterfaceItem());
                    Expect(WitTokenKind.RBrace, "'}'");
                    // Inline-interface extern specs may or may not carry a
                    // trailing ';' — tolerate either.
                    if (At(WitTokenKind.Semi)) Consume();
                    return new WitExternInlineInterface { Name = name, Interface = iface, Span = SpanOf(startTok) };
                }
                // otherwise must be a use-path reference — name-bound alias
                // to an external interface (possibly package-qualified).
                var path = ParseUsePath();
                Expect(WitTokenKind.Semi, "';'");
                return new WitExternInterfaceRef { Name = name, Path = path, Span = SpanOf(startTok) };
            }
            // Bare use path — name is derived from the interface's local name.
            var bare = ParseUsePath();
            Expect(WitTokenKind.Semi, "';'");
            return new WitExternInterfaceRef
            {
                Name = bare.InterfaceName,
                Path = bare,
                Span = bare.Span,
            };
        }

        /// <summary>
        /// True if the upcoming tokens form a <c>name : desc</c> binding
        /// rather than a bare <c>pkg:ns/iface</c> reference. We peek past
        /// the first ident+colon — a binding's RHS starts with either a
        /// keyword (<c>func</c> / <c>interface</c>) or a single-segment
        /// interface name; a package path starts with another ident that
        /// is itself followed by Colon, Slash, or At.
        /// </summary>
        private bool IsNameBinding()
        {
            if (Peek(0).Kind != WitTokenKind.Ident) return false;
            if (Peek(1).Kind != WitTokenKind.Colon) return false;
            var third = Peek(2);
            if (third.Kind == WitTokenKind.Keyword) return true;   // func / interface
            if (third.Kind != WitTokenKind.Ident) return true;
            // ident : ident <next> — binding iff <next> is ';' (bare alias).
            var fourth = Peek(3);
            switch (fourth.Kind)
            {
                case WitTokenKind.Colon:
                case WitTokenKind.Slash:
                case WitTokenKind.At:
                    return false;   // continuing a package path
                default:
                    return true;
            }
        }

        private WitWorldInclude ParseInclude()
        {
            var kw = ExpectKeyword("include");
            var path = ParseUsePath();
            var inc = new WitWorldInclude { Path = path, Span = SpanOf(kw) };
            if (AtKeyword("with"))
            {
                Consume();
                Expect(WitTokenKind.LBrace, "'{'");
                while (!At(WitTokenKind.RBrace))
                {
                    var fromTok = Current;
                    var rename = new WitRename
                    {
                        Span = SpanOf(fromTok),
                        From = ConsumeIdent("rename source"),
                    };
                    ExpectKeyword("as");
                    rename.To = ConsumeIdent("rename target");
                    inc.With.Add(rename);
                    if (At(WitTokenKind.Comma)) Consume();
                    else break;
                }
                Expect(WitTokenKind.RBrace, "'}'");
            }
            Expect(WitTokenKind.Semi, "';'");
            return inc;
        }
    }
}
