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
    /// Pass 4 coverage: <see cref="TextModuleWriter"/> emits canonical
    /// WAT for tags, element segments, data segments, and GC type
    /// bodies — replacing the Phase-2 placeholders that just dropped
    /// these sections into the round-trip stream.
    /// </summary>
    public class StructuralEmissionTests
    {
        private static Module Parse(string wat)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wat));
            return TextModuleParser.ParseWat(ms);
        }

        // ---- Tags --------------------------------------------------------

        [Fact]
        public void Tag_RendersAsTypeRef()
        {
            const string wat = @"(module
              (type (func (param i32)))
              (tag (type 0)))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module);
            Assert.Contains("(tag (type 0))", output);
        }

        // ---- Data segments -----------------------------------------------

        [Fact]
        public void DataSegment_ActiveDefaultMemory_RendersWithOffset()
        {
            const string wat = @"(module
              (memory 1)
              (data (offset (i32.const 0)) ""hi""))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module);

            // The data bytes round-trip through BytesEncoder; the
            // form must contain (data (offset (i32.const 0)) "hi").
            Assert.Contains("(data ", output);
            Assert.Contains("(i32.const 0)", output);
            Assert.Contains("\"hi\"", output);
            // No more placeholder text.
            Assert.DoesNotContain("(round-trip not supported", output);
        }

        [Fact]
        public void DataSegment_Passive_RendersWithoutOffset()
        {
            const string wat = @"(module
              (memory 1)
              (data ""abc""))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module);

            Assert.Contains("(data \"abc\")", output);
        }

        // ---- Element segments --------------------------------------------

        [Fact]
        public void ElementSegment_ActiveFuncShortcut_RendersConcise()
        {
            const string wat = @"(module
              (func)
              (func)
              (table 2 funcref)
              (elem (offset (i32.const 0)) func 0 1))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module);

            // Func-shortcut form: `(elem (offset (i32.const 0)) func N M)`.
            Assert.Contains("(elem (offset (i32.const 0)) func 0 1)", output);
        }

        [Fact]
        public void ElementSegment_DeclarativeFunc_RendersDeclareKeyword()
        {
            const string wat = @"(module
              (func)
              (elem declare func 0))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module);

            Assert.Contains("(elem declare func 0)", output);
        }

        // ---- GC types ----------------------------------------------------

        [Fact]
        public void StructType_RendersFieldsWithMutability()
        {
            const string wat = @"(module
              (type (struct (field i32) (field (mut i64)))))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module);

            // Inside the canonical type body:
            //   (type (struct (field i32) (field (mut i64))))
            Assert.Contains("(struct", output);
            Assert.Contains("(field i32)", output);
            Assert.Contains("(field (mut i64))", output);
            Assert.DoesNotContain("(GC non-func body not supported", output);
        }

        [Fact]
        public void ArrayType_RendersElementType()
        {
            const string wat = @"(module
              (type (array (mut i8))))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module);

            Assert.Contains("(array (mut i8))", output);
        }

        [Fact]
        public void RecGroup_WrapsInRecKeyword()
        {
            const string wat = @"(module
              (rec
                (type (struct (field i32)))
                (type (sub 0 (struct (field i32) (field i64))))))";
            var module = Parse(wat);
            var output = TextModuleWriter.Write(module);

            // Multi-sub rec group wraps in (rec …). The second sub
            // carries a super index so it can't collapse to bare form.
            Assert.Contains("(rec", output);
            Assert.Contains("(sub 0", output);
        }

        // ---- Full module round-trip --------------------------------------

        [Fact]
        public void FullModule_AllStructuralSections_RoundTripCleanly()
        {
            // Modules that previously hit the "not supported in phase 2"
            // placeholders now render real WAT. Walk a representative
            // module through parse → write → re-parse and confirm
            // structure survives.
            const string wat = @"(module
              (type (func (result i32)))
              (memory 1)
              (table 1 funcref)
              (func (type 0) i32.const 7)
              (elem (offset (i32.const 0)) func 0)
              (data (offset (i32.const 0)) ""ok""))";
            var first = Parse(wat);
            var rendered = TextModuleWriter.Write(first);
            // Re-parse must succeed.
            var second = Parse(rendered);

            // Section counts must match.
            Assert.Equal(first.Types.Count, second.Types.Count);
            Assert.Equal(first.Funcs.Count, second.Funcs.Count);
            Assert.Equal(first.Memories.Count, second.Memories.Count);
            Assert.Equal(first.Tables.Count, second.Tables.Count);
            Assert.Equal(first.Elements.Length, second.Elements.Length);
            Assert.Equal(first.Datas.Length, second.Datas.Length);
        }
    }
}
