// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    public class SExprParserTests
    {
        [Fact]
        public void Empty_source_parses_to_empty_list()
        {
            var result = SExprParser.Parse("");
            Assert.Empty(result);
        }

        [Fact]
        public void Single_top_level_module()
        {
            var result = SExprParser.Parse("(module)");
            Assert.Single(result);
            var sexpr = result[0];
            Assert.Equal(SExprKind.List, sexpr.Kind);
            Assert.True(sexpr.IsForm("module"));
            Assert.Single(sexpr.Children);  // head only
        }

        [Fact]
        public void Multiple_top_level_forms()
        {
            var result = SExprParser.Parse("(module) (assert_return)");
            Assert.Equal(2, result.Count);
            Assert.True(result[0].IsForm("module"));
            Assert.True(result[1].IsForm("assert_return"));
        }

        [Fact]
        public void Nested_sexpr_builds_tree()
        {
            // (module (func (param $x i32) (result i32) (i32.const 42)))
            var src = "(module (func (param $x i32) (result i32) (i32.const 42)))";
            var result = SExprParser.Parse(src);
            Assert.Single(result);

            var module = result[0];
            Assert.True(module.IsForm("module"));
            var func = module.Children[1];
            Assert.True(func.IsForm("func"));

            var param = func.Children[1];
            Assert.True(param.IsForm("param"));
            Assert.Equal("$x", param.Children[1].AtomText());
            Assert.Equal("i32", param.Children[2].AtomText());

            var result0 = func.Children[2];
            Assert.True(result0.IsForm("result"));
            Assert.Equal("i32", result0.Children[1].AtomText());

            var i32const = func.Children[3];
            Assert.True(i32const.IsForm("i32.const"));
            Assert.Equal("42", i32const.Children[1].AtomText());
        }

        [Fact]
        public void Unclosed_paren_throws()
        {
            Assert.Throws<FormatException>(() => SExprParser.Parse("(module"));
        }

        [Fact]
        public void Stray_close_paren_throws()
        {
            Assert.Throws<FormatException>(() => SExprParser.Parse(")"));
            Assert.Throws<FormatException>(() => SExprParser.Parse("(module))"));
        }

        [Fact]
        public void Comments_between_tokens_dont_break_structure()
        {
            var src = @"
                ;; leading comment
                (module   ;; trailing on opener
                  (; block comment ;)
                  (func)  ;; trailing
                )
                ;; trailing top level
            ";
            var result = SExprParser.Parse(src);
            Assert.Single(result);
            Assert.True(result[0].IsForm("module"));
            Assert.True(result[0].Children[1].IsForm("func"));
        }

        [Fact]
        public void ToString_roundtrip_approximates_input()
        {
            // We don't promise formatting parity, just structural roundtrip of
            // atoms and parens for a simple well-formed input.
            var src = "(module (func $f (result i32) (i32.const 1)))";
            var result = SExprParser.Parse(src);
            var rendered = result[0].ToString();
            // Re-parse the rendered form and check structural equality.
            var reparse = SExprParser.Parse(rendered);
            Assert.Single(reparse);
            Assert.True(reparse[0].IsForm("module"));
            Assert.Equal(src.Replace(" ", "").Replace("\t",""),
                rendered.Replace(" ", "").Replace("\t",""));
        }
    }
}
