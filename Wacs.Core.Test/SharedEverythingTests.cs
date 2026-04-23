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
    /// Regression gate for the shared-everything-threads foundation
    /// subset (Layer 5). Covers feature-flag gating, shared-global
    /// concurrent semantics, and thread-local global per-thread scoping.
    /// Instructions (<c>global.atomic.*</c>, <c>pause</c>) are deferred
    /// until the proposal assigns canonical opcode bytes, so those
    /// subphases are not tested here.
    /// </summary>
    public class SharedEverythingTests
    {
        // ---- 5f: feature-flag enforcement -------------------------------

        [Fact]
        public void Shared_global_rejected_without_feature_flag()
        {
            var src = @"
                (module
                  (global (shared) (mut i64) (i64.const 0)))";

            var runtime = new WasmRuntime(new RuntimeAttributes
            {
                EnableSharedEverythingThreads = false,
            });
            var module = TextModuleParser.ParseWat(src);

            Assert.Throws<NotSupportedException>(() => runtime.InstantiateModule(module));
        }

        [Fact]
        public void Thread_local_global_rejected_without_feature_flag()
        {
            var src = @"
                (module
                  (global (thread_local) (mut i32) (i32.const 0)))";

            var runtime = new WasmRuntime(new RuntimeAttributes
            {
                EnableSharedEverythingThreads = false,
            });
            var module = TextModuleParser.ParseWat(src);

            Assert.Throws<NotSupportedException>(() => runtime.InstantiateModule(module));
        }

        // ---- 5a: shared globals under contention ------------------------

        [Fact]
        public void Shared_global_serializes_concurrent_reads_and_writes()
        {
            // Two distinctive 64-bit patterns alternated by writers; readers
            // must never observe a torn mix. Post-Layer-2c the per-instance
            // lock serializes struct assignments; Layer 5a now drives
            // IsShared from the per-declaration flag rather than the
            // module-has-shared-memory approximation.
            var src = @"
                (module
                  (global $g (export ""g"") (shared) (mut i64) (i64.const 0))
                  (func (export ""set"") (param $v i64)
                    local.get $v
                    global.set $g)
                  (func (export ""get"") (result i64)
                    global.get $g))";

            var runtime = new WasmRuntime(new RuntimeAttributes
            {
                EnableSharedEverythingThreads = true,
            });
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var setAddr = runtime.GetExportedFunction(("M", "set"));
            var getAddr = runtime.GetExportedFunction(("M", "get"));

            // Verify the global actually landed as IsShared.
            var gAddr = ((ExternalValue.Global)inst.Exports[0].Value).Address;
            Assert.True(runtime.RuntimeStore[gAddr].IsShared);

            const long patternA = unchecked((long)0x0123456789ABCDEF);
            const long patternB = unchecked((long)0xFEDCBA9876543210);
            const int iters = 3000;
            var torn = 0;
            var stop = false;
            var barrier = new Barrier(3); // 2 writers + 1 reader

            var writerA = new Thread(() =>
            {
                var inv = runtime.CreateStackInvoker(setAddr);
                barrier.SignalAndWait();
                for (int i = 0; i < iters; i++) inv(new Value[] { (Value)patternA });
            });
            var writerB = new Thread(() =>
            {
                var inv = runtime.CreateStackInvoker(setAddr);
                barrier.SignalAndWait();
                for (int i = 0; i < iters; i++) inv(new Value[] { (Value)patternB });
            });
            var reader = new Thread(() =>
            {
                var inv = runtime.CreateStackInvoker(getAddr);
                barrier.SignalAndWait();
                while (!Volatile.Read(ref stop))
                {
                    var v = (long)inv(Array.Empty<Value>())[0];
                    if (v != 0 && v != patternA && v != patternB)
                        Interlocked.Increment(ref torn);
                }
            });

            writerA.Start(); writerB.Start(); reader.Start();
            writerA.Join(); writerB.Join();
            Volatile.Write(ref stop, true);
            reader.Join();

            Assert.Equal(0, torn);
        }

        // ---- 5c: thread-local globals -----------------------------------

        [Fact]
        public void Thread_local_global_stores_are_per_thread()
        {
            // Each thread writes its own thread-id into the TL global, reads
            // it back, and checks it matches. Main thread writes 0 (the
            // initial value from the declarator), then asserts its own read
            // returns 0 after worker threads finish — writes in other
            // threads' slots must not leak.
            var src = @"
                (module
                  (global $t (export ""t"") (thread_local) (mut i32) (i32.const 0))
                  (func (export ""set_t"") (param $v i32)
                    local.get $v
                    global.set $t)
                  (func (export ""get_t"") (result i32)
                    global.get $t))";

            var runtime = new WasmRuntime(new RuntimeAttributes
            {
                EnableSharedEverythingThreads = true,
            });
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            var setAddr = runtime.GetExportedFunction(("M", "set_t"));
            var getAddr = runtime.GetExportedFunction(("M", "get_t"));

            // Main thread's TL slot defaults to the declared initializer (0).
            var mainGet = runtime.CreateStackInvoker(getAddr);
            Assert.Equal(0, (int)mainGet(Array.Empty<Value>())[0]);

            const int threadCount = 8;
            var mismatches = 0;
            var threads = new Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int value = 100 + t;
                threads[t] = new Thread(() =>
                {
                    var set = runtime.CreateStackInvoker(setAddr);
                    var get = runtime.CreateStackInvoker(getAddr);
                    // Write, read back — expect exactly my value, not 0 and
                    // not any other thread's value.
                    set(new Value[] { (Value)value });
                    var read = (int)get(Array.Empty<Value>())[0];
                    if (read != value) Interlocked.Increment(ref mismatches);
                });
            }
            foreach (var th in threads) th.Start();
            foreach (var th in threads) th.Join();

            Assert.Equal(0, mismatches);

            // Main thread's TL slot must still be 0 — worker writes don't
            // leak across thread slots.
            Assert.Equal(0, (int)mainGet(Array.Empty<Value>())[0]);
        }
    }
}
