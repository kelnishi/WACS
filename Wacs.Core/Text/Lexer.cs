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

namespace Wacs.Core.Text
{
    /// <summary>
    /// Tokenizer for the WebAssembly text format (.wat / .wast). Follows the
    /// grammar from the core spec appendix: characters split on whitespace,
    /// parens, and comments; idchars form keywords / ids / reserved tokens;
    /// strings are double-quoted with backslash escapes.
    ///
    /// The lexer is context-free — numeric literals are emitted as
    /// <see cref="TokenKind.Reserved"/> and parsed per-context by
    /// <c>NumericLiteral</c> (phase 1.3+).
    /// </summary>
    public sealed class Lexer
    {
        private readonly string _source;
        private int _pos;
        private int _line = 1;
        private int _col = 1;

        public Lexer(string source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public static Lexer FromStream(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            return new Lexer(reader.ReadToEnd());
        }

        public string Source => _source;

        /// <summary>
        /// Returns the raw lexeme (exact source characters) for a token.
        /// </summary>
        public string Slice(Token tok) => _source.Substring(tok.Start, tok.Length);

        /// <summary>
        /// Materializes a <see cref="TokenKind.String"/> token as its decoded
        /// byte sequence, per spec string escapes. Valid escapes:
        ///   \t \n \r \" \' \\ \uXXXX \uNNNN... (hex codepoint up to 6 digits)
        ///   \XX   (two hex digits = one byte)
        /// The WAST spec treats strings as byte sequences, not UTF-16; this
        /// method returns <see cref="byte"/>[].
        /// </summary>
        public byte[] DecodeString(Token tok)
        {
            if (tok.Kind != TokenKind.String)
                throw new InvalidOperationException($"Token {tok} is not a string");
            // The raw slice includes the surrounding quotes. Walk the interior
            // character-by-character, expanding escapes to their byte form.
            int end = tok.Start + tok.Length - 1;  // index of closing quote
            var buf = new List<byte>(tok.Length);
            int i = tok.Start + 1;
            while (i < end)
            {
                char c = _source[i];
                if (c != '\\')
                {
                    // Encode non-escape chars as UTF-8 bytes. WAT strings are
                    // defined as sequences of bytes, but the source is UTF-8;
                    // non-ASCII chars map to their UTF-8 encoding.
                    if (c < 0x80)
                    {
                        buf.Add((byte)c);
                        i++;
                    }
                    else
                    {
                        // Re-encode this char (and a possible surrogate pair)
                        // as UTF-8 bytes.
                        int advance = 1;
                        if (char.IsHighSurrogate(c) && i + 1 < end && char.IsLowSurrogate(_source[i + 1]))
                            advance = 2;
                        var encoded = Encoding.UTF8.GetBytes(_source.Substring(i, advance));
                        buf.AddRange(encoded);
                        i += advance;
                    }
                    continue;
                }

                // Escape sequence
                if (i + 1 >= end)
                    throw new FormatException($"line {tok.Line}: truncated escape in string");
                char esc = _source[i + 1];
                switch (esc)
                {
                    case 't':  buf.Add((byte)'\t'); i += 2; break;
                    case 'n':  buf.Add((byte)'\n'); i += 2; break;
                    case 'r':  buf.Add((byte)'\r'); i += 2; break;
                    case '"':  buf.Add((byte)'"');  i += 2; break;
                    case '\'': buf.Add((byte)'\''); i += 2; break;
                    case '\\': buf.Add((byte)'\\'); i += 2; break;
                    case 'u':
                    {
                        // \u{XXXX} — hex codepoint, emitted as UTF-8 bytes
                        if (i + 2 >= end || _source[i + 2] != '{')
                            throw new FormatException($"line {tok.Line}: expected '{{' after \\u");
                        int j = i + 3;
                        int cp = 0;
                        int digits = 0;
                        while (j < end && _source[j] != '}')
                        {
                            int d = HexDigit(_source[j]);
                            if (d < 0) throw new FormatException($"line {tok.Line}: bad hex digit '{_source[j]}'");
                            cp = (cp << 4) | d;
                            digits++;
                            if (digits > 6) throw new FormatException($"line {tok.Line}: codepoint too large");
                            j++;
                        }
                        if (j >= end || _source[j] != '}')
                            throw new FormatException($"line {tok.Line}: unterminated \\u{{");
                        if (digits == 0) throw new FormatException($"line {tok.Line}: empty \\u{{}}");
                        buf.AddRange(Encoding.UTF8.GetBytes(char.ConvertFromUtf32(cp)));
                        i = j + 1;
                        break;
                    }
                    default:
                    {
                        // Two hex digits = one byte
                        int hi = HexDigit(esc);
                        if (hi < 0)
                            throw new FormatException($"line {tok.Line}: unknown string escape '\\{esc}'");
                        if (i + 2 >= end)
                            throw new FormatException($"line {tok.Line}: truncated hex escape");
                        int lo = HexDigit(_source[i + 2]);
                        if (lo < 0)
                            throw new FormatException($"line {tok.Line}: bad hex escape '\\{esc}{_source[i + 2]}'");
                        buf.Add((byte)((hi << 4) | lo));
                        i += 3;
                        break;
                    }
                }
            }
            var result = new byte[buf.Count];
            for (int k = 0; k < buf.Count; k++) result[k] = buf[k];
            return result;
        }

        /// <summary>
        /// Tokenize the full source into a list of tokens. Terminates with a
        /// single <see cref="TokenKind.Eof"/> token.
        ///
        /// <para>Per the annotations proposal, the <c>(@name …)</c> form
        /// relaxes the idchar class inside its body to include <c>[ ] { } ,
        /// ; :</c>. This lexer tracks annotation-depth to apply the broader
        /// rule within nested annotations and fall back to the spec-strict
        /// rule outside. The relaxed class also bypasses line-comment
        /// detection on single <c>;</c>.</para>
        /// </summary>
        public List<Token> Tokenize()
        {
            var tokens = new List<Token>(Math.Max(16, _source.Length / 8));
            int annotDepth = 0;
            while (true)
            {
                SkipTrivia(annotDepth);
                int line = _line, col = _col;
                int start = _pos;
                if (_pos >= _source.Length)
                {
                    tokens.Add(new Token(TokenKind.Eof, _pos, 0, line, col));
                    return tokens;
                }
                char c = _source[_pos];
                if (c == '(')
                {
                    // `(@` opens an annotation scope.
                    if (_pos + 1 < _source.Length && _source[_pos + 1] == '@')
                        annotDepth++;
                    else if (annotDepth > 0)
                        annotDepth++;   // nested paren within annotation — counted too
                    tokens.Add(new Token(TokenKind.LParen, start, 1, line, col));
                    Advance();
                    continue;
                }
                if (c == ')')
                {
                    if (annotDepth > 0) annotDepth--;
                    tokens.Add(new Token(TokenKind.RParen, start, 1, line, col));
                    Advance();
                    continue;
                }
                if (c == '"')
                {
                    int len = LexString(line, col);
                    tokens.Add(new Token(TokenKind.String, start, len, line, col));
                    continue;
                }
                bool inAnnot = annotDepth > 0;
                if (IsIdChar(c) || (inAnnot && IsAnnotExtChar(c)))
                {
                    int runStart = _pos;
                    int runLine = line, runCol = col;
                    while (_pos < _source.Length
                        && (IsIdChar(_source[_pos]) || (inAnnot && IsAnnotExtChar(_source[_pos]))))
                        Advance();
                    int len = _pos - runStart;
                    TokenKind kind;
                    if (_source[runStart] == '$') kind = TokenKind.Id;
                    else if (IsLowerLetter(_source[runStart])) kind = TokenKind.Keyword;
                    else kind = TokenKind.Reserved;
                    tokens.Add(new Token(kind, runStart, len, runLine, runCol));
                    continue;
                }
                throw new FormatException($"line {line}:{col}: unexpected character '{c}' (U+{(int)c:X4})");
            }
        }

        /// <summary>
        /// Characters the annotations proposal admits as idchars only inside
        /// a <c>(@name …)</c> body: brackets, braces, comma, and bare
        /// semicolon / colon (which are outside the spec's core idchar
        /// class). Single-char tokens like <c>(</c> and <c>)</c> stay as
        /// their own lexemes so annotation-level nesting still works.
        /// </summary>
        private static bool IsAnnotExtChar(char c)
        {
            switch (c)
            {
                case '[': case ']':
                case '{': case '}':
                case ',': case ';':
                    return true;
                default: return false;
            }
        }

        private void SkipTrivia(int annotDepth = 0)
        {
            while (_pos < _source.Length)
            {
                char c = _source[_pos];
                if (c == ' ' || c == '\t' || c == '\r')
                {
                    Advance();
                    continue;
                }
                if (c == '\n')
                {
                    _pos++;
                    _line++;
                    _col = 1;
                    continue;
                }
                // `;;` line comment. Per the annotations proposal,
                // comments still apply inside annotations — single `;`
                // becomes an idchar there, but `;;` is always a comment.
                if (c == ';' && _pos + 1 < _source.Length && _source[_pos + 1] == ';')
                {
                    while (_pos < _source.Length && _source[_pos] != '\n') Advance();
                    continue;
                }
                if (c == '(' && _pos + 1 < _source.Length && _source[_pos + 1] == ';')
                {
                    // Block comment — may nest
                    int startLine = _line, startCol = _col;
                    Advance(); Advance();       // consume '(;'
                    int depth = 1;
                    while (depth > 0)
                    {
                        if (_pos >= _source.Length)
                            throw new FormatException($"line {startLine}:{startCol}: unterminated block comment");
                        char d = _source[_pos];
                        if (d == '(' && _pos + 1 < _source.Length && _source[_pos + 1] == ';')
                        {
                            Advance(); Advance(); depth++;
                        }
                        else if (d == ';' && _pos + 1 < _source.Length && _source[_pos + 1] == ')')
                        {
                            Advance(); Advance(); depth--;
                        }
                        else if (d == '\n')
                        {
                            _pos++; _line++; _col = 1;
                        }
                        else
                        {
                            Advance();
                        }
                    }
                    continue;
                }
                break;
            }
        }

        private int LexString(int startLine, int startCol)
        {
            int start = _pos;
            Advance(); // opening quote
            while (_pos < _source.Length)
            {
                char c = _source[_pos];
                if (c == '"')
                {
                    Advance();
                    return _pos - start;
                }
                if (c == '\\')
                {
                    if (_pos + 1 >= _source.Length)
                        throw new FormatException($"line {startLine}:{startCol}: unterminated string escape");
                    Advance(); Advance();
                    continue;
                }
                if (c == '\n')
                {
                    // Spec disallows raw newlines inside strings.
                    throw new FormatException($"line {_line}:{_col}: newline inside string literal (use \\n)");
                }
                Advance();
            }
            throw new FormatException($"line {startLine}:{startCol}: unterminated string literal");
        }

        private void Advance()
        {
            _pos++;
            _col++;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static bool IsLowerLetter(char c) => c >= 'a' && c <= 'z';

        /// <summary>
        /// WAT idchar set (spec Appendix): alphanumeric + the symbol set
        /// <c>!#$%&amp;'*+-./:&lt;=&gt;?@\^_`|~</c>. Also includes digits and
        /// both letter cases.
        /// </summary>
        private static bool IsIdChar(char c)
        {
            if (c >= 'a' && c <= 'z') return true;
            if (c >= 'A' && c <= 'Z') return true;
            if (c >= '0' && c <= '9') return true;
            switch (c)
            {
                case '!': case '#': case '$': case '%': case '&': case '\'':
                case '*': case '+': case '-': case '.': case '/':
                case ':': case '<': case '=': case '>': case '?': case '@':
                case '\\': case '^': case '_': case '`': case '|': case '~':
                    return true;
                default:
                    return false;
            }
        }
    }
}
