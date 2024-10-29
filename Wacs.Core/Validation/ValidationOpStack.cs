using System.Collections.Generic;
using FluentValidation;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Validation
{
    public interface IValidationOpStack
    {
        public void Clear();
        void Push(ResultType types);
        void PushI32(int i32 = 0);
        void PushI64(long i64 = 0);
        void PushF32(float f32 = 0.0f);
        void PushF64(double f64 = 0.0d);
        void PushV128(V128 v128 = default);
        void PushFuncref(Value value);
        void PushExternref(Value value);
        void PushType(ValType type);
        void PopI32();
        void PopI64();
        void PopF32();
        void PopF64();
        void PopV128();
        void PopRefType();
        void ValidateStack(ResultType types, bool keep = true);
        void PopType(ValType type);
        Value PopAny();
    }

    public class ValidationOpStack : IValidationOpStack
    {
        private readonly Stack<Value> _stack = new();

        public void Clear() => _stack.Clear();

        public void Push(ResultType types)
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

        public void PopI32()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow.");

            Value value = _stack.Pop();
            if (value.Type != ValType.I32)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.I32}");
        }

        public void PopI64()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow.");

            Value value = _stack.Pop();
            if (value.Type != ValType.I64)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.I64}");
        }

        public void PopF32()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow.");

            Value value = _stack.Pop();
            if (value.Type != ValType.F32)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.F32}");
        }

        public void PopF64()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow.");

            Value value = _stack.Pop();
            if (value.Type != ValType.F64)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.F64}");
        }

        public void PopV128()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow.");

            Value value = _stack.Pop();
            if (value.Type != ValType.V128)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.V128}");
        }

        public void PopRefType()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow.");

            Value value = _stack.Pop();
            switch (value.Type)
            {
                case ValType.Funcref:
                case ValType.Externref:
                    return;
                default:
                    throw new ValidationException(
                        $"Wrong operand type {value.Type} at top of stack. Expected: FuncRef or ExternRef");
            }
        }

        public void ValidateStack(ResultType types, bool keep = true)
        {
            var aside = new Stack<Value>();
            //Pop vals off the stack
            for (int i = 0, l = types.Types.Length; i < l; ++i)
            {
                var v = PopAny();
                aside.Push(v);
            }

            //Check that they match ResultType and push them back on
            foreach (var type in types.Types)
            {
                var p = aside.Pop();
                if (p.Type != type)
                    throw new ValidationException("Invalid Operand Stack did not match ResultType");
                if (keep)
                    PushType(p.Type);
            }
        }

        public void PopType(ValType type)
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow.");

            Value value = _stack.Pop();
            if (value.Type != type)
                throw new ValidationException(
                    $"Wrong operand type {value.Type} at top of stack. Expected: {type}");
        }

        public Value PopAny()
        {
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow.");

            return _stack.Pop();
        }
    }

    public class UnreachableOpStack : IValidationOpStack
    {
        public void Clear()
        {
        }

        public void Push(ResultType types)
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

        public void PopI32()
        {
        }

        public void PopI64()
        {
        }

        public void PopF32()
        {
        }

        public void PopF64()
        {
        }

        public void PopV128()
        {
        }

        public void PopRefType()
        {
        }

        public void ValidateStack(ResultType types, bool keep = true)
        {
        }

        public void PopType(ValType type)
        {
        }

        public Value PopAny() => new(ValType.I32);
    }
}