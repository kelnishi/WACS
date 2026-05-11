// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.Core.Instructions;

namespace Wacs.Core.Bin
{
    /// <summary>
    /// Populate <see cref="InstructionBase.ByteOffsetInFunc"/> for every
    /// instruction in a WAT-parsed module by running the binary writer
    /// over each function body through a counting stream and recording
    /// the pre-write byte position. Mirrors what
    /// <c>BinaryModuleParser</c> stamps onto each instruction during a
    /// real binary parse, so post-annotation a WAT-parsed module is
    /// indistinguishable from a binary-parsed one for stack-trace
    /// `@+0xXX` byte-offset reporting.
    /// </summary>
    public static class ByteOffsetAnnotator
    {
        /// <summary>
        /// Walk every defined function's body and stamp
        /// <see cref="InstructionBase.ByteOffsetInFunc"/> with the
        /// position the binary encoder would place that instruction
        /// at. Block-shaped instructions recurse into their inner
        /// bodies. Offsets are body-relative — they reset to zero at
        /// the start of each function, matching the binary parser's
        /// convention.
        /// </summary>
        public static void Annotate(Module module)
        {
            foreach (var fn in module.Funcs)
            {
                using var counter = new CountingStream();
                using var w = new BinaryWriter(counter);
                AnnotateSequence(w, fn.Body.Instructions, counter);
            }
        }

        private static void AnnotateSequence(
            BinaryWriter w, InstructionSequence seq, CountingStream counter)
        {
            for (int i = 0; i < seq.Count; i++)
            {
                var inst = seq[i];
                if (inst == null) continue;

                // Stamp the body-relative position BEFORE emitting
                // the instruction's bytes. That matches what
                // BinaryModuleParser captures via `instAbsPos - bodyStart`
                // (Module.cs:392).
                inst.ByteOffsetInFunc = (uint)counter.Position;

                // Emit opcode + immediates through the existing
                // BinaryFormat machinery. The CountingStream just
                // tallies bytes — no allocation per write.
                TypeWriters.WriteOpcode(w, inst.Op);
                inst.RenderBinary(w);

                // Recurse into block bodies (same shape as
                // TypeWriters.WriteInstruction).
                switch (inst)
                {
                    case InstBlock blk:
                        AnnotateSequence(w, blk.GetBlock(0).Instructions, counter);
                        break;
                    case InstLoop lp:
                        AnnotateSequence(w, lp.GetBlock(0).Instructions, counter);
                        break;
                    case InstIf ifi:
                        AnnotateSequence(w, ifi.GetBlock(0).Instructions, counter);
                        if (((IBlockInstruction)ifi).Count == 2)
                            AnnotateSequence(w, ifi.GetBlock(1).Instructions, counter);
                        break;
                    case InstTryTable tt:
                        AnnotateSequence(w, tt.GetBlock(0).Instructions, counter);
                        break;
                }
            }
        }

        /// <summary>
        /// <see cref="Stream"/> that swallows writes and only tracks
        /// the byte position. Used to compute encoded-instruction
        /// sizes without allocating per-instruction byte arrays.
        /// </summary>
        private sealed class CountingStream : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => Position;
            public override long Position { get; set; }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new System.NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) =>
                throw new System.NotSupportedException();
            public override void SetLength(long value) =>
                throw new System.NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                Position += count;
            }

            public override void WriteByte(byte value)
            {
                Position++;
            }
        }
    }
}
