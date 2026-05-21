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
using Wacs.Core.Runtime.Types;
using Wacs.WASI.Preview3.Http;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Drives each wasip3 http fixture through the same harness:
    /// instantiate the component with a default
    /// <see cref="WasiPreview3Host"/>, invoke <c>run()</c> with a
    /// 15-second hang guard. Fixtures that don't make outbound
    /// requests (the field / request / response types) need no
    /// extra wiring; <c>http-service</c> exercises the
    /// <c>handle</c> outgoing-request path and may require
    /// additional host plumbing if it spins.
    /// </summary>
    public class HttpFixtureTests
    {
        private readonly ITestOutputHelper _output;
        public HttpFixtureTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public static System.Collections.Generic.IEnumerable<object[]> Fixtures()
        {
            // No HTTP fixtures are exercised yet. See the
            // class XML comment on Run_completes_without_trap
            // for the gating issues — each fixture stays
            // xfail'd by being omitted from this collection
            // until its blockers land.
            //
            // Concretely:
            //  * http-fields / http-request / http-response:
            //    instantiate cleanly with the 0.1.65 fields.
            //    append retptr fix but hang during run() in
            //    the same shape filesystem-read-directory hit
            //    pre-0.1.64 — needs a guest-panic surface to
            //    diagnose.
            //  * http-service: fails to instantiate — the
            //    [task-return]handle import lowers to 8 flat
            //    slots (i32 i32 i32 i64 i32 i32 i32 i32)
            //    because the export returns
            //    `result<response, error-code>`; our scaffolding
            //    binder's generic task-return handler is a
            //    single i32 disc. Needs the export-typed
            //    task-return generator the source-gen registry
            //    was scoped for.
            //
            // Returning a single Skip placeholder so xUnit
            // doesn't fail the Theory with "no data".
            yield return new object[] { "http-fields" };
            yield return new object[] { "http-response" };
            yield return new object[] { "http-request" };
            yield return new object[] { "http-service" };
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Run_completes_without_trap(string fixtureName)
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(HttpFixtureTests), fixtureName + ".wasm");
            var bytes = File.ReadAllBytes(path);

            var host = new WasiPreview3Host(new WasiPreview3HostBuilder());
            var instance = Wasip3FixtureHarness.InstantiateWithHost(bytes, host);
            host.Dispatcher = instance.AsyncDispatcher;

            var stderrCapture = new StringWriter();
            var originalErr = Console.Error;
            Console.SetError(stderrCapture);
            HostFunction.ResetProfile();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var runTask = Task.Run(() =>
            {
                try
                {
                    if (fixtureName == "http-service")
                    {
                        // http-service exports wasi:http/handler#handle,
                        // not wasi:cli/run. Build a host-side Request,
                        // hand its handle to handle().
                        var req = new Request(
                            new Fields(),
                            body: null,
                            trailers: null);
                        var reqHandle = host.RequestHandles
                            .Allocate(req);
                        instance.InvokeCoreAsyncLift(
                            "[async-lift]wasi:http/handler@" +
                            "0.3.0-rc-2026-03-15#handle",
                            reqHandle);
                    }
                    else
                    {
                        instance.InvokeCoreAsyncLift(
                            "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run");
                    }
                    return (object?)null;
                }
                catch (Exception ex) { return ex; }
            });
            if (!runTask.Wait(TimeSpan.FromSeconds(60)))
            {
                Console.SetError(originalErr);
                sw.Stop();
                var trace = WitBindgenScaffoldingBinder.SnapshotTrace();
                _output.WriteLine(
                    $"Scaffolding trace ({trace.Count} entries):");
                foreach (var kv in trace.OrderByDescending(p => p.Value))
                    _output.WriteLine($"  {kv.Value,6}  {kv.Key}");
                var captured = stderrCapture.ToString();
                if (!string.IsNullOrEmpty(captured))
                    _output.WriteLine("--- captured stderr ---\n" + captured);
                DumpHostProfile(sw.Elapsed);
                Assert.Fail($"{fixtureName}.run() hung past 300s.");
            }
            Console.SetError(originalErr);
            sw.Stop();
            var capturedOk = stderrCapture.ToString();
            if (!string.IsNullOrEmpty(capturedOk))
                _output.WriteLine("--- captured stderr ---\n" + capturedOk);
            DumpHostProfile(sw.Elapsed);
            if (runTask.Result is Exception ex2)
            {
                _output.WriteLine(
                    $"Invocation threw: {ex2.GetType().Name}: {ex2.Message}");
                throw ex2;
            }
        }

        private void DumpHostProfile(TimeSpan totalElapsed)
        {
            var prof = HostFunction.SnapshotProfile();
            if (prof.Count == 0) return;
            long totalCalls = 0;
            long totalTicks = 0;
            foreach (var (_, v) in prof) { totalCalls += v.Calls; totalTicks += v.Ticks; }
            _output.WriteLine($"=== Host profile (total elapsed " +
                $"{totalElapsed.TotalSeconds:F2}s, host invokes " +
                $"{totalCalls} calls / {totalTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F1}ms) ===");
            _output.WriteLine($"{"calls",10} {"total ms",12} " +
                $"{"avg us",10}  {"name"}");
            foreach (var kv in prof.OrderByDescending(p => p.Value.Ticks).Take(20))
            {
                double ms = kv.Value.Ticks * 1000.0
                    / System.Diagnostics.Stopwatch.Frequency;
                double us = kv.Value.Calls > 0
                    ? ms * 1000.0 / kv.Value.Calls : 0;
                _output.WriteLine($"{kv.Value.Calls,10} " +
                    $"{ms,12:F2} {us,10:F2}  {kv.Key}");
            }
        }
    }
}
