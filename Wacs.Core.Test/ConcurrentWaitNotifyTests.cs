// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// End-to-end wait/notify synchronization through the
    /// <see cref="Wacs.Core.Runtime.Concurrency.HostDefinedPolicy"/>
    /// (Layer 4b). Proves the default concurrency policy actually parks
    /// a real host thread on <c>memory.atomic.wait32</c> and wakes it
    /// on <c>memory.atomic.notify</c> — not just the mechanical atomic
    /// RMW path.
    /// </summary>
    public class ConcurrentWaitNotifyTests
    {
        /// <summary>
        /// Consumer wasm thread parks on wait32(signal, 0); producer on
        /// main thread stores data + sets signal=1 + notify. Consumer
        /// resumes, copies data into a result cell, exits. Main thread
        /// awaits completion and reads the result.
        ///
        /// <para>Race-free by wait's atomic-check semantics: if notify
        /// fires before consumer reaches wait, the in-wait check sees
        /// signal != 0 and returns "not-equal" (2) immediately. Either
        /// ordering converges on consumer reading the correct data.</para>
        /// </summary>
        [Fact]
        public async Task Producer_notifies_consumer_waiting_on_shared_cell()
        {
            // Layout:
            //   cell 0: signal (0=pending, 1=data-ready)
            //   cell 4: data (producer's payload)
            //   cell 8: consumer's observed result
            var src = @"
                (module
                  (memory (export ""memory"") 1 1 shared)
                  (func (export ""produce"") (param $payload i32)
                    i32.const 4
                    local.get $payload
                    i32.atomic.store
                    i32.const 0
                    i32.const 1
                    i32.atomic.store
                    i32.const 0
                    i32.const -1
                    memory.atomic.notify
                    drop)
                  (func (export ""consume"")
                    i32.const 0
                    i32.const 0
                    i64.const -1
                    memory.atomic.wait32
                    drop
                    i32.const 8
                    i32.const 4
                    i32.atomic.load
                    i32.atomic.store)
                  (func (export ""read_result"") (result i32)
                    i32.const 8
                    i32.atomic.load))";

            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var produceAddr = runtime.GetExportedFunction(("M", "produce"));
            var consumeAddr = runtime.GetExportedFunction(("M", "consume"));
            var readAddr = runtime.GetExportedFunction(("M", "read_result"));

            // Spawn consumer — parks on wait32 against signal==0.
            var consumer = runtime.ThreadHost.Spawn(consumeAddr, ReadOnlySpan<Value>.Empty);

            // Give the consumer time to reach the wait state. Not required
            // for correctness (wait's atomic check handles both orderings)
            // but makes the "waiting thread actually got woken" code path
            // the one under test most often.
            await Task.Delay(50);

            // Main thread fires the notify.
            runtime.CreateStackInvoker(produceAddr)(new Value[] { (Value)42 });

            // Consumer must complete within a reasonable deadline — failure
            // here means notify never woke the waiter.
            var trap = await consumer.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(trap);

            Assert.Equal(42, (int)runtime.CreateStackInvoker(readAddr)(Array.Empty<Value>())[0]);
        }

        /// <summary>
        /// Timeout path: no notifier. Consumer waits with a short timeout
        /// and must return 1 (timed out). Proves
        /// <c>HostDefinedPolicy.Wait32</c> respects the timeout, doesn't
        /// park forever.
        /// </summary>
        [Fact]
        public void Wait32_returns_timed_out_when_no_notifier()
        {
            // 10ms timeout (10,000,000 ns) on a cell whose current value
            // matches the expected argument — wait blocks, then returns 1.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""wait_briefly"") (result i32)
                    i32.const 0
                    i32.const 0
                    i64.const 10000000
                    memory.atomic.wait32))";

            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var addr = runtime.GetExportedFunction(("M", "wait_briefly"));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = (int)runtime.CreateStackInvoker(addr)(Array.Empty<Value>())[0];
            sw.Stop();

            Assert.Equal(1, result); // spec: 1 = timed-out
            // Sanity on the timing — waited at least 8ms (10ms nominal, allow slack).
            Assert.True(sw.ElapsedMilliseconds >= 8,
                $"wait returned too fast ({sw.ElapsedMilliseconds}ms); expected ~10ms");
        }

        /// <summary>
        /// Mismatched-expected path: wait32 returns 2 (not-equal) immediately
        /// without parking when the cell's current value doesn't match the
        /// expected argument. Proves the atomic check-before-park semantics.
        /// </summary>
        [Fact]
        public void Wait32_returns_not_equal_when_cell_already_differs()
        {
            // Seed cell with 1; wait expects 0 → immediate return 2.
            var src = @"
                (module
                  (memory 1 1 shared)
                  (func (export ""seed"")
                    i32.const 0
                    i32.const 1
                    i32.atomic.store)
                  (func (export ""wait_expect_zero"") (result i32)
                    i32.const 0
                    i32.const 0
                    i64.const -1
                    memory.atomic.wait32))";

            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var seed = runtime.GetExportedFunction(("M", "seed"));
            var wait = runtime.GetExportedFunction(("M", "wait_expect_zero"));

            runtime.CreateStackInvoker(seed)(Array.Empty<Value>());

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = (int)runtime.CreateStackInvoker(wait)(Array.Empty<Value>())[0];
            sw.Stop();

            Assert.Equal(2, result); // spec: 2 = not-equal
            // Should return immediately — well under a second.
            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"wait blocked despite not-equal precheck ({sw.ElapsedMilliseconds}ms)");
        }
    }
}
