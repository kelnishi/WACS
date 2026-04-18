// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.Core;
using Wacs.Core.Compilation;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types.Defs;
using Xunit;

namespace Wacs.Compilation.Test
{
    /// <summary>
    /// Full-pipeline tests: parse a minimal hand-built .wasm blob, instantiate via
    /// <see cref="WasmRuntime"/>, then invoke the exported function through
    /// <see cref="SwitchRuntime"/>. Exercises the BytecodeCompiler on a body that
    /// came through the real <c>BinaryModuleParser</c> and <c>Link</c> pass — not a
    /// hand-constructed InstructionBase[].
    /// </summary>
    public class EndToEndTests
    {
        /// <summary>
        /// Minimal valid WebAssembly module exporting one function:
        /// <code>(func (export "add") (param i32 i32) (result i32) local.get 0; local.get 1; i32.add)</code>
        /// 41 bytes. Built by hand so the test doesn't depend on external .wasm files.
        /// </summary>
        private static readonly byte[] AddModule =
        {
            // Magic + version
            0x00, 0x61, 0x73, 0x6D,  0x01, 0x00, 0x00, 0x00,
            // Type section: 1 type — (i32 i32) -> i32
            0x01, 0x07, 0x01,  0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            // Function section: 1 function of type 0
            0x03, 0x02, 0x01, 0x00,
            // Export section: "add" → func 0
            0x07, 0x07, 0x01, 0x03, (byte)'a', (byte)'d', (byte)'d', 0x00, 0x00,
            // Code section: 1 body, 7 bytes total: 0 locals, local.get 0, local.get 1, i32.add, end
            0x0A, 0x09, 0x01, 0x07, 0x00,  0x20, 0x00, 0x20, 0x01, 0x6A, 0x0B,
        };

        [Fact]
        public void SwitchRuntime_invokes_parsed_wasm_function()
        {
            var runtime = new WasmRuntime();
            using var ms = new MemoryStream(AddModule);
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("m", moduleInst);

            var addr = runtime.GetExportedFunction(("m", "add"));
            var funcInst = (FunctionInstance)runtime.RuntimeStore[addr];

            var ctx = new ExecContext(runtime.RuntimeStore);
            var results = SwitchRuntime.Invoke(ctx, funcInst,
                new Value(ValType.I32, 2),
                new Value(ValType.I32, 3));

            Assert.Single(results);
            Assert.Equal(5, results[0].Data.Int32);
        }

        /// <summary>
        /// Minimal WASM module exporting "block_ret":
        /// <code>
        /// (func (export "block_ret") (result i32)
        ///   (block (result i32)
        ///     i32.const 7
        ///     br 0       ;; branch to end of the block, carrying i32 on the stack
        ///     i32.const 99  ;; dead
        ///   )
        /// )
        /// </code>
        /// Exercises br carrying a result value across a block exit.
        /// </summary>
        private static readonly byte[] BrBlockModule =
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type: () -> i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // Func 0 of type 0
            0x03, 0x02, 0x01, 0x00,
            // Export "block_ret" -> func 0
            0x07, 0x0D, 0x01, 0x09, (byte)'b', (byte)'l', (byte)'o', (byte)'c', (byte)'k', (byte)'_', (byte)'r', (byte)'e', (byte)'t', 0x00, 0x00,
            // Code:
            //   body: 0 locals, block i32 { i32.const 7; br 0; i32.const 99 }; end
            //     0x00        = locals vec count
            //     0x02 0x7F   = block (result i32)
            //     0x41 0x07   = i32.const 7
            //     0x0C 0x00   = br 0
            //     0x41 0x63   = i32.const 99  (99 = 0x63, single-byte LEB128 s32)
            //     0x0B        = end (block)
            //     0x0B        = end (func)
            //   → 11 body bytes; body-size prefix 0x0B; 1 (count) + 1 (size) + 11 = 13 section body bytes; section size 0x0D.
            0x0A, 0x0D, 0x01, 0x0B, 0x00,
            0x02, 0x7F,
            0x41, 0x07,
            0x0C, 0x00,
            0x41, 0x63,
            0x0B,
            0x0B,
        };

        [Fact]
        public void Br_exits_block_with_result()
        {
            var runtime = new WasmRuntime();
            using var ms = new MemoryStream(BrBlockModule);
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("m", moduleInst);

            var addr = runtime.GetExportedFunction(("m", "block_ret"));
            var funcInst = (FunctionInstance)runtime.RuntimeStore[addr];

            var ctx = new ExecContext(runtime.RuntimeStore);
            var results = SwitchRuntime.Invoke(ctx, funcInst);

            Assert.Single(results);
            // br 0 from inside the block exits carrying the top i32 (7); the i32.const 99
            // after the br is dead code and never pushed.
            Assert.Equal(7, results[0].Data.Int32);
        }

        /// <summary>
        /// (func (export "abs") (param i32) (result i32)
        ///   local.get 0
        ///   i32.const 0
        ///   i32.lt_s
        ///   if (result i32)
        ///     i32.const 0
        ///     local.get 0
        ///     i32.sub
        ///   else
        ///     local.get 0
        ///   end)
        /// </summary>
        private static readonly byte[] IfElseModule =
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type: (i32) -> i32
            0x01, 0x06, 0x01, 0x60, 0x01, 0x7F, 0x01, 0x7F,
            // Func 0 of type 0
            0x03, 0x02, 0x01, 0x00,
            // Export "abs" → func 0
            0x07, 0x07, 0x01, 0x03, (byte)'a', (byte)'b', (byte)'s', 0x00, 0x00,
            // Code: body bytes:
            //   00           locals vec count (0)
            //   20 00        local.get 0
            //   41 00        i32.const 0
            //   48           i32.lt_s
            //   04 7F        if (result i32)
            //     41 00        i32.const 0
            //     20 00        local.get 0
            //     6B           i32.sub
            //   05           else
            //     20 00        local.get 0
            //   0B           end (of if)
            //   0B           end (of func)
            // 18 body bytes; body-size 0x12; section body = 1+1+18 = 20; section size 0x14.
            0x0A, 0x14, 0x01, 0x12, 0x00,
            0x20, 0x00,
            0x41, 0x00,
            0x48,
            0x04, 0x7F,
            0x41, 0x00,
            0x20, 0x00,
            0x6B,
            0x05,
            0x20, 0x00,
            0x0B,
            0x0B,
        };

        [Theory]
        [InlineData(5, 5)]
        [InlineData(-3, 3)]
        [InlineData(0, 0)]
        public void IfElse_computes_abs(int input, int expected)
        {
            var runtime = new WasmRuntime();
            using var ms = new MemoryStream(IfElseModule);
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("m", moduleInst);

            var addr = runtime.GetExportedFunction(("m", "abs"));
            var funcInst = (FunctionInstance)runtime.RuntimeStore[addr];

            var ctx = new ExecContext(runtime.RuntimeStore);
            var results = SwitchRuntime.Invoke(ctx, funcInst, new Value(ValType.I32, input));

            Assert.Single(results);
            Assert.Equal(expected, results[0].Data.Int32);
        }

        /// <summary>
        /// (func (export "countdown") (param i32) (result i32)
        ///   (local i32)            ;; sum accumulator starts at 0
        ///   (loop
        ///     local.get 0          ;; n
        ///     local.get 1          ;; sum
        ///     i32.add              ;; sum = sum + n
        ///     local.set 1
        ///     local.get 0          ;; n
        ///     i32.const 1
        ///     i32.sub
        ///     local.tee 0          ;; n = n - 1, leave on stack
        ///     br_if 0              ;; if n != 0, loop
        ///   )
        ///   local.get 1            ;; return sum
        /// )
        /// Sum of 1..n via br_if + loop. countdown(5) = 15.
        /// </summary>
        private static readonly byte[] BrIfLoopModule =
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type: (i32) -> i32
            0x01, 0x06, 0x01, 0x60, 0x01, 0x7F, 0x01, 0x7F,
            // Func 0
            0x03, 0x02, 0x01, 0x00,
            // Export "countdown" -> func 0
            0x07, 0x0D, 0x01, 0x09, (byte)'c', (byte)'o', (byte)'u', (byte)'n', (byte)'t', (byte)'d', (byte)'o', (byte)'w', (byte)'n', 0x00, 0x00,
            // Code body:
            //   01 01 7F                1 local run: 1 × i32  (3 bytes)
            //   03 40                   loop (empty type)     (2)
            //     20 00                  local.get 0           (2)
            //     20 01                  local.get 1           (2)
            //     6A                     i32.add               (1)
            //     21 01                  local.set 1           (2)
            //     20 00                  local.get 0           (2)
            //     41 01                  i32.const 1           (2)
            //     6B                     i32.sub               (1)
            //     22 00                  local.tee 0           (2)
            //     0D 00                  br_if 0               (2)
            //   0B                      end (loop)             (1)
            //   20 01                   local.get 1            (2)
            //   0B                      end (func)             (1)
            // 25 body bytes; body-size 0x19; section body = 1+1+25 = 27; section size 0x1B.
            0x0A, 0x1B, 0x01, 0x19,
            0x01, 0x01, 0x7F,
            0x03, 0x40,
            0x20, 0x00,
            0x20, 0x01,
            0x6A,
            0x21, 0x01,
            0x20, 0x00,
            0x41, 0x01,
            0x6B,
            0x22, 0x00,
            0x0D, 0x00,
            0x0B,
            0x20, 0x01,
            0x0B,
        };

        [Theory]
        [InlineData(5, 15)]   // 5+4+3+2+1 = 15
        [InlineData(1, 1)]    // just 1
        [InlineData(10, 55)]
        public void BrIf_loop_sums_countdown(int n, int expected)
        {
            var runtime = new WasmRuntime();
            using var ms = new MemoryStream(BrIfLoopModule);
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("m", moduleInst);

            var addr = runtime.GetExportedFunction(("m", "countdown"));
            var funcInst = (FunctionInstance)runtime.RuntimeStore[addr];

            var ctx = new ExecContext(runtime.RuntimeStore);
            var results = SwitchRuntime.Invoke(ctx, funcInst, new Value(ValType.I32, n));

            Assert.Single(results);
            Assert.Equal(expected, results[0].Data.Int32);
        }

        /// <summary>
        /// (func (export "classify") (param i32) (result i32)
        ///   (block (block (block
        ///     local.get 0
        ///     br_table 0 1 2  ;; 0 → exit innermost (returns 100), 1 → middle (200), default 2 → outer (300)
        ///   )) i32.const 100 return )
        ///   end i32.const 200 return
        ///   end i32.const 300 return
        /// )
        /// Classic br_table dispatch: three nested blocks, table selects which to exit.
        /// </summary>
        private static readonly byte[] BrTableModule =
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type (i32) -> i32
            0x01, 0x06, 0x01, 0x60, 0x01, 0x7F, 0x01, 0x7F,
            // Func
            0x03, 0x02, 0x01, 0x00,
            // Export "classify"
            0x07, 0x0C, 0x01, 0x08, (byte)'c', (byte)'l', (byte)'a', (byte)'s', (byte)'s', (byte)'i', (byte)'f', (byte)'y', 0x00, 0x00,
            // Code body:
            //   00                  locals (1 byte)
            //   02 40               outer block (2)
            //   02 40               middle block (2)
            //   02 40               inner block (2)
            //   20 00               local.get 0 (2)
            //   0E 02 00 01 02      br_table [0 1] default 2 (5)
            //   0B                  end (inner) (1)
            //   41 E4 00            i32.const 100 (3)
            //   0F                  return (1)
            //   0B                  end (middle) (1)
            //   41 C8 01            i32.const 200 (3)
            //   0F                  return (1)
            //   0B                  end (outer) (1)
            //   41 AC 02            i32.const 300 (3)
            //   0F                  return (1)
            //   0B                  end (func) (1)
            // 30 body bytes; body-size 0x1E; section body = 1+1+30 = 32; section size 0x20.
            0x0A, 0x20, 0x01, 0x1E,
            0x00,
            0x02, 0x40,
            0x02, 0x40,
            0x02, 0x40,
            0x20, 0x00,
            0x0E, 0x02, 0x00, 0x01, 0x02,
            0x0B,
            0x41, 0xE4, 0x00,
            0x0F,
            0x0B,
            0x41, 0xC8, 0x01,
            0x0F,
            0x0B,
            0x41, 0xAC, 0x02,
            0x0F,
            0x0B,
        };

        [Theory]
        [InlineData(0, 100)]
        [InlineData(1, 200)]
        [InlineData(2, 300)]
        [InlineData(42, 300)]   // out-of-range → default
        [InlineData(-1, 300)]   // negative also out-of-range
        public void BrTable_dispatches_by_index(int selector, int expected)
        {
            var runtime = new WasmRuntime();
            using var ms = new MemoryStream(BrTableModule);
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("m", moduleInst);

            var addr = runtime.GetExportedFunction(("m", "classify"));
            var funcInst = (FunctionInstance)runtime.RuntimeStore[addr];

            var ctx = new ExecContext(runtime.RuntimeStore);
            var results = SwitchRuntime.Invoke(ctx, funcInst, new Value(ValType.I32, selector));

            Assert.Single(results);
            Assert.Equal(expected, results[0].Data.Int32);
        }
    }
}
