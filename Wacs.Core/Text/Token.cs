// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.Core.Text
{
    public enum TokenKind : byte
    {
        Eof = 0,
        LParen,         // (
        RParen,         // )
        Keyword,        // lowercase-initial idchars: i32.add, module, param, …
        Id,             // '$' idchars: $foo
        String,         // "..." with escapes
        Reserved,       // any other idchar run — numbers land here and are parsed by context
    }

    /// <summary>
    /// A lexeme in a .wat / .wast source. Holds offsets into the original source
    /// text; use <see cref="Lexer.Slice"/> / <see cref="Lexer.SliceString"/> to
    /// materialize the lexeme when needed.
    /// </summary>
    public readonly struct Token
    {
        public readonly TokenKind Kind;
        public readonly int Start;
        public readonly int Length;
        public readonly int Line;
        public readonly int Column;

        public Token(TokenKind kind, int start, int length, int line, int column)
        {
            Kind = kind;
            Start = start;
            Length = length;
            Line = line;
            Column = column;
        }

        public override string ToString() => $"{Kind}@{Line}:{Column}+{Length}";
    }
}
