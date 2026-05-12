// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Pass 2 coverage: <c>Lexer.TokenizeWithTrivia</c> captures both
    /// line and block comments while keeping the regular token stream
    /// identical to <c>Lexer.Tokenize</c>.
    /// </summary>
    public class LexerTriviaTests
    {
        [Fact]
        public void Tokenize_Default_DropsAllComments()
        {
            const string src = ";; lead\n(module ;; trail\n  (func))  (; block ;)";
            var lex = new Lexer(src);
            var toks = lex.Tokenize();

            // No TriviaKind in the regular token enum — comments stay
            // invisible on this path. The token sequence should be the
            // parser-level shape: ( module ( func ) ) EOF.
            Assert.Equal(TokenKind.LParen, toks[0].Kind);
            Assert.Equal("module", lex.Slice(toks[1]));
            Assert.Equal(TokenKind.LParen, toks[2].Kind);
            Assert.Equal("func", lex.Slice(toks[3]));
            Assert.Equal(TokenKind.RParen, toks[4].Kind);
            Assert.Equal(TokenKind.RParen, toks[5].Kind);
            Assert.Equal(TokenKind.Eof, toks[6].Kind);
        }

        [Fact]
        public void TokenizeWithTrivia_CapturesLineAndBlockComments()
        {
            const string src = ";; lead\n(module ;; trail\n  (func))  (; block ;)";
            var lex = new Lexer(src);
            var (toks, trivia) = lex.TokenizeWithTrivia();

            // The regular token stream is the same as the default Tokenize.
            Assert.Equal(TokenKind.LParen, toks[0].Kind);
            Assert.Equal("module", lex.Slice(toks[1]));

            // Trivia: three comments — leading ;;, trailing ;;, then
            // a final (;…;) block.
            Assert.Equal(3, trivia.Count);
            Assert.Equal(TriviaKind.LineComment, trivia[0].Kind);
            Assert.Equal(TriviaKind.LineComment, trivia[1].Kind);
            Assert.Equal(TriviaKind.BlockComment, trivia[2].Kind);

            // Raw slices include the delimiters.
            Assert.Equal(";; lead", lex.SliceTrivia(trivia[0]));
            Assert.Equal(";; trail", lex.SliceTrivia(trivia[1]));
            Assert.Equal("(; block ;)", lex.SliceTrivia(trivia[2]));
        }

        [Fact]
        public void TokenizeWithTrivia_NestedBlockComments()
        {
            const string src = "(; outer (; inner ;) tail ;)\n(module)";
            var lex = new Lexer(src);
            var (toks, trivia) = lex.TokenizeWithTrivia();

            // The inner comment is consumed by the outer's depth
            // counter; we should see exactly one BlockComment trivia
            // covering the whole "(;…(;…;)…;)" run.
            Assert.Single(trivia);
            Assert.Equal(TriviaKind.BlockComment, trivia[0].Kind);
            Assert.Equal("(; outer (; inner ;) tail ;)",
                lex.SliceTrivia(trivia[0]));

            // Tokens unaffected — same shape as the comment-free source.
            Assert.Equal(TokenKind.LParen, toks[0].Kind);
            Assert.Equal("module", lex.Slice(toks[1]));
        }

        [Fact]
        public void TokenizeWithTrivia_LineCommentAtEof_NoCrash()
        {
            // No terminating newline — the lexer should still close
            // the comment cleanly and emit EOF.
            const string src = "(module) ;; eof comment";
            var lex = new Lexer(src);
            var (toks, trivia) = lex.TokenizeWithTrivia();

            Assert.Equal(TokenKind.Eof, toks[^1].Kind);
            Assert.Single(trivia);
            Assert.Equal(TriviaKind.LineComment, trivia[0].Kind);
        }

        [Fact]
        public void ModuleAnnotations_StartsEmpty()
        {
            // A freshly-parsed Module has no captured trivia until
            // Pass 3 wires the parser to populate the side-tables.
            // For Pass 2 we just verify the dictionaries are lazy.
            const string wat = "(module (func (result i32) i32.const 1))";
            using var ms = new System.IO.MemoryStream(
                System.Text.Encoding.UTF8.GetBytes(wat));
            var module = TextModuleParser.ParseWat(ms);

            Assert.Null(module.Annotations);
            Assert.Null(module.Comments);
        }

        [Fact]
        public void ModuleAnnotations_LazyAllocation()
        {
            const string wat = "(module)";
            using var ms = new System.IO.MemoryStream(
                System.Text.Encoding.UTF8.GetBytes(wat));
            var module = TextModuleParser.ParseWat(ms);

            module.AddComment(ModuleElementRef.ModuleLevel, new WatComment
            {
                Kind = TriviaKind.LineComment,
                Text = ";; hi",
                Line = 1,
                Column = 1,
            });

            Assert.NotNull(module.Comments);
            Assert.Single(module.Comments!);
            Assert.Equal(";; hi",
                module.Comments![ModuleElementRef.ModuleLevel][0].Text);
        }
    }
}
