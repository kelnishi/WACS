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
    /// End-to-end regression for the wasi-threads adapter (Layer 3). A wasm
    /// module imports <c>wasi:thread-spawn</c> and spawns N worker threads that
    /// each perform an atomic RMW on shared memory. The test waits for every
    /// worker's contribution to land in the counter and asserts the final
    /// value matches the expected sum — which passes only if all layers of
    /// the stack compose correctly:
    /// <list type="bullet">
    /// <item>Layer 1c/1d: per-thread ExecContext + ThreadBasedHost spawn real Threads</item>
    /// <item>Layer 2b-2d: IsShared flips for globals/tables in modules with shared memory</item>
    /// <item>Layer 3: WasiThreads host import wires spawn to IWasmThreadHost</item>
    /// <item>Phase-1 threads atomics: i32.atomic.rmw.add is actually atomic across those threads</item>
    /// </list>
    /// </summary>
    public class WasiThreadsTests
    {
        private const string SpawnModule = @"
            (module
              (import ""wasi"" ""thread-spawn"" (func $spawn (param i32) (result i32)))
              (memory (export ""memory"") 1 1 shared)
              (func $wasi_thread_start (export ""wasi_thread_start"")
                (param $tid i32) (param $start_arg i32)
                i32.const 0
                i32.const 1
                i32.atomic.rmw.add
                drop)
              (func (export ""spawn_workers"") (param $n i32) (result i32)
                (local $i i32) (local $fail i32)
                (local.set $i (i32.const 0))
                (local.set $fail (i32.const 0))
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
        public void Spawn_workers_each_increment_shared_counter()
        {
            const int workerCount = 16;

            var runtime = new WasmRuntime();
            new WasiThreads().BindToRuntime(runtime);

            var module = TextModuleParser.ParseWat(SpawnModule);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);

            var spawnAddr = runtime.GetExportedFunction(("M", "spawn_workers"));
            var readAddr = runtime.GetExportedFunction(("M", "read"));

            var spawnInvoker = runtime.CreateStackInvoker(spawnAddr);
            var readInvoker = runtime.CreateStackInvoker(readAddr);

            var failures = (int)spawnInvoker(new Value[] { (Value)workerCount })[0];
            Assert.Equal(0, failures);

            // Spin until every worker has contributed its increment. The
            // atomic RMW on shared memory is the synchronization point;
            // nothing else in the test observes thread completion.
            var deadline = Stopwatch.StartNew();
            int final = 0;
            while (deadline.ElapsedMilliseconds < 5000)
            {
                final = (int)readInvoker(Array.Empty<Value>())[0];
                if (final == workerCount) break;
                Thread.Sleep(10);
            }
            Assert.Equal(workerCount, final);
        }

        [Fact]
        public void Spawn_with_no_thread_start_export_returns_error()
        {
            // Module imports thread-spawn but omits the wasi_thread_start
            // export the spec requires. The adapter must surface a negative
            // return value rather than crashing.
            var src = @"
                (module
                  (import ""wasi"" ""thread-spawn"" (func $spawn (param i32) (result i32)))
                  (memory 1 1 shared)
                  (func (export ""try_spawn"") (result i32)
                    i32.const 0
                    call $spawn))";

            var runtime = new WasmRuntime();
            new WasiThreads().BindToRuntime(runtime);

            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);

            var addr = runtime.GetExportedFunction(("M", "try_spawn"));
            var result = (int)runtime.CreateStackInvoker(addr)(Array.Empty<Value>())[0];

            Assert.True(result < 0, $"expected negative tid, got {result}");
        }
    }
}
