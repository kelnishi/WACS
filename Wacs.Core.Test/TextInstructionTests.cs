// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Linq;
using Wacs.Core;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Text;
using Wacs.Core.Types.Defs;
using Xunit;

namespace Wacs.Core.Test
{
    public class TextInstructionTests
    {
        private static Module ParseWithFunc(string body, string signature = "(func)", string? locals = null)
        {
            var src = $@"
                (module
                  (type {signature})
                  (func (type 0) {locals ?? ""}
                    {body}))";
            return TextModuleParser.ParseWat(src);
        }

        // ---- Constants ----------------------------------------------------

        [Fact]
        public void I32_const_plain_form()
        {
            var m = ParseWithFunc("i32.const 42");
            var insts = m.Funcs[0].Body.Instructions.ToList();
            // expect [i32.const 42, end]
            Assert.Equal(2, insts.Count);
            var c = Assert.IsType<InstI32Const>(insts[0]);
            Assert.Equal(42, c.Value);
            Assert.IsType<InstEnd>(insts[1]);
        }

        [Fact]
        public void I32_const_folded_form()
        {
            var m = ParseWithFunc("(i32.const -1)");
            var insts = m.Funcs[0].Body.Instructions.ToList();
            var c = Assert.IsType<InstI32Const>(insts[0]);
            Assert.Equal(-1, c.Value);
        }

        [Fact]
        public void I32_const_hex_literal()
        {
            var m = ParseWithFunc("i32.const 0xCAFE");
            var c = Assert.IsType<InstI32Const>(m.Funcs[0].Body.Instructions.First());
            Assert.Equal(0xCAFE, c.Value);
        }

        [Fact]
        public void I64_const_ok()
        {
            var m = ParseWithFunc("i64.const 123456789012");
            // We don't have a clean public getter for i64 Value, but the
            // presence of an i64.const instruction is enough to assert
            // parsing succeeded.
            Assert.Equal((ByteCode)OpCode.I64Const,
                m.Funcs[0].Body.Instructions.First().Op);
        }

        // ---- Locals -------------------------------------------------------

        [Fact]
        public void Local_get_by_index()
        {
            var m = ParseWithFunc(
                "local.get 0 local.get 1 i32.add",
                signature: "(func (param i32 i32) (result i32))");
            var insts = m.Funcs[0].Body.Instructions.ToList();
            Assert.Equal((ByteCode)OpCode.LocalGet, insts[0].Op);
            Assert.Equal((ByteCode)OpCode.LocalGet, insts[1].Op);
            Assert.Equal((ByteCode)OpCode.I32Add,   insts[2].Op);
            Assert.IsType<InstEnd>(insts[3]);
        }

        [Fact]
        public void Local_get_by_name()
        {
            // $x in (type …) doesn't carry through the (type $n) reference
            // per spec — callers must redeclare via a redundant (param $x T)
            // after the (type 0) header to bind the local name.
            var src = @"
                (module
                  (type (func (param i32) (result i32)))
                  (func (type 0) (param $x i32) (result i32)
                    local.get $x))";
            var m = TextModuleParser.ParseWat(src);
            var insts = m.Funcs[0].Body.Instructions.ToList();
            var lg = Assert.IsType<InstLocalGet>(insts[0]);
            Assert.Equal(0, lg.GetIndex());
        }

        [Fact]
        public void Local_declarations_extend_local_indices()
        {
            var src = @"
                (module
                  (type (func (param i32)))
                  (func (type 0) (param $a i32)
                    (local $tmp i32)
                    local.get $a
                    local.set $tmp))";
            var m = TextModuleParser.ParseWat(src);
            // $a is local 0 (from params); $tmp is local 1 (declared)
            var insts = m.Funcs[0].Body.Instructions.ToList();
            var get = Assert.IsType<InstLocalGet>(insts[0]);
            Assert.Equal(0, get.GetIndex());
            Assert.Equal((ByteCode)OpCode.LocalSet, insts[1].Op);
            // Func.Locals should contain 1 declared local
            Assert.Single(m.Funcs[0].Locals);
            Assert.Equal(Wacs.Core.Types.Defs.ValType.I32, m.Funcs[0].Locals[0]);
        }

        // ---- Globals ------------------------------------------------------

        [Fact]
        public void Global_get_by_name()
        {
            var src = @"
                (module
                  (type (func (result i32)))
                  (global $g i32 (i32.const 10))
                  (func (type 0)
                    global.get $g))";
            var m = TextModuleParser.ParseWat(src);
            var insts = m.Funcs[0].Body.Instructions.ToList();
            Assert.Equal((ByteCode)OpCode.GlobalGet, insts[0].Op);
        }

        [Fact]
        public void Global_initializer_is_parsed()
        {
            var src = @"
                (module
                  (global $g i32 (i32.const 7)))";
            var m = TextModuleParser.ParseWat(src);
            var init = m.Globals[0].Initializer.Instructions.ToList();
            var c = Assert.IsType<InstI32Const>(init[0]);
            Assert.Equal(7, c.Value);
            Assert.IsType<InstEnd>(init[1]);
        }

        // ---- Folded-form nesting -----------------------------------------

        [Fact]
        public void Folded_binary_op_flattens_operands_then_op()
        {
            // (i32.add (i32.const 1) (i32.const 2)) =>
            // [i32.const 1, i32.const 2, i32.add, end]
            var m = ParseWithFunc(
                "(i32.add (i32.const 1) (i32.const 2))",
                signature: "(func (result i32))");
            var insts = m.Funcs[0].Body.Instructions.ToList();
            Assert.Equal(4, insts.Count);
            Assert.Equal((ByteCode)OpCode.I32Const, insts[0].Op);
            Assert.Equal(1, ((InstI32Const)insts[0]).Value);
            Assert.Equal((ByteCode)OpCode.I32Const, insts[1].Op);
            Assert.Equal(2, ((InstI32Const)insts[1]).Value);
            Assert.Equal((ByteCode)OpCode.I32Add, insts[2].Op);
            Assert.IsType<InstEnd>(insts[3]);
        }

        // ---- Control flow -------------------------------------------------

        [Fact]
        public void Block_with_label_and_branch()
        {
            var src = @"
                (module
                  (type (func))
                  (func (type 0)
                    block $exit
                      br $exit
                    end))";
            var m = TextModuleParser.ParseWat(src);
            var insts = m.Funcs[0].Body.Instructions.ToList();
            var block = Assert.IsType<InstBlock>(insts[0]);
            // block's inner seq should contain: br 0, end
            // We can verify via the interface.
            Assert.Equal(1, block.Count);
            var inner = block.GetBlock(0);
            Assert.Equal(ByteCode.Br, inner.Instructions[0]!.Op);
        }

        [Fact]
        public void Loop_does_not_break_parse()
        {
            var src = @"
                (module
                  (type (func))
                  (func (type 0)
                    loop
                      nop
                    end))";
            var m = TextModuleParser.ParseWat(src);
            Assert.IsType<InstLoop>(m.Funcs[0].Body.Instructions.First());
        }

        [Fact]
        public void If_with_else_parses()
        {
            var src = @"
                (module
                  (type (func (param i32) (result i32)))
                  (func (type 0)
                    local.get 0
                    if (result i32)
                      i32.const 1
                    else
                      i32.const 2
                    end))";
            var m = TextModuleParser.ParseWat(src);
            var insts = m.Funcs[0].Body.Instructions.ToList();
            Assert.Equal((ByteCode)OpCode.LocalGet, insts[0].Op);
            Assert.IsType<InstIf>(insts[1]);
        }

        [Fact]
        public void If_folded_form()
        {
            var src = @"
                (module
                  (type (func (param i32) (result i32)))
                  (func (type 0)
                    (if (result i32) (local.get 0)
                      (then (i32.const 100))
                      (else (i32.const 200)))))";
            var m = TextModuleParser.ParseWat(src);
            // First two instructions: local.get (condition), if.
            var insts = m.Funcs[0].Body.Instructions.ToList();
            Assert.Equal((ByteCode)OpCode.LocalGet, insts[0].Op);
            Assert.IsType<InstIf>(insts[1]);
        }

        // ---- Call --------------------------------------------------------

        [Fact]
        public void Call_by_name()
        {
            var src = @"
                (module
                  (type (func (result i32)))
                  (func $a (type 0) i32.const 42)
                  (func (type 0) call $a))";
            var m = TextModuleParser.ParseWat(src);
            var insts = m.Funcs[1].Body.Instructions.ToList();
            Assert.Equal((ByteCode)OpCode.Call, insts[0].Op);
        }

        // ---- Nullary / numeric regression --------------------------------

        [Fact]
        public void Nop_unreachable_return_drop_parse()
        {
            var m = ParseWithFunc("nop unreachable return drop");
            var insts = m.Funcs[0].Body.Instructions.ToList();
            Assert.Equal((ByteCode)OpCode.Nop,         insts[0].Op);
            Assert.Equal((ByteCode)OpCode.Unreachable, insts[1].Op);
            Assert.Equal((ByteCode)OpCode.Return,      insts[2].Op);
            Assert.Equal((ByteCode)OpCode.Drop,        insts[3].Op);
        }

        [Theory]
        [InlineData("i32.add", OpCode.I32Add)]
        [InlineData("i32.sub", OpCode.I32Sub)]
        [InlineData("i32.mul", OpCode.I32Mul)]
        [InlineData("i64.add", OpCode.I64Add)]
        [InlineData("f32.mul", OpCode.F32Mul)]
        public void Zero_immediate_numeric_ops_resolve(string mnemonic, OpCode expected)
        {
            var m = ParseWithFunc(mnemonic);
            var insts = m.Funcs[0].Body.Instructions.ToList();
            Assert.Equal(expected, insts[0].Op.x00);
        }

        // ---- Ref ---------------------------------------------------------

        [Fact]
        public void Ref_null_and_ref_is_null()
        {
            var m = ParseWithFunc("ref.null func ref.is_null");
            var insts = m.Funcs[0].Body.Instructions.ToList();
            Assert.Equal((ByteCode)OpCode.RefNull,   insts[0].Op);
            Assert.Equal((ByteCode)OpCode.RefIsNull, insts[1].Op);
        }

        // ---- Error paths -------------------------------------------------

        [Fact]
        public void Unknown_opcode_in_body_throws()
        {
            Assert.Throws<NotSupportedException>(() =>
                ParseWithFunc("i32.atomic.load"));
        }

        [Fact]
        public void Unknown_local_name_throws()
        {
            Assert.Throws<FormatException>(() =>
                ParseWithFunc("local.get $missing",
                    signature: "(func (param i32))"));
        }
    }
}
