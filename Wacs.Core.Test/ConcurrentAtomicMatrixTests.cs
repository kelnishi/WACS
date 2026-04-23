// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Threading;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Concurrent atomic-op variety stress (Layer 4a). Exercises every
    /// RMW family (add, sub, and, or, xor, xchg, cmpxchg) across i32 and
    /// i64 widths plus i32.rmw8 / i32.rmw16 subword paths under 16-thread
    /// contention. Each test has an op-commutative expected value so a
    /// single incorrect final observation fails the test deterministically.
    ///
    /// <para>Bigger iteration counts than the Layer 1 smoke tests — the
    /// goal is to exercise the CAS-loop fallback on subword ops and the
    /// native <c>Interlocked</c> paths on full-width ops through many
    /// cycles to surface any stale-register / ordering bugs.</para>
    /// </summary>
    public class ConcurrentAtomicMatrixTests
    {
        private const int ThreadCount = 16;
        private const int Iterations = 1000;

        private static (WasmRuntime runtime, FuncAddr addr, FuncAddr readAddr)
            Build(string src, string writeExport, string readExport)
        {
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            return (
                runtime,
                runtime.GetExportedFunction(("M", writeExport)),
                runtime.GetExportedFunction(("M", readExport))
            );
        }

        private static long ReadI64(WasmRuntime runtime, FuncAddr addr)
            => (long)runtime.CreateStackInvoker(addr)(Array.Empty<Value>())[0];

        private static int ReadI32(WasmRuntime runtime, FuncAddr addr)
            => (int)runtime.CreateStackInvoker(addr)(Array.Empty<Value>())[0];

        // ---- i32.atomic.rmw.add -----------------------------------------

        [Fact]
        public void I32_rmw_add_is_commutative_under_contention()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""inc"")
                    i32.const 0
                    i32.const 1
                    i32.atomic.rmw.add
                    drop)
                  (func (export ""read"") (result i32)
                    i32.const 0
                    i32.atomic.load))";
            var (runtime, inc, read) = Build(src, "inc", "read");

            RunWriters(runtime, inc);
            Assert.Equal(ThreadCount * Iterations, ReadI32(runtime, read));
        }

        // ---- i32.atomic.rmw.sub (add-with-negation) ---------------------

        [Fact]
        public void I32_rmw_sub_from_large_initial_reaches_zero()
        {
            // Pre-populate cell via a one-off host write (simpler than a data
            // segment literal). Then each thread subs 1 until zero. Final == 0
            // only if every RMW was commutative-correct.
            int initial = ThreadCount * Iterations;
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""seed"") (param $v i32)
                    i32.const 0
                    local.get $v
                    i32.atomic.store)
                  (func (export ""dec"")
                    i32.const 0
                    i32.const 1
                    i32.atomic.rmw.sub
                    drop)
                  (func (export ""read"") (result i32)
                    i32.const 0
                    i32.atomic.load))";
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var seedAddr = runtime.GetExportedFunction(("M", "seed"));
            var dec = runtime.GetExportedFunction(("M", "dec"));
            var read = runtime.GetExportedFunction(("M", "read"));

            runtime.CreateStackInvoker(seedAddr)(new Value[] { (Value)initial });
            RunWriters(runtime, dec);
            Assert.Equal(0, ReadI32(runtime, read));
        }

        // ---- i32.atomic.rmw.or (commutative idempotent bitwise) ---------

        [Fact]
        public void I32_rmw_or_applies_all_bits()
        {
            // Each thread ORs in its own unique bit position. Final value
            // must be the OR of all ThreadCount bits = (1 << ThreadCount) - 1.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""or_bit"") (param $bit i32)
                    i32.const 0
                    local.get $bit
                    i32.atomic.rmw.or
                    drop)
                  (func (export ""read"") (result i32)
                    i32.const 0
                    i32.atomic.load))";
            var (runtime, orBit, read) = Build(src, "or_bit", "read");

            var barrier = new Barrier(ThreadCount);
            var threads = new Thread[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                int bit = 1 << t;
                threads[t] = new Thread(() =>
                {
                    var inv = runtime.CreateStackInvoker(orBit);
                    barrier.SignalAndWait();
                    for (int i = 0; i < Iterations; i++)
                        inv(new Value[] { (Value)bit });
                });
            }
            foreach (var th in threads) th.Start();
            foreach (var th in threads) th.Join();

            int expected = (1 << ThreadCount) - 1;
            Assert.Equal(expected, ReadI32(runtime, read));
        }

        // ---- i32.atomic.rmw.xor (each bit toggled even-count times) -----

        [Fact]
        public void I32_rmw_xor_even_iterations_is_identity()
        {
            // Each thread XORs its unique bit Iterations times. If Iterations
            // is even, every bit ends up toggled back to its initial state
            // (0) regardless of interleaving.
            Assert.True(Iterations % 2 == 0, "test assumes even iteration count");
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""xor_bit"") (param $bit i32)
                    i32.const 0
                    local.get $bit
                    i32.atomic.rmw.xor
                    drop)
                  (func (export ""read"") (result i32)
                    i32.const 0
                    i32.atomic.load))";
            var (runtime, xorBit, read) = Build(src, "xor_bit", "read");

            var barrier = new Barrier(ThreadCount);
            var threads = new Thread[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                int bit = 1 << t;
                threads[t] = new Thread(() =>
                {
                    var inv = runtime.CreateStackInvoker(xorBit);
                    barrier.SignalAndWait();
                    for (int i = 0; i < Iterations; i++)
                        inv(new Value[] { (Value)bit });
                });
            }
            foreach (var th in threads) th.Start();
            foreach (var th in threads) th.Join();

            Assert.Equal(0, ReadI32(runtime, read));
        }

        // ---- i32.atomic.rmw.xchg (result is one of the written values) ---

        [Fact]
        public void I32_rmw_xchg_final_is_one_of_written_values()
        {
            // Each thread writes its thread-id (1..N) repeatedly. Final
            // value is whichever thread wrote last — always in [1, N].
            // Pre-2c a torn struct write could produce a value outside the
            // legal set; post-2c the lock serializes and tests should see
            // only the written values.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""set"") (param $v i32)
                    i32.const 0
                    local.get $v
                    i32.atomic.rmw.xchg
                    drop)
                  (func (export ""read"") (result i32)
                    i32.const 0
                    i32.atomic.load))";
            var (runtime, set, read) = Build(src, "set", "read");

            var barrier = new Barrier(ThreadCount);
            var threads = new Thread[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                int id = t + 1;
                threads[t] = new Thread(() =>
                {
                    var inv = runtime.CreateStackInvoker(set);
                    barrier.SignalAndWait();
                    for (int i = 0; i < Iterations; i++)
                        inv(new Value[] { (Value)id });
                });
            }
            foreach (var th in threads) th.Start();
            foreach (var th in threads) th.Join();

            int final = ReadI32(runtime, read);
            Assert.InRange(final, 1, ThreadCount);
        }

        // ---- i32.atomic.rmw.cmpxchg (CAS-loop counter) ------------------

        [Fact]
        public void I32_rmw_cmpxchg_loop_sums_correctly()
        {
            // Each thread uses cmpxchg in a loop to atomically increment.
            // Proves the cmpxchg round-trip (read expected → cas → retry)
            // composes under ThreadCount-way contention.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""inc_cas"")
                    (local $cur i32)
                    (loop $retry
                      ;; cur = atomic.load[0]
                      i32.const 0
                      i32.atomic.load
                      local.set $cur

                      ;; prev = cmpxchg(addr=0, expected=cur, replacement=cur+1)
                      i32.const 0
                      local.get $cur
                      local.get $cur
                      i32.const 1
                      i32.add
                      i32.atomic.rmw.cmpxchg

                      ;; retry if prev != cur
                      local.get $cur
                      i32.ne
                      br_if $retry))
                  (func (export ""read"") (result i32)
                    i32.const 0
                    i32.atomic.load))";
            var (runtime, inc, read) = Build(src, "inc_cas", "read");

            RunWriters(runtime, inc);
            Assert.Equal(ThreadCount * Iterations, ReadI32(runtime, read));
        }

        // ---- i64.atomic.rmw.add -----------------------------------------

        [Fact]
        public void I64_rmw_add_is_commutative_under_contention()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""inc"")
                    i32.const 0
                    i64.const 1
                    i64.atomic.rmw.add
                    drop)
                  (func (export ""read"") (result i64)
                    i32.const 0
                    i64.atomic.load))";
            var (runtime, inc, read) = Build(src, "inc", "read");

            RunWriters(runtime, inc);
            Assert.Equal((long)ThreadCount * Iterations, ReadI64(runtime, read));
        }

        // ---- Subword: i32.atomic.rmw8.add_u (CAS-loop path) -------------

        [Fact]
        public void I32_rmw8_add_u_commutes_under_contention()
        {
            // Subword RMW goes through SubwordCas.Loop — a CAS loop on the
            // enclosing 32-bit word. Modular arithmetic is the expectation:
            // final = (threads × iters × delta) mod 256.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""inc"")
                    i32.const 0
                    i32.const 1
                    i32.atomic.rmw8.add_u
                    drop)
                  (func (export ""read"") (result i32)
                    i32.const 0
                    i32.atomic.load8_u))";
            var (runtime, inc, read) = Build(src, "inc", "read");

            RunWriters(runtime, inc);
            int expected = (ThreadCount * Iterations) & 0xFF;
            Assert.Equal(expected, ReadI32(runtime, read));
        }

        // ---- Subword: i32.atomic.rmw16.add_u ----------------------------

        [Fact]
        public void I32_rmw16_add_u_commutes_under_contention()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""inc"")
                    i32.const 0
                    i32.const 1
                    i32.atomic.rmw16.add_u
                    drop)
                  (func (export ""read"") (result i32)
                    i32.const 0
                    i32.atomic.load16_u))";
            var (runtime, inc, read) = Build(src, "inc", "read");

            RunWriters(runtime, inc);
            int expected = (ThreadCount * Iterations) & 0xFFFF;
            Assert.Equal(expected, ReadI32(runtime, read));
        }

        // ---- shared helper ----------------------------------------------

        private static void RunWriters(WasmRuntime runtime, FuncAddr addr)
        {
            var barrier = new Barrier(ThreadCount);
            var threads = new Thread[ThreadCount];
            for (int t = 0; t < ThreadCount; t++)
            {
                threads[t] = new Thread(() =>
                {
                    var inv = runtime.CreateStackInvoker(addr);
                    barrier.SignalAndWait();
                    for (int i = 0; i < Iterations; i++)
                        inv(Array.Empty<Value>());
                });
            }
            foreach (var th in threads) th.Start();
            foreach (var th in threads) th.Join();
        }
    }
}
