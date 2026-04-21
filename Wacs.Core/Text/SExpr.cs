// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;

namespace Wacs.Core.Text
{
    public enum SExprKind : byte
    {
        /// <summary>A parenthesized list: <c>(head child1 child2 ...)</c>.</summary>
        List,
        /// <summary>A bare atom — keyword, id, reserved, or string.</summary>
        Atom,
    }

    /// <summary>
    /// A node in the parsed s-expression tree. Atoms carry their lexeme via
    /// <see cref="Token"/>; lists carry child nodes. Lists also expose a
    /// convenience <see cref="Head"/> for the first atom (typically the form
    /// name like <c>module</c>, <c>func</c>, <c>i32.add</c>).
    /// </summary>
    public sealed class SExpr
    {
        public SExprKind Kind { get; }
        public Token Token { get; }       // for Atom; for List this is the opening '('
        public List<SExpr> Children { get; }  // empty list for Atom

        // Convenience: the lexer that produced this node. Useful for decoding
        // string tokens / rendering slices without threading a context
        // everywhere.
        public Lexer Lexer { get; }

        private SExpr(SExprKind kind, Token token, List<SExpr> children, Lexer lexer)
        {
            Kind = kind;
            Token = token;
            Children = children;
            Lexer = lexer;
        }

        public static SExpr MakeAtom(Token token, Lexer lexer) =>
            new SExpr(SExprKind.Atom, token, EmptyChildren, lexer);

        public static SExpr MakeList(Token openParen, List<SExpr> children, Lexer lexer) =>
            new SExpr(SExprKind.List, openParen, children, lexer);

        private static readonly List<SExpr> EmptyChildren = new List<SExpr>();

        /// <summary>
        /// The first child if this is a non-empty list and its first child is
        /// an atom; otherwise null. Commonly used to peek at the form name
        /// (<c>module</c>, <c>func</c>, <c>param</c>, <c>i32.add</c>, etc).
        /// </summary>
        public SExpr? Head
        {
            get
            {
                if (Kind != SExprKind.List) return null;
                if (Children.Count == 0) return null;
                return Children[0];
            }
        }

        /// <summary>
        /// True if this node is a list whose head atom's lexeme equals
        /// <paramref name="keyword"/>. Comparison is ordinal.
        /// </summary>
        public bool IsForm(string keyword)
        {
            if (Kind != SExprKind.List) return false;
            var h = Head;
            if (h == null || h.Kind != SExprKind.Atom) return false;
            if (h.Token.Kind != TokenKind.Keyword) return false;
            return Lexer.Slice(h.Token) == keyword;
        }

        /// <summary>
        /// The lexeme text of this atom. Throws on lists.
        /// For <see cref="TokenKind.Id"/> tokens, canonicalizes the
        /// <c>$"..."</c> quoted form to the equivalent <c>$name</c> form
        /// so namespace lookups unify both spellings.
        /// </summary>
        public string AtomText()
        {
            if (Kind != SExprKind.Atom)
                throw new System.InvalidOperationException("AtomText called on a list");
            var raw = Lexer.Slice(Token);
            if (Token.Kind == TokenKind.Id
                && raw.Length >= 3 && raw[0] == '$' && raw[1] == '"' && raw[raw.Length - 1] == '"')
                return "$" + raw.Substring(2, raw.Length - 3);
            return raw;
        }

        public override string ToString()
        {
            if (Kind == SExprKind.Atom) return AtomText();
            var sb = new System.Text.StringBuilder();
            sb.Append('(');
            for (int i = 0; i < Children.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(Children[i].ToString());
            }
            sb.Append(')');
            return sb.ToString();
        }
    }
}
