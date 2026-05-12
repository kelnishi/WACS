// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Text;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Pass 5 coverage: <see cref="TextWriterStyle.Folded"/> emits
    /// S-expression / folded form for pure (non-control) instruction
    /// chains. Falls back to flat at control-flow boundaries.
    /// </summary>
    public class FoldedEmissionTests
    {
        private static Module Parse(string wat)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wat));
            return TextModuleParser.ParseWat(ms);
        }

        private static readonly TextWriterOptions Folded =
            new() { Style = TextWriterStyle.Folded };

        [Fact]
        public void Folded_BinaryAdd_NestsOperands()
        {
            const string wat = @"(module
              (func (result i32)
                i32.const 1
                i32.const 2
                i32.add))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module, Folded);

            // The flat body
            //   i32.const 1
            //   i32.const 2
            //   i32.add
            // folds into a single nested expression.
            Assert.Contains("(i32.add (i32.const 1) (i32.const 2))", output);
        }

        [Fact]
        public void Folded_LocalSet_PullsRhsAsOperand()
        {
            const string wat = @"(module
              (func (param i32) (result i32)
                local.get 0
                i32.const 1
                i32.add
                local.set 0
                local.get 0))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module, Folded);

            // local.set 0 is effectful (consume=1, produce=0); its
            // operand chain folds into the operand position.
            Assert.Contains(
                "(local.set 0 (i32.add (local.get 0) (i32.const 1)))",
                output);
            // The standalone local.get 0 that follows leaves a value
            // on the stack; it drains as a flat folded leaf at the
            // end of the body.
            Assert.Contains("(local.get 0)", output);
        }

        [Fact]
        public void Folded_StoreOp_TwoOperands()
        {
            const string wat = @"(module
              (memory 1)
              (func
                i32.const 0
                i32.const 42
                i32.store))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module, Folded);

            // i32.store consume=2 (addr, value), produce=0 — emits as
            // a stand-alone folded line.
            Assert.Contains(
                "(i32.store (i32.const 0) (i32.const 42))",
                output);
        }

        [Fact]
        public void Folded_BlockInstruction_StaysFlat()
        {
            const string wat = @"(module
              (func (result i32)
                (block (result i32) i32.const 1)))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module, Folded);

            // Block-shaped instructions fall back to the flat emit
            // path (recursive folding lands in a later pass).
            Assert.Contains("block", output);
            Assert.Contains("end", output);
        }

        [Fact]
        public void Folded_CallInstruction_ChainBreaks()
        {
            const string wat = @"(module
              (func $a (result i32) i32.const 1)
              (func (result i32)
                call $a))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module, Folded);

            // call is a chain breaker — emits flat. The folder
            // doesn't try to compute call arity in this pass.
            Assert.Contains("call ", output);
        }

        [Fact]
        public void Canonical_StillFlat_UnaffectedByPass5()
        {
            // Canonical style (the default) MUST keep emitting flat
            // form so Pass 5 introduces zero on-the-wire change for
            // existing TextModuleWriter.Write(module) callers.
            const string wat = @"(module
              (func (result i32)
                i32.const 1
                i32.const 2
                i32.add))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module);  // default

            // Flat form: each instruction on its own line.
            Assert.Contains("i32.const 1", output);
            Assert.Contains("i32.const 2", output);
            Assert.Contains("i32.add", output);
            Assert.DoesNotContain("(i32.add (i32.const 1)", output);
        }

        [Fact]
        public void Folded_RoundTrip_ReparsesCleanly()
        {
            // The folded output must re-parse to a Module the test
            // can poke at. Validation tightness aside, the parser
            // needs to accept the canonical folded shape we emit.
            const string wat = @"(module
              (func (result i32)
                i32.const 1
                i32.const 2
                i32.add))";
            var first = Parse(wat);
            var folded = TextModuleWriter.Write(first, Folded);
            var second = Parse(folded);
            Assert.Single(second.Funcs);
            Assert.Equal(first.Funcs.Count, second.Funcs.Count);
        }
    }
}
