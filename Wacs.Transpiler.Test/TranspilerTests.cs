// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Linq;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.Transpiler.Test
{
    public class TranspilerTests
    {
        private readonly ITestOutputHelper _output;
        private static readonly string? SpecTestDir;

        static TranspilerTests()
        {
            // Walk up from bin dir to find Spec.Test/generated-json
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                var candidate = Path.Combine(dir, "Spec.Test", "generated-json");
                if (Directory.Exists(candidate))
                {
                    SpecTestDir = candidate;
                    break;
                }
                dir = Path.GetDirectoryName(dir) ?? dir;
            }
        }

        public TranspilerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Verify that the transpiler processes modules without crashing,
        /// producing valid IL for functions it can handle.
        /// </summary>
        [Theory]
        [MemberData(nameof(GetSpecTestWasmFiles))]
        public void TranspileModule(string wasmPath)
        {
            var testName = Path.GetFileName(wasmPath);

            Module module;
            using (var stream = new FileStream(wasmPath, FileMode.Open, FileAccess.Read))
            {
                try
                {
                    module = BinaryModuleParser.ParseWasm(stream);
                }
                catch
                {
                    // Malformed wasm files (spec tests include intentionally broken modules)
                    _output.WriteLine($"  {testName}: skipped (parse error)");
                    return;
                }
            }

            var runtime = new WasmRuntime();

            ModuleInstance moduleInst;
            try
            {
                moduleInst = runtime.InstantiateModule(module);
            }
            catch
            {
                // Modules requiring imports we don't provide — skip
                _output.WriteLine($"  {testName}: skipped (missing imports)");
                return;
            }

            var transpiler = new ModuleTranspiler();
            var result = transpiler.Transpile(moduleInst, runtime);

            _output.WriteLine($"  {testName}: {result.TranspiledCount} transpiled, {result.FallbackCount} fallback");

            Assert.NotNull(result.Assembly);
            Assert.NotNull(result.FunctionsType);
            Assert.Equal(result.TranspiledCount + result.FallbackCount, result.Methods.Length);
        }

        /// <summary>
        /// Verify the IBindable pattern — transpiled exports bind to a runtime.
        /// </summary>
        [Fact]
        public void TranspiledModuleBindsAsIBindable()
        {
            if (SpecTestDir == null)
            {
                _output.WriteLine("Skipped: spec test directory not found");
                return;
            }

            var wasmFiles = Directory.GetFiles(SpecTestDir, "*.wasm", SearchOption.AllDirectories)
                .OrderBy(f => f)
                .Take(5)
                .ToList();

            foreach (var wasmPath in wasmFiles)
            {
                using var stream = new FileStream(wasmPath, FileMode.Open, FileAccess.Read);
                var module = BinaryModuleParser.ParseWasm(stream);

                var runtime = new WasmRuntime();
                ModuleInstance moduleInst;
                try
                {
                    moduleInst = runtime.InstantiateModule(module);
                }
                catch { continue; }

                var transpiler = new ModuleTranspiler();
                var result = transpiler.Transpile(moduleInst, runtime);
                var ctx = new TranspiledContext();

                var transpiledModule = new AOT.TranspiledModule("test", result, ctx);

                // Bind to a fresh runtime — should not throw
                var runtime2 = new WasmRuntime();
                transpiledModule.BindToRuntime(runtime2);

                int exportCount = result.Manifest.Functions
                    .Count(f => !string.IsNullOrEmpty(f.ExportName));
                _output.WriteLine($"  {Path.GetFileName(wasmPath)}: bound {exportCount} exports");
            }
        }

        /// <summary>
        /// Verify the manifest correctly tracks transpiled vs fallback counts.
        /// </summary>
        [Fact]
        public void ManifestTracksTranspilationCoverage()
        {
            if (SpecTestDir == null)
            {
                _output.WriteLine("Skipped: spec test directory not found");
                return;
            }

            int totalTranspiled = 0;
            int totalFallback = 0;
            int modulesProcessed = 0;

            var wasmFiles = Directory.GetFiles(SpecTestDir, "*.wasm", SearchOption.AllDirectories)
                .OrderBy(f => f);

            foreach (var wasmPath in wasmFiles)
            {
                using var stream = new FileStream(wasmPath, FileMode.Open, FileAccess.Read);
                Module module;
                try { module = BinaryModuleParser.ParseWasm(stream); }
                catch { continue; }

                var runtime = new WasmRuntime();
                ModuleInstance moduleInst;
                try { moduleInst = runtime.InstantiateModule(module); }
                catch { continue; }

                var transpiler = new ModuleTranspiler();
                TranspilationResult result;
                try { result = transpiler.Transpile(moduleInst, runtime); }
                catch { continue; }

                totalTranspiled += result.TranspiledCount;
                totalFallback += result.FallbackCount;
                modulesProcessed++;

                // Verify manifest consistency
                Assert.Equal(result.Methods.Length, result.Manifest.Functions.Count);
                Assert.Equal(result.TranspiledCount,
                    result.Manifest.Functions.Count(f => f.IsTranspiled));
                Assert.Equal(result.FallbackCount,
                    result.Manifest.Functions.Count(f => !f.IsTranspiled));
            }

            int total = totalTranspiled + totalFallback;
            double pct = total > 0 ? (double)totalTranspiled / total * 100 : 0;
            _output.WriteLine($"Overall: {modulesProcessed} modules, {totalTranspiled}/{total} functions transpiled ({pct:F1}%)");
        }

        public static TheoryData<string> GetSpecTestWasmFiles()
        {
            var data = new TheoryData<string>();
            if (SpecTestDir == null)
                return data;

            var wasmFiles = Directory.GetFiles(SpecTestDir, "*.wasm", SearchOption.AllDirectories)
                .OrderBy(f => f);

            foreach (var file in wasmFiles)
            {
                data.Add(file);
            }

            return data;
        }
    }
}
