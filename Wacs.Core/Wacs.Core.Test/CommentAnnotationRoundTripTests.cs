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
    /// Pass 3 coverage: comments and <c>(@…)</c> annotations captured
    /// by <see cref="TextModuleParser"/> survive a round-trip through
    /// <see cref="TextModuleWriter"/> at their attached positions.
    /// Section-internal trivia (comments inside a function body, etc.)
    /// is a later pass; this test focuses on module-level placement
    /// since that's where Pass 3 wires up.
    /// </summary>
    public class CommentAnnotationRoundTripTests
    {
        private static Module Parse(string wat)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wat));
            return TextModuleParser.ParseWat(ms);
        }

        [Fact]
        public void LineComments_BeforeSections_SurviveRoundTrip()
        {
            const string wat = @";; top of file
(module
  ;; before first type
  (type (func (result i32)))
  ;; before the func
  (func (type 0) i32.const 1)
)";
            var module = Parse(wat);

            // Module.Comments should hold three line comments — one
            // module-level (before `(module`) and two attached to
            // section elements.
            Assert.NotNull(module.Comments);
            var moduleLevel = module.Comments![ModuleElementRef.ModuleLevel];
            Assert.Contains(moduleLevel,
                c => c.Text == ";; top of file");

            // The "before first type" comment attaches to Type[0].
            var typeOwner = new ModuleElementRef(ModuleElementKind.Type, 0);
            Assert.Contains(module.Comments[typeOwner],
                c => c.Text == ";; before first type");

            // The "before the func" comment attaches to Function[0].
            var funcOwner = new ModuleElementRef(ModuleElementKind.Function, 0);
            Assert.Contains(module.Comments[funcOwner],
                c => c.Text == ";; before the func");

            // Round-trip: the writer emits each comment ahead of its
            // owner. We don't require byte-equality, just survival.
            string roundTripped = TextModuleWriter.Write(module);
            Assert.Contains(";; top of file", roundTripped);
            Assert.Contains(";; before first type", roundTripped);
            Assert.Contains(";; before the func", roundTripped);
        }

        [Fact]
        public void BlockComment_SurvivesRoundTrip()
        {
            const string wat = @"(module
  (; explanatory block ;)
  (type (func (result i32)))
)";
            var module = Parse(wat);

            Assert.NotNull(module.Comments);
            var typeOwner = new ModuleElementRef(ModuleElementKind.Type, 0);
            var typeComments = module.Comments![typeOwner];
            Assert.Contains(typeComments,
                c => c.Kind == TriviaKind.BlockComment
                  && c.Text == "(; explanatory block ;)");

            string roundTripped = TextModuleWriter.Write(module);
            Assert.Contains("(; explanatory block ;)", roundTripped);
        }

        [Fact]
        public void ModuleLevelAnnotation_RouteIntoAnnotationsTable()
        {
            // (@custom …) is a generic annotation form — captured
            // verbatim onto Module.Annotations at module-level.
            const string wat = @"(module
  (@custom ""abc"")
  (type (func (result i32)))
)";
            var module = Parse(wat);

            Assert.NotNull(module.Annotations);
            var ann = module.Annotations![ModuleElementRef.ModuleLevel];
            Assert.Single(ann);
            Assert.Equal("custom", ann[0].Name);
            // Payload retains the quoted string, trimmed.
            Assert.Equal("\"abc\"", ann[0].Payload);

            // Writer re-emits as `(@custom "abc")`.
            string roundTripped = TextModuleWriter.Write(module);
            Assert.Contains("(@custom \"abc\")", roundTripped);
        }

        [Fact]
        public void CommentFreeModule_LeavesSideTablesNull()
        {
            const string wat = "(module (func (result i32) i32.const 1))";
            var module = Parse(wat);

            // Lazy allocation — modules without comments / annotations
            // never touch the dictionaries.
            Assert.Null(module.Comments);
            Assert.Null(module.Annotations);
        }
    }
}
