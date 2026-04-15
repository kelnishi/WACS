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
using Spec.Test.WastJson;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// xUnit ClassData adapter for full test definitions (JSON commands).
    /// Used by both TranspilerTests and AotSpecTests.
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
        /// Verify that the transpiler can process each module in a wast test plan
        /// without crashing. Walks the command sequence inline, just like the core
        /// test suite, so that imports are available and only valid modules are tested.
        /// </summary>
        [Theory]
        [ClassData(typeof(TranspilerTestDefinitions))]
        public void TranspileModule(WastJson file)
        {
            _output.WriteLine($"Transpile: {file.TestName}");
            var env = new SpecTestEnv();
            var runtime = new WasmRuntime();
            env.BindToRuntime(runtime);
            runtime.TranspileModules = false;

            int modulesTranspiled = 0;
            int totalFunctions = 0;
            int totalTranspiled = 0;

            Module? module = null;
            foreach (var command in file.Commands)
            {
                // Only process module loads — skip assertions and other commands
                if (command is not ModuleCommand)
                {
                    try { command.RunTest(file, ref runtime, ref module); }
                    catch { /* assertion failures don't matter for transpile test */ }
                    continue;
                }

                try
                {
                    // Load the module through the standard path (provides imports)
                    command.RunTest(file, ref runtime, ref module);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"    Module load failed: {ex.Message}");
                    continue;
                }

                // Transpile the just-loaded module
                ModuleInstance moduleInst;
                try { moduleInst = runtime.GetModule(null); }
                catch { continue; }

                var transpiler = new ModuleTranspiler();
                TranspilationResult result;
                try
                {
                    result = transpiler.Transpile(moduleInst, runtime);
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Transpilation crashed on {file.TestName}: {ex.Message}");
                    return;
                }

                Assert.NotNull(result.Assembly);
                Assert.NotNull(result.FunctionsType);
                Assert.Equal(result.TranspiledCount + result.FallbackCount, result.Methods.Length);

                modulesTranspiled++;
                totalFunctions += result.Methods.Length;
                totalTranspiled += result.TranspiledCount;
            }

            if (modulesTranspiled > 0)
            {
                _output.WriteLine($"    {modulesTranspiled} modules, {totalTranspiled}/{totalFunctions} functions transpiled");
            }
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

            foreach (var testDef in provider.GetTestDefinitions())
            {
                var env = new SpecTestEnv();
                var runtime = new WasmRuntime();
                env.BindToRuntime(runtime);
                runtime.TranspileModules = false;

                Module? module = null;
                foreach (var command in testDef.Commands)
                {
                    if (command is not ModuleCommand)
                    {
                        try { command.RunTest(testDef, ref runtime, ref module); }
                        catch { }
                        continue;
                    }

                    try { command.RunTest(testDef, ref runtime, ref module); }
                    catch { continue; }

                    ModuleInstance moduleInst;
                    try { moduleInst = runtime.GetModule(null); }
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
            }

            int total = totalTranspiled + totalFallback;
            double pct = total > 0 ? (double)totalTranspiled / total * 100 : 0;
            _output.WriteLine($"Overall: {modulesProcessed} modules, {totalTranspiled}/{total} functions transpiled ({pct:F1}%)");
        }
    }
}
