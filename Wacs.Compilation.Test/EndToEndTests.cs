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
    }
}
