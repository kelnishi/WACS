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
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Transpiler
{
    
    public class InstAggregateValue<T> : InstAggregate1_0<Value>
        where T : struct
    {
        public InstAggregateValue(ITypedValueProducer<T> inA, ValType type, INodeComputer<Value> compute)
            : base(new Wrapper(inA, type), compute) {}

        class Wrapper : ITypedValueProducer<Value>
        {
            private readonly ITypedValueProducer<T> _inA;
            
            public Wrapper(ITypedValueProducer<T> inA, ValType type)
            {
                _inA = inA;
                if (typeof(T) == typeof(Value))
                {
                    _func = ((ITypedValueProducer<Value>)_inA).GetFunc;
                }
                else
                {
                    var func = _inA.GetFunc;
                    _func = context => new Value(func(context));
                }
            }
            public int CalculateSize() => _inA.CalculateSize();

            private Func<ExecContext, Value> _func;
            public Func<ExecContext, Value> GetFunc => _func;
        }
    }
    
    public class UnwrapValue<T> : ITypedValueProducer<T>
        where T : struct
    {
        private readonly ITypedValueProducer<Value> _inA;
        public UnwrapValue(ITypedValueProducer<Value> inA)
        {
            _inA = inA;
            var func = _inA.GetFunc;
            _func = context => (T)func(context).CastScalar<T>();
        }

        public int CalculateSize() => _inA.CalculateSize();

        private Func<ExecContext, T> _func;
        public Func<ExecContext, T> GetFunc => _func;
    }
    
    public class CastToI32<T> : ITypedValueProducer<int>
        where T : struct
    {
        private readonly ITypedValueProducer<T> _inA;
        public CastToI32(ITypedValueProducer<T> inA)
        {
            _inA = inA;
            if (typeof(T) == typeof(int))
            {
                _func = ((ITypedValueProducer<int>)_inA).GetFunc;
            }
            else if (typeof(T) == typeof(uint))
            {
                var func = _inA.GetFunc as Func<ExecContext, uint>;
                _func = context => (int)func!(context);
            }
            else
            {
                throw new ArgumentException($"Cannot convert type {typeof(T)} to int");
            }
        }

        public int CalculateSize() => _inA.CalculateSize();

        private Func<ExecContext, int> _func;
        public Func<ExecContext, int> GetFunc => _func;
    }
    
    public class CastToU32<T> : ITypedValueProducer<uint>
        where T : struct
    {
        private readonly ITypedValueProducer<T> _inA;
        public CastToU32(ITypedValueProducer<T> inA)
        {
            _inA = inA;
            if (typeof(T) == typeof(uint))
            {
                _func = ((ITypedValueProducer<uint>)_inA).GetFunc;
            }
            else if (typeof(T) == typeof(int))
            {
                var func = _inA.GetFunc as Func<ExecContext, int>;
                _func = context => (uint)func!(context);
            }
            else
            {
                throw new ArgumentException($"Cannot convert type {typeof(T)} to int");
            }
        }

        public int CalculateSize() => _inA.CalculateSize();

        private Func<ExecContext, uint> _func;
        public Func<ExecContext, uint> GetFunc => _func;
    }
}