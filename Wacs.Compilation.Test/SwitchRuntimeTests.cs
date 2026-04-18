// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using Wacs.Core.Compilation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Xunit;

namespace Wacs.Compilation.Test
{
    /// <summary>
    /// Smoke-tests the generator-driven byte-stream runtime end-to-end.
    /// Each case pushes operands onto the OpStack, runs a tiny bytecode program via
    /// <see cref="SwitchRuntime.Run"/>, and asserts the popped result. No WasmRuntime,
    /// module instance, or InstructionBase allocations are involved.
    /// </summary>
    public class SwitchRuntimeTests
    {
        private static ExecContext FreshContext() => new ExecContext(new Store());

        // -----------------------------------------------------------------------------
        // Stack-only numeric ops (the [OpSource] path — no immediates in the stream).
        // -----------------------------------------------------------------------------

        [Fact]
        public void I32Add_2_3_is_5()
        {
            var ctx = FreshContext();
            ctx.OpStack.PushI32(2);
            ctx.OpStack.PushI32(3);
            SwitchRuntime.Run(ctx, new byte[] { (byte)OpCode.I32Add });
            Assert.Equal(5, ctx.OpStack.PopI32());
        }

        [Fact]
        public void I32Sub_is_left_minus_right()
        {
            var ctx = FreshContext();
            ctx.OpStack.PushI32(10);
            ctx.OpStack.PushI32(3);
            SwitchRuntime.Run(ctx, new byte[] { (byte)OpCode.I32Sub });
            Assert.Equal(7, ctx.OpStack.PopI32());
        }

        [Fact]
        public void I32Mul_then_I32Add_chains()
        {
            // Stack: [2, 3, 4] -> i32.mul -> [2, 12] -> i32.add -> [14]
            var ctx = FreshContext();
            ctx.OpStack.PushI32(2);
            ctx.OpStack.PushI32(3);
            ctx.OpStack.PushI32(4);
            SwitchRuntime.Run(ctx, new byte[] { (byte)OpCode.I32Mul, (byte)OpCode.I32Add });
            Assert.Equal(14, ctx.OpStack.PopI32());
        }

        [Fact]
        public void I32Eqz_on_zero_returns_one()
        {
            var ctx = FreshContext();
            ctx.OpStack.PushI32(0);
            SwitchRuntime.Run(ctx, new byte[] { (byte)OpCode.I32Eqz });
            Assert.Equal(1, ctx.OpStack.PopI32());
        }

        [Fact]
        public void I32Eqz_on_nonzero_returns_zero()
        {
            var ctx = FreshContext();
            ctx.OpStack.PushI32(42);
            SwitchRuntime.Run(ctx, new byte[] { (byte)OpCode.I32Eqz });
            Assert.Equal(0, ctx.OpStack.PopI32());
        }

        [Fact]
        public void I32DivS_traps_on_zero()
        {
            var ctx = FreshContext();
            ctx.OpStack.PushI32(10);
            ctx.OpStack.PushI32(0);
            Assert.Throws<TrapException>(() =>
                SwitchRuntime.Run(ctx, new byte[] { (byte)OpCode.I32DivS }));
        }

        [Fact]
        public void I64Mul_works()
        {
            var ctx = FreshContext();
            ctx.OpStack.PushI64(1_000_000_000L);
            ctx.OpStack.PushI64(3L);
            SwitchRuntime.Run(ctx, new byte[] { (byte)OpCode.I64Mul });
            Assert.Equal(3_000_000_000L, ctx.OpStack.PopI64());
        }

        [Fact]
        public void F64Add_works()
        {
            var ctx = FreshContext();
            ctx.OpStack.PushF64(1.5);
            ctx.OpStack.PushF64(2.25);
            SwitchRuntime.Run(ctx, new byte[] { (byte)OpCode.F64Add });
            Assert.Equal(3.75, ctx.OpStack.PopF64());
        }

        // -----------------------------------------------------------------------------
        // Immediate-carrying ops (the [OpHandler] path — constant inlined in stream).
        // -----------------------------------------------------------------------------

        [Fact]
        public void I32Const_pushes_immediate()
        {
            var ctx = FreshContext();
            var code = new byte[5];
            code[0] = (byte)OpCode.I32Const;
            BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(1), 42);
            SwitchRuntime.Run(ctx, code);
            Assert.Equal(42, ctx.OpStack.PopI32());
        }

        [Fact]
        public void I32Const_negative_value()
        {
            var ctx = FreshContext();
            var code = new byte[5];
            code[0] = (byte)OpCode.I32Const;
            BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(1), -12345);
            SwitchRuntime.Run(ctx, code);
            Assert.Equal(-12345, ctx.OpStack.PopI32());
        }

        [Fact]
        public void I32Const_then_I32Add_end_to_end()
        {
            // Program: i32.const 10 ; i32.const 32 ; i32.add  -> 42
            var ctx = FreshContext();
            var code = new byte[11];
            code[0] = (byte)OpCode.I32Const;
            BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(1), 10);
            code[5] = (byte)OpCode.I32Const;
            BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(6), 32);
            code[10] = (byte)OpCode.I32Add;
            SwitchRuntime.Run(ctx, code);
            Assert.Equal(42, ctx.OpStack.PopI32());
        }

        [Fact]
        public void I64Const_pushes_immediate()
        {
            var ctx = FreshContext();
            var code = new byte[9];
            code[0] = (byte)OpCode.I64Const;
            BinaryPrimitives.WriteInt64LittleEndian(code.AsSpan(1), 1234567890123L);
            SwitchRuntime.Run(ctx, code);
            Assert.Equal(1234567890123L, ctx.OpStack.PopI64());
        }

        [Fact]
        public void F32Const_pushes_immediate()
        {
            var ctx = FreshContext();
            var code = new byte[5];
            code[0] = (byte)OpCode.F32Const;
            BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(1), System.BitConverter.SingleToInt32Bits(3.14f));
            SwitchRuntime.Run(ctx, code);
            Assert.Equal(3.14f, ctx.OpStack.PopF32());
        }

        [Fact]
        public void F64Const_pushes_immediate()
        {
            var ctx = FreshContext();
            var code = new byte[9];
            code[0] = (byte)OpCode.F64Const;
            BinaryPrimitives.WriteInt64LittleEndian(code.AsSpan(1), System.BitConverter.DoubleToInt64Bits(2.718281828)) ;
            SwitchRuntime.Run(ctx, code);
            Assert.Equal(2.718281828, ctx.OpStack.PopF64());
        }

        // -----------------------------------------------------------------------------
        // Variable ops — exercise the Frame.Locals integration.
        // -----------------------------------------------------------------------------

        [Fact]
        public void LocalGet_reads_from_frame_locals()
        {
            // Build a context with a single i32 local seeded to 7.
            var ctx = FreshContext();
            var localsArr = new Value[] { new Value(Wacs.Core.Types.Defs.ValType.I32, 7) };
            ctx.Frame = new Frame { Locals = localsArr };

            var code = new byte[5];
            code[0] = (byte)OpCode.LocalGet;
            BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(1), 0u);
            SwitchRuntime.Run(ctx, code);
            Assert.Equal(7, ctx.OpStack.PopI32());
        }

        [Fact]
        public void LocalSet_writes_to_frame_locals()
        {
            var ctx = FreshContext();
            var localsArr = new Value[] { new Value(Wacs.Core.Types.Defs.ValType.I32, 0) };
            ctx.Frame = new Frame { Locals = localsArr };
            ctx.OpStack.PushI32(99);

            var code = new byte[5];
            code[0] = (byte)OpCode.LocalSet;
            BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(1), 0u);
            SwitchRuntime.Run(ctx, code);
            Assert.Equal(99, localsArr[0].Data.Int32);
        }

        [Fact]
        public void LocalTee_writes_and_leaves_on_stack()
        {
            var ctx = FreshContext();
            var localsArr = new Value[] { new Value(Wacs.Core.Types.Defs.ValType.I32, 0) };
            ctx.Frame = new Frame { Locals = localsArr };
            ctx.OpStack.PushI32(55);

            var code = new byte[5];
            code[0] = (byte)OpCode.LocalTee;
            BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(1), 0u);
            SwitchRuntime.Run(ctx, code);
            Assert.Equal(55, localsArr[0].Data.Int32);
            Assert.Equal(55, ctx.OpStack.PopI32());
        }

        // -----------------------------------------------------------------------------
        // Parametric — Drop, Select.
        // -----------------------------------------------------------------------------

        [Fact]
        public void Drop_discards_top_of_stack()
        {
            var ctx = FreshContext();
            ctx.OpStack.PushI32(1);
            ctx.OpStack.PushI32(2);
            SwitchRuntime.Run(ctx, new byte[] { (byte)OpCode.Drop });
            Assert.Equal(1, ctx.OpStack.PopI32());
        }

        [Fact]
        public void Select_picks_v1_when_cond_nonzero()
        {
            var ctx = FreshContext();
            ctx.OpStack.PushI32(10);  // v1
            ctx.OpStack.PushI32(20);  // v2
            ctx.OpStack.PushI32(1);   // cond (truthy)
            SwitchRuntime.Run(ctx, new byte[] { (byte)OpCode.Select });
            Assert.Equal(10, ctx.OpStack.PopI32());
        }

        [Fact]
        public void Select_picks_v2_when_cond_zero()
        {
            var ctx = FreshContext();
            ctx.OpStack.PushI32(10);  // v1
            ctx.OpStack.PushI32(20);  // v2
            ctx.OpStack.PushI32(0);   // cond
            SwitchRuntime.Run(ctx, new byte[] { (byte)OpCode.Select });
            Assert.Equal(20, ctx.OpStack.PopI32());
        }

        // -----------------------------------------------------------------------------
        // Coverage sanity.
        // -----------------------------------------------------------------------------

        [Fact]
        public void Unknown_opcode_throws()
        {
            var ctx = FreshContext();
            // 0x00 (Unreachable) has no [OpSource] coverage yet — expect NotSupported.
            Assert.Throws<System.NotSupportedException>(() =>
                SwitchRuntime.Run(ctx, new byte[] { 0x00 }));
        }

        [Fact]
        public void HandledOpcodeCount_includes_phase2_additions()
        {
            // 102 numeric [OpSource] + 4 const + 5 variable + 2 parametric + return + call = 115 minimum.
            Assert.True(GeneratedDispatcher.HandledOpcodeCount >= 115,
                $"Expected ≥115 covered ops; got {GeneratedDispatcher.HandledOpcodeCount}.");
        }
    }
}
