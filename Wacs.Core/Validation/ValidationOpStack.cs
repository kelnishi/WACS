using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Validation
{
    public interface IValidationOpStack
    {
        int Height { get; }
        void Clear();
        void PushResult(ResultType types);
        void PushI32(int i32 = 0);
        void PushI64(long i64 = 0);
        void PushF32(float f32 = 0.0f);
        void PushF64(double f64 = 0.0d);
        void PushV128(V128 v128 = default);
        void PushFuncref(Value value);
        void PushExternref(Value value);
        void PushType(ValType type);
        void PushValues(Stack<Value> vals);
        Value PopI32();
        Value PopI64();
        Value PopF32();
        Value PopF64();
        Value PopV128();
        Value PopRefType();
        Value PopType(ValType type);
        Value PopAny();

        Stack<Value> PopValues(ResultType types);
        public void ReturnResults(ResultType type);
    }

    public class ValidationOpStack : IValidationOpStack
    {
        private readonly Stack<Value> _stack = new();

        public bool Unreachable { get; set; } = false;

        public int Height => _stack.Count;

        public void Clear() => _stack.Clear();

        public void PushResult(ResultType types)
        {
            foreach (var type in types.Types)
            {
                PushType(type);
            }
        }

        public void PushI32(int i32 = 0)
        {
            Value value = i32;
            if (value.Type != ValType.I32)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.I32}");
            _stack.Push(value);
        }

        public void PushI64(long i64 = 0)
        {
            Value value = i64;
            if (value.Type != ValType.I64)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.I64}");
            _stack.Push(value);
        }

        public void PushF32(float f32 = 0.0f)
        {
            Value value = f32;
            if (value.Type != ValType.F32)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.F32}");
            _stack.Push(value);
        }

        public void PushF64(double f64 = 0.0d)
        {
            Value value = f64;
            if (value.Type != ValType.F64)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.F64}");
            _stack.Push(value);
        }

        public void PushV128(V128 v128 = default)
        {
            Value value = v128;
            if (value.Type != ValType.V128)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.V128}");
            _stack.Push(value);
        }

        public void PushFuncref(Value value)
        {
            if (value.Type != ValType.Funcref)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.Funcref}");
            _stack.Push(value);
        }

        public void PushExternref(Value value)
        {
            if (value.Type != ValType.Externref)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.Externref}");
            _stack.Push(value);
        }

        public void PushType(ValType type)
        {
            _stack.Push(new Value(type));
        }

        public void PushValues(Stack<Value> vals)
        {
            while (vals.Count > 0)
                _stack.Push(vals.Pop());
        }

        public Value PopI32()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow. pop(i32)");

            Value value = _stack.Pop();
            if (value.Type != ValType.I32)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} popped from stack. Expected: {ValType.I32}");
            return value;
        }

        public Value PopI64()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow. pop(i64)");

            Value value = _stack.Pop();
            if (value.Type != ValType.I64)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} popped from stack. Expected: {ValType.I64}");
            return value;
        }

        public Value PopF32()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow. pop(f32)");

            Value value = _stack.Pop();
            if (value.Type != ValType.F32)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} popped from stack. Expected: {ValType.F32}");
            return value;
        }

        public Value PopF64()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow. pop(f64)");

            Value value = _stack.Pop();
            if (value.Type != ValType.F64)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} popped from stack. Expected: {ValType.F64}");
            return value;
        }

        public Value PopV128()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow. pop(v128)");

            Value value = _stack.Pop();
            if (value.Type != ValType.V128)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} popped from stack. Expected {ValType.V128}");
            return value;
        }

        public Value PopRefType()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow. pop(ref)");

            Value value = _stack.Pop();
            switch (value.Type)
            {
                case ValType.Funcref:
                case ValType.Externref:
                    return value;
                default:
                    throw new ValidationException(
                        $"Wrong operand type {value.Type} at top of stack. Expected: FuncRef or ExternRef");
            }
        }

        public Stack<Value> PopValues(ResultType types)
        {
            var aside = new Stack<Value>();
            foreach (var type in types.Types.Reverse())
            {
                var stackType = PopAny();
                if (stackType.Type != type)
                    throw new ValidationException("Invalid Operand Stack did not match ResultType");
                aside.Push(stackType);
            }
            return aside;
        }

        public void ReturnResults(ResultType types)
        {
            foreach (var type in types.Types)
            {
                PushType(type);
            }
        }

        public Value PopType(ValType type)
        {
            if (_stack.Count == 0)
                throw new ValidationException($"Operand stack underflow. pop({type})");

            Value value = _stack.Pop();
            if (value.Type != type)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} at top of stack. Expected: {type}");
            
            return value;
        }

        public Value PopAny()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow. pop(any)");

            return _stack.Pop();
        }
    }

    public class UnreachableOpStack : IValidationOpStack
    {
        public bool Unreachable { get => true; set {} }
        public int Height => 0;

        public void Clear()
        {
        }

        public void PushResult(ResultType types)
        {
        }

        public void PushI32(int i32 = 0)
        {
        }

        public void PushI64(long i64 = 0)
        {
        }

        public void PushF32(float f32 = 0)
        {
        }

        public void PushF64(double f64 = 0)
        {
        }

        public void PushV128(V128 v128 = default)
        {
        }

        public void PushFuncref(Value value)
        {
        }

        public void PushExternref(Value value)
        {
        }

        public void PushType(ValType type)
        {
        }

        public void PushValues(Stack<Value> vals)
        {
        }

        public Value PopI32() => new(ValType.I32);

        public Value PopI64() => new(ValType.I64);

        public Value PopF32() => new(ValType.F32);

        public Value PopF64() => new(ValType.F64);

        public Value PopV128() => new(ValType.V128);

        public Value PopRefType() => new(ValType.Funcref);

        public Stack<Value> PopValues(ResultType types) => new();

        public void ReturnResults(ResultType types) { }

        public Value PopType(ValType type) => new(type);

        public Value PopAny() => new(ValType.I32);
    }
}