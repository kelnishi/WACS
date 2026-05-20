// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Text;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview3.Cli;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 4 acceptance end-to-end test driver. The full
    /// acceptance is "interpreter + transpiler execute cli-hello's
    /// run() with stdout captured against <c>"hello, wasip3\n"</c>".
    ///
    /// <para>Reaching that requires the cooperative-yield
    /// refactor + a substantial canon-async lift-adapter
    /// integration into <see cref="ComponentInstance"/>.Invoke
    /// (run() is an async-lifted export — its return value
    /// arrives via <c>task.return</c>, not via the core
    /// function's flat results, so Invoke's synchronous lift
    /// path doesn't apply). Plus binding the ~60 wit-bindgen
    /// scaffolding imports the toolchain auto-injects.</para>
    ///
    /// <para>These tests are diagnostic — they exercise the
    /// import-resolution gauntlet up to its first failure and
    /// surface the gap to whoever picks up the follow-up.</para>
    /// </summary>
    public class CliHelloEndToEndTests
    {
        private readonly ITestOutputHelper _output;
        public CliHelloEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string FixturePath(string name)
        {
            return Path.Combine(
                Path.GetDirectoryName(typeof(CliHelloEndToEndTests).Assembly.Location)
                    ?? string.Empty,
                "Fixtures", name);
        }

        /// <summary>
        /// Parse-level smoke: dump the component's exports,
        /// section counts, and core-module count. Pins the
        /// structural shape independent of any host bindings.
        /// </summary>
        [Fact]
        public void Component_structure_smoke()
        {
            var bytes = File.ReadAllBytes(FixturePath("cli-hello.wasm"));
            using var stream = new MemoryStream(bytes);
            var component = ComponentBinaryParser.Parse(stream);
            _output.WriteLine(
                $"Sections: {component.RawSections.Count}, " +
                $"Core modules: {component.CoreModuleCount}, " +
                $"Nested: {component.NestedComponentCount}, " +
                $"Exports: {component.Exports.Count}, " +
                $"Canons: {component.Canons.Count}");
            foreach (var e in component.Exports)
            {
                _output.WriteLine($"  export: {e.Name} ({e.Sort})");
            }
        }

        /// <summary>
        /// Attempt instantiation with WasiPreview3Host configured
        /// for stdout capture. Catches the first
        /// unbound-function error and reports it via the output
        /// helper — drives whoever's adding the next binding.
        /// </summary>
        [Fact]
        public void Instantiation_attempt_surfaces_first_missing_import()
        {
            var bytes = File.ReadAllBytes(FixturePath("cli-hello.wasm"));
            using var capturedStdout = new MemoryStream();
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    Stdout = new StreamBackedSink(capturedStdout),
                });

            // Bind the wit-component-synthesized task-return
            // for the async-lifted run() export. (Beyond this
            // is the wit-bindgen-emitted wit_stream/wit_future
            // scaffolding — ~60 helper imports per facade
            // interface.)
            void ConfigureKnownExports(WasmRuntime runtime)
            {
                runtime.BindHostFunction(
                    ("[export]wasi:cli/run@0.3.0-rc-2026-03-15",
                     "[task-return]run"),
                    (Action<ExecContext, int>)((_, _) => { }));
            }

            try
            {
                var instance = ComponentInstance.Instantiate(bytes,
                    runtime =>
                    {
                        host.BindToRuntime(runtime);
                        ConfigureKnownExports(runtime);
                    });
                _output.WriteLine("Instantiation succeeded — promote " +
                    "this test to the actual stdout-capture run.");
            }
            catch (Exception ex)
            {
                _output.WriteLine(
                    $"First failure (expected — pending wit-bindgen " +
                    $"scaffolding binder):\n" +
                    $"  {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    _output.WriteLine(
                        $"  Inner: {ex.InnerException.GetType().Name}: " +
                        $"{ex.InnerException.Message}");
            }
        }

        /// <summary>
        /// Enumerate the cli-hello core modules' function
        /// imports and categorize what the next round of
        /// bindings needs to cover. Surfaces totals by
        /// (module-prefix, function-prefix) so the binder
        /// scope is concrete rather than guesswork.
        /// </summary>
        [Fact]
        public void Core_module_imports_inventory()
        {
            var bytes = File.ReadAllBytes(FixturePath("cli-hello.wasm"));
            using var stream = new MemoryStream(bytes);
            var component = ComponentBinaryParser.Parse(stream);

            int totalCores = 0;
            int totalFuncImports = 0;
            var moduleCounts = new System.Collections.Generic
                .Dictionary<string, int>();
            var prefixCounts = new System.Collections.Generic
                .Dictionary<string, int>();

            foreach (var coreBytes in component.CoreModuleBinaries)
            {
                totalCores++;
                using var coreStream = new MemoryStream(coreBytes);
                var module = BinaryModuleParser.ParseWasm(coreStream);
                foreach (var import in module.Imports)
                {
                    if (!(import.Desc is Module.ImportDesc.FuncDesc))
                        continue;
                    totalFuncImports++;
                    moduleCounts.TryGetValue(import.ModuleName, out int m);
                    moduleCounts[import.ModuleName] = m + 1;

                    // Pull the leading [bracketed] tag if present.
                    string prefix = import.Name.StartsWith("[")
                        ? import.Name.Substring(0,
                            Math.Min(import.Name.IndexOf(']') + 1,
                                     import.Name.Length))
                        : "(unbracketed)";
                    prefixCounts.TryGetValue(prefix, out int p);
                    prefixCounts[prefix] = p + 1;
                }
            }

            _output.WriteLine(
                $"{totalCores} core module(s), " +
                $"{totalFuncImports} function imports total");
            _output.WriteLine("Per-module breakdown:");
            foreach (var kvp in moduleCounts.OrderByDescending(k => k.Value))
            {
                _output.WriteLine($"  {kvp.Value,4} {kvp.Key}");
            }
            _output.WriteLine("Per-name-prefix breakdown:");
            foreach (var kvp in prefixCounts.OrderByDescending(k => k.Value))
            {
                _output.WriteLine($"  {kvp.Value,4} {kvp.Key}");
            }
        }

        /// <summary>
        /// The full end-to-end acceptance. Skipped pending the
        /// canon-async lift-adapter integration into Invoke +
        /// the wit-bindgen scaffolding binder. Promote when
        /// those land.
        /// </summary>
        [Fact(Skip = "Pending canon-async lift-adapter Invoke wiring + wit-bindgen scaffolding binder.")]
        public void CliHello_writes_expected_stdout()
        {
            var bytes = File.ReadAllBytes(FixturePath("cli-hello.wasm"));

            using var capturedStdout = new MemoryStream();
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    Stdout = new StreamBackedSink(capturedStdout),
                });

            var instance = ComponentInstance.Instantiate(bytes,
                runtime => host.BindToRuntime(runtime));
            instance.Invoke("wasi:cli/run@0.3.0-rc-2026-03-15#run");

            capturedStdout.Position = 0;
            var captured = Encoding.UTF8.GetString(capturedStdout.ToArray());
            Assert.Equal("hello, wasip3\n", captured);
        }
    }
}
