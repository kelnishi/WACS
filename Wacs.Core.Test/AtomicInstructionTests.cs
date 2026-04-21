// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Threading;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// End-to-end tests for the threads proposal in the polymorphic
    /// interpreter. Exercises validation rules, per-family execution
    /// semantics, and both <see cref="ConcurrencyPolicyMode"/> policies.
    /// </summary>
    public class AtomicInstructionTests
    {
        private static (WasmRuntime runtime, ModuleInstance inst) Build(string src,
            IConcurrencyPolicy? policy = null, bool relaxSharedCheck = false)
        {
            var attrs = new RuntimeAttributes();
            if (policy != null) attrs.ConcurrencyPolicy = policy;
            attrs.RelaxAtomicSharedCheck = relaxSharedCheck;
            var runtime = new WasmRuntime(attrs);
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            return (runtime, inst);
        }

        private static int InvokeI32(WasmRuntime runtime, string name, params Value[] args)
        {
            var addr = runtime.GetExportedFunction(("M", name));
            var inv = runtime.CreateStackInvoker(addr);
            var r = inv(args);
            return (int)r[0];
        }

        private static long InvokeI64(WasmRuntime runtime, string name, params Value[] args)
        {
            var addr = runtime.GetExportedFunction(("M", name));
            var inv = runtime.CreateStackInvoker(addr);
            var r = inv(args);
            return (long)r[0];
        }

        // ---- Validation -------------------------------------------------

        [Fact]
        public void Wrong_alignment_rejected()
        {
            // i32.atomic.load requires align=4 exactly. Specifying align=2
            // (half of natural) must fail validation.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 0
                    i32.atomic.load align=2))";
            Assert.ThrowsAny<Exception>(() => Build(src));
        }

        [Fact]
        public void Non_shared_memory_rejected_by_default()
        {
            // Atomic op on a non-shared memory must fail validation unless
            // RelaxAtomicSharedCheck is true.
            var src = @"
                (module
                  (memory 1 1)
                  (func (export ""f"") (result i32)
                    i32.const 0
                    i32.atomic.load align=4))";
            Assert.ThrowsAny<Exception>(() => Build(src));
        }

        [Fact]
        public void Non_shared_memory_accepted_with_relax_flag()
        {
            // Same module, but with RelaxAtomicSharedCheck enabled, parses.
            var src = @"
                (module
                  (memory 1 1)
                  (func (export ""f"") (result i32)
                    i32.const 0
                    i32.const 99
                    i32.atomic.store align=4
                    i32.const 0
                    i32.atomic.load align=4))";
            var (rt, _) = Build(src, relaxSharedCheck: true);
            Assert.Equal(99, InvokeI32(rt, "f"));
        }

        // ---- Load / store round-trip ------------------------------------

        [Fact]
        public void I32_load_store_round_trip()
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
            var (rt, _) = Build(src);
            Assert.Equal(0x12345678, InvokeI32(rt, "f"));
        }

        [Fact]
        public void I64_load_store_round_trip()
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
            var (rt, _) = Build(src);
            Assert.Equal(0x0123456789ABCDEFL, InvokeI64(rt, "f"));
        }

        [Fact]
        public void Subword_load_store()
        {
            // Store byte 0xA5 at offset 7, read back as i32 zero-extended.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 7
                    i32.const 0xA5
                    i32.atomic.store8 align=1
                    i32.const 7
                    i32.atomic.load8_u align=1))";
            var (rt, _) = Build(src);
            Assert.Equal(0xA5, InvokeI32(rt, "f"));
        }

        // ---- RMW correctness --------------------------------------------

        [Fact]
        public void Rmw_add_returns_original_then_sum()
        {
            // Store 10, then rmw.add 5, then load. Return should be 15.
            // The rmw op itself returns the original (10).
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""rmw"") (result i32)
                    i32.const 0
                    i32.const 10
                    i32.atomic.store align=4
                    i32.const 0
                    i32.const 5
                    i32.atomic.rmw.add align=4)
                  (func (export ""load"") (result i32)
                    i32.const 0
                    i32.atomic.load align=4))";
            var (rt, _) = Build(src);
            Assert.Equal(10, InvokeI32(rt, "rmw"));  // original
            Assert.Equal(15, InvokeI32(rt, "load")); // cell updated
        }

        [Fact]
        public void Rmw_xchg_swaps_and_returns_old()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 0 i32.const 7 i32.atomic.store align=4
                    i32.const 0 i32.const 42 i32.atomic.rmw.xchg align=4))";
            var (rt, _) = Build(src);
            Assert.Equal(7, InvokeI32(rt, "f"));
        }

        [Fact]
        public void Subword_rmw_add_CAS_loop()
        {
            // Subword RMW goes through SubwordCas.Loop on the enclosing
            // 32-bit word. Exercise it with rmw8.add_u.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""rmw"") (result i32)
                    i32.const 3 i32.const 100 i32.atomic.store8 align=1
                    i32.const 3 i32.const 56 i32.atomic.rmw8.add_u align=1))";
            var (rt, _) = Build(src);
            // Returns original (100). Cell is now 156.
            Assert.Equal(100, InvokeI32(rt, "rmw"));
        }

        // ---- Cmpxchg ----------------------------------------------------

        [Fact]
        public void Cmpxchg_success_replaces_and_returns_original()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""cas"") (result i32)
                    i32.const 0 i32.const 50 i32.atomic.store align=4
                    i32.const 0 i32.const 50 i32.const 99 i32.atomic.rmw.cmpxchg align=4)
                  (func (export ""load"") (result i32)
                    i32.const 0 i32.atomic.load align=4))";
            var (rt, _) = Build(src);
            Assert.Equal(50, InvokeI32(rt, "cas"));   // original
            Assert.Equal(99, InvokeI32(rt, "load"));  // replaced
        }

        [Fact]
        public void Cmpxchg_mismatch_leaves_cell_unchanged()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""cas"") (result i32)
                    i32.const 0 i32.const 50 i32.atomic.store align=4
                    i32.const 0 i32.const 999 i32.const 99 i32.atomic.rmw.cmpxchg align=4)
                  (func (export ""load"") (result i32)
                    i32.const 0 i32.atomic.load align=4))";
            var (rt, _) = Build(src);
            Assert.Equal(50, InvokeI32(rt, "cas"));   // original (no match)
            Assert.Equal(50, InvokeI32(rt, "load"));  // unchanged
        }

        // ---- Fence ------------------------------------------------------

        [Fact]
        public void Atomic_fence_is_executable()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    atomic.fence
                    i32.const 42))";
            var (rt, _) = Build(src);
            Assert.Equal(42, InvokeI32(rt, "f"));
        }

        // ---- Wait / notify policy ---------------------------------------

        [Fact]
        public void NotSupported_wait_mismatch_returns_2()
        {
            // Cell holds 0; wait for expected=99 → not-equal (2).
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 0
                    i32.const 99
                    i64.const 0
                    memory.atomic.wait32 align=4))";
            var (rt, _) = Build(src, new NotSupportedPolicy());
            Assert.Equal(2, InvokeI32(rt, "f"));
        }

        [Fact]
        public void NotSupported_wait_zero_timeout_returns_1()
        {
            // Matching value, timeout=0 → timed-out (1).
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 0
                    i32.const 0
                    i64.const 0
                    memory.atomic.wait32 align=4))";
            var (rt, _) = Build(src, new NotSupportedPolicy());
            Assert.Equal(1, InvokeI32(rt, "f"));
        }

        [Fact]
        public void NotSupported_wait_infinite_timeout_traps()
        {
            // Matching value + infinite timeout → TrapException.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 0
                    i32.const 0
                    i64.const -1
                    memory.atomic.wait32 align=4))";
            var (rt, _) = Build(src, new NotSupportedPolicy());
            Assert.ThrowsAny<TrapException>(() => InvokeI32(rt, "f"));
        }

        [Fact]
        public void NotSupported_notify_returns_0()
        {
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""f"") (result i32)
                    i32.const 0
                    i32.const 10
                    memory.atomic.notify align=4))";
            var (rt, _) = Build(src, new NotSupportedPolicy());
            Assert.Equal(0, InvokeI32(rt, "f"));
        }

        // ---- HostDefined producer/consumer ------------------------------

        [Fact]
        public void HostDefined_wait_and_notify_rendezvous()
        {
            // Phase 1 does not support concurrent wasm execution in the
            // same WasmRuntime (single ExecContext) — that's a phase 2
            // follow-up. We exercise the policy directly against a
            // standalone MemoryInstance, which is exactly what Execute
            // would call through if the runtime were thread-local.
            var memType = new Wacs.Core.Types.MemoryType(
                new Wacs.Core.Types.Limits(Wacs.Core.Types.Defs.AddrType.I32, 1, 1, shared: true));
            var mem = new MemoryInstance(memType);
            mem.EnableConcurrentGrow();

            var policy = new HostDefinedPolicy();

            int waitResult = -1;
            var waiter = new Thread(() =>
            {
                waitResult = policy.Wait32(mem, addr: 0, expected: 0, timeoutNs: -1);
            });
            waiter.Start();

            // Give the waiter a moment to enqueue in the wait-slot.
            Thread.Sleep(50);
            int woken = policy.Notify(mem, addr: 0, maxWaiters: 1);

            Assert.True(waiter.Join(TimeSpan.FromSeconds(2)),
                "waiter did not wake within 2s");

            Assert.Equal(1, woken);     // exactly one waiter notified
            Assert.Equal(0, waitResult); // wait returned ok
        }

        [Fact]
        public void HostDefined_notify_with_no_waiters_returns_zero()
        {
            var memType = new Wacs.Core.Types.MemoryType(
                new Wacs.Core.Types.Limits(Wacs.Core.Types.Defs.AddrType.I32, 1, 1, shared: true));
            var mem = new MemoryInstance(memType);
            var policy = new HostDefinedPolicy();
            Assert.Equal(0, policy.Notify(mem, 0, int.MaxValue));
        }

        [Fact]
        public void HostDefined_wait_with_mismatched_expected_returns_2()
        {
            var memType = new Wacs.Core.Types.MemoryType(
                new Wacs.Core.Types.Limits(Wacs.Core.Types.Defs.AddrType.I32, 1, 1, shared: true));
            var mem = new MemoryInstance(memType);
            mem.AtomicStoreInt32(0, 42);
            var policy = new HostDefinedPolicy();
            // Cell is 42 but we expect 99 → not-equal.
            Assert.Equal(2, policy.Wait32(mem, 0, expected: 99, timeoutNs: 0));
        }

        [Fact]
        public void HostDefined_wait_zero_timeout_times_out()
        {
            var memType = new Wacs.Core.Types.MemoryType(
                new Wacs.Core.Types.Limits(Wacs.Core.Types.Defs.AddrType.I32, 1, 1, shared: true));
            var mem = new MemoryInstance(memType);
            var policy = new HostDefinedPolicy();
            // Cell is 0, expected 0 (match), timeout 0 → timed-out immediately.
            Assert.Equal(1, policy.Wait32(mem, 0, expected: 0, timeoutNs: 0));
        }

        // ---- Default policy selection -----------------------------------

        [Fact]
        public void Default_policy_on_non_Unity_is_HostDefined()
        {
            // Running the test suite outside Unity → HostDefined default.
            var attrs = new RuntimeAttributes();
            Assert.Equal(ConcurrencyPolicyMode.HostDefined, attrs.ConcurrencyPolicy.Mode);
        }
    }
}
