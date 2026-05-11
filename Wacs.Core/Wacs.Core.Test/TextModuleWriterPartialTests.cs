// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.Core.Text;
using Wacs.Core.Types;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Pass 1 coverage: <c>TextModuleWriter.WriteFunction</c> partial-
    /// render entry point plus the StackAnnotated mode that the CLI's
    /// validation-error reporter now uses (replacing the retired
    /// <c>ModuleRenderer.RenderFunctionWat</c>).
    /// </summary>
    public class TextModuleWriterPartialTests
    {
        private static Module Parse(string wat)
        {
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(wat));
            return TextModuleParser.ParseWat(ms);
        }

        [Fact]
        public void WriteFunction_Canonical_RendersSingleFunc()
        {
            const string wat = @"
            (module
              (func $a (result i32) i32.const 1)
              (func $b (param i32) (result i32)
                local.get 0
                i32.const 1
                i32.add))";
            var module = Parse(wat);

            // Function index 1 is the second defined func (no imports).
            string text = TextModuleWriter.WriteFunction(
                module, (FuncIdx)1u, indent: "");

            Assert.Contains("(func", text);
            Assert.Contains("local.get 0", text);
            Assert.Contains("i32.add", text);
            // Canonical form does NOT carry stack-state side comments
            // — those only appear in StackAnnotated mode.
            Assert.DoesNotContain(";;", text);
        }

        [Fact]
        public void WriteFunction_StackAnnotated_AddsStackComments()
        {
            // Locals-touching functions surface a pre-existing latent
            // bug in StackRenderer.BuildDummyContext (it reserves
            // result-arity slots only, so local.get N at render time
            // index-fails). Out of scope for Pass 1; use a function
            // with no local refs.
            const string wat = @"
            (module
              (func $b (result i32)
                i32.const 1
                i32.const 2
                i32.add))";
            var module = Parse(wat);

            string text = TextModuleWriter.WriteFunction(
                module, (FuncIdx)0u, indent: "",
                TextWriterOptions.StackAnnotated);

            // StackAnnotated routes through Function.RenderText, which
            // emits per-instruction stack-state side comments using
            // (; … ;) block markers (e.g. `(;I >;)` showing the stack
            // after pushing an i32). Canonical mode wouldn't emit any
            // of that; presence of the block-comment marker is the
            // diagnostic-mode signature.
            Assert.Contains("i32.add", text);
            Assert.Contains("(;", text);
            // The header carries an id comment. After Pass C
            // follow-up, WAT-parsed funcs with `$name` propagate the
            // name onto Function.Id, so the comment is `(;$b;)`
            // rather than the index-only `(;0;)`.
            Assert.Contains("(;$b;)", text);
        }

        [Fact]
        public void WriteFunction_OutOfRange_ReturnsEmpty()
        {
            const string wat = @"
            (module
              (func (result i32) i32.const 1))";
            var module = Parse(wat);

            // Index 99 is past the defined functions; the partial-
            // render path returns empty rather than throwing.
            string text = TextModuleWriter.WriteFunction(
                module, (FuncIdx)99u, indent: "");
            Assert.Equal(string.Empty, text);
        }

        [Fact]
        public void TextDiagnostics_GetFuncIdx_ParsesValidationPath()
        {
            // Validation paths follow FluentValidation's
            // PropertyName chain: "Function[3].Expression[7].…".
            FuncIdx idx = TextDiagnostics.GetFuncIdx(
                "Function[3].Expression[7].NumericInst");
            Assert.Equal(3u, idx.Value);

            // A path that doesn't begin with Function[N] returns the
            // default sentinel.
            Assert.Equal(FuncIdx.Default,
                TextDiagnostics.GetFuncIdx("Globals[0].Initializer"));
        }

        [Fact]
        public void TextDiagnostics_ChopFunctionId_ReturnsHeadToken()
        {
            string head = TextDiagnostics.ChopFunctionId(
                "Function[12].Expression[3]");
            Assert.Equal("Function[12]", head);
        }
    }
}
