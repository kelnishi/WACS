using System;
using System.Collections.Generic;
using System.IO;
using Wacs.Core.Types;

namespace Wacs.Core.Execution
{
    public interface IOperandStack
    {
        void PushI32(StackValue value);
        void PushI64(StackValue value);
        void PushF32(StackValue value);
        void PushF64(StackValue value);
        void PushV128(StackValue value);
        void PushFuncref(StackValue value);
        void PushExternref(StackValue value);
        
        StackValue PopI32();
        StackValue PopI64();
        StackValue PopF32();
        StackValue PopF64();
        StackValue PopV128();
        StackValue PopFuncref();
        StackValue PopExternref();
    }
    
    public class ExecStack : IOperandStack
    {
        private readonly Stack<StackValue> _stack = new Stack<StackValue>();
        public void PushI32(StackValue value) => _stack.Push(value);
        public void PushI64(StackValue value) => _stack.Push(value);
        public void PushF32(StackValue value) => _stack.Push(value);
        public void PushF64(StackValue value) => _stack.Push(value);
        public void PushV128(StackValue value) => _stack.Push(value);
        public void PushFuncref(StackValue value) => _stack.Push(value);
        public void PushExternref(StackValue value) => _stack.Push(value);
        public StackValue PopI32() => _stack.Pop();
        public StackValue PopI64() => _stack.Pop();
        public StackValue PopF32() => _stack.Pop();
        public StackValue PopF64() => _stack.Pop();
        public StackValue PopV128() => _stack.Pop();
        public StackValue PopFuncref() => _stack.Pop();
        public StackValue PopExternref() => _stack.Pop();
    }

    public class ValidationStack : IOperandStack
    {
        private readonly Stack<StackValue> _stack = new Stack<StackValue>();

        public void PushI32(StackValue value) {
            if (value.Type != ValType.I32)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.I32}");
            _stack.Push(value);
        }
        public void PushI64(StackValue value) {
            if (value.Type != ValType.I64)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.I64}");
            _stack.Push(value);
        }

        public void PushF32(StackValue value) {
            if (value.Type != ValType.F32)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.F32}");
            _stack.Push(value);
        }

        public void PushF64(StackValue value) {
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

        public StackValue PopFuncref() {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            StackValue value = _stack.Pop();
            if (value.Type != ValType.Funcref)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.Funcref}");
            
            return value;
        }

        public StackValue PopExternref() {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            StackValue value = _stack.Pop();
            if (value.Type != ValType.Externref)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} popped from stack. Expected:{ValType.Externref}");
            
            return value;
        }
        
    }
    
}