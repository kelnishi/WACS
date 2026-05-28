// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.WASI.Preview3.Cli;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 acceptance: drives the cli-stdio-roundtrip
    /// fixture. The guest reads exactly 13 bytes from stdin
    /// via <c>read-via-stream</c>, asserts the read completed,
    /// then writes the same bytes to stdout AND stderr via
    /// <c>write-via-stream</c>. Closes both directions of the
    /// async stream flow end-to-end.
    /// </summary>
    public class CliStdioRoundtripEndToEndTests
    {
        private const string Payload = "hello-stdio!\n"; // 13 bytes UTF-8

        private readonly ITestOutputHelper _output;
        public CliStdioRoundtripEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Component_structure_smoke()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(CliStdioRoundtripEndToEndTests),
                "cli-stdio-roundtrip.wasm");
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            var component = ComponentBinaryParser.Parse(stream);
            _output.WriteLine(
                $"Sections: {component.RawSections.Count}, " +
                $"Core modules: {component.CoreModuleCount}, " +
                $"Exports: {component.Exports.Count}, " +
                $"Canons: {component.Canons.Count}");
        }

        [Fact]
        public void Roundtrip_stdin_to_stdout_and_stderr()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(CliStdioRoundtripEndToEndTests),
                "cli-stdio-roundtrip.wasm");
            var bytes = File.ReadAllBytes(path);

            var payloadBytes = Encoding.UTF8.GetBytes(Payload);
            Assert.Equal(13, payloadBytes.Length);
            var stdin = new MemoryStream(payloadBytes);
            using var stdout = new MemoryStream();
            using var stderr = new MemoryStream();

            // StreamBackedStdin drains on a background task; the
            // canon-lowered stream-read import blocks the CLR
            // thread until at least one byte is buffered, so the
            // background drain has a chance to push before the
            // guest's read returns.
            var host = new WasiPreview3Host(new WasiPreview3HostBuilder
            {
                Stdin = new StreamBackedStdin(stdin),
                Stdout = new StreamBackedSink(stdout),
                Stderr = new StreamBackedSink(stderr),
            });

            var instance = Wasip3FixtureHarness.InstantiateWithHost(bytes, host);
            host.Dispatcher = instance.AsyncDispatcher;

            instance.InvokeCoreAsyncLift(
                "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run");

            Assert.Equal(Payload, Encoding.UTF8.GetString(stdout.ToArray()));
            Assert.Equal(Payload, Encoding.UTF8.GetString(stderr.ToArray()));
        }
    }
}
