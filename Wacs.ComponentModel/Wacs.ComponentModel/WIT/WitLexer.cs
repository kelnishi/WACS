// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Text;

namespace Wacs.ComponentModel.WIT
{
    public enum WitTokenKind : byte
    {
        Eof = 0,

        // Punctuation
        LBrace,      // {
        RBrace,      // }
        LAngle,      // <
        RAngle,      // >
        LParen,      // (
        RParen,      // )
        Comma,       // ,
        Semi,        // ;
        Colon,       // :
        Dot,         // .
        Slash,       // /
        Equals,      // =
        At,          // @
        Arrow,       // ->
        Star,        // *

        // Literals / ids
        Ident,       // foo, kebab-case, _foo
        Integer,     // 1, 42, 1_000
        String,      // "literal text"

        // Reserved word (context-specific — resolve by lexeme in parser)
        Keyword,

        /// <summary>
        /// A single <c>/// …</c> doc-comment line. The token's slice
        /// includes the leading <c>///</c>; callers strip it (plus
        /// optionally one space) to get the doc text. Adjacent
        /// DocComment tokens belong to the same comment block.
        /// </summary>
        DocComment,
    }

    /// <summary>
    /// A WIT source lexeme. Stores offsets into the source text; use
    /// <see cref="WitLexer.Slice"/> to materialize the lexeme.
    /// </summary>
    public readonly struct WitToken
    {
        public readonly WitTokenKind Kind;
        public readonly int Start;
        public readonly int Length;
        public readonly int Line;
        public readonly int Column;

        public WitToken(WitTokenKind kind, int start, int length, int line, int column)
        {
            Kind = kind;
            Start = start;
            Length = length;
            Line = line;
            Column = column;
        }

        public override string ToString() => $"{Kind}@{Line}:{Column}+{Length}";
    }

    /// <summary>
    /// Tokenizer for WIT source. The WIT grammar uses a conventional
    /// punctuation + identifier scheme — not s-expressions — so this lexer
    /// shares no code with <see cref="Wacs.Core.Text.Lexer"/>.
    ///
    /// <para>Identifier rules (per the WIT spec): kebab-case with letters,
    /// digits, and single hyphens; may start with an ASCII letter or
    /// underscore; a leading <c>%</c> escapes a keyword-name collision.</para>
    /// </summary>
    public sealed class WitLexer
    {
        // Reserved words recognized as Keyword tokens. Context determines
        // which ones are valid where; the parser classifies by lexeme.
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "package", "interface", "world", "import", "export", "use", "as",
            "func", "static", "constructor", "type", "resource", "record",
            "variant", "enum", "flags", "option", "result", "tuple", "list",
            "own", "borrow", "include", "with",
            // Primitive types double as keywords in type position
            "bool", "s8", "u8", "s16", "u16", "s32", "u32", "s64", "u64",
            "f32", "f64", "float32", "float64", "char", "string",
        };

        private readonly string _source;
        private int _pos;
        private int _line = 1;
        private int _col = 1;

        public WitLexer(string source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public string Source => _source;

        public string Slice(WitToken tok) => _source.Substring(tok.Start, tok.Length);

        /// <summary>
        /// Extract the doc text from a <see cref="WitTokenKind.DocComment"/>
        /// token — strips the leading <c>///</c> and up to one
        /// following space. Multi-line doc blocks are joined by
        /// the parser; this helper handles one line at a time.
        /// </summary>
        public string DecodeDocLine(WitToken tok)
        {
            if (tok.Kind != WitTokenKind.DocComment)
                throw new InvalidOperationException($"not a doc comment: {tok}");
            // Token slice starts with `///`. Skip those 3 chars + an
            // optional one space (the conventional formatter space).
            int i = tok.Start + 3;
            int end = tok.Start + tok.Length;
            if (i < end && _source[i] == ' ') i++;
            // Trim trailing CR (Windows line endings).
            int trimEnd = end;
            while (trimEnd > i && _source[trimEnd - 1] == '\r') trimEnd--;
            return _source.Substring(i, trimEnd - i);
        }

        /// <summary>
        /// Materialize a <see cref="WitTokenKind.String"/> token as a .NET
        /// string. Handles backslash escapes (\n \t \r \" \\ \0).
        /// </summary>
        public string DecodeString(WitToken tok)
        {
            if (tok.Kind != WitTokenKind.String)
                throw new InvalidOperationException($"not a string: {tok}");
            var sb = new StringBuilder(tok.Length);
            int end = tok.Start + tok.Length - 1;
            int i = tok.Start + 1;
            while (i < end)
            {
                char c = _source[i];
                if (c != '\\') { sb.Append(c); i++; continue; }
                if (i + 1 >= end)
                    throw new FormatException($"line {tok.Line}: truncated string escape");
                char esc = _source[i + 1];
                switch (esc)
                {
                    case 'n':  sb.Append('\n'); break;
                    case 't':  sb.Append('\t'); break;
                    case 'r':  sb.Append('\r'); break;
                    case '"':  sb.Append('"');  break;
                    case '\\': sb.Append('\\'); break;
                    case '0':  sb.Append('\0'); break;
                    default:
                        throw new FormatException(
                            $"line {tok.Line}: unknown string escape '\\{esc}'");
                }
                i += 2;
            }
            return sb.ToString();
        }

        public List<WitToken> Tokenize()
        {
            var toks = new List<WitToken>(Math.Max(16, _source.Length / 8));
            while (true)
            {
                SkipTrivia(toks);
                int line = _line, col = _col, start = _pos;
                if (_pos >= _source.Length)
                {
                    toks.Add(new WitToken(WitTokenKind.Eof, _pos, 0, line, col));
                    return toks;
                }
                char c = _source[_pos];

                // Single-char punctuation
                switch (c)
                {
                    case '{': toks.Add(new WitToken(WitTokenKind.LBrace, start, 1, line, col)); Advance(); continue;
                    case '}': toks.Add(new WitToken(WitTokenKind.RBrace, start, 1, line, col)); Advance(); continue;
                    case '<': toks.Add(new WitToken(WitTokenKind.LAngle, start, 1, line, col)); Advance(); continue;
                    case '>': toks.Add(new WitToken(WitTokenKind.RAngle, start, 1, line, col)); Advance(); continue;
                    case '(': toks.Add(new WitToken(WitTokenKind.LParen, start, 1, line, col)); Advance(); continue;
                    case ')': toks.Add(new WitToken(WitTokenKind.RParen, start, 1, line, col)); Advance(); continue;
                    case ',': toks.Add(new WitToken(WitTokenKind.Comma, start, 1, line, col)); Advance(); continue;
                    case ';': toks.Add(new WitToken(WitTokenKind.Semi, start, 1, line, col)); Advance(); continue;
                    case ':': toks.Add(new WitToken(WitTokenKind.Colon, start, 1, line, col)); Advance(); continue;
                    case '.': toks.Add(new WitToken(WitTokenKind.Dot, start, 1, line, col)); Advance(); continue;
                    case '/': toks.Add(new WitToken(WitTokenKind.Slash, start, 1, line, col)); Advance(); continue;
                    case '=': toks.Add(new WitToken(WitTokenKind.Equals, start, 1, line, col)); Advance(); continue;
                    case '@': toks.Add(new WitToken(WitTokenKind.At, start, 1, line, col)); Advance(); continue;
                    case '*': toks.Add(new WitToken(WitTokenKind.Star, start, 1, line, col)); Advance(); continue;
                }

                // '->'
                if (c == '-' && _pos + 1 < _source.Length && _source[_pos + 1] == '>')
                {
                    toks.Add(new WitToken(WitTokenKind.Arrow, start, 2, line, col));
                    Advance(); Advance();
                    continue;
                }

                // String
                if (c == '"')
                {
                    int len = LexString(line, col);
                    toks.Add(new WitToken(WitTokenKind.String, start, len, line, col));
                    continue;
                }

                // Integer
                if (c >= '0' && c <= '9')
                {
                    int s = _pos;
                    while (_pos < _source.Length && (IsDigit(_source[_pos]) || _source[_pos] == '_'))
                        Advance();
                    toks.Add(new WitToken(WitTokenKind.Integer, s, _pos - s, line, col));
                    continue;
                }

                // Identifier / keyword / %-escaped-identifier
                if (IsIdentStart(c) || c == '%')
                {
                    int s = _pos;
                    if (c == '%') Advance();   // leading % is not part of the name semantically
                    int nameStart = _pos;
                    if (_pos >= _source.Length || !IsIdentStart(_source[_pos]))
                        throw new FormatException($"line {line}:{col}: expected identifier after '%'");
                    while (_pos < _source.Length && IsIdentContinue(_source[_pos]))
                        Advance();
                    int len = _pos - s;
                    // Decide kind: if the lexeme (minus leading %) is a
                    // keyword and the % was not present, emit Keyword.
                    var lex = _source.Substring(nameStart, _pos - nameStart);
                    bool hadEscape = (c == '%');
                    bool isKeyword = !hadEscape && Keywords.Contains(lex);
                    toks.Add(new WitToken(isKeyword ? WitTokenKind.Keyword : WitTokenKind.Ident,
                        s, len, line, col));
                    continue;
                }

                throw new FormatException($"line {line}:{col}: unexpected character '{c}'");
            }
        }

        private int LexString(int startLine, int startCol)
        {
            int start = _pos;
            Advance();
            while (_pos < _source.Length)
            {
                char c = _source[_pos];
                if (c == '"') { Advance(); return _pos - start; }
                if (c == '\\')
                {
                    if (_pos + 1 >= _source.Length)
                        throw new FormatException($"line {startLine}:{startCol}: truncated string escape");
                    Advance(); Advance();
                    continue;
                }
                if (c == '\n')
                    throw new FormatException($"line {_line}:{_col}: newline inside string (use \\n)");
                Advance();
            }
            throw new FormatException($"line {startLine}:{startCol}: unterminated string");
        }

        private void SkipTrivia(List<WitToken> toks)
        {
            while (_pos < _source.Length)
            {
                char c = _source[_pos];
                if (c == ' ' || c == '\t' || c == '\r') { Advance(); continue; }
                if (c == '\n') { _pos++; _line++; _col = 1; continue; }
                if (c == '/' && _pos + 1 < _source.Length && _source[_pos + 1] == '/')
                {
                    // `///` → doc comment (emit token). `//` →
                    // regular line comment (trivia). Checking the
                    // third char disambiguates; must not go past
                    // end-of-source.
                    bool isDoc = _pos + 2 < _source.Length
                        && _source[_pos + 2] == '/';
                    if (isDoc)
                    {
                        int sLine = _line, sCol = _col;
                        int start = _pos;
                        while (_pos < _source.Length && _source[_pos] != '\n') Advance();
                        toks.Add(new WitToken(WitTokenKind.DocComment, start,
                                              _pos - start, sLine, sCol));
                    }
                    else
                    {
                        while (_pos < _source.Length && _source[_pos] != '\n') Advance();
                    }
                    continue;
                }
                if (c == '/' && _pos + 1 < _source.Length && _source[_pos + 1] == '*')
                {
                    int sLine = _line, sCol = _col;
                    Advance(); Advance();
                    // Block comments nest per the WIT spec convention.
                    int depth = 1;
                    while (depth > 0)
                    {
                        if (_pos >= _source.Length)
                            throw new FormatException($"line {sLine}:{sCol}: unterminated block comment");
                        char d = _source[_pos];
                        if (d == '/' && _pos + 1 < _source.Length && _source[_pos + 1] == '*')
                        { Advance(); Advance(); depth++; }
                        else if (d == '*' && _pos + 1 < _source.Length && _source[_pos + 1] == '/')
                        { Advance(); Advance(); depth--; }
                        else if (d == '\n') { _pos++; _line++; _col = 1; }
                        else Advance();
                    }
                    continue;
                }
                break;
            }
        }

        private void Advance()
        {
            _pos++;
            _col++;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private static bool IsIdentStart(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

        // WIT idents allow kebab-case — a single '-' in the middle is part of
        // the name. The lexer greedily consumes runs including hyphens;
        // pathological cases (leading or trailing hyphen) get rejected at
        // parse time if they matter.
        private static bool IsIdentContinue(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
            (c >= '0' && c <= '9') || c == '_' || c == '-';
    }
}
