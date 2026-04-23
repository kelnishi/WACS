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
        public void Concurrent_global_read_write_never_tears()
        {
            // i64 global backed by a shared-memory module → IsShared fires at
            // instantiation (Layer 2b). Two distinctive 64-bit bit patterns are
            // written in alternation. Readers assert every read equals one of
            // those exact patterns — never a mix of bytes from each (which a
            // pre-Layer-2c torn write could produce on platforms where the
            // struct write is non-atomic).
            var src = @"
                (module
                  (memory (export ""m"") 1 1 shared)
                  (global $g (export ""g"") (mut i64) (i64.const 0))
                  (func (export ""set_g"") (param $v i64)
                    local.get $v
                    global.set $g)
                  (func (export ""get_g"") (result i64)
                    global.get $g))";
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var setAddr = runtime.GetExportedFunction(("M", "set_g"));
            var getAddr = runtime.GetExportedFunction(("M", "get_g"));

            const long patternA = unchecked((long)0x0123456789ABCDEF);
            const long patternB = unchecked((long)0xFEDCBA9876543210);
            const int writerCount = 4;
            const int readerCount = 4;
            const int iterations = 5000;

            var torn = 0;
            var stop = false;
            var barrier = new Barrier(writerCount + readerCount);

            var threads = new Thread[writerCount + readerCount];
            for (int w = 0; w < writerCount; w++)
            {
                int which = w;
                threads[w] = new Thread(() =>
                {
                    var invoker = runtime.CreateStackInvoker(setAddr);
                    long val = (which % 2 == 0) ? patternA : patternB;
                    barrier.SignalAndWait();
                    for (int i = 0; i < iterations; i++)
                    {
                        invoker(new Value[] { (Value)val });
                        val = val == patternA ? patternB : patternA;
                    }
                });
            }
            for (int r = 0; r < readerCount; r++)
            {
                threads[writerCount + r] = new Thread(() =>
                {
                    var invoker = runtime.CreateStackInvoker(getAddr);
                    barrier.SignalAndWait();
                    while (!Volatile.Read(ref stop))
                    {
                        var v = (long)invoker(Array.Empty<Value>())[0];
                        if (v != patternA && v != patternB && v != 0)
                            Interlocked.Increment(ref torn);
                    }
                });
            }

            foreach (var th in threads) th.Start();
            // Let writers finish; then stop readers.
            for (int w = 0; w < writerCount; w++) threads[w].Join();
            Volatile.Write(ref stop, true);
            for (int r = 0; r < readerCount; r++) threads[writerCount + r].Join();

            Assert.Equal(0, torn);
        }

        [Fact]
        public void Concurrent_table_grow_never_crashes()
        {
            // Multiple threads calling table.grow on the same shared table
            // + other threads calling call_indirect into the stable prefix.
            // Pre-Layer-2d, a reader hitting List<T>.Elements[i] during another
            // thread's List<T>.Add-triggered internal resize could OOB or see
            // stale backing. Post-2d, Grow pre-allocates capacity atomically
            // and readers stay lock-free on valid indices.
            var src = @"
                (module
                  (memory (export ""m"") 1 1 shared)
                  (table (export ""t"") 4 funcref)
                  (elem (i32.const 0) $fa $fb $fc $fd)
                  (func $fa (result i32) i32.const 10)
                  (func $fb (result i32) i32.const 20)
                  (func $fc (result i32) i32.const 30)
                  (func $fd (result i32) i32.const 40)
                  (type $ret_i32 (func (result i32)))
                  (func (export ""call_n"") (param $i i32) (result i32)
                    local.get $i
                    call_indirect (type $ret_i32))
                  (func (export ""grow_one"") (result i32)
                    ref.func $fa
                    i32.const 1
                    table.grow 0))";
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var callAddr = runtime.GetExportedFunction(("M", "call_n"));
            var growAddr = runtime.GetExportedFunction(("M", "grow_one"));

            const int growerCount = 4;
            const int readerCount = 4;
            const int iterations = 500;

            var exceptions = 0;
            var wrongResults = 0;
            var stop = false;
            var barrier = new Barrier(growerCount + readerCount);

            var threads = new Thread[growerCount + readerCount];
            for (int g = 0; g < growerCount; g++)
            {
                threads[g] = new Thread(() =>
                {
                    var invoker = runtime.CreateStackInvoker(growAddr);
                    barrier.SignalAndWait();
                    for (int i = 0; i < iterations; i++)
                    {
                        try { invoker(Array.Empty<Value>()); }
                        catch { Interlocked.Increment(ref exceptions); }
                    }
                });
            }
            for (int r = 0; r < readerCount; r++)
            {
                int rid = r;
                threads[growerCount + r] = new Thread(() =>
                {
                    var invoker = runtime.CreateStackInvoker(callAddr);
                    barrier.SignalAndWait();
                    int idx = rid & 3; // stable prefix: always one of fa/fb/fc/fd
                    int[] expected = { 10, 20, 30, 40 };
                    while (!Volatile.Read(ref stop))
                    {
                        try
                        {
                            var result = (int)invoker(new Value[] { (Value)idx })[0];
                            if (result != expected[idx])
                                Interlocked.Increment(ref wrongResults);
                        }
                        catch { Interlocked.Increment(ref exceptions); }
                    }
                });
            }

            foreach (var th in threads) th.Start();
            for (int g = 0; g < growerCount; g++) threads[g].Join();
            Volatile.Write(ref stop, true);
            for (int r = 0; r < readerCount; r++) threads[growerCount + r].Join();

            Assert.Equal(0, exceptions);
            Assert.Equal(0, wrongResults);
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
