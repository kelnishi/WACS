// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Expression = System.Linq.Expressions.Expression;

namespace Wacs.Core.Runtime
{
    public static class Delegates
    {
        public delegate object GenericFunc(params object[] args);

        public delegate Value[] GenericFuncs(params object[] args);

        public delegate Task<Value[]> GenericFuncsAsync(params Value[] args);

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
            if (returnType.BaseType == typeof(Task))
            {
                if (returnType.IsGenericType)
                {
                    returnType = returnType.GenericTypeArguments[0];
                }
            }
            

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
                if (pType == expectedType)
                    continue;
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
                    if (implicitOp is null && returnType != typeof(object))
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
                (0, 0) => new Action<object[]>(p => func()),
                (1, 0) => new Action<object[]>(p => func(p[0])),
                (2, 0) => new Action<object[]>(p => func(p[0], p[1])),
                (3, 0) => new Action<object[]>(p => func(p[0], p[1], p[2])),
                (4, 0) => new Action<object[]>(p => func(p[0], p[1], p[2], p[3])),
                (5, 0) => new Action<object[]>(p => func(p[0], p[1], p[2], p[3], p[4])),
                (6, 0) => new Action<object[]>(p => func(p[0], p[1], p[2], p[3], p[4], p[5])),
                (7, 0) => new Action<object[]>(p => func(p[0], p[1], p[2], p[3], p[4], p[5], p[6])),
                (8, 0) => new Action<object[]>(p => func(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7])),
                (9, 0) => new Action<object[]>(p => func(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8])),
                
                (0, 1) => new Func<object[], Value>(p => new Value(func())),
                (1, 1) => new Func<object[], Value>(p => new Value(func(p[0]))),
                (2, 1) => new Func<object[], Value>(p => new Value(func(p[0], p[1]))),
                (3, 1) => new Func<object[], Value>(p => new Value(func(p[0], p[1], p[2]))),
                (4, 1) => new Func<object[], Value>(p => new Value(func(p[0], p[1], p[2], p[3]))),
                (5, 1) => new Func<object[], Value>(p => new Value(func(p[0], p[1], p[2], p[3], p[4]))),
                (6, 1) => new Func<object[], Value>(p => new Value(func(p[0], p[1], p[2], p[3], p[4], p[5]))),
                (7, 1) => new Func<object[], Value>(p => new Value(func(p[0], p[1], p[2], p[3], p[4], p[5], p[6]))),
                (8, 1) => new Func<object[], Value>(p => new Value(func(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7]))),
                (9, 1) => new Func<object[], Value>(p => new Value(func(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8]))),
                
                _ => throw new NotSupportedException($"Cannot auto-bind function signature: ({string.Join(", ", paramTypes)}) -> ({string.Join(", ", resultTypes)})")
            };
        }

        public static TDelegate CreateTypedDelegate<TDelegate>(Delegate genericDelegate) where TDelegate : Delegate
        {
            // Get the 'Invoke' method of the desired delegate type
            var delegateInvokeMethod = typeof(TDelegate).GetMethod("Invoke");
            if (delegateInvokeMethod == null)
                throw new ArgumentException($"Delegate type {typeof(TDelegate)} did not have Invoke() method");
            
            var delegateParameters = delegateInvokeMethod.GetParameters();
            
            // Create parameter expressions matching the desired delegate's parameters
            var parameterExpressions = delegateParameters
                .Select(p => Expression.Parameter(p.ParameterType, p.Name))
                .ToArray();

            // Convert the parameters to 'object' type for the generic delegate
            var convertedParameters = parameterExpressions
                .Select(p => Expression.Convert(p, typeof(object)))
                .ToArray();

            // Create an array of 'object' to pass to the generic delegate
            var parametersArray = Expression.NewArrayInit(typeof(object), convertedParameters);

            // Create the method call expression to invoke the generic delegate
            var callExpression = Expression.Invoke(
                Expression.Constant(genericDelegate),
                parametersArray
            );

            // Handle the return type if necessary
            Expression body;
            if (delegateInvokeMethod.ReturnType == typeof(void))
            {
                body = callExpression;
            }
            else
            {
                body = Expression.Convert(callExpression, delegateInvokeMethod.ReturnType);
            }

            // Create the lambda expression
            var lambda = Expression.Lambda<TDelegate>(body, parameterExpressions);

            // Compile and return the delegate
            return lambda.Compile();
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
            if (typeof(Delegate).IsAssignableFrom(type)) return ValType.FuncRef;
            return ValType.ExternRef;
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
                ValType.FuncRef => typeof(Delegate), // Generic function reference
                ValType.ExternRef => typeof(object),
                _ => throw new ArgumentException($"Unsupported ValType: {valType}")
            };
        }
    }
}