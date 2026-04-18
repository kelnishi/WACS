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
using System.Runtime.CompilerServices;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// Translates a linked <see cref="InstructionBase"/>[] into the annotated bytecode stream
    /// consumed by <see cref="SwitchRuntime"/>. Runs once per function at instantiation time;
    /// the produced <see cref="CompiledFunction"/> is reusable across invocations.
    ///
    /// Two passes:
    /// <list type="number">
    ///   <item>Sizing — compute per-instruction byte footprint into <c>streamOffset[]</c> and
    ///   build maps from each <c>BlockTarget</c> to its local array index (for loop targets)
    ///   and to its matching <c>End</c>'s local index (for block/if targets).</item>
    ///   <item>Emit — write opcodes + immediates, resolving branch targets via the maps
    ///   built in pass 1.</item>
    /// </list>
    ///
    /// AOT-safe: pure byte writes, no reflection.
    /// </summary>
    public static class BytecodeCompiler
    {
        public static CompiledFunction Compile(
            InstructionBase[] linked,
            FunctionType signature,
            int localsCount)
        {
            // --- Pass 1: sizing + block maps -------------------------------------------------
            var streamOffset = new int[linked.Length + 1];
            // ReferenceEqualityComparer<T>.Instance was added in .NET 5; the netstandard2.1
            // target doesn't ship it, so use a tiny reference-identity polyfill.
            var blockLocalIdx = new Dictionary<BlockTarget, int>(RefEq<BlockTarget>.Instance);
            var blockEndLocalIdx = new Dictionary<BlockTarget, int>(RefEq<BlockTarget>.Instance);
            var labelStack = new Stack<BlockTarget>();
            int running = 0;
            for (int i = 0; i < linked.Length; i++)
            {
                streamOffset[i] = running;
                var inst = linked[i];
                if (inst is BlockTarget bt)
                {
                    blockLocalIdx[bt] = i;
                    labelStack.Push(bt);
                }
                else if (inst is InstEnd)
                {
                    if (labelStack.Count > 0) blockEndLocalIdx[labelStack.Pop()] = i;
                }
                running += SizeOf(inst);
            }
            streamOffset[linked.Length] = running;

            // --- Pass 2: emit ----------------------------------------------------------------
            var buf = new byte[running];
            int writePos = 0;
            for (int i = 0; i < linked.Length; i++)
            {
                writePos = Emit(buf, writePos, linked[i], streamOffset, blockLocalIdx, blockEndLocalIdx);
            }

            return new CompiledFunction(buf, localsCount, signature);
        }

        /// <summary>Byte footprint of <paramref name="inst"/> in the annotated stream.</summary>
        private static int SizeOf(InstructionBase inst)
        {
            OpCode primary = inst.Op;
            // Block-structure markers are elided from the stream (Link already stamped
            // branch targets onto the block instances; nothing to execute).
            if (primary == OpCode.Block || primary == OpCode.Loop || primary == OpCode.End)
                return 0;

            // Prefix ops need their secondary byte too.
            int hdr = (primary == OpCode.FB || primary == OpCode.FC || primary == OpCode.FD ||
                      primary == OpCode.FE || primary == OpCode.FF) ? 2 : 1;

            int imm = primary switch
            {
                // Constants: inline the value at fixed width.
                OpCode.I32Const => 4,
                OpCode.I64Const => 8,
                OpCode.F32Const => 4,
                OpCode.F64Const => 8,
                // Variable + call: u32 index.
                OpCode.LocalGet or OpCode.LocalSet or OpCode.LocalTee => 4,
                OpCode.GlobalGet or OpCode.GlobalSet => 4,
                OpCode.Call => 4,
                // Branches: three u32 values — (target_pc, restore_stack, arity).
                OpCode.Br => 12,
                // No-immediate ops (drop/select/return/unreachable/nop/numeric).
                _ => 0,
            };
            return hdr + imm;
        }

        private static int Emit(
            byte[] buf, int writePos,
            InstructionBase inst,
            int[] streamOffset,
            IReadOnlyDictionary<BlockTarget, int> blockLocalIdx,
            IReadOnlyDictionary<BlockTarget, int> blockEndLocalIdx)
        {
            var op = inst.Op;
            OpCode primary = op;

            if (primary == OpCode.Block || primary == OpCode.Loop || primary == OpCode.End)
                return writePos;

            buf[writePos++] = (byte)primary;
            switch (primary)
            {
                case OpCode.FB: buf[writePos++] = (byte)op.xFB; break;
                case OpCode.FC: buf[writePos++] = (byte)op.xFC; break;
                case OpCode.FD: buf[writePos++] = (byte)op.xFD; break;
                case OpCode.FE: buf[writePos++] = (byte)op.xFE; break;
                case OpCode.FF: buf[writePos++] = (byte)op.xFF; break;
            }

            switch (primary)
            {
                // ---- t.const ----
                case OpCode.I32Const:
                    writePos = WriteS32(buf, writePos, ((InstI32Const)inst).Value); break;
                case OpCode.I64Const:
                    writePos = WriteS64(buf, writePos, ((InstI64Const)inst).Value); break;
                case OpCode.F32Const:
                    writePos = WriteS32(buf, writePos, BitConverter.SingleToInt32Bits(((InstF32Const)inst).Value)); break;
                case OpCode.F64Const:
                    writePos = WriteS64(buf, writePos, BitConverter.DoubleToInt64Bits(((InstF64Const)inst).Value)); break;

                // ---- local/global ----
                case OpCode.LocalGet:
                    writePos = WriteU32(buf, writePos, (uint)((InstLocalGet)inst).GetIndex()); break;
                case OpCode.LocalSet:
                    writePos = WriteU32(buf, writePos, (uint)((InstLocalSet)inst).GetIndex()); break;
                case OpCode.LocalTee:
                    writePos = WriteU32(buf, writePos, (uint)((InstLocalTee)inst).GetIndex()); break;
                case OpCode.GlobalGet:
                    writePos = WriteU32(buf, writePos, (uint)((InstGlobalGet)inst).GetIndex()); break;
                case OpCode.GlobalSet:
                    writePos = WriteU32(buf, writePos, (uint)((InstGlobalSet)inst).GetIndex()); break;

                // ---- call ----
                case OpCode.Call:
                    writePos = WriteU32(buf, writePos, ((InstCall)inst).X.Value); break;

                // ---- branches ----
                case OpCode.Br:
                    writePos = WriteBranch(buf, writePos, ((InstBranch)inst).LinkedLabel!,
                                           streamOffset, blockLocalIdx, blockEndLocalIdx);
                    break;

                // ---- No-immediate ----
                case OpCode.Unreachable:
                case OpCode.Nop:
                case OpCode.Drop:
                case OpCode.Select:
                case OpCode.Return:
                    break;

                default:
                    if (IsNumericStackOnly(primary)) break;
                    throw new NotSupportedException(
                        $"BytecodeCompiler cannot yet emit opcode {op} (primary 0x{(byte)primary:X2}). " +
                        "Add a case here and a matching [OpSource]/[OpHandler] on the dispatcher side.");
            }

            return writePos;
        }

        /// <summary>
        /// Writes the branch-target triple: target_pc, results_height, arity. For Loop labels
        /// the target is the block's own stream position (re-enter the loop body); for Block
        /// and If labels it's the position past the matching End.
        ///
        /// <c>results_height</c> is the final OpStack height after the branch completes —
        /// the label's entry height plus its arity. It's precomputed here so the handler
        /// can pass it straight to <c>OpStack.ShiftResults</c> without an add at runtime.
        /// </summary>
        private static int WriteBranch(
            byte[] buf, int writePos,
            BlockTarget target,
            int[] streamOffset,
            IReadOnlyDictionary<BlockTarget, int> blockLocalIdx,
            IReadOnlyDictionary<BlockTarget, int> blockEndLocalIdx)
        {
            int targetIdx = ((OpCode)target.Op) == OpCode.Loop
                ? blockLocalIdx[target]
                : blockEndLocalIdx[target];
            int arity = target.Label.Arity;
            uint targetPc      = (uint)streamOffset[targetIdx];
            uint resultsHeight = (uint)(target.Label.StackHeight + arity);
            writePos = WriteU32(buf, writePos, targetPc);
            writePos = WriteU32(buf, writePos, resultsHeight);
            writePos = WriteU32(buf, writePos, (uint)arity);
            return writePos;
        }

        /// <summary>
        /// True for opcodes that have a numeric [OpSource] handler and no immediates.
        /// Covers the 102 numeric ops already generated into GeneratedDispatcher.
        /// </summary>
        private static bool IsNumericStackOnly(OpCode op)
        {
            byte b = (byte)op;
            return b >= 0x45 && b <= 0xC4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int WriteS32(byte[] buf, int pos, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos, 4), value);
            return pos + 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int WriteU32(byte[] buf, int pos, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), value);
            return pos + 4;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int WriteS64(byte[] buf, int pos, long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(pos, 8), value);
            return pos + 8;
        }

        private sealed class RefEq<T> : IEqualityComparer<T> where T : class
        {
            public static readonly RefEq<T> Instance = new RefEq<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
