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
            runtime.BindHostFunction<Action<int>>((module, "print_i32"),
                value => Console.WriteLine($"{value}"));
            runtime.BindHostFunction<Action<long>>((module, "print_i64"),
                value => Console.WriteLine($"{value}"));
            runtime.BindHostFunction<Action<float>>((module, "print_f32"),
                value => Console.WriteLine($"{value}"));
            runtime.BindHostFunction<Action<double>>((module, "print_f64"),
                value => Console.WriteLine($"{value}"));
            runtime.BindHostFunction<Action<int, float>>((module, "print_i32_f32"),
                (i32,f32) => Console.WriteLine($"i32={i32} f32={f32}"));
            runtime.BindHostFunction<Action<double,double>>((module, "print_f64_f64"),
                (f641,f642) => Console.WriteLine($"f64={f641} f64={f642}"));
            
            //Bind these...
            //externref(s)
            //is_externref(x)
            //is_funcref(x)
            //eq_externref(x,y)
            //eq_funcref(x,y)
            
            
            runtime.BindHostGlobal((module, "global_i32"), new GlobalType(ValType.I32, Mutability.Immutable),
                new Value(ValType.I32, 666));
            runtime.BindHostGlobal((module, "global_i64"), new GlobalType(ValType.I64, Mutability.Immutable),
                new Value(ValType.I64, 666L));
            runtime.BindHostGlobal((module, "global_f32"), new GlobalType(ValType.F32, Mutability.Immutable),
                new Value(ValType.F32, 666.6f));
            runtime.BindHostGlobal((module, "global_f64"), new GlobalType(ValType.F64, Mutability.Immutable),
                new Value(ValType.F64, 666.6));

            runtime.BindHostTable((module, "table"), new TableType(ReferenceType.Funcref,new Limits(10,20)),
                new Value(ValType.Funcref));
            
            runtime.BindHostMemory((module, "memory"), new MemoryType(minimum:1, maximum:2));
        }
    }
}