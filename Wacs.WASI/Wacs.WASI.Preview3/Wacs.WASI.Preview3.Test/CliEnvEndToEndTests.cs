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
    /// Phase 5 acceptance: drives the cli-env fixture which
    /// exercises the canonical-ABI lowerings for
    /// <c>list&lt;tuple&lt;string,string&gt;&gt;</c>,
    /// <c>list&lt;string&gt;</c>, and <c>option&lt;string&gt;</c>.
    /// The guest asserts an exact env+args set, so the test
    /// supplies the matching values via a fixed
    /// <see cref="IEnvironment"/> impl.
    /// </summary>
    public class CliEnvEndToEndTests
    {
        private readonly ITestOutputHelper _output;
        public CliEnvEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Component_structure_smoke()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(CliEnvEndToEndTests), "cli-env.wasm");
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
        public void CliEnv_run_completes_without_trap()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(CliEnvEndToEndTests), "cli-env.wasm");
            var bytes = File.ReadAllBytes(path);

            var host = new WasiPreview3Host(new WasiPreview3HostBuilder
            {
                // The cli-env Rust fixture asserts the exact
                // env+args set: env={foo=bar, baz=42},
                // args=["cli-env.wasm","a","b","42"],
                // cwd=None.
                Environment = new FixtureEnvironment(),
            });

            var instance = Wasip3FixtureHarness.InstantiateWithHost(bytes, host);
            host.Dispatcher = instance.AsyncDispatcher;

            instance.InvokeCoreAsyncLift(
                "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run");
        }

        private sealed class FixtureEnvironment : IEnvironment
        {
            public (string, string)[] GetEnvironment() =>
                new[] { ("foo", "bar"), ("baz", "42") };

            public string[] GetArguments() =>
                new[] { "cli-env.wasm", "a", "b", "42" };

            public string? GetInitialCwd() => null;
        }
    }
}
