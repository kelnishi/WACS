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
using System.Collections.Generic;
using System.IO;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
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
            _registers[Count].Type = ValType.I32;
            _registers[Count].Data.Int32 = value;
            Count++;
        }

        public void PushU32(uint value)
        {
            _registers[Count].Type = ValType.I32;
            _registers[Count].Data.UInt32 = value;
            Count++;
        }

        public void PushI64(long value)
        {
            try
            {
                _registers[Count].Type = ValType.I64;
                _registers[Count].Data.Int64 = value;
                Count++;
            }
            catch (Exception _)
            {
                throw;
            }
        }

        public void PushU64(ulong value)
        {
            _registers[Count].Type = ValType.I64;
            _registers[Count].Data.UInt64 = value;
            Count++;
        }

        public void PushF32(float value)
        {
            _registers[Count].Type = ValType.F32;
            _registers[Count].Data.Float32 = value;
            Count++;
        }

        public void PushF64(double value)
        {
            _registers[Count].Type = ValType.F64;
            _registers[Count].Data.Float64 = value;
            Count++;
        }

        public void PushV128(V128 value)
        {
            _registers[Count].Type = ValType.V128;
            _registers[Count].GcRef = new VecRef(value);
            Count++;
        }

        public void PushRef(Value value)
        {
            if (!value.Type.IsRefType())
                throw new InvalidDataException($"Pushed non-reftype {value.Type} onto the stack");
            PushValue(value);
        }

        public void PushValue(Value value)
        {
            _registers[Count++] = value;
        }
        
        public int PopI32()
        {
            --Count;
            return _registers[Count].Data.Int32;
        }

        public uint PopU32()
        {
            --Count;
            return _registers[Count].Data.UInt32;
        }

        public long PopI64()
        {
            --Count;
            return _registers[Count].Data.Int64;
        }

        public ulong PopU64()
        {
            --Count;
            return _registers[Count].Data.UInt64;
        }

        public long PopAddr()
        {
            --Count;
            var value = _registers[Count];
            var addr = value.Type switch
            {
                ValType.I32 => value.Data.UInt32,
                ValType.I64 => value.Data.Int64,
                ValType.U32 => value.Data.UInt32,
                ValType.U64 => (long)value.Data.UInt64,
                _ => throw new InvalidDataException($"OperandStack contained wrong type {value.Type} expected int")
            };
            if (addr < 0)
                throw new TrapException($"Address was negative {addr}");
            return addr;
        }

        public float PopF32()
        {
            --Count;
            return _registers[Count].Data.Float32;
        }

        public double PopF64()
        {
            --Count;
            return _registers[Count].Data.Float64;
        }

        public V128 PopV128()
        {
            --Count;
            var value = (_registers[Count].GcRef as VecRef)!.V128;
            _registers[Count].GcRef = null;
            return value;
        }

        public Value PopRefType()
        {
            --Count;
            var value = _registers[Count];
            _registers[Count].GcRef = null;
            return value;
        }

        public Value PopAny()
        {
            --Count;
            var value = _registers[Count];
            _registers[Count].GcRef = null;
            return value;
        }

        public void PopTo(int height)
        {
            Count = height;
        }

        public Value PopType(ValType type)
        {
            --Count;
            
            var val = _registers[Count];
            // if (val.Type != type && !val.Type.Matches(type))
            //     throw new InvalidDataException($"OperandStack contained wrong type {val.Type} expected {type}");
            _registers[Count].GcRef = null;
            return val;
        }

        public void Clear()
        {
            for (int i = 0; i < Count; i++) 
                _registers[i].GcRef = null;
            
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

        public void GuardExhaust(int stack)
        {
            if (Count + stack > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count + stack}");
        }

        public Memory<Value> ReserveLocals(int parameters, int total)
        {
            //TODO: We need to account for stack use here.
            if (Count + total - parameters >= _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {Count + total - parameters}");
            
            int first = Count - parameters;
            Count += total - parameters;
            try
            {
                return new Memory<Value>(_registers, first, total);
            }
            catch (ArgumentOutOfRangeException e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        /// <summary>
        /// Shrink the OpStack to resultsHeight.
        /// </summary>
        /// <param name="resultCount">The number of results that should be shifted</param>
        /// <param name="resultsHeight">The OpStack height after shrinking</param>
        public void ShiftResults(int resultCount, int resultsHeight)
        {
            if (Count == resultsHeight)
                return;
            int src = Count - resultCount;
            int dest = resultsHeight - resultCount;
            Array.Copy(_registers, src, _registers, dest, resultCount);
            for (int i = resultsHeight; i < Count; ++i)
            {
                _registers[i].GcRef = null;
            }
            Count = resultsHeight;
        }
    }
}