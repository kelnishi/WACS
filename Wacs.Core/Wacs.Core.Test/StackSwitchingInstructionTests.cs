// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.IO;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Instruction-level round-trip + factory dispatch tests for the
    /// Stack Switching proposal opcodes (0xE0–0xE5). Covers binary
    /// parse / render symmetry; runtime execution is covered by the
    /// dispatcher's own tests.
    /// </summary>
    public class StackSwitchingInstructionTests
    {
        private static byte[] Render(InstructionBase inst)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            inst.RenderBinary(w);
            w.Flush();
            return ms.ToArray();
        }

        private static T ParseBytes<T>(T inst, byte[] bytes) where T : InstructionBase
        {
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            return (T)inst.Parse(r);
        }

        [Theory]
        [InlineData(OpCode.ContNew, typeof(InstContNew))]
        [InlineData(OpCode.ContBind, typeof(InstContBind))]
        [InlineData(OpCode.Suspend, typeof(InstSuspend))]
        [InlineData(OpCode.Resume, typeof(InstResume))]
        [InlineData(OpCode.ResumeThrow, typeof(InstResumeThrow))]
        [InlineData(OpCode.Switch, typeof(InstSwitch))]
        public void SpecFactory_dispatches_to_correct_instruction(OpCode op, System.Type expected)
        {
            var inst = SpecFactory.Factory.CreateInstruction(new ByteCode(op));
            Assert.NotNull(inst);
            Assert.IsType(expected, inst);
        }

        [Fact]
        public void ContNew_round_trips_typeidx()
        {
            var original = new InstContNew();
            ParseBytes(original, new byte[] { 0x07 });
            var bytes = Render(original);
            Assert.Equal(new byte[] { 0x07 }, bytes);
            var reparsed = ParseBytes(new InstContNew(), bytes);
            Assert.Equal(7, reparsed.TypeIndex);
        }

        [Fact]
        public void ContBind_round_trips_two_typeidx()
        {
            var original = new InstContBind();
            ParseBytes(original, new byte[] { 0x03, 0x05 });
            var bytes = Render(original);
            Assert.Equal(new byte[] { 0x03, 0x05 }, bytes);
            var reparsed = ParseBytes(new InstContBind(), bytes);
            Assert.Equal(3, reparsed.TypeIndex1);
            Assert.Equal(5, reparsed.TypeIndex2);
        }

        [Fact]
        public void Suspend_round_trips_tagidx()
        {
            var original = new InstSuspend();
            ParseBytes(original, new byte[] { 0x02 });
            var bytes = Render(original);
            Assert.Equal(new byte[] { 0x02 }, bytes);
            var reparsed = ParseBytes(new InstSuspend(), bytes);
            Assert.Equal(2, reparsed.TagIndex);
        }

        [Fact]
        public void Resume_round_trips_typeidx_and_no_handlers()
        {
            // typeidx=4, handler count=0
            var bytes = new byte[] { 0x04, 0x00 };
            var inst = ParseBytes(new InstResume(), bytes);
            Assert.Equal(4, inst.TypeIndex);
            Assert.Empty(inst.Handlers);
            Assert.Equal(bytes, Render(inst));
        }

        [Fact]
        public void Resume_round_trips_typeidx_and_handlers()
        {
            // typeidx=4, 2 handlers:
            //   [0x00 tag=1 label=2]  (on-tag)
            //   [0x01 tag=3 label=5]  (on-tag-switch)
            var bytes = new byte[]
            {
                0x04,           // typeidx
                0x02,           // 2 handlers
                0x00, 0x01, 0x02,
                0x01, 0x03, 0x05,
            };
            var inst = ParseBytes(new InstResume(), bytes);
            Assert.Equal(4, inst.TypeIndex);
            Assert.Equal(2, inst.Handlers.Length);
            Assert.False(inst.Handlers[0].OnSwitch);
            Assert.Equal(1u, inst.Handlers[0].Tag.Value);
            Assert.Equal(2u, inst.Handlers[0].Label.Value);
            Assert.True(inst.Handlers[1].OnSwitch);
            Assert.Equal(3u, inst.Handlers[1].Tag.Value);
            Assert.Equal(5u, inst.Handlers[1].Label.Value);
            Assert.Equal(bytes, Render(inst));
        }

        [Fact]
        public void Resume_rejects_unknown_handler_flag()
        {
            // typeidx=4, 1 handler with bogus flag 0x42
            var bytes = new byte[] { 0x04, 0x01, 0x42, 0x00, 0x00 };
            var inst = new InstResume();
            Assert.Throws<System.FormatException>(() => ParseBytes(inst, bytes));
        }

        [Fact]
        public void ResumeThrow_round_trips_typeidx_tagidx_and_handlers()
        {
            // typeidx=4, tagidx=7, 1 handler: 0x00 tag=2 label=3
            var bytes = new byte[]
            {
                0x04,           // typeidx
                0x07,           // tagidx
                0x01,           // 1 handler
                0x00, 0x02, 0x03,
            };
            var inst = ParseBytes(new InstResumeThrow(), bytes);
            Assert.Equal(4, inst.TypeIndex);
            Assert.Equal(7, inst.TagIndex);
            Assert.Single(inst.Handlers);
            Assert.False(inst.Handlers[0].OnSwitch);
            Assert.Equal(2u, inst.Handlers[0].Tag.Value);
            Assert.Equal(3u, inst.Handlers[0].Label.Value);
            Assert.Equal(bytes, Render(inst));
        }

        [Fact]
        public void Switch_round_trips_typeidx_and_tagidx()
        {
            var bytes = new byte[] { 0x09, 0x02 };
            var inst = ParseBytes(new InstSwitch(), bytes);
            Assert.Equal(9, inst.TypeIndex);
            Assert.Equal(2, inst.TagIndex);
            Assert.Equal(bytes, Render(inst));
        }
    }
}
