// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using Wacs.Core;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Xunit;

namespace Wacs.Compilation.Test
{
    /// <summary>
    /// Phase 1 of the Branch Hinting work: verify that the binary
    /// parser captures the <c>metadata.code.branch_hint</c> custom
    /// section verbatim into <see cref="Module.BranchHints"/>, and
    /// that every parsed instruction in a function body carries the
    /// byte offset that the hint section keys against.
    /// </summary>
    public class BranchHintParsingTests
    {
        // (module
        //   (type (func (param i32 i32) (result i32)))
        //   (func (export "f") (param i32 i32) (result i32)
        //     local.get 0
        //     (@metadata.code.branch_hint "\01") if (result i32)
        //       local.get 1
        //     else
        //       i32.const 0
        //     end))
        //
        // Function body bytes. The branch-hinting proposal indexes
        // into the body AFTER the locals declaration, so offsets
        // shown are relative to the byte immediately after the
        // locals-count byte (which is 0x00 here for "no locals").
        //   00: 0x20 0x00         local.get 0
        //   02: 0x04 0x7F         if (result i32)
        //   04: 0x20 0x01         local.get 1
        //   06: 0x05              else
        //   07: 0x41 0x00         i32.const 0
        //   09: 0x0B              end
        //   0A: 0x0B              expression-end
        //
        // The `if` lands at body-relative byte 0x02 — that is the
        // hint's lookup key.
        private static readonly byte[] HintedIfModule =
        {
            // Magic + version
            0x00, 0x61, 0x73, 0x6D,  0x01, 0x00, 0x00, 0x00,
            // Type: (i32 i32) -> i32
            0x01, 0x07, 0x01,  0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            // Function: 1 func, type 0
            0x03, 0x02, 0x01, 0x00,
            // Export "f" -> func 0
            0x07, 0x05, 0x01,  0x01, 0x66, 0x00, 0x00,
            // Code section: 1 body, size 12 bytes
            0x0A, 0x0E, 0x01,
                0x0C,                            // body size
                0x00,                            // 0 locals
                0x20, 0x00,                      // local.get 0          (offset 0)
                0x04, 0x7F,                      // if (result i32)      (offset 2)
                0x20, 0x01,                      // local.get 1          (offset 4)
                0x05,                            // else                 (offset 6)
                0x41, 0x00,                      // i32.const 0          (offset 7)
                0x0B,                            // end                  (offset 9)
                0x0B,                            // expression end       (offset 10)
            // Custom section: "metadata.code.branch_hint"
            //   1 func entry: funcidx=0, 1 hint at offset=3, payload="\x01"
            // Payload size: name-len(1) + name(25) + funcs-vec(6) = 32
            0x00,                                // section id 0 (custom)
            0x20,                                // section size = 32
                // name length = 25, name = "metadata.code.branch_hint"
                0x19,
                0x6D, 0x65, 0x74, 0x61, 0x64, 0x61, 0x74, 0x61, 0x2E,
                0x63, 0x6F, 0x64, 0x65, 0x2E, 0x62, 0x72, 0x61, 0x6E,
                0x63, 0x68, 0x5F, 0x68, 0x69, 0x6E, 0x74,
                // payload: 1 func entry
                0x01,                            // 1 function entry
                0x00,                            // funcidx 0
                0x01,                            // 1 hint
                0x02,                            // byte_offset = 2 (= `if`)
                0x01,                            // hint payload length = 1
                0x01,                            // hint byte = 0x01 (likely)
        };

        [Fact]
        public void ParsesBranchHintCustomSection()
        {
            var module = ParseModule(HintedIfModule);

            Assert.NotNull(module.BranchHints);
            Assert.True(module.BranchHints!.ByFuncIndex.ContainsKey(0));

            var fnMap = module.BranchHints.ByFuncIndex[0];
            Assert.Single(fnMap);
            Assert.True(fnMap.ContainsKey(2));

            var hint = fnMap[2];
            Assert.Equal(2u, hint.ByteOffset);
            Assert.Equal(1, hint.RawData.Length);
            Assert.Equal((byte)0x01, hint.HintByte);
            Assert.True(hint.IsLikely);
        }

        [Fact]
        public void TryGetReturnsHintForKnownInstruction()
        {
            var module = ParseModule(HintedIfModule);
            var hit = module.BranchHints!.TryGet(0, 2);
            Assert.NotNull(hit);
            Assert.True(hit!.Value.IsLikely);

            // Negative lookup paths
            Assert.Null(module.BranchHints.TryGet(0, 99));
            Assert.Null(module.BranchHints.TryGet(99, 2));
        }

        [Fact]
        public void InstructionsCarryFuncRelativeByteOffset()
        {
            var module = ParseModule(HintedIfModule);

            // Walk the top-level instructions of the only function.
            // Per the byte map in the test fixture comment:
            //   local.get 0  -> offset 0
            //   if           -> offset 2
            //   end          -> offset 10
            // Byte offsets are now computed on demand via
            // ByteOffsetWalker rather than stored on the instance.
            var body = module.Funcs[0].Body.Instructions;
            Assert.Equal((uint?)0, Wacs.Core.Bin.ByteOffsetWalker.Find(body, body[0]));
            Assert.Equal((uint?)2, Wacs.Core.Bin.ByteOffsetWalker.Find(body, body[1]));

            // The hint's offset (2) matches the `if` instruction's
            // offset and resolves through the instance-keyed lookup.
            Assert.NotNull(module.BranchHints!.TryGet(body[1]));
            Assert.True(module.BranchHints!.TryGet(body[1])!.Value.IsLikely);
        }

        // Module byte layout (cumulative offsets from start of blob):
        //    0..7   magic + version             (8 bytes)
        //    8..16  type section                (9 bytes: id+size+payload)
        //   17..20  function section            (4 bytes)
        //   21..27  export section              (7 bytes)
        //   28..43  code section                (16 bytes: id+size+14 payload)
        //   44      custom section id (0x00)
        //   45      custom section size (0x20 = 32)
        //   46      name-length byte (0x19 = 25)
        //   47..71  name "metadata.code.branch_hint"
        //   72      func count (0x01)
        //   73      funcidx (0x00)              ← mutation target
        //   74      hint count (0x01)
        //   75      byte_offset (0x02)
        //   76      hint data length (0x01)
        //   77      hint data byte (0x01)
        private const int CustomSectionStart = 44;
        private const int FuncIdxByteIndex = 73;

        [Fact]
        public void NoBranchHintsWhenSectionAbsent()
        {
            // Truncate the blob right before the custom section.
            var withoutHints = HintedIfModule.AsSpan(0, CustomSectionStart).ToArray();
            var module = ParseModule(withoutHints);
            Assert.Null(module.BranchHints);

            // Byte offsets are still resolvable via ByteOffsetWalker
            // regardless of whether a hint section was present.
            var body = module.Funcs[0].Body.Instructions;
            Assert.Equal((uint?)0, Wacs.Core.Bin.ByteOffsetWalker.Find(body, body[0]));
            Assert.Equal((uint?)2, Wacs.Core.Bin.ByteOffsetWalker.Find(body, body[1]));
        }

        [Fact]
        public void RejectsBranchHintWithFuncIdxOutOfRange()
        {
            var bad = (byte[])HintedIfModule.Clone();
            Assert.Equal((byte)0x00, bad[FuncIdxByteIndex]); // sanity
            bad[FuncIdxByteIndex] = 0x07; // module only has 1 function
            Assert.Throws<FormatException>(() => ParseModule(bad));
        }

        // -----------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------

        private static Module ParseModule(byte[] bytes)
        {
            // Branch-hint parsing is opt-in — interpreter consumers
            // skip the section. Tests in this file always exercise
            // the hint path, so flip the static flag once here.
            BinaryModuleParser.ParseBranchHints = true;
            using var ms = new MemoryStream(bytes);
            return BinaryModuleParser.ParseWasm(ms);
        }
    }
}
