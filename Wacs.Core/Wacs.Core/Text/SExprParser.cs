// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.Core.Text
{
    /// <summary>
    /// Parse a token stream into an s-expression tree. No WASM semantics — the
    /// input is just parens and atoms, the output is a flat list of top-level
    /// <see cref="SExpr"/> nodes.
    ///
    /// A .wat file is usually a single top-level <c>(module ...)</c>; a .wast
    /// file is a sequence of top-level forms (modules + assertions + invokes).
    /// This parser handles both shapes uniformly.
    /// </summary>
    public static class SExprParser
    {
        /// <summary>
        /// Parse a full source string into its sequence of top-level s-exprs.
        /// </summary>
        public static List<SExpr> Parse(string source)
        {
            var lex = new Lexer(source);
            var toks = lex.Tokenize();
            return ParseTokens(lex, toks);
        }

        /// <summary>
        /// Parse a pre-tokenized stream. Useful for tests or if the caller
        /// already holds a token list.
        /// </summary>
        public static List<SExpr> ParseTokens(Lexer lex, List<Token> tokens)
        {
            int i = 0;
            var result = new List<SExpr>();
            while (tokens[i].Kind != TokenKind.Eof)
            {
                result.Add(ParseOne(lex, tokens, ref i));
            }
            return result;
        }

        private static SExpr ParseOne(Lexer lex, List<Token> tokens, ref int i)
        {
            var tok = tokens[i];
            switch (tok.Kind)
            {
                case TokenKind.LParen:
                {
                    i++;
                    var children = new List<SExpr>();
                    while (tokens[i].Kind != TokenKind.RParen)
                    {
                        if (tokens[i].Kind == TokenKind.Eof)
                            throw new FormatException($"line {tok.Line}:{tok.Column}: unclosed '('");
                        children.Add(ParseOne(lex, tokens, ref i));
                    }
                    i++;   // consume ')'
                    return SExpr.MakeList(tok, children, lex);
                }
                case TokenKind.RParen:
                    throw new FormatException($"line {tok.Line}:{tok.Column}: unexpected ')'");
                case TokenKind.Eof:
                    throw new FormatException("unexpected end of input");
                default:
                    i++;
                    return SExpr.MakeAtom(tok, lex);
            }
        }
    }
}
