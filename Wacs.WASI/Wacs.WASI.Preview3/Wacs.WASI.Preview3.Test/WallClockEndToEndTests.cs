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
    /// Phase 5 acceptance: drives the wall-clock fixture
    /// (system-clock::now + get-resolution, no streams or waits)
    /// through ComponentInstance.InvokeCoreAsyncLift. Guest's
    /// async run() body trap-aborts on assertion failure, so a
    /// clean lift return is the pass signal.
    /// </summary>
    public class WallClockEndToEndTests
    {
        private readonly ITestOutputHelper _output;
        public WallClockEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Component_structure_smoke()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(WallClockEndToEndTests), "wall-clock.wasm");
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
        public void WallClock_run_completes_without_trap()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(WallClockEndToEndTests), "wall-clock.wasm");
            var bytes = File.ReadAllBytes(path);

            var host = new WasiPreview3Host(new WasiPreview3HostBuilder());
            var instance = Wasip3FixtureHarness.InstantiateWithHost(bytes, host);

            host.Dispatcher = instance.AsyncDispatcher;
            instance.InvokeCoreAsyncLift(
                "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run");
        }
    }
}
