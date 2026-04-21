// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Regression gate for per-thread <see cref="ExecContext"/> (Layer 1c). Proves
    /// that concurrent host threads invoking the same exported function against
    /// the same <see cref="WasmRuntime"/> don't corrupt each other's operand
    /// stack, frame pool, or call stack.
    ///
    /// <para>Pre-1c this test would race and either trap
    /// (OpStack underflow / frame-pool exhaustion) or return wrong values. Post-1c
    /// each host thread lazily gets its own ExecContext while sharing the
    /// <see cref="SharedRuntimeState"/> (Store, linked instructions, attributes)
    /// by reference.</para>
    /// </summary>
    public class ConcurrentInvokeTests
    {
        private const string PureAddWat = @"
            (module
              (func (export ""add"") (param i32 i32) (result i32)
                local.get 0
                local.get 1
                i32.add))";

        [Fact]
        public void Concurrent_invoke_of_pure_function_returns_correct_results()
        {
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(PureAddWat);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var addr = runtime.GetExportedFunction(("M", "add"));

            const int threadCount = 8;
            const int iterations = 1000;
            var failures = 0;
            var barrier = new Barrier(threadCount);

            var threads = new Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                threads[t] = new Thread(() =>
                {
                    var invoker = runtime.CreateStackInvoker(addr);
                    barrier.SignalAndWait();
                    for (int i = 0; i < iterations; i++)
                    {
                        // Use thread-id-dependent inputs so interleaved corruption
                        // produces wrong results, not just crashes.
                        int a = threadId * 10000 + i;
                        int b = threadId * 10000 + i + 1;
                        var result = invoker(new Value[] { a, b });
                        if ((int)result[0] != a + b)
                            Interlocked.Increment(ref failures);
                    }
                });
            }

            foreach (var th in threads) th.Start();
            foreach (var th in threads) th.Join();

            Assert.Equal(0, failures);
        }

        [Fact]
        public void Concurrent_invoke_under_switch_runtime_returns_correct_results()
        {
            var runtime = new WasmRuntime();
            runtime.UseSwitchRuntime = true;
            var module = TextModuleParser.ParseWat(PureAddWat);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var addr = runtime.GetExportedFunction(("M", "add"));

            const int threadCount = 8;
            const int iterations = 1000;
            var failures = 0;
            var barrier = new Barrier(threadCount);

            var threads = new Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                threads[t] = new Thread(() =>
                {
                    var invoker = runtime.CreateStackInvoker(addr);
                    barrier.SignalAndWait();
                    for (int i = 0; i < iterations; i++)
                    {
                        int a = threadId * 10000 + i;
                        int b = threadId * 10000 + i + 1;
                        var result = invoker(new Value[] { a, b });
                        if ((int)result[0] != a + b)
                            Interlocked.Increment(ref failures);
                    }
                });
            }

            foreach (var th in threads) th.Start();
            foreach (var th in threads) th.Join();

            Assert.Equal(0, failures);
        }

        [Fact]
        public void Concurrent_atomic_rmw_on_shared_memory_sums_correctly()
        {
            // Each thread performs N atomic.add ops against the same cell.
            // Final sum must equal threadCount * iterations * delta.
            // Proves per-thread ExecContext (Layer 1c) + atomic RMW semantics
            // (phase-1 atomics) compose correctly under real concurrency.
            var src = @"
                (module
                  (memory (export ""m"") 1 1 shared)
                  (func (export ""inc"") (param $delta i32) (result i32)
                    i32.const 0
                    local.get $delta
                    i32.atomic.rmw.add)
                  (func (export ""read"") (result i32)
                    i32.const 0
                    i32.atomic.load))";
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var incAddr = runtime.GetExportedFunction(("M", "inc"));
            var readAddr = runtime.GetExportedFunction(("M", "read"));

            const int threadCount = 8;
            const int iterations = 1000;
            const int delta = 3;
            var barrier = new Barrier(threadCount);

            var threads = new Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                threads[t] = new Thread(() =>
                {
                    var invoker = runtime.CreateStackInvoker(incAddr);
                    barrier.SignalAndWait();
                    for (int i = 0; i < iterations; i++)
                        invoker(new Value[] { (Value)delta });
                });
            }

            foreach (var th in threads) th.Start();
            foreach (var th in threads) th.Join();

            var reader = runtime.CreateStackInvoker(readAddr);
            var finalSum = (int)reader(Array.Empty<Value>())[0];
            Assert.Equal(threadCount * iterations * delta, finalSum);
        }

        [Fact]
        public async Task WasmThread_runs_entry_and_completes_task()
        {
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(PureAddWat);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var addr = runtime.GetExportedFunction(("M", "add"));

            Value[] args = { (Value)7, (Value)35 };
            var wt = runtime.ThreadHost.Spawn(addr, args);

            var trap = await wt.Completion;
            Assert.Null(trap);
            Assert.True(wt.HostId > 0);
        }

        [Fact]
        public async Task WasmThread_cancels_via_CancellationToken()
        {
            // Tight infinite loop calling a nop function. Cancellation is
            // observed at function-call boundaries (Layer 1f), so the next
            // `call $nop` after the CT fires produces an InterruptedException.
            var src = @"
                (module
                  (func $nop)
                  (func (export ""spin"")
                    (loop $L
                      call $nop
                      br $L)))";
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var addr = runtime.GetExportedFunction(("M", "spin"));

            using var cts = new CancellationTokenSource();
            var wt = runtime.ThreadHost.Spawn(addr, ReadOnlySpan<Value>.Empty, cts.Token);

            // Let the thread enter its loop, then cancel.
            await Task.Delay(50);
            cts.Cancel();

            var trap = await wt.Completion;
            Assert.NotNull(trap);
            Assert.Contains("interrupted", trap!.Message);
        }

        [Fact]
        public async Task WasmThread_cancels_via_RequestTrap()
        {
            // Same loop, cancel via wasmThread.RequestTrap instead of CT.
            var src = @"
                (module
                  (func $nop)
                  (func (export ""spin"")
                    (loop $L
                      call $nop
                      br $L)))";
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var addr = runtime.GetExportedFunction(("M", "spin"));

            var wt = runtime.ThreadHost.Spawn(addr, ReadOnlySpan<Value>.Empty);
            await Task.Delay(50);
            wt.RequestTrap("host-stop");

            var trap = await wt.Completion;
            Assert.NotNull(trap);
            Assert.Contains("interrupted", trap!.Message);
        }

        [Fact]
        public async Task WasmThread_surfaces_trap_in_completion()
        {
            // Function that unconditionally traps via unreachable.
            var src = @"
                (module
                  (func (export ""boom"")
                    unreachable))";
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var addr = runtime.GetExportedFunction(("M", "boom"));

            var wt = runtime.ThreadHost.Spawn(addr, ReadOnlySpan<Value>.Empty);
            var trap = await wt.Completion;

            Assert.NotNull(trap);
        }
    }
}
