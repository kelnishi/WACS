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
using System.Reflection;
using System.Threading.Tasks;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Runtime
{
    public static class Delegates
    {
        public delegate object GenericFunc(params object[] args);

        public delegate Value[] GenericFuncs(params object[] args);

        public delegate Task<Value[]> GenericFuncsAsync(params Value[] args);

        public delegate Value[] StackFunc(Value[] parameters);
        
        private static readonly Type ClassType = typeof(Delegates);
        private static readonly MethodInfo M0X0 = ClassType.GetMethod(nameof(CreateInvoker0X0), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M1X0 = ClassType.GetMethod(nameof(CreateInvoker1X0), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M2X0 = ClassType.GetMethod(nameof(CreateInvoker2X0), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M3X0 = ClassType.GetMethod(nameof(CreateInvoker3X0), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M4X0 = ClassType.GetMethod(nameof(CreateInvoker4X0), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M5X0 = ClassType.GetMethod(nameof(CreateInvoker5X0), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M6X0 = ClassType.GetMethod(nameof(CreateInvoker6X0), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M7X0 = ClassType.GetMethod(nameof(CreateInvoker7X0), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M8X0 = ClassType.GetMethod(nameof(CreateInvoker8X0), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M9X0 = ClassType.GetMethod(nameof(CreateInvoker9X0), BindingFlags.Public | BindingFlags.Static)!;
        
        private static readonly MethodInfo M0X1 = ClassType.GetMethod(nameof(CreateInvoker0X1), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M1X1 = ClassType.GetMethod(nameof(CreateInvoker1X1), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M2X1 = ClassType.GetMethod(nameof(CreateInvoker2X1), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M3X1 = ClassType.GetMethod(nameof(CreateInvoker3X1), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M4X1 = ClassType.GetMethod(nameof(CreateInvoker4X1), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M5X1 = ClassType.GetMethod(nameof(CreateInvoker5X1), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M6X1 = ClassType.GetMethod(nameof(CreateInvoker6X1), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M7X1 = ClassType.GetMethod(nameof(CreateInvoker7X1), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M8X1 = ClassType.GetMethod(nameof(CreateInvoker8X1), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo M9X1 = ClassType.GetMethod(nameof(CreateInvoker9X1), BindingFlags.Public | BindingFlags.Static)!;
        

        public static Action CreateInvoker0X0(Action func) => func;
        public static Action<T> CreateInvoker1X0<T>(Action<object> func) => arg => func(arg);
        public static Action<T1, T2> CreateInvoker2X0<T1, T2>(Action<object, object> func) => (arg1, arg2) => func(arg1, arg2);
        public static Action<T1, T2, T3> CreateInvoker3X0<T1, T2, T3>(Action<object, object, object> func) => (arg1, arg2, arg3) => func(arg1, arg2, arg3);
        public static Action<T1, T2, T3, T4> CreateInvoker4X0<T1, T2, T3, T4>(Action<object, object, object, object> func) => (arg1, arg2, arg3, arg4) => func(arg1, arg2, arg3, arg4);
        public static Action<T1, T2, T3, T4, T5> CreateInvoker5X0<T1, T2, T3, T4, T5>(Action<object, object, object, object, object> func) => (arg1, arg2, arg3, arg4, arg5) => func(arg1, arg2, arg3, arg4, arg5);
        public static Action<T1, T2, T3, T4, T5, T6> CreateInvoker6X0<T1, T2, T3, T4, T5, T6>(Action<object, object, object, object, object, object> func) => (arg1, arg2, arg3, arg4, arg5, arg6) => func(arg1, arg2, arg3, arg4, arg5, arg6);
        public static Action<T1, T2, T3, T4, T5, T6, T7> CreateInvoker7X0<T1, T2, T3, T4, T5, T6, T7>(Action<object, object, object, object, object, object, object> func) => (arg1, arg2, arg3, arg4, arg5, arg6, arg7) => func(arg1, arg2, arg3, arg4, arg5, arg6, arg7);
        public static Action<T1, T2, T3, T4, T5, T6, T7, T8> CreateInvoker8X0<T1, T2, T3, T4, T5, T6, T7, T8>(Action<object, object, object, object, object, object, object, object> func) => (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8) => func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
        public static Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> CreateInvoker9X0<T1, T2, T3, T4, T5, T6, T7, T8, T9>(Action<object, object, object, object, object, object, object, object, object> func) => (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9) => func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
        
        
        public static Func<TResult> CreateInvoker0X1<TResult>(Func<TResult> func) => () => func();
        public static Func<T, TResult> CreateInvoker1X1<T, TResult>(Func<object, TResult> func) => arg => func(arg);
        public static Func<T1, T2, TResult> CreateInvoker2X1<T1, T2, TResult>(Func<object, object, TResult> func) => (arg1, arg2) => func(arg1, arg2);
        public static Func<T1, T2, T3, TResult> CreateInvoker3X1<T1, T2, T3, TResult>(Func<object,object,object, TResult> func) => (arg1, arg2, arg3) => func(arg1, arg2, arg3 );
        public static Func<T1, T2, T3, T4, TResult> CreateInvoker4X1<T1, T2, T3, T4, TResult>(Func<object, object, object, object, TResult> func) => (arg1, arg2, arg3, arg4) => func(arg1, arg2, arg3, arg4);
        public static Func<T1, T2, T3, T4, T5, TResult> CreateInvoker5X1<T1, T2, T3, T4, T5, TResult>(Func<object, object, object, object, object, TResult> func) => (arg1, arg2, arg3, arg4, arg5) => func(arg1, arg2, arg3, arg4, arg5);
        public static Func<T1, T2, T3, T4, T5, T6, TResult> CreateInvoker6X1<T1, T2, T3, T4, T5, T6, TResult>(Func<object, object, object, object, object, object, TResult> func) => (arg1, arg2, arg3, arg4, arg5, arg6) => func(arg1, arg2, arg3, arg4, arg5, arg6);
        public static Func<T1, T2, T3, T4, T5, T6, T7, TResult> CreateInvoker7X1<T1, T2, T3, T4, T5, T6, T7, TResult>(Func<object, object, object, object, object, object, object, TResult> func) => (arg1, arg2, arg3, arg4, arg5, arg6, arg7) => func(arg1, arg2, arg3, arg4, arg5, arg6, arg7);
        public static Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> CreateInvoker8X1<T1, T2, T3, T4, T5, T6, T7, T8, TResult>(Func<object, object, object, object, object, object, object, object, TResult> func) => (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8) => func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
        public static Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult> CreateInvoker9X1<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(Func<object, object, object, object, object, object, object, object, object, TResult> func) => (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9) => func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
        
        
        public static MethodInfo WrapGenericMethod(MethodInfo invokeMethod)
        {
            ParameterInfo[] parameters = invokeMethod.GetParameters();
            bool isAction = invokeMethod.ReturnType == typeof(void);

            int p = parameters.Length;
            int r = isAction ? 0 : 1;
            
            MethodInfo mi = (p, r) switch
            {
                (0, 0) => M0X0,
                (1, 0) => M1X0,
                (2, 0) => M2X0,
                (3, 0) => M3X0,
                (4, 0) => M4X0,
                (5, 0) => M5X0,
                (6, 0) => M6X0,
                (7, 0) => M7X0,
                (8, 0) => M8X0,
                (9, 0) => M9X0,
                
                (0, 1) => M0X1,
                (1, 1) => M1X1,
                (2, 1) => M2X1,
                (3, 1) => M3X1,
                (4, 1) => M4X1,
                (5, 1) => M5X1,
                (6, 1) => M6X1,
                (7, 1) => M7X1,
                (8, 1) => M8X1,
                (9, 1) => M9X1,
                _ => throw new ArgumentException($"No DelegateWrapper for {p}x{r}")
            };

            if ((p, r) == (0, 0))
                return mi;
            
            MethodInfo mgm = typeof(MethodInfo).GetMethod("MakeGenericMethod");
            var mgmargs = parameters.Select(p => p.ParameterType).ToList();
            if (!isAction)
                mgmargs.Add(invokeMethod.ReturnType);
            
            MethodInfo? genMi = (MethodInfo)mgm.Invoke(mi, new object[]{mgmargs.ToArray()});
            if (genMi is null)
                throw new ArgumentException($"Could not wrap generic method {invokeMethod.Name}");
            
            return genMi;
        }
        
        public static Delegate AnonymousFunctionFromType(FunctionType functionType, GenericFuncs func)
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
                (2, 0) => new Action<object, object>((i1,i2)=>func(i1,i2)),
                (3, 0) => new Action<object, object, object>((i1,i2,i3)=>func(i1,i2,i3)),
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