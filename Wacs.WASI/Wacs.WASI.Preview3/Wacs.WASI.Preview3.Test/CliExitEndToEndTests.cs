// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.WASI.Preview3.Cli;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 acceptance: drives the cli-exit fixture which
    /// calls <c>exit(Err)</c> mid-run(). The default
    /// <see cref="ExitHandler"/> throws
    /// <see cref="ExitException"/> carrying exit code 1 — the
    /// host's natural translation of WASI's "terminal,
    /// divergent" exit semantics. The acceptance is that the
    /// async-lift invocation surfaces that exception, not that
    /// the run() body returns Ok.
    /// </summary>
    public class CliExitEndToEndTests
    {
        private readonly ITestOutputHelper _output;
        public CliExitEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Component_structure_smoke()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(CliExitEndToEndTests), "cli-exit.wasm");
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
        public void CliExit_throws_ExitException_with_code_1()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(CliExitEndToEndTests), "cli-exit.wasm");
            var bytes = File.ReadAllBytes(path);

            var host = new WasiPreview3Host(new WasiPreview3HostBuilder());
            var instance = Wasip3FixtureHarness.InstantiateWithHost(bytes, host);
            host.Dispatcher = instance.AsyncDispatcher;

            var ex = Assert.Throws<ExitException>(() =>
                instance.InvokeCoreAsyncLift(
                    "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run"));
            Assert.Equal((byte)1, ex.ExitCode);
        }
    }
}
