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
    public class BindingTests
    {
        [Fact]
        public void BindStackBinder()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<hostInOut>(("env","bound_host"), BoundHost);
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "4x4"));
            var invoker = runtime.CreateStackInvoker(fa);
            
            var result = invoker(new Value[]{1, 1, 1f, 1.0});
            
            Assert.Equal(1 * 2, (int)result[0]);
            Assert.Equal(1 * 3, (long)result[1]);
            Assert.Equal(1f * 4f, (float)result[2]);
            Assert.Equal(1.0 * 5.0, (double)result[3]);
        }

        [Fact]
        public void BindParamAndResultI32()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<hostInOut>(("env","bound_host"), BoundHost);
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "i32"));
            var invoker = runtime.CreateInvoker<Func<int, int>>(fa);
            
            Assert.Equal(1 * 2, invoker(1));
            Assert.Equal(2 * 2, invoker(2));
            Assert.Equal(3 * 2, invoker(3));
        }

        [Fact]
        public void BindParamAndResultF32()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<hostInOut>(("env","bound_host"), BoundHost);
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "f32"));
            var invoker = runtime.CreateInvoker<Func<float, float>>(fa);
            
            Assert.Equal(1f * 2f, invoker(1f));
            Assert.Equal(2f * 2f, invoker(2f));
            Assert.Equal(3f * 2f, invoker(3f));
        }

        static int BoundHost(int a, out int b)
        {
            b = a;
            int c = a * 2;
            return c;
        }

        [Fact]
        public void BindHostFunction()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<hostInOut>(("env","bound_host"), BoundHost);
            
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "call_host"));
            var invoker = runtime.CreateInvoker<Func<int, int>>(fa);
            
            Assert.Equal(10 + 20, invoker(10));
        }

        delegate int hostInOut(int a, out int b);
    }
}