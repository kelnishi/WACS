using System;
using System.Reflection;
using Wacs.Core.Attributes;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.6. Function Instances
    /// Represents a host (native) function instance provided by the host environment.
    /// </summary>
    public class HostFunction : IFunctionInstance
    {
        /// <summary>
        /// The delegate representing the host function implementation.
        /// </summary>
        private readonly Delegate _hostFunction;

        private readonly MethodInfo _invoker;

        private bool _captureReturn = false;

        private ConversionHelper?[] _parameterConversions = null!;
        private ConversionHelper?[] _resultConversions = null!;

        public object[] ParameterBuffer;

        /// <summary>
        /// @Spec 4.5.3.2. Host Functions
        /// Initializes a new instance of the <see cref="HostFunction"/> class.
        /// </summary>
        /// <param name="type">The function type.</param>
        /// <param name="delType">The System.Type of the delegate must match type.</param>
        /// <param name="hostFunction">The delegate representing the host function.</param>
        /// <param name="passCtx">True if the specified function type had Store as the first type</param>
        public HostFunction((string module, string entity) id, FunctionType type, Type delType, Delegate hostFunction)
        {
            Type = type;
            _hostFunction = hostFunction;
            _invoker = delType.GetMethod("Invoke")!;
            (ModuleName, Name) = id;

            var invokerParams = _invoker.GetParameters();
            if (invokerParams[0].ParameterType == typeof(ExecContext))
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
        public string Name { get; }
        public void SetName(string value) {}
        public string Id => $"{ModuleName}.{Name}";

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
            
            _resultConversions = new ConversionHelper?[Type.ResultType.Length];
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

            for (; idx < Type.ResultType.Length; ++idx)
            {
                int pIdx = parameters.Length - idx - 1;
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
        /// <param name="args">The arguments to pass to the function.</param>
        /// <param name="opStack">The Operand Stack to push results onto.</param>
        public void Invoke(object[] args, OpStack opStack)
        {
            for (int i = 0; i < _parameterConversions.Length; ++i)
            {
                args[i] = _parameterConversions[i]?.Invoke(args[i]) ?? args[i];
            }

            try
            {
                var returnValue = _invoker.Invoke(_hostFunction, args);

                int outArgs = Type.ResultType.Types.Length;
                int j = 0;
                if (_captureReturn)
                {
                    outArgs -= 1;
                    if (_resultConversions[j] != null)
                        returnValue = _resultConversions[j]?.Invoke(returnValue) ?? returnValue;
                    opStack.PushValue(new Value(returnValue));
                    ++j;
                }
                
                int idx = args.Length - outArgs;
                for (; idx < args.Length; ++idx, ++j)
                {
                    var returnVal = args[idx];
                    if (_resultConversions[j] != null)
                        returnVal = _resultConversions[j]?.Invoke(returnVal) ?? returnVal;
                    opStack.PushValue(new Value(returnVal));
                }
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException!;
            }
        }

        delegate object ConversionHelper(object value);
    }
}