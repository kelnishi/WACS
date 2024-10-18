using System;
using System.Collections.Generic;
using System.IO;
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
        void PushV128((ulong, ulong) lowhigh = default);
        void PushFuncref(Value value);
        void PushExternref(Value value);
        void PushType(ValType type);
        Value PopI32();
        Value PopI64();
        Value PopF32();
        Value PopF64();
        Value PopV128();
        Value PopRefType();
        void ValidateStack(ResultType types, bool keep = true);
        Value PopType(ValType type);
        Value PopAny();
    }
    
    public class ValidationOpStack : IValidationOpStack
    {
        private readonly Stack<Value> _stack = new();

        public void Clear() => _stack.Clear();

        public void Push(ResultType types) {
            foreach (var type in types.Types)
            {
                PushType(type);
            }
        }
        
        public void PushI32(int i32 = 0) {
            Value value = i32; 
            if (value.Type != ValType.I32)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.I32}");
            _stack.Push(value);
        }
        public void PushI64(long i64 = 0) {
            Value value = i64;
            if (value.Type != ValType.I64)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.I64}");
            _stack.Push(value);
        }

        public void PushF32(float f32 = 0.0f) {
            Value value = f32;
            if (value.Type != ValType.F32)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.F32}");
            _stack.Push(value);
        }

        public void PushF64(double f64 = 0.0d) {
            Value value = f64;
            if (value.Type != ValType.F64)
                throw new InvalidDataException($"Wrong operand type {value.Type} pushed to stack. Expected: {ValType.F64}");
            _stack.Push(value);
        }

        public void PushV128((ulong, ulong) lowhigh = default)
        {
            Value value = lowhigh;
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

        public void PushType(ValType type) {
            _stack.Push(new Value(type));
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

        public void ValidateStack(ResultType types, bool keep = true)
        {
            Stack<Value> aside = new Stack<Value>();
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
                    throw new InvalidDataException("Invalid Operand Stack did not match ResultType");
                if (keep)
                    PushType(p.Type);
            }
        }

        public Value PopType(ValType type)
        {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");

            Value value = _stack.Pop();
            if (value.Type != type)
                throw new InvalidDataException(
                    $"Wrong operand type {value.Type} at top of stack. Expected: {type}");
            return value;
        }

        public Value PopAny()
        {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Operand stack underflow.");
            
            return _stack.Pop();
        }
    }

    public class UnreachableOpStack : IValidationOpStack
    {
        public void Clear() { }
        
        public void Push(ResultType types) {}
        public void PushI32(int i32 = 0) {}
        public void PushI64(long i64 = 0) { }
        public void PushF32(float f32 = 0) { }
        public void PushF64(double f64 = 0) { }
        public void PushV128((ulong, ulong) lowhigh = default) { }
        public void PushFuncref(Value value) { }
        public void PushExternref(Value value) { }
        public void PushType(ValType type) { }
        public Value PopI32() => new(ValType.I32);
        public Value PopI64() => new(ValType.I64);
        public Value PopF32() => new(ValType.F32);
        public Value PopF64() => new(ValType.F64);
        public Value PopV128() => new(ValType.V128);

        public Value PopRefType() => Value.NullFuncRef;

        public void ValidateStack(ResultType types, bool keep = true) {}
        public Value PopType(ValType type) => new(type);
        public Value PopAny() => new(ValType.I32);
    }
}