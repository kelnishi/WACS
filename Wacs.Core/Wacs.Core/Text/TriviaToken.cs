// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.Core.Text
{
    /// <summary>
    /// The two trivia shapes the WAT lexer recognizes and discards
    /// when running in canonical mode. <see cref="LineComment"/> is
    /// <c>;; …</c> through end-of-line; <see cref="BlockComment"/> is
    /// <c>(; … ;)</c> with optional nesting.
    /// </summary>
    public enum TriviaKind : byte
    {
        LineComment = 1,
        BlockComment = 2,
    }

    /// <summary>
    /// A comment captured from the source by
    /// <see cref="Lexer.TokenizeWithTrivia"/>. Holds enough position
    /// metadata to attach to the nearest following non-trivia token
    /// when round-tripping is required.
    ///
    /// <para>The <see cref="Start"/> / <see cref="Length"/> pair refers
    /// into the original <see cref="Lexer.Source"/> string and
    /// includes the comment delimiters (<c>;;</c> or <c>(;…;)</c>).
    /// The raw text can be reconstructed via
    /// <see cref="Lexer.SliceTrivia"/>.</para>
    ///
    /// <para>Trivia tokens are kept on a side-band list rather than
    /// being threaded into the main token stream so the existing
    /// parser (SExpr / TextModuleParser) sees an unchanged token
    /// shape. Position information lets Pass-3 attachment code
    /// associate each trivia with the AST node it precedes.</para>
    /// </summary>
    public readonly struct TriviaToken
    {
        public readonly TriviaKind Kind;
        public readonly int Start;
        public readonly int Length;
        public readonly int Line;
        public readonly int Column;

        public TriviaToken(
            TriviaKind kind, int start, int length, int line, int column)
        {
            Kind = kind;
            Start = start;
            Length = length;
            Line = line;
            Column = column;
        }

        public override string ToString() =>
            $"{Kind}@{Line}:{Column}+{Length}";
    }
}
