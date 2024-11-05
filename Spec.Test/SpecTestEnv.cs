using System;
using Wacs.Core.Runtime;

namespace Spec.Test
{
    public class SpecTestEnv : IBindable
    {
        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "spectest";
            runtime.BindHostFunction<Action<int>>((module, "print_i32"), Print_I32);
        }

        public void Print_I32(int value)
        {
            Console.WriteLine($"value");
        }
    }
}