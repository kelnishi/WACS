using System;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Spec.Test
{
    public class SpecTestEnv : IBindable
    {
        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "spectest";
            runtime.BindHostFunction<Action<int>>((module, "print_i32"), Print_I32);
            runtime.BindHostMemory((module, "memory"), new MemoryType(minimum:1, maximum:3));
            runtime.BindHostGlobal((module, "global_i32"), new GlobalType(ValType.I32, Mutability.Immutable),
                new Value(ValType.I32, 666));
            runtime.BindHostGlobal((module, "global_i64"), new GlobalType(ValType.I64, Mutability.Immutable),
                new Value(ValType.I64, 666L));
            runtime.BindHostGlobal((module, "global_f32"), new GlobalType(ValType.F32, Mutability.Immutable),
                new Value(ValType.F32, 666.6f));
            runtime.BindHostGlobal((module, "global_f64"), new GlobalType(ValType.F64, Mutability.Immutable),
                new Value(ValType.F64, 666.6));
        }

        public void Print_I32(int value)
        {
            Console.WriteLine($"value");
        }
    }
}