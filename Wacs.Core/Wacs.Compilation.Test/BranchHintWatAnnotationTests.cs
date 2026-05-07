// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Linq;
using Wacs.Core;
using Wacs.Core.Instructions;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Compilation.Test
{
    /// <summary>
    /// Phase-1 follow-up: the WAT parser also recognizes
    /// <c>(@metadata.code.branch_hint "\xx")</c> annotations and
    /// attaches the resulting <see cref="BranchHint"/> to the
    /// next-emitted <c>if</c> / <c>br_if</c> instruction. The hint
    /// surfaces via <see cref="Module.BranchHintMap.TryGet(InstructionBase)"/>,
    /// the same lookup the transpiler uses for binary inputs.
    /// </summary>
    public class BranchHintWatAnnotationTests
    {
        private const string AnnotatedIfModule = @"
(module
  (func (export ""f"") (param i32) (result i32)
    local.get 0
    (@metadata.code.branch_hint ""\01"")
    if (result i32)
      i32.const 100
    else
      i32.const 200
    end))";

        [Fact]
        public void WatParserCapturesBranchHintOnIf()
        {
            BinaryModuleParser.ParseBranchHints = true;
            var module = TextModuleParser.ParseWat(AnnotatedIfModule);

            Assert.NotNull(module.BranchHints);

            // Walk the function body, find the `if`, look it up by ref.
            var ifInst = FindFirst<InstIf>(module.Funcs[0].Body.Instructions);
            Assert.NotNull(ifInst);

            var hint = module.BranchHints!.TryGet(ifInst!);
            Assert.NotNull(hint);
            Assert.True(hint!.Value.IsLikely);
            Assert.Equal((byte)0x01, hint.Value.HintByte);
        }

        [Fact]
        public void GateRespectsParserFeatureFlag()
        {
            // Flag off → annotation skipped silently, no BranchHints map.
            BinaryModuleParser.ParseBranchHints = false;
            var module = TextModuleParser.ParseWat(AnnotatedIfModule);
            Assert.Null(module.BranchHints);
        }

        [Fact]
        public void RejectsDuplicateAnnotationOnSameInstruction()
        {
            const string wat = @"
(module
  (func (export ""f"") (param i32) (result i32)
    local.get 0
    (@metadata.code.branch_hint ""\01"")
    (@metadata.code.branch_hint ""\01"")
    if (result i32)
      i32.const 1
    else
      i32.const 2
    end))";
            BinaryModuleParser.ParseBranchHints = true;
            var ex = Assert.Throws<FormatException>(() => TextModuleParser.ParseWat(wat));
            Assert.Contains("duplicate annotation", ex.Message);
        }

        [Fact]
        public void RejectsAnnotationOnInvalidTarget()
        {
            // Hint precedes `i32.eq`, not an if/br_if. Spec: invalid target.
            const string wat = @"
(module
  (func (export ""f"") (param i32 i32) (result i32)
    local.get 0
    local.get 1
    (@metadata.code.branch_hint ""\01"")
    i32.eq))";
            BinaryModuleParser.ParseBranchHints = true;
            var ex = Assert.Throws<FormatException>(() => TextModuleParser.ParseWat(wat));
            Assert.Contains("invalid target", ex.Message);
        }

        [Fact]
        public void RejectsModuleLevelBranchHintAnnotation()
        {
            const string wat = @"
(module
  (@metadata.code.branch_hint ""\01"")
  (func (export ""f"") (result i32) i32.const 42))";
            BinaryModuleParser.ParseBranchHints = true;
            var ex = Assert.Throws<FormatException>(() => TextModuleParser.ParseWat(wat));
            Assert.Contains("not in a function", ex.Message);
        }

        [Fact]
        public void FoldedIfShapeAlsoCapturesHint()
        {
            const string wat = @"
(module
  (func (export ""f"") (param i32) (result i32)
    (@metadata.code.branch_hint ""\00"")
    (if (result i32) (local.get 0)
      (then (i32.const 1))
      (else (i32.const 2)))))";
            BinaryModuleParser.ParseBranchHints = true;
            var module = TextModuleParser.ParseWat(wat);

            var ifInst = FindFirst<InstIf>(module.Funcs[0].Body.Instructions);
            Assert.NotNull(ifInst);
            var hint = module.BranchHints?.TryGet(ifInst!);
            Assert.NotNull(hint);
            Assert.False(hint!.Value.IsLikely);
        }

        // -----------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------

        private static T? FindFirst<T>(System.Collections.Generic.IEnumerable<InstructionBase> body)
            where T : InstructionBase
        {
            foreach (var inst in body)
            {
                if (inst is T t) return t;
                if (inst is IBlockInstruction block)
                {
                    for (int i = 0; i < block.Count; i++)
                    {
                        var nested = FindFirst<T>(block.GetBlock(i).Instructions);
                        if (nested != null) return nested;
                    }
                }
            }
            return null;
        }
    }
}
