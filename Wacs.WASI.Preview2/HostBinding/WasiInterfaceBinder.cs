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
                if (IsPrimitiveSignature(m))
                {
                    BindMethod(runtime, namespaceName, impl, m);
                    continue;
                }
                if (IsListU8ReturnPrimitiveParams(m))
                {
                    BindByteArrayReturnMethod(
                        runtime, namespaceName, impl, m);
                    continue;
                }
                // Other aggregate shapes (string returns, list<T>
                // for non-byte T, records, resources) are
                // follow-ups — silently skipped here.
            }
        }

        /// <summary>True iff the method returns <c>byte[]</c>
        /// (the C# representation of <c>list&lt;u8&gt;</c>) and
        /// every parameter is a primitive flat slot. Drives the
        /// canon-lower wrapper for the common WASI shape of
        /// "compute some bytes and hand them back" — random,
        /// some clocks, etc.</summary>
        private static bool IsListU8ReturnPrimitiveParams(MethodInfo m)
        {
            if (m.ReturnType != typeof(byte[])) return false;
            foreach (var p in m.GetParameters())
                if (!IsPrimitive(p.ParameterType)) return false;
            return true;
        }

        /// <summary>Build a canon-lower wrapper for a host method
        /// returning <c>byte[]</c>. The canon-lowered core import
        /// has the host's primitive params followed by a
        /// trailing retAreaPtr (i32), no result — per the
        /// canonical-ABI <c>MAX_FLAT_RESULTS = 1</c> rule that
        /// forces aggregate returns through a caller-supplied
        /// memory pointer. The wrapper:
        /// <list type="number">
        /// <item>Calls the host method with the primitive args.</item>
        /// <item>Allocates a guest buffer via the runtime's
        /// <c>cabi_realloc</c> export (resolved lazily on first
        /// call since the inner module isn't instantiated when
        /// the binder runs).</item>
        /// <item>Copies the bytes into guest memory.</item>
        /// <item>Writes (dataPtr, count) at retAreaPtr — the
        /// 8-byte (ptr, len) pair the canon lift expects to
        /// read.</item>
        /// </list></summary>
        private static void BindByteArrayReturnMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m)
        {
            var importName = ToKebabCase(m.Name);

            var paramInfos = m.GetParameters();
            // Wrapper signature: ExecContext, [wire types for host
            // params...], retAreaPtr. Wire types map u64→long,
            // u32→int, etc., so the wasm runtime's primitive
            // boxing (always signed) lines up with the
            // delegate's parameter types. Inside the body we
            // cast back to the host method's declared type before
            // invoke.
            var paramTypes = new Type[paramInfos.Length + 2];
            paramTypes[0] = typeof(ExecContext);
            for (int i = 0; i < paramInfos.Length; i++)
                paramTypes[i + 1] = ToWireType(paramInfos[i].ParameterType);
            paramTypes[paramInfos.Length + 1] = typeof(int);

            var delegateType = OpenActionType(paramTypes.Length)
                .MakeGenericType(paramTypes);

            // Lazy holder for cabi_realloc invoker — resolved on
            // first call after the inner core has instantiated
            // and exported it.
            Wacs.Core.Runtime.Delegates.GenericFuncs? cabiRealloc = null;

            void Body(ExecContext ctx, object?[] hostArgs, int retAreaPtr)
            {
                var bytes = (byte[])m.Invoke(impl, hostArgs)!;
                if (cabiRealloc == null)
                {
                    if (!runtime.TryGetExportedFunction(
                            "cabi_realloc", out var reallocAddr))
                        throw new InvalidOperationException(
                            "Component does not export "
                            + "cabi_realloc — required for "
                            + "aggregate-returning host imports.");
                    cabiRealloc = runtime.CreateInvoker(
                        reallocAddr, new InvokerOptions());
                }
                var dataPtr = cabiRealloc(
                    0, 0, 1, bytes.Length)[0].Data.Int32;
                var memory = ctx.DefaultMemory.Data;
                if (bytes.Length > 0)
                    System.Array.Copy(bytes, 0, memory, dataPtr, bytes.Length);
                memory[retAreaPtr]     = (byte)(dataPtr & 0xFF);
                memory[retAreaPtr + 1] = (byte)((dataPtr >> 8) & 0xFF);
                memory[retAreaPtr + 2] = (byte)((dataPtr >> 16) & 0xFF);
                memory[retAreaPtr + 3] = (byte)((dataPtr >> 24) & 0xFF);
                memory[retAreaPtr + 4] = (byte)(bytes.Length & 0xFF);
                memory[retAreaPtr + 5] = (byte)((bytes.Length >> 8) & 0xFF);
                memory[retAreaPtr + 6] = (byte)((bytes.Length >> 16) & 0xFF);
                memory[retAreaPtr + 7] = (byte)((bytes.Length >> 24) & 0xFF);
            }

            // Build typed delegate via expression trees so
            // BindHostFunction sees the right closed signature.
            // Lambda param types must match the delegate's
            // parameter types — wire-typed (long for ulong, etc.)
            // so the wasm runtime's signed-primitive boxing
            // lines up.
            var lambdaParams = new ParameterExpression[paramTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            for (int i = 0; i < paramInfos.Length; i++)
                lambdaParams[i + 1] = Expression.Parameter(
                    paramTypes[i + 1], paramInfos[i].Name);
            lambdaParams[paramInfos.Length + 1] =
                Expression.Parameter(typeof(int), "retAreaPtr");

            // For each host param, convert the wire-typed
            // lambda param back to the declared host type before
            // boxing for reflection invoke. Identity for matching
            // pairs; otherwise this is e.g. (ulong)(long)wireArg.
            var argArr = Expression.NewArrayInit(typeof(object),
                paramInfos.Select((p, i) =>
                {
                    Expression e = lambdaParams[i + 1];
                    if (p.ParameterType != e.Type)
                        e = Expression.Convert(e, p.ParameterType);
                    return (Expression)Expression.Convert(e, typeof(object));
                }).ToArray());
            var bodyTarget = Expression.Constant(
                (Action<ExecContext, object?[], int>)Body);
            var call = Expression.Invoke(bodyTarget,
                lambdaParams[0], argArr,
                lambdaParams[paramInfos.Length + 1]);
            var lambda = Expression.Lambda(delegateType, call, lambdaParams);
            var compiled = lambda.Compile();

            var bindOpen = typeof(WasmRuntime).GetMethods()
                .First(mi => mi.Name == nameof(WasmRuntime.BindHostFunction)
                    && mi.IsGenericMethod
                    && mi.GetParameters().Length == 2);
            var bindClosed = bindOpen.MakeGenericMethod(delegateType);
            bindClosed.Invoke(runtime,
                new object[] { (namespaceName, importName), compiled });
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

        /// <summary>Map a host primitive type to the underlying
        /// signed-integer wire type the wasm runtime uses on its
        /// operand stack. The runtime always boxes primitives as
        /// signed (i32 → int, i64 → long); unsigned host params
        /// (uint, ulong) read as int / long here, then we cast
        /// back to the declared type inside the wrapper body.
        /// </summary>
        private static Type ToWireType(Type t)
        {
            if (t == typeof(uint)) return typeof(int);
            if (t == typeof(ulong)) return typeof(long);
            if (t == typeof(byte)) return typeof(int);
            if (t == typeof(sbyte)) return typeof(int);
            if (t == typeof(ushort)) return typeof(int);
            if (t == typeof(short)) return typeof(int);
            if (t == typeof(bool)) return typeof(int);
            return t;
        }

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
