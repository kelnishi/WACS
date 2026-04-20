// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Wacs.Core.Compilation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// Register-program super-op handler — prototype for folding arbitrary-
    /// depth pure-arith expression subtrees into a single dispatch. The
    /// stream carries an inline register-machine bytecode; this handler
    /// loads inputs from the OpStack into 8 <c>ulong</c> locals, runs the
    /// inner bytecode in a tight switch, and pushes the declared outputs
    /// back onto the OpStack.
    ///
    /// <para>Stream format (starting at <c>pc</c> when this handler runs,
    /// i.e. after the outer <c>0xFF 0x80</c> prefix+secondary):</para>
    /// <code>
    ///   u8  nInputs          — popped off the OpStack into r0..r[nInputs-1]
    ///   u8  nOutputs         — pushed onto the OpStack from r[outputRegs[i]]
    ///   u16 microByteCount   — length of the inner bytecode, LE
    ///   u8* outputRegs       — array of length nOutputs; register indices
    ///   u8* microBytecode    — length microByteCount
    /// </code>
    ///
    /// <para>Microop ISA (see <c>MOP_*</c> constants). All register indices
    /// are single bytes (0..7); stricter encoding can come later if we
    /// want smaller microops. Every scalar is stored as <c>ulong</c>;
    /// signedness is applied per-op when reading.</para>
    /// </summary>
    public static class RegProgHandler
    {
        // ---- Microop opcodes ---------------------------------------------

        public const byte MOP_CONST_I32   = 0x01; // op, dst, imm:s32
        public const byte MOP_LOCAL_GET   = 0x02; // op, dst, idx:u32
        public const byte MOP_I32_ADD     = 0x10; // op, dst, a, b
        public const byte MOP_I32_SUB     = 0x11;
        public const byte MOP_I32_MUL     = 0x12;
        public const byte MOP_I32_AND     = 0x13;
        public const byte MOP_I32_OR      = 0x14;
        public const byte MOP_I32_XOR     = 0x15;
        public const byte MOP_I32_SHL     = 0x16;
        public const byte MOP_I32_SHR_S   = 0x17;
        public const byte MOP_I32_SHR_U   = 0x18;
        public const byte MOP_I32_EQ      = 0x20;
        public const byte MOP_I32_NE      = 0x21;
        public const byte MOP_I32_LT_S    = 0x22;
        public const byte MOP_I32_LT_U    = 0x23;
        public const byte MOP_I32_GT_S    = 0x24;
        public const byte MOP_I32_GT_U    = 0x25;
        public const byte MOP_I32_LE_S    = 0x26;
        public const byte MOP_I32_LE_U    = 0x27;
        public const byte MOP_I32_GE_S    = 0x28;
        public const byte MOP_I32_GE_U    = 0x29;

        // ---- Handler ------------------------------------------------------
        //
        // Signature: generator emits this with `ref int pc` (pc advances as
        // we read the header + microbytecode). ExecContext access gives the
        // OpStack + Frame.Locals.Span for pop/push + local loads.
        //
        // Hot-path discipline: 8 register locals (r0..r7) as ulong in method
        // locals so the JIT can keep them in CPU registers. Inner switch is
        // ~20 cases — much smaller than the outer 172-case Run switch.

        // Note: NOT an [OpHandler] — the generator inlines handler bodies
        // into the dispatcher, which breaks on this method's references to
        // private class-level consts. Called directly from generated IL
        // via a hand-written case emitted by DispatchGenerator.
        public static void Execute(ExecContext ctx, ref int pc, ReadOnlySpan<byte> code)
        {
            // Hoist a ref to the first byte of `code` so all inline reads
            // below use Unsafe.Add(ref codeBase, offset) instead of the
            // ReadOnlySpan indexer's bounds check.
            ref byte codeBase = ref MemoryMarshal.GetReference(code);

            // --- Read header ---------------------------------------------
            int nInputs = code[pc++];
            int nOutputs = code[pc++];
            int microByteCount = code[pc] | (code[pc + 1] << 8);
            pc += 2;

            int outputsPc = pc;
            pc += nOutputs;

            // --- Pop inputs into regs[0..nInputs-1] ----------------------
            // Reverse order since stack top = last pushed = last input.
            // Register file on the caller's stack frame as an 8-slot Span;
            // indexed access compiles to `mov <reg>, [rsp + idx*8]` on
            // x64 / ARM64 — cheaper than a switch-over-8-cases dispatch
            // and avoids the per-reg-index branch tree the JIT emits for
            // named-local switches. Tradeoff: no register-pinning — the
            // 8 slots stay memory-backed but very cache-hot.
            var opStack = ctx.OpStack;
            Span<ulong> regs = stackalloc ulong[8];
            for (int i = nInputs - 1; i >= 0; i--)
                regs[i] = (ulong)(uint)opStack.PopI32Fast();

            // --- Inner microop loop --------------------------------------
            int microEnd = pc + microByteCount;
            var locals = ctx.Frame.Locals.Span;
            while (pc < microEnd)
            {
                byte mop = code[pc++];
                switch (mop)
                {
                    case MOP_CONST_I32:
                    {
                        byte dst = code[pc++];
                        int imm = Unsafe.ReadUnaligned<int>(ref Unsafe.Add(ref codeBase, pc));
                        pc += 4;
                        regs[dst] = (ulong)(uint)imm;
                        break;
                    }
                    case MOP_LOCAL_GET:
                    {
                        byte dst = code[pc++];
                        uint idx = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref codeBase, pc));
                        pc += 4;
                        regs[dst] = (ulong)(uint)locals[(int)idx].Data.Int32;
                        break;
                    }
                    case MOP_I32_ADD:
                    {
                        byte dst = code[pc++]; byte a = code[pc++]; byte b = code[pc++];
                        int x = (int)(uint)regs[a];
                        int y = (int)(uint)regs[b];
                        regs[dst] = (ulong)(uint)(x + y);
                        break;
                    }
                    case MOP_I32_SUB:
                    {
                        byte dst = code[pc++]; byte a = code[pc++]; byte b = code[pc++];
                        int x = (int)(uint)regs[a];
                        int y = (int)(uint)regs[b];
                        regs[dst] = (ulong)(uint)(x - y);
                        break;
                    }
                    case MOP_I32_MUL:
                    {
                        byte dst = code[pc++]; byte a = code[pc++]; byte b = code[pc++];
                        int x = (int)(uint)regs[a];
                        int y = (int)(uint)regs[b];
                        regs[dst] = (ulong)(uint)(x * y);
                        break;
                    }
                    case MOP_I32_AND:
                    case MOP_I32_OR:
                    case MOP_I32_XOR:
                    case MOP_I32_SHL:
                    case MOP_I32_SHR_S:
                    case MOP_I32_SHR_U:
                    {
                        byte dst = code[pc++]; byte a = code[pc++]; byte b = code[pc++];
                        uint x = (uint)regs[a];
                        uint y = (uint)regs[b];
                        uint result = mop switch
                        {
                            MOP_I32_AND   => x & y,
                            MOP_I32_OR    => x | y,
                            MOP_I32_XOR   => x ^ y,
                            MOP_I32_SHL   => x << (int)(y & 31),
                            MOP_I32_SHR_S => (uint)((int)x >> (int)(y & 31)),
                            MOP_I32_SHR_U => x >> (int)(y & 31),
                            _ => 0u,
                        };
                        regs[dst] = result;
                        break;
                    }
                    case MOP_I32_EQ:
                    case MOP_I32_NE:
                    case MOP_I32_LT_S:
                    case MOP_I32_LT_U:
                    case MOP_I32_GT_S:
                    case MOP_I32_GT_U:
                    case MOP_I32_LE_S:
                    case MOP_I32_LE_U:
                    case MOP_I32_GE_S:
                    case MOP_I32_GE_U:
                    {
                        byte dst = code[pc++]; byte a = code[pc++]; byte b = code[pc++];
                        uint x = (uint)regs[a];
                        uint y = (uint)regs[b];
                        bool bresult = mop switch
                        {
                            MOP_I32_EQ    => x == y,
                            MOP_I32_NE    => x != y,
                            MOP_I32_LT_S  => (int)x < (int)y,
                            MOP_I32_LT_U  => x < y,
                            MOP_I32_GT_S  => (int)x > (int)y,
                            MOP_I32_GT_U  => x > y,
                            MOP_I32_LE_S  => (int)x <= (int)y,
                            MOP_I32_LE_U  => x <= y,
                            MOP_I32_GE_S  => (int)x >= (int)y,
                            MOP_I32_GE_U  => x >= y,
                            _ => false,
                        };
                        regs[dst] = bresult ? 1ul : 0ul;
                        break;
                    }
                    default:
                        throw new InvalidProgramException(
                            $"RegProg: unknown microop 0x{mop:X2} at pc={pc - 1}");
                }
            }

            // --- Push outputs --------------------------------------------
            for (int i = 0; i < nOutputs; i++)
            {
                byte outReg = code[outputsPc + i];
                opStack.PushI32Fast((int)(uint)regs[outReg]);
            }
        }
    }
}
