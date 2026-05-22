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
using Xunit;
using Xunit.Abstractions;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Drives each wasip3 sockets fixture through the same
    /// harness: instantiate the component with a default
    /// <see cref="WasiPreview3Host"/>, invoke <c>run()</c>
    /// with a hang guard. Sockets fixtures need the socket
    /// host bindings to be wired up by the default host
    /// builder (no extra preopens / capabilities — the
    /// fixtures bind to ephemeral loopback ports).
    /// </summary>
    public class SocketsFixtureTests
    {
        private readonly ITestOutputHelper _output;
        public SocketsFixtureTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public static System.Collections.Generic.IEnumerable<object[]> Fixtures()
        {
            yield return new object[] { "sockets-tcp-bind" };
            yield return new object[] { "sockets-tcp-connect" };
            yield return new object[] { "sockets-tcp-listen" };
            yield return new object[] { "sockets-tcp-properties" };
            yield return new object[] { "sockets-tcp-send" };
            yield return new object[] { "sockets-udp-bind" };
            yield return new object[] { "sockets-udp-connect" };
            yield return new object[] { "sockets-udp-properties" };
            yield return new object[] { "sockets-udp-receive" };
            yield return new object[] { "sockets-udp-send" };
        }

        // xfail: integration tests that require an external
        // driver process to drive the listener / writer half.
        // sockets-echo binds + listens but never creates a
        // client; its design expects wasi-testsuite's harness
        // to attach an out-of-process TCP client.
        // sockets-tcp-receive's test_multiple_receive /
        // test_drop_read_half both .await on a future whose
        // resolution requires the peer to FIN, which only
        // happens when the harness orchestrates a close — not
        // exercised in-process.
        public static System.Collections.Generic.IEnumerable<object[]> IntegrationFixtures()
        {
            yield return new object[] { "sockets-echo" };
            yield return new object[] { "sockets-tcp-receive" };
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Run_completes_without_trap(string fixtureName)
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(SocketsFixtureTests), fixtureName + ".wasm");
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
                    instance.InvokeCoreAsyncLift(
                        "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run");
                    return (object?)null;
                }
                catch (Exception ex) { return ex; }
            });
            if (!runTask.Wait(TimeSpan.FromSeconds(30)))
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
                Assert.Fail($"{fixtureName}.run() hung past 30s.");
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
