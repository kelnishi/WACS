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
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Wacs.Core;
using Wacs.Core.Runtime;
using Xunit;

namespace Spec.Test
{
    public class BindingTests
    {
        private static int HostResult;
        
        static int BoundHost(int a, out int b)
        {
            b = a;
            int c = a * 2;
            HostResult = c;
            return c;
        }

        static async Task<int> BoundAsyncHost(int a)
        {
            await Task.Delay(1000); // Simulate work
            return a*2;
        }


        [Fact]
        public void BindStackBinder()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<HostInOut>(("env","bound_host"), BoundHost);
            runtime.BindHostFunction<HostAsyncInRet>(("env","bound_async_host"), BoundAsyncHost);
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "4x4"));
            var invoker = runtime.CreateStackInvoker(fa);
            
            var result = invoker(new Value[]{1, 1L, 1f, 1.0});
            
            Assert.Equal(1 * 2, (int)result[0]);
            Assert.Equal(1 * 3, (long)result[1]);
            Assert.Equal(1f * 4f, (float)result[2]);
            Assert.Equal(1.0 * 5.0, (double)result[3]);
        }

        [Fact]
        public void Bind4x1()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<HostInOut>(("env","bound_host"), BoundHost);
            runtime.BindHostFunction<HostAsyncInRet>(("env","bound_async_host"), BoundAsyncHost);
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "4x1"));
            var invoker = runtime.CreateInvokerFunc<int,long,float,double,Value>(fa);
            
            int result = invoker(1, 1L, 1.0f, 1.0);
            
            Assert.Equal(1, result);
        }

        [Fact]
        public void BindParamAndResultI32()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<HostInOut>(("env","bound_host"), BoundHost);
            runtime.BindHostFunction<HostAsyncInRet>(("env","bound_async_host"), BoundAsyncHost);
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "i32"));
            var invoker = runtime.CreateInvokerFunc<int, Value>(fa);
            
            Assert.Equal(1 * 2, (int)invoker(1));
            Assert.Equal(2 * 2, (int)invoker(2));
            Assert.Equal(3 * 2, (int)invoker(3));
        }

        [Fact]
        public void BindParamAndResultF32()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<HostInOut>(("env","bound_host"), BoundHost);
            runtime.BindHostFunction<HostAsyncInRet>(("env","bound_async_host"), BoundAsyncHost);
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "f32"));
            var invoker = runtime.CreateInvokerFunc<float, Value>(fa);
            
            Assert.Equal(1f * 2f, (float)invoker(1f));
            Assert.Equal(2f * 2f, (float)invoker(2f));
            Assert.Equal(3f * 2f, (float)invoker(3f));
        }

        [Fact]
        public void BindHostFunction()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<HostInOut>(("env","bound_host"), BoundHost);
            runtime.BindHostFunction<HostAsyncInRet>(("env","bound_async_host"), BoundAsyncHost);
            
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "call_host"));
            var invoker = runtime.CreateInvokerFunc<int, Value>(fa);
            
            Assert.Equal(10 + 20, (int)invoker(10));
        }


        [Fact]
        public void BindAsyncHostFunction()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<HostInOut>(("env","bound_host"), BoundHost);
            runtime.BindHostFunction<HostAsyncInRet>(("env","bound_async_host"), BoundAsyncHost);
            
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "call_async_host"));
            
            //Host function is async, Wasm function is called synchronously
            var invoker = runtime.CreateInvokerFunc<int, Value>(fa);
            
            Assert.Equal(10*2, (int)invoker(10));
        }

        [Fact]
        public async Task BindAsyncWasmFunction()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<HostInOut>(("env","bound_host"), BoundHost);

            runtime.BindHostFunction<HostAsyncInRet>(("env","bound_async_host"), static async a =>
            {
                await Task.Delay(3_000); // Simulate work
                return a*2;
            });
            
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "call_async_host"));
            
            //Host function is async, Wasm function is called asynchronously
            var invoker = runtime.CreateStackInvokerAsync(fa);

            var stopwatch = new Stopwatch();
            
            stopwatch.Start();
            var results = await invoker(10);
            stopwatch.Stop();
            
            Assert.InRange(stopwatch.ElapsedMilliseconds, 3000, 3500);
            Assert.Equal(10*2, results[0].Data.Int32);
        }

        [Fact]
        public void Bind0ParameterAction()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<HostInOut>(("env","bound_host"), BoundHost);
            runtime.BindHostFunction<HostAsyncInRet>(("env","bound_async_host"), BoundAsyncHost);
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "0x0"));
            var invoker = runtime.CreateInvokerAction(fa);
            
            HostResult = 0;
            invoker();
            Assert.Equal(12, HostResult);
        }
        
        [Fact]
        public void Bind1ParameterAction()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<HostInOut>(("env","bound_host"), BoundHost);
            runtime.BindHostFunction<HostAsyncInRet>(("env","bound_async_host"), BoundAsyncHost);
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "1x0"));
            var invoker = runtime.CreateInvokerAction<int>(fa);

            HostResult = 0;
            invoker(1);
            Assert.Equal(2, HostResult);
        }
        
        [Fact]
        public void Bind2ParameterAction()
        {
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<HostInOut>(("env","bound_host"), BoundHost);
            runtime.BindHostFunction<HostAsyncInRet>(("env","bound_async_host"), BoundAsyncHost);
            using var fileStream = new FileStream("../../../engine/binding.wasm", FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);
            var moduleInst = runtime.InstantiateModule(module);
            runtime.RegisterModule("binding", moduleInst);

            var fa = runtime.GetExportedFunction(("binding", "2x0"));
            var invoker = runtime.CreateInvokerAction<int,int>(fa);

            HostResult = 0;
            invoker(1, 2);
            Assert.Equal(4, HostResult);
        }
        

        delegate int HostInOut(int a, out int b);

        delegate Task<int> HostAsyncInRet(int a);
    }
}