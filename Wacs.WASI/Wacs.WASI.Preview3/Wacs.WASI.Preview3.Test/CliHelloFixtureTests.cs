// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Linq;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 4 acceptance fixture: <c>cli-hello.component.wasm</c>
    /// is a wit-bindgen-compiled component exporting
    /// <c>wasi:cli/run@0.3.0-rc-2026-03-15</c> which writes
    /// <c>"hello, wasip3\n"</c> to stdout when run.
    ///
    /// <para>These tests pin the structural shape of the
    /// vendored component — parses cleanly, has the expected
    /// imports + exports. The end-to-end invocation
    /// (interpreter+transpiler executing run() with stdout
    /// captured to a memory buffer) needs the cooperative-yield
    /// refactor + the wasip2 facade bindings (the wit-bindgen
    /// toolchain auto-injects wasip2 shims since
    /// wasm32-wasip3 triple doesn't exist), so it lands in a
    /// follow-up.</para>
    /// </summary>
    public class CliHelloFixtureTests
    {
        private static string FixturePath(string name)
        {
            return Path.Combine(
                Path.GetDirectoryName(typeof(CliHelloFixtureTests).Assembly.Location)
                    ?? string.Empty,
                "Fixtures", name);
        }

        [Fact]
        public void Fixture_wasm_is_present_and_parseable()
        {
            var path = FixturePath("cli-hello.wasm");
            Assert.True(File.Exists(path),
                $"cli-hello.wasm not found at {path} — confirm " +
                "the Fixtures/ CopyToOutputDirectory item in " +
                "Wacs.WASI.Preview3.Test.csproj.");

            using var stream = File.OpenRead(path);
            var component = ComponentBinaryParser.Parse(stream);
            Assert.NotNull(component);
            Assert.NotEmpty(component.RawSections);
        }

        [Fact]
        public void Fixture_exports_wasi_cli_run()
        {
            using var stream = File.OpenRead(FixturePath("cli-hello.wasm"));
            var component = ComponentBinaryParser.Parse(stream);

            // wit-bindgen + wasm-component-ld emit both the
            // wasip2 (0.2.0) and wasip3 (0.3.0-rc) variants of
            // wasi:cli/run.
            var runExports = component.Exports
                .Where(e => e.Name.Contains("wasi:cli/run"))
                .ToList();
            Assert.NotEmpty(runExports);
            Assert.Contains(runExports,
                e => e.Name.Contains("0.3.0-rc-2026-03-15"));
        }

        [Fact]
        public void Fixture_imports_wasi_cli_stdout_at_rc_2026_03_15()
        {
            // Confirm the component depends on the wasip3
            // stdout interface (the one WACS.WASI.Preview3
            // binds), not just the wasip2 facade.
            using var stream = File.OpenRead(FixturePath("cli-hello.wasm"));
            var component = ComponentBinaryParser.Parse(stream);

            // Imports live in RawSections — check there's at
            // least one section referencing the wasip3 stdout
            // interface name in its raw bytes.
            var allBytes = File.ReadAllBytes(FixturePath("cli-hello.wasm"));
            string asString = System.Text.Encoding.UTF8.GetString(allBytes);
            Assert.Contains(
                "wasi:cli/stdout@0.3.0-rc-2026-03-15", asString);
        }

        [Fact]
        public void Operations_sidecar_documents_run_then_stdout_read()
        {
            // Parses the JSON operations file as a smoke-check
            // that the vendored sidecar is well-formed.
            var json = File.ReadAllText(FixturePath("cli-hello.json"));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var ops = doc.RootElement.GetProperty("operations");
            Assert.Equal(3, ops.GetArrayLength());

            Assert.Equal("run",
                ops[0].GetProperty("type").GetString());
            Assert.Equal("read",
                ops[1].GetProperty("type").GetString());
            Assert.Equal("stdout",
                ops[1].GetProperty("id").GetString());
            Assert.Equal("hello, wasip3\n",
                ops[1].GetProperty("payload").GetString());
            Assert.Equal("wait",
                ops[2].GetProperty("type").GetString());
        }
    }
}
