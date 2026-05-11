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
    /// Pass 6 coverage: <see cref="TextModuleWriter.WriteWithLineMap"/>
    /// returns a bidirectional map between module elements and the
    /// line / column spans they produced in the rendered WAT.
    /// </summary>
    public class LineMapTests
    {
        private static Module Parse(string wat)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wat));
            return TextModuleParser.ParseWat(ms);
        }

        [Fact]
        public void Write_RecordsSpansForEverySection()
        {
            const string wat = @"(module
              (type (func (result i32)))
              (memory 1)
              (func (type 0) i32.const 1)
              (export ""m"" (memory 0)))";
            var module = Parse(wat);

            var (text, map) = TextModuleWriter.WriteWithLineMap(module);

            // Each top-level section element gets a recorded span.
            Assert.NotNull(map.ByElement(
                new ModuleElementRef(ModuleElementKind.Type, 0)));
            Assert.NotNull(map.ByElement(
                new ModuleElementRef(ModuleElementKind.Memory, 0)));
            Assert.NotNull(map.ByElement(
                new ModuleElementRef(ModuleElementKind.Function, 0)));
            Assert.NotNull(map.ByElement(
                new ModuleElementRef(ModuleElementKind.Export, 0)));

            // The text we got back must contain the WAT we expect at
            // those line positions.
            string[] lines = text.Split('\n');
            var funcSpan = map.ByElement(
                new ModuleElementRef(ModuleElementKind.Function, 0))!.Value;
            // funcSpan covers the (func …) lines.
            Assert.Contains("(func", lines[funcSpan.StartLine - 1]);
        }

        [Fact]
        public void ByLine_FindsContainingElement()
        {
            const string wat = @"(module
              (type (func (result i32)))
              (func (type 0) i32.const 1))";
            var module = Parse(wat);
            var (text, map) = TextModuleWriter.WriteWithLineMap(module);

            // Walk lines, asking ByLine for each. At least one line
            // should resolve to Function[0] (the func emit), and at
            // least one line should resolve to Type[0].
            int lineCount = text.Split('\n').Length;
            bool sawType = false, sawFunc = false;
            for (int i = 1; i <= lineCount; i++)
            {
                var elem = map.ByLine(i);
                if (elem == null) continue;
                if (elem.Value.Kind == ModuleElementKind.Type) sawType = true;
                if (elem.Value.Kind == ModuleElementKind.Function) sawFunc = true;
            }
            Assert.True(sawType, "expected Type[0] to cover at least one line");
            Assert.True(sawFunc, "expected Function[0] to cover at least one line");
        }

        [Fact]
        public void Write_BackwardCompatible_NoLineMap_StillWorks()
        {
            // The default Write(module) path remains a string return.
            const string wat = "(module (func))";
            var module = Parse(wat);
            string text = TextModuleWriter.Write(module);
            Assert.Contains("(module", text);
        }

        [Fact]
        public void LineMap_All_EnumeratesRecordedSpans()
        {
            const string wat = @"(module
              (type (func))
              (type (func (result i32))))";
            var module = Parse(wat);
            var (_, map) = TextModuleWriter.WriteWithLineMap(module);

            // Two type spans recorded.
            Assert.Equal(2, map.All.Count);
            Assert.True(map.All.ContainsKey(
                new ModuleElementRef(ModuleElementKind.Type, 0)));
            Assert.True(map.All.ContainsKey(
                new ModuleElementRef(ModuleElementKind.Type, 1)));
        }
    }
}
