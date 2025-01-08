// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System.Linq;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Validation;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Spec.Test
{
    public class WastTests
    {
        private readonly ITestOutputHelper _output;

        public WastTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [ClassData(typeof(WastJsonTestData))]
        public void RunWast(WastJson.WastJson file)
        {
            _output.WriteLine($"Running test:{file.TestName}");
            SpecTestEnv env = new SpecTestEnv();
            WasmRuntime runtime = new();
            env.BindToRuntime(runtime);
            runtime.TranspileModules = false;
            
            Module? module = null;
            foreach (var command in file.Commands)
            {
                try
                {
                    _output.WriteLine($"    {command}");
                    var warnings = command.RunTest(file, ref runtime, ref module);
                    foreach (var error in warnings)
                    {
                        _output.WriteLine($"Warning: {error}");
                    }
                }
                catch (TestException exc)
                {
                    Assert.Fail(exc.Message);
                }
            }
        }

        [Theory(Skip = "Skip transpiled tests for now.")]
        // [Theory]
        [ClassData(typeof(WastJsonTestData))]
        public void RunWastTranspiled(WastJson.WastJson file)
        {
            if (!WastJsonTestData.RunTranspilerTests)
                throw SkipException.ForSkip("Skipping transpiled test");
            
            _output.WriteLine($"Running test:{file.TestName}");
            SpecTestEnv env = new SpecTestEnv();
            WasmRuntime runtime = new();
            env.BindToRuntime(runtime);

            runtime.TranspileModules = true;
            
            Module? module = null;
            foreach (var command in file.Commands)
            {
                try
                {
                    _output.WriteLine($"    {command}");
                    var warnings = command.RunTest(file, ref runtime, ref module);
                    foreach (var error in warnings)
                    {
                        _output.WriteLine($"Warning: {error}");
                    }
                }
                catch (TestException exc)
                {
                    Assert.Fail(exc.Message);
                }
            }
        }
    }
}