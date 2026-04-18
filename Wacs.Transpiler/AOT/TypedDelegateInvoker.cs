// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Caches a JIT-compiled wrapper per delegate type that invokes the
    /// delegate without going through <see cref="Delegate.DynamicInvoke"/>.
    ///
    /// <c>DynamicInvoke</c> is reflection-heavy per call (arg-type check,
    /// internal <see cref="MethodBase.Invoke(object, object[])"/>) and made
    /// call_indirect-heavy tests (fib/fac/runaway) run ~100x slower than a
    /// direct delegate invocation. The wrapper emitted here:
    ///
    ///   1. Casts the Delegate parameter to the concrete Func/Action type.
    ///   2. Unboxes each object? arg to the delegate's parameter type.
    ///   3. Calls Invoke directly (the JIT inlines this).
    ///   4. Boxes the return value (or returns null for Action).
    ///
    /// Keyed by delegate Type, so one wrapper serves every instance of
    /// <c>Func&lt;int,int&gt;</c>, etc.
    /// </summary>
    internal static class TypedDelegateInvoker
    {
        public delegate object? Invoker(Delegate del, object?[] args);

        private static readonly ConcurrentDictionary<Type, Invoker> _cache = new();

        public static Invoker GetOrBuild(Type delegateType)
            => _cache.GetOrAdd(delegateType, Build);

        private static Invoker Build(Type delegateType)
        {
            var invokeMethod = delegateType.GetMethod("Invoke")
                ?? throw new InvalidOperationException(
                    $"Delegate type {delegateType} has no Invoke method");
            var parameters = invokeMethod.GetParameters();

            var dyn = new DynamicMethod(
                $"Inv_{delegateType.Name}",
                typeof(object),
                new[] { typeof(Delegate), typeof(object?[]) },
                typeof(TypedDelegateInvoker).Module,
                skipVisibility: true);

            var il = dyn.GetILGenerator();

            // Cast the Delegate to the concrete type so we can call its typed Invoke.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, delegateType);

            // Unbox each arg to the declared parameter type.
            for (int i = 0; i < parameters.Length; i++)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);
                var pt = parameters[i].ParameterType;
                if (pt.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, pt);
                else
                    il.Emit(OpCodes.Castclass, pt);
            }

            il.Emit(OpCodes.Callvirt, invokeMethod);

            // Box the return value (or push null for Action).
            if (invokeMethod.ReturnType == typeof(void))
            {
                il.Emit(OpCodes.Ldnull);
            }
            else if (invokeMethod.ReturnType.IsValueType)
            {
                il.Emit(OpCodes.Box, invokeMethod.ReturnType);
            }

            il.Emit(OpCodes.Ret);

            return (Invoker)dyn.CreateDelegate(typeof(Invoker));
        }
    }
}
