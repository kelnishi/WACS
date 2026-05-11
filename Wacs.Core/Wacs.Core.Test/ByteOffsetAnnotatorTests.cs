// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.IO;
using System.Text;
using Wacs.Core.Instructions;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Pass F: WAT parser stamps <see cref="InstructionBase.ByteOffsetInFunc"/>
    /// to match what the binary parser records natively, so stack-trace
    /// formatting surfaces <c>@+0xXX</c> coordinates regardless of input form.
    /// </summary>
    public class ByteOffsetAnnotatorTests
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

        private static void Walk(InstructionSequence seq, List<uint> sink)
        {
            for (int i = 0; i < seq.Count; i++)
            {
                var inst = seq[i];
                sink.Add(inst.ByteOffsetInFunc);
                switch (inst)
                {
                    case InstBlock blk:
                        Walk(blk.GetBlock(0).Instructions, sink);
                        break;
                    case InstLoop lp:
                        Walk(lp.GetBlock(0).Instructions, sink);
                        break;
                    case InstIf ifi:
                        Walk(ifi.GetBlock(0).Instructions, sink);
                        if (((IBlockInstruction)ifi).Count == 2)
                            Walk(ifi.GetBlock(1).Instructions, sink);
                        break;
                    case InstTryTable tt:
                        Walk(tt.GetBlock(0).Instructions, sink);
                        break;
                }
            }
        }

        [Fact]
        public void FlatBody_FirstInstructionAtZero_RestNonZero()
        {
            const string wat = @"(module
              (func (result i32)
                i32.const 1
                i32.const 2
                i32.add))";
            var module = ParseWat(wat);
            var offsets = new List<uint>();
            Walk(module.Funcs[0].Body.Instructions, offsets);

            // First instruction must sit at offset 0.
            Assert.Equal(0u, offsets[0]);

            // Every subsequent offset must be strictly greater than the
            // last (we walk in source order, each instruction is at
            // least 1 byte).
            for (int i = 1; i < offsets.Count; i++)
                Assert.True(offsets[i] > offsets[i - 1],
                    $"offset[{i}]={offsets[i]} not > offset[{i - 1}]={offsets[i - 1]}");
        }

        [Fact]
        public void WatAndBinaryOffsets_AgreePerInstruction()
        {
            const string wat = @"(module
              (func (result i32)
                i32.const 10
                i32.const 20
                i32.add
                i32.const 3
                i32.mul))";

            var watModule = ParseWat(wat);
            var binModule = RoundTripBinary(wat);

            var watOff = new List<uint>();
            var binOff = new List<uint>();
            Walk(watModule.Funcs[0].Body.Instructions, watOff);
            Walk(binModule.Funcs[0].Body.Instructions, binOff);

            Assert.Equal(binOff.Count, watOff.Count);
            for (int i = 0; i < binOff.Count; i++)
                Assert.Equal(binOff[i], watOff[i]);
        }

        [Fact]
        public void NestedBlocks_OffsetsIncreaseThroughChildren()
        {
            const string wat = @"(module
              (func (result i32)
                (block (result i32)
                  i32.const 1
                  (loop
                    nop
                    nop)
                  i32.const 2
                  i32.add)))";

            var watModule = ParseWat(wat);
            var binModule = RoundTripBinary(wat);

            var watOff = new List<uint>();
            var binOff = new List<uint>();
            Walk(watModule.Funcs[0].Body.Instructions, watOff);
            Walk(binModule.Funcs[0].Body.Instructions, binOff);

            // Same shape — every nested instruction's offset matches
            // the binary parser's stamp.
            Assert.Equal(binOff.Count, watOff.Count);
            for (int i = 0; i < binOff.Count; i++)
                Assert.Equal(binOff[i], watOff[i]);
        }

        [Fact]
        public void IfElse_BothBranches_GetOffsets()
        {
            const string wat = @"(module
              (func (param i32) (result i32)
                local.get 0
                (if (result i32)
                  (then i32.const 1)
                  (else i32.const 2))))";

            var watModule = ParseWat(wat);
            var binModule = RoundTripBinary(wat);

            var watOff = new List<uint>();
            var binOff = new List<uint>();
            Walk(watModule.Funcs[0].Body.Instructions, watOff);
            Walk(binModule.Funcs[0].Body.Instructions, binOff);

            Assert.Equal(binOff.Count, watOff.Count);
            for (int i = 0; i < binOff.Count; i++)
                Assert.Equal(binOff[i], watOff[i]);
        }
    }
}
