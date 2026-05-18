// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.IO;
using Wacs.ComponentModel.Runtime.Parser;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Binary type-section coverage for the three async-ABI
    /// type tags (0x64 / 0x65 / 0x66). Pairs with
    /// <see cref="CanonAsyncBuiltinTests"/> to close out
    /// Phase 2's acceptance criterion: every async type +
    /// canon-builtin shape parses end-to-end from hand-built
    /// byte sequences.
    ///
    /// <para>Wire encoding of the three tags (from
    /// component-model main HEAD design/mvp/Binary.md):</para>
    /// <code>
    /// 0x64                          => error-context
    /// 0x65 t?:&lt;valtype&gt;?      => future
    /// 0x66 t?:&lt;valtype&gt;?      => stream
    /// </code>
    /// </summary>
    public class AsyncDefTypeBinaryTests
    {
        private static byte[] TypeSection(byte count, params byte[][] entries)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(count);
            foreach (var e in entries) ms.Write(e, 0, e.Length);
            return ms.ToArray();
        }

        [Fact]
        public void Stream_with_u8_element_decodes()
        {
            // 0x66 0x01 0x7d  => stream<u8>
            //   0x01 = optional-valtype "present"
            //   0x7d = signed-LEB i33 for -3 = U8 (ComponentPrim.U8 = -0x03)
            var defs = TypeSectionReader.Decode(
                TypeSection(1, new byte[] { 0x66, 0x01, 0x7d }));
            var stream = Assert.IsType<ComponentStreamType>(Assert.Single(defs));
            Assert.NotNull(stream.Element);
            Assert.True(stream.Element!.Value.IsPrimitive);
            Assert.Equal(ComponentPrim.U8, stream.Element!.Value.Prim);
        }

        [Fact]
        public void Stream_empty_element_decodes_to_null()
        {
            // 0x66 0x00  => stream  (no inner)
            var defs = TypeSectionReader.Decode(
                TypeSection(1, new byte[] { 0x66, 0x00 }));
            var stream = Assert.IsType<ComponentStreamType>(defs[0]);
            Assert.Null(stream.Element);
        }

        [Fact]
        public void Future_with_string_element_decodes()
        {
            // 0x65 0x01 0x73  => future<string>
            //   0x73 = signed-LEB for -0x0D = String
            var defs = TypeSectionReader.Decode(
                TypeSection(1, new byte[] { 0x65, 0x01, 0x73 }));
            var fut = Assert.IsType<ComponentFutureType>(defs[0]);
            Assert.NotNull(fut.Element);
            Assert.Equal(ComponentPrim.String, fut.Element!.Value.Prim);
        }

        [Fact]
        public void Future_empty_element_decodes_to_null()
        {
            var defs = TypeSectionReader.Decode(
                TypeSection(1, new byte[] { 0x65, 0x00 }));
            var fut = Assert.IsType<ComponentFutureType>(defs[0]);
            Assert.Null(fut.Element);
        }

        [Fact]
        public void ErrorContext_decodes_to_singleton()
        {
            // 0x64  => error-context (no payload)
            var defs = TypeSectionReader.Decode(
                TypeSection(1, new byte[] { 0x64 }));
            var ec = Assert.IsType<ComponentErrorContextType>(defs[0]);
            Assert.Same(ComponentErrorContextType.Instance, ec);
        }

        [Fact]
        public void Stream_referencing_typed_index_decodes()
        {
            // stream<typeidx=2> — non-primitive inner, encoded as a
            // non-negative signed-LEB.
            var defs = TypeSectionReader.Decode(
                TypeSection(1, new byte[] { 0x66, 0x01, 0x02 }));
            var stream = Assert.IsType<ComponentStreamType>(defs[0]);
            Assert.NotNull(stream.Element);
            Assert.False(stream.Element!.Value.IsPrimitive);
            Assert.Equal(2u, stream.Element!.Value.TypeIdx);
        }

        [Fact]
        public void Mixed_section_decodes_all_three_tags_in_one_pass()
        {
            // Sequential round-trip: stream<u8>, future, error-context,
            // future<typeidx 5>. Verifies the dispatch doesn't lose
            // its place between entries.
            var defs = TypeSectionReader.Decode(
                TypeSection(4,
                    new byte[] { 0x66, 0x01, 0x7d },   // stream<u8>
                    new byte[] { 0x65, 0x00 },         // future
                    new byte[] { 0x64 },               // error-context
                    new byte[] { 0x65, 0x01, 0x05 })); // future<#5>
            Assert.Equal(4, defs.Count);
            Assert.IsType<ComponentStreamType>(defs[0]);
            Assert.IsType<ComponentFutureType>(defs[1]);
            Assert.IsType<ComponentErrorContextType>(defs[2]);
            var fut = Assert.IsType<ComponentFutureType>(defs[3]);
            Assert.Equal(5u, fut.Element!.Value.TypeIdx);
        }
    }
}
