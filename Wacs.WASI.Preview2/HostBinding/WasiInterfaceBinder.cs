// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Wacs.Core.Runtime;

namespace Wacs.WASI.Preview2.HostBinding
{
    /// <summary>
    /// Reflection-driven binder that registers each public
    /// instance method on a host implementation as a host
    /// function under <c>(namespace, kebab-case-method)</c>.
    /// Saves callers from writing one
    /// <c>runtime.BindHostFunction&lt;Func&lt;…&gt;&gt;</c> line
    /// per WASI function — the WASI cli world has dozens.
    ///
    /// <para>Phase 3 v0 limit: <b>primitive-only</b> signatures.
    /// Methods returning aggregates (string, list, record) or
    /// taking aggregate params are skipped silently; they need
    /// real canon-lower wiring, which rides incrementally as
    /// each WASI interface demands. Compatible primitives:
    /// bool, byte/sbyte, short/ushort, int/uint, long/ulong,
    /// float, double, void.</para>
    /// </summary>
    public static class WasiInterfaceBinder
    {
        /// <summary>
        /// Bind every primitive-signatured public method on
        /// <paramref name="impl"/> as a host function under
        /// <paramref name="namespaceName"/>.
        ///
        /// <para>The WASI namespace is used verbatim as the
        /// core-import module name — wit-component's
        /// instantiate-with passes the host-provided instance
        /// straight through under the same namespace, so the
        /// inner core wasm imports
        /// <c>(namespace, kebab-case-method)</c>
        /// directly.</para>
        /// </summary>
        public static void BindWasiInstance(this WasmRuntime runtime,
            string namespaceName, object impl)
        {
            if (impl == null) throw new ArgumentNullException(nameof(impl));
            var implType = impl.GetType();
            foreach (var m in implType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.DeclaringType == typeof(object)) continue;
                if (m.IsSpecialName) continue;   // skip property accessors
                if (!IsPrimitiveSignature(m)) continue;
                BindMethod(runtime, namespaceName, impl, m);
            }
        }

        /// <summary>True iff every parameter + the return type
        /// is a primitive that maps cleanly to a wasm flat slot
        /// (i32 / i64 / f32 / f64). Aggregates land in a
        /// follow-up.</summary>
        private static bool IsPrimitiveSignature(MethodInfo m)
        {
            if (!IsPrimitiveOrVoid(m.ReturnType)) return false;
            foreach (var p in m.GetParameters())
                if (!IsPrimitive(p.ParameterType)) return false;
            return true;
        }

        private static bool IsPrimitive(Type t)
        {
            return t == typeof(bool)
                || t == typeof(sbyte) || t == typeof(byte)
                || t == typeof(short) || t == typeof(ushort)
                || t == typeof(int) || t == typeof(uint)
                || t == typeof(long) || t == typeof(ulong)
                || t == typeof(float) || t == typeof(double);
        }

        private static bool IsPrimitiveOrVoid(Type t) =>
            t == typeof(void) || IsPrimitive(t);

        /// <summary>Construct a delegate matching the wasm host-
        /// function shape (<c>Func&lt;ExecContext, …, TRet&gt;</c>
        /// or <c>Action&lt;ExecContext, …&gt;</c>) that invokes
        /// the impl method, then hand it to
        /// <c>runtime.BindHostFunction</c>. Uses LINQ expression
        /// trees to build the closed-generic delegate type without
        /// reflection-emit (AOT-friendly).</summary>
        private static void BindMethod(WasmRuntime runtime,
            string namespaceName, object impl, MethodInfo m)
        {
            var importName = ToKebabCase(m.Name);

            var paramInfos = m.GetParameters();
            var paramTypes = new Type[paramInfos.Length + 1];
            paramTypes[0] = typeof(ExecContext);
            for (int i = 0; i < paramInfos.Length; i++)
                paramTypes[i + 1] = paramInfos[i].ParameterType;

            // Build the open-generic delegate type for
            // Func<ExecContext, …, TRet> / Action<ExecContext, …>.
            Type delegateType;
            if (m.ReturnType == typeof(void))
                delegateType = OpenActionType(paramTypes.Length)
                    .MakeGenericType(paramTypes);
            else
            {
                var allTypes = new Type[paramTypes.Length + 1];
                Array.Copy(paramTypes, allTypes, paramTypes.Length);
                allTypes[paramTypes.Length] = m.ReturnType;
                delegateType = OpenFuncType(allTypes.Length)
                    .MakeGenericType(allTypes);
            }

            // Expression tree: (ctx, p1, p2, …) => impl.Method(p1, p2, …)
            // The ExecContext parameter is accepted but ignored —
            // primitive WASI methods don't read host state.
            var lambdaParams = new ParameterExpression[paramTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            for (int i = 0; i < paramInfos.Length; i++)
                lambdaParams[i + 1] = Expression.Parameter(
                    paramInfos[i].ParameterType, paramInfos[i].Name);

            var implExpr = Expression.Constant(impl);
            var argExprs = new Expression[paramInfos.Length];
            for (int i = 0; i < paramInfos.Length; i++)
                argExprs[i] = lambdaParams[i + 1];
            var call = Expression.Call(implExpr, m, argExprs);
            var lambda = Expression.Lambda(delegateType, call, lambdaParams);
            var compiled = lambda.Compile();

            // Reflection-call BindHostFunction<T> with the closed
            // generic. The runtime resolves imports by exact
            // (module, name) match against the inner core wasm.
            var bindOpen = typeof(WasmRuntime).GetMethods()
                .First(mi => mi.Name == nameof(WasmRuntime.BindHostFunction)
                    && mi.IsGenericMethod
                    && mi.GetParameters().Length == 2);
            var bindClosed = bindOpen.MakeGenericMethod(delegateType);
            bindClosed.Invoke(runtime,
                new object[] { (namespaceName, importName), compiled });
        }

        /// <summary>PascalCase → kebab-case, the convention WIT
        /// uses for function / interface names. <c>GetRandomU64</c>
        /// → <c>get-random-u64</c>.</summary>
        private static string ToKebabCase(string pascal)
        {
            if (string.IsNullOrEmpty(pascal)) return pascal;
            var sb = new StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                var c = pascal[i];
                if (i > 0 && char.IsUpper(c))
                {
                    // Insert dash before sequences like "U64"
                    // (digit follows upper) but only at the
                    // first upper of the run — "GetRandomU64"
                    // → "get-random-u64", not "get-random-u-6-4".
                    if (char.IsLower(pascal[i - 1])
                        || (i + 1 < pascal.Length
                            && char.IsLower(pascal[i + 1])))
                        sb.Append('-');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static Type OpenFuncType(int count) => count switch
        {
            1 => typeof(Func<>),
            2 => typeof(Func<,>),
            3 => typeof(Func<,,>),
            4 => typeof(Func<,,,>),
            5 => typeof(Func<,,,,>),
            6 => typeof(Func<,,,,,>),
            7 => typeof(Func<,,,,,,>),
            8 => typeof(Func<,,,,,,,>),
            _ => throw new NotSupportedException(
                "Func arities above 8 are a follow-up — none of "
                + "the v0.2.3 WASI interfaces hit this."),
        };

        private static Type OpenActionType(int count) => count switch
        {
            1 => typeof(Action<>),
            2 => typeof(Action<,>),
            3 => typeof(Action<,,>),
            4 => typeof(Action<,,,>),
            5 => typeof(Action<,,,,>),
            6 => typeof(Action<,,,,,>),
            7 => typeof(Action<,,,,,,>),
            8 => typeof(Action<,,,,,,,>),
            _ => throw new NotSupportedException(
                "Action arities above 8 are a follow-up."),
        };
    }
}
