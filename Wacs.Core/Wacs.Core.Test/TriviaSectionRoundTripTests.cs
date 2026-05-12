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
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Optimistic round-trip of WAT comments / annotations through the
    /// <c>wacs.trivia</c> custom section. WACS-specific (no spec), gated
    /// by <see cref="BinaryModuleParser.ParseTrivia"/>. Best-effort:
    /// source line/col aren't preserved, but the comment text and the
    /// element it attaches to do round-trip.
    /// </summary>
    public class TriviaSectionRoundTripTests
    {
        private static Module ParseWat(string wat)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wat));
            return TextModuleParser.ParseWat(ms);
        }

        private static Module BinaryRoundTrip(Module module)
        {
            var bytes = Wacs.Core.Bin.BinaryModuleWriter.Write(module);
            using var ms = new MemoryStream(bytes);
            bool priorTrivia = BinaryModuleParser.ParseTrivia;
            BinaryModuleParser.ParseTrivia = true;
            try
            {
                return BinaryModuleParser.ParseWasm(ms);
            }
            finally
            {
                BinaryModuleParser.ParseTrivia = priorTrivia;
            }
        }

        [Fact]
        public void TopLevelLineComment_SurvivesBinaryLeg()
        {
            const string wat = @"
;; module-level commentary
(module
  (func))";
            var watModule = ParseWat(wat);
            Assert.NotNull(watModule.Comments);

            var binModule = BinaryRoundTrip(watModule);
            Assert.NotNull(binModule.Comments);

            var moduleLevel = binModule.Comments![ModuleElementRef.ModuleLevel];
            Assert.Contains(moduleLevel,
                c => c.Text.Contains("module-level commentary"));
        }

        [Fact]
        public void FunctionAnnotation_SurvivesBinaryLeg()
        {
            const string wat = @"(module
  (@custom.tag ""payload-text"")
  (func))";
            var watModule = ParseWat(wat);
            Assert.NotNull(watModule.Annotations);

            var binModule = BinaryRoundTrip(watModule);
            Assert.NotNull(binModule.Annotations);

            var moduleLevel = binModule.Annotations![ModuleElementRef.ModuleLevel];
            Assert.Contains(moduleLevel, a => a.Name == "custom.tag");
        }

        [Fact]
        public void NoOptIn_NoTriviaParsed()
        {
            const string wat = @"
;; this stays in WAT side only
(module (func))";
            var watModule = ParseWat(wat);
            var bytes = Wacs.Core.Bin.BinaryModuleWriter.Write(watModule);

            using var ms = new MemoryStream(bytes);
            // ParseTrivia defaults to false — trivia bytes ride along
            // in the binary but the parser doesn't materialize them.
            var binModule = BinaryModuleParser.ParseWasm(ms);
            Assert.Null(binModule.Comments);
            Assert.Null(binModule.Annotations);
        }

        [Fact]
        public void TriviaFreeModule_EmitsNoSection()
        {
            // A module with no comments / annotations doesn't emit the
            // custom section at all — keeps the binary lean for the
            // common case.
            const string wat = "(module (func))";
            var watModule = ParseWat(wat);
            Assert.Null(watModule.Comments);
            Assert.Null(watModule.Annotations);

            var bytes = Wacs.Core.Bin.BinaryModuleWriter.Write(watModule);
            // Custom-section names appear as length-prefixed UTF-8 in
            // the binary. If the section weren't emitted, the literal
            // "wacs.trivia" doesn't appear in the byte stream.
            string asString = Encoding.UTF8.GetString(bytes);
            Assert.DoesNotContain("wacs.trivia", asString);
        }
    }
}
