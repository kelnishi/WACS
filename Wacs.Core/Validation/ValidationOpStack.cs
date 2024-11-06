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
        Value PopType(ValType expectedType);
        Value PopAny();

        Stack<Value> PopValues(ResultType types);
        public void ReturnResults(ResultType type);
    }

    public class ValidationOpStack : IValidationOpStack
    {
        private readonly Stack<Value> _stack = new();
        private WasmValidationContext _context;

        public ValidationOpStack(WasmValidationContext ctx)
        {
            _context = ctx;
        }

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
            if (_stack.Count == _context.ControlFrame.Height && _context.ControlFrame.Unreachable)
                return Value.Unknown;
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
            if (_stack.Count == _context.ControlFrame.Height && _context.ControlFrame.Unreachable)
                return Value.Unknown;
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
            if (_stack.Count == _context.ControlFrame.Height && _context.ControlFrame.Unreachable)
                return Value.Unknown;
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
            if (_stack.Count == _context.ControlFrame.Height && _context.ControlFrame.Unreachable)
                return Value.Unknown;
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
            if (_stack.Count == _context.ControlFrame.Height && _context.ControlFrame.Unreachable)
                return Value.Unknown;
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
            if (_stack.Count == _context.ControlFrame.Height && _context.ControlFrame.Unreachable)
                return Value.Unknown;
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

        public Value PopType(ValType expectedType)
        {
            if (_stack.Count == _context.ControlFrame.Height && _context.ControlFrame.Unreachable)
                return Value.Unknown;
            if (_stack.Count == 0)
                throw new ValidationException($"Operand stack underflow. pop({expectedType})");

            Value actual = _stack.Pop();
            if (actual.Type != expectedType
                && actual.Type != ValType.Unknown
                && expectedType != ValType.Unknown)
                throw new ValidationException(
                    $"Wrong operand type {actual.Type} at top of stack. Expected: {expectedType}");
            
            return actual;
        }

        public Stack<Value> PopValues(ResultType types)
        {
            var aside = new Stack<Value>();
            foreach (var type in types.Types.Reverse())
            {
                aside.Push(PopType(type));
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

        public Value PopAny()
        {
            if (_stack.Count == _context.ControlFrame.Height && _context.ControlFrame.Unreachable)
                return Value.Unknown;
            if (_stack.Count == 0)
                throw new ValidationException("Operand stack underflow. pop(any)");

            return _stack.Pop();
        }
    }
}