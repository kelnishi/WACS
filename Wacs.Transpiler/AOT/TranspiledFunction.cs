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
        private readonly TranspiledContext _ctx;
        private readonly object?[] _paramBuffer;
        private readonly int _paramCount;
        private readonly int _resultCount;

        public FunctionType Type { get; }
        public string Name { get; set; } = "";
        public string Id => string.IsNullOrEmpty(Name) ? "" : Name;
        public bool IsExport { get; set; }
        public bool IsAsync => false;

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
            // +1 for the TranspiledContext first parameter
            _paramBuffer = new object?[_paramCount + 1];
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
            var result = _method.Invoke(null, _paramBuffer);

            // Push results back onto OpStack
            if (_resultCount == 1 && result != null)
            {
                context.OpStack.PushValue(ConvertToValue(result, Type.ResultType.Types[0]));
            }
            // TODO: Handle multi-value returns (WasmReturn structs) in later phases
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
