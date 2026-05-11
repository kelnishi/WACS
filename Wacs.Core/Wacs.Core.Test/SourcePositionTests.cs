// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Linq;
using System.Text;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Pass B coverage: <see cref="Module.SourcePositions"/> populated
    /// by <see cref="TextModuleParser"/>. Per-instruction source coords
    /// survive parsing without further work; binary-parsed modules
    /// leave the side-table null (lazy-allocation invariant).
    /// </summary>
    public class SourcePositionTests
    {
        private static Module Parse(string wat)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wat));
            return TextModuleParser.ParseWat(ms);
        }

        [Fact]
        public void FlatInstructions_GetSourceLine()
        {
            const string wat = @"(module
              (func (result i32)
                i32.const 1
                i32.const 2
                i32.add))";
            var module = Parse(wat);

            // Every body instruction was memoized.
            Assert.NotNull(module.SourcePositions);

            var body = module.Funcs[0].Body.Instructions;
            // body shape: i32.const 1 (line 3), i32.const 2 (line 4),
            // i32.add (line 5), trailing InstEnd (synthesized — no
            // source position attached).
            var consts = body.OfType<InstI32Const>().ToArray();
            Assert.Equal(2, consts.Length);

            var pos0 = module.SourcePositions![consts[0]];
            var pos1 = module.SourcePositions[consts[1]];
            Assert.Equal(3, pos0.Line);
            Assert.Equal(4, pos1.Line);

            // i32.add is on line 5.
            var addInst = body.OfType<InstructionBase>()
                .First(i => i.Op.GetMnemonic() == "i32.add");
            Assert.Equal(5, module.SourcePositions[addInst].Line);
        }

        [Fact]
        public void FoldedForm_AllOperands_GetOuterFormPosition()
        {
            // A folded form (i32.add (i32.const 1) (i32.const 2)) emits
            // three instructions; all share the outermost form's line.
            const string wat = @"(module
              (func (result i32)
                (i32.add (i32.const 1) (i32.const 2))))";
            var module = Parse(wat);

            var body = module.Funcs[0].Body.Instructions;
            var consts = body.OfType<InstI32Const>().ToArray();
            var addInst = body.OfType<InstructionBase>()
                .First(i => i.Op.GetMnemonic() == "i32.add");

            // All three (the two i32.const + the i32.add) point to
            // the same line — the outer (i32.add …) form's open paren.
            Assert.Equal(3, module.SourcePositions![consts[0]].Line);
            Assert.Equal(3, module.SourcePositions[consts[1]].Line);
            Assert.Equal(3, module.SourcePositions[addInst].Line);
        }

        [Fact]
        public void SourceOffset_PointsIntoSource()
        {
            const string wat = "(module (func (result i32) i32.const 42))";
            var module = Parse(wat);

            var ic = module.Funcs[0].Body.Instructions
                .OfType<InstI32Const>().Single();
            var pos = module.SourcePositions![ic];

            // The source-offset must point at "i32.const" in the
            // original string.
            Assert.Equal("i32.const",
                wat.Substring(pos.SourceOffset, "i32.const".Length));
        }

        [Fact]
        public void BinaryParsed_ModuleHasNullSourcePositions()
        {
            // Build a tiny wasm via the round-trip path: parse WAT,
            // emit binary, re-parse binary. The binary-parsed module
            // must NOT carry source positions — Pass B is WAT-only.
            const string wat = "(module (func (result i32) i32.const 1))";
            var watModule = Parse(wat);
            var bytes = Wacs.Core.Bin.BinaryModuleWriter.Write(watModule);
            using var ms = new MemoryStream(bytes);
            var binModule = BinaryModuleParser.ParseWasm(ms);

            Assert.Null(binModule.SourcePositions);
        }

        [Fact]
        public void CommentFree_NoExtraSourcePositionsCost()
        {
            // The side-table is lazy-allocated only when at least one
            // instruction is parsed. An empty (module) has no body, so
            // SourcePositions stays null.
            const string wat = "(module)";
            var module = Parse(wat);
            Assert.Null(module.SourcePositions);
        }
    }
}
