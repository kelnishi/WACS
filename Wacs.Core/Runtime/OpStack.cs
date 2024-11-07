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
        private int _stackLimit;

        public OpStack(int limit)
        {
            _stackLimit = limit;
            _stack = new(limit);
        }

        public bool HasValue => _stack.Count > 0;

        public int Count => _stack.Count;

        public void Push(Stack<Value> vals)
        {
            while (vals.Count > 0) PushValue(vals.Pop());
        }

        public void PushI32(int value) => PushValue(value);
        public void PushI32(uint value) => PushValue(value);
        public void PushI64(long value) => PushValue(value);
        public void PushF32(float value) => PushValue(value);
        public void PushF64(double value) => PushValue(value);
        public void PushV128(Value value) => PushValue(value);

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
            if (_stack.Count > _stackLimit)
                throw new WasmRuntimeException($"Operand stack exhausted {_stack.Count}");
            
            _stack.Push(value);
        }

        public Value PopI32() => _stack.Pop();
        public Value PopI64() => _stack.Pop();
        public Value PopF32() => _stack.Pop();
        public Value PopF64() => _stack.Pop();
        public Value PopV128() => _stack.Pop();
        public Value PopRefType() => _stack.Pop();

        public Value PopAny() => _stack.Pop();

        public Value PopType(ValType type)
        {
            var val = _stack.Pop();
            if (val.Type != type)
                throw new InvalidDataException($"OperandStack contained wrong type {val.Type} expected {type}");
            return val;
        }

        public Value Peek() => _stack.Peek();

        public void PopResults(ResultType type, ref Stack<Value> results)
        {
            for (int i = 0, l = type.Arity; i < l; ++i)
            {
                //We could check the types here, but the spec just says to YOLO it.
                results.Push(PopAny());
            }
        }

        public void PopScalars(ResultType type, Span<object> targetBuf)
        {
            for (int i = type.Arity - 1; i >= 0; --i)
            {
                targetBuf[i] = PopAny().Scalar;
            }
        }

        public void PushScalars(ResultType type, object[] scalars)
        {
            for (int i = 0, l = type.Arity; i < l; ++i)
            {
                PushValue((Value)(scalars[i]));
            }
        }
    }
}