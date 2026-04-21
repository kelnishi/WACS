// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Text;
using Wacs.Transpiler.AOT;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Phase-3 equivalence tests for atomic ops. Each test parses a small
    /// WAT source, instantiates the module on the polymorphic runtime, and
    /// transpiles it through <see cref="ModuleTranspiler"/>. Both back-ends
    /// must produce the same result for the same invocation — the whole
    /// point of phase 3 is correctness parity with phases 1 and 2.
    /// </summary>
    public class AtomicEquivalenceTests
    {
        private static (object Poly, object Aot) RunBoth(string src, string export, params Value[] args)
        {
            // Polymorphic path
            var polyRt = new WasmRuntime(new RuntimeAttributes
            {
                ConcurrencyPolicy = new NotSupportedPolicy(),
            });
            var module = TextModuleParser.ParseWat(src);
            var polyInst = polyRt.InstantiateModule(module);
            polyRt.RegisterModule("M", polyInst);
            var polyFunc = polyRt.GetExportedFunction(("M", export));
            var polyResult = polyRt.CreateStackInvoker(polyFunc)(args);
            object polyVal = polyResult.Length == 0 ? null! : (object)polyResult[0];

            // Transpiled path
            var aotRt = new WasmRuntime(new RuntimeAttributes
            {
                ConcurrencyPolicy = new NotSupportedPolicy(),
            });
            var module2 = TextModuleParser.ParseWat(src);
            var aotInst = aotRt.InstantiateModule(module2);
            aotRt.RegisterModule("M", aotInst);

            var transpiler = new ModuleTranspiler();
            var result = transpiler.Transpile(aotInst, aotRt);
            Assert.NotNull(result.ModuleClass);
            Assert.Equal(0, result.FallbackCount);

            var wrapper = new TranspiledModuleWrapper(result);
            wrapper.Instantiate();
            var aotResult = wrapper.InvokeExport(export, args);
            object aotVal = aotResult.Length == 0 ? null! : (object)aotResult[0];

            return (polyVal, aotVal);
        }

        private static int I32(Value v) => v.Data.Int32;
        private static long I64(Value v) => v.Data.Int64;

        [Fact]
        public void I32_load_store_round_trip_matches()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 0
                    i32.const 0x12345678
                    i32.atomic.store align=4
                    i32.const 0
                    i32.atomic.load align=4))";
            var (poly, aot) = RunBoth(src, "f");
            Assert.Equal(I32((Value)poly), I32((Value)aot));
            Assert.Equal(0x12345678, I32((Value)aot));
        }

        [Fact]
        public void I64_load_store_round_trip_matches()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i64)
                    i32.const 8
                    i64.const 0x0123456789ABCDEF
                    i64.atomic.store align=8
                    i32.const 8
                    i64.atomic.load align=8))";
            var (poly, aot) = RunBoth(src, "f");
            Assert.Equal(I64((Value)poly), I64((Value)aot));
            Assert.Equal(0x0123456789ABCDEFL, I64((Value)aot));
        }

        [Fact]
        public void Subword_load_store_matches()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 7
                    i32.const 0xA5
                    i32.atomic.store8 align=1
                    i32.const 7
                    i32.atomic.load8_u align=1))";
            var (poly, aot) = RunBoth(src, "f");
            Assert.Equal(I32((Value)poly), I32((Value)aot));
            Assert.Equal(0xA5, I32((Value)aot));
        }

        [Fact]
        public void Rmw_add_matches()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 0 i32.const 10 i32.atomic.store align=4
                    i32.const 0 i32.const 5 i32.atomic.rmw.add align=4))";
            var (poly, aot) = RunBoth(src, "f");
            Assert.Equal(I32((Value)poly), I32((Value)aot));
            Assert.Equal(10, I32((Value)aot));  // returns original
        }

        [Fact]
        public void Rmw_xchg_matches()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 0 i32.const 7 i32.atomic.store align=4
                    i32.const 0 i32.const 42 i32.atomic.rmw.xchg align=4))";
            var (poly, aot) = RunBoth(src, "f");
            Assert.Equal(I32((Value)poly), I32((Value)aot));
            Assert.Equal(7, I32((Value)aot));
        }

        [Fact]
        public void Subword_rmw_add_CAS_loop_matches()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 3 i32.const 100 i32.atomic.store8 align=1
                    i32.const 3 i32.const 56 i32.atomic.rmw8.add_u align=1))";
            var (poly, aot) = RunBoth(src, "f");
            Assert.Equal(I32((Value)poly), I32((Value)aot));
            Assert.Equal(100, I32((Value)aot));  // original
        }

        [Fact]
        public void Cmpxchg_match_and_mismatch()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""match"") (result i32)
                    i32.const 0 i32.const 50 i32.atomic.store align=4
                    i32.const 0 i32.const 50 i32.const 99 i32.atomic.rmw.cmpxchg align=4)
                  (func (export ""miss"") (result i32)
                    i32.const 4 i32.const 50 i32.atomic.store align=4
                    i32.const 4 i32.const 999 i32.const 99 i32.atomic.rmw.cmpxchg align=4))";
            var (polyM, aotM) = RunBoth(src, "match");
            Assert.Equal(I32((Value)polyM), I32((Value)aotM));
            Assert.Equal(50, I32((Value)aotM));

            var (polyF, aotF) = RunBoth(src, "miss");
            Assert.Equal(I32((Value)polyF), I32((Value)aotF));
            Assert.Equal(50, I32((Value)aotF));
        }

        [Fact]
        public void Fence_executes_and_matches()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    atomic.fence
                    i32.const 42))";
            var (poly, aot) = RunBoth(src, "f");
            Assert.Equal(I32((Value)poly), I32((Value)aot));
            Assert.Equal(42, I32((Value)aot));
        }

        [Fact]
        public void Wait_mismatch_returns_2()
        {
            // NotSupported policy: wait with mismatched expected → 2.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 0
                    i32.const 99
                    i64.const 0
                    memory.atomic.wait32 align=4))";
            var (poly, aot) = RunBoth(src, "f");
            Assert.Equal(I32((Value)poly), I32((Value)aot));
            Assert.Equal(2, I32((Value)aot));
        }

        [Fact]
        public void Notify_with_no_waiters_returns_0()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 0 i32.const 10
                    memory.atomic.notify align=4))";
            var (poly, aot) = RunBoth(src, "f");
            Assert.Equal(I32((Value)poly), I32((Value)aot));
            Assert.Equal(0, I32((Value)aot));
        }

        [Fact]
        public void I64_rmw_add_matches()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i64)
                    i32.const 0 i64.const 100 i64.atomic.store align=8
                    i32.const 0 i64.const 23 i64.atomic.rmw.add align=8))";
            var (poly, aot) = RunBoth(src, "f");
            Assert.Equal(I64((Value)poly), I64((Value)aot));
            Assert.Equal(100L, I64((Value)aot));
        }

        [Fact]
        public void Function_with_atomics_does_not_fall_back()
        {
            // Ensure phase-3 gives us full transpilation coverage for
            // every atomic family — a FallbackCount of 0 means no
            // function bailed to the interpreter.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 0 i32.const 1 i32.atomic.store align=4
                    i32.const 0 i32.const 2 i32.atomic.rmw.add align=4
                    i32.const 0 i32.const 3 i32.const 4 i32.atomic.rmw.cmpxchg align=4
                    i32.const 0 i32.const 5 i32.atomic.rmw8.add_u align=1
                    atomic.fence
                    drop drop drop
                    i32.const 0 i32.atomic.load align=4))";
            var rt = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = rt.InstantiateModule(module);
            rt.RegisterModule("M", inst);
            var result = new ModuleTranspiler().Transpile(inst, rt);
            Assert.Equal(0, result.FallbackCount);
        }
    }
}
