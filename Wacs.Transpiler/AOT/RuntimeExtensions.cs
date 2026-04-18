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
using System.Linq;
using System.Reflection;
using Wacs.Core.Runtime;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Wraps a TranspilationResult as an IBindable, following the same pattern
    /// as Wacs.WASIp1 modules (Env, FileSystem, Clock, etc.).
    ///
    /// Each exported transpiled function is bound as a host function under the
    /// given module name, using the standard BindHostFunction path.
    /// The ThinContext is captured in wrapper delegates so the runtime
    /// sees normal .NET delegates with WASM-visible parameter signatures.
    /// </summary>
    public class TranspiledModule : IBindable
    {
        private readonly string _moduleName;
        private readonly TranspilationResult _result;
        private readonly ThinContext _ctx;

        public TranspiledModule(
            string moduleName,
            TranspilationResult result,
            ThinContext ctx)
        {
            _moduleName = moduleName;
            _result = result;
            _ctx = ctx;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            foreach (var entry in _result.Manifest.Functions)
            {
                if (string.IsNullOrEmpty(entry.ExportName))
                    continue;

                var method = _result.Methods[entry.Index];
                var wrapper = CreateContextWrapper(method, _ctx);

                if (wrapper != null)
                {
                    runtime.BindHostFunction((_moduleName, entry.ExportName), wrapper);
                }
            }
        }

        /// <summary>
        /// Creates a delegate that wraps a transpiled static method, capturing the
        /// ThinContext so the resulting delegate has only the WASM-visible parameters.
        ///
        /// Input:  static int Function0(ThinContext ctx, int p0, long p1)
        /// Output: Func&lt;int, long, int&gt; wrapper = (p0, p1) => Function0(capturedCtx, p0, p1)
        /// </summary>
        internal static Delegate? CreateContextWrapper(MethodInfo method, ThinContext ctx)
        {
            var allParams = method.GetParameters();
            if (allParams.Length == 0 || allParams[0].ParameterType != typeof(ThinContext))
                return null;

            var wasmParamTypes = allParams.Skip(1).Select(p => p.ParameterType).ToArray();
            var returnType = method.ReturnType;

            Type delegateType = returnType == typeof(void)
                ? GetActionType(wasmParamTypes)
                : GetFuncType(wasmParamTypes, returnType);

            var wrapper = new MethodWrapper(method, ctx);
            return wrapper.CreateDelegate(delegateType, wasmParamTypes, returnType);
        }

        private static Type GetActionType(Type[] paramTypes)
        {
            return paramTypes.Length switch
            {
                0 => typeof(Action),
                1 => typeof(Action<>).MakeGenericType(paramTypes),
                2 => typeof(Action<,>).MakeGenericType(paramTypes),
                3 => typeof(Action<,,>).MakeGenericType(paramTypes),
                4 => typeof(Action<,,,>).MakeGenericType(paramTypes),
                _ => throw new TranspilerException(
                    $"Cannot create Action delegate with {paramTypes.Length} parameters")
            };
        }

        private static Type GetFuncType(Type[] paramTypes, Type returnType)
        {
            var allTypes = paramTypes.Append(returnType).ToArray();
            return allTypes.Length switch
            {
                1 => typeof(Func<>).MakeGenericType(allTypes),
                2 => typeof(Func<,>).MakeGenericType(allTypes),
                3 => typeof(Func<,,>).MakeGenericType(allTypes),
                4 => typeof(Func<,,,>).MakeGenericType(allTypes),
                5 => typeof(Func<,,,,>).MakeGenericType(allTypes),
                _ => throw new TranspilerException(
                    $"Cannot create Func delegate with {paramTypes.Length} parameters")
            };
        }
    }

    /// <summary>
    /// Captures a ThinContext and a MethodInfo, providing typed trampoline
    /// methods that prepend the context to arguments. The trampoline is bound as
    /// a delegate, avoiding per-call argument array allocation.
    /// </summary>
    internal class MethodWrapper
    {
        private readonly MethodInfo _method;
        private readonly ThinContext _ctx;

        public MethodWrapper(MethodInfo method, ThinContext ctx)
        {
            _method = method;
            _ctx = ctx;
        }

        public Delegate CreateDelegate(Type delegateType, Type[] wasmParamTypes, Type returnType)
        {
            string name = GetTrampolineName(wasmParamTypes.Length, returnType != typeof(void));
            var trampolineMethod = typeof(MethodWrapper).GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (trampolineMethod != null)
            {
                var genericArgs = returnType != typeof(void)
                    ? wasmParamTypes.Append(returnType).ToArray()
                    : wasmParamTypes;

                if (genericArgs.Length > 0)
                    trampolineMethod = trampolineMethod.MakeGenericMethod(genericArgs);

                return Delegate.CreateDelegate(delegateType, this, trampolineMethod);
            }

            throw new TranspilerException(
                $"Functions with {wasmParamTypes.Length} parameters require emitted IL trampolines (not yet implemented)");
        }

        private static string GetTrampolineName(int paramCount, bool hasReturn) =>
            hasReturn ? $"InvokeFunc{paramCount}" : $"InvokeAction{paramCount}";

        // Trampolines for Func<..., TResult> (functions with return values)
        private TResult InvokeFunc0<TResult>()
            => (TResult)_method.Invoke(null, new object?[] { _ctx })!;

        private TResult InvokeFunc1<T0, TResult>(T0 a0)
            => (TResult)_method.Invoke(null, new object?[] { _ctx, a0 })!;

        private TResult InvokeFunc2<T0, T1, TResult>(T0 a0, T1 a1)
            => (TResult)_method.Invoke(null, new object?[] { _ctx, a0, a1 })!;

        private TResult InvokeFunc3<T0, T1, T2, TResult>(T0 a0, T1 a1, T2 a2)
            => (TResult)_method.Invoke(null, new object?[] { _ctx, a0, a1, a2 })!;

        private TResult InvokeFunc4<T0, T1, T2, T3, TResult>(T0 a0, T1 a1, T2 a2, T3 a3)
            => (TResult)_method.Invoke(null, new object?[] { _ctx, a0, a1, a2, a3 })!;

        // Trampolines for Action<...> (void functions)
        private void InvokeAction0()
            => _method.Invoke(null, new object?[] { _ctx });

        private void InvokeAction1<T0>(T0 a0)
            => _method.Invoke(null, new object?[] { _ctx, a0 });

        private void InvokeAction2<T0, T1>(T0 a0, T1 a1)
            => _method.Invoke(null, new object?[] { _ctx, a0, a1 });

        private void InvokeAction3<T0, T1, T2>(T0 a0, T1 a1, T2 a2)
            => _method.Invoke(null, new object?[] { _ctx, a0, a1, a2 });

        private void InvokeAction4<T0, T1, T2, T3>(T0 a0, T1 a1, T2 a2, T3 a3)
            => _method.Invoke(null, new object?[] { _ctx, a0, a1, a2, a3 });
    }
}
