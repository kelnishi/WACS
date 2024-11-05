using System;
using System.Collections.Generic;
using System.IO;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class OpStack
    {
        private readonly Stack<Value> _stack = new(256);

        public bool HasValue => _stack.Count > 0;

        public int Count => _stack.Count;

        public void Push(Stack<Value> vals)
        {
            while (vals.Count > 0) _stack.Push(vals.Pop());
        }

        public void PushI32(int value) => _stack.Push(value);
        public void PushI32(uint value) => _stack.Push(value);
        public void PushI64(long value) => _stack.Push(value);
        public void PushF32(float value) => _stack.Push(value);
        public void PushF64(double value) => _stack.Push(value);
        public void PushV128(Value value) => _stack.Push(value);
        public void PushFuncref(Value value) => _stack.Push(value);
        public void PushExternref(Value value) => _stack.Push(value);

        public void PushRef(Value value)
        {
            if (!value.Type.IsReference())
                throw new InvalidDataException($"Pushed non-reftype {value.Type} onto the stack");
            _stack.Push(value);
        }

        public void PushValue(Value value) => _stack.Push(value);

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