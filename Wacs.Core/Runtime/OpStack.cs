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
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Runtime
{
    public class OpStack
    {
        private readonly Value[] _registers;
        private readonly int _stackLimit;
        public int Count;

        public OpStack(int limit)
        {
            _stackLimit = limit;
            _registers = new Value[limit];
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

            _registers[Count - 1].Type = ValType.I32;
            _registers[Count - 1].Int32 = value;
        }

        public void PushU32(uint value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");

            _registers[Count - 1].Type = ValType.I32;
            _registers[Count - 1].UInt32 = value;
        }

        public void PushI64(long value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");

            _registers[Count - 1].Type = ValType.I64;
            _registers[Count - 1].Int64 = value;
        }

        public void PushU64(ulong value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");

            _registers[Count - 1].Type = ValType.I64;
            _registers[Count - 1].UInt64 = value;
        }

        public void PushF32(float value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");

            _registers[Count - 1].Type = ValType.F32;
            _registers[Count - 1].Float32 = value;
        }

        public void PushF64(double value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");

            _registers[Count - 1].Type = ValType.F64;
            _registers[Count - 1].Float64 = value;
        }

        public void PushV128(V128 value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");

            _registers[Count - 1].Type = ValType.V128;
            _registers[Count - 1].V128 = value;
        }

        public void PushFuncref(Value value)
        {
            if (value.Type != ValType.Func)
                throw new InvalidDataException($"Pushed non-funcref {value.Type} onto the stack");
            PushValue(value);
        }

        public void PushExternref(Value value)
        {
            if (value.Type != ValType.Extern)
                throw new InvalidDataException($"Pushed non-externref {value.Type} onto the stack");
            PushValue(value);
        }

        public void PushRef(Value value)
        {
            if (!value.Type.IsRefType())
                throw new InvalidDataException($"Pushed non-reftype {value.Type} onto the stack");
            PushValue(value);
        }

        public void PushValue(Value value)
        {
            if (++Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count}");
            _registers[Count - 1] = value;
        }

        public int PopI32()
        {
            --Count;
            return _registers[Count].Int32;
        }

        public uint PopU32()
        {
            --Count;
            return _registers[Count].UInt32;
        }

        public long PopI64()
        {
            --Count;
            return _registers[Count].Int64;
        }

        public ulong PopU64()
        {
            --Count;
            return _registers[Count].UInt64;
        }

        public float PopF32()
        {
            --Count;
            return _registers[Count].Float32;
        }

        public double PopF64()
        {
            --Count;
            return _registers[Count].Float64;
        }

        public V128 PopV128()
        {
            --Count;
            return _registers[Count].V128;
        }

        public Value PopRefType()
        {
            --Count;
            return _registers[Count];
        }

        public Value PopAny()
        {
            --Count;
            return _registers[Count];
        }

        public void PopTo(int height)
        {
            Count = height;
        }

        public Value PopType(ValType type)
        {
            --Count;
            if (Count < 0)
                throw new InvalidDataException($"Stackunderflow");
            
            var val = _registers[Count];
            // if (val.Type != type && !val.Type.Matches(type))
            //     throw new InvalidDataException($"OperandStack contained wrong type {val.Type} expected {type}");
            return val;
        }

        public void Clear()
        {
            Count = 0;
        }

        public Value Peek()
        {
            return _registers[Count-1];
        }

        public int PopResults(ResultType type, ref Value[] results)
        {
            int arity = type.Arity;
            for (int i = arity - 1; i >= 0; --i)
            {
                results[i] = PopType(type.Types[i]);
            }
            return arity;
        }

        public void PopResults(ResultType type, ref Stack<Value> results)
        {
            for (int i = 0, l = type.Arity; i < l; ++i)
            {
                results.Push(PopType(type.Types[i]));
            }
        }

        public void PopResults(int arity, ref Stack<Value> results)
        {
            for (int i = 0, l = arity; i < l; ++i)
            {
                results.Push(PopAny());
            }
        }

        public void PopScalars(ResultType type, object[] targetBuf, int firstParameter)
        {
            for (int i = type.Arity - 1; i >= 0; --i)
            {
                targetBuf[i + firstParameter] = type.Types[i] switch
                {
                    ValType.I32 => PopI32(),
                    ValType.I64 => PopI64(),
                    ValType.U32 => PopU32(),
                    ValType.F32 => PopF32(),
                    ValType.F64 => PopF64(),
                    ValType.V128 => PopV128(),
                    _ => throw new InvalidDataException($"Unsupported value type {type.Types[i]}")
                };
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