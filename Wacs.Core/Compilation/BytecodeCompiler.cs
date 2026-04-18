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
    /// Current coverage matches the set of opcodes the generated dispatcher handles:
    /// numeric (no immediates), t.const (fixed-width constant), local/global access
    /// (u32 index), drop, select. Unrecognized opcodes throw
    /// <see cref="NotSupportedException"/> — expand coverage by teaching both this compiler
    /// and the dispatcher about the new opcode in tandem.
    ///
    /// AOT-safe: no reflection, no dynamic code. Pure byte-writing.
    /// </summary>
    public static class BytecodeCompiler
    {
        public static CompiledFunction Compile(
            InstructionBase[] linked,
            FunctionType signature,
            int localsCount)
        {
            var buf = new List<byte>(linked.Length * 2);
            foreach (var inst in linked)
            {
                EmitInstruction(buf, inst);
            }
            return new CompiledFunction(buf.ToArray(), localsCount, signature);
        }

        private static void EmitInstruction(List<byte> buf, InstructionBase inst)
        {
            var op = inst.Op;
            // ByteCode has an implicit conversion to OpCode returning its `x00` field —
            // which is the primary byte. Don't write `(byte)op` here: that goes through
            // ByteCode→ushort (primary << 8 | secondary) then narrows, giving the
            // *secondary* byte (zero for non-prefix ops). Hit-and-run bug ask me how I know.
            OpCode primary = op;

            // Block-structure markers are elided from the annotated stream: once Link has
            // stamped branch targets onto the block instances, block/loop/end contribute
            // nothing executable. Skip emit entirely. (If/else are NOT elided — they carry
            // conditional-jump semantics; handle those with the rest of control flow.)
            if (primary == OpCode.Block || primary == OpCode.Loop || primary == OpCode.End)
                return;

            buf.Add((byte)primary);
            switch (primary)
            {
                case OpCode.FB: buf.Add((byte)op.xFB); break;
                case OpCode.FC: buf.Add((byte)op.xFC); break;
                case OpCode.FD: buf.Add((byte)op.xFD); break;
                case OpCode.FE: buf.Add((byte)op.xFE); break;
                case OpCode.FF: buf.Add((byte)op.xFF); break;
            }

            // Immediates per opcode. Order matters — the dispatcher reads them in the same
            // left-to-right order (matching [Imm] parameter order on the handler).
            switch (primary)
            {
                // ---- t.const: one fixed-width constant ----
                case OpCode.I32Const:
                    WriteS32(buf, ((InstI32Const)inst).Value);
                    break;
                case OpCode.I64Const:
                    WriteS64(buf, ((InstI64Const)inst).Value);
                    break;
                case OpCode.F32Const:
                    WriteS32(buf, BitConverter.SingleToInt32Bits(((InstF32Const)inst).Value));
                    break;
                case OpCode.F64Const:
                    WriteS64(buf, BitConverter.DoubleToInt64Bits(((InstF64Const)inst).Value));
                    break;

                // ---- local.get/set/tee: u32 local index ----
                case OpCode.LocalGet:
                    WriteU32(buf, (uint)((InstLocalGet)inst).GetIndex());
                    break;
                case OpCode.LocalSet:
                    WriteU32(buf, (uint)((InstLocalSet)inst).GetIndex());
                    break;
                case OpCode.LocalTee:
                    WriteU32(buf, (uint)((InstLocalTee)inst).GetIndex());
                    break;

                // ---- global.get/set: u32 global index ----
                case OpCode.GlobalGet:
                    WriteU32(buf, (uint)((InstGlobalGet)inst).GetIndex());
                    break;
                case OpCode.GlobalSet:
                    WriteU32(buf, (uint)((InstGlobalSet)inst).GetIndex());
                    break;

                // ---- call: u32 function index ----
                case OpCode.Call:
                    WriteU32(buf, ((InstCall)inst).X.Value);
                    break;

                // ---- No-immediate ops: drop, select, return, and every numeric opcode ----
                case OpCode.Drop:
                case OpCode.Select:
                case OpCode.Return:
                    break;

                default:
                    // Numeric binops/unops/relops/testops carry no immediates. If the opcode
                    // has an [OpSource] numeric handler, we're done. Otherwise the compiler
                    // has outpaced the dispatcher and should refuse the build.
                    if (IsNumericStackOnly(primary))
                        break;
                    throw new NotSupportedException(
                        $"BytecodeCompiler cannot yet emit opcode {op} (primary 0x{(byte)primary:X2}). " +
                        "Add a case here and a matching [OpSource]/[OpHandler] on the dispatcher side.");
            }
        }

        /// <summary>
        /// True for opcodes that have a numeric [OpSource] handler and no immediates.
        /// Covers the 102 numeric ops already generated into GeneratedDispatcher.
        /// </summary>
        private static bool IsNumericStackOnly(OpCode op)
        {
            // Spec ranges: 0x45..0xC4 is the numeric block (const excluded — we handled above).
            // Also i32/i64 sign-extension ops 0xC0..0xC4 sit at the top of that range.
            byte b = (byte)op;
            return b >= 0x45 && b <= 0xC4;
        }

        private static void WriteS32(List<byte> buf, int value)
        {
            Span<byte> scratch = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(scratch, value);
            buf.Add(scratch[0]); buf.Add(scratch[1]); buf.Add(scratch[2]); buf.Add(scratch[3]);
        }

        private static void WriteU32(List<byte> buf, uint value)
        {
            Span<byte> scratch = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(scratch, value);
            buf.Add(scratch[0]); buf.Add(scratch[1]); buf.Add(scratch[2]); buf.Add(scratch[3]);
        }

        private static void WriteS64(List<byte> buf, long value)
        {
            Span<byte> scratch = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(scratch, value);
            for (int i = 0; i < 8; i++) buf.Add(scratch[i]);
        }
    }
}
