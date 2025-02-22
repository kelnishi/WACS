// Copyright 2024 Kelvin Nishikawa
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
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.Transpiler
{
    
    public class InstAggregateValue<T> : InstAggregate1_0<Value>
        where T : struct
    {
        public InstAggregateValue(ITypedValueProducer<T> inA, ValType type, INodeConsumer<Value> consumer)
            : base(GetWrapper(inA, type), consumer) {}

        private static ITypedValueProducer<Value> GetWrapper(ITypedValueProducer<T> inA, ValType type) =>
            type switch
            {
                ValType.Nil => (ITypedValueProducer<Value>)inA,
                ValType.I32 => new WrapValueI32((ITypedValueProducer<int>)inA),
                ValType.I64 => new WrapValueI64((ITypedValueProducer<long>)inA),
                ValType.F32 => new WrapValueF32((ITypedValueProducer<float>)inA),
                ValType.F64 => new WrapValueF64((ITypedValueProducer<double>)inA),
                ValType.U32 => new WrapValueU32((ITypedValueProducer<uint>)inA),
                ValType.U64 => new WrapValueU64((ITypedValueProducer<ulong>)inA),
                ValType.V128 => new WrapValueV128((ITypedValueProducer<V128>)inA),
                _ => throw new ArgumentException($"Unsupported ValType: {type}")
            };
    }
    
    public abstract class WrapValue<T> : ITypedValueProducer<Value>
        where T : struct
    {
        private readonly ITypedValueProducer<T> _inA;

        protected Func<ExecContext, Value> _func = null!;
        public int StackDiff { get; set; }
        
        protected WrapValue(ITypedValueProducer<T> inA)
        {
            _inA = inA;
            StackDiff = Math.Min(0, inA.StackDiff);
        }

        public int CalculateSize() => _inA.CalculateSize();
        public Func<ExecContext, Value> GetFunc => _func;
    }

    public class NakedValue : WrapValue<Value>
    {
        public NakedValue(ITypedValueProducer<Value> inA) : base(inA)
        {
            var func = inA.GetFunc;
            _func = context => func(context);
        }
    }

    public class WrapValueI32 : WrapValue<int>
    {
        public WrapValueI32(ITypedValueProducer<int> inA) : base(inA)
        {
            var func = inA.GetFunc;
            _func = context => new Value(func(context));
        }
    }
    
    public class WrapValueU32 : WrapValue<uint>
    {
        public WrapValueU32(ITypedValueProducer<uint> inA) : base(inA)
        {
            var func = inA.GetFunc;
            _func = context => new Value(func(context));
        }
    }
    
    public class WrapValueI64 : WrapValue<long>
    {
        public WrapValueI64(ITypedValueProducer<long> inA) : base(inA)
        {
            var func = inA.GetFunc;
            _func = context => new Value(func(context));
        }
    }

    public class WrapValueU64 : WrapValue<ulong>
    {
        public WrapValueU64(ITypedValueProducer<ulong> inA) : base(inA)
        {
            var func = inA.GetFunc;
            _func = context => new Value(func(context));
        }
    }

    public class WrapValueF32 : WrapValue<float>
    {
        public WrapValueF32(ITypedValueProducer<float> inA) : base(inA)
        {
            var func = inA.GetFunc;
            _func = context => new Value(func(context));
        }
    }

    public class WrapValueF64 : WrapValue<double>
    {
        public WrapValueF64(ITypedValueProducer<double> inA) : base(inA)
        {
            var func = inA.GetFunc;
            _func = context => new Value(func(context));
        }
    }
    
    public class WrapValueV128 : WrapValue<V128>
    {
        public WrapValueV128(ITypedValueProducer<V128> inA) : base(inA)
        {
            var func = inA.GetFunc;
            _func = context => new Value(func(context));
        }
    }
    
    public abstract class UnwrapValue<T> : ITypedValueProducer<T>
    {
        protected ITypedValueProducer<Value> InA;
        public int StackDiff { get; set; }
        protected UnwrapValue(ITypedValueProducer<Value> inA)
        {
            InA = inA;
            StackDiff = Math.Min(0, inA.StackDiff);
        }

        public int CalculateSize() => InA.CalculateSize();
        public abstract Func<ExecContext, T> GetFunc { get; }
    }
    
    public class UnwrapValueI32 : UnwrapValue<int>
    {
        public UnwrapValueI32(ITypedValueProducer<Value> inA) : base(inA)
        {
            var func = InA.GetFunc;
            GetFunc = context => func(context).Data.Int32;
        }

        public override Func<ExecContext, int> GetFunc { get; }
    }
    
    public class UnwrapValueU32 : UnwrapValue<uint>
    {
        public UnwrapValueU32(ITypedValueProducer<Value> inA) : base(inA)
        {
            var func = InA.GetFunc;
            GetFunc = context => func(context).Data.UInt32;
        }

        public override Func<ExecContext, uint> GetFunc { get; }
    }
    
    public class UnwrapValueF32 : UnwrapValue<float>
    {
        public UnwrapValueF32(ITypedValueProducer<Value> inA) : base(inA)
        {
            var func = InA.GetFunc;
            GetFunc = context => func(context).Data.Float32;
        }

        public override Func<ExecContext, float> GetFunc { get; }
    }
    
    public class UnwrapValueI64 : UnwrapValue<long>
    {
        public UnwrapValueI64(ITypedValueProducer<Value> inA) : base(inA)
        {
            var func = InA.GetFunc;
            GetFunc = context => func(context) switch
            {
                { Type: ValType.I64 } val64 => val64.Data.Int64,
                //We need to accommodate 32-bit integers for mem64 pointers
                { Type: ValType.I32 } val32 => (long)val32.Data.UInt32,
                var val => throw new WasmRuntimeException($"Unable to unwrap integer value of type {val.Type}")
            };
        }

        public override Func<ExecContext, long> GetFunc { get; }
    }
    
    public class UnwrapValueU64 : UnwrapValue<ulong>
    {
        public UnwrapValueU64(ITypedValueProducer<Value> inA) : base(inA)
        {
            var func = InA.GetFunc;
            GetFunc = context => func(context).Data.UInt64;
        }

        public override Func<ExecContext, ulong> GetFunc { get; }
    }
    
    public class UnwrapValueF64 : UnwrapValue<double>
    {
        public UnwrapValueF64(ITypedValueProducer<Value> inA) : base(inA)
        {
            var func = InA.GetFunc;
            GetFunc = context => func(context).Data.Float64;
        }

        public override Func<ExecContext, double> GetFunc { get; }
    }
    
    public class UnwrapValueV128 : UnwrapValue<V128>
    {
        public UnwrapValueV128(ITypedValueProducer<Value> inA) : base(inA)
        {
            var func = InA.GetFunc;
            GetFunc = context => (func(context).GcRef as VecRef)!.V128;
        }

        public override Func<ExecContext, V128> GetFunc { get; }
    }
    
    public class CastToI32<T> : ITypedValueProducer<int>
        where T : struct
    {
        private readonly ITypedValueProducer<T> _inA;
        public int StackDiff { get; set; }

        public CastToI32(ITypedValueProducer<T> inA)
        {
            _inA = inA;
            StackDiff = Math.Min(0, inA.StackDiff);
            if (typeof(T) == typeof(int))
            {
                GetFunc = ((ITypedValueProducer<int>)_inA).GetFunc;
            }
            else if (typeof(T) == typeof(uint))
            {
                var func = _inA.GetFunc as Func<ExecContext, uint>;
                GetFunc = context => (int)func!(context);
            }
            else
            {
                throw new ArgumentException($"Cannot convert type {typeof(T)} to int");
            }
        }

        public int CalculateSize() => _inA.CalculateSize();
        public Func<ExecContext, int> GetFunc { get; }
    }
    
    public class CastToU32<T> : ITypedValueProducer<uint>
        where T : struct
    {
        private readonly ITypedValueProducer<T> _inA;
        public int StackDiff { get; set; }

        public CastToU32(ITypedValueProducer<T> inA)
        {
            _inA = inA;
            StackDiff = Math.Min(0, inA.StackDiff);
            if (typeof(T) == typeof(uint))
            {
                GetFunc = ((ITypedValueProducer<uint>)_inA).GetFunc;
            }
            else if (typeof(T) == typeof(int))
            {
                var func = _inA.GetFunc as Func<ExecContext, int>;
                GetFunc = context => (uint)func!(context);
            }
            else
            {
                throw new ArgumentException($"Cannot convert type {typeof(T)} to int");
            }
        }

        public int CalculateSize() => _inA.CalculateSize();
        public Func<ExecContext, uint> GetFunc { get; }
    }
    
    public class CastToI64<T> : ITypedValueProducer<long>
        where T : struct
    {
        private readonly ITypedValueProducer<T> _inA;
        public int StackDiff { get; set; }

        public CastToI64(ITypedValueProducer<T> inA)
        {
            _inA = inA;
            StackDiff = Math.Min(0, inA.StackDiff);
            if (typeof(T) == typeof(long))
            {
                GetFunc = ((ITypedValueProducer<long>)_inA).GetFunc;
            }
            else if (typeof(T) == typeof(ulong))
            {
                var func = _inA.GetFunc as Func<ExecContext, ulong>;
                GetFunc = context => (long)func!(context);
            }
            else
            {
                throw new ArgumentException($"Cannot convert type {typeof(T)} to int");
            }
        }

        public int CalculateSize() => _inA.CalculateSize();
        public Func<ExecContext, long> GetFunc { get; }
    }
    
    public class CastToU64<T> : ITypedValueProducer<ulong>
        where T : struct
    {
        private readonly ITypedValueProducer<T> _inA;
        public int StackDiff { get; set; }

        public CastToU64(ITypedValueProducer<T> inA)
        {
            _inA = inA;
            StackDiff = Math.Min(0, inA.StackDiff);
            if (typeof(T) == typeof(ulong))
            {
                GetFunc = ((ITypedValueProducer<ulong>)_inA).GetFunc;
            }
            else if (typeof(T) == typeof(long))
            {
                var func = _inA.GetFunc as Func<ExecContext, long>;
                GetFunc = context => (ulong)func!(context);
            }
            else
            {
                throw new ArgumentException($"Cannot convert type {typeof(T)} to int");
            }
        }

        public int CalculateSize() => _inA.CalculateSize();
        public Func<ExecContext, ulong> GetFunc { get; }
    }
}