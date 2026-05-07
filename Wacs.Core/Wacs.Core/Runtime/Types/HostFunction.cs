// Copyright 2024 Kelvin Nishikawa
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
using System.Threading.Tasks;
using Wacs.Core.Attributes;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.6. Function Instances
    /// Represents a host (native) function instance provided by the host environment.
    /// </summary>
    public class HostFunction : IFunctionInstance
    {
        private readonly bool _captureReturn = false;

        /// <summary>
        /// The delegate representing the host function implementation.
        /// </summary>
        private readonly Delegate _hostFunction;

        private readonly MethodInfo _invoker;

        private ConversionHelper?[] _parameterConversions = null!;
        private ConversionHelper?[] _resultConversions = null!;

        public object[] ParameterBuffer;

        /// <summary>
        /// @Spec 4.5.3.2. Host Functions
        /// Initializes a new instance of the <see cref="HostFunction"/> class.
        /// </summary>
        /// <param name="id">The bound export name</param>
        /// <param name="type">The function type.</param>
        /// <param name="delType">The System.Type of the delegate must match type.</param>
        /// <param name="hostFunction">The delegate representing the host function.</param>
        /// <param name="isAsync">True if the function returns a System.Threading.Task</param>
        public HostFunction((string module, string entity) id, FunctionType type, Type delType, Delegate hostFunction, bool isAsync)
        {
            Type = type;
            _hostFunction = hostFunction;
            _invoker = delType.GetMethod("Invoke")!;
            IsAsync = isAsync;
            (ModuleName, Name) = id;

            var invokerParams = _invoker.GetParameters();
            if (invokerParams.Length > 0 && invokerParams[0].ParameterType == typeof(ExecContext))
            {
                PassExecContext = true;
            }
            
            int parameterCount = type.ParameterTypes.Arity;
            
            if (PassExecContext)
                parameterCount += 1;
            if (type.ResultType.Arity > 0)
            {
                parameterCount += type.ResultType.Arity - 1;
                if (_invoker.ReturnParameter?.ParameterType != typeof(void))
                {
                    _captureReturn = true;
                }
                else
                {
                    parameterCount += 1;
                }
            }
            ParameterBuffer = new object[parameterCount];
            BuildConversionHelpers();
        }

        public bool PassExecContext { get; set; }

        public string ModuleName { get; }

        public bool IsAsync { get; }

        public string Name { get; }
        public void SetName(string value) {}
        public string Id => $"{ModuleName}.{Name}";

        public bool IsExport
        {
            get => true;
            set => throw new NotImplementedException("Host functions are inherently exported.");
        }

        public FunctionType Type { get; }

        public Span<object> GetParameterBuf(ExecContext ctx)
        {
            var span = ParameterBuffer.AsSpan();
            if (!PassExecContext) return span;
            span[0] = ctx;
            return span[1..];
        }

        private void BuildConversionHelpers()
        {
            var parameters = _invoker.GetParameters();
            _parameterConversions = new ConversionHelper?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                if (paramType == typeof(ExecContext))
                {
                    if (i > 0)
                        throw new ArgumentException($"Host binding ({ModuleName} {Name}) has invalid parameters. ExecContext may only be the first parameter");
                }

                _parameterConversions[i] = paramType switch
                {
                    { } t when t == typeof(ExecContext) => wasmValue => wasmValue,
                    { } t when t == typeof(char) => wasmValue => (char)(int)wasmValue,
                    { } t when t == typeof(byte) => wasmValue => (byte)(int)wasmValue,
                    { } t when t == typeof(sbyte) => wasmValue => (sbyte)(int)wasmValue,
                    { } t when t == typeof(ushort) => wasmValue => (ushort)(int)wasmValue,
                    { } t when t == typeof(short) => wasmValue => (short)(int)wasmValue,
                    { } t when t == typeof(uint) => wasmValue => (uint)(int)wasmValue,
                    { } t when t.GetWasmType() is { } wasmType => CreateConversionHelper(t),
                    _ => null
                };
            }
            
            _resultConversions = new ConversionHelper?[Type.ResultType.Arity];
            int idx = 0;
            if (_captureReturn)
            {
                var returnType = _invoker.ReturnParameter!.ParameterType;
                _resultConversions[idx++] = returnType switch
                {
                    { } t when t == typeof(char) => hostValue => (int)hostValue,
                    { } t when t == typeof(byte) => hostValue => (int)hostValue,
                    { } t when t == typeof(sbyte) => hostValue => (int)hostValue,
                    { } t when t == typeof(ushort) => hostValue => (int)hostValue,
                    { } t when t == typeof(short) => hostValue => (int)hostValue,
                    { } t when t.GetWasmType() is { } wasmType => CreateReturnHelper(t),
                    _ => null
                };
            }

            for (; idx < Type.ResultType.Arity; ++idx)
            {
                int pIdx = parameters.Length - idx - 1;
                if (pIdx < 0 || pIdx >= parameters.Length)
                    break;
                
                var outType = parameters[pIdx].ParameterType;
                var paramType = outType.GetElementType();
                _resultConversions[idx] = paramType switch
                {
                    { } t when t == typeof(char) => hostValue => (int)hostValue,
                    { } t when t == typeof(byte) => hostValue => (int)hostValue,
                    { } t when t == typeof(sbyte) => hostValue => (int)hostValue,
                    { } t when t == typeof(ushort) => hostValue => (int)hostValue,
                    { } t when t == typeof(short) => hostValue => (int)hostValue,
                    { } t when t.GetWasmType() is { } wasmType => CreateReturnHelper(t),
                    _ => null
                };
            }
        }

        private static uint ConvertInt(int value) => BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
        private static ulong ConvertLong(long value) => BitConverter.ToUInt64(BitConverter.GetBytes(value), 0);

        private static ConversionHelper CreateConversionHelper(Type hostType)
        {
            if (hostType.IsEnum)
            {
                Type baseType = Enum.GetUnderlyingType(hostType);
                return wasmValue => baseType switch
                {
                    { } t when t == typeof(sbyte) => Enum.ToObject(hostType, (sbyte)(int)wasmValue),
                    { } t when t == typeof(byte) => Enum.ToObject(hostType, (byte)(int)wasmValue),
                    { } t when t == typeof(short) => Enum.ToObject(hostType, (short)(int)wasmValue),
                    { } t when t == typeof(ushort) => Enum.ToObject(hostType, (ushort)(int)wasmValue),
                    { } t when t == typeof(int) => Enum.ToObject(hostType, (int)wasmValue),
                    { } t when t == typeof(uint) => Enum.ToObject(hostType, ConvertInt((int)wasmValue)),
                    { } t when t == typeof(long) => Enum.ToObject(hostType, (long)wasmValue),
                    { } t when t == typeof(ulong) => Enum.ToObject(hostType, ConvertLong((long)wasmValue)),
                    _ => throw new ArgumentException($"Unsupported underlying type: {baseType} for enum type {hostType}")
                };
            }
            if (hostType.IsValueType && typeof(ITypeConvertable).IsAssignableFrom(hostType))
            {
                return wasmValue =>
                {
                    var instance = Activator.CreateInstance(hostType);
                    ((ITypeConvertable)instance).FromWasmValue(wasmValue);
                    return instance;
                }; 
            }

            throw new ArgumentException($"Runtime cannot automatically convert value to host type {hostType}");
        }

        private static ConversionHelper CreateReturnHelper(Type hostType)
        {
            if (hostType.IsEnum)
            {
                Type baseType = Enum.GetUnderlyingType(hostType);
                return hostValue => baseType switch
                {
                    { } t when t == typeof(sbyte) => (sbyte)hostValue,
                    { } t when t == typeof(byte) => (byte)hostValue,
                    { } t when t == typeof(short) => (short)hostValue,
                    { } t when t == typeof(ushort) => (ushort)hostValue,
                    { } t when t == typeof(int) => (int)hostValue,
                    { } t when t == typeof(uint) => (uint)hostValue,
                    { } t when t == typeof(long) => (long)hostValue,
                    { } t when t == typeof(ulong) => (ulong)hostValue,
                    _ => throw new ArgumentException($"Unsupported underlying type: {baseType} for enum type {hostType}")
                };
            }
            if (hostType.IsValueType && typeof(ITypeConvertable).IsAssignableFrom(hostType))
            {
                return hostValue => ((ITypeConvertable)hostValue).ToWasmType();
            }

            throw new ArgumentException($"Runtime cannot automatically convert host value to wasm type {hostType}");
        }

        /// <summary>
        /// Invokes the host function with the given arguments.
        /// Pushes any results onto the passed OpStack.
        /// </summary>
        public void Invoke(ExecContext context)
        {
            if (IsAsync)
                throw new WasmRuntimeException("Cannot call asynchronous function synchronously");

            //Fetch the parameters
            context.OpStack.PopScalars(Type.ParameterTypes, ParameterBuffer, PassExecContext?1:0);
            if (PassExecContext) 
                ParameterBuffer[0] = context;
            
            for (int i = 0; i < _parameterConversions.Length; ++i)
            {
                ParameterBuffer[i] = _parameterConversions[i]?.Invoke(ParameterBuffer[i]) ?? ParameterBuffer[i];
            }

            if (IsAsync)
                throw new WasmRuntimeException("Cannot synchronously execute Async Function");
            try
            {
                var returnValue = _invoker.Invoke(_hostFunction, ParameterBuffer);
                int outArgs = Type.ResultType.Types.Length;
                int j = 0;
                if (_captureReturn)
                {
                    outArgs -= 1;
                    if (_resultConversions[j] != null)
                        returnValue = _resultConversions[j]?.Invoke(returnValue) ?? returnValue;
                    context.OpStack.PushValue(new Value(returnValue));
                    ++j;
                }
                
                int idx = ParameterBuffer.Length - outArgs;
                for (; idx < ParameterBuffer.Length; ++idx, ++j)
                {
                    var returnVal = ParameterBuffer[idx];
                    if (_resultConversions[j] != null)
                        returnVal = _resultConversions[j]?.Invoke(returnVal) ?? returnVal;
                    context.OpStack.PushValue(new Value(returnVal));
                }
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException!;
            }   
        }

        public async ValueTask InvokeAsync(ExecContext context)
        {
            //Fetch the parameters
            context.OpStack.PopScalars(Type.ParameterTypes, ParameterBuffer, PassExecContext?1:0);
            if (PassExecContext) 
                ParameterBuffer[0] = context;
            
            for (int i = 0; i < _parameterConversions.Length; ++i)
            {
                ParameterBuffer[i] = _parameterConversions[i]?.Invoke(ParameterBuffer[i]) ?? ParameterBuffer[i];
            }
            
            if (!IsAsync)
                throw new WasmRuntimeException("Cannot asynchronously execute Synchronous Function");

            try
            {
                var task = _invoker.Invoke(_hostFunction, ParameterBuffer) as Task;
                await task;

                int outArgs = Type.ResultType.Types.Length;
                int j = 0;
                if (_captureReturn)
                {
                    outArgs -= 1;
                        
                    var resultProperty = task.GetType().GetProperty("Result");
                    var returnValue = resultProperty?.GetValue(task) ?? null;
                        
                    if (_resultConversions[j] != null)
                        returnValue = _resultConversions[j]?.Invoke(returnValue) ?? returnValue;
                    context.OpStack.PushValue(new Value(returnValue));
                    ++j;
                }
                
                int idx = ParameterBuffer.Length - outArgs;
                for (; idx < ParameterBuffer.Length; ++idx, ++j)
                {
                    var returnVal = ParameterBuffer[idx];
                    if (_resultConversions[j] != null)
                        returnVal = _resultConversions[j]?.Invoke(returnVal) ?? returnVal;
                    context.OpStack.PushValue(new Value(returnVal));
                }
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException!;
            }
        }

        public override string ToString() => $"HostFunction[{Id}] (Type: {Type})";

        delegate object ConversionHelper(object value);
    }
}