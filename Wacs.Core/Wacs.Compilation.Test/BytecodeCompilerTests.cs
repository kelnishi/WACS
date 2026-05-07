// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Compilation;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Xunit;

namespace Wacs.Compilation.Test
{
    /// <summary>
    /// End-to-end tests for the link → annotated-stream → switch-dispatch pipeline.
    /// Construct small InstructionBase sequences by hand, compile them via
    /// <see cref="BytecodeCompiler"/>, run them through <see cref="SwitchRuntime"/>,
    /// and assert the stack state matches what the polymorphic Execute would produce.
    /// </summary>
    public class BytecodeCompilerTests
    {
        private static readonly FunctionType EmptySig =
            new(ResultType.Empty, ResultType.Empty);

        private static ExecContext FreshContext() => new ExecContext(new Store());

        [Fact]
        public void Compiles_single_numeric_binop()
        {
            // i32.add — no immediates, pure opcode.
            var compiled = BytecodeCompiler.Compile(
                new InstructionBase[] { InstI32BinOp.I32Add },
                EmptySig, localsCount: 0);
            Assert.Single(compiled.Bytecode);
            Assert.Equal((byte)OpCode.I32Add, compiled.Bytecode[0]);

            var ctx = FreshContext();
            ctx.OpStack.PushI32(40);
            ctx.OpStack.PushI32(2);
            SwitchRuntime.Run(ctx, compiled.Bytecode);
            Assert.Equal(42, ctx.OpStack.PopI32());
        }

        [Fact]
        public void Compiles_i32_const_with_inlined_immediate()
        {
            // Use the existing Immediate() helper to get a cached InstI32Const with Value=99.
            var inst = InstI32Const.Inst.Immediate(99);
            var compiled = BytecodeCompiler.Compile(
                new InstructionBase[] { inst },
                EmptySig, localsCount: 0);
            // 1 opcode byte + 4 immediate bytes = 5 bytes total.
            Assert.Equal(5, compiled.Bytecode.Length);

            var ctx = FreshContext();
            SwitchRuntime.Run(ctx, compiled.Bytecode);
            Assert.Equal(99, ctx.OpStack.PopI32());
        }

        [Fact]
        public void Compiles_full_add_program()
        {
            // Program: i32.const 10 ; i32.const 32 ; i32.add
            var linked = new InstructionBase[]
            {
                InstI32Const.Inst.Immediate(10),
                InstI32Const.Inst.Immediate(32),
                InstI32BinOp.I32Add,
            };
            var compiled = BytecodeCompiler.Compile(linked, EmptySig, localsCount: 0);
            // 5 + 5 + 1 = 11 bytes.
            Assert.Equal(11, compiled.Bytecode.Length);

            var ctx = FreshContext();
            SwitchRuntime.Run(ctx, compiled.Bytecode);
            Assert.Equal(42, ctx.OpStack.PopI32());
        }

        [Fact]
        public void Compiles_local_get_and_binop()
        {
            // Simulate: local.get 0 ; local.get 1 ; i32.add  — classic "add two locals".
            // Use InstLocalGet.Inst.Immediate(idx) to get cached instances.
            var linked = new InstructionBase[]
            {
                InstLocalGet.Inst.Immediate(0),
                InstLocalGet.Inst.Immediate(1),
                InstI32BinOp.I32Add,
            };
            var compiled = BytecodeCompiler.Compile(linked, EmptySig, localsCount: 2);
            // Two local.gets (1 + 4 bytes each) + one add (1 byte) = 11 bytes.
            Assert.Equal(11, compiled.Bytecode.Length);

            // Seed a frame with locals [5, 7].
            var ctx = FreshContext();
            var locals = new Value[]
            {
                new Value(ValType.I32, 5),
                new Value(ValType.I32, 7),
            };
            ctx.Frame = new Frame { Locals = locals };

            SwitchRuntime.Run(ctx, compiled.Bytecode);
            Assert.Equal(12, ctx.OpStack.PopI32());
        }

        [Fact]
        public void Return_terminates_execution_early()
        {
            // Program: i32.const 7 ; return ; i32.const 99  (99 should never be pushed).
            var linked = new InstructionBase[]
            {
                InstI32Const.Inst.Immediate(7),
                InstReturn.Inst,
                InstI32Const.Inst.Immediate(99),
            };
            var compiled = BytecodeCompiler.Compile(linked, EmptySig, localsCount: 0);

            var ctx = FreshContext();
            SwitchRuntime.Run(ctx, compiled.Bytecode);
            Assert.Equal(7, ctx.OpStack.PopI32());
            // Stack should now be empty — if 99 got pushed, we'd have a second value.
            Assert.False(ctx.OpStack.HasValue);
        }

        [Fact]
        public void Compiles_drop_and_select_combo()
        {
            // Stack: push 10, push 20, push 30, drop -> leaves [10, 20].
            // Then push 1 (cond truthy), select -> picks 10.
            var linked = new InstructionBase[]
            {
                InstI32Const.Inst.Immediate(10),
                InstI32Const.Inst.Immediate(20),
                InstI32Const.Inst.Immediate(30),
                new InstDrop(),
                InstI32Const.Inst.Immediate(1),
                new InstSelect(),
            };
            var compiled = BytecodeCompiler.Compile(linked, EmptySig, localsCount: 0);

            var ctx = FreshContext();
            SwitchRuntime.Run(ctx, compiled.Bytecode);
            Assert.Equal(10, ctx.OpStack.PopI32());
        }
    }
}
