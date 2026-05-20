// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
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
    /// Phase 5 acceptance: cli-stdio exercises the drop-end
    /// cleanup paths:
    /// <list type="number">
    ///   <item>get stdin reader, drop it immediately, await
    ///         <c>stdin_result</c></item>
    ///   <item>create a guest-owned stream pair, write 1 byte
    ///         into stdout via <c>write-via-stream(rx)</c>,
    ///         drop the tx half</item>
    ///   <item>same for stderr</item>
    /// </list>
    /// </summary>
    public class CliStdioEndToEndTests
    {
        private readonly ITestOutputHelper _output;
        public CliStdioEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Component_structure_smoke()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(CliStdioEndToEndTests), "cli-stdio.wasm");
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
        public void CliStdio_drop_paths_complete()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(CliStdioEndToEndTests), "cli-stdio.wasm");
            var bytes = File.ReadAllBytes(path);

            using var stdout = new MemoryStream();
            using var stderr = new MemoryStream();
            var host = new WasiPreview3Host(new WasiPreview3HostBuilder
            {
                // Empty stdin: the guest only drops the reader
                // then awaits the completion future, which
                // resolves Ok immediately.
                Stdin = new EmptyStdin(),
                Stdout = new StreamBackedSink(stdout),
                Stderr = new StreamBackedSink(stderr),
            });

            var instance = Wasip3FixtureHarness.InstantiateWithHost(bytes, host);
            host.Dispatcher = instance.AsyncDispatcher;

            instance.InvokeCoreAsyncLift(
                "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run");

            // The two stream-write subtests each write a single
            // zero byte to stdout/stderr.
            Assert.Equal(new byte[] { 0 }, stdout.ToArray());
            Assert.Equal(new byte[] { 0 }, stderr.ToArray());
        }

        /// <summary>
        /// Stdin that resolves the completion future Ok with no
        /// bytes — for guests that drop the reader without
        /// reading. The dispatcher gets an already-closed
        /// stream and an already-resolved future.
        /// </summary>
        private sealed class EmptyStdin : IStdin
        {
            public (int streamHandle, int futureHandle, Task ReadCompletion)
                ReadViaStream(AsyncDispatcher dispatcher)
            {
                var streamHandle = dispatcher.StreamNew(typeIdx: 0);
                var futureHandle = dispatcher.FutureNew(typeIdx: 0);
                dispatcher.StreamDropWritable(streamHandle);
                dispatcher.FutureWrite(futureHandle, /* ok */ null);
                return (streamHandle, futureHandle, Task.CompletedTask);
            }
        }
    }
}
