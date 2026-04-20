// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Wacs.Core.Runtime;

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// Hand-written prototype dispatcher covering just the ops that fib/fac/sum need.
    /// No wrappers, no bank struct, no per-prefix sub-methods, no local functions —
    /// one method, one switch, each case is the raw instruction semantics touching
    /// <see cref="ExecContext"/> fields directly.
    ///
    /// Goal: prove that source-generated switch dispatch can beat the polymorphic
    /// interpreter when it does ONLY what each opcode semantically requires (one
    /// array load + the work + the push/pop) and nothing else.
    ///
    /// Bytecode format matches what <c>Wacs.Core.Compilation.BytecodeCompiler</c>
    /// produces: byte opcode followed by fixed-width immediates, block-structure
    /// ops elided, branches as 12-byte triples (targetPc:u32, resultsHeight:u32,
    /// arity:u32).
    /// </summary>
    public static class MinimalDispatcher
    {
        public static void Run(ExecContext ctx, ReadOnlySpan<byte> code)
        {
            int pc = 0;
            var locals = ctx.Frame.Locals.Span;
            while (pc < code.Length)
            {
                byte op = code[pc++];
                switch (op)
                {
                    // 0x0C br — unconditional branch with triple.
                    case 0x0C:
                    {
                        uint targetPc = BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(pc));
                        uint resultsHeight = BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(pc + 4));
                        uint arity = BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(pc + 8));
                        ctx.OpStack.ShiftResults((int)arity, (int)resultsHeight);
                        pc = (int)targetPc;
                        continue;
                    }
                    // 0x0D br_if — pop cond; if != 0 branch.
                    case 0x0D:
                    {
                        if (ctx.OpStack.PopI32() != 0)
                        {
                            uint targetPc = BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(pc));
                            uint resultsHeight = BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(pc + 4));
                            uint arity = BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(pc + 8));
                            ctx.OpStack.ShiftResults((int)arity, (int)resultsHeight);
                            pc = (int)targetPc;
                        }
                        else
                        {
                            pc += 12;
                        }
                        continue;
                    }
                    // 0x0F return — jump to end-of-function; the outer Run loop exits.
                    case 0x0F:
                        pc = code.Length;
                        continue;
                    // 0x20 local.get
                    case 0x20:
                    {
                        int idx = (int)BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(pc));
                        pc += 4;
                        ctx.OpStack.PushValue(locals[idx]);
                        continue;
                    }
                    // 0x21 local.set
                    case 0x21:
                    {
                        int idx = (int)BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(pc));
                        pc += 4;
                        locals[idx] = ctx.OpStack.PopAny();
                        continue;
                    }
                    // 0x41 i32.const
                    case 0x41:
                    {
                        int v = BinaryPrimitives.ReadInt32LittleEndian(code.Slice(pc));
                        pc += 4;
                        ctx.OpStack.PushI32(v);
                        continue;
                    }
                    // 0x42 i64.const
                    case 0x42:
                    {
                        long v = BinaryPrimitives.ReadInt64LittleEndian(code.Slice(pc));
                        pc += 8;
                        ctx.OpStack.PushI64(v);
                        continue;
                    }
                    // 0x4A i32.gt_s
                    case 0x4A:
                    {
                        int b = ctx.OpStack.PopI32();
                        int a = ctx.OpStack.PopI32();
                        ctx.OpStack.PushI32(a > b ? 1 : 0);
                        continue;
                    }
                    // 0x4C i32.le_s
                    case 0x4C:
                    {
                        int b = ctx.OpStack.PopI32();
                        int a = ctx.OpStack.PopI32();
                        ctx.OpStack.PushI32(a <= b ? 1 : 0);
                        continue;
                    }
                    // 0x4E i32.ge_s
                    case 0x4E:
                    {
                        int b = ctx.OpStack.PopI32();
                        int a = ctx.OpStack.PopI32();
                        ctx.OpStack.PushI32(a >= b ? 1 : 0);
                        continue;
                    }
                    // 0x6A i32.add
                    case 0x6A:
                    {
                        int b = ctx.OpStack.PopI32();
                        int a = ctx.OpStack.PopI32();
                        ctx.OpStack.PushI32(a + b);
                        continue;
                    }
                    // 0x6B i32.sub
                    case 0x6B:
                    {
                        int b = ctx.OpStack.PopI32();
                        int a = ctx.OpStack.PopI32();
                        ctx.OpStack.PushI32(a - b);
                        continue;
                    }
                    // 0x6C i32.mul
                    case 0x6C:
                    {
                        int b = ctx.OpStack.PopI32();
                        int a = ctx.OpStack.PopI32();
                        ctx.OpStack.PushI32(a * b);
                        continue;
                    }
                    // 0x7C i64.add
                    case 0x7C:
                    {
                        long b = ctx.OpStack.PopI64();
                        long a = ctx.OpStack.PopI64();
                        ctx.OpStack.PushI64(a + b);
                        continue;
                    }
                    // 0x7E i64.mul
                    case 0x7E:
                    {
                        long b = ctx.OpStack.PopI64();
                        long a = ctx.OpStack.PopI64();
                        ctx.OpStack.PushI64(a * b);
                        continue;
                    }
                    // 0xAC i64.extend_i32_s
                    case 0xAC:
                        ctx.OpStack.PushI64((long)ctx.OpStack.PopI32());
                        continue;
                    default:
                        throw new NotSupportedException($"MinimalDispatcher: unsupported opcode 0x{op:X2} at pc={pc - 1}");
                }
            }
        }
    }
}
