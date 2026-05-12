// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Binary-parsed modules resolve to source coordinates via
    /// <see cref="TextModuleWriter.WriteWithLineMap"/>. The writer
    /// records a per-instruction span keyed by
    /// <see cref="ModuleElementKind.Instruction"/> with
    /// <see cref="ModuleElementRef.Index"/> = funcIdx and
    /// <see cref="ModuleElementRef.InstructionIndex"/> = body-relative
    /// byte offset (resolved via
    /// <see cref="Wacs.Core.Bin.ByteOffsetWalker"/>), so
    /// <see cref="Runtime.Exceptions.WasmStackTrace"/> can resolve
    /// binary frames the same way it resolves WAT-parsed ones.
    /// </summary>
    public class BinarySourceLineBridgeTests
    {
        private static Module ParseWat(string wat)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wat));
            return TextModuleParser.ParseWat(ms);
        }

        private static Module RoundTripBinary(string wat)
        {
            var watModule = ParseWat(wat);
            var bytes = Wacs.Core.Bin.BinaryModuleWriter.Write(watModule);
            using var ms = new MemoryStream(bytes);
            return BinaryModuleParser.ParseWasm(ms);
        }

        /// <summary>Count the recorded instruction-scoped spans in a
        /// LineMap. Filters out the function/type/etc. section keys.</summary>
        private static List<KeyValuePair<ModuleElementRef, LineMap.Span>>
            InstructionEntries(LineMap lineMap, int funcIdx) => lineMap.All
                .Where(kv => kv.Key.Kind == ModuleElementKind.Instruction
                             && kv.Key.Index == funcIdx)
                .ToList();

        [Fact]
        public void LineMap_RecordsPerInstructionSpans_ForBinaryParsed()
        {
            const string wat = @"(module
              (func
                i32.const 1
                drop))";

            var binModule = RoundTripBinary(wat);
            // SourcePositions is the WAT-only side-table — binary
            // parse leaves it null.
            Assert.Null(binModule.SourcePositions);

            var (_, lineMap) = TextModuleWriter.WriteWithLineMap(binModule);
            int funcIdx = (int)binModule.Funcs[0].Index.Value;

            // At minimum the writer must have stamped:
            //   i32.const, drop. (Trailing InstEnd is trimmed in WAT.)
            Assert.True(InstructionEntries(lineMap, funcIdx).Count >= 2);
        }

        [Fact]
        public void LineMap_SpansHaveValidLines_PerInstruction()
        {
            const string wat = @"(module
              (func
                i32.const 10
                i32.const 20
                i32.add
                drop))";

            var binModule = RoundTripBinary(wat);
            var (_, lineMap) = TextModuleWriter.WriteWithLineMap(binModule);
            int funcIdx = (int)binModule.Funcs[0].Index.Value;

            var entries = InstructionEntries(lineMap, funcIdx);
            Assert.True(entries.Count >= 1);
            foreach (var kv in entries)
                Assert.True(kv.Value.StartLine >= 1
                    && kv.Value.EndLine >= kv.Value.StartLine);
        }

        [Fact]
        public void LineMap_NestedBlock_RecordsAtLeastOneSpan()
        {
            const string wat = @"(module
              (func
                (block
                  i32.const 1
                  drop)))";

            var binModule = RoundTripBinary(wat);
            var (_, lineMap) = TextModuleWriter.WriteWithLineMap(binModule);
            int funcIdx = (int)binModule.Funcs[0].Index.Value;

            Assert.True(InstructionEntries(lineMap, funcIdx).Count >= 1);
        }

        [Fact]
        public void WatParsed_LineMapStillRecordsSpans()
        {
            // A WAT-parsed module has Module.SourcePositions
            // populated, so the WasmStackTrace formatter prefers
            // that table. WriteWithLineMap still records spans
            // regardless — this test verifies the LineMap mechanism
            // is path-independent.
            const string wat = @"(module
              (func (result i32)
                i32.const 7))";
            var watModule = ParseWat(wat);
            Assert.NotNull(watModule.SourcePositions);

            var (_, lineMap) = TextModuleWriter.WriteWithLineMap(watModule);
            int funcIdx = (int)watModule.Funcs[0].Index.Value;

            // i32.const 7 — one instruction recorded.
            Assert.True(InstructionEntries(lineMap, funcIdx).Count >= 1);
        }
    }
}
