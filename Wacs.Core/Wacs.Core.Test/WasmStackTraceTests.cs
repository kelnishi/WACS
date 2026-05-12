// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Text;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Pass C coverage: <see cref="WasmStackTrace.Format"/> and
    /// <see cref="WasmStackTrace.FormatVerbose"/> render a captured
    /// <see cref="WasmStackFrame"/> chain to a human-readable trace.
    /// Cheap form runs purely on the captured fields; verbose form
    /// resolves source coords via <see cref="Module.SourcePositions"/>
    /// when available.
    /// </summary>
    public class WasmStackTraceTests
    {
        private static (Module module, WasmRuntime runtime, TrapException trap)
            RunTrap(string wat, string export)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wat));
            var module = TextModuleParser.ParseWat(ms);
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
        public void Format_TopFrame_IncludesMnemonicAndOffset()
        {
            const string wat = @"(module
              (func (export ""bad"") unreachable))";
            var (module, runtime, trap) = RunTrap(wat, "bad");
            Assert.NotNull(trap.WasmFrames);

            string trace = WasmStackTrace.Format(trap.WasmFrames!, module, runtime.RuntimeStore);

            // Cheap form must mention the trap instruction's mnemonic
            // and a byte-offset suffix.
            Assert.Contains("unreachable", trace);
            Assert.Contains("@+0x", trace);
        }

        [Fact]
        public void FormatVerbose_WatParsedModule_AddsSourceLine()
        {
            // WAT-parsed modules carry Module.SourcePositions from
            // Pass B. FormatVerbose surfaces the (line:col) suffix.
            const string wat = @"(module
              (func (export ""bad"")
                unreachable))";
            var (module, runtime, trap) = RunTrap(wat, "bad");
            Assert.NotNull(trap.WasmFrames);

            string verbose = WasmStackTrace.FormatVerbose(
                trap.WasmFrames!, module, runtime.RuntimeStore);

            // `unreachable` is on line 3 of the source, column 17
            // (1-based). Don't pin the exact column since it depends
            // on the leading whitespace; just check the line is there.
            Assert.Contains("(3:", verbose);
        }

        [Fact]
        public void FormatVerbose_BinaryParsedModule_ResolvesViaLineMap()
        {
            // Pass G. Binary-parsed modules have no SourcePositions,
            // but a caller-supplied LineMap (from a canonical re-
            // render) lets the formatter still surface (line:col).
            const string wat = @"(module
              (func (export ""bad"")
                unreachable))";

            // Build a binary copy of the WAT module, re-parse it
            // through the binary parser, and instantiate that.
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wat));
            var watModule = TextModuleParser.ParseWat(ms);
            var bytes = Wacs.Core.Bin.BinaryModuleWriter.Write(watModule);
            using var binStream = new MemoryStream(bytes);
            var binModule = BinaryModuleParser.ParseWasm(binStream);
            Assert.Null(binModule.SourcePositions);

            var runtime = new WasmRuntime();
            var inst = runtime.InstantiateModule(binModule);
            runtime.RegisterModule("m", inst);
            var entry = runtime.GetExportedFunction(("m", "bad"));
            var invoker = runtime.CreateStackInvoker(entry);
            var trap = Assert.Throws<TrapException>(() =>
                invoker(System.Array.Empty<Value>()));

            var (_, lineMap) = TextModuleWriter.WriteWithLineMap(binModule);
            string verbose = WasmStackTrace.FormatVerbose(
                trap.WasmFrames!, binModule, runtime.RuntimeStore, lineMap);

            // The trace must include a (line:col) suffix for the
            // `unreachable` frame — sourced from the re-rendered
            // LineMap, not from `Module.SourcePositions`.
            Assert.Contains("unreachable", verbose);
            Assert.Matches(@"\(\d+:\d+\)", verbose);
        }

        [Fact]
        public void Format_EmptyFrames_ReturnsSentinel()
        {
            // Use a parsed module + a fresh runtime as a stand-in
            // (Module's constructor is internal; the easiest live
            // pair is the smallest valid (module) form).
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes("(module)"));
            var module = TextModuleParser.ParseWat(ms);
            var runtime = new WasmRuntime();
            runtime.InstantiateModule(module);

            string trace = WasmStackTrace.Format(
                System.Array.Empty<WasmStackFrame>(),
                module,
                runtime.RuntimeStore);
            Assert.Equal("<empty WASM stack>", trace);
        }
    }
}
