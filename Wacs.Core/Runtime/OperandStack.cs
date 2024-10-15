using System;
using System.Collections.Generic;
using System.IO;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public interface IOperandStack
    {
        void PushI32(int value);
        void PushI64(long value);
        void PushF32(float value);
        void PushF64(double value);
        void PushV128(Value value);
        void PushFuncref(Value value);
        void PushExternref(Value value);

        void PushValue(Value value);
        
        Value PopI32();
        Value PopI64();
        Value PopF32();
        Value PopF64();
        Value PopV128();
        Value PopRefType();
        Value PopAny();
        Value Peek();
    }
    
    public class ExecStack : IOperandStack
    {
        private readonly Stack<Value> _stack = new Stack<Value>();
        public void PushI32(int value) => _stack.Push(value);
        public void PushI64(long value) => _stack.Push(value);
        public void PushF32(float value) => _stack.Push(value);
        public void PushF64(double value) => _stack.Push(value);
        public void PushV128(Value value) => _stack.Push(value);
        public void PushFuncref(Value value) => _stack.Push(value);
        public void PushExternref(Value value) => _stack.Push(value);

        public void PushValue(Value value) => _stack.Push(value);
        
        public Value PopI32() => _stack.Pop();
        public Value PopI64() => _stack.Pop();
        public Value PopF32() => _stack.Pop();
        public Value PopF64() => _stack.Pop();
        public Value PopV128() => _stack.Pop();
        public Value PopRefType() => _stack.Pop();

        public Value PopAny() => _stack.Pop();
        public Value Peek() => _stack.Peek();
    }

    public class ValidationStack : IOperandStack
    {
        private readonly Stack<Value> _stack = new Stack<Value>();

        public void PushI32(int i32) {
            Value value = i32; 
            if (value.Type != ValType.I32)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.I32}");
            _stack.Push(value);
        }
        public void PushI64(long i64) {
            Value value = i64;
            if (value.Type != ValType.I64)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.I64}");
            _stack.Push(value);
        }

        public void PushF32(float f32) {
            Value value = f32;
            if (value.Type != ValType.F32)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.F32}");
            _stack.Push(value);
        }

        public void PushF64(double f64) {
            Value value = f64;
            if (value.Type != ValType.F64)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.F64}");
            _stack.Push(value);
        }

        public void PushV128(Value value) {
            if (value.Type != ValType.V128)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.V128}");
            _stack.Push(value);
        }

        public void PushFuncref(Value value) {
            if (value.Type != ValType.Funcref)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.Funcref}");
            _stack.Push(value);
        }

        public void PushExternref(Value value) {
            if (value.Type != ValType.Externref)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.Externref}");
            _stack.Push(value);
        }

        public void PushValue(Value value) {
            _stack.Push(value);
        }

        public Value PopI32()
        {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");

            Value value = _stack.Pop();
            if (value.Type != ValType.I32)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.I32}");
            
            return value;
        }
        public Value PopI64() {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            Value value = _stack.Pop();
            if (value.Type != ValType.I64)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.I64}");
            
            return value;
        }

        public Value PopF32() {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            Value value = _stack.Pop();
            if (value.Type != ValType.F32)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.F32}");
            
            return value;
        }

        public Value PopF64() {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            Value value = _stack.Pop();
            if (value.Type != ValType.F64)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.F64}");
            
            return value;
        }

        public Value PopV128() {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            Value value = _stack.Pop();
            if (value.Type != ValType.V128)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.V128}");
            
            return value;
        }

        public Value PopRefType() {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            Value value = _stack.Pop();
            return value.Type switch
            {
                ValType.Funcref => value,
                ValType.Externref => value,
                _ => throw new InvalidDataException(
                    $"Wrong operand type {value.Type} at top of stack. Expected: FuncRef or ExternRef")
            };
        }

        public Value PopAny()
        {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            return _stack.Pop();
        }

        public Value Peek()
        {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            return _stack.Peek();
        }
    }
    
}