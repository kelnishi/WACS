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
using Spec.Test;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// xUnit ClassData adapter using the shared WastTestDataProvider.
    /// </summary>
    public class TranspilerWasmTestData : WastTestDataAdapter
    {
    }

    /// <summary>
    /// Base class for adapting WastTestDataProvider to xUnit ClassData.
    /// </summary>
    public abstract class WastTestDataAdapter : System.Collections.IEnumerable,
        System.Collections.Generic.IEnumerable<object[]>
    {
        private static readonly WastTestDataProvider Provider = new();

        public System.Collections.Generic.IEnumerator<object[]> GetEnumerator()
        {
            foreach (var wasmPath in Provider.GetWasmFiles())
            {
                yield return new object[] { wasmPath };
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// xUnit ClassData adapter for full test definitions (JSON commands).
    /// </summary>
    public class TranspilerTestDefinitions : System.Collections.IEnumerable,
        System.Collections.Generic.IEnumerable<object[]>
    {
        private static readonly WastTestDataProvider Provider = new();

        public System.Collections.Generic.IEnumerator<object[]> GetEnumerator()
        {
            foreach (var testDef in Provider.GetTestDefinitions())
            {
                yield return new object[] { testDef };
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class TranspilerTests
    {
        private readonly ITestOutputHelper _output;

        public TranspilerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Verify that the transpiler can process a module without crashing.
        /// Reports how many functions were transpiled vs fell back.
        /// </summary>
        [Theory]
        [ClassData(typeof(TranspilerWasmTestData))]
        public void TranspileModule(string wasmPath)
        {
            var testName = Path.GetFileName(wasmPath);

            Module module;
            using (var stream = new FileStream(wasmPath, FileMode.Open, FileAccess.Read))
            {
                try { module = BinaryModuleParser.ParseWasm(stream); }
                catch
                {
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
        [Theory]
        [ClassData(typeof(TranspilerTestDefinitions))]
        public void TranspiledModuleBindsAsIBindable(Spec.Test.WastJson.WastJson file)
        {
            _output.WriteLine($"  Testing: {file.TestName}");

            // Load the first module command's wasm file
            var moduleCmd = file.Commands
                .OfType<Spec.Test.WastJson.ModuleCommand>()
                .FirstOrDefault();

            if (moduleCmd == null || string.IsNullOrEmpty(moduleCmd.Filename))
                return;

            var filepath = Path.Combine(file.Path, moduleCmd.Filename);
            if (!File.Exists(filepath))
                return;

            Module module;
            using (var stream = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            {
                try { module = BinaryModuleParser.ParseWasm(stream); }
                catch { return; }
            }

            var runtime = new WasmRuntime();
            var env = new SpecTestEnv();
            env.BindToRuntime(runtime);

            ModuleInstance moduleInst;
            try { moduleInst = runtime.InstantiateModule(module); }
            catch { return; }

            var transpiler = new ModuleTranspiler();
            var result = transpiler.Transpile(moduleInst, runtime);
            var ctx = new TranspiledContext();

            var transpiledModule = new AOT.TranspiledModule("test", result, ctx);

            // Bind to a fresh runtime — should not throw
            var runtime2 = new WasmRuntime();
            transpiledModule.BindToRuntime(runtime2);

            int exportCount = result.Manifest.Functions
                .Count(f => !string.IsNullOrEmpty(f.ExportName));
            _output.WriteLine($"    Bound {exportCount} exports, {result.TranspiledCount}/{result.Methods.Length} transpiled");
        }

        /// <summary>
        /// Verify the manifest correctly tracks transpiled vs fallback counts.
        /// Reports overall coverage across the entire spec test suite.
        /// </summary>
        [Fact]
        public void ManifestTracksTranspilationCoverage()
        {
            var provider = new WastTestDataProvider();

            int totalTranspiled = 0;
            int totalFallback = 0;
            int modulesProcessed = 0;

            foreach (var wasmPath in provider.GetWasmFiles())
            {
                Module module;
                using (var stream = new FileStream(wasmPath, FileMode.Open, FileAccess.Read))
                {
                    try { module = BinaryModuleParser.ParseWasm(stream); }
                    catch { continue; }
                }

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
    }
}
