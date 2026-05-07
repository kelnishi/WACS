// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Linq;
using System.Text;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    public class LexerTests
    {
        private static TokenKind[] Kinds(string source)
        {
            var lex = new Lexer(source);
            return lex.Tokenize().Select(t => t.Kind).ToArray();
        }

        [Fact]
        public void Empty_source_produces_eof()
        {
            Assert.Equal(new[] { TokenKind.Eof }, Kinds(""));
            Assert.Equal(new[] { TokenKind.Eof }, Kinds("  \n  \t\r\n  "));
        }

        [Fact]
        public void Parens_and_atoms()
        {
            Assert.Equal(
                new[] { TokenKind.LParen, TokenKind.Keyword, TokenKind.RParen, TokenKind.Eof },
                Kinds("(module)"));
        }

        [Fact]
        public void Id_vs_keyword_vs_reserved_first_char_matters()
        {
            // $foo → Id.  foo → Keyword.  123 / -1 / +0 / 0x1A → Reserved.
            var lex = new Lexer("$foo bar 123 -1 +0 0x1A 1.5 1_000");
            var toks = lex.Tokenize();
            Assert.Equal(TokenKind.Id,       toks[0].Kind);
            Assert.Equal("$foo",             lex.Slice(toks[0]));
            Assert.Equal(TokenKind.Keyword,  toks[1].Kind);
            Assert.Equal("bar",              lex.Slice(toks[1]));
            Assert.Equal(TokenKind.Reserved, toks[2].Kind);
            Assert.Equal("123",              lex.Slice(toks[2]));
            Assert.Equal(TokenKind.Reserved, toks[3].Kind);
            Assert.Equal("-1",               lex.Slice(toks[3]));
            Assert.Equal(TokenKind.Reserved, toks[4].Kind);
            Assert.Equal("+0",               lex.Slice(toks[4]));
            Assert.Equal(TokenKind.Reserved, toks[5].Kind);
            Assert.Equal("0x1A",             lex.Slice(toks[5]));
            Assert.Equal(TokenKind.Reserved, toks[6].Kind);
            Assert.Equal("1.5",              lex.Slice(toks[6]));
            Assert.Equal(TokenKind.Reserved, toks[7].Kind);
            Assert.Equal("1_000",            lex.Slice(toks[7]));
            Assert.Equal(TokenKind.Eof,      toks[8].Kind);
        }

        [Fact]
        public void Line_comments_are_skipped()
        {
            var toks = new Lexer(";; one\n;; two\n(module);;tail\n").Tokenize();
            Assert.Equal(new[] { TokenKind.LParen, TokenKind.Keyword, TokenKind.RParen, TokenKind.Eof },
                toks.Select(t => t.Kind).ToArray());
        }

        [Fact]
        public void Block_comments_nest()
        {
            var src = "(; outer (; inner ;) still outer ;)(module)";
            var toks = new Lexer(src).Tokenize();
            Assert.Equal(new[] { TokenKind.LParen, TokenKind.Keyword, TokenKind.RParen, TokenKind.Eof },
                toks.Select(t => t.Kind).ToArray());
        }

        [Fact]
        public void Unterminated_block_comment_throws()
        {
            Assert.Throws<FormatException>(() => new Lexer("(; unterminated").Tokenize());
        }

        [Fact]
        public void Dot_is_an_idchar_so_i32_add_is_one_token()
        {
            var lex = new Lexer("i32.add");
            var toks = lex.Tokenize();
            Assert.Equal(2, toks.Count);
            Assert.Equal(TokenKind.Keyword, toks[0].Kind);
            Assert.Equal("i32.add", lex.Slice(toks[0]));
        }

        [Fact]
        public void String_escape_decoding()
        {
            var lex = new Lexer("\"abc\\n\\t\\\"\\41\"");
            var toks = lex.Tokenize();
            var bytes = lex.DecodeString(toks[0]);
            Assert.Equal(Encoding.ASCII.GetBytes("abc\n\t\"A"), bytes);
        }

        [Fact]
        public void String_unicode_escape()
        {
            var lex = new Lexer("\"\\u{2603}\"");  // snowman
            var toks = lex.Tokenize();
            var bytes = lex.DecodeString(toks[0]);
            var expected = Encoding.UTF8.GetBytes("☃");
            Assert.Equal(expected, bytes);
        }

        [Fact]
        public void Newline_inside_string_throws()
        {
            Assert.Throws<FormatException>(() => new Lexer("\"hello\nworld\"").Tokenize());
        }

        [Fact]
        public void Line_column_tracking()
        {
            var lex = new Lexer("(module\n  (func))");
            var toks = lex.Tokenize();
            Assert.Equal(1, toks[0].Line);   // (
            Assert.Equal(1, toks[0].Column);
            Assert.Equal(1, toks[1].Line);   // module
            Assert.Equal(2, toks[1].Column);
            Assert.Equal(2, toks[2].Line);   // (
            Assert.Equal(3, toks[2].Column);
            Assert.Equal(2, toks[3].Line);   // func
            Assert.Equal(4, toks[3].Column);
        }
    }
}
