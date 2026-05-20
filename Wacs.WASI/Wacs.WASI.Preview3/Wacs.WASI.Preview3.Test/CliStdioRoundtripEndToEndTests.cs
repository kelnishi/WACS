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
            using var stdout = new MemoryStream();
            using var stderr = new MemoryStream();

            // Pre-buffer stdin synchronously so the guest's
            // read sees all 13 bytes immediately. The default
            // StreamBackedStdin drains on a background task,
            // which races the guest's read and returns
            // Completed(0) on the first call — wit-bindgen-rt
            // then loops on `while Complete(_)`, hanging.
            var host = new WasiPreview3Host(new WasiPreview3HostBuilder
            {
                Stdin = new BufferedStdin(payloadBytes),
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

        /// <summary>
        /// Synchronously-pre-filled stdin: pushes every byte
        /// into the dispatcher's stream buffer before returning
        /// the handles, then drops the writable end. The guest's
        /// first read sees the full payload. Until the canon-
        /// async BLOCKED + waitable-set-wait path is wired, this
        /// is how a test feeds a fixed-size stdin without
        /// racing the guest.
        /// </summary>
        private sealed class BufferedStdin : IStdin
        {
            private readonly byte[] _bytes;
            public BufferedStdin(byte[] bytes) { _bytes = bytes; }

            public (int streamHandle, int futureHandle, Task ReadCompletion)
                ReadViaStream(AsyncDispatcher dispatcher)
            {
                var streamHandle = dispatcher.StreamNew(typeIdx: 0);
                var futureHandle = dispatcher.FutureNew(typeIdx: 0);
                for (int i = 0; i < _bytes.Length; i++)
                    dispatcher.StreamTryWrite(streamHandle, _bytes[i]);
                dispatcher.StreamDropWritable(streamHandle);
                dispatcher.FutureWrite(futureHandle, /* ok */ null);
                return (streamHandle, futureHandle, Task.CompletedTask);
            }
        }
    }
}
