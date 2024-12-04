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

using System;
using System.IO;
using Wacs.Core;
using Wacs.Core.Runtime;
using Xunit;

namespace Spec.Test
{
    public class ExtensionTests
    {
        [Fact]
        public void TailCallFactorial()
        {
            var runtime = new WasmRuntime();
            runtime.TranspileModules = false;
            
            using var fileStream = new FileStream("../../../engine/tailcalls.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("tailcalls", moduleInst);

            var fa = runtime.GetExportedFunction(("tailcalls", "factorial"));
            var invoker = runtime.CreateInvoker<Func<long, long>>(fa);

            Assert.Equal(479_001_600, invoker(12));
            Assert.Equal(2_432_902_008_176_640_000 , invoker(20));
        }
        
        [Fact]
        public void TailCallFactorialTranspiled()
        {
            var runtime = new WasmRuntime();
            runtime.TranspileModules = true;
            
            using var fileStream = new FileStream("../../../engine/tailcalls.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("tailcalls", moduleInst);

            var fa = runtime.GetExportedFunction(("tailcalls", "factorial"));
            var invoker = runtime.CreateInvoker<Func<long, long>>(fa);

            Assert.Equal(479_001_600, invoker(12));
            Assert.Equal(2_432_902_008_176_640_000 , invoker(20));
        }
    }
}