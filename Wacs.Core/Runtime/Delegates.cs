using System;
using System.Linq;
using System.Reflection;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public static class Delegates
    {
        public delegate object GenericFunc(params object[] args);

        public delegate object[] GenericFuncs(params object[] args);

        public delegate Value[] StackFunc(Value[] parameters);

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
                if (expectedReturnType != returnType)
                {
                    var implicitOp = returnType
                        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
                        .FirstOrDefault(m => m.Name == "op_Implicit" && m.ReturnType == expectedReturnType);
                    // Check if returnType has an implicit conversion operator to expectedReturnType
                    if (implicitOp is null)
                    {
                        throw new ArgumentException(
                            $"Return type mismatch. Expected return type is {expectedReturnType.Name}, but delegate returns {returnType.Name}.");
                    }
                }
            }
            else
            {
                throw new ArgumentException("WebAssembly functions should have at most one return value.");
            }
        }

        public static Delegate AnonymousFunctionFromType(FunctionType functionType, GenericFunc func)
        {
            var paramTypes = functionType.ParameterTypes.Types;
            var resultTypes = functionType.ResultType.Types;

            if (resultTypes.Length > 1)
            {
                throw new NotSupportedException("Multiple return values are not supported in C# delegates.");
            }

            return (paramTypes.Length, resultTypes.Length) switch
            {
                (0, 0) => new Action(() =>func()),
                (1, 0) => new Action<object>(i1=>func(i1)),
                (2, 0) => new Action<object,object>((i1,i2)=>func(i1,i2)),
                (3, 0) => new Action<object,object,object>((i1,i2,i3)=>func(i1,i2,i3)),
                (4, 0) => new Action<object, object, object, object>((i1,i2,i3,i4)=>func(i1,i2,i3,i4)),
                (5, 0) => new Action<object, object, object, object, object>((i1,i2,i3,i4,i5)=>func(i1,i2,i3,i4,i5)),
                (6, 0) => new Action<object, object, object, object, object, object>((i1,i2,i3,i4,i5,i6)=>func(i1,i2,i3,i4,i5,i6)),
                (7, 0) => new Action<object, object, object, object, object, object, object>((i1,i2,i3,i4,i5,i6,i7)=>func(i1,i2,i3,i4,i5,i6,i7)),
                (8, 0) => new Action<object, object, object, object, object, object, object, object>((i1,i2,i3,i4,i5,i6,i7,i8)=>func(i1,i2,i3,i4,i5,i6,i7,i8)),
                (9, 0) => new Action<object, object, object, object, object, object, object, object, object>((i1,i2,i3,i4,i5,i6,i7,i8,i9)=>func(i1,i2,i3,i4,i5,i6,i7,i8,i9)),
                
                (0, 1) => new Func<Value>(()=>new Value(func())),
                (1, 1) => new Func<object, Value>(i1=>new Value(func(i1))),
                (2, 1) => new Func<object, object, Value>((i1,i2)=> new Value(func(i1,i2))),
                (3, 1) => new Func<object, object, object, Value>((i1,i2,i3)=> new Value(func(i1,i2,i3))),
                (4, 1) => new Func<object, object, object, object, Value>((i1,i2,i3,i4)=> new Value(func(i1,i2,i3,i4))),
                (5, 1) => new Func<object, object, object, object, object, Value>((i1,i2,i3,i4,i5)=> new Value(func(i1,i2,i3,i4,i5))),
                (6, 1) => new Func<object, object, object, object, object, object, Value>((i1,i2,i3,i4,i5,i6)=> new Value(func(i1,i2,i3,i4,i5,i6))),
                (7, 1) => new Func<object, object, object, object, object, object, object, Value>((i1,i2,i3,i4,i5,i6,i7)=> new Value(func(i1,i2,i3,i4,i5,i6,i7))),
                (8, 1) => new Func<object, object, object, object, object, object, object, object, Value>((i1,i2,i3,i4,i5,i6,i7,i8)=> new Value(func(i1,i2,i3,i4,i5,i6,i7,i8))),
                (9, 1) => new Func<object, object, object, object, object, object, object, object, object, Value>((i1,i2,i3,i4,i5,i6,i7,i8,i9)=> new Value(func(i1,i2,i3,i4,i5,i6,i7,i8,i9))),
                
                _ => throw new NotSupportedException($"Cannot auto-bind function signature: ({string.Join(", ", paramTypes)}) -> ({string.Join(", ", resultTypes)})")
            };
        }

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