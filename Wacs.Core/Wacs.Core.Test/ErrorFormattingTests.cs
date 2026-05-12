// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Linq;
using System.Text;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Every trap that escapes the interpreter's dispatch loop
    /// carries a populated <c>WasmFrames</c> snapshot, regardless of
    /// where in the runtime it was thrown. Most trap sites construct
    /// <see cref="TrapException"/> without frames; the dispatch
    /// loop's outer catch fills them in retroactively from the
    /// live <see cref="ExecContext"/>.
    /// </summary>
    public class ErrorFormattingTests
    {
        private static Module Parse(string wat)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wat));
            return TextModuleParser.ParseWat(ms);
        }

        private static (Module module, WasmRuntime runtime, TrapException trap)
            RunTrap(string wat, string export)
        {
            var module = Parse(wat);
            var runtime = new WasmRuntime();
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("m", inst);
            var entry = runtime.GetExportedFunction(("m", export));
            var invoker = runtime.CreateStackInvoker(entry);
            var trap = Assert.Throws<TrapException>(() =>
                invoker(System.Array.Empty<Value>()));
            return (module, runtime, trap);
        }

        [Fact]
        public void Unreachable_HasFrameCapture()
        {
            // InstUnreachable throws via the snapshot-aware
            // TrapException constructor directly. Verifies that path
            // produces a populated WasmFrames chain.
            var (_, _, trap) = RunTrap(
                "(module (func (export \"x\") unreachable))",
                "x");
            Assert.NotNull(trap.WasmFrames);
            Assert.True(trap.WasmFrames!.Length >= 1);
            var top = trap.WasmFrames[0];
            Assert.NotNull(top.Instruction);
            Assert.Equal("unreachable", top.Instruction!.Op.GetMnemonic());
        }

        [Fact]
        public void DivByZero_AutoEnrichedByDispatchLoop()
        {
            // i32.div_s with a zero divisor throws via the legacy
            // `throw new TrapException("...")` constructor — no
            // WasmFrames at construction time. The dispatch loop's
            // outer catch retroactively populates them.
            var (_, _, trap) = RunTrap(@"(module
                (func (export ""x"") (result i32)
                  i32.const 10
                  i32.const 0
                  i32.div_s))", "x");
            Assert.NotNull(trap.WasmFrames);
            var top = trap.WasmFrames![0];
            Assert.NotNull(top.Instruction);
            // The exact instance is the executing instruction —
            // an InstI32BinOp variant; just check the mnemonic.
            Assert.Equal("i32.div_s", top.Instruction!.Op.GetMnemonic());
        }

        [Fact]
        public void MemoryOutOfBounds_AutoEnriched()
        {
            // Memory load past the end traps via a non-migrated
            // throw site. The dispatch loop fills WasmFrames.
            var (_, _, trap) = RunTrap(@"(module
                (memory 1)
                (func (export ""x"") (result i32)
                  i32.const 999999999
                  i32.load))", "x");
            Assert.NotNull(trap.WasmFrames);
            Assert.Equal("i32.load",
                trap.WasmFrames![0].Instruction!.Op.GetMnemonic());
        }

        [Fact]
        public void CallChain_FramesIncludeIntermediateCallers()
        {
            // A trap inside a callee should produce a frame chain
            // with the caller frames present, in inner-most-first
            // order.
            var (_, _, trap) = RunTrap(@"(module
                (func $inner unreachable)
                (func $outer (call $inner))
                (func (export ""x"") (call $outer)))", "x");
            Assert.NotNull(trap.WasmFrames);
            Assert.True(trap.WasmFrames!.Length >= 3,
                $"expected ≥3 frames, got {trap.WasmFrames.Length}");
            // Top frame is $inner with the unreachable instruction.
            Assert.Equal("unreachable",
                trap.WasmFrames[0].Instruction!.Op.GetMnemonic());
            // Caller frames don't carry instruction refs in the
            // cheap snapshot — they carry resume PCs instead. The
            // formatter reports the PC rather than trying to resolve
            // it to a source-level instruction.
            Assert.Null(trap.WasmFrames[1].Instruction);
            Assert.Null(trap.WasmFrames[2].Instruction);
        }

        [Fact]
        public void FormatVerbose_IncludesSourceLineFromSourcePositions()
        {
            // WAT-parsed modules have Module.SourcePositions
            // populated. FormatVerbose surfaces `(line:col)`.
            var (module, runtime, trap) = RunTrap(@"(module
                (func (export ""x"") (result i32)
                  i32.const 7
                  i32.const 0
                  i32.div_s))", "x");
            string trace = WasmStackTrace.FormatVerbose(
                trap.WasmFrames!, module, runtime.RuntimeStore);

            // The i32.div_s instruction was on line 5 of the source.
            Assert.Contains("i32.div_s", trace);
            Assert.Contains("(5:", trace);
        }

        [Fact]
        public void Format_FunctionLabel_UsesParsedDollarName()
        {
            // The CLI / formatter should print the WAT $name on
            // function labels — not "func@<addr>". Requires the
            // WAT parser → FunctionInstance.Id propagation landed
            // in the WAT-name follow-up.
            var (module, runtime, trap) = RunTrap(@"(module
                (func $impl unreachable)
                (func (export ""x"") (call $impl)))", "x");
            string trace = WasmStackTrace.Format(
                trap.WasmFrames!, module, runtime.RuntimeStore);
            Assert.Contains("$impl", trace);
        }
    }
}
