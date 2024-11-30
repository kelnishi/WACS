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
using System.Collections.Generic;
using System.IO;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class OpStack
    {
        private readonly Stack<Value> _stack;
        private readonly int _stackLimit;
        public int Count;

        public OpStack(int limit)
        {
            _stackLimit = limit;
            _stack = new(limit);
            Count = 0;
        }

        public bool HasValue => Count > 0;

        public void PushResults(Stack<Value> vals)
        {
            for (int i = 0, l = vals.Count; i < l; ++i)
            {
                PushValue(vals.Pop());
            }
        }

        public void PushI32(int value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");
            
            _stack.Push(new Value(value));
        }

        public void PushU32(uint value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");
            
            _stack.Push(new Value(value));
        }

        public void PushI64(long value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");
            
            _stack.Push(new Value(value));
        }

        public void PushU64(ulong value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");
            
            _stack.Push(new Value(value));
        }

        public void PushF32(float value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");
            
            _stack.Push(new Value(value));
        }

        public void PushF64(double value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");
            
            _stack.Push(new Value(value));
        }

        public void PushV128(V128 value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");
            
            _stack.Push(new Value(value));
        }

        public void PushFuncref(Value value)
        {
            if (value.Type != ValType.Funcref)
                throw new InvalidDataException($"Pushed non-funcref {value.Type} onto the stack");
            PushValue(value);
        }

        public void PushExternref(Value value)
        {
            if (value.Type != ValType.Externref)
                throw new InvalidDataException($"Pushed non-externref {value.Type} onto the stack");
            PushValue(value);
        }

        public void PushRef(Value value)
        {
            if (!value.Type.IsReference())
                throw new InvalidDataException($"Pushed non-reftype {value.Type} onto the stack");
            PushValue(value);
        }

        public void PushValue(Value value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");
            
            _stack.Push(value);
        }

        public int PopI32()
        {
            --Count;
            return _stack.Pop().Int32;
        }

        public uint PopU32()
        {
            --Count;
            return _stack.Pop().UInt32;
        }

        public long PopI64()
        {
            --Count;
            return _stack.Pop().Int64;
        }

        public ulong PopU64()
        {
            --Count;
            return _stack.Pop().UInt64;
        }

        public float PopF32()
        {
            --Count;
            return _stack.Pop().Float32;
        }

        public double PopF64()
        {
            --Count;
            return _stack.Pop().Float64;
        }

        public V128 PopV128()
        {
            --Count;
            return _stack.Pop().V128;
        }

        public Value PopRefType()
        {
            --Count;
            return _stack.Pop();
        }

        public Value  PopAny()
        {
            --Count;
            return _stack.Pop();
        }

        public Value PopType(ValType type)
        {
            --Count;
            var val = _stack.Pop();
            if (val.Type != type)
                throw new InvalidDataException($"OperandStack contained wrong type {val.Type} expected {type}");
            return val;
        }

        public void Clear()
        {
            _stack.Clear();
            Count = 0;
        }

        public Value Peek() => _stack.Peek();

        public void PopResults(ResultType type, ref Stack<Value> results) => PopResults(type.Arity, ref results);

        public void PopResults(int arity, ref Stack<Value> results)
        {
            for (int i = 0, l = arity; i < l; ++i)
            {
                //We could check the types here, but the spec just says to YOLO it.
                results.Push(PopAny());
            }
        }

        public void PopScalars(ResultType type, object[] targetBuf, int firstParameter)
        {
            for (int i = type.Arity - 1 + firstParameter; i >= firstParameter; --i)
            {
                targetBuf[i] = PopAny().Scalar;
            }
        }

        public void PopScalars(ResultType type, Span<Value> targetBuf)
        {
            for (int i = type.Arity - 1; i >= 0; --i)
            {
                targetBuf[i] = PopAny();
            }
        }
        
        public void PushValues(Value[] scalars)
        {
            for (int i = 0, l = scalars.Length; i < l; ++i)
            {
                PushValue(scalars[i]);
            }
        }

        public void PushScalars(ResultType type, object[] scalars)
        {
            for (int i = 0, l = type.Arity; i < l; ++i)
            {
                var scalar = scalars[i];
                if (scalar is Value v)
                    PushValue(v);
                else
                    PushValue(new Value(type.Types[i], scalar));
            }
        }
    }
}