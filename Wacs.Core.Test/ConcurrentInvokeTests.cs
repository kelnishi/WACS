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
