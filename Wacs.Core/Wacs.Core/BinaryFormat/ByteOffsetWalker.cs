// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using Wacs.Core.Instructions;

namespace Wacs.Core.Bin
{
    /// <summary>
    /// Walk a function body in source order with a synchronized byte
    /// counter, invoking a visitor at each instruction with its body-
    /// relative byte offset. Used by:
    /// <list type="bullet">
    ///   <item><see cref="Wacs.Core.Runtime.Exceptions.WasmStackTrace"/>:
    ///     find the byte offset of a trap's <c>InstructionBase</c>
    ///     reference on demand, without storing per-occurrence offsets
    ///     on the instance.</item>
    ///   <item><c>BranchHintSection.JoinByInstruction</c>: rebuild the
    ///     instruction-keyed hint map from offset-keyed parse data.</item>
    /// </list>
    /// The counter is driven through the existing
    /// <see cref="BinaryModuleWriter"/> emit machinery via a
    /// <see cref="CountingStream"/>, so byte positions match what the
    /// binary parser would observe at decode time without allocating
    /// per-instruction byte arrays.
    /// </summary>
    public static class ByteOffsetWalker
    {
        /// <summary>
        /// Visitor for each instruction encountered. Return
        /// <c>true</c> to stop the walk (find-first); <c>false</c> to
        /// continue.
        /// </summary>
        public delegate bool Visitor(InstructionBase inst, uint byteOffset);

        /// <summary>
        /// Walk <paramref name="body"/> recursively, invoking
        /// <paramref name="visit"/> at every instruction (including
        /// those inside nested blocks). Returns <c>true</c> if the
        /// visitor short-circuited the walk.
        /// </summary>
        public static bool Walk(InstructionSequence body, Visitor visit)
        {
            using var counter = new CountingStream();
            using var w = new BinaryWriter(counter);
            return WalkSeq(w, body, counter, visit);
        }

        /// <summary>
        /// Find the body-relative byte offset of the first
        /// reference-equal occurrence of <paramref name="target"/> in
        /// <paramref name="body"/>. Returns <c>null</c> if the target
        /// isn't found.
        /// <para>For interned instruction singletons
        /// (<c>i32.const N</c>, <c>local.get N</c>, <c>drop</c>,
        /// <c>nop</c>, …) the first-match semantics mean the returned
        /// offset is the offset of the first occurrence, which may not
        /// be the executing one. Per-occurrence resolution would need
        /// a body-position handle in <see cref="Wacs.Core.Runtime.WasmStackFrame"/>;
        /// kept simple for now.</para>
        /// </summary>
        public static uint? Find(InstructionSequence body, InstructionBase target)
        {
            uint? found = null;
            Walk(body, (inst, off) =>
            {
                if (ReferenceEquals(inst, target))
                {
                    found = off;
                    return true;
                }
                return false;
            });
            return found;
        }

        private static bool WalkSeq(
            BinaryWriter w, InstructionSequence seq,
            CountingStream counter, Visitor visit)
        {
            for (int i = 0; i < seq.Count; i++)
            {
                var inst = seq[i];
                if (inst == null) continue;

                if (visit(inst, (uint)counter.Position)) return true;

                // Advance the counter past this instruction's
                // encoded bytes — opcode + immediates — so subsequent
                // visits see the correct body-relative position.
                TypeWriters.WriteOpcode(w, inst.Op);
                inst.RenderBinary(w);

                switch (inst)
                {
                    case InstBlock blk:
                        if (WalkSeq(w, blk.GetBlock(0).Instructions, counter, visit))
                            return true;
                        break;
                    case InstLoop lp:
                        if (WalkSeq(w, lp.GetBlock(0).Instructions, counter, visit))
                            return true;
                        break;
                    case InstIf ifi:
                        if (WalkSeq(w, ifi.GetBlock(0).Instructions, counter, visit))
                            return true;
                        if (((IBlockInstruction)ifi).Count == 2
                            && WalkSeq(w, ifi.GetBlock(1).Instructions, counter, visit))
                            return true;
                        break;
                    case InstTryTable tt:
                        if (WalkSeq(w, tt.GetBlock(0).Instructions, counter, visit))
                            return true;
                        break;
                }
            }
            return false;
        }

        /// <summary>
        /// <see cref="Stream"/> that swallows writes and only tracks
        /// the byte position. Used to compute encoded-instruction
        /// sizes without allocating per-instruction byte arrays.
        /// </summary>
        internal sealed class CountingStream : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => Position;
            public override long Position { get; set; }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) =>
                throw new NotSupportedException();
            public override void SetLength(long value) =>
                throw new NotSupportedException();

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
