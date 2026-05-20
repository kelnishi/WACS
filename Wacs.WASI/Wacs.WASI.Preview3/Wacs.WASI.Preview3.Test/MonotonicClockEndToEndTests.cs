// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 acceptance: drives the monotonic-clock fixture
    /// which exercises <c>monotonic-clock::now</c>,
    /// <c>get-resolution</c>, and the two async waits
    /// (<c>wait-for</c>, <c>wait-until</c>). This is the first
    /// non-cli-hello fixture that drives the canon-async
    /// cooperative-yield path: each <c>wait_for(MILLISECOND)</c>
    /// awaits a host Task.Delay through the dispatcher. Wrapped
    /// in a wall-clock timeout — Phase 6 cooperative-yield work
    /// may still be incomplete.
    /// </summary>
    public class MonotonicClockEndToEndTests
    {
        private readonly ITestOutputHelper _output;
        public MonotonicClockEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Component_structure_smoke()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(MonotonicClockEndToEndTests), "monotonic-clock.wasm");
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            var component = ComponentBinaryParser.Parse(stream);
            _output.WriteLine(
                $"Sections: {component.RawSections.Count}, " +
                $"Core modules: {component.CoreModuleCount}, " +
                $"Exports: {component.Exports.Count}, " +
                $"Canons: {component.Canons.Count}");
            foreach (var e in component.Exports)
                _output.WriteLine($"  export: {e.Name} ({e.Sort})");
        }

        [Fact]
        public void MonotonicClock_run_completes_without_trap()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(MonotonicClockEndToEndTests), "monotonic-clock.wasm");
            var bytes = File.ReadAllBytes(path);

            var host = new WasiPreview3Host(new WasiPreview3HostBuilder());
            var instance = Wasip3FixtureHarness.InstantiateWithHost(bytes, host);

            host.Dispatcher = instance.AsyncDispatcher;

            WitBindgenScaffoldingBinder.ResetTrace();
            var task = Task.Run(() =>
            {
                try
                {
                    instance.InvokeCoreAsyncLift(
                        "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run");
                    return (object?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            if (!task.Wait(TimeSpan.FromSeconds(5)))
            {
                DumpScaffoldTrace();
                Assert.Fail(
                    "MonotonicClock.run() did not complete in 5s — " +
                    "likely the canon-async cooperative-yield path " +
                    "is incomplete (Phase 6). See test output for " +
                    "the scaffolding trace.");
            }
            if (task.Result is Exception ex2)
            {
                _output.WriteLine(
                    $"Invocation threw: {ex2.GetType().Name}: {ex2.Message}");
                throw ex2;
            }
        }

        private void DumpScaffoldTrace()
        {
            var trace = WitBindgenScaffoldingBinder.SnapshotTrace();
            if (trace.Count == 0)
            {
                _output.WriteLine(
                    "Scaffolding trace empty — set " +
                    "WACS_TRACE_SCAFFOLD=1 to enable.");
                return;
            }
            _output.WriteLine("Scaffolding call counts:");
            foreach (var kv in trace.OrderByDescending(p => p.Value))
                _output.WriteLine($"  {kv.Value,6}  {kv.Key}");
        }
    }
}
