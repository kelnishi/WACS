// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.IO;
using System.Linq;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Smoke + sample-based tests over the pinned threads-proposal
    /// atomic.wast snapshot at <c>Spec.Test/Data/threads/atomic.wast</c>.
    /// Upstream pin: WebAssembly/threads@f521d7b3.
    /// <para>
    /// Structure:
    /// 1. Parse-the-whole-file smoke test (script parser round-trip).
    /// 2. Instantiate the first module and invoke a representative sample
    ///    of <c>(assert_return …)</c> cases — load/store, RMW, cmpxchg,
    ///    subword — through the polymorphic runtime.
    /// 3. Spot-check the spec's <c>(assert_invalid)</c> alignment cases
    ///    by building the inner module and asserting validation fails.
    /// </para>
    /// </summary>
    public class SpecWastThreadsTests
    {
        private static string FindWastFile()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            if (dir == null) return string.Empty;
            return Path.Combine(dir.FullName, "Spec.Test", "Data", "threads", "atomic.wast");
        }

        [Fact]
        public void Full_file_parses_as_sexpr()
        {
            var path = FindWastFile();
            Assert.True(File.Exists(path), $"missing pinned snapshot: {path}");
            var src = File.ReadAllText(path);
            var top = SExprParser.Parse(src);
            Assert.NotEmpty(top);
            foreach (var node in top)
                Assert.Equal(SExprKind.List, node.Kind);
        }

        [Fact]
        public void Full_file_parses_through_script_parser()
        {
            var path = FindWastFile();
            var src = File.ReadAllText(path);
            var cmds = TextScriptParser.ParseWast(src);
            Assert.NotNull(cmds);
            // Should contain at least one Module command and many asserts.
            Assert.NotEmpty(cmds);
        }

        [Fact]
        public void Atomic_wast_first_module_runs_load_store()
        {
            // The first (module …) in the file defines a shared-memory
            // module exporting helpers named "init", "i32.atomic.load",
            // "i32.atomic.store", etc. Reach into the script, grab that
            // module, and exercise a handful of the spec's assert_return
            // cases.
            var path = FindWastFile();
            var src = File.ReadAllText(path);
            var cmds = TextScriptParser.ParseWast(src);

            var firstModuleCmd = cmds.OfType<ScriptModule>().First();
            Assert.NotNull(firstModuleCmd.Module);
            var module = firstModuleCmd.Module!;

            var runtime = new WasmRuntime(new RuntimeAttributes
            {
                ConcurrencyPolicy = new NotSupportedPolicy()
            });
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);

            int InvokeI32(string name, params Value[] args)
            {
                var addr = runtime.GetExportedFunction(("M", name));
                var inv = runtime.CreateStackInvoker(addr);
                return (int)inv(args)[0];
            }
            long InvokeI64(string name, params Value[] args)
            {
                var addr = runtime.GetExportedFunction(("M", name));
                var inv = runtime.CreateStackInvoker(addr);
                return (long)inv(args)[0];
            }
            void InvokeVoid(string name, params Value[] args)
            {
                var addr = runtime.GetExportedFunction(("M", name));
                var inv = runtime.CreateStackInvoker(addr);
                inv(args);
            }

            // Per the spec file: (init 0x0706050403020100)
            // then i32.atomic.load at addr 0 returns 0x03020100,
            //      i32.atomic.load at addr 4 returns 0x07060504.
            InvokeVoid("init", (long)0x0706050403020100L);
            Assert.Equal(0x03020100, InvokeI32("i32.atomic.load", 0));
            Assert.Equal(0x07060504, InvokeI32("i32.atomic.load", 4));

            Assert.Equal(0x0706050403020100L, InvokeI64("i64.atomic.load", 0));
            Assert.Equal(0x00, InvokeI32("i32.atomic.load8_u", 0));
            Assert.Equal(0x05, InvokeI32("i32.atomic.load8_u", 5));
            Assert.Equal(0x0100, InvokeI32("i32.atomic.load16_u", 0));
            Assert.Equal(0x0706, InvokeI32("i32.atomic.load16_u", 6));

            // Per the spec file, memory is re-init'd to zero before the
            // store section.
            InvokeVoid("init", 0L);
            InvokeVoid("i32.atomic.store", 0, unchecked((int)0xffeeddcc));
            Assert.Equal(unchecked((long)0x00000000ffeeddccL),
                InvokeI64("i64.atomic.load", 0));

            // i64 store replaces the whole cell.
            InvokeVoid("i64.atomic.store", 0, unchecked((long)0x0123456789abcdefL));
            Assert.Equal(unchecked((long)0x0123456789abcdefL),
                InvokeI64("i64.atomic.load", 0));

            // Subword store: i32.atomic.store8 at addr 1 writes 0x42
            // over byte 1.
            InvokeVoid("i32.atomic.store8", 1, 0x42);
            Assert.Equal(unchecked((long)0x0123456789ab42efL),
                InvokeI64("i64.atomic.load", 0));
        }

        [Fact]
        public void Invalid_alignment_module_fails_validation()
        {
            // Mirrors the spec's (assert_invalid) cases: atomic ops with
            // sub-natural alignment must fail validation. Build the inner
            // module directly to assert the validator rejects it.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""bad"") (param $a i32) (result i32)
                    local.get $a
                    i32.atomic.load align=1))";
            var module = TextModuleParser.ParseWat(src);
            var runtime = new WasmRuntime();
            Assert.ThrowsAny<System.Exception>(() => runtime.InstantiateModule(module));
        }
    }
}
