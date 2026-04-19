// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// Peephole super-instruction rewriter for the annotated bytecode stream.
    ///
    /// <para>Runs as an optional post-pass inside <see cref="BytecodeCompiler.Compile"/>.
    /// Walks the already-emitted stream, matches a small set of common wasm op
    /// sequences, and rewrites each match into a single <see cref="WacsCode"/>-prefixed
    /// super-op. Produces a shrunken stream and a remapped <see cref="HandlerEntry"/>
    /// array whose <c>StartPc/EndPc/HandlerPc</c> now point at post-fusion offsets.</para>
    ///
    /// <para>Correctness invariants:
    /// <list type="bullet">
    ///   <item>No branch landing pc is absorbed into the middle of a super-op. Before
    ///         matching, we walk the stream once to collect every branch target
    ///         (Br/BrIf/BrOnNull/BrOnNonNull triples, BrTable triples incl. default,
    ///         If's elsePc, Else's endPc, HandlerTable's three pc fields). Any pattern
    ///         that would swallow a target as an internal instruction is rejected.</item>
    ///   <item>Every branch-target immediate in the emitted stream is re-encoded
    ///         through <c>oldToNew[]</c>, so jumps land at the post-fusion offset
    ///         of whatever op the target originally pointed at.</item>
    ///   <item>No semantics change — the super-op handlers produce the same OpStack
    ///         side-effects as the sequence they replace. Disabling the fuser
    ///         (<c>UseSwitchSuperInstructions = false</c>) leaves the stream
    ///         byte-identical to an unfused build.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class StreamFusePass
    {
        /// <summary>Apply the fusion pass. Returns a rewritten <see cref="CompiledFunction"/>;
        /// the input is not mutated.</summary>
        /// <param name="fn">The just-compiled function.</param>
        /// <param name="linkedStreamOffsets">The <c>streamOffset[]</c> array
        /// <see cref="BytecodeCompiler"/> built in pass 1 — one entry per linked
        /// <c>InstructionBase</c>, giving its start offset in the emitted stream plus
        /// one end-of-stream entry. Elided block-structure instructions repeat the
        /// previous offset; we derive op-start positions by taking successive
        /// offsets where the value advances.</param>
        public static CompiledFunction Apply(CompiledFunction fn, int[] linkedStreamOffsets)
        {
            byte[] oldCode = fn.Bytecode;
            int oldLen = oldCode.Length;

            // Phase 1: derive the list of op-start offsets from streamOffset. Distinct
            // strictly-increasing values only — repeated offsets mean the corresponding
            // linked instruction was elided (Block/Loop/End/TryTable).
            var opStarts = new List<int>(linkedStreamOffsets.Length);
            var opSize = new Dictionary<int, int>(linkedStreamOffsets.Length);
            int prev = -1;
            for (int i = 0; i < linkedStreamOffsets.Length - 1; i++)
            {
                int here = linkedStreamOffsets[i];
                int next = linkedStreamOffsets[i + 1];
                if (here == prev) continue;       // elided instruction
                if (next == here) continue;       // this one is the elided entry
                opStarts.Add(here);
                opSize[here] = next - here;
                prev = here;
            }
            int opCount = opStarts.Count;

            // Phase 2: collect every pc that is a branch target. Every branch
            // immediate and every handler table entry in the INPUT stream names a
            // target pc. Anything absorbed by a fused super-op must not appear here.
            var targets = CollectTargets(oldCode, opStarts, opSize, fn.HandlerTable);

            // Phase 3: greedy pattern match across the op sequence. For each op we
            // try the longest applicable fusion pattern; if the window doesn't
            // absorb a branch target (other than its own head, which is allowed),
            // record a fusion at that position.
            var fuseAt = new Dictionary<int, FuseDecision>();
            for (int i = 0; i < opCount; )
            {
                if (TryMatchPattern(oldCode, opStarts, opSize, targets, i, out var decision))
                {
                    fuseAt[opStarts[i]] = decision;
                    i += decision.ConsumedOps;
                }
                else
                {
                    i += 1;
                }
            }

            // Phase 4: sizing — walk each op, decide its new offset in the output.
            // oldToNew[i] is defined at each op-start offset. oldToNew[oldLen] is
            // the end of the new stream.
            var oldToNew = new Dictionary<int, int>(opCount + 1);
            int newLen = 0;
            int p = 0;
            while (p < oldLen)
            {
                oldToNew[p] = newLen;
                if (fuseAt.TryGetValue(p, out var fd))
                {
                    newLen += fd.EncodedSize;
                    p += fd.ConsumedBytes;
                }
                else
                {
                    newLen += opSize[p];
                    p += opSize[p];
                }
            }
            oldToNew[oldLen] = newLen;

            // Phase 5: emit the new stream. For each op-start in the original
            // stream: if a fusion starts here, write the fused encoding (with
            // operands copied from the absorbed ops). Otherwise copy the op over
            // (remapping branch-target immediates through oldToNew).
            var newCode = new byte[newLen];
            int writePos = 0;
            p = 0;
            while (p < oldLen)
            {
                if (fuseAt.TryGetValue(p, out var fd))
                {
                    writePos = WriteFused(oldCode, p, fd, newCode, writePos);
                    p += fd.ConsumedBytes;
                }
                else
                {
                    writePos = CopyOp(oldCode, p, opSize[p], oldToNew, newCode, writePos);
                    p += opSize[p];
                }
            }

            // Phase 6: remap the handler table to post-fusion offsets.
            HandlerEntry[] newHandlers = fn.HandlerTable;
            if (newHandlers.Length > 0)
            {
                newHandlers = new HandlerEntry[fn.HandlerTable.Length];
                for (int i = 0; i < fn.HandlerTable.Length; i++)
                {
                    var h = fn.HandlerTable[i];
                    newHandlers[i] = new HandlerEntry(
                        startPc:       RemapTarget(h.StartPc, oldToNew),
                        endPc:         RemapTarget(h.EndPc, oldToNew),
                        tagIdx:        h.TagIdx,
                        handlerPc:     RemapTarget(h.HandlerPc, oldToNew),
                        resultsHeight: h.ResultsHeight,
                        arity:         h.Arity,
                        kind:          h.Kind);
                }
            }

            return new CompiledFunction(newCode, fn.LocalsCount, fn.Signature, newHandlers);
        }

        // ------------------------------------------------------------------------
        // Target-pc collector. One walk over the stream recording every pc that a
        // branch immediate or handler table entry names as a jump target.
        // ------------------------------------------------------------------------
        private static HashSet<int> CollectTargets(
            byte[] code, List<int> opStarts, Dictionary<int, int> opSize,
            HandlerEntry[] handlers)
        {
            var set = new HashSet<int>();
            // int.MaxValue (0x7FFFFFFF) is BytecodeCompiler's sentinel for "past end of
            // function" — used by Return and by branches that target
            // InstExpressionProxy. It's not a real pc, skip it. Keeping it out of
            // `targets` also means a fusion can legitimately end at the last op, since
            // no actual target lands there.
            void AddTarget(uint t)
            {
                if (t != EndOfFunctionSentinel) set.Add((int)t);
            }

            foreach (int pc in opStarts)
            {
                byte b = code[pc];
                if (b >= 0xFB && b <= 0xFF) continue;   // prefix ops have no targets
                OpCode op = (OpCode)b;
                int immPc = pc + 1;
                switch (op)
                {
                    case OpCode.Br:
                    case OpCode.BrIf:
                    case OpCode.BrOnNull:
                    case OpCode.BrOnNonNull:
                        AddTarget(BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(immPc)));
                        break;
                    case OpCode.If:
                    case OpCode.Else:
                        AddTarget(BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(immPc)));
                        break;
                    case OpCode.BrTable:
                    {
                        uint count = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(immPc));
                        int triplePc = immPc + 4;
                        for (int i = 0; i <= count; i++)
                        {
                            AddTarget(BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(triplePc)));
                            triplePc += 12;
                        }
                        break;
                    }
                }
            }
            foreach (var h in handlers)
            {
                AddTarget(h.StartPc);
                AddTarget(h.EndPc);
                AddTarget(h.HandlerPc);
            }
            return set;
        }

        // ------------------------------------------------------------------------
        // Pattern matcher. Greedy: tries the longest pattern first. Returns the
        // match's consumed op count, consumed byte count, encoded super-op size,
        // and enough state to recreate the fused encoding from the source bytes.
        // ------------------------------------------------------------------------
        private readonly struct FuseDecision
        {
            public readonly WacsCode SuperOp;
            public readonly int ConsumedOps;
            public readonly int ConsumedBytes;
            public readonly int EncodedSize;
            public FuseDecision(WacsCode superOp, int ops, int bytes, int encoded)
            {
                SuperOp = superOp;
                ConsumedOps = ops;
                ConsumedBytes = bytes;
                EncodedSize = encoded;
            }
        }

        private static bool TryMatchPattern(
            byte[] code, List<int> opStarts, Dictionary<int, int> opSize,
            HashSet<int> targets, int i, out FuseDecision decision)
        {
            decision = default;
            int opCount = opStarts.Count;

            // 3-op patterns first (longest fuse wins).
            if (i + 2 < opCount)
            {
                int p0 = opStarts[i], p1 = opStarts[i + 1], p2 = opStarts[i + 2];
                // Internal ops (p1, p2) must not be branch targets — a jump into
                // the middle would land inside the super-op's encoded immediate.
                if (!targets.Contains(p1) && !targets.Contains(p2))
                {
                    byte op0 = code[p0], op1 = code[p1], op2 = code[p2];
                    if (op0 == (byte)OpCode.LocalGet)
                    {
                        // local.get $idx ; i32.const $k ; i32.<op>
                        if (op1 == (byte)OpCode.I32Const)
                        {
                            WacsCode? fused = op2 switch
                            {
                                (byte)OpCode.I32Add => WacsCode.I32FusedAdd,
                                (byte)OpCode.I32Sub => WacsCode.I32FusedSub,
                                (byte)OpCode.I32Mul => WacsCode.I32FusedMul,
                                (byte)OpCode.I32And => WacsCode.I32FusedAnd,
                                _ => null,
                            };
                            if (fused is WacsCode fs)
                            {
                                decision = new FuseDecision(fs, 3,
                                    opSize[p0] + opSize[p1] + opSize[p2], 10);
                                return true;
                            }
                        }
                        // local.get $idx ; i64.const $k ; i64.add (symmetric i64 set)
                        if (op1 == (byte)OpCode.I64Const && op2 == (byte)OpCode.I64Add)
                        {
                            decision = new FuseDecision(WacsCode.I64FusedAdd, 3,
                                opSize[p0] + opSize[p1] + opSize[p2], 14);
                            return true;
                        }
                    }
                }
            }

            // 2-op patterns.
            if (i + 1 < opCount)
            {
                int p0 = opStarts[i], p1 = opStarts[i + 1];
                if (!targets.Contains(p1))
                {
                    byte op0 = code[p0], op1 = code[p1];
                    // local.get ; local.set  →  LocalGetSet
                    if (op0 == (byte)OpCode.LocalGet && op1 == (byte)OpCode.LocalSet)
                    {
                        decision = new FuseDecision(WacsCode.LocalGetSet, 2,
                            opSize[p0] + opSize[p1], 10);
                        return true;
                    }
                    // i32.const ; local.set  →  LocalConstSet (i32)
                    if (op0 == (byte)OpCode.I32Const && op1 == (byte)OpCode.LocalSet)
                    {
                        decision = new FuseDecision(WacsCode.LocalConstSet, 2,
                            opSize[p0] + opSize[p1], 10);
                        return true;
                    }
                    // i64.const ; local.set  →  LocalI64ConstSet
                    if (op0 == (byte)OpCode.I64Const && op1 == (byte)OpCode.LocalSet)
                    {
                        decision = new FuseDecision(WacsCode.LocalI64ConstSet, 2,
                            opSize[p0] + opSize[p1], 14);
                        return true;
                    }
                }
            }

            return false;
        }

        // ------------------------------------------------------------------------
        // Emitters. WriteFused reads the absorbed ops' immediates from the old
        // stream and lays out the corresponding super-op encoding. CopyOp copies
        // a non-fused op over and remaps any branch-target immediates.
        // ------------------------------------------------------------------------
        private static int WriteFused(byte[] oldCode, int p, FuseDecision fd,
                                       byte[] newCode, int writePos)
        {
            newCode[writePos++] = 0xFF;
            newCode[writePos++] = (byte)fd.SuperOp;

            switch (fd.SuperOp)
            {
                case WacsCode.LocalGetSet:
                    // local.get @p+1 (u32 from), local.set @p+5+1 (u32 to)
                    Span4(newCode, writePos, ReadU32(oldCode, p + 1)); writePos += 4;
                    Span4(newCode, writePos, ReadU32(oldCode, p + 5 + 1)); writePos += 4;
                    break;
                case WacsCode.LocalConstSet:
                    // i32.const @p+1 (s32 k), local.set @p+5+1 (u32 to)
                    Span4(newCode, writePos, ReadU32(oldCode, p + 1)); writePos += 4;
                    Span4(newCode, writePos, ReadU32(oldCode, p + 5 + 1)); writePos += 4;
                    break;
                case WacsCode.LocalI64ConstSet:
                    // i64.const @p+1 (s64 k), local.set @p+9+1 (u32 to)
                    Span8(newCode, writePos, ReadU64(oldCode, p + 1)); writePos += 8;
                    Span4(newCode, writePos, ReadU32(oldCode, p + 9 + 1)); writePos += 4;
                    break;
                case WacsCode.I32FusedAdd:
                case WacsCode.I32FusedSub:
                case WacsCode.I32FusedMul:
                case WacsCode.I32FusedAnd:
                case WacsCode.I32FusedOr:
                    // local.get @p+1 (u32 idx), i32.const @p+5+1 (s32 k), arith @p+10
                    Span4(newCode, writePos, ReadU32(oldCode, p + 1)); writePos += 4;
                    Span4(newCode, writePos, ReadU32(oldCode, p + 5 + 1)); writePos += 4;
                    break;
                case WacsCode.I64FusedAdd:
                case WacsCode.I64FusedSub:
                case WacsCode.I64FusedMul:
                case WacsCode.I64FusedAnd:
                case WacsCode.I64FusedOr:
                    // local.get @p+1 (u32 idx), i64.const @p+5+1 (s64 k), arith @p+14
                    Span4(newCode, writePos, ReadU32(oldCode, p + 1)); writePos += 4;
                    Span8(newCode, writePos, ReadU64(oldCode, p + 5 + 1)); writePos += 8;
                    break;
                default:
                    throw new NotSupportedException($"StreamFusePass: no emit rule for {fd.SuperOp}");
            }

            return writePos;
        }

        /// <summary>Remap a branch target. <see cref="int.MaxValue"/> (0x7FFFFFFF) is a
        /// sentinel used by <c>BytecodeCompiler</c> for "jump past end of function"
        /// (synthesised from <c>return</c> and branches that exit the outermost
        /// expression) — it has no real pc so pass it through unchanged. Also accept
        /// any pc at or past the end of the original stream (safety net for other
        /// implicit-end-of-function sentinels).</summary>
        private const uint EndOfFunctionSentinel = (uint)int.MaxValue;
        private static uint RemapTarget(uint targetOld, Dictionary<int, int> oldToNew)
        {
            if (targetOld == EndOfFunctionSentinel) return EndOfFunctionSentinel;
            return oldToNew.TryGetValue((int)targetOld, out int mapped)
                ? (uint)mapped
                : targetOld;   // unknown target — leave as-is rather than crash
        }

        private static int CopyOp(byte[] oldCode, int p, int size,
                                   Dictionary<int, int> oldToNew, byte[] newCode, int writePos)
        {
            byte b = oldCode[p];
            // For non-branching ops, copy bytes verbatim. Branches need their
            // target-pc immediate remapped through oldToNew.
            if (b >= 0xFB && b <= 0xFF)
            {
                Buffer.BlockCopy(oldCode, p, newCode, writePos, size);
                return writePos + size;
            }

            OpCode op = (OpCode)b;
            switch (op)
            {
                case OpCode.Br:
                case OpCode.BrIf:
                case OpCode.BrOnNull:
                case OpCode.BrOnNonNull:
                {
                    // triple = (target:u32, resultsHeight:u32, arity:u32)
                    newCode[writePos++] = b;
                    Span4(newCode, writePos, RemapTarget(ReadU32(oldCode, p + 1), oldToNew)); writePos += 4;
                    Buffer.BlockCopy(oldCode, p + 5, newCode, writePos, 8); writePos += 8;
                    return writePos;
                }
                case OpCode.If:
                case OpCode.Else:
                {
                    newCode[writePos++] = b;
                    Span4(newCode, writePos, RemapTarget(ReadU32(oldCode, p + 1), oldToNew)); writePos += 4;
                    return writePos;
                }
                case OpCode.BrTable:
                {
                    newCode[writePos++] = b;
                    uint count = ReadU32(oldCode, p + 1);
                    Span4(newCode, writePos, count); writePos += 4;
                    int oldTriple = p + 5;
                    for (int i = 0; i <= count; i++)
                    {
                        Span4(newCode, writePos, RemapTarget(ReadU32(oldCode, oldTriple), oldToNew)); writePos += 4;
                        Buffer.BlockCopy(oldCode, oldTriple + 4, newCode, writePos, 8); writePos += 8;
                        oldTriple += 12;
                    }
                    return writePos;
                }
                default:
                    Buffer.BlockCopy(oldCode, p, newCode, writePos, size);
                    return writePos + size;
            }
        }

        // ------------------------------------------------------------------------
        // Tiny LE read/write helpers. The annotated stream is always little-endian.
        // ------------------------------------------------------------------------
        private static uint ReadU32(byte[] code, int pc) =>
            BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(pc));
        private static ulong ReadU64(byte[] code, int pc) =>
            BinaryPrimitives.ReadUInt64LittleEndian(code.AsSpan(pc));
        private static void Span4(byte[] code, int pc, uint v) =>
            BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(pc, 4), v);
        private static void Span8(byte[] code, int pc, ulong v) =>
            BinaryPrimitives.WriteUInt64LittleEndian(code.AsSpan(pc, 8), v);
    }
}
