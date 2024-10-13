using System;
using System.Collections.Generic;
using System.IO;
using Wacs.Core.Types;

namespace Wacs.Core.Execution
{
    public interface IOperandStack
    {
        void PushI32(int value);
        void PushI64(long value);
        void PushF32(float value);
        void PushF64(double value);
        void PushV128(StackValue value);
        void PushFuncref(StackValue value);
        void PushExternref(StackValue value);

        void PushValue(StackValue value);
        
        StackValue PopI32();
        StackValue PopI64();
        StackValue PopF32();
        StackValue PopF64();
        StackValue PopV128();
        StackValue PopRefType();
        StackValue PopAny();
        StackValue Peek();
    }
    
    public class ExecStack : IOperandStack
    {
        private readonly Stack<StackValue> _stack = new Stack<StackValue>();
        public void PushI32(int value) => _stack.Push(value);
        public void PushI64(long value) => _stack.Push(value);
        public void PushF32(float value) => _stack.Push(value);
        public void PushF64(double value) => _stack.Push(value);
        public void PushV128(StackValue value) => _stack.Push(value);
        public void PushFuncref(StackValue value) => _stack.Push(value);
        public void PushExternref(StackValue value) => _stack.Push(value);

        public void PushValue(StackValue value) => _stack.Push(value);
        
        public StackValue PopI32() => _stack.Pop();
        public StackValue PopI64() => _stack.Pop();
        public StackValue PopF32() => _stack.Pop();
        public StackValue PopF64() => _stack.Pop();
        public StackValue PopV128() => _stack.Pop();
        public StackValue PopRefType() => _stack.Pop();

        public StackValue PopAny() => _stack.Pop();
        public StackValue Peek() => _stack.Peek();
    }

    public class ValidationStack : IOperandStack
    {
        private readonly Stack<StackValue> _stack = new Stack<StackValue>();

        public void PushI32(int i32) {
            StackValue value = i32; 
            if (value.Type != ValType.I32)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.I32}");
            _stack.Push(value);
        }
        public void PushI64(long i64) {
            StackValue value = i64;
            if (value.Type != ValType.I64)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.I64}");
            _stack.Push(value);
        }

        public void PushF32(float f32) {
            StackValue value = f32;
            if (value.Type != ValType.F32)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.F32}");
            _stack.Push(value);
        }

        public void PushF64(double f64) {
            StackValue value = f64;
            if (value.Type != ValType.F64)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.F64}");
            _stack.Push(value);
        }

        public void PushV128(StackValue value) {
            if (value.Type != ValType.V128)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.V128}");
            _stack.Push(value);
        }

        public void PushFuncref(StackValue value) {
            if (value.Type != ValType.Funcref)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.Funcref}");
            _stack.Push(value);
        }

        public void PushExternref(StackValue value) {
            if (value.Type != ValType.Externref)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.Externref}");
            _stack.Push(value);
        }

        public void PushValue(StackValue value) {
            _stack.Push(value);
        }

        public StackValue PopI32()
        {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");

            StackValue value = _stack.Pop();
            if (value.Type != ValType.I32)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.I32}");
            
            return value;
        }
        public StackValue PopI64() {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            StackValue value = _stack.Pop();
            if (value.Type != ValType.I64)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.I64}");
            
            return value;
        }

        public StackValue PopF32() {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            StackValue value = _stack.Pop();
            if (value.Type != ValType.F32)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.F32}");
            
            return value;
        }

        public StackValue PopF64() {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            StackValue value = _stack.Pop();
            if (value.Type != ValType.F64)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.F64}");
            
            return value;
        }

        public StackValue PopV128() {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            StackValue value = _stack.Pop();
            if (value.Type != ValType.V128)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.V128}");
            
            return value;
        }

        public StackValue PopRefType() {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            StackValue value = _stack.Pop();
            return value.Type switch
            {
                ValType.Funcref => value,
                ValType.Externref => value,
                _ => throw new InvalidDataException(
                    $"Wrong operand type {value.Type} at top of stack. Expected: FuncRef or ExternRef")
            };
        }

        public StackValue PopAny()
        {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            return _stack.Pop();
        }

        public StackValue Peek()
        {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            return _stack.Peek();
        }
    }
    
}