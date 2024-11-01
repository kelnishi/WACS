using System;
using System.Linq;
using System.Reflection;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public static class Delegates
    {
        public delegate object GenericFunc(params object[] args);

        // Named delegate types for common WebAssembly function signatures
        public delegate void WasmAction();

        public delegate void WasmAction<T>(Value arg);

        public delegate void WasmAction<T1,T2>(Value arg1, Value arg2);

        public delegate void WasmAction<T1,T2,T3>(Value arg1, Value arg2, Value arg3);

        public delegate Value WasmFunc<TResult>();

        public delegate Value WasmFunc<T, TResult>(Value arg);

        public delegate Value WasmFunc<T1, T2, TResult>(Value arg1, Value arg2);

        public delegate Value WasmFunc<T1, T2, T3, TResult>(Value arg1, Value arg2, Value arg3);

        public static void ValidateFunctionTypeCompatibility(FunctionType functionType, Type delegateType)
        {
            if (!typeof(Delegate).IsAssignableFrom(delegateType))
            {
                throw new ArgumentException($"The type {delegateType.Name} is not a delegate type.");
            }

            MethodInfo invokeMethod = delegateType.GetMethod("Invoke");
            if (invokeMethod == null)
            {
                throw new ArgumentException($"The delegate type {delegateType.Name} does not have an Invoke method.");
            }

            ParameterInfo[] parameters = invokeMethod.GetParameters();
            Type returnType = invokeMethod.ReturnType;

            // Check if the number of parameters matches
            if (parameters.Length != functionType.ParameterTypes.Types.Length)
            {
                throw new ArgumentException(
                    $"Parameter count mismatch. FunctionType has {functionType.ParameterTypes.Types.Length} parameter(s), but delegate has {parameters.Length}.");
            }

            // Check if parameter types match
            for (int i = 0; i < parameters.Length; i++)
            {
                Type expectedType = ConvertValTypeToSystemType(functionType.ParameterTypes.Types[i]);
                var pType = parameters[i].ParameterType;
                // Check if pType has a constructor that takes expectedType
                if (pType.GetConstructor(new[] { expectedType }) == null)
                {
                    throw new ArgumentException(
                        $"Parameter type mismatch at position {i}. Expected {expectedType.Name}, but delegate has {parameters[i].ParameterType.Name}.");
                }
            }


            // Check if return type matches
            if (functionType.ResultType.Types.Length == 0)
            {
                if (returnType != typeof(void))
                {
                    throw new ArgumentException(
                        $"Return type mismatch. FunctionType has no return value, but delegate returns {returnType.Name}.");
                }
            }
            else if (functionType.ResultType.Types.Length == 1)
            {
                Type expectedReturnType = ConvertValTypeToSystemType(functionType.ResultType.Types[0]);
                var implicitOp = returnType
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
                    .Where(m => m.Name == "op_Implicit" && m.ReturnType == expectedReturnType)
                    .FirstOrDefault();
                // Check if returnType has an implicit conversion operator to expectedReturnType
                if (implicitOp is null)
                {
                    throw new ArgumentException(
                        $"Return type mismatch. Expected return type is {expectedReturnType.Name}, but delegate returns {returnType.Name}.");
                }
            }
            else
            {
                throw new ArgumentException("WebAssembly functions should have at most one return value.");
            }
        }

        public static Delegate AnonymousFunctionFromType(FunctionType functionType, GenericFunc implementation)
        {
            var paramTypes = functionType.ParameterTypes.Types;
            var resultTypes = functionType.ResultType.Types;

            if (resultTypes.Length > 1)
            {
                throw new NotSupportedException("Multiple return values are not supported in C# delegates.");
            }

            return (paramTypes.Length, resultTypes.Length) switch
            {
                (0, 0) => new WasmAction(() => { implementation(); }),
                (1, 0) => CreateWasmAction<object>(paramTypes[0], implementation),
                (2, 0) => CreateWasmAction<object, object>(paramTypes[0], paramTypes[1], implementation),
                (3, 0) => CreateWasmAction<object, object, object>(paramTypes[0], paramTypes[1], paramTypes[2], implementation),
                (0, 1) => CreateWasmFunc<object>(resultTypes[0], implementation),
                (1, 1) => CreateWasmFunc<object, object>(paramTypes[0], resultTypes[0], implementation),
                (2, 1) => CreateWasmFunc<object, object, object>(paramTypes[0], paramTypes[1], resultTypes[0], implementation),
                (3, 1) => CreateWasmFunc<object, object, object, object>(paramTypes[0], paramTypes[1], paramTypes[2], resultTypes[0], implementation),
                _ => throw new NotSupportedException($"Unsupported function signature: ({string.Join(", ", paramTypes)}) -> ({string.Join(", ", resultTypes)})")
            };
        }


        private static Delegate CreateWasmAction<T>(ValType paramType, GenericFunc implementation) =>
            new WasmAction<T>(arg => implementation(arg));

        private static Delegate CreateWasmAction<T1, T2>(ValType param1Type, ValType param2Type, GenericFunc implementation) =>
            new WasmAction<T1, T2>((arg1, arg2) => implementation(arg1, arg2));

        private static Delegate CreateWasmAction<T1, T2, T3>(ValType param1Type, ValType param2Type, ValType param3Type, GenericFunc implementation) =>
            new WasmAction<T1, T2, T3>((arg1, arg2, arg3) => implementation(arg1, arg2, arg3));

        private static Delegate CreateWasmFunc<TResult>(ValType resultType, GenericFunc implementation) =>
            new WasmFunc<TResult>(() => new Value(implementation()));

        private static Delegate CreateWasmFunc<T, TResult>(ValType paramType, ValType resultType, GenericFunc implementation) =>
            new WasmFunc<T, TResult>(arg => new Value(implementation(arg)));

        private static Delegate CreateWasmFunc<T1, T2, TResult>(ValType param1Type, ValType param2Type, ValType resultType, GenericFunc implementation) =>
            new WasmFunc<T1, T2, TResult>((arg1, arg2) => new Value(implementation(arg1, arg2)));

        private static Delegate CreateWasmFunc<T1, T2, T3, TResult>(ValType param1Type, ValType param2Type, ValType param3Type, ValType resultType, GenericFunc implementation) =>
            new WasmFunc<T1, T2, T3, TResult>((arg1, arg2, arg3) => new Value(implementation(arg1, arg2, arg3)));


        public static Delegate CreateTypedDelegate(Delegate genericDelegate, Type desiredDelegateType)
        {
            var genericMethod = typeof(Delegates).GetMethod(nameof(CreateTypedDelegateInternal), BindingFlags.NonPublic | BindingFlags.Static);
            var typedMethod = genericMethod.MakeGenericMethod(desiredDelegateType);
            return (Delegate)typedMethod.Invoke(null, new object[] { genericDelegate });
        }

        private static TDelegate CreateTypedDelegateInternal<TDelegate>(Delegate genericDelegate) where TDelegate : Delegate
        {
            return (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), genericDelegate.Target, genericDelegate.Method);
        }

        private static FunctionType GetFunctionTypeFromDelegate(Delegate del)
        {
            var method = del.Method;
            var parameters = method.GetParameters().Select(p => GetValTypeFromSystemType(p.ParameterType)).ToArray();
            var returnType = method.ReturnType == typeof(void) ? Array.Empty<ValType>() : new[] { GetValTypeFromSystemType(method.ReturnType) };
            return new FunctionType(new ResultType(parameters), new ResultType(returnType));
        }

        private static ValType GetValTypeFromSystemType(Type type)
        {
            if (type == typeof(int)) return ValType.I32;
            if (type == typeof(long)) return ValType.I64;
            if (type == typeof(float)) return ValType.F32;
            if (type == typeof(double)) return ValType.F64;
            if (type == typeof(byte[])) return ValType.V128;
            if (typeof(Delegate).IsAssignableFrom(type)) return ValType.Funcref;
            return ValType.Externref;
        }

        public static Type ConvertValTypeToSystemType(ValType valType)
        {
            return valType switch
            {
                ValType.I32 => typeof(int),
                ValType.I64 => typeof(long),
                ValType.F32 => typeof(float),
                ValType.F64 => typeof(double),
                ValType.V128 => typeof(byte[]), // Representing V128 as byte array
                ValType.Funcref => typeof(Delegate), // Generic function reference
                ValType.Externref => typeof(object),
                _ => throw new ArgumentException($"Unsupported ValType: {valType}")
            };
        }
    }
}