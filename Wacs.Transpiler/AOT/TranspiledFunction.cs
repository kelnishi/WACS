// Copyright 2025 Kelvin Nishikawa
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
using System.Reflection;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Adapter that wraps a transpiled .NET static method as an IFunctionInstance.
    /// This enables the interpreter to call transpiled code through the standard
    /// Store dispatch path, and vice versa.
    ///
    /// The wrapped method has signature: static T MethodName(TranspiledContext ctx, ...)
    /// When Invoke(ExecContext) is called, parameters are popped from OpStack,
    /// the transpiled method is invoked, and results are pushed back onto OpStack.
    /// </summary>
    public class TranspiledFunction : IFunctionInstance
    {
        private readonly MethodInfo _method;
        public MethodInfo Method => _method;
        private readonly TranspiledContext _ctx;
        private readonly object?[] _paramBuffer;
        private readonly int _paramCount;
        private readonly int _resultCount;

        public FunctionType Type { get; }
        public string Name { get; set; } = "";
        public string Id => string.IsNullOrEmpty(Name) ? "" : Name;
        public bool IsExport { get; set; }
        public bool IsAsync => false;

        private readonly int _outParamCount;

        public TranspiledFunction(
            MethodInfo method,
            FunctionType type,
            TranspiledContext ctx)
        {
            _method = method;
            Type = type;
            _ctx = ctx;
            _paramCount = type.ParameterTypes.Arity;
            _resultCount = type.ResultType.Arity;
            _outParamCount = _resultCount > 1 ? _resultCount - 1 : 0;
            // +1 for TranspiledContext + out params
            _paramBuffer = new object?[1 + _paramCount + _outParamCount];
            _paramBuffer[0] = ctx;
        }

        public void SetName(string name) => Name = name;

        public void Invoke(ExecContext context)
        {
            // Pop parameters from the interpreter's OpStack (reverse order)
            for (int i = _paramCount; i > 0; i--)
            {
                var val = context.OpStack.PopAny();
                _paramBuffer[i] = ConvertFromValue(val, Type.ParameterTypes.Types[i - 1]);
            }

            // Invoke the transpiled method
            object? result;
            try
            {
                result = _method.Invoke(null, _paramBuffer);
            }
            catch (System.Reflection.TargetInvocationException tie)
            {
                var inner = tie.InnerException;
                if (inner is TrapException)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(inner);
                }
                // Wrap CLR arithmetic/memory exceptions as WASM traps
                if (inner is DivideByZeroException)
                    throw new TrapException("integer divide by zero");
                if (inner is OverflowException)
                    throw new TrapException("integer overflow");
                if (inner is IndexOutOfRangeException)
                    throw new TrapException("out of bounds memory access");

                if (inner != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(inner);
                throw;
            }

            // Push results back onto OpStack
            if (_resultCount >= 1)
            {
                if (result == null)
                    throw new System.InvalidOperationException(
                        $"TranspiledFunction '{Name}' expected {_resultCount} result(s) but method returned null");
                // Result 0 is the CLR return value
                context.OpStack.PushValue(ConvertToValue(result, Type.ResultType.Types[0]));
                // Results 1..N are in the out param slots of _paramBuffer
                for (int i = 0; i < _outParamCount; i++)
                {
                    var outVal = _paramBuffer[1 + _paramCount + i];
                    context.OpStack.PushValue(ConvertToValue(outVal!, Type.ResultType.Types[i + 1]));
                }
            }
        }

        private static object ConvertFromValue(Value val, ValType type)
        {
            return type switch
            {
                ValType.I32 => val.Data.Int32,
                ValType.I64 => val.Data.Int64,
                ValType.F32 => val.Data.Float32,
                ValType.F64 => val.Data.Float64,
                _ => val // Reference types stay as Value
            };
        }

        private static Value ConvertToValue(object obj, ValType type)
        {
            return type switch
            {
                ValType.I32 => new Value((int)obj),
                ValType.I64 => new Value((long)obj),
                ValType.F32 => new Value((float)obj),
                ValType.F64 => new Value((double)obj),
                _ => (Value)obj
            };
        }
    }
}
