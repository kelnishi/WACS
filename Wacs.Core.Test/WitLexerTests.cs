// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Linq;
using Wacs.Core.Components;
using Xunit;

namespace Wacs.Core.Test
{
    public class WitLexerTests
    {
        private static WitTokenKind[] Kinds(string source) =>
            new WitLexer(source).Tokenize().Select(t => t.Kind).ToArray();

        [Fact]
        public void Empty_source()
        {
            Assert.Equal(new[] { WitTokenKind.Eof }, Kinds(""));
        }

        [Fact]
        public void Punctuation_tokens()
        {
            Assert.Equal(
                new[] {
                    WitTokenKind.LBrace, WitTokenKind.RBrace,
                    WitTokenKind.LAngle, WitTokenKind.RAngle,
                    WitTokenKind.LParen, WitTokenKind.RParen,
                    WitTokenKind.Comma, WitTokenKind.Semi, WitTokenKind.Colon,
                    WitTokenKind.Dot, WitTokenKind.Slash, WitTokenKind.Equals,
                    WitTokenKind.At, WitTokenKind.Arrow, WitTokenKind.Star,
                    WitTokenKind.Eof,
                },
                Kinds("{}<>(),;:./=@->*"));
        }

        [Fact]
        public void Keyword_vs_identifier()
        {
            var lex = new WitLexer("interface foo record bar");
            var toks = lex.Tokenize();
            Assert.Equal(WitTokenKind.Keyword, toks[0].Kind);
            Assert.Equal("interface", lex.Slice(toks[0]));
            Assert.Equal(WitTokenKind.Ident,   toks[1].Kind);
            Assert.Equal("foo",                lex.Slice(toks[1]));
            Assert.Equal(WitTokenKind.Keyword, toks[2].Kind);
            Assert.Equal("record",             lex.Slice(toks[2]));
            Assert.Equal(WitTokenKind.Ident,   toks[3].Kind);
            Assert.Equal("bar",                lex.Slice(toks[3]));
        }

        [Fact]
        public void Kebab_case_identifier()
        {
            var lex = new WitLexer("my-interface-name");
            var toks = lex.Tokenize();
            Assert.Equal(WitTokenKind.Ident, toks[0].Kind);
            Assert.Equal("my-interface-name", lex.Slice(toks[0]));
        }

        [Fact]
        public void Percent_escape_forces_identifier()
        {
            var lex = new WitLexer("%interface");
            var toks = lex.Tokenize();
            Assert.Equal(WitTokenKind.Ident, toks[0].Kind);
        }

        [Fact]
        public void Integer_with_underscore()
        {
            var lex = new WitLexer("1_000");
            var toks = lex.Tokenize();
            Assert.Equal(WitTokenKind.Integer, toks[0].Kind);
            Assert.Equal("1_000", lex.Slice(toks[0]));
        }

        [Fact]
        public void Line_comment_skipped()
        {
            var lex = new WitLexer("// ignore me\nfoo");
            var toks = lex.Tokenize();
            Assert.Equal(WitTokenKind.Ident, toks[0].Kind);
            Assert.Equal("foo", lex.Slice(toks[0]));
        }

        [Fact]
        public void Block_comment_nests()
        {
            var lex = new WitLexer("/* outer /* inner */ still */ foo");
            var toks = lex.Tokenize();
            Assert.Equal(WitTokenKind.Ident, toks[0].Kind);
        }

        [Fact]
        public void String_with_escapes()
        {
            var lex = new WitLexer("\"hello\\n\\\"\"");
            var toks = lex.Tokenize();
            Assert.Equal(WitTokenKind.String, toks[0].Kind);
            Assert.Equal("hello\n\"", lex.DecodeString(toks[0]));
        }

        [Fact]
        public void Arrow_token()
        {
            Assert.Equal(new[] { WitTokenKind.Arrow, WitTokenKind.Eof }, Kinds("->"));
        }

        [Fact]
        public void Unterminated_string_throws()
        {
            Assert.Throws<FormatException>(() => new WitLexer("\"oops").Tokenize());
        }

        [Fact]
        public void Unterminated_block_comment_throws()
        {
            Assert.Throws<FormatException>(() => new WitLexer("/* never closes").Tokenize());
        }
    }
}
