// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 acceptance: the run-with-err fixture is one line —
    /// <c>async fn run() -> Result&lt;(), ()&gt; { Err(()) }</c>.
    /// Exercises the task.return path for the Err discriminant
    /// (= 1). InvokeCoreAsyncLift surfaces the disc as the lift
    /// adapter's return value, so the test asserts it
    /// observed 1.
    /// </summary>
    public class RunWithErrEndToEndTests
    {
        private readonly ITestOutputHelper _output;
        public RunWithErrEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Component_structure_smoke()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(RunWithErrEndToEndTests), "run-with-err.wasm");
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
        public void Run_returns_Err_discriminant_1()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(RunWithErrEndToEndTests), "run-with-err.wasm");
            var bytes = File.ReadAllBytes(path);

            var host = new WasiPreview3Host(new WasiPreview3HostBuilder());
            var instance = Wasip3FixtureHarness.InstantiateWithHost(bytes, host);
            host.Dispatcher = instance.AsyncDispatcher;

            var result = instance.InvokeCoreAsyncLift(
                "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run");
            _output.WriteLine(
                $"task-return: {result ?? "(null)"} " +
                $"({result?.GetType().Name ?? "null"})");
            Assert.Equal(1, result);
        }
    }
}
