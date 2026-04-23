// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Diagnostics;
using System.Threading;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Text;
using Wacs.WASI.Threads;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Multi-runtime soak (Layer 4c). Creates many
    /// <see cref="WasmRuntime"/> instances sequentially, each running a
    /// small wasi-threads workload, then lets them fall out of scope.
    /// Regression gate for the class of resource-accumulation bug that
    /// crashed the .NET test host in Layer 1c's original
    /// <c>ThreadLocal&lt;ExecContext&gt;</c> implementation after ~20-30
    /// spec-test runtimes. Post-fix (swap to
    /// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>
    /// keyed by ManagedThreadId), many hundreds of runtimes survive with
    /// zero residue.
    /// </summary>
    public class MultiRuntimeSoakTests
    {
        private const int RuntimeCount = 60;
        private const int WorkersPerRuntime = 4;

        private const string SoakModule = @"
            (module
              (import ""wasi"" ""thread-spawn"" (func $spawn (param i32) (result i32)))
              (memory (export ""memory"") 1 1 shared)
              (func (export ""wasi_thread_start"") (param $tid i32) (param $start_arg i32)
                i32.const 0
                i32.const 1
                i32.atomic.rmw.add
                drop)
              (func (export ""spawn_n"") (param $n i32) (result i32)
                (local $i i32) (local $fail i32)
                (loop $L
                  (if (i32.lt_s (local.get $i) (local.get $n))
                    (then
                      (local.set $i (i32.add (local.get $i) (i32.const 1)))
                      (if (i32.lt_s (call $spawn (i32.const 0)) (i32.const 1))
                        (then (local.set $fail (i32.add (local.get $fail) (i32.const 1)))))
                      (br $L))))
                (local.get $fail))
              (func (export ""read"") (result i32)
                i32.const 0
                i32.atomic.load))";

        [Fact]
        public void Many_runtimes_in_sequence_do_not_accumulate_resources()
        {
            var sw = Stopwatch.StartNew();

            for (int iter = 0; iter < RuntimeCount; iter++)
            {
                RunOne(iter);

                // Force collection every 10 iterations so any leak
                // accumulation surfaces within the loop rather than at the
                // end. Not required for correctness — just tightens the
                // regression window.
                if ((iter + 1) % 10 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }

            sw.Stop();
            // Loose wall-clock sanity: 60 × (spawn 4 workers + 4 atomic adds
            // + read) should fit comfortably under 30s. A runaway here
            // usually means the prior runtime's ExecContexts are still
            // being held and the GC is under pressure.
            Assert.True(sw.ElapsedMilliseconds < 30_000,
                $"soak took {sw.ElapsedMilliseconds}ms — suspect resource accumulation");
        }

        private static void RunOne(int iter)
        {
            var runtime = new WasmRuntime();
            new WasiThreads().BindToRuntime(runtime);

            var module = TextModuleParser.ParseWat(SoakModule);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);

            var spawnAddr = runtime.GetExportedFunction(("M", "spawn_n"));
            var readAddr = runtime.GetExportedFunction(("M", "read"));

            var failures = (int)runtime.CreateStackInvoker(spawnAddr)(new Value[] { (Value)WorkersPerRuntime })[0];
            Assert.True(failures == 0, $"iter {iter}: {failures} spawn failures");

            // Poll for every worker's increment to land.
            var deadline = Stopwatch.StartNew();
            int final = 0;
            while (deadline.ElapsedMilliseconds < 2000)
            {
                final = (int)runtime.CreateStackInvoker(readAddr)(Array.Empty<Value>())[0];
                if (final == WorkersPerRuntime) break;
                Thread.Sleep(5);
            }
            Assert.True(final == WorkersPerRuntime,
                $"iter {iter}: expected {WorkersPerRuntime}, got {final}");
        }
    }
}
