// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Linq;
using Wacs.Core;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Text;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Phase 2 round-trip tests. Parse WAT → render → re-parse and compare
    /// the two resulting <see cref="Module"/> objects for structural parity.
    /// Equality is asserted via field-level inspection rather than binary
    /// encoding since WACS doesn't have a WASM binary writer.
    /// </summary>
    public class TextRoundTripTests
    {
        private static Module RoundTrip(string wat, out string rendered)
        {
            var m1 = TextModuleParser.ParseWat(wat);
            rendered = TextModuleWriter.Write(m1);
            var m2 = TextModuleParser.ParseWat(rendered);
            return m2;
        }

        // ---- Types --------------------------------------------------------

        [Fact]
        public void Empty_module()
        {
            var m = RoundTrip("(module)", out var s);
            Assert.NotNull(m);
            Assert.Empty(m.Types);
            Assert.Empty(m.Imports);
            Assert.Empty(m.Funcs);
        }

        [Fact]
        public void Type_section_preserves_signatures()
        {
            var m = RoundTrip(@"
                (module
                  (type (func))
                  (type (func (param i32) (result i32)))
                  (type (func (param i32 i64) (result f32 f64))))",
                out var _);
            Assert.Equal(3, m.Types.Count);

            var t1 = (FunctionType)m.Types[1];
            Assert.Equal(1, t1.ParameterTypes.Arity);
            Assert.Equal(ValType.I32, t1.ParameterTypes.Types[0]);
            Assert.Equal(ValType.I32, t1.ResultType.Types[0]);

            var t2 = (FunctionType)m.Types[2];
            Assert.Equal(new[] { ValType.I32, ValType.I64 }, t2.ParameterTypes.Types);
            Assert.Equal(new[] { ValType.F32, ValType.F64 }, t2.ResultType.Types);
        }

        // ---- Imports / Exports --------------------------------------------

        [Fact]
        public void Imports_preserved()
        {
            var m = RoundTrip(@"
                (module
                  (type (func (param i32)))
                  (import ""env"" ""logger"" (func (type 0)))
                  (import ""env"" ""mem"" (memory 1 2))
                  (import ""env"" ""tbl"" (table 0 funcref))
                  (import ""env"" ""g"" (global (mut i64))))",
                out var _);
            Assert.Equal(4, m.Imports.Length);
            Assert.Equal("env", m.Imports[0].ModuleName);
            Assert.Equal("logger", m.Imports[0].Name);
            var md = Assert.IsType<Module.ImportDesc.MemDesc>(m.Imports[1].Desc);
            Assert.Equal(1L, md.MemDef.Limits.Minimum);
            Assert.Equal(2L, md.MemDef.Limits.Maximum);
        }

        [Fact]
        public void Exports_preserved()
        {
            var m = RoundTrip(@"
                (module
                  (type (func))
                  (func (type 0))
                  (memory 1)
                  (global $g i32 (i32.const 0))
                  (export ""f"" (func 0))
                  (export ""m"" (memory 0))
                  (export ""g"" (global 0)))",
                out var _);
            Assert.Equal(3, m.Exports.Length);
            Assert.Equal("f", m.Exports[0].Name);
            Assert.Equal("m", m.Exports[1].Name);
            Assert.Equal("g", m.Exports[2].Name);
        }

        // ---- Functions + bodies ------------------------------------------

        [Fact]
        public void Function_body_roundtrips_consts_and_numeric()
        {
            var m = RoundTrip(@"
                (module
                  (type (func (result i32)))
                  (func (type 0)
                    i32.const 1
                    i32.const 2
                    i32.add))",
                out var _);
            var body = m.Funcs[0].Body.Instructions.ToList();
            Assert.Equal((ByteCode)OpCode.I32Const, body[0].Op);
            Assert.Equal(1, ((InstI32Const)body[0]).Value);
            Assert.Equal((ByteCode)OpCode.I32Const, body[1].Op);
            Assert.Equal(2, ((InstI32Const)body[1]).Value);
            Assert.Equal((ByteCode)OpCode.I32Add, body[2].Op);
        }

        [Fact]
        public void Function_body_roundtrips_locals_and_globals()
        {
            var m = RoundTrip(@"
                (module
                  (type (func (param i32) (result i32)))
                  (global $g (mut i32) (i32.const 0))
                  (func (type 0) (param $x i32) (result i32)
                    local.get $x
                    global.get $g
                    i32.add))",
                out var _);
            var body = m.Funcs[0].Body.Instructions.ToList();
            Assert.Equal((ByteCode)OpCode.LocalGet, body[0].Op);
            Assert.Equal((ByteCode)OpCode.GlobalGet, body[1].Op);
            Assert.Equal((ByteCode)OpCode.I32Add, body[2].Op);
        }

        [Fact]
        public void Function_body_roundtrips_block()
        {
            var m = RoundTrip(@"
                (module
                  (type (func))
                  (func (type 0)
                    block
                      nop
                    end
                    block (result i32)
                      i32.const 42
                    end
                    drop))",
                out var _);
            var body = m.Funcs[0].Body.Instructions.ToList();
            Assert.IsType<InstBlock>(body[0]);
            Assert.IsType<InstBlock>(body[1]);
            Assert.Equal((ByteCode)OpCode.Drop, body[2].Op);
        }

        [Fact]
        public void Function_body_roundtrips_if_else()
        {
            var m = RoundTrip(@"
                (module
                  (type (func (param i32) (result i32)))
                  (func (type 0) (param $x i32) (result i32)
                    local.get $x
                    if (result i32)
                      i32.const 1
                    else
                      i32.const 2
                    end))",
                out var _);
            var body = m.Funcs[0].Body.Instructions.ToList();
            Assert.Equal((ByteCode)OpCode.LocalGet, body[0].Op);
            var iif = Assert.IsType<InstIf>(body[1]);
            Assert.Equal(2, ((IBlockInstruction)iif).Count);   // then + else blocks
        }

        [Fact]
        public void Function_body_roundtrips_loop_and_br()
        {
            var m = RoundTrip(@"
                (module
                  (type (func))
                  (func (type 0)
                    block $outer
                      loop $inner
                        br $outer
                      end
                    end))",
                out var _);
            var body = m.Funcs[0].Body.Instructions.ToList();
            Assert.IsType<InstBlock>(body[0]);
        }

        // ---- Tables / Memories / Globals ---------------------------------

        [Fact]
        public void Tables_memories_globals_preserved()
        {
            var m = RoundTrip(@"
                (module
                  (table 10 funcref)
                  (memory 1 2)
                  (global (mut f64) (f64.const 3.14)))",
                out var _);
            Assert.Single(m.Tables);
            Assert.Equal(10L, m.Tables[0].Limits.Minimum);
            Assert.Equal(ValType.FuncRef, m.Tables[0].ElementType);
            Assert.Single(m.Memories);
            Assert.Equal(1L, m.Memories[0].Limits.Minimum);
            Assert.Equal(2L, m.Memories[0].Limits.Maximum);
            Assert.Single(m.Globals);
            Assert.Equal(ValType.F64, m.Globals[0].Type.ContentType);
            Assert.Equal(Mutability.Mutable, m.Globals[0].Type.Mutability);
        }

        // ---- Start --------------------------------------------------------

        [Fact]
        public void Start_index_preserved()
        {
            var m = RoundTrip(@"
                (module
                  (type (func))
                  (func (type 0))
                  (start 0))",
                out var _);
            Assert.Equal(0u, (uint)m.StartIndex.Value);
        }

        // ---- Rendered form is valid WAT ----------------------------------

        [Fact]
        public void Rendered_output_is_parseable()
        {
            var src = @"
                (module
                  (type (func (param i32) (result i32)))
                  (func (type 0) (param $x i32) (result i32)
                    local.get $x
                    i32.const 1
                    i32.add))";
            var m1 = TextModuleParser.ParseWat(src);
            var rendered = TextModuleWriter.Write(m1);
            // Log the rendered form for debug context on failure.
            System.Console.WriteLine("---- rendered ----");
            System.Console.WriteLine(rendered);
            System.Console.WriteLine("---- /rendered ----");
            // Re-parsing the rendered form must not throw.
            var reparsed = TextModuleParser.ParseWat(rendered);
            Assert.NotNull(reparsed);
        }

        // ---- Structural parity: parse/render/parse yields == types -------

        [Fact]
        public void Round_trip_preserves_module_shape()
        {
            var src = @"
                (module
                  (type (func))
                  (type (func (param i32 i64) (result f32)))
                  (import ""m"" ""n"" (func (type 0)))
                  (func (type 1) (param i32 i64) (result f32)
                    f32.const 0.5)
                  (memory 1)
                  (global i32 (i32.const 42))
                  (export ""f"" (func 1)))";
            var m1 = TextModuleParser.ParseWat(src);
            var rendered = TextModuleWriter.Write(m1);
            var m2 = TextModuleParser.ParseWat(rendered);

            Assert.Equal(m1.Types.Count, m2.Types.Count);
            Assert.Equal(m1.Imports.Length, m2.Imports.Length);
            Assert.Equal(m1.Funcs.Count, m2.Funcs.Count);
            Assert.Equal(m1.Memories.Count, m2.Memories.Count);
            Assert.Equal(m1.Globals.Count, m2.Globals.Count);
            Assert.Equal(m1.Exports.Length, m2.Exports.Length);
        }
    }
}
