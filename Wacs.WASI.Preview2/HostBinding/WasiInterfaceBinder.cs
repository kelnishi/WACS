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
            string namespaceName, object impl,
            ResourceContext? resources = null)
        {
            if (impl == null) throw new ArgumentNullException(nameof(impl));
            resources ??= new ResourceContext();
            var implType = impl.GetType();
            foreach (var m in implType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.DeclaringType == typeof(object)) continue;
                if (m.IsSpecialName) continue;   // skip property accessors
                if (m.GetCustomAttribute<WasiErrorResultAttribute>() != null
                    && m.ReturnType.GetCustomAttribute<WasiResourceAttribute>() != null)
                {
                    BindErrorResultResourceReturnMethod(
                        runtime, namespaceName, impl, m, resources);
                    continue;
                }
                if (IsResourceReturnPrimitiveParams(m))
                {
                    BindResourceReturnMethod(
                        runtime, namespaceName, impl, m, resources);
                    continue;
                }
                if (IsOptionResourceReturnPrimitiveParams(m))
                {
                    BindOptionResourceReturnMethod(
                        runtime, namespaceName, impl, m, resources);
                    continue;
                }
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
                // option-string check FIRST since the predicate
                // requires an explicit attribute; without it the
                // plain string-return path would catch the same
                // method first.
                if (IsOptionStringReturnPrimitiveParams(m))
                {
                    BindOptionStringReturnMethod(
                        runtime, namespaceName, impl, m);
                    continue;
                }
                if (IsOptionEnumReturnBorrowParam(m))
                {
                    BindOptionEnumReturnBorrowParamMethod(
                        runtime, namespaceName, impl, m, resources);
                    continue;
                }
                if (IsStringReturnPrimitiveParams(m))
                {
                    BindStringReturnMethod(
                        runtime, namespaceName, impl, m);
                    continue;
                }
                if (IsStringArrayReturnPrimitiveParams(m))
                {
                    BindStringArrayReturnMethod(
                        runtime, namespaceName, impl, m);
                    continue;
                }
                if (IsStringPairArrayReturnPrimitiveParams(m))
                {
                    BindStringPairArrayReturnMethod(
                        runtime, namespaceName, impl, m);
                    continue;
                }
                if (IsResourceStringPairArrayReturnPrimitiveParams(m))
                {
                    BindResourceStringPairArrayReturnMethod(
                        runtime, namespaceName, impl, m, resources);
                    continue;
                }
                if (IsRecordOfPrimitivesReturnPrimitiveParams(m))
                {
                    BindRecordReturnMethod(
                        runtime, namespaceName, impl, m);
                    continue;
                }
                if (IsResourceArrayParamU32ArrayReturn(m))
                {
                    BindResourceArrayParamU32ArrayReturnMethod(
                        runtime, namespaceName, impl, m, resources);
                    continue;
                }
                // Other aggregate shapes (list<tuple<string,string>>,
                // resources) are follow-ups — silently skipped here.
            }
        }

        private static bool IsStringReturnPrimitiveParams(MethodInfo m)
        {
            if (m.ReturnType != typeof(string)) return false;
            foreach (var p in m.GetParameters())
                if (!IsPrimitive(p.ParameterType)) return false;
            return true;
        }

        /// <summary>True iff the method shape matches
        /// <c>EnumType? M(TResource)</c> where TResource has
        /// <see cref="WasiResourceAttribute"/> and the return
        /// is a nullable enum tagged
        /// <see cref="WasiOptionalReturnAttribute"/>.
        /// Drives <c>wasi:filesystem/types.filesystem-error-code</c>
        /// — borrow<error> in, option<error-code> out.</summary>
        private static bool IsOptionEnumReturnBorrowParam(MethodInfo m)
        {
            if (m.GetCustomAttribute<WasiOptionalReturnAttribute>() == null)
                return false;
            var rt = m.ReturnType;
            if (!rt.IsGenericType
                || rt.GetGenericTypeDefinition() != typeof(Nullable<>))
                return false;
            if (!rt.GetGenericArguments()[0].IsEnum) return false;
            var ps = m.GetParameters();
            if (ps.Length != 1) return false;
            return ps[0].ParameterType.GetCustomAttribute<
                WasiResourceAttribute>() != null;
        }

        /// <summary>Canon-lower wrapper for
        /// <c>option&lt;enum&gt; M(borrow&lt;TResource&gt;)</c> —
        /// the shape used by
        /// <c>wasi:filesystem/types.filesystem-error-code</c>.
        /// Wire form: 2-byte retArea (option disc + 1-byte
        /// payload), 1 borrow handle param.</summary>
        private static void BindOptionEnumReturnBorrowParamMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m, ResourceContext resources)
        {
            var importName = m.GetCustomAttribute<WasiMethodNameAttribute>()?
                .WitName ?? ToKebabCase(m.Name);
            var paramType = m.GetParameters()[0].ParameterType;
            var paramTable = resources.TableFor(paramType);

            // Wire: ExecContext, handle (i32), retAreaPtr (i32) → void.
            void Body(ExecContext ctx, int handle, int retArea)
            {
                var inst = paramTable.Get(handle);
                var ret = m.Invoke(impl, new object?[] { inst });
                var memory = ctx.DefaultMemory.Data;
                if (ret == null)
                {
                    memory[retArea] = 0;       // None
                    memory[retArea + 1] = 0;
                }
                else
                {
                    memory[retArea] = 1;       // Some
                    memory[retArea + 1] = Convert.ToByte(ret);
                }
            }

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (namespaceName, importName), Body);
        }

        /// <summary>True iff the method shape matches
        /// <c>uint[] M(TResource[])</c> where TResource is a
        /// <see cref="WasiResourceAttribute"/>-marked type.
        /// Drives the canon-lower wrapper for
        /// <c>wasi:io/poll.poll</c> — list&lt;borrow&lt;pollable&gt;&gt;
        /// in, list&lt;u32&gt; out.</summary>
        private static bool IsResourceArrayParamU32ArrayReturn(MethodInfo m)
        {
            if (m.ReturnType != typeof(uint[])) return false;
            var ps = m.GetParameters();
            if (ps.Length != 1) return false;
            if (!ps[0].ParameterType.IsArray) return false;
            var elem = ps[0].ParameterType.GetElementType();
            if (elem == null) return false;
            return elem.GetCustomAttribute<WasiResourceAttribute>() != null;
        }

        private static bool IsStringArrayReturnPrimitiveParams(MethodInfo m)
        {
            if (m.ReturnType != typeof(string[])) return false;
            foreach (var p in m.GetParameters())
                if (!IsPrimitive(p.ParameterType)) return false;
            return true;
        }

        /// <summary>True iff the method returns a class tagged
        /// <see cref="WasiResourceAttribute"/> and every param
        /// is primitive. Drives the alloc-on-return canon-lower
        /// wrapper for <c>own&lt;T&gt;</c> results — the host
        /// hands back a fresh i32 handle that indexes into the
        /// component's resource table.</summary>
        private static bool IsResourceReturnPrimitiveParams(MethodInfo m)
        {
            if (m.ReturnType.GetCustomAttribute<WasiResourceAttribute>() == null)
                return false;
            // Plain own<T> path — no [WasiOptionalReturn].
            if (m.GetCustomAttribute<WasiOptionalReturnAttribute>() != null)
                return false;
            foreach (var p in m.GetParameters())
                if (!IsPrimitive(p.ParameterType)) return false;
            return true;
        }

        /// <summary>True iff the method returns a
        /// [WasiResource]-marked class tagged
        /// [WasiOptionalReturn] — option&lt;own&lt;T&gt;&gt;.
        /// C# can't distinguish nullable from non-nullable
        /// reference types at reflection level, so the host
        /// opts in via the attribute.</summary>
        private static bool IsOptionResourceReturnPrimitiveParams(MethodInfo m)
        {
            if (m.ReturnType.GetCustomAttribute<WasiResourceAttribute>() == null)
                return false;
            if (m.GetCustomAttribute<WasiOptionalReturnAttribute>() == null)
                return false;
            foreach (var p in m.GetParameters())
                if (!IsPrimitive(p.ParameterType)) return false;
            return true;
        }

        /// <summary>Build a canon-lower wrapper for a host
        /// method returning <c>own&lt;T&gt;</c>. Wire form is a
        /// single i32 result (the handle) — no retArea
        /// indirection needed since it fits in a flat slot. The
        /// wrapper invokes the impl, allocates a fresh handle
        /// in the resource table, and returns the handle.</summary>
        private static void BindResourceReturnMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m, ResourceContext resources)
        {
            var importName = ToKebabCase(m.Name);
            var paramInfos = m.GetParameters();

            // Wrapper signature: ExecContext, [host params...] → int
            var paramTypes = new Type[paramInfos.Length + 1];
            paramTypes[0] = typeof(ExecContext);
            for (int i = 0; i < paramInfos.Length; i++)
                paramTypes[i + 1] = ToWireType(paramInfos[i].ParameterType);
            var allTypes = new Type[paramTypes.Length + 1];
            Array.Copy(paramTypes, allTypes, paramTypes.Length);
            allTypes[paramTypes.Length] = typeof(int);
            var delegateType = OpenFuncType(allTypes.Length)
                .MakeGenericType(allTypes);

            var table = resources.TableFor(m.ReturnType);

            int Body(ExecContext _, object?[] hostArgs)
            {
                var inst = m.Invoke(impl, hostArgs);
                if (inst == null)
                    throw new InvalidOperationException(
                        "Resource-returning host method '" + m.Name
                        + "' returned null — own<T> cannot be null.");
                return table.Allocate(inst);
            }

            var lambdaParams = new ParameterExpression[paramTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            for (int i = 0; i < paramInfos.Length; i++)
                lambdaParams[i + 1] = Expression.Parameter(
                    paramTypes[i + 1], paramInfos[i].Name);

            var argArr = Expression.NewArrayInit(typeof(object),
                paramInfos.Select((p, i) =>
                {
                    Expression e = lambdaParams[i + 1];
                    if (p.ParameterType != e.Type)
                        e = Expression.Convert(e, p.ParameterType);
                    return (Expression)Expression.Convert(e, typeof(object));
                }).ToArray());
            var bodyTarget = Expression.Constant(
                (Func<ExecContext, object?[], int>)Body);
            var call = Expression.Invoke(bodyTarget,
                lambdaParams[0], argArr);
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

        /// <summary>Canon-lower wrapper for a host method
        /// tagged <see cref="WasiErrorResultAttribute"/>
        /// returning a resource. Wire shape: void return,
        /// retAreaPtr trailing param. retArea layout: 1-byte
        /// outer disc + 3-byte padding + 4-byte handle (when
        /// Ok). Ok-only semantics in v0 — host throwing
        /// propagates as wasm trap; full Err mapping is a
        /// follow-up. Per-param wire shape supports primitives
        /// (including enum-typed ints) just like
        /// <see cref="BindMethod"/>.</summary>
        private static void BindErrorResultResourceReturnMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m, ResourceContext resources)
        {
            var importName = m.GetCustomAttribute<WasiMethodNameAttribute>()?
                .WitName ?? ToKebabCase(m.Name);
            var paramInfos = m.GetParameters();

            // Param shapes: primitive / enum (1 wire slot),
            // string (2 wire slots), borrow<resource> or
            // own<resource> (1 wire slot, resolved via
            // ResourceContext), option<own<resource>>
            // (2 wire slots: option disc + handle, tagged
            // [WasiOptionalParam]).
            bool IsOptResource(ParameterInfo p)
                => p.GetCustomAttribute<WasiOptionalParamAttribute>() != null
                   && p.ParameterType.GetCustomAttribute<
                       WasiResourceAttribute>() != null;
            foreach (var p in paramInfos)
                if (!IsPrimitive(p.ParameterType)
                    && !p.ParameterType.IsEnum
                    && p.ParameterType != typeof(string)
                    && p.ParameterType.GetCustomAttribute<
                        WasiResourceAttribute>() == null)
                    throw new InvalidOperationException(
                        "[WasiErrorResult] resource-return host "
                        + "method '" + m.Name + "' takes "
                        + "unsupported param type "
                        + p.ParameterType + ".");

            int paramSlotCount = 0;
            foreach (var p in paramInfos)
            {
                if (p.ParameterType == typeof(string)
                    || IsOptResource(p))
                    paramSlotCount += 2;
                else
                    paramSlotCount += 1;
            }
            var paramTypes = new Type[2 + paramSlotCount];
            paramTypes[0] = typeof(ExecContext);
            int wi = 1;
            foreach (var p in paramInfos)
            {
                if (p.ParameterType == typeof(string))
                {
                    paramTypes[wi++] = typeof(int);
                    paramTypes[wi++] = typeof(int);
                }
                else if (IsOptResource(p))
                {
                    paramTypes[wi++] = typeof(int);   // option disc
                    paramTypes[wi++] = typeof(int);   // handle
                }
                else if (p.ParameterType.GetCustomAttribute<
                    WasiResourceAttribute>() != null)
                {
                    paramTypes[wi++] = typeof(int);
                }
                else
                {
                    paramTypes[wi++] = ToWireType(p.ParameterType);
                }
            }
            paramTypes[wi] = typeof(int);   // retArea

            var delegateType = OpenActionType(paramTypes.Length)
                .MakeGenericType(paramTypes);

            var table = resources.TableFor(m.ReturnType);

            void Body(ExecContext ctx, object?[] hostArgs, int retAreaPtr)
            {
                var inst = m.Invoke(impl, hostArgs);
                var memory = ctx.DefaultMemory.Data;
                if (inst == null)
                    throw new InvalidOperationException(
                        "[WasiErrorResult] host method '" + m.Name
                        + "' returned null — Ok payload must "
                        + "be non-null.");
                var handle = table.Allocate(inst);
                // Always-Ok: outer disc=0, handle at offset 4.
                memory[retAreaPtr] = 0;
                memory[retAreaPtr + 1] = 0;
                memory[retAreaPtr + 2] = 0;
                memory[retAreaPtr + 3] = 0;
                WriteI32LE(memory, retAreaPtr + 4, handle);
            }

            var lambdaParams = new ParameterExpression[paramTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            wi = 1;
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (paramInfos[i].ParameterType == typeof(string))
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_ptr");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_len");
                }
                else if (IsOptResource(paramInfos[i]))
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_optDisc");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_handle");
                }
                else
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        paramTypes[wi - 1], paramInfos[i].Name);
                }
            }
            var retAreaParam = Expression.Parameter(typeof(int), "retAreaPtr");
            lambdaParams[paramTypes.Length - 1] = retAreaParam;

            var ctxParam = lambdaParams[0];
            var memoryProp = typeof(ExecContext).GetProperty(
                nameof(ExecContext.DefaultMemory))!;
            var memDataField = typeof(Wacs.Core.Runtime.Types.MemoryInstance)
                .GetField("Data")!;
            var memoryAccess = Expression.Field(
                Expression.Property(ctxParam, memoryProp),
                memDataField);
            var encodingUtf8 = Expression.Property(null,
                typeof(System.Text.Encoding).GetProperty(
                    nameof(System.Text.Encoding.UTF8))!);
            var getStringMethod = typeof(System.Text.Encoding).GetMethod(
                nameof(System.Text.Encoding.GetString),
                new[] { typeof(byte[]), typeof(int), typeof(int) })!;

            var contextConst = Expression.Constant(resources);
            var tableForMethod = typeof(ResourceContext).GetMethod(
                nameof(ResourceContext.TableFor))!;
            var getResourceMethod = typeof(ResourceTable).GetMethod(
                nameof(ResourceTable.Get))!;

            wi = 1;
            var argExprs = new Expression[paramInfos.Length];
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (paramInfos[i].ParameterType == typeof(string))
                {
                    var ptrParam = lambdaParams[wi++];
                    var lenParam = lambdaParams[wi++];
                    argExprs[i] = Expression.Convert(
                        Expression.Call(encodingUtf8, getStringMethod,
                            memoryAccess, ptrParam, lenParam),
                        typeof(object));
                }
                else if (IsOptResource(paramInfos[i]))
                {
                    // option<own<resource>> param: 2 slots
                    // (option disc + handle). Resolve via
                    // table when disc==1, else null.
                    var optDiscParam = lambdaParams[wi++];
                    var handleParam = lambdaParams[wi++];
                    var paramTable = Expression.Call(contextConst,
                        tableForMethod,
                        Expression.Constant(paramInfos[i].ParameterType));
                    var resolved = Expression.Convert(
                        Expression.Call(paramTable, getResourceMethod,
                            handleParam),
                        paramInfos[i].ParameterType);
                    argExprs[i] = Expression.Convert(
                        Expression.Condition(
                            Expression.Equal(optDiscParam,
                                Expression.Constant(0)),
                            Expression.Constant(null,
                                paramInfos[i].ParameterType),
                            resolved),
                        typeof(object));
                }
                else if (paramInfos[i].ParameterType.GetCustomAttribute<
                    WasiResourceAttribute>() != null)
                {
                    var handleParam = lambdaParams[wi++];
                    var paramTable = Expression.Call(contextConst,
                        tableForMethod,
                        Expression.Constant(paramInfos[i].ParameterType));
                    argExprs[i] = Expression.Convert(
                        Expression.Call(paramTable, getResourceMethod,
                            handleParam),
                        typeof(object));
                }
                else
                {
                    Expression e = lambdaParams[wi++];
                    if (paramInfos[i].ParameterType != e.Type)
                    {
                        if (paramInfos[i].ParameterType == typeof(bool))
                            e = Expression.NotEqual(e,
                                Expression.Constant(0, e.Type));
                        else
                            e = Expression.Convert(e,
                                paramInfos[i].ParameterType);
                    }
                    argExprs[i] = Expression.Convert(e, typeof(object));
                }
            }

            var argArr = Expression.NewArrayInit(typeof(object), argExprs);
            var bodyTarget = Expression.Constant(
                (Action<ExecContext, object?[], int>)Body);
            var call = Expression.Invoke(bodyTarget,
                lambdaParams[0], argArr, retAreaParam);
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

        /// <summary>Build a canon-lower wrapper for a host
        /// method returning <c>option&lt;own&lt;T&gt;&gt;</c>.
        /// Wire form: void return, retAreaPtr trailing param.
        /// retArea layout: 1 byte disc + 3 bytes padding + 4
        /// byte handle (when Some). Total 8 bytes.
        /// <list type="bullet">
        /// <item>Host returns null → write disc=0 (rest don't-
        /// care; we zero-fill defensively).</item>
        /// <item>Host returns instance → allocate handle,
        /// write disc=1 + handle at offset 4.</item>
        /// </list></summary>
        private static void BindOptionResourceReturnMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m, ResourceContext resources)
        {
            var importName = ToKebabCase(m.Name);
            var paramInfos = m.GetParameters();

            // Wrapper signature: ExecContext, [host params...],
            // retAreaPtr → void.
            var paramTypes = new Type[paramInfos.Length + 2];
            paramTypes[0] = typeof(ExecContext);
            for (int i = 0; i < paramInfos.Length; i++)
                paramTypes[i + 1] = ToWireType(paramInfos[i].ParameterType);
            paramTypes[paramInfos.Length + 1] = typeof(int);

            var delegateType = OpenActionType(paramTypes.Length)
                .MakeGenericType(paramTypes);

            var table = resources.TableFor(m.ReturnType);

            void Body(ExecContext ctx, object?[] hostArgs, int retAreaPtr)
            {
                var inst = m.Invoke(impl, hostArgs);
                var memory = ctx.DefaultMemory.Data;
                if (inst == null)
                {
                    memory[retAreaPtr] = 0;
                    memory[retAreaPtr + 1] = 0;
                    memory[retAreaPtr + 2] = 0;
                    memory[retAreaPtr + 3] = 0;
                    return;
                }
                var handle = table.Allocate(inst);
                memory[retAreaPtr] = 1;
                memory[retAreaPtr + 1] = 0;
                memory[retAreaPtr + 2] = 0;
                memory[retAreaPtr + 3] = 0;
                WriteI32LE(memory, retAreaPtr + 4, handle);
            }

            var lambdaParams = new ParameterExpression[paramTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            for (int i = 0; i < paramInfos.Length; i++)
                lambdaParams[i + 1] = Expression.Parameter(
                    paramTypes[i + 1], paramInfos[i].Name);
            lambdaParams[paramInfos.Length + 1] =
                Expression.Parameter(typeof(int), "retAreaPtr");

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

        /// <summary>Bind a resource type's methods + drop
        /// callback under a WIT namespace. Walks public
        /// instance methods on <typeparamref name="T"/>,
        /// kebab-cases each name, and registers under
        /// <c>[method]ResourceName.method-name</c>. Also
        /// registers a <c>[resource-drop]ResourceName</c>
        /// handler that calls <see cref="ResourceTable.Drop"/>
        /// (which Disposes IDisposable instances).
        /// </summary>
        public static void BindWasiResource<T>(
            this WasmRuntime runtime, string namespaceName,
            ResourceContext resources) where T : class
        {
            BindWasiResource(runtime, namespaceName, typeof(T), resources);
        }

        public static void BindWasiResource(
            this WasmRuntime runtime, string namespaceName,
            Type resourceType, ResourceContext resources)
        {
            var attr = resourceType.GetCustomAttribute<WasiResourceAttribute>();
            if (attr == null)
                throw new ArgumentException(
                    "Resource type " + resourceType + " must be "
                    + "marked [WasiResource].",
                    nameof(resourceType));
            var witName = attr.WitName ?? ToKebabCase(resourceType.Name);
            var table = resources.TableFor(resourceType);

            // Resource methods bind as [method]Name.method-name.
            // Skip Dispose — it's bound as [resource-drop]Name
            // separately rather than as an instance method.
            foreach (var m in resourceType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.DeclaringType == typeof(object)) continue;
                if (m.IsSpecialName) continue;
                if (m.Name == "Dispose") continue;
                BindResourceMethod(runtime, namespaceName,
                    witName, table, resourceType, m, resources);
            }

            // [constructor]Name — static factory methods
            // tagged [WasiConstructor]. Register one bind
            // per matching method. v0 supports the zero-arg
            // shape (Fields() pattern); parameterized
            // constructors land when the call site requires
            // them.
            foreach (var m in resourceType.GetMethods(
                BindingFlags.Public | BindingFlags.Static))
            {
                if (m.GetCustomAttribute<
                    WasiConstructorAttribute>() == null) continue;
                BindResourceConstructor(runtime, namespaceName,
                    witName, table, resourceType, m, resources);
            }

            // [resource-drop]Name — wasm passes i32 handle,
            // we drop the table entry (and Dispose).
            BindResourceDrop(runtime, namespaceName, witName, table);
        }

        /// <summary>Bind one method on a resource class. Wire
        /// form: first i32 is the handle (self via
        /// borrow&lt;T&gt;), then host params follow. Method
        /// return is dispatched on shape — primitive / void
        /// inline; string returns through a retArea wrapper
        /// (same pattern as BindStringReturnMethod but with a
        /// leading handle param).</summary>
        private static void BindResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string witResourceName, ResourceTable table,
            Type resourceType, MethodInfo m,
            ResourceContext resources)
        {
            // Honor [WasiMethodName] override when the C#
            // method name can't kebab-case back to the WIT
            // name (e.g. WIT "get-type" can't come from
            // GetType which clashes with Object.GetType).
            // [WasiStaticMethod] swaps the [method] prefix
            // for [static] — wire form is otherwise identical.
            var nameAttr = m.GetCustomAttribute<WasiMethodNameAttribute>();
            var rawName = nameAttr?.WitName ?? ToKebabCase(m.Name);
            var prefix = m.GetCustomAttribute<
                WasiStaticMethodAttribute>() != null
                ? "[static]" : "[method]";
            var importName = prefix + witResourceName + "." + rawName;
            var paramInfos = m.GetParameters();

            // [WasiStreamResult]-tagged methods canon-lower to
            // result<T, stream-error> where T is the host
            // method's return type. This covers most of
            // wasi:io/streams' surface.
            if (m.GetCustomAttribute<WasiStreamResultAttribute>() != null)
            {
                BindStreamResultResourceMethod(runtime, namespaceName,
                    importName, table, resourceType, m, resources);
                return;
            }

            // [WasiErrorResult]-tagged + resource return →
            // result<own<TReturn>, error-code> on a resource
            // method. Used by wasi:filesystem/types descriptor's
            // bridge methods (read-via-stream, write-via-stream).
            if (m.GetCustomAttribute<WasiErrorResultAttribute>() != null
                && m.ReturnType.GetCustomAttribute<WasiResourceAttribute>() != null)
            {
                BindErrorResultResourceMethodReturningResource(
                    runtime, namespaceName, importName, table,
                    resourceType, m, resources);
                return;
            }

            // [WasiErrorResult]-tagged + plain enum return
            // (no [Flags]) → result<enum, error-code>. Plain
            // enums are no-payload variants; wire is u8 disc.
            // [Flags] enums are handled below — they widen
            // to u8 / u16 / u32 based on declared flag count.
            if (m.GetCustomAttribute<WasiErrorResultAttribute>() != null
                && m.ReturnType.IsEnum
                && m.ReturnType.GetCustomAttribute<
                    System.FlagsAttribute>() == null)
            {
                BindErrorResultEnumReturnResourceMethod(
                    runtime, namespaceName, importName, table,
                    resourceType, m);
                return;
            }

            // [WasiErrorResult]-tagged + void return →
            // result<_, error-code> on a resource method.
            // Used by descriptor.sync / set-size / etc.
            if (m.GetCustomAttribute<WasiErrorResultAttribute>() != null
                && m.ReturnType == typeof(void))
            {
                BindErrorResultVoidReturnResourceMethod(
                    runtime, namespaceName, importName, table,
                    resourceType, m, resources);
                return;
            }

            // [WasiUnitResult] + void return →
            // result<_, _> on a resource method. Wire is
            // flat-return i32 (just the disc) instead of the
            // retArea memory write [WasiErrorResult] uses.
            // Reuses the same binder with the unit-result flag.
            if (m.GetCustomAttribute<WasiUnitResultAttribute>() != null
                && m.ReturnType == typeof(void))
            {
                BindErrorResultVoidReturnResourceMethod(
                    runtime, namespaceName, importName, table,
                    resourceType, m, resources,
                    unitResultFlatReturn: true);
                return;
            }

            // [WasiErrorResult]-tagged + ulong / byte[] return →
            // result<{u64|list<u8>}, error-code>. Wire layout
            // matches result<X, stream-error> at the Ok side
            // (both have payload alignment driven by the Ok
            // type), so we reuse BindStreamResultResourceMethod
            // which writes disc=0 + Ok payload at the right
            // offset. The Err side encoding differs but always-
            // Ok semantics in v0 means we never write it.
            if (m.GetCustomAttribute<WasiErrorResultAttribute>() != null
                && (m.ReturnType == typeof(ulong)
                    || m.ReturnType == typeof(uint)
                    || m.ReturnType == typeof(ushort)
                    || m.ReturnType == typeof(byte)
                    || m.ReturnType == typeof(bool)
                    || m.ReturnType == typeof(byte[])
                    || m.ReturnType == typeof(ValueTuple<byte[], bool>)
                    || IsPrimitiveRecord(m.ReturnType)
                    || m.ReturnType == typeof(
                        Wacs.WASI.Preview2.Sockets.IpSocketAddress)
                    || m.ReturnType.IsSubclassOf(typeof(
                        Wacs.WASI.Preview2.Sockets.IpSocketAddress))
                    || IsResourceTuple(m.ReturnType)
                    || m.ReturnType == typeof(
                        Wacs.WASI.Preview2.Sockets.IpAddressEnumerationItem)
                    || m.ReturnType.IsSubclassOf(typeof(
                        Wacs.WASI.Preview2.Sockets.IpAddressEnumerationItem))
                    || m.ReturnType == typeof(
                        Wacs.WASI.Preview2.Sockets.IncomingDatagram[])
                    || m.ReturnType == typeof(
                        Wacs.WASI.Preview2.Filesystem.DirectoryEntry)
                    || m.ReturnType == typeof(
                        Wacs.WASI.Preview2.Filesystem.DescriptorStat)
                    || (m.ReturnType.IsEnum && m.ReturnType
                        .GetCustomAttribute<System.FlagsAttribute>() != null)))
            {
                BindStreamResultResourceMethod(runtime, namespaceName,
                    importName, table, resourceType, m, resources);
                return;
            }

            // [WasiErrorResult]-tagged + string return →
            // result<string, error-code>. Wire layout: outer
            // disc + 3-byte align padding + (string-ptr,
            // string-len) at retArea+4/+8. String params
            // accepted (decoded via Encoding.UTF8.GetString).
            // Used by descriptor.readlink-at, etc.
            if (m.GetCustomAttribute<WasiErrorResultAttribute>() != null
                && m.ReturnType == typeof(string))
            {
                BindErrorResultStringReturnResourceMethod(
                    runtime, namespaceName, importName, table,
                    resourceType, m);
                return;
            }

            // Method returns a resource → alloc handle.
            if (m.ReturnType.GetCustomAttribute<WasiResourceAttribute>() != null)
            {
                BindResourceReturnResourceMethod(runtime, namespaceName,
                    importName, table, resourceType, m, resources);
                return;
            }

            // void(this, [WasiResultParam] T? response) — the
            // response-outparam.set shape. Single param, marker
            // attribute, T is a resource. Wire: 2 slots
            // (outer disc + handle); disc==0 resolves the
            // handle, else null.
            if (m.ReturnType == typeof(void)
                && paramInfos.Length == 1
                && paramInfos[0].GetCustomAttribute<
                    WasiResultParamAttribute>() != null
                && paramInfos[0].ParameterType.GetCustomAttribute<
                    WasiResourceAttribute>() != null)
            {
                BindResultResourceParamVoidResourceMethod(
                    runtime, namespaceName, importName, table,
                    resourceType, m, resources);
                return;
            }

            // Aggregate-param resource methods are a follow-up
            // (write etc. are handled via [WasiStreamResult]).
            // Admitted: primitive (1 wire slot), borrow<resource>
            // (1 slot, resolved via ResourceContext), string
            // (2 slots — ptr + len, decoded via UTF-8). Other
            // aggregates (records, lists, options) stay
            // deferred.
            foreach (var p in paramInfos)
                if (!IsPrimitive(p.ParameterType)
                    && p.ParameterType.GetCustomAttribute<
                        WasiResourceAttribute>() == null
                    && p.ParameterType != typeof(string)) return;

            // Dispatch on return shape. String returns go
            // through the retArea wrapper; primitive / void
            // continue inline.
            if (m.ReturnType == typeof(string))
            {
                if (m.GetCustomAttribute<
                    WasiOptionalReturnAttribute>() != null)
                {
                    BindOptionStringReturnResourceMethod(
                        runtime, namespaceName, importName,
                        table, resourceType, m);
                    return;
                }
                BindStringReturnResourceMethod(runtime, namespaceName,
                    importName, table, resourceType, m);
                return;
            }
            if (m.ReturnType == typeof(byte[]))
            {
                BindByteArrayReturnResourceMethod(runtime, namespaceName,
                    importName, table, resourceType, m);
                return;
            }
            // option<primitive> return on resource method —
            // Nullable<T> with [WasiOptionalReturn]. retArea:
            // option disc + alignment padding + payload (size
            // matches T's wire size).
            if (m.GetCustomAttribute<WasiOptionalReturnAttribute>() != null
                && m.ReturnType.IsGenericType
                && m.ReturnType.GetGenericTypeDefinition()
                    == typeof(Nullable<>)
                && IsPrimitive(m.ReturnType.GetGenericArguments()[0]))
            {
                BindOptionPrimitiveReturnResourceMethod(
                    runtime, namespaceName, importName,
                    table, resourceType, m);
                return;
            }
            // HTTP method / scheme variant returns — 12-byte
            // retArea (variant disc + 3 pad + (str-ptr, len)
            // when the case is "other(string)"; payload bytes
            // unused for the named cases).
            if (m.ReturnType == typeof(
                    Wacs.WASI.Preview2.Http.HttpMethod)
                || m.ReturnType.IsSubclassOf(typeof(
                    Wacs.WASI.Preview2.Http.HttpMethod)))
            {
                BindHttpMethodReturnResourceMethod(
                    runtime, namespaceName, importName,
                    table, resourceType, m);
                return;
            }
            if (m.ReturnType == typeof(
                    Wacs.WASI.Preview2.Http.HttpScheme)
                || m.ReturnType.IsSubclassOf(typeof(
                    Wacs.WASI.Preview2.Http.HttpScheme)))
            {
                BindHttpSchemeReturnResourceMethod(
                    runtime, namespaceName, importName,
                    table, resourceType, m);
                return;
            }
            // list<(string, byte[])> return — used by
            // fields.entries() in wasi:http. Each element is
            // 16 bytes: (string-ptr, string-len, bytes-ptr,
            // bytes-len) at align 4.
            if (m.ReturnType == typeof(ValueTuple<string, byte[]>[]))
            {
                BindStringByteArrayPairListReturnResourceMethod(
                    runtime, namespaceName, importName,
                    table, resourceType, m);
                return;
            }
            // list<list<u8>> return — byte[][]. Used by
            // fields.get() in wasi:http. Each element is
            // 8 bytes (bytes-ptr, bytes-len) at align 4.
            if (m.ReturnType == typeof(byte[][]))
            {
                BindByteArrayListReturnResourceMethod(
                    runtime, namespaceName, importName,
                    table, resourceType, m);
                return;
            }
            // Bare enum returns (no result wrapping) ride the
            // primitive path through ToWireType — wire form is
            // the underlying integer.
            if (!IsPrimitiveOrVoid(m.ReturnType)
                && !m.ReturnType.IsEnum) return;

            // Wrapper signature: ExecContext, handle (i32),
            // [host params...] → wire return type (or void).
            // Resource params lower to i32 (handle); string
            // params lower to (i32 ptr, i32 len).
            int slotsPerParam(ParameterInfo p)
                => p.ParameterType == typeof(string) ? 2 : 1;
            int paramSlotsTotal = 0;
            foreach (var p in paramInfos)
                paramSlotsTotal += slotsPerParam(p);
            var wireParamTypes = new Type[paramSlotsTotal + 2];
            wireParamTypes[0] = typeof(ExecContext);
            wireParamTypes[1] = typeof(int);
            int wti = 2;
            foreach (var p in paramInfos)
            {
                if (p.ParameterType == typeof(string))
                {
                    wireParamTypes[wti++] = typeof(int);
                    wireParamTypes[wti++] = typeof(int);
                }
                else if (p.ParameterType.GetCustomAttribute<
                    WasiResourceAttribute>() != null)
                {
                    wireParamTypes[wti++] = typeof(int);
                }
                else
                {
                    wireParamTypes[wti++] = ToWireType(p.ParameterType);
                }
            }

            Type delegateType;
            if (m.ReturnType == typeof(void))
                delegateType = OpenActionType(wireParamTypes.Length)
                    .MakeGenericType(wireParamTypes);
            else
            {
                var allTypes = new Type[wireParamTypes.Length + 1];
                Array.Copy(wireParamTypes, allTypes, wireParamTypes.Length);
                allTypes[wireParamTypes.Length] = ToWireType(m.ReturnType);
                delegateType = OpenFuncType(allTypes.Length)
                    .MakeGenericType(allTypes);
            }

            // Body: look up instance from handle, call method.
            var lambdaParams = new ParameterExpression[wireParamTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            lambdaParams[1] = Expression.Parameter(typeof(int), "self");
            int lpi = 2;
            foreach (var p in paramInfos)
            {
                if (p.ParameterType == typeof(string))
                {
                    lambdaParams[lpi++] = Expression.Parameter(
                        typeof(int), p.Name + "_ptr");
                    lambdaParams[lpi++] = Expression.Parameter(
                        typeof(int), p.Name + "_len");
                }
                else
                {
                    lambdaParams[lpi++] = Expression.Parameter(
                        wireParamTypes[lpi - 1], p.Name);
                }
            }

            // instance = (T)table.Get(self)
            var tableConst = Expression.Constant(table);
            var getMethod = typeof(ResourceTable).GetMethod(
                nameof(ResourceTable.Get))!;
            var instanceExpr = Expression.Convert(
                Expression.Call(tableConst, getMethod, lambdaParams[1]),
                resourceType);

            // For resource-typed params we need the matching
            // ResourceContext to resolve the handle. The expr
            // tree uses TableFor(type).Get(h) per param.
            var contextConst = Expression.Constant(resources);
            var tableForMethod = typeof(ResourceContext).GetMethod(
                nameof(ResourceContext.TableFor))!;
            // For string params: load memory once and reuse.
            var memoryProp = typeof(ExecContext).GetProperty(
                nameof(ExecContext.DefaultMemory))!;
            var memDataField = typeof(Wacs.Core.Runtime.Types.MemoryInstance)
                .GetField("Data")!;
            var memoryAccess = Expression.Field(
                Expression.Property(lambdaParams[0], memoryProp),
                memDataField);
            var encodingUtf8 = Expression.Property(null,
                typeof(System.Text.Encoding).GetProperty(
                    nameof(System.Text.Encoding.UTF8))!);
            var getStringMethod = typeof(System.Text.Encoding).GetMethod(
                nameof(System.Text.Encoding.GetString),
                new[] { typeof(byte[]), typeof(int), typeof(int) })!;

            var argExprs = new Expression[paramInfos.Length];
            int slotIdx = 2;
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (paramInfos[i].ParameterType == typeof(string))
                {
                    var ptrParam = lambdaParams[slotIdx++];
                    var lenParam = lambdaParams[slotIdx++];
                    argExprs[i] = Expression.Call(encodingUtf8,
                        getStringMethod, memoryAccess, ptrParam, lenParam);
                    continue;
                }
                Expression e = lambdaParams[slotIdx++];
                if (paramInfos[i].ParameterType.GetCustomAttribute<
                        WasiResourceAttribute>() != null)
                {
                    // Resolve borrow handle via ResourceContext.
                    var paramTable = Expression.Call(contextConst,
                        tableForMethod,
                        Expression.Constant(paramInfos[i].ParameterType));
                    e = Expression.Convert(
                        Expression.Call(paramTable, getMethod, e),
                        paramInfos[i].ParameterType);
                }
                else if (paramInfos[i].ParameterType != e.Type)
                {
                    if (paramInfos[i].ParameterType == typeof(bool))
                        e = Expression.NotEqual(e,
                            Expression.Constant(0, e.Type));
                    else
                        e = Expression.Convert(e,
                            paramInfos[i].ParameterType);
                }
                argExprs[i] = e;
            }
            Expression call = Expression.Call(instanceExpr, m, argExprs);
            if (m.ReturnType != typeof(void)
                && m.ReturnType != ToWireType(m.ReturnType))
            {
                if (m.ReturnType == typeof(bool))
                    call = Expression.Condition(call,
                        Expression.Constant(1),
                        Expression.Constant(0));
                else
                    call = Expression.Convert(call,
                        ToWireType(m.ReturnType));
            }
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

        /// <summary>Resource method returning string — same
        /// retArea-write template as the regular string path
        /// but with a leading handle param that resolves to the
        /// host instance via the resource table.</summary>
        private static void BindStringReturnResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable table,
            Type resourceType, MethodInfo m)
        {
            BindAggregateResourceMethod(runtime, namespaceName,
                importName, table, resourceType, m,
                (memory, retAreaPtr, ret, allocate) =>
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes((string)ret!);
                    var dataPtr = bytes.Length == 0 ? 0
                        : allocate(1, bytes.Length);
                    if (bytes.Length > 0)
                        System.Array.Copy(bytes, 0, memory,
                            dataPtr, bytes.Length);
                    WriteI32LE(memory, retAreaPtr, dataPtr);
                    WriteI32LE(memory, retAreaPtr + 4, bytes.Length);
                });
        }

        /// <summary>Resource method returning
        /// <c>list&lt;list&lt;u8&gt;&gt;</c> (byte[][]). Used
        /// by fields.get() in wasi:http. retArea: 8 bytes
        /// (list-ptr, list-len). Each elem 8 bytes
        /// (bytes-ptr, bytes-len) at align 4.</summary>
        private static void BindByteArrayListReturnResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable table,
            Type resourceType, MethodInfo m)
        {
            BindAggregateResourceMethod(runtime, namespaceName,
                importName, table, resourceType, m,
                (memory, retAreaPtr, ret, allocate) =>
                {
                    var arr = (byte[][])ret!;
                    int count = arr.Length;
                    int arrayPtr = count == 0 ? 0
                        : allocate(4, count * 8);
                    for (int i = 0; i < count; i++)
                    {
                        var bs = arr[i];
                        int dPtr = bs.Length == 0 ? 0
                            : allocate(1, bs.Length);
                        if (bs.Length > 0)
                            Array.Copy(bs, 0, memory,
                                dPtr, bs.Length);
                        WriteI32LE(memory, arrayPtr + i * 8, dPtr);
                        WriteI32LE(memory, arrayPtr + i * 8 + 4,
                            bs.Length);
                    }
                    WriteI32LE(memory, retAreaPtr, arrayPtr);
                    WriteI32LE(memory, retAreaPtr + 4, count);
                });
        }

        /// <summary>Resource method returning
        /// <c>list&lt;tuple&lt;string, list&lt;u8&gt;&gt;&gt;</c>
        /// (string-byte-array pairs). Used by
        /// fields.entries() in wasi:http. retArea is 8 bytes
        /// (list-ptr, list-len); each element is 16 bytes
        /// (string-ptr, string-len, bytes-ptr, bytes-len) at
        /// align 4.</summary>
        private static void BindStringByteArrayPairListReturnResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable table,
            Type resourceType, MethodInfo m)
        {
            BindAggregateResourceMethod(runtime, namespaceName,
                importName, table, resourceType, m,
                (memory, retAreaPtr, ret, allocate) =>
                {
                    var arr = (ValueTuple<string, byte[]>[])ret!;
                    int count = arr.Length;
                    int arrayPtr = count == 0 ? 0
                        : allocate(4, count * 16);
                    for (int i = 0; i < count; i++)
                    {
                        var (key, value) = arr[i];
                        var keyBytes = System.Text.Encoding.UTF8
                            .GetBytes(key);
                        int keyPtr = keyBytes.Length == 0 ? 0
                            : allocate(1, keyBytes.Length);
                        if (keyBytes.Length > 0)
                            Array.Copy(keyBytes, 0, memory,
                                keyPtr, keyBytes.Length);
                        int valPtr = value.Length == 0 ? 0
                            : allocate(1, value.Length);
                        if (value.Length > 0)
                            Array.Copy(value, 0, memory,
                                valPtr, value.Length);
                        WriteI32LE(memory, arrayPtr + i * 16, keyPtr);
                        WriteI32LE(memory, arrayPtr + i * 16 + 4,
                            keyBytes.Length);
                        WriteI32LE(memory, arrayPtr + i * 16 + 8,
                            valPtr);
                        WriteI32LE(memory, arrayPtr + i * 16 + 12,
                            value.Length);
                    }
                    WriteI32LE(memory, retAreaPtr, arrayPtr);
                    WriteI32LE(memory, retAreaPtr + 4, count);
                });
        }

        /// <summary>Resource method returning
        /// <c>variant method</c> (HTTP method) or
        /// <c>variant scheme</c> (HTTP scheme). Both variants
        /// are 9-or-3 unit cases plus a final
        /// "other(string)" case. retArea is 12 bytes:
        /// 1-byte disc + 3 bytes padding + (str-ptr, str-len)
        /// at offsets 4/8 (only meaningful for the "other"
        /// case; named cases leave the trailing slots as zero).
        /// </summary>
        private static void BindHttpVariantReturnHelper(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable table,
            Type resourceType, MethodInfo m,
            Func<object, (byte disc, string? payloadStr)> mapper)
        {
            BindAggregateResourceMethod(runtime, namespaceName,
                importName, table, resourceType, m,
                (memory, retAreaPtr, ret, allocate) =>
                {
                    var (disc, payloadStr) = mapper(ret!);
                    memory[retAreaPtr] = disc;
                    memory[retAreaPtr + 1] = 0;
                    memory[retAreaPtr + 2] = 0;
                    memory[retAreaPtr + 3] = 0;
                    if (payloadStr != null)
                    {
                        var bytes = System.Text.Encoding.UTF8
                            .GetBytes(payloadStr);
                        int dataPtr = bytes.Length == 0 ? 0
                            : allocate(1, bytes.Length);
                        if (bytes.Length > 0)
                            Array.Copy(bytes, 0, memory,
                                dataPtr, bytes.Length);
                        WriteI32LE(memory, retAreaPtr + 4, dataPtr);
                        WriteI32LE(memory, retAreaPtr + 8, bytes.Length);
                    }
                });
        }

        private static void BindHttpMethodReturnResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable table,
            Type resourceType, MethodInfo m)
        {
            BindHttpVariantReturnHelper(runtime, namespaceName,
                importName, table, resourceType, m, ret =>
                {
                    var meth = (Wacs.WASI.Preview2.Http.HttpMethod)ret;
                    return meth switch
                    {
                        Wacs.WASI.Preview2.Http.HttpMethodGet
                            => ((byte)0, (string?)null),
                        Wacs.WASI.Preview2.Http.HttpMethodHead
                            => ((byte)1, (string?)null),
                        Wacs.WASI.Preview2.Http.HttpMethodPost
                            => ((byte)2, (string?)null),
                        Wacs.WASI.Preview2.Http.HttpMethodPut
                            => ((byte)3, (string?)null),
                        Wacs.WASI.Preview2.Http.HttpMethodDelete
                            => ((byte)4, (string?)null),
                        Wacs.WASI.Preview2.Http.HttpMethodConnect
                            => ((byte)5, (string?)null),
                        Wacs.WASI.Preview2.Http.HttpMethodOptions
                            => ((byte)6, (string?)null),
                        Wacs.WASI.Preview2.Http.HttpMethodTrace
                            => ((byte)7, (string?)null),
                        Wacs.WASI.Preview2.Http.HttpMethodPatch
                            => ((byte)8, (string?)null),
                        Wacs.WASI.Preview2.Http.HttpMethodOther o
                            => ((byte)9, (string?)o.Name),
                        _ => throw new ArgumentException(
                            "Unknown HttpMethod subclass: "
                            + meth.GetType()),
                    };
                });
        }

        private static void BindHttpSchemeReturnResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable table,
            Type resourceType, MethodInfo m)
        {
            BindHttpVariantReturnHelper(runtime, namespaceName,
                importName, table, resourceType, m, ret =>
                {
                    var sch = (Wacs.WASI.Preview2.Http.HttpScheme)ret;
                    return sch switch
                    {
                        Wacs.WASI.Preview2.Http.HttpSchemeHttp
                            => ((byte)0, (string?)null),
                        Wacs.WASI.Preview2.Http.HttpSchemeHttps
                            => ((byte)1, (string?)null),
                        Wacs.WASI.Preview2.Http.HttpSchemeOther o
                            => ((byte)2, (string?)o.Name),
                        _ => throw new ArgumentException(
                            "Unknown HttpScheme subclass: "
                            + sch.GetType()),
                    };
                });
        }

        /// <summary>Resource method returning
        /// <c>option&lt;primitive&gt;</c> (option<u64>,
        /// option<u32>, option<bool>, etc.). retArea size +
        /// payload offset depend on the primitive's wire
        /// width:
        ///   u8 / bool: payload at +1, total 2
        ///   u16:        payload at +2, total 4
        ///   u32 / s32:  payload at +4, total 8
        ///   u64 / s64:  payload at +8, total 16
        /// null → disc=0; non-null → disc=1 + payload bytes
        /// at the aligned offset.</summary>
        private static void BindOptionPrimitiveReturnResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable table,
            Type resourceType, MethodInfo m)
        {
            var inner = m.ReturnType.GetGenericArguments()[0];
            int payloadSize = PrimitiveByteSize(inner);
            int payloadOffset = AlignUp(1, payloadSize);
            BindAggregateResourceMethod(runtime, namespaceName,
                importName, table, resourceType, m,
                (memory, retAreaPtr, ret, allocate) =>
                {
                    if (ret == null)
                    {
                        memory[retAreaPtr] = 0;     // None
                        return;
                    }
                    memory[retAreaPtr] = 1;         // Some
                    // Zero padding bytes between disc + payload.
                    for (int i = 1; i < payloadOffset; i++)
                        memory[retAreaPtr + i] = 0;
                    WritePrimitiveLE(memory,
                        retAreaPtr + payloadOffset, inner, ret);
                });
        }

        /// <summary>Resource method returning
        /// <c>option&lt;string&gt;</c> — null lifts to disc=0,
        /// non-null to disc=1 + (ptr, len) at offsets 4/8.
        /// retArea is 12 bytes (1-byte option disc, 3 bytes
        /// padding to 4-align, 4-byte ptr, 4-byte len).
        /// Methods opt in via [WasiOptionalReturn].</summary>
        private static void BindOptionStringReturnResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable table,
            Type resourceType, MethodInfo m)
        {
            BindAggregateResourceMethod(runtime, namespaceName,
                importName, table, resourceType, m,
                (memory, retAreaPtr, ret, allocate) =>
                {
                    if (ret == null)
                    {
                        memory[retAreaPtr] = 0;     // None
                        return;
                    }
                    memory[retAreaPtr] = 1;         // Some
                    memory[retAreaPtr + 1] = 0;
                    memory[retAreaPtr + 2] = 0;
                    memory[retAreaPtr + 3] = 0;
                    var bytes = System.Text.Encoding.UTF8.GetBytes((string)ret);
                    var dataPtr = bytes.Length == 0 ? 0
                        : allocate(1, bytes.Length);
                    if (bytes.Length > 0)
                        System.Array.Copy(bytes, 0, memory,
                            dataPtr, bytes.Length);
                    WriteI32LE(memory, retAreaPtr + 4, dataPtr);
                    WriteI32LE(memory, retAreaPtr + 8, bytes.Length);
                });
        }

        /// <summary>Resource method returning byte[]. Same wire
        /// form as string return; differs only in the encoding
        /// step (here: identity).</summary>
        private static void BindByteArrayReturnResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable table,
            Type resourceType, MethodInfo m)
        {
            BindAggregateResourceMethod(runtime, namespaceName,
                importName, table, resourceType, m,
                (memory, retAreaPtr, ret, allocate) =>
                {
                    var bytes = (byte[])ret!;
                    var dataPtr = bytes.Length == 0 ? 0
                        : allocate(1, bytes.Length);
                    if (bytes.Length > 0)
                        System.Array.Copy(bytes, 0, memory,
                            dataPtr, bytes.Length);
                    WriteI32LE(memory, retAreaPtr, dataPtr);
                    WriteI32LE(memory, retAreaPtr + 4, bytes.Length);
                });
        }

        /// <summary>Shared template for aggregate-returning
        /// resource methods. Handles handle lookup, expression-
        /// tree compilation, and BindHostFunction registration.
        /// The shape-specific bit lives in the writePayload
        /// callback.</summary>
        private static void BindAggregateResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable table,
            Type resourceType, MethodInfo m,
            Action<byte[], int, object?, Func<int, int, int>> writePayload)
        {
            var paramInfos = m.GetParameters();

            // Wrapper signature: ExecContext, handle (i32),
            // [host params...], retAreaPtr (i32) → void.
            // string params expand to (ptr, len) — 2 wire slots.
            int paramSlots = 0;
            foreach (var p in paramInfos)
                paramSlots += p.ParameterType == typeof(string) ? 2 : 1;
            var wireParamTypes = new Type[paramSlots + 3];
            wireParamTypes[0] = typeof(ExecContext);
            wireParamTypes[1] = typeof(int);   // self handle
            int wpi = 2;
            foreach (var p in paramInfos)
            {
                if (p.ParameterType == typeof(string))
                {
                    wireParamTypes[wpi++] = typeof(int);
                    wireParamTypes[wpi++] = typeof(int);
                }
                else
                {
                    wireParamTypes[wpi++] = ToWireType(p.ParameterType);
                }
            }
            wireParamTypes[wpi] = typeof(int);   // retAreaPtr

            var delegateType = OpenActionType(wireParamTypes.Length)
                .MakeGenericType(wireParamTypes);

            Wacs.Core.Runtime.Delegates.GenericFuncs? cabiRealloc = null;
            int Allocate(int align, int size)
            {
                if (cabiRealloc == null)
                {
                    if (!runtime.TryGetExportedFunction(
                            "cabi_realloc", out var addr))
                        throw new InvalidOperationException(
                            "Component does not export "
                            + "cabi_realloc — required for "
                            + "aggregate-returning resource methods.");
                    cabiRealloc = runtime.CreateInvoker(
                        addr, new InvokerOptions());
                }
                return cabiRealloc(0, 0, align, size)[0].Data.Int32;
            }

            void Body(ExecContext ctx, int handle, object?[] hostArgs,
                int retAreaPtr)
            {
                var inst = table.Get(handle);
                var ret = m.Invoke(inst, hostArgs);
                var memory = ctx.DefaultMemory.Data;
                writePayload(memory, retAreaPtr, ret, Allocate);
            }

            var lambdaParams = new ParameterExpression[wireParamTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            lambdaParams[1] = Expression.Parameter(typeof(int), "self");
            int lpi = 2;
            foreach (var p in paramInfos)
            {
                if (p.ParameterType == typeof(string))
                {
                    lambdaParams[lpi++] = Expression.Parameter(
                        typeof(int), p.Name + "_ptr");
                    lambdaParams[lpi++] = Expression.Parameter(
                        typeof(int), p.Name + "_len");
                }
                else
                {
                    lambdaParams[lpi++] = Expression.Parameter(
                        wireParamTypes[lpi - 1], p.Name);
                }
            }
            lambdaParams[wireParamTypes.Length - 1] =
                Expression.Parameter(typeof(int), "retAreaPtr");

            // For string params: load memory once.
            var memoryProp = typeof(ExecContext).GetProperty(
                nameof(ExecContext.DefaultMemory))!;
            var memDataField = typeof(Wacs.Core.Runtime.Types.MemoryInstance)
                .GetField("Data")!;
            var memoryAccess = Expression.Field(
                Expression.Property(lambdaParams[0], memoryProp),
                memDataField);
            var encodingUtf8 = Expression.Property(null,
                typeof(System.Text.Encoding).GetProperty(
                    nameof(System.Text.Encoding.UTF8))!);
            var getStringMethod = typeof(System.Text.Encoding).GetMethod(
                nameof(System.Text.Encoding.GetString),
                new[] { typeof(byte[]), typeof(int), typeof(int) })!;

            int slotIdx = 2;
            var argExprs = new Expression[paramInfos.Length];
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (paramInfos[i].ParameterType == typeof(string))
                {
                    var ptrParam = lambdaParams[slotIdx++];
                    var lenParam = lambdaParams[slotIdx++];
                    argExprs[i] = Expression.Convert(
                        Expression.Call(encodingUtf8, getStringMethod,
                            memoryAccess, ptrParam, lenParam),
                        typeof(object));
                    continue;
                }
                Expression e = lambdaParams[slotIdx++];
                if (paramInfos[i].ParameterType != e.Type)
                    e = Expression.Convert(e, paramInfos[i].ParameterType);
                argExprs[i] = Expression.Convert(e, typeof(object));
            }
            var argArr = Expression.NewArrayInit(typeof(object), argExprs);
            var bodyTarget = Expression.Constant(
                (Action<ExecContext, int, object?[], int>)Body);
            var call = Expression.Invoke(bodyTarget,
                lambdaParams[0], lambdaParams[1], argArr,
                lambdaParams[wireParamTypes.Length - 1]);
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

        /// <summary>Resource method tagged
        /// <see cref="WasiStreamResultAttribute"/>. Canon-lowers
        /// to <c>result&lt;T, stream-error&gt;</c> where T is
        /// the host method's return type. v0 always-Ok
        /// semantics — host throwing propagates as wasm trap
        /// rather than mapping to last-operation-failed /
        /// closed; the Err encoding is a follow-up.
        ///
        /// <para>Wire shape varies by Ok payload:
        /// <list type="bullet">
        /// <item>void → 1-byte disc only (12-byte retArea
        /// reserved for the variant-payload Err side, but Ok
        /// only writes byte 0 = 0).</item>
        /// <item>u64 → disc + u64 at align-8 offset.</item>
        /// <item>list&lt;u8&gt; → disc + (ptr, len) at
        /// align-4 offset.</item>
        /// </list>
        /// Param shape: optional list&lt;u8&gt; (lifted from
        /// (dataPtr, len) on the wasm stack).</para>
        /// </summary>
        private static void BindStreamResultResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable table,
            Type resourceType, MethodInfo m,
            ResourceContext? resources = null)
        {
            var paramInfos = m.GetParameters();
            // Recognized param shapes:
            //   1. Zero params.
            //   2. One byte[] (list<u8>) — expands to 2 wire i32s
            //      (dataPtr, len). MUST be first when present.
            //   3. Any number of primitive params — each takes
            //      one wire slot (i32 or i64 per ToWireType).
            //   4. Borrow<resource> params — 1 wire slot (handle
            //      resolved via ResourceContext at the boundary).
            //   5. option<ip-socket-address> param tagged
            //      [WasiOptionalParam] — 13 wire slots (option
            //      disc + 12 ip-socket-address flat slots).
            bool hasBytesParam = false;
            int bytesParamIndex = -1;
            bool IsOptionalIpAddrParam(ParameterInfo p)
                => p.ParameterType == typeof(
                       Wacs.WASI.Preview2.Sockets.IpSocketAddress)
                   && p.GetCustomAttribute<
                       WasiOptionalParamAttribute>() != null;
            bool IsOutgoingDatagramArrayParam(ParameterInfo p)
                => p.ParameterType == typeof(
                    Wacs.WASI.Preview2.Sockets.OutgoingDatagram[]);
            for (int i = 0; i < paramInfos.Length; i++)
            {
                var pt = paramInfos[i].ParameterType;
                if (pt == typeof(byte[]))
                {
                    if (hasBytesParam)
                        return;   // multiple byte[] params unsupported
                    hasBytesParam = true;
                    bytesParamIndex = i;
                }
                else if (IsOptionalIpAddrParam(paramInfos[i]))
                {
                    // OK — handled specially below.
                }
                else if (IsOutgoingDatagramArrayParam(paramInfos[i]))
                {
                    // OK — handled specially below.
                }
                else if (pt == typeof(string))
                {
                    // OK — 2 wire slots (ptr + len), decoded via
                    // Encoding.UTF8.GetString.
                }
                else if (!IsPrimitive(pt)
                    && pt.GetCustomAttribute<WasiResourceAttribute>() == null)
                {
                    return;   // unsupported (record etc.)
                }
            }
            if (hasBytesParam && bytesParamIndex != 0)
                return;   // byte[] must be first for v0

            // Per-param wire-slot count.
            int SlotsFor(ParameterInfo p)
            {
                if (p.ParameterType == typeof(byte[])) return 2;
                if (p.ParameterType == typeof(string)) return 2;
                if (IsOptionalIpAddrParam(p)) return 13;
                if (IsOutgoingDatagramArrayParam(p)) return 2;   // ptr + len
                return 1;
            }
            int paramSlotsTotal = 0;
            foreach (var p in paramInfos)
                paramSlotsTotal += SlotsFor(p);
            int wireParamCount = 1 /*handle*/
                + paramSlotsTotal
                + 1 /*retArea*/;
            var wireParamTypes = new Type[wireParamCount + 1];
            wireParamTypes[0] = typeof(ExecContext);
            wireParamTypes[1] = typeof(int);   // self handle
            int ti = 2;
            Type WireOf(Type pt)
                => pt.GetCustomAttribute<WasiResourceAttribute>() != null
                    ? typeof(int) : ToWireType(pt);
            foreach (var p in paramInfos)
            {
                if (p.ParameterType == typeof(byte[]))
                {
                    wireParamTypes[ti++] = typeof(int);   // dataPtr
                    wireParamTypes[ti++] = typeof(int);   // len
                }
                else if (p.ParameterType == typeof(string))
                {
                    wireParamTypes[ti++] = typeof(int);   // strPtr
                    wireParamTypes[ti++] = typeof(int);   // strLen
                }
                else if (IsOptionalIpAddrParam(p))
                {
                    for (int k = 0; k < 13; k++)
                        wireParamTypes[ti++] = typeof(int);
                }
                else if (IsOutgoingDatagramArrayParam(p))
                {
                    wireParamTypes[ti++] = typeof(int);   // listPtr
                    wireParamTypes[ti++] = typeof(int);   // listLen
                }
                else
                {
                    wireParamTypes[ti++] = WireOf(p.ParameterType);
                }
            }
            wireParamTypes[ti] = typeof(int);   // retArea

            var delegateType = OpenActionType(wireParamTypes.Length)
                .MakeGenericType(wireParamTypes);

            // Recognized return shapes (Ok payload): void, bool/u8,
            // u16, u32, u64, byte[], (byte[], bool) ValueTuple
            // (descriptor.read shape), record-of-primitives
            // (metadata-hash-value etc.), IpSocketAddress
            // (sockets local-/remote-address), or tuple-of-
            // resources (accept / finish-connect: 2-3 i32
            // handles, each allocated in its own table). Each
            // encodes as disc + Ok payload at the natural
            // alignment.
            var rt = m.ReturnType;
            var retShape = rt == typeof(void) ? 0
                : rt == typeof(ulong) ? 1
                : rt == typeof(byte[]) ? 2
                : rt == typeof(bool) ? 3
                : rt == typeof(byte) ? 4
                : rt == typeof(uint) ? 5
                : rt == typeof(ushort) ? 6
                : rt == typeof(ValueTuple<byte[], bool>) ? 7
                : IsPrimitiveRecord(rt) ? 8
                : rt == typeof(Wacs.WASI.Preview2.Sockets.IpSocketAddress)
                    || rt.IsSubclassOf(typeof(
                        Wacs.WASI.Preview2.Sockets.IpSocketAddress)) ? 9
                : IsResourceTuple(rt) ? 10
                : rt == typeof(Wacs.WASI.Preview2.Sockets.IpAddressEnumerationItem)
                    || rt.IsSubclassOf(typeof(
                        Wacs.WASI.Preview2.Sockets.IpAddressEnumerationItem)) ? 11
                : rt == typeof(Wacs.WASI.Preview2.Sockets.IncomingDatagram[]) ? 12
                : rt == typeof(Wacs.WASI.Preview2.Filesystem.DirectoryEntry) ? 13
                : rt == typeof(Wacs.WASI.Preview2.Filesystem.DescriptorStat) ? 14
                : (rt.IsEnum
                    && rt.GetCustomAttribute<System.FlagsAttribute>() != null) ? 15
                : -1;
            if (retShape < 0)
                throw new InvalidOperationException(
                    "[WasiStreamResult] on method with unsupported "
                    + "return type " + rt + " (expected void / "
                    + "bool / u8 / u16 / u32 / u64 / byte[] / "
                    + "(byte[], bool) / record-of-primitives / "
                    + "ip-socket-address / tuple-of-resources).");

            // For [Flags] enum returns: precompute the wire
            // byte width based on declared flag count (1-8 →
            // 1 byte, 9-16 → 2, 17-32 → 4). Per WIT spec
            // "alignment(flags) = sizeof(flags)".
            int flagsByteWidth = 0;
            if (retShape == 15)
            {
                int flagCount = 0;
                foreach (var v in Enum.GetValues(rt))
                    if (Convert.ToUInt64(v) != 0)
                        flagCount++;
                flagsByteWidth = flagCount <= 8 ? 1
                    : flagCount <= 16 ? 2 : 4;
            }

            // For tuple-of-resources: precompute the per-field
            // resource tables so the hot path doesn't reflect.
            FieldInfo[]? tupleFields = null;
            ResourceTable[]? tupleTables = null;
            if (retShape == 10)
            {
                if (resources == null)
                    throw new InvalidOperationException(
                        "[WasiStreamResult] tuple-of-resources "
                        + "return on '" + m.Name + "' requires "
                        + "a ResourceContext.");
                tupleFields = rt.GetFields(
                    BindingFlags.Public | BindingFlags.Instance);
                tupleTables = new ResourceTable[tupleFields.Length];
                for (int i = 0; i < tupleFields.Length; i++)
                    tupleTables[i] = resources.TableFor(
                        tupleFields[i].FieldType);
            }

            // For record-of-prims: precompute field offsets +
            // total alignment so the hot path doesn't reflect.
            FieldInfo[]? recordFields = null;
            int[]? recordOffsets = null;
            int recordAlign = 1;
            int recordPayloadOffset = 0;
            if (retShape == 8)
            {
                recordFields = rt.GetFields(
                    BindingFlags.Public | BindingFlags.Instance);
                recordOffsets = new int[recordFields.Length];
                int off = 0;
                foreach (var f in recordFields)
                {
                    var sz = PrimitiveByteSize(f.FieldType);
                    if (sz > recordAlign) recordAlign = sz;
                }
                for (int i = 0; i < recordFields.Length; i++)
                {
                    var sz = PrimitiveByteSize(recordFields[i].FieldType);
                    off = AlignUp(off, sz);
                    recordOffsets[i] = off;
                    off += sz;
                }
                recordPayloadOffset = AlignUp(1, recordAlign);
            }

            Wacs.Core.Runtime.Delegates.GenericFuncs? cabiRealloc = null;
            int Allocate(int align, int size)
            {
                if (cabiRealloc == null)
                {
                    if (!runtime.TryGetExportedFunction(
                            "cabi_realloc", out var addr))
                        throw new InvalidOperationException(
                            "Component does not export "
                            + "cabi_realloc — required for "
                            + "stream-result methods with "
                            + "list payload.");
                    cabiRealloc = runtime.CreateInvoker(
                        addr, new InvokerOptions());
                }
                return cabiRealloc(0, 0, align, size)[0].Data.Int32;
            }

            // Common payload writer — disc=0 + Ok payload bytes
            // at natural alignment.
            void WriteOk(byte[] memOut, int retAreaPtr, object? ret)
            {
                memOut[retAreaPtr] = 0;
                memOut[retAreaPtr + 1] = 0;
                memOut[retAreaPtr + 2] = 0;
                memOut[retAreaPtr + 3] = 0;
                switch (retShape)
                {
                    case 1:   // ulong at offset 8
                        memOut[retAreaPtr + 4] = 0;
                        memOut[retAreaPtr + 5] = 0;
                        memOut[retAreaPtr + 6] = 0;
                        memOut[retAreaPtr + 7] = 0;
                        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(
                            memOut.AsSpan(retAreaPtr + 8, 8), (ulong)ret!);
                        break;
                    case 2:   // byte[] (ptr, len) at offset 4
                        var bs = (byte[])ret!;
                        int dPtr = bs.Length == 0 ? 0
                            : Allocate(1, bs.Length);
                        if (bs.Length > 0)
                            Array.Copy(bs, 0, memOut, dPtr, bs.Length);
                        WriteI32LE(memOut, retAreaPtr + 4, dPtr);
                        WriteI32LE(memOut, retAreaPtr + 8, bs.Length);
                        break;
                    case 3:   // bool at offset 1
                        memOut[retAreaPtr + 1] = (bool)ret! ? (byte)1 : (byte)0;
                        break;
                    case 4:   // u8 at offset 1
                        memOut[retAreaPtr + 1] = (byte)ret!;
                        break;
                    case 5:   // u32 at offset 4
                        WriteI32LE(memOut, retAreaPtr + 4,
                            (int)(uint)ret!);
                        break;
                    case 6:   // u16 at offset 2
                        ushort v = (ushort)ret!;
                        memOut[retAreaPtr + 2] = (byte)(v & 0xff);
                        memOut[retAreaPtr + 3] = (byte)((v >> 8) & 0xff);
                        break;
                    case 7:   // (byte[], bool): list at +4, eof at +12
                        var tup = (ValueTuple<byte[], bool>)ret!;
                        var bs2 = tup.Item1;
                        int dPtr2 = bs2.Length == 0 ? 0
                            : Allocate(1, bs2.Length);
                        if (bs2.Length > 0)
                            Array.Copy(bs2, 0, memOut, dPtr2, bs2.Length);
                        WriteI32LE(memOut, retAreaPtr + 4, dPtr2);
                        WriteI32LE(memOut, retAreaPtr + 8, bs2.Length);
                        memOut[retAreaPtr + 12] = tup.Item2 ? (byte)1 : (byte)0;
                        break;
                    case 8:   // record-of-primitives: fields at
                              // retArea + recordPayloadOffset + recordOffsets[i]
                        for (int i = 0; i < recordFields!.Length; i++)
                        {
                            var fv = recordFields[i].GetValue(ret)!;
                            WritePrimitiveLE(memOut,
                                retAreaPtr + recordPayloadOffset
                                    + recordOffsets![i],
                                recordFields[i].FieldType, fv);
                        }
                        break;
                    case 9:   // IpSocketAddress: variant at retArea+4
                              // Layout: variant disc(1) + 3 pad +
                              // 28-byte payload at retArea+8.
                        WriteIpSocketAddressAt(memOut, retAreaPtr + 4,
                            (Wacs.WASI.Preview2.Sockets.IpSocketAddress)ret!);
                        break;
                    case 10:  // tuple-of-resources: each Item_i is
                              // allocated in its table; handles
                              // written as i32 at retArea+4+i*4.
                        for (int i = 0; i < tupleFields!.Length; i++)
                        {
                            var resInst = tupleFields[i].GetValue(ret);
                            int handle = tupleTables![i].Allocate(resInst!);
                            WriteI32LE(memOut, retAreaPtr + 4 + i * 4,
                                handle);
                        }
                        break;
                    case 15:  // [Flags] enum: 1, 2, or 4 bytes
                              // wire — payload at offset =
                              // flagsByteWidth (= max(1, width)).
                              // result wrapper align = width.
                        ulong flagsVal = Convert.ToUInt64(ret);
                        if (flagsByteWidth == 1)
                        {
                            memOut[retAreaPtr + 1] = (byte)flagsVal;
                        }
                        else if (flagsByteWidth == 2)
                        {
                            ushort fv16 = (ushort)flagsVal;
                            memOut[retAreaPtr + 2] = (byte)(fv16 & 0xff);
                            memOut[retAreaPtr + 3] = (byte)((fv16 >> 8) & 0xff);
                        }
                        else
                        {
                            WriteI32LE(memOut, retAreaPtr + 4,
                                (int)(uint)flagsVal);
                        }
                        break;
                    case 14:  // descriptor-stat: 96-byte record.
                              // Layout (relative to retArea):
                              //   +0: outer disc (Ok)
                              //   +1..+7: padding (align 8)
                              //   +8: type (u8)
                              //   +16: link-count (u64)
                              //   +24: size (u64)
                              //   +32: data-access-timestamp (24)
                              //   +56: data-mod-timestamp (24)
                              //   +80: status-change-timestamp (24)
                              // option<datetime> (24 bytes, align 8):
                              //   +0: option disc
                              //   +8: seconds (u64)
                              //   +16: nanoseconds (u32)
                        var st = (Wacs.WASI.Preview2.Filesystem
                            .DescriptorStat)ret!;
                        memOut[retAreaPtr + 8] = (byte)st.Type;
                        System.Buffers.Binary.BinaryPrimitives
                            .WriteUInt64LittleEndian(
                                memOut.AsSpan(retAreaPtr + 16, 8),
                                st.LinkCount);
                        System.Buffers.Binary.BinaryPrimitives
                            .WriteUInt64LittleEndian(
                                memOut.AsSpan(retAreaPtr + 24, 8),
                                st.Size);
                        WriteOptionalDatetime(memOut, retAreaPtr + 32,
                            st.DataAccessTimestamp);
                        WriteOptionalDatetime(memOut, retAreaPtr + 56,
                            st.DataModificationTimestamp);
                        WriteOptionalDatetime(memOut, retAreaPtr + 80,
                            st.StatusChangeTimestamp);
                        break;
                    case 13:  // option<directory-entry>: 20-byte total.
                              // Layout:
                              //   +0: outer disc (0=Ok)
                              //   +1..+3: padding
                              //   +4: option disc (0=None, 1=Some)
                              //   +5..+7: padding
                              //   +8: type (u8)
                              //   +9..+11: padding (string aligns 4)
                              //   +12: name ptr (i32)
                              //   +16: name len (i32)
                        var de = (Wacs.WASI.Preview2.Filesystem
                            .DirectoryEntry?)ret;
                        if (de == null)
                        {
                            memOut[retAreaPtr + 4] = 0;   // None
                        }
                        else
                        {
                            memOut[retAreaPtr + 4] = 1;   // Some
                            memOut[retAreaPtr + 8] = (byte)de.Type;
                            var nameBytes = System.Text.Encoding.UTF8
                                .GetBytes(de.Name);
                            int namePtr = nameBytes.Length == 0 ? 0
                                : Allocate(1, nameBytes.Length);
                            if (nameBytes.Length > 0)
                                Array.Copy(nameBytes, 0, memOut,
                                    namePtr, nameBytes.Length);
                            WriteI32LE(memOut, retAreaPtr + 12, namePtr);
                            WriteI32LE(memOut, retAreaPtr + 16,
                                nameBytes.Length);
                        }
                        break;
                    case 12:  // list<incoming-datagram>: each elem
                              // is 40 bytes (data list + variant);
                              // (list-ptr, list-len) at retArea+4/+8.
                        var dgs = (Wacs.WASI.Preview2.Sockets
                            .IncomingDatagram[])ret!;
                        int dgsCount = dgs.Length;
                        int dgsArrayPtr = dgsCount == 0 ? 0
                            : Allocate(4, dgsCount * 40);
                        for (int i = 0; i < dgsCount; i++)
                        {
                            var dg = dgs[i];
                            int elemBase = dgsArrayPtr + i * 40;
                            // data: list<u8> at elemBase+0,+4
                            int dataPtr = dg.Data.Length == 0 ? 0
                                : Allocate(1, dg.Data.Length);
                            if (dg.Data.Length > 0)
                                Array.Copy(dg.Data, 0, memOut,
                                    dataPtr, dg.Data.Length);
                            WriteI32LE(memOut, elemBase, dataPtr);
                            WriteI32LE(memOut, elemBase + 4,
                                dg.Data.Length);
                            // remote-address: ip-socket-address
                            // (32 bytes) at elemBase+8.
                            WriteIpSocketAddressAt(memOut,
                                elemBase + 8, dg.RemoteAddress);
                        }
                        WriteI32LE(memOut, retAreaPtr + 4, dgsArrayPtr);
                        WriteI32LE(memOut, retAreaPtr + 8, dgsCount);
                        break;
                    case 11:  // option<ip-address>: 22-byte total.
                              // Layout:
                              //   +0: outer disc (0=Ok)
                              //   +1: padding
                              //   +2: option disc (0=None, 1=Some)
                              //   +3: padding
                              //   +4: variant disc (0=ipv4, 1=ipv6)
                              //   +5: padding
                              //   +6..+21: variant payload
                        // outer disc=0 already written above.
                        var ipa = (Wacs.WASI.Preview2.Sockets
                            .IpAddressEnumerationItem?)ret;
                        if (ipa == null)
                        {
                            memOut[retAreaPtr + 2] = 0;   // option None
                        }
                        else
                        {
                            memOut[retAreaPtr + 2] = 1;   // option Some
                            memOut[retAreaPtr + 3] = 0;
                            if (ipa is Wacs.WASI.Preview2.Sockets.Ipv4Address v4)
                            {
                                memOut[retAreaPtr + 4] = 0;
                                memOut[retAreaPtr + 5] = 0;
                                memOut[retAreaPtr + 6] = v4.Address[0];
                                memOut[retAreaPtr + 7] = v4.Address[1];
                                memOut[retAreaPtr + 8] = v4.Address[2];
                                memOut[retAreaPtr + 9] = v4.Address[3];
                            }
                            else if (ipa is Wacs.WASI.Preview2.Sockets.Ipv6Address v6)
                            {
                                memOut[retAreaPtr + 4] = 1;
                                memOut[retAreaPtr + 5] = 0;
                                for (int i = 0; i < 8; i++)
                                {
                                    memOut[retAreaPtr + 6 + i * 2]
                                        = (byte)(v6.Address[i] & 0xff);
                                    memOut[retAreaPtr + 7 + i * 2]
                                        = (byte)((v6.Address[i] >> 8) & 0xff);
                                }
                            }
                            else
                            {
                                throw new ArgumentException(
                                    "Unknown IpAddressEnumerationItem "
                                    + "subclass: " + ipa.GetType());
                            }
                        }
                        break;
                }
            }

            // Unified body: (ctx, handle, dataPtr, len,
            // hostArgs[primitives only], retArea). dataPtr/len
            // are 0/0 when no byte[] param is present;
            // hostArgs holds the converted primitive params in
            // host-type form (the expression tree below builds
            // it from the wire-typed lambda params).
            void Body(ExecContext ctx, int handle, int dataPtr, int len,
                object?[] primArgs, int retAreaPtr)
            {
                var inst = table.Get(handle);
                var memory = ctx.DefaultMemory.Data;
                object?[] hostArgs;
                if (hasBytesParam)
                {
                    var bytes = new byte[len];
                    if (len > 0) Array.Copy(memory, dataPtr, bytes, 0, len);
                    hostArgs = new object?[primArgs.Length + 1];
                    hostArgs[0] = bytes;
                    for (int i = 0; i < primArgs.Length; i++)
                        hostArgs[i + 1] = primArgs[i];
                }
                else
                {
                    hostArgs = primArgs;
                }
                var ret = m.Invoke(inst, hostArgs);
                WriteOk(memory, retAreaPtr, ret);
            }

            var lambdaParams = new ParameterExpression[wireParamTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            lambdaParams[1] = Expression.Parameter(typeof(int), "self");
            // Compute lambda param names per host param — byte[]
            // expands to (ptr, len), option<ip-socket-address>
            // expands to 13 slots, others to 1 slot.
            int idx = 2;
            ParameterExpression dataPtrParam = Expression.Parameter(typeof(int), "dataPtr");
            ParameterExpression lenParam = Expression.Parameter(typeof(int), "len");
            foreach (var p in paramInfos)
            {
                if (p.ParameterType == typeof(byte[]))
                {
                    lambdaParams[idx++] = dataPtrParam;
                    lambdaParams[idx++] = lenParam;
                }
                else if (p.ParameterType == typeof(string))
                {
                    lambdaParams[idx++] = Expression.Parameter(
                        typeof(int), p.Name + "_strPtr");
                    lambdaParams[idx++] = Expression.Parameter(
                        typeof(int), p.Name + "_strLen");
                }
                else if (IsOptionalIpAddrParam(p))
                {
                    lambdaParams[idx++] = Expression.Parameter(
                        typeof(int), p.Name + "_optDisc");
                    lambdaParams[idx++] = Expression.Parameter(
                        typeof(int), p.Name + "_disc");
                    for (int k = 1; k <= 11; k++)
                        lambdaParams[idx++] = Expression.Parameter(
                            typeof(int), p.Name + "_s" + k);
                }
                else if (IsOutgoingDatagramArrayParam(p))
                {
                    lambdaParams[idx++] = Expression.Parameter(
                        typeof(int), p.Name + "_listPtr");
                    lambdaParams[idx++] = Expression.Parameter(
                        typeof(int), p.Name + "_listLen");
                }
                else
                {
                    lambdaParams[idx++] = Expression.Parameter(
                        wireParamTypes[idx - 1], p.Name);
                }
            }
            var retAreaParam = Expression.Parameter(typeof(int), "retAreaPtr");
            lambdaParams[wireParamTypes.Length - 1] = retAreaParam;

            // Build host-arg array — converts each wire param
            // group back to its host type. Borrow<resource>
            // looks up via ResourceContext; option<ip-socket-
            // address> calls DecodeOptionalIpSocketAddress;
            // byte[] is handled outside this array (passed via
            // dataPtr/len directly to Body).
            var hasBorrowParam = paramInfos.Any(p =>
                p.ParameterType.GetCustomAttribute<
                    WasiResourceAttribute>() != null);
            var hasOptionalIpAddr = paramInfos.Any(IsOptionalIpAddrParam);
            if ((hasBorrowParam || hasOptionalIpAddr) && resources == null)
                throw new InvalidOperationException(
                    "[WasiStreamResult] method '" + m.Name
                    + "' takes a resource-typed param but no "
                    + "ResourceContext was supplied.");
            var contextConst = (hasBorrowParam || hasOptionalIpAddr)
                ? (Expression)Expression.Constant(resources!)
                : Expression.Constant(null, typeof(ResourceContext));
            var tableForMethod = typeof(ResourceContext).GetMethod(
                nameof(ResourceContext.TableFor))!;
            var getResourceMethod = typeof(ResourceTable).GetMethod(
                nameof(ResourceTable.Get))!;
            var decodeOptIpAddrMethod = typeof(WasiInterfaceBinder)
                .GetMethod(nameof(DecodeOptionalIpSocketAddressFromFlatWire),
                    BindingFlags.Static | BindingFlags.NonPublic)!;
            var decodeOutDgArrayMethod = typeof(WasiInterfaceBinder)
                .GetMethod(nameof(DecodeOutgoingDatagramArray),
                    BindingFlags.Static | BindingFlags.NonPublic)!;
            var memoryPropForBody = typeof(ExecContext).GetProperty(
                nameof(ExecContext.DefaultMemory))!;
            var memDataFieldForBody = typeof(Wacs.Core.Runtime.Types
                .MemoryInstance).GetField("Data")!;
            var memoryAccessForBody = Expression.Field(
                Expression.Property(lambdaParams[0], memoryPropForBody),
                memDataFieldForBody);

            // Walk lambda params per host-param slot count to
            // build per-host-param expressions. byte[] params
            // skip the host-arg array (Body uses dataPtr/len).
            int slotIdx = 2;   // after ctx + self
            int hostArgCount = paramInfos.Count(p =>
                p.ParameterType != typeof(byte[]));
            var primExprs = new Expression[hostArgCount];
            int hostIdx = 0;
            foreach (var p in paramInfos)
            {
                if (p.ParameterType == typeof(byte[]))
                {
                    slotIdx += 2;
                    continue;
                }
                if (IsOptionalIpAddrParam(p))
                {
                    var slotArgs = new Expression[13];
                    for (int k = 0; k < 13; k++)
                        slotArgs[k] = lambdaParams[slotIdx + k];
                    primExprs[hostIdx++] = Expression.Convert(
                        Expression.Call(decodeOptIpAddrMethod, slotArgs),
                        typeof(object));
                    slotIdx += 13;
                    continue;
                }
                if (IsOutgoingDatagramArrayParam(p))
                {
                    var listPtrParam = lambdaParams[slotIdx];
                    var listLenParam = lambdaParams[slotIdx + 1];
                    primExprs[hostIdx++] = Expression.Convert(
                        Expression.Call(decodeOutDgArrayMethod,
                            memoryAccessForBody,
                            listPtrParam, listLenParam),
                        typeof(object));
                    slotIdx += 2;
                    continue;
                }
                if (p.ParameterType == typeof(string))
                {
                    var ptrParam = lambdaParams[slotIdx];
                    var lenParam2 = lambdaParams[slotIdx + 1];
                    var encUtf8 = Expression.Property(null,
                        typeof(System.Text.Encoding).GetProperty(
                            nameof(System.Text.Encoding.UTF8))!);
                    var getStr = typeof(System.Text.Encoding).GetMethod(
                        nameof(System.Text.Encoding.GetString),
                        new[] { typeof(byte[]), typeof(int), typeof(int) })!;
                    primExprs[hostIdx++] = Expression.Convert(
                        Expression.Call(encUtf8, getStr,
                            memoryAccessForBody, ptrParam, lenParam2),
                        typeof(object));
                    slotIdx += 2;
                    continue;
                }
                Expression e = lambdaParams[slotIdx++];
                if (p.ParameterType.GetCustomAttribute<
                    WasiResourceAttribute>() != null)
                {
                    var paramTable = Expression.Call(contextConst,
                        tableForMethod,
                        Expression.Constant(p.ParameterType));
                    e = Expression.Convert(
                        Expression.Call(paramTable, getResourceMethod, e),
                        p.ParameterType);
                }
                else if (p.ParameterType != e.Type)
                {
                    if (p.ParameterType == typeof(bool))
                        e = Expression.NotEqual(e, Expression.Constant(0, e.Type));
                    else
                        e = Expression.Convert(e, p.ParameterType);
                }
                primExprs[hostIdx++] = Expression.Convert(e, typeof(object));
            }
            var primArr = hostArgCount == 0
                ? Expression.NewArrayBounds(typeof(object),
                    Expression.Constant(0))
                : (Expression)Expression.NewArrayInit(typeof(object), primExprs);

            var bodyTarget = Expression.Constant(
                (Action<ExecContext, int, int, int, object?[], int>)Body);
            var call = Expression.Invoke(bodyTarget,
                lambdaParams[0], lambdaParams[1],
                hasBytesParam ? (Expression)dataPtrParam
                              : Expression.Constant(0),
                hasBytesParam ? (Expression)lenParam
                              : Expression.Constant(0),
                primArr,
                retAreaParam);
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

        /// <summary>Resource method tagged
        /// [WasiErrorResult] returning another resource —
        /// result&lt;own&lt;TReturn&gt;, error-code&gt;. The
        /// canonical use is filesystem bridge methods like
        /// descriptor.read-via-stream returning
        /// own&lt;input-stream&gt;. Wire shape: (handle, ...primitive
        /// params, retAreaPtr) → void; retArea: 1-byte outer
        /// disc + 3-byte padding + 4-byte handle (when Ok).
        /// Always-Ok semantics in v0.</summary>
        private static void BindErrorResultResourceMethodReturningResource(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable selfTable,
            Type resourceType, MethodInfo m, ResourceContext resources)
        {
            var paramInfos = m.GetParameters();
            // Param shape: each param is either primitive /
            // enum (1 wire slot — flags-typed C# enums map to
            // their underlying integer through ToWireType) or
            // string (2 wire slots: ptr + len). Other aggregates
            // (records, lists) stay deferred.
            foreach (var p in paramInfos)
                if (!IsPrimitive(p.ParameterType)
                    && !p.ParameterType.IsEnum
                    && p.ParameterType != typeof(string))
                    throw new InvalidOperationException(
                        "[WasiErrorResult] resource method '" + m.Name
                        + "' takes unsupported param type "
                        + p.ParameterType + ".");

            // Compute wire-slot count: 1 (handle) + per-param
            // (1 for primitive, 2 for string) + 1 (retArea).
            int paramSlotCount = 0;
            foreach (var p in paramInfos)
                paramSlotCount += p.ParameterType == typeof(string) ? 2 : 1;
            var wireParamTypes = new Type[3 + paramSlotCount];
            wireParamTypes[0] = typeof(ExecContext);
            wireParamTypes[1] = typeof(int);   // handle
            int wi = 2;
            foreach (var p in paramInfos)
            {
                if (p.ParameterType == typeof(string))
                {
                    wireParamTypes[wi++] = typeof(int);   // ptr
                    wireParamTypes[wi++] = typeof(int);   // len
                }
                else
                {
                    wireParamTypes[wi++] = ToWireType(p.ParameterType);
                }
            }
            wireParamTypes[wi] = typeof(int);   // retArea

            var delegateType = OpenActionType(wireParamTypes.Length)
                .MakeGenericType(wireParamTypes);

            var returnTable = resources.TableFor(m.ReturnType);

            // Body takes pre-built hostArgs[] from the lambda;
            // string params are decoded from (ptr, len) before
            // the call.
            void Body(ExecContext ctx, int handle, object?[] hostArgs,
                int retAreaPtr)
            {
                var inst = selfTable.Get(handle);
                var ret = m.Invoke(inst, hostArgs);
                if (ret == null)
                    throw new InvalidOperationException(
                        "[WasiErrorResult] resource method '" + m.Name
                        + "' returned null — Ok payload must "
                        + "be non-null.");
                var memory = ctx.DefaultMemory.Data;
                var retHandle = returnTable.Allocate(ret);
                memory[retAreaPtr] = 0;
                memory[retAreaPtr + 1] = 0;
                memory[retAreaPtr + 2] = 0;
                memory[retAreaPtr + 3] = 0;
                WriteI32LE(memory, retAreaPtr + 4, retHandle);
            }

            var lambdaParams = new ParameterExpression[wireParamTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            lambdaParams[1] = Expression.Parameter(typeof(int), "self");
            wi = 2;
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (paramInfos[i].ParameterType == typeof(string))
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_ptr");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_len");
                }
                else
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        wireParamTypes[wi - 1], paramInfos[i].Name);
                }
            }
            var retAreaParam = Expression.Parameter(typeof(int), "retAreaPtr");
            lambdaParams[wireParamTypes.Length - 1] = retAreaParam;

            // Build object?[] of host args. String params:
            // call helper to decode (ptr, len). Primitive
            // params: convert wire→host.
            var ctxParam = lambdaParams[0];
            var memoryProp = typeof(ExecContext).GetProperty(
                nameof(ExecContext.DefaultMemory))!;
            var memDataField = typeof(Wacs.Core.Runtime.Types.MemoryInstance)
                .GetField("Data")!;
            var memoryAccess = Expression.Field(
                Expression.Property(ctxParam, memoryProp),
                memDataField);
            var encodingUtf8 = Expression.Property(null,
                typeof(System.Text.Encoding).GetProperty(
                    nameof(System.Text.Encoding.UTF8))!);
            var getStringMethod = typeof(System.Text.Encoding).GetMethod(
                nameof(System.Text.Encoding.GetString),
                new[] { typeof(byte[]), typeof(int), typeof(int) })!;

            wi = 2;
            var argExprs = new Expression[paramInfos.Length];
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (paramInfos[i].ParameterType == typeof(string))
                {
                    var ptrParam = lambdaParams[wi++];
                    var lenParam = lambdaParams[wi++];
                    // Encoding.UTF8.GetString(memory, ptr, len)
                    argExprs[i] = Expression.Convert(
                        Expression.Call(encodingUtf8, getStringMethod,
                            memoryAccess, ptrParam, lenParam),
                        typeof(object));
                }
                else
                {
                    Expression e = lambdaParams[wi++];
                    if (paramInfos[i].ParameterType != e.Type)
                    {
                        if (paramInfos[i].ParameterType == typeof(bool))
                            e = Expression.NotEqual(e,
                                Expression.Constant(0, e.Type));
                        else
                            e = Expression.Convert(e,
                                paramInfos[i].ParameterType);
                    }
                    argExprs[i] = Expression.Convert(e, typeof(object));
                }
            }

            var argArr = Expression.NewArrayInit(typeof(object), argExprs);
            var bodyTarget = Expression.Constant(
                (Action<ExecContext, int, object?[], int>)Body);
            var call = Expression.Invoke(bodyTarget,
                lambdaParams[0], lambdaParams[1], argArr, retAreaParam);
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

        /// <summary>Resource method whose return is one of:
        /// (a) [WasiErrorResult] void — result&lt;_, error-code&gt;
        /// canon-lowered through retArea; wire form (handle,
        /// ...params, retAreaPtr) → void.
        /// (b) [WasiUnitResult] void — result&lt;_, _&gt; canon-
        /// lowered as flat-return i32; wire form (handle,
        /// ...params) → i32 (always 0 in v0; Ok disc).
        /// The two shapes share param decoding — only the
        /// trailing wire slot differs (retArea param vs i32
        /// return).</summary>
        private static void BindErrorResultVoidReturnResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable selfTable,
            Type resourceType, MethodInfo m,
            ResourceContext? resources = null,
            bool unitResultFlatReturn = false)
        {
            var paramInfos = m.GetParameters();
            // Each param: primitive / enum (1 wire slot),
            // string (2 wire slots: ptr + len),
            // borrow<resource> (1 wire slot: handle resolved
            // via ResourceContext),
            // option<own<resource>> (2 wire slots: option
            // disc + handle, tagged [WasiOptionalParam]),
            // IpSocketAddress (12
            // wire slots — variant flat-lowering: 1 disc +
            // 11 joined-case payload slots, all i32),
            // NewTimestamp (3 wire slots: variant disc +
            // datetime seconds(i64) + nanoseconds(i32)),
            // AccessType (2 wire slots: variant disc + modes),
            // byte[] (2 wire slots: ptr + len), or
            // byte[][] (2 wire slots: list-ptr + list-len,
            // per-elem 8 bytes ptr+len decoded in body).
            // Aggregates (records, lists) stay deferred.
            bool IsOptResource(ParameterInfo p)
                => p.GetCustomAttribute<WasiOptionalParamAttribute>() != null
                   && p.ParameterType.GetCustomAttribute<
                       WasiResourceAttribute>() != null;
            // option<primitive> param — Nullable<T> with
            // [WasiOptionalParam]. Wire: 2 slots (option disc
            // + value), where value's wire type matches the
            // underlying primitive (i32 / i64 / f32 / f64).
            bool IsOptPrimitive(ParameterInfo p)
                => p.GetCustomAttribute<WasiOptionalParamAttribute>() != null
                   && p.ParameterType.IsGenericType
                   && p.ParameterType.GetGenericTypeDefinition()
                       == typeof(Nullable<>)
                   && IsPrimitive(p.ParameterType
                       .GetGenericArguments()[0]);
            // option<string> param — string with
            // [WasiOptionalParam]. Wire: 3 slots (disc i32 +
            // string ptr i32 + string len i32).
            bool IsOptString(ParameterInfo p)
                => p.GetCustomAttribute<WasiOptionalParamAttribute>() != null
                   && p.ParameterType == typeof(string);
            // HttpMethod / HttpScheme variant param. Wire:
            // 3 slots (disc + joined-flat ptr + len).
            bool IsHttpMethodParam(ParameterInfo p)
                => p.ParameterType ==
                    typeof(Wacs.WASI.Preview2.Http.HttpMethod)
                   || (p.ParameterType
                       .IsSubclassOf(typeof(
                           Wacs.WASI.Preview2.Http.HttpMethod)));
            bool IsHttpSchemeParam(ParameterInfo p)
                => p.ParameterType ==
                    typeof(Wacs.WASI.Preview2.Http.HttpScheme)
                   || (p.ParameterType
                       .IsSubclassOf(typeof(
                           Wacs.WASI.Preview2.Http.HttpScheme)));
            foreach (var p in paramInfos)
                if (!IsPrimitive(p.ParameterType)
                    && !p.ParameterType.IsEnum
                    && p.ParameterType != typeof(string)
                    && p.ParameterType != typeof(byte[])
                    && p.ParameterType != typeof(byte[][])
                    && p.ParameterType.GetCustomAttribute<
                        WasiResourceAttribute>() == null
                    && !IsOptResource(p)
                    && !IsOptPrimitive(p)
                    && !IsOptString(p)
                    && !IsHttpMethodParam(p)
                    && !IsHttpSchemeParam(p)
                    && p.ParameterType != typeof(
                        Wacs.WASI.Preview2.Sockets.IpSocketAddress)
                    && p.ParameterType != typeof(
                        Wacs.WASI.Preview2.Filesystem.NewTimestamp)
                    && p.ParameterType != typeof(
                        Wacs.WASI.Preview2.Filesystem.AccessType))
                    throw new InvalidOperationException(
                        "[WasiErrorResult] void-return resource "
                        + "method '" + m.Name + "' takes "
                        + "unsupported param type "
                        + p.ParameterType + ".");

            int paramSlotCount = 0;
            foreach (var p in paramInfos)
            {
                if (IsOptString(p)
                    || IsHttpMethodParam(p)
                    || IsHttpSchemeParam(p))
                    paramSlotCount += 3;
                else if (p.ParameterType == typeof(string)
                    || p.ParameterType == typeof(byte[])
                    || p.ParameterType == typeof(byte[][])
                    || IsOptResource(p)
                    || IsOptPrimitive(p))
                    paramSlotCount += 2;
                else if (p.ParameterType == typeof(
                    Wacs.WASI.Preview2.Sockets.IpSocketAddress))
                    paramSlotCount += 12;
                else if (p.ParameterType == typeof(
                    Wacs.WASI.Preview2.Filesystem.NewTimestamp))
                    paramSlotCount += 3;
                else if (p.ParameterType == typeof(
                    Wacs.WASI.Preview2.Filesystem.AccessType))
                    paramSlotCount += 2;
                else
                    paramSlotCount += 1;
            }
            var wireParamTypes = new Type[3 + paramSlotCount];
            wireParamTypes[0] = typeof(ExecContext);
            wireParamTypes[1] = typeof(int);
            int wi = 2;
            foreach (var p in paramInfos)
            {
                if (IsOptString(p)
                    || IsHttpMethodParam(p)
                    || IsHttpSchemeParam(p))
                {
                    wireParamTypes[wi++] = typeof(int);   // disc
                    wireParamTypes[wi++] = typeof(int);   // ptr
                    wireParamTypes[wi++] = typeof(int);   // len
                }
                else if (p.ParameterType == typeof(string)
                    || p.ParameterType == typeof(byte[])
                    || p.ParameterType == typeof(byte[][]))
                {
                    wireParamTypes[wi++] = typeof(int);   // ptr
                    wireParamTypes[wi++] = typeof(int);   // len
                }
                else if (IsOptResource(p))
                {
                    wireParamTypes[wi++] = typeof(int);   // option disc
                    wireParamTypes[wi++] = typeof(int);   // handle
                }
                else if (IsOptPrimitive(p))
                {
                    // option<primitive>: disc (i32) + payload
                    // (wire type of underlying primitive).
                    wireParamTypes[wi++] = typeof(int);
                    wireParamTypes[wi++] = ToWireType(
                        p.ParameterType.GetGenericArguments()[0]);
                }
                else if (p.ParameterType.GetCustomAttribute<
                    WasiResourceAttribute>() != null)
                {
                    wireParamTypes[wi++] = typeof(int);   // borrow handle
                }
                else if (p.ParameterType == typeof(
                    Wacs.WASI.Preview2.Sockets.IpSocketAddress))
                {
                    // Variant flat-lowering: 1 disc + 11 joined
                    // payload slots, all i32.
                    for (int k = 0; k < 12; k++)
                        wireParamTypes[wi++] = typeof(int);
                }
                else if (p.ParameterType == typeof(
                    Wacs.WASI.Preview2.Filesystem.NewTimestamp))
                {
                    // Variant flat-lowering: 1 disc + 2 joined
                    // payload slots (i64 seconds, i32 nanos).
                    wireParamTypes[wi++] = typeof(int);   // disc
                    wireParamTypes[wi++] = typeof(long);  // seconds
                    wireParamTypes[wi++] = typeof(int);   // nanoseconds
                }
                else if (p.ParameterType == typeof(
                    Wacs.WASI.Preview2.Filesystem.AccessType))
                {
                    // Variant flat-lowering: 1 disc + 1 modes
                    // payload slot (i32, ignored when exists).
                    wireParamTypes[wi++] = typeof(int);   // disc
                    wireParamTypes[wi++] = typeof(int);   // modes
                }
                else
                {
                    wireParamTypes[wi++] = ToWireType(p.ParameterType);
                }
            }
            // Last wireParamTypes slot is either the retArea
            // i32 (Action shape) or the i32 return (Func shape).
            // Both are typed i32; the difference only shows up
            // when constructing the delegate type.
            wireParamTypes[wi] = typeof(int);

            Type delegateType = unitResultFlatReturn
                ? OpenFuncType(wireParamTypes.Length)
                    .MakeGenericType(wireParamTypes)
                : OpenActionType(wireParamTypes.Length)
                    .MakeGenericType(wireParamTypes);

            // Action body: writes Ok disc to retArea memory.
            void ActionBody(ExecContext ctx, int handle,
                object?[] hostArgs, int retAreaPtr)
            {
                var inst = selfTable.Get(handle);
                m.Invoke(inst, hostArgs);
                var memory = ctx.DefaultMemory.Data;
                memory[retAreaPtr] = 0;       // Ok
                memory[retAreaPtr + 1] = 0;   // padding
            }

            // Func body: invokes method; returns Ok disc as
            // the wire i32. retArea is not touched.
            int FuncBody(ExecContext _, int handle, object?[] hostArgs)
            {
                var inst = selfTable.Get(handle);
                m.Invoke(inst, hostArgs);
                return 0;   // Ok
            }

            // For Action: lambdaParams matches wireParamTypes
            // length (last entry is retAreaPtr input).
            // For Func: lambdaParams is one shorter (Func's
            // last wireParamTypes slot is the return, not an
            // input).
            int lambdaSlotCount = unitResultFlatReturn
                ? wireParamTypes.Length - 1
                : wireParamTypes.Length;
            var lambdaParams = new ParameterExpression[lambdaSlotCount];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            lambdaParams[1] = Expression.Parameter(typeof(int), "self");
            wi = 2;
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (IsOptString(paramInfos[i]))
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_optDisc");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_ptr");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_len");
                }
                else if (IsHttpMethodParam(paramInfos[i])
                    || IsHttpSchemeParam(paramInfos[i]))
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_disc");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_ptr");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_len");
                }
                else if (paramInfos[i].ParameterType == typeof(string)
                    || paramInfos[i].ParameterType == typeof(byte[])
                    || paramInfos[i].ParameterType == typeof(byte[][]))
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_ptr");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_len");
                }
                else if (IsOptResource(paramInfos[i]))
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_optDisc");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_handle");
                }
                else if (IsOptPrimitive(paramInfos[i]))
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_optDisc");
                    lambdaParams[wi++] = Expression.Parameter(
                        ToWireType(paramInfos[i].ParameterType
                            .GetGenericArguments()[0]),
                        paramInfos[i].Name + "_value");
                }
                else if (paramInfos[i].ParameterType == typeof(
                    Wacs.WASI.Preview2.Sockets.IpSocketAddress))
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_disc");
                    for (int k = 1; k <= 11; k++)
                        lambdaParams[wi++] = Expression.Parameter(
                            typeof(int),
                            paramInfos[i].Name + "_s" + k);
                }
                else if (paramInfos[i].ParameterType == typeof(
                    Wacs.WASI.Preview2.Filesystem.NewTimestamp))
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_tsDisc");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(long), paramInfos[i].Name + "_tsSec");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_tsNanos");
                }
                else if (paramInfos[i].ParameterType == typeof(
                    Wacs.WASI.Preview2.Filesystem.AccessType))
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_atDisc");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_atModes");
                }
                else
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        wireParamTypes[wi - 1], paramInfos[i].Name);
                }
            }
            // Action shape: append retAreaPtr input. Func/unit-
            // result shape: skip — last wireParamTypes slot is
            // the i32 return, not an input.
            ParameterExpression? retAreaParam = null;
            if (!unitResultFlatReturn)
            {
                retAreaParam = Expression.Parameter(
                    typeof(int), "retAreaPtr");
                lambdaParams[wireParamTypes.Length - 1] = retAreaParam;
            }

            var ctxParam = lambdaParams[0];
            var memoryProp = typeof(ExecContext).GetProperty(
                nameof(ExecContext.DefaultMemory))!;
            var memDataField = typeof(Wacs.Core.Runtime.Types.MemoryInstance)
                .GetField("Data")!;
            var memoryAccess = Expression.Field(
                Expression.Property(ctxParam, memoryProp),
                memDataField);
            var encodingUtf8 = Expression.Property(null,
                typeof(System.Text.Encoding).GetProperty(
                    nameof(System.Text.Encoding.UTF8))!);
            var getStringMethod = typeof(System.Text.Encoding).GetMethod(
                nameof(System.Text.Encoding.GetString),
                new[] { typeof(byte[]), typeof(int), typeof(int) })!;

            // Resource params resolve via the per-component
            // ResourceContext (closure-captured). Without one
            // we can't decode borrow handles, so demand it
            // upfront. option<own<resource>> params also need
            // the table to resolve the Some-side handle.
            var hasResourceParam = paramInfos.Any(p =>
                p.ParameterType.GetCustomAttribute<
                    WasiResourceAttribute>() != null);
            if (hasResourceParam && resources == null)
                throw new InvalidOperationException(
                    "Resource method '" + m.Name + "' takes a "
                    + "borrow<resource> param but no "
                    + "ResourceContext was supplied.");
            var contextConst = hasResourceParam
                ? (Expression)Expression.Constant(resources!)
                : Expression.Constant(null, typeof(ResourceContext));
            var tableForMethod = typeof(ResourceContext).GetMethod(
                nameof(ResourceContext.TableFor))!;
            var getResourceMethod = typeof(ResourceTable).GetMethod(
                nameof(ResourceTable.Get))!;

            // Static helper for IpSocketAddress flat-wire decode.
            var decodeIpSocketAddrMethod = typeof(WasiInterfaceBinder)
                .GetMethod(nameof(DecodeIpSocketAddressFromFlatWire),
                    BindingFlags.Static | BindingFlags.NonPublic)!;

            wi = 2;
            var argExprs = new Expression[paramInfos.Length];
            // Cache the helper that copies a slice of memory
            // into a fresh byte[] (used for byte[] params).
            var sliceCopyMethod = typeof(WasiInterfaceBinder)
                .GetMethod(nameof(CopyMemorySlice),
                    BindingFlags.Static | BindingFlags.NonPublic)!;
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (IsOptString(paramInfos[i]))
                {
                    // option<string>: 3 wire slots (disc i32 +
                    // ptr i32 + len i32). disc==0 → null;
                    // disc==1 → UTF-8 decode (ptr, len). The
                    // string-decode happens unconditionally to
                    // simplify the expression; the conditional
                    // chooses null vs decoded.
                    var optDiscParam = lambdaParams[wi++];
                    var ptrParam = lambdaParams[wi++];
                    var lenParam = lambdaParams[wi++];
                    var decoded = Expression.Call(
                        encodingUtf8, getStringMethod,
                        memoryAccess, ptrParam, lenParam);
                    argExprs[i] = Expression.Convert(
                        Expression.Condition(
                            Expression.Equal(optDiscParam,
                                Expression.Constant(0)),
                            Expression.Constant(null,
                                typeof(string)),
                            decoded),
                        typeof(object));
                }
                else if (IsHttpMethodParam(paramInfos[i]))
                {
                    var decodeMeth = typeof(WasiInterfaceBinder)
                        .GetMethod(
                            nameof(DecodeHttpMethodFromFlatWire),
                            BindingFlags.Static
                                | BindingFlags.NonPublic)!;
                    var discParam = lambdaParams[wi++];
                    var ptrParam = lambdaParams[wi++];
                    var lenParam = lambdaParams[wi++];
                    argExprs[i] = Expression.Convert(
                        Expression.Call(decodeMeth,
                            memoryAccess, discParam,
                            ptrParam, lenParam),
                        typeof(object));
                }
                else if (IsHttpSchemeParam(paramInfos[i]))
                {
                    var decodeSch = typeof(WasiInterfaceBinder)
                        .GetMethod(
                            nameof(DecodeHttpSchemeFromFlatWire),
                            BindingFlags.Static
                                | BindingFlags.NonPublic)!;
                    var discParam = lambdaParams[wi++];
                    var ptrParam = lambdaParams[wi++];
                    var lenParam = lambdaParams[wi++];
                    argExprs[i] = Expression.Convert(
                        Expression.Call(decodeSch,
                            memoryAccess, discParam,
                            ptrParam, lenParam),
                        typeof(object));
                }
                else if (paramInfos[i].ParameterType == typeof(string))
                {
                    var ptrParam = lambdaParams[wi++];
                    var lenParam = lambdaParams[wi++];
                    argExprs[i] = Expression.Convert(
                        Expression.Call(encodingUtf8, getStringMethod,
                            memoryAccess, ptrParam, lenParam),
                        typeof(object));
                }
                else if (paramInfos[i].ParameterType == typeof(byte[]))
                {
                    var ptrParam = lambdaParams[wi++];
                    var lenParam = lambdaParams[wi++];
                    argExprs[i] = Expression.Convert(
                        Expression.Call(sliceCopyMethod,
                            memoryAccess, ptrParam, lenParam),
                        typeof(object));
                }
                else if (paramInfos[i].ParameterType == typeof(byte[][]))
                {
                    var decodeListMethod = typeof(WasiInterfaceBinder)
                        .GetMethod(nameof(DecodeByteArrayList),
                            BindingFlags.Static | BindingFlags.NonPublic)!;
                    var ptrParam = lambdaParams[wi++];
                    var lenParam = lambdaParams[wi++];
                    argExprs[i] = Expression.Convert(
                        Expression.Call(decodeListMethod,
                            memoryAccess, ptrParam, lenParam),
                        typeof(object));
                }
                else if (IsOptResource(paramInfos[i]))
                {
                    // option<own<resource>> param: 2 slots
                    // (option disc + handle). Resolve via
                    // table when disc==1, else null.
                    var optDiscParam = lambdaParams[wi++];
                    var handleParam = lambdaParams[wi++];
                    var paramTable = Expression.Call(contextConst,
                        tableForMethod,
                        Expression.Constant(paramInfos[i].ParameterType));
                    var resolved = Expression.Convert(
                        Expression.Call(paramTable, getResourceMethod,
                            handleParam),
                        paramInfos[i].ParameterType);
                    argExprs[i] = Expression.Convert(
                        Expression.Condition(
                            Expression.Equal(optDiscParam,
                                Expression.Constant(0)),
                            Expression.Constant(null,
                                paramInfos[i].ParameterType),
                            resolved),
                        typeof(object));
                }
                else if (IsOptPrimitive(paramInfos[i]))
                {
                    // option<primitive>: 2 wire slots (disc i32
                    // + value of primitive's wire type). Decode
                    // to Nullable<T>: disc==0 → null, else
                    // (T?)value.
                    var optDiscParam = lambdaParams[wi++];
                    var valueParam = lambdaParams[wi++];
                    var underlying = paramInfos[i].ParameterType
                        .GetGenericArguments()[0];
                    Expression valueExpr = valueParam;
                    if (valueExpr.Type != underlying)
                    {
                        if (underlying == typeof(bool))
                            valueExpr = Expression.NotEqual(
                                valueExpr,
                                Expression.Constant(0,
                                    valueExpr.Type));
                        else
                            valueExpr = Expression.Convert(
                                valueExpr, underlying);
                    }
                    var someExpr = Expression.New(
                        paramInfos[i].ParameterType
                            .GetConstructor(new[] { underlying })!,
                        valueExpr);
                    argExprs[i] = Expression.Convert(
                        Expression.Condition(
                            Expression.Equal(optDiscParam,
                                Expression.Constant(0)),
                            Expression.Constant(null,
                                paramInfos[i].ParameterType),
                            someExpr),
                        typeof(object));
                }
                else if (paramInfos[i].ParameterType.GetCustomAttribute<
                    WasiResourceAttribute>() != null)
                {
                    var handleParam = lambdaParams[wi++];
                    var paramTable = Expression.Call(contextConst,
                        tableForMethod,
                        Expression.Constant(paramInfos[i].ParameterType));
                    argExprs[i] = Expression.Convert(
                        Expression.Call(paramTable, getResourceMethod,
                            handleParam),
                        typeof(object));
                }
                else if (paramInfos[i].ParameterType == typeof(
                    Wacs.WASI.Preview2.Sockets.IpSocketAddress))
                {
                    var slotArgs = new Expression[12];
                    for (int k = 0; k < 12; k++)
                        slotArgs[k] = lambdaParams[wi++];
                    argExprs[i] = Expression.Convert(
                        Expression.Call(decodeIpSocketAddrMethod, slotArgs),
                        typeof(object));
                }
                else if (paramInfos[i].ParameterType == typeof(
                    Wacs.WASI.Preview2.Filesystem.NewTimestamp))
                {
                    var decodeTsMethod = typeof(WasiInterfaceBinder)
                        .GetMethod(nameof(DecodeNewTimestamp),
                            BindingFlags.Static | BindingFlags.NonPublic)!;
                    var discParam = lambdaParams[wi++];
                    var secParam = lambdaParams[wi++];
                    var nanosParam = lambdaParams[wi++];
                    argExprs[i] = Expression.Convert(
                        Expression.Call(decodeTsMethod,
                            discParam, secParam, nanosParam),
                        typeof(object));
                }
                else if (paramInfos[i].ParameterType == typeof(
                    Wacs.WASI.Preview2.Filesystem.AccessType))
                {
                    var decodeAtMethod = typeof(WasiInterfaceBinder)
                        .GetMethod(nameof(DecodeAccessType),
                            BindingFlags.Static | BindingFlags.NonPublic)!;
                    var discParam = lambdaParams[wi++];
                    var modesParam = lambdaParams[wi++];
                    argExprs[i] = Expression.Convert(
                        Expression.Call(decodeAtMethod,
                            discParam, modesParam),
                        typeof(object));
                }
                else
                {
                    Expression e = lambdaParams[wi++];
                    if (paramInfos[i].ParameterType != e.Type)
                    {
                        if (paramInfos[i].ParameterType == typeof(bool))
                            e = Expression.NotEqual(e,
                                Expression.Constant(0, e.Type));
                        else
                            e = Expression.Convert(e,
                                paramInfos[i].ParameterType);
                    }
                    argExprs[i] = Expression.Convert(e, typeof(object));
                }
            }

            var argArr = Expression.NewArrayInit(typeof(object), argExprs);
            Expression call;
            if (unitResultFlatReturn)
            {
                var funcTarget = Expression.Constant(
                    (Func<ExecContext, int, object?[], int>)FuncBody);
                call = Expression.Invoke(funcTarget,
                    lambdaParams[0], lambdaParams[1], argArr);
            }
            else
            {
                var actionTarget = Expression.Constant(
                    (Action<ExecContext, int, object?[], int>)ActionBody);
                call = Expression.Invoke(actionTarget,
                    lambdaParams[0], lambdaParams[1], argArr,
                    retAreaParam!);
            }
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

        /// <summary>Resource method tagged
        /// [WasiErrorResult] returning <c>string</c> —
        /// result&lt;string, error-code&gt;. Wire form: outer
        /// disc + 3-byte padding + (string-ptr, string-len)
        /// at retArea+4/+8 (12 bytes total). Each string
        /// param decodes via Encoding.UTF8.GetString from
        /// (ptr, len) wire slots; primitives ride through
        /// ToWireType. Used by descriptor.readlink-at,
        /// terminal-* readlinks, etc.</summary>
        private static void BindErrorResultStringReturnResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable selfTable,
            Type resourceType, MethodInfo m)
        {
            var paramInfos = m.GetParameters();
            foreach (var p in paramInfos)
                if (!IsPrimitive(p.ParameterType)
                    && !p.ParameterType.IsEnum
                    && p.ParameterType != typeof(string))
                    throw new InvalidOperationException(
                        "[WasiErrorResult] string-return resource "
                        + "method '" + m.Name + "' takes "
                        + "unsupported param type "
                        + p.ParameterType + ".");

            int paramSlotCount = 0;
            foreach (var p in paramInfos)
                paramSlotCount += p.ParameterType == typeof(string) ? 2 : 1;
            var wireParamTypes = new Type[3 + paramSlotCount];
            wireParamTypes[0] = typeof(ExecContext);
            wireParamTypes[1] = typeof(int);
            int wi = 2;
            foreach (var p in paramInfos)
            {
                if (p.ParameterType == typeof(string))
                {
                    wireParamTypes[wi++] = typeof(int);
                    wireParamTypes[wi++] = typeof(int);
                }
                else
                {
                    wireParamTypes[wi++] = ToWireType(p.ParameterType);
                }
            }
            wireParamTypes[wi] = typeof(int);

            var delegateType = OpenActionType(wireParamTypes.Length)
                .MakeGenericType(wireParamTypes);

            Wacs.Core.Runtime.Delegates.GenericFuncs? cabiRealloc = null;
            int Allocate(int align, int size)
            {
                if (cabiRealloc == null)
                {
                    if (!runtime.TryGetExportedFunction(
                            "cabi_realloc", out var addr))
                        throw new InvalidOperationException(
                            "Component does not export "
                            + "cabi_realloc — required for "
                            + "string-result resource methods.");
                    cabiRealloc = runtime.CreateInvoker(
                        addr, new InvokerOptions());
                }
                return cabiRealloc(0, 0, align, size)[0].Data.Int32;
            }

            void Body(ExecContext ctx, int handle, object?[] hostArgs,
                int retAreaPtr)
            {
                var inst = selfTable.Get(handle);
                var ret = (string)(m.Invoke(inst, hostArgs) ?? "");
                var memory = ctx.DefaultMemory.Data;
                var bs = System.Text.Encoding.UTF8.GetBytes(ret);
                int dPtr = bs.Length == 0 ? 0 : Allocate(1, bs.Length);
                if (bs.Length > 0)
                    Array.Copy(bs, 0, memory, dPtr, bs.Length);
                memory[retAreaPtr] = 0;       // Ok disc
                memory[retAreaPtr + 1] = 0;
                memory[retAreaPtr + 2] = 0;
                memory[retAreaPtr + 3] = 0;
                WriteI32LE(memory, retAreaPtr + 4, dPtr);
                WriteI32LE(memory, retAreaPtr + 8, bs.Length);
            }

            var lambdaParams = new ParameterExpression[wireParamTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            lambdaParams[1] = Expression.Parameter(typeof(int), "self");
            wi = 2;
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (paramInfos[i].ParameterType == typeof(string))
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_ptr");
                    lambdaParams[wi++] = Expression.Parameter(
                        typeof(int), paramInfos[i].Name + "_len");
                }
                else
                {
                    lambdaParams[wi++] = Expression.Parameter(
                        wireParamTypes[wi - 1], paramInfos[i].Name);
                }
            }
            var retAreaParam = Expression.Parameter(typeof(int), "retAreaPtr");
            lambdaParams[wireParamTypes.Length - 1] = retAreaParam;

            var ctxParam = lambdaParams[0];
            var memoryProp = typeof(ExecContext).GetProperty(
                nameof(ExecContext.DefaultMemory))!;
            var memDataField = typeof(Wacs.Core.Runtime.Types.MemoryInstance)
                .GetField("Data")!;
            var memoryAccess = Expression.Field(
                Expression.Property(ctxParam, memoryProp),
                memDataField);
            var encodingUtf8 = Expression.Property(null,
                typeof(System.Text.Encoding).GetProperty(
                    nameof(System.Text.Encoding.UTF8))!);
            var getStringMethod = typeof(System.Text.Encoding).GetMethod(
                nameof(System.Text.Encoding.GetString),
                new[] { typeof(byte[]), typeof(int), typeof(int) })!;

            wi = 2;
            var argExprs = new Expression[paramInfos.Length];
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (paramInfos[i].ParameterType == typeof(string))
                {
                    var ptrParam = lambdaParams[wi++];
                    var lenParam = lambdaParams[wi++];
                    argExprs[i] = Expression.Convert(
                        Expression.Call(encodingUtf8, getStringMethod,
                            memoryAccess, ptrParam, lenParam),
                        typeof(object));
                }
                else
                {
                    Expression e = lambdaParams[wi++];
                    if (paramInfos[i].ParameterType != e.Type)
                    {
                        if (paramInfos[i].ParameterType == typeof(bool))
                            e = Expression.NotEqual(e,
                                Expression.Constant(0, e.Type));
                        else
                            e = Expression.Convert(e,
                                paramInfos[i].ParameterType);
                    }
                    argExprs[i] = Expression.Convert(e, typeof(object));
                }
            }

            var argArr = Expression.NewArrayInit(typeof(object), argExprs);
            var bodyTarget = Expression.Constant(
                (Action<ExecContext, int, object?[], int>)Body);
            var call = Expression.Invoke(bodyTarget,
                lambdaParams[0], lambdaParams[1], argArr, retAreaParam);
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

        /// <summary>Resource method tagged
        /// [WasiErrorResult] returning an enum —
        /// result&lt;enum, error-code&gt;. Both sides are
        /// no-payload variants; wire form is 1-byte outer disc
        /// + 1-byte inner enum disc at retArea+1 (no padding
        /// since both align to 1).</summary>
        private static void BindErrorResultEnumReturnResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable selfTable,
            Type resourceType, MethodInfo m)
        {
            if (m.GetParameters().Length != 0)
                throw new InvalidOperationException(
                    "[WasiErrorResult] enum-return resource "
                    + "method '" + m.Name + "' takes params — "
                    + "v0 only handles zero-arg get-* methods.");

            // Wire signature: ExecContext, handle, retAreaPtr.
            var delegateType = typeof(Action<ExecContext, int, int>);

            void Body(ExecContext ctx, int handle, int retAreaPtr)
            {
                var inst = selfTable.Get(handle);
                var ret = m.Invoke(inst, Array.Empty<object?>())!;
                // Convert enum to its underlying byte.
                byte v = System.Convert.ToByte(ret);
                var memory = ctx.DefaultMemory.Data;
                memory[retAreaPtr] = 0;       // Ok disc
                memory[retAreaPtr + 1] = v;   // enum value
            }

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (namespaceName, importName), Body);
        }

        /// <summary>Resource method returning another resource
        /// type (e.g. <c>output-stream.subscribe() -&gt;
        /// own&lt;pollable&gt;</c>). Wire form: handle in,
        /// handle out — both i32 flat slots. Wrapper looks up
        /// self, invokes host method, allocates a fresh handle
        /// for the returned resource via the matching table.
        /// </summary>
        private static void BindResourceReturnResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable selfTable,
            Type resourceType, MethodInfo m,
            ResourceContext resources)
        {
            if (m.GetParameters().Length != 0)
                throw new InvalidOperationException(
                    "Resource method returning a resource with "
                    + "parameters is a follow-up — only zero-arg "
                    + "factory methods (subscribe-style) ship in v0.");
            var returnTable = resources.TableFor(m.ReturnType);

            int Body(ExecContext _, int handle)
            {
                var inst = selfTable.Get(handle);
                var ret = m.Invoke(inst, Array.Empty<object?>());
                if (ret == null)
                    throw new InvalidOperationException(
                        "Resource-returning method '" + m.Name
                        + "' returned null — own<T> cannot be null.");
                return returnTable.Allocate(ret);
            }

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (namespaceName, importName), Body);
        }

        /// <summary>Resource method
        /// <c>void M([WasiResultParam] T? response)</c> where T
        /// is a resource — the response-outparam.set shape. Wire:
        /// (self_handle, result_disc, payload_handle), no
        /// return. disc==0 → resolve payload_handle through the
        /// param-type table; disc!=0 → host gets null and the
        /// payload-handle slot is ignored. Always-Ok semantics
        /// for now; payload-bearing error-code variants are a
        /// follow-up.</summary>
        private static void BindResultResourceParamVoidResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string importName, ResourceTable selfTable,
            Type resourceType, MethodInfo m,
            ResourceContext resources)
        {
            var paramType = m.GetParameters()[0].ParameterType;
            var paramTable = resources.TableFor(paramType);

            void Body(ExecContext _, int self, int disc, int handle)
            {
                var inst = selfTable.Get(self);
                object? arg = disc == 0
                    ? paramTable.Get(handle)
                    : null;
                m.Invoke(inst, new[] { arg });
            }

            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (namespaceName, importName), Body);
        }

        /// <summary>Register the <c>[constructor]T</c> handler
        /// — wasm calls into the static factory method tagged
        /// <see cref="WasiConstructorAttribute"/>; we allocate
        /// the returned instance into the resource table and
        /// hand back its handle. v0 admits zero-arg ctors and
        /// own<resource>-only-param ctors (one or more
        /// resource params). Each resource param is one i32
        /// wire slot resolved through the per-type table.
        /// Other param shapes (primitive / string / aggregate)
        /// are follow-ups.</summary>
        private static void BindResourceConstructor(
            WasmRuntime runtime, string namespaceName,
            string witResourceName, ResourceTable table,
            Type resourceType, MethodInfo factory,
            ResourceContext resources)
        {
            if (factory.ReturnType != resourceType)
                throw new InvalidOperationException(
                    "Resource constructor '" + factory.Name
                    + "' must return its declaring type "
                    + resourceType + ".");
            var paramInfos = factory.GetParameters();
            foreach (var p in paramInfos)
                if (p.ParameterType.GetCustomAttribute<
                    WasiResourceAttribute>() == null)
                    throw new InvalidOperationException(
                        "Resource constructor '" + factory.Name
                        + "' takes unsupported param type "
                        + p.ParameterType + " — only "
                        + "own<resource> params ship in v0.");
            var importName = "[constructor]" + witResourceName;

            // Zero-arg fast path keeps the existing wire shape.
            if (paramInfos.Length == 0)
            {
                int ZeroArg(ExecContext _)
                {
                    var inst = factory.Invoke(null,
                        Array.Empty<object?>());
                    if (inst == null)
                        throw new InvalidOperationException(
                            "Resource constructor '" + factory.Name
                            + "' returned null.");
                    return table.Allocate(inst);
                }
                runtime.BindHostFunction<Func<ExecContext, int>>(
                    (namespaceName, importName), ZeroArg);
                return;
            }

            // own<resource>-only param shape — wire slot per
            // param is i32 (handle). Build an Action-shaped
            // body via expression tree so we can take any
            // arity in [1, 4].
            var paramTables = new ResourceTable[paramInfos.Length];
            for (int i = 0; i < paramInfos.Length; i++)
                paramTables[i] = resources.TableFor(paramInfos[i].ParameterType);

            int Body(ExecContext _, object?[] hostArgs)
            {
                var inst = factory.Invoke(null, hostArgs);
                if (inst == null)
                    throw new InvalidOperationException(
                        "Resource constructor '" + factory.Name
                        + "' returned null.");
                return table.Allocate(inst);
            }

            // Wire signature: ExecContext + N i32 handles -> i32.
            var wireTypes = new Type[paramInfos.Length + 2];
            wireTypes[0] = typeof(ExecContext);
            for (int i = 0; i < paramInfos.Length; i++)
                wireTypes[i + 1] = typeof(int);
            wireTypes[paramInfos.Length + 1] = typeof(int);

            var lambdaParams = new ParameterExpression[paramInfos.Length + 1];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            var argExprs = new Expression[paramInfos.Length];
            var getMethod = typeof(ResourceTable).GetMethod(
                nameof(ResourceTable.Get))!;
            for (int i = 0; i < paramInfos.Length; i++)
            {
                lambdaParams[i + 1] = Expression.Parameter(
                    typeof(int), paramInfos[i].Name);
                argExprs[i] = Expression.Convert(
                    Expression.Call(
                        Expression.Constant(paramTables[i]),
                        getMethod, lambdaParams[i + 1]),
                    typeof(object));
            }
            var argArr = Expression.NewArrayInit(typeof(object), argExprs);
            var bodyDel = Expression.Constant(
                (Func<ExecContext, object?[], int>)Body);
            var call = Expression.Invoke(bodyDel,
                lambdaParams[0], argArr);
            var delegateType = OpenFuncType(wireTypes.Length)
                .MakeGenericType(wireTypes);
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

        /// <summary>Register the <c>[resource-drop]T</c> handler
        /// — wasm passes the i32 handle; we drop it from the
        /// table (Disposing IDisposable instances). Drop on an
        /// already-dropped handle returns silently per the
        /// canonical-ABI spec.</summary>
        private static void BindResourceDrop(
            WasmRuntime runtime, string namespaceName,
            string witResourceName, ResourceTable table)
        {
            var importName = "[resource-drop]" + witResourceName;
            Action<ExecContext, int> body = (_, handle) =>
            {
                table.Drop(handle);
            };
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (namespaceName, importName), body);
        }

        /// <summary>True iff the method returns
        /// <c>(TResource, string)[]</c> where TResource is a
        /// [WasiResource]-marked class. Used by
        /// wasi:filesystem/preopens.get-directories — list of
        /// (descriptor handle, host-path-string) pairs.</summary>
        private static bool IsResourceStringPairArrayReturnPrimitiveParams(MethodInfo m)
        {
            if (!m.ReturnType.IsArray) return false;
            var elem = m.ReturnType.GetElementType()!;
            if (!elem.IsValueType) return false;
            if (!elem.IsGenericType) return false;
            if (elem.GetGenericTypeDefinition() != typeof(System.ValueTuple<,>))
                return false;
            var args = elem.GetGenericArguments();
            if (args[0].GetCustomAttribute<WasiResourceAttribute>() == null)
                return false;
            if (args[1] != typeof(string)) return false;
            foreach (var p in m.GetParameters())
                if (!IsPrimitive(p.ParameterType)) return false;
            return true;
        }

        private static bool IsStringPairArrayReturnPrimitiveParams(MethodInfo m)
        {
            // (string, string)[] — i.e. ValueTuple<string, string>[]
            // matches list<tuple<string, string>>. Used by
            // wasi:cli/environment.get-environment.
            if (!m.ReturnType.IsArray) return false;
            var elem = m.ReturnType.GetElementType()!;
            if (!elem.IsValueType) return false;
            if (!elem.IsGenericType) return false;
            if (elem.GetGenericTypeDefinition() != typeof(System.ValueTuple<,>))
                return false;
            var args = elem.GetGenericArguments();
            if (args[0] != typeof(string) || args[1] != typeof(string))
                return false;
            foreach (var p in m.GetParameters())
                if (!IsPrimitive(p.ParameterType)) return false;
            return true;
        }

        private static bool IsOptionStringReturnPrimitiveParams(MethodInfo m)
        {
            // Treat string? (a Nullable-ish reference type) as
            // option<string>. C# can't actually distinguish
            // string from string? at the reflection level (both
            // are typeof(string)), so we use a marker attribute
            // — or here, accept any string-returning method
            // tagged [WasiOptional]. For v0 this binder only
            // fires when the host opts in via the attribute.
            if (m.ReturnType != typeof(string)) return false;
            if (m.GetCustomAttribute<WasiOptionalReturnAttribute>() == null)
                return false;
            foreach (var p in m.GetParameters())
                if (!IsPrimitive(p.ParameterType)) return false;
            return true;
        }

        /// <summary>True iff the method returns a struct/class
        /// whose every public field is a primitive (the C#
        /// representation of a record-of-primitives) and every
        /// parameter is a primitive flat slot. The most common
        /// pattern after byte[] for WASI shapes — datetime,
        /// stat-like records, etc.</summary>
        private static bool IsRecordOfPrimitivesReturnPrimitiveParams(MethodInfo m)
        {
            var t = m.ReturnType;
            if (IsPrimitive(t) || t == typeof(byte[]) || t == typeof(void)
                || t == typeof(string)) return false;
            if (!t.IsValueType && !t.IsClass) return false;
            // Record-of-primitives shape: at least one public
            // field, all primitive-typed.
            var fields = t.GetFields(
                BindingFlags.Public | BindingFlags.Instance);
            if (fields.Length == 0) return false;
            foreach (var f in fields)
                if (!IsPrimitive(f.FieldType)) return false;
            foreach (var p in m.GetParameters())
                if (!IsPrimitive(p.ParameterType)) return false;
            return true;
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

        /// <summary>Build a canon-lower wrapper for a host
        /// method returning a record-of-primitives. Same
        /// retArea-pointer convention as
        /// <see cref="BindByteArrayReturnMethod"/> — the wasm
        /// import takes (host params..., retAreaPtr) and writes
        /// the record's fields at retAreaPtr per canonical-ABI
        /// alignment rules. Field offsets are computed by
        /// walking declared field order with running alignment
        /// (each field starts at the next multiple of its own
        /// alignment from the running offset).</summary>
        private static void BindRecordReturnMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m)
        {
            var importName = ToKebabCase(m.Name);

            var paramInfos = m.GetParameters();
            var paramTypes = new Type[paramInfos.Length + 2];
            paramTypes[0] = typeof(ExecContext);
            for (int i = 0; i < paramInfos.Length; i++)
                paramTypes[i + 1] = ToWireType(paramInfos[i].ParameterType);
            paramTypes[paramInfos.Length + 1] = typeof(int);

            var delegateType = OpenActionType(paramTypes.Length)
                .MakeGenericType(paramTypes);

            // Pre-compute per-field byte offsets following
            // canonical-ABI alignment rules so the wrapper
            // doesn't recompute on every call.
            var recordType = m.ReturnType;
            var fields = recordType.GetFields(
                BindingFlags.Public | BindingFlags.Instance);
            var fieldOffsets = new int[fields.Length];
            int offset = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                var size = PrimitiveByteSize(fields[i].FieldType);
                offset = AlignUp(offset, size);
                fieldOffsets[i] = offset;
                offset += size;
            }

            void Body(ExecContext ctx, object?[] hostArgs, int retAreaPtr)
            {
                var record = m.Invoke(impl, hostArgs)!;
                var memory = ctx.DefaultMemory.Data;
                for (int i = 0; i < fields.Length; i++)
                {
                    var v = fields[i].GetValue(record);
                    WritePrimitiveLE(memory, retAreaPtr + fieldOffsets[i],
                        fields[i].FieldType, v!);
                }
            }

            var lambdaParams = new ParameterExpression[paramTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            for (int i = 0; i < paramInfos.Length; i++)
                lambdaParams[i + 1] = Expression.Parameter(
                    paramTypes[i + 1], paramInfos[i].Name);
            lambdaParams[paramInfos.Length + 1] =
                Expression.Parameter(typeof(int), "retAreaPtr");

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

        /// <summary>Canon-lower wrapper for host methods
        /// returning <c>string</c> — same wire form as byte[]
        /// (UTF-8 bytes + (ptr, len) at retAreaPtr); the only
        /// difference is the encoding step.</summary>
        private static void BindStringReturnMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m)
        {
            BindAggregateReturnMethod(runtime, namespaceName, impl, m,
                (memory, retAreaPtr, ret, allocate) =>
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes((string)ret!);
                    var dataPtr = allocate(1, bytes.Length);
                    if (bytes.Length > 0)
                        System.Array.Copy(bytes, 0, memory,
                            dataPtr, bytes.Length);
                    WriteI32LE(memory, retAreaPtr, dataPtr);
                    WriteI32LE(memory, retAreaPtr + 4, bytes.Length);
                });
        }

        /// <summary>Canon-lower wrapper for host methods
        /// returning <c>string[]</c> — list of strings. Each
        /// string is encoded + allocated separately, then a
        /// pointer-array of (ptr, len) pairs is allocated and
        /// written, then (arrayPtr, count) goes to retAreaPtr.
        /// </summary>
        private static void BindStringArrayReturnMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m)
        {
            BindAggregateReturnMethod(runtime, namespaceName, impl, m,
                (memory, retAreaPtr, ret, allocate) =>
                {
                    var arr = (string[])ret!;
                    int count = arr.Length;
                    int arrayPtr = count == 0 ? 0
                        : allocate(4, count * 8);
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(arr[i]);
                        int strPtr = bytes.Length == 0 ? 0
                            : allocate(1, bytes.Length);
                        if (bytes.Length > 0)
                            System.Array.Copy(bytes, 0, memory,
                                strPtr, bytes.Length);
                        WriteI32LE(memory, arrayPtr + i * 8, strPtr);
                        WriteI32LE(memory, arrayPtr + i * 8 + 4, bytes.Length);
                    }
                    WriteI32LE(memory, retAreaPtr, arrayPtr);
                    WriteI32LE(memory, retAreaPtr + 4, count);
                });
        }

        /// <summary>Canon-lower wrapper for host methods
        /// returning <c>(string, string)[]</c> — list of string
        /// pairs (e.g. environment vars). Each element is 4 i32s
        /// (k_ptr, k_len, v_ptr, v_len = 16 bytes); the array
        /// itself is allocated as count*16 bytes; (arrayPtr,
        /// count) goes to retAreaPtr.</summary>
        private static void BindStringPairArrayReturnMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m)
        {
            BindAggregateReturnMethod(runtime, namespaceName, impl, m,
                (memory, retAreaPtr, ret, allocate) =>
                {
                    var arr = (System.Collections.IList)ret!;
                    int count = arr.Count;
                    int arrayPtr = count == 0 ? 0
                        : allocate(4, count * 16);
                    var elemType = ret.GetType().GetElementType()!;
                    var item1Field = elemType.GetField("Item1")!;
                    var item2Field = elemType.GetField("Item2")!;
                    for (int i = 0; i < count; i++)
                    {
                        var pair = arr[i]!;
                        var k = (string)item1Field.GetValue(pair)!;
                        var v = (string)item2Field.GetValue(pair)!;
                        var kBytes = System.Text.Encoding.UTF8.GetBytes(k);
                        var vBytes = System.Text.Encoding.UTF8.GetBytes(v);
                        int kPtr = kBytes.Length == 0 ? 0
                            : allocate(1, kBytes.Length);
                        if (kBytes.Length > 0)
                            System.Array.Copy(kBytes, 0, memory,
                                kPtr, kBytes.Length);
                        int vPtr = vBytes.Length == 0 ? 0
                            : allocate(1, vBytes.Length);
                        if (vBytes.Length > 0)
                            System.Array.Copy(vBytes, 0, memory,
                                vPtr, vBytes.Length);
                        WriteI32LE(memory, arrayPtr + i * 16, kPtr);
                        WriteI32LE(memory, arrayPtr + i * 16 + 4, kBytes.Length);
                        WriteI32LE(memory, arrayPtr + i * 16 + 8, vPtr);
                        WriteI32LE(memory, arrayPtr + i * 16 + 12, vBytes.Length);
                    }
                    WriteI32LE(memory, retAreaPtr, arrayPtr);
                    WriteI32LE(memory, retAreaPtr + 4, count);
                });
        }

        /// <summary>Canon-lower wrapper for host methods
        /// returning <c>(TResource, string)[]</c> — list of
        /// pairs where the first element is a resource handle
        /// (allocated in the matching resource table on each
        /// call) and the second is a string. Used by
        /// wasi:filesystem/preopens.get-directories. Each
        /// element is 12 bytes (handle: i32 + strPtr: i32 +
        /// strLen: i32) with align=4.</summary>
        private static void BindResourceStringPairArrayReturnMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m, ResourceContext resources)
        {
            var elem = m.ReturnType.GetElementType()!;
            var item1Field = elem.GetField("Item1")!;
            var item2Field = elem.GetField("Item2")!;
            var resourceType = item1Field.FieldType;
            var table = resources.TableFor(resourceType);

            BindAggregateReturnMethod(runtime, namespaceName, impl, m,
                (memory, retAreaPtr, ret, allocate) =>
                {
                    var arr = (System.Collections.IList)ret!;
                    int count = arr.Count;
                    int arrayPtr = count == 0 ? 0
                        : allocate(4, count * 12);
                    for (int i = 0; i < count; i++)
                    {
                        var pair = arr[i]!;
                        var resInst = item1Field.GetValue(pair)!;
                        var s = (string)item2Field.GetValue(pair)!;
                        int handle = table.Allocate(resInst);
                        var sBytes = System.Text.Encoding.UTF8.GetBytes(s);
                        int sPtr = sBytes.Length == 0 ? 0
                            : allocate(1, sBytes.Length);
                        if (sBytes.Length > 0)
                            System.Array.Copy(sBytes, 0, memory,
                                sPtr, sBytes.Length);
                        WriteI32LE(memory, arrayPtr + i * 12, handle);
                        WriteI32LE(memory, arrayPtr + i * 12 + 4, sPtr);
                        WriteI32LE(memory, arrayPtr + i * 12 + 8, sBytes.Length);
                    }
                    WriteI32LE(memory, retAreaPtr, arrayPtr);
                    WriteI32LE(memory, retAreaPtr + 4, count);
                });
        }

        /// <summary>Canon-lower wrapper for
        /// <c>poll(in: list&lt;borrow&lt;TResource&gt;&gt;)
        /// -&gt; list&lt;u32&gt;</c>. The single param decodes from
        /// (in_ptr, in_len) into a TResource[] (handles
        /// resolved via the per-component ResourceContext);
        /// the u32[] return allocates via cabi_realloc and
        /// writes (out_ptr, out_len) at retArea.</summary>
        private static void BindResourceArrayParamU32ArrayReturnMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m, ResourceContext resources)
        {
            var importName = ToKebabCase(m.Name);
            var elemType = m.GetParameters()[0].ParameterType
                .GetElementType()!;
            var resTable = resources.TableFor(elemType);

            // Wire: ExecContext, in_ptr, in_len, retArea → void.
            var delegateType = typeof(Action<ExecContext, int, int, int>);

            Wacs.Core.Runtime.Delegates.GenericFuncs? cabiRealloc = null;
            int Allocate(int align, int size)
            {
                if (cabiRealloc == null)
                {
                    if (!runtime.TryGetExportedFunction(
                            "cabi_realloc", out var addr))
                        throw new InvalidOperationException(
                            "Component does not export cabi_realloc.");
                    cabiRealloc = runtime.CreateInvoker(
                        addr, new InvokerOptions());
                }
                return cabiRealloc(0, 0, align, size)[0].Data.Int32;
            }

            void Body(ExecContext ctx, int inPtr, int inLen, int retArea)
            {
                var memory = ctx.DefaultMemory.Data;
                // Decode list<borrow<TResource>> from the input
                // pointer/length pair. Each handle is i32.
                var resArr = System.Array.CreateInstance(elemType, inLen);
                for (int i = 0; i < inLen; i++)
                {
                    int h = (int)System.Buffers.Binary.BinaryPrimitives
                        .ReadInt32LittleEndian(
                            memory.AsSpan(inPtr + i * 4, 4));
                    resArr.SetValue(resTable.Get(h), i);
                }

                var ret = (uint[])m.Invoke(impl, new object[] { resArr })!;

                int outPtr = ret.Length == 0 ? 0
                    : Allocate(4, ret.Length * 4);
                for (int i = 0; i < ret.Length; i++)
                    WriteI32LE(memory, outPtr + i * 4, (int)ret[i]);
                WriteI32LE(memory, retArea, outPtr);
                WriteI32LE(memory, retArea + 4, ret.Length);
            }

            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (namespaceName, importName), Body);
        }

        /// <summary>Canon-lower wrapper for host methods
        /// returning <c>option&lt;string&gt;</c> — null on the
        /// host side maps to disc=0; non-null maps to disc=1
        /// + (ptr, len) at offsets 4/8. Per WASI's option-string
        /// shape, retArea is 12 bytes (1-byte disc, padding to
        /// 4-align, 4-byte ptr, 4-byte len). Methods opt into
        /// this path via
        /// <see cref="WasiOptionalReturnAttribute"/>.</summary>
        private static void BindOptionStringReturnMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m)
        {
            BindAggregateReturnMethod(runtime, namespaceName, impl, m,
                (memory, retAreaPtr, ret, allocate) =>
                {
                    if (ret == null)
                    {
                        // None — disc=0; canonical ABI doesn't
                        // require us to zero the rest, but doing
                        // so is cheap and avoids leaking stale
                        // bytes if cabi_realloc didn't.
                        memory[retAreaPtr] = 0;
                        memory[retAreaPtr + 1] = 0;
                        memory[retAreaPtr + 2] = 0;
                        memory[retAreaPtr + 3] = 0;
                        return;
                    }
                    var bytes = System.Text.Encoding.UTF8.GetBytes((string)ret);
                    int strPtr = bytes.Length == 0 ? 0
                        : allocate(1, bytes.Length);
                    if (bytes.Length > 0)
                        System.Array.Copy(bytes, 0, memory,
                            strPtr, bytes.Length);
                    memory[retAreaPtr] = 1;
                    memory[retAreaPtr + 1] = 0;
                    memory[retAreaPtr + 2] = 0;
                    memory[retAreaPtr + 3] = 0;
                    WriteI32LE(memory, retAreaPtr + 4, strPtr);
                    WriteI32LE(memory, retAreaPtr + 8, bytes.Length);
                });
        }

        /// <summary>Shared template for aggregate-return canon-
        /// lower wrappers. Builds the (ExecContext, host-params...,
        /// retAreaPtr) → void delegate signature, drives
        /// expression-tree compilation, and registers via
        /// BindHostFunction. The supplied
        /// <paramref name="writePayload"/> callback is the
        /// shape-specific bit: it gets memory + retAreaPtr +
        /// the host method's return value + an allocate(align,
        /// size) closure routing to lazy-resolved
        /// cabi_realloc.</summary>
        private static void BindAggregateReturnMethod(
            WasmRuntime runtime, string namespaceName,
            object impl, MethodInfo m,
            Action<byte[], int, object?, Func<int, int, int>> writePayload)
        {
            var importName = ToKebabCase(m.Name);

            var paramInfos = m.GetParameters();
            var paramTypes = new Type[paramInfos.Length + 2];
            paramTypes[0] = typeof(ExecContext);
            for (int i = 0; i < paramInfos.Length; i++)
                paramTypes[i + 1] = ToWireType(paramInfos[i].ParameterType);
            paramTypes[paramInfos.Length + 1] = typeof(int);

            var delegateType = OpenActionType(paramTypes.Length)
                .MakeGenericType(paramTypes);

            Wacs.Core.Runtime.Delegates.GenericFuncs? cabiRealloc = null;
            int Allocate(int align, int size)
            {
                if (cabiRealloc == null)
                {
                    if (!runtime.TryGetExportedFunction(
                            "cabi_realloc", out var addr))
                        throw new InvalidOperationException(
                            "Component does not export "
                            + "cabi_realloc — required for "
                            + "aggregate-returning host imports.");
                    cabiRealloc = runtime.CreateInvoker(
                        addr, new InvokerOptions());
                }
                return cabiRealloc(0, 0, align, size)[0].Data.Int32;
            }

            void Body(ExecContext ctx, object?[] hostArgs, int retAreaPtr)
            {
                var ret = m.Invoke(impl, hostArgs);
                var memory = ctx.DefaultMemory.Data;
                writePayload(memory, retAreaPtr, ret, Allocate);
            }

            var lambdaParams = new ParameterExpression[paramTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            for (int i = 0; i < paramInfos.Length; i++)
                lambdaParams[i + 1] = Expression.Parameter(
                    paramTypes[i + 1], paramInfos[i].Name);
            lambdaParams[paramInfos.Length + 1] =
                Expression.Parameter(typeof(int), "retAreaPtr");

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

        /// <summary>Write an i32 to memory in little-endian.
        /// Used by aggregate-return wrappers to lay down ptr/
        /// len pairs in retArea or in inner arrays.</summary>
        private static void WriteI32LE(byte[] memory, int ptr, int value)
        {
            memory[ptr]     = (byte)(value & 0xFF);
            memory[ptr + 1] = (byte)((value >> 8) & 0xFF);
            memory[ptr + 2] = (byte)((value >> 16) & 0xFF);
            memory[ptr + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>Byte size of a primitive — drives both
        /// alignment (start at AlignUp(offset, size)) and write
        /// width (size bytes per field). Mirrors the canonical-
        /// ABI alignment rule "alignment(P) = sizeof(P)" for
        /// primitives.</summary>
        /// <summary>Write an
        /// <c>option&lt;datetime&gt;</c> at <paramref name="ptr"/>.
        /// 24 bytes, align 8: option disc(1) + 7 pad +
        /// seconds(u64) + nanoseconds(u32) + 4 pad. When
        /// null, only the disc byte is meaningful.</summary>
        private static void WriteOptionalDatetime(byte[] memory, int ptr,
            Wacs.WASI.Preview2.Clocks.Datetime? dt)
        {
            if (dt == null)
            {
                memory[ptr] = 0;   // None
                return;
            }
            memory[ptr] = 1;       // Some
            // bytes ptr+1..+7 stay zero (preamble already
            // cleared retArea on outer disc write; for nested
            // option<datetime> in stat the outer write only
            // touches +0..+3, so we zero the option padding).
            for (int i = 1; i < 8; i++) memory[ptr + i] = 0;
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt64LittleEndian(
                    memory.AsSpan(ptr + 8, 8), dt.Value.Seconds);
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt32LittleEndian(
                    memory.AsSpan(ptr + 16, 4), dt.Value.Nanoseconds);
            // bytes ptr+20..+23 are tail padding (align 8).
            memory[ptr + 20] = 0;
            memory[ptr + 21] = 0;
            memory[ptr + 22] = 0;
            memory[ptr + 23] = 0;
        }

        /// <summary>Decode a list&lt;outgoing-datagram&gt;
        /// from linear memory at <paramref name="listPtr"/>
        /// for <paramref name="listLen"/> elements. Each
        /// element is 44 bytes: data list (8) + option<ip-
        /// socket-address> (36). Returns the decoded array
        /// in host-side form for passing to the host
        /// method.</summary>
        private static Wacs.WASI.Preview2.Sockets.OutgoingDatagram[]
            DecodeOutgoingDatagramArray(byte[] memory, int listPtr,
                int listLen)
        {
            var result = new Wacs.WASI.Preview2.Sockets
                .OutgoingDatagram[listLen];
            for (int i = 0; i < listLen; i++)
            {
                int eb = listPtr + i * 44;
                // data: list<u8> at eb+0/+4
                int dataPtr = (int)System.Buffers.Binary
                    .BinaryPrimitives.ReadInt32LittleEndian(
                        memory.AsSpan(eb, 4));
                int dataLen = (int)System.Buffers.Binary
                    .BinaryPrimitives.ReadInt32LittleEndian(
                        memory.AsSpan(eb + 4, 4));
                var data = new byte[dataLen];
                if (dataLen > 0)
                    Array.Copy(memory, dataPtr, data, 0, dataLen);
                // remote-address: option<ip-socket-address> at eb+8.
                //   eb+8: option disc (u8), eb+9..+11 padding
                //   eb+12: variant disc (u8), eb+13..+15 pad
                //   eb+16..: variant payload
                Wacs.WASI.Preview2.Sockets.IpSocketAddress? addr = null;
                if (memory[eb + 8] != 0)
                {
                    byte variantDisc = memory[eb + 12];
                    if (variantDisc == 0)
                    {
                        // ipv4 case: port at +16, address at +18..+21
                        ushort port = (ushort)(memory[eb + 16]
                            | (memory[eb + 17] << 8));
                        addr = new Wacs.WASI.Preview2.Sockets
                            .Ipv4SocketAddress(port,
                            new byte[] {
                                memory[eb + 18], memory[eb + 19],
                                memory[eb + 20], memory[eb + 21],
                            });
                    }
                    else
                    {
                        // ipv6 case: port at +16, flow-info at +20,
                        // address at +24..+39, scope-id at +40
                        ushort port = (ushort)(memory[eb + 16]
                            | (memory[eb + 17] << 8));
                        uint flow = (uint)System.Buffers.Binary
                            .BinaryPrimitives.ReadInt32LittleEndian(
                                memory.AsSpan(eb + 20, 4));
                        var addr6 = new ushort[8];
                        for (int k = 0; k < 8; k++)
                            addr6[k] = (ushort)(memory[eb + 24 + k * 2]
                                | (memory[eb + 25 + k * 2] << 8));
                        uint scope = (uint)System.Buffers.Binary
                            .BinaryPrimitives.ReadInt32LittleEndian(
                                memory.AsSpan(eb + 40, 4));
                        addr = new Wacs.WASI.Preview2.Sockets
                            .Ipv6SocketAddress(port, flow, addr6, scope);
                    }
                }
                result[i] = new Wacs.WASI.Preview2.Sockets
                    .OutgoingDatagram(data, addr);
            }
            return result;
        }

        /// <summary>Decode a flat-lowered
        /// <c>variant access-type</c> from its 2 wire slots:
        /// disc(i32) + modes(i32). Disc 0 = access(modes),
        /// disc 1 = exists.</summary>
        private static Wacs.WASI.Preview2.Filesystem.AccessType
            DecodeAccessType(int disc, int modes)
        {
            if (disc == 0)
                return new Wacs.WASI.Preview2.Filesystem.AccessTypeAccess(
                    (Wacs.WASI.Preview2.Filesystem.AccessModes)modes);
            return new Wacs.WASI.Preview2.Filesystem.AccessTypeExists();
        }

        /// <summary>Decode the <c>method</c> variant from its
        /// flat wire form: disc (i32) + joined-flat (i32 ptr +
        /// i32 len). Cases 0–8 are no-payload (get/head/post/
        /// put/delete/connect/options/trace/patch); case 9 is
        /// <c>other(string)</c> with the (ptr,len) payload
        /// pointing at UTF-8 bytes in linear memory.</summary>
        private static Wacs.WASI.Preview2.Http.HttpMethod
            DecodeHttpMethodFromFlatWire(byte[] memory,
                int disc, int ptr, int len)
        {
            switch (disc)
            {
                case 0: return new Wacs.WASI.Preview2.Http.HttpMethodGet();
                case 1: return new Wacs.WASI.Preview2.Http.HttpMethodHead();
                case 2: return new Wacs.WASI.Preview2.Http.HttpMethodPost();
                case 3: return new Wacs.WASI.Preview2.Http.HttpMethodPut();
                case 4: return new Wacs.WASI.Preview2.Http.HttpMethodDelete();
                case 5: return new Wacs.WASI.Preview2.Http.HttpMethodConnect();
                case 6: return new Wacs.WASI.Preview2.Http.HttpMethodOptions();
                case 7: return new Wacs.WASI.Preview2.Http.HttpMethodTrace();
                case 8: return new Wacs.WASI.Preview2.Http.HttpMethodPatch();
                case 9:
                    var name = System.Text.Encoding.UTF8.GetString(
                        memory, ptr, len);
                    return new Wacs.WASI.Preview2.Http.HttpMethodOther(name);
                default:
                    throw new ArgumentException(
                        "Unknown HttpMethod variant disc: " + disc);
            }
        }

        /// <summary>Decode the <c>scheme</c> variant from its
        /// flat wire form: disc (i32) + joined-flat (i32 ptr +
        /// i32 len). Cases 0/1 are no-payload (HTTP/HTTPS);
        /// case 2 is <c>other(string)</c>.</summary>
        private static Wacs.WASI.Preview2.Http.HttpScheme
            DecodeHttpSchemeFromFlatWire(byte[] memory,
                int disc, int ptr, int len)
        {
            switch (disc)
            {
                case 0: return new Wacs.WASI.Preview2.Http.HttpSchemeHttp();
                case 1: return new Wacs.WASI.Preview2.Http.HttpSchemeHttps();
                case 2:
                    var name = System.Text.Encoding.UTF8.GetString(
                        memory, ptr, len);
                    return new Wacs.WASI.Preview2.Http.HttpSchemeOther(name);
                default:
                    throw new ArgumentException(
                        "Unknown HttpScheme variant disc: " + disc);
            }
        }

        /// <summary>Copy a slice of linear memory into a
        /// fresh byte[]. Used to decode byte[] params (WIT
        /// list<u8>) at the host boundary — the binder reads
        /// (ptr, len) wire slots and calls this helper to
        /// produce a managed array the host method can take
        /// ownership of without aliasing guest memory.</summary>
        private static byte[] CopyMemorySlice(byte[] memory, int ptr, int len)
        {
            var slice = new byte[len];
            if (len > 0)
                System.Array.Copy(memory, ptr, slice, 0, len);
            return slice;
        }

        /// <summary>Decode a list&lt;list&lt;u8&gt;&gt; param
        /// from linear memory. listPtr points at len entries,
        /// each 8 bytes (bytes-ptr i32 + bytes-len i32).
        /// Returns a fresh byte[][] copy — host owns the
        /// elements and won't alias guest memory.</summary>
        private static byte[][] DecodeByteArrayList(byte[] memory,
            int listPtr, int listLen)
        {
            var result = new byte[listLen][];
            for (int i = 0; i < listLen; i++)
            {
                int eb = listPtr + i * 8;
                int dataPtr = (int)System.Buffers.Binary
                    .BinaryPrimitives.ReadInt32LittleEndian(
                        memory.AsSpan(eb, 4));
                int dataLen = (int)System.Buffers.Binary
                    .BinaryPrimitives.ReadInt32LittleEndian(
                        memory.AsSpan(eb + 4, 4));
                var slice = new byte[dataLen];
                if (dataLen > 0)
                    Array.Copy(memory, dataPtr, slice, 0, dataLen);
                result[i] = slice;
            }
            return result;
        }

        /// <summary>Decode a flat-lowered
        /// <c>variant new-timestamp</c> from its 3 wire slots:
        /// disc(i32) + seconds(i64) + nanoseconds(i32). Disc 0
        /// = no-change, disc 1 = now, disc 2 = timestamp.
        /// </summary>
        private static Wacs.WASI.Preview2.Filesystem.NewTimestamp
            DecodeNewTimestamp(int disc, long seconds, int nanoseconds)
        {
            if (disc == 0)
                return new Wacs.WASI.Preview2.Filesystem
                    .NewTimestampNoChange();
            if (disc == 1)
                return new Wacs.WASI.Preview2.Filesystem
                    .NewTimestampNow();
            return new Wacs.WASI.Preview2.Filesystem
                .NewTimestampTimestamp(
                    new Wacs.WASI.Preview2.Clocks.Datetime
                    {
                        Seconds = (ulong)seconds,
                        Nanoseconds = (uint)nanoseconds,
                    });
        }

        /// <summary>Decode a flat-lowered
        /// <c>option&lt;ip-socket-address&gt;</c> from its 13
        /// wire slots: 1 option disc + 12 inner ip-socket-
        /// address slots. Returns null when option disc is 0.
        /// </summary>
        private static Wacs.WASI.Preview2.Sockets.IpSocketAddress?
            DecodeOptionalIpSocketAddressFromFlatWire(int optionDisc,
                int variantDisc, int s1, int s2, int s3, int s4, int s5,
                int s6, int s7, int s8, int s9, int s10, int s11)
        {
            if (optionDisc == 0) return null;
            return DecodeIpSocketAddressFromFlatWire(variantDisc,
                s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11);
        }

        /// <summary>Decode a flat-lowered
        /// <c>ip-socket-address</c> param from its 12 wire
        /// slots: 1 disc + 11 joined-payload slots (all i32).
        /// Disc 0 → ipv4 (slots s1..s5 used: port + 4 bytes
        /// address); disc 1 → ipv6 (s1..s11 used: port +
        /// flow-info + 8 u16 address + scope-id). Padding
        /// slots in the ipv4 case (s6..s11) are ignored.
        /// </summary>
        private static Wacs.WASI.Preview2.Sockets.IpSocketAddress
            DecodeIpSocketAddressFromFlatWire(int disc, int s1, int s2,
                int s3, int s4, int s5, int s6, int s7, int s8, int s9,
                int s10, int s11)
        {
            if (disc == 0)
            {
                return new Wacs.WASI.Preview2.Sockets.Ipv4SocketAddress(
                    (ushort)s1,
                    new byte[] { (byte)s2, (byte)s3, (byte)s4, (byte)s5 });
            }
            else
            {
                return new Wacs.WASI.Preview2.Sockets.Ipv6SocketAddress(
                    (ushort)s1, (uint)s2,
                    new ushort[] {
                        (ushort)s3, (ushort)s4, (ushort)s5, (ushort)s6,
                        (ushort)s7, (ushort)s8, (ushort)s9, (ushort)s10,
                    },
                    (uint)s11);
            }
        }

        /// <summary>Write a WIT
        /// <c>variant ip-socket-address</c> at
        /// <paramref name="ptr"/>. Layout: 1-byte case disc
        /// (0=ipv4, 1=ipv6) + 3 padding + 28-byte payload (max
        /// of the two cases — ipv4 fills only 6 bytes, ipv6
        /// fills the full 28). Total 32 bytes.</summary>
        private static void WriteIpSocketAddressAt(byte[] memory, int ptr,
            Wacs.WASI.Preview2.Sockets.IpSocketAddress addr)
        {
            if (addr is Wacs.WASI.Preview2.Sockets.Ipv4SocketAddress v4)
            {
                memory[ptr] = 0;     // variant disc = ipv4
                memory[ptr + 1] = 0;
                memory[ptr + 2] = 0;
                memory[ptr + 3] = 0;
                // ipv4-socket-address payload at ptr+4:
                //   port(u16) + 4×u8 = 6 bytes total
                memory[ptr + 4] = (byte)(v4.Port & 0xff);
                memory[ptr + 5] = (byte)((v4.Port >> 8) & 0xff);
                memory[ptr + 6] = v4.Address[0];
                memory[ptr + 7] = v4.Address[1];
                memory[ptr + 8] = v4.Address[2];
                memory[ptr + 9] = v4.Address[3];
            }
            else if (addr is Wacs.WASI.Preview2.Sockets.Ipv6SocketAddress v6)
            {
                memory[ptr] = 1;     // variant disc = ipv6
                memory[ptr + 1] = 0;
                memory[ptr + 2] = 0;
                memory[ptr + 3] = 0;
                // ipv6-socket-address payload at ptr+4:
                //   port(u16) + 2 pad + flow-info(u32) +
                //   8×u16(16) + scope-id(u32) = 28 bytes
                memory[ptr + 4] = (byte)(v6.Port & 0xff);
                memory[ptr + 5] = (byte)((v6.Port >> 8) & 0xff);
                memory[ptr + 6] = 0;   // padding
                memory[ptr + 7] = 0;
                WriteI32LE(memory, ptr + 8, (int)v6.FlowInfo);
                for (int i = 0; i < 8; i++)
                {
                    memory[ptr + 12 + i * 2]
                        = (byte)(v6.Address[i] & 0xff);
                    memory[ptr + 13 + i * 2]
                        = (byte)((v6.Address[i] >> 8) & 0xff);
                }
                WriteI32LE(memory, ptr + 28, (int)v6.ScopeId);
            }
            else
            {
                throw new ArgumentException(
                    "Unknown IpSocketAddress subclass: "
                    + addr?.GetType());
            }
        }

        /// <summary>True iff <paramref name="t"/> is a
        /// <c>ValueTuple&lt;...&gt;</c> whose every type
        /// argument is a <see cref="WasiResourceAttribute"/>-
        /// marked class. Drives the stream-result wrapper's
        /// "tuple-of-resources" Ok payload — accept (3-tuple)
        /// and finish-connect (2-tuple) on tcp-socket.
        /// </summary>
        private static bool IsResourceTuple(Type t)
        {
            if (!t.IsGenericType) return false;
            if (!t.GetGenericTypeDefinition().Name.StartsWith(
                    "ValueTuple", StringComparison.Ordinal))
                return false;
            var args = t.GetGenericArguments();
            if (args.Length < 2) return false;
            foreach (var a in args)
                if (a.GetCustomAttribute<WasiResourceAttribute>() == null)
                    return false;
            return true;
        }

        /// <summary>True iff <paramref name="t"/> is a struct
        /// or class whose public instance fields are all
        /// primitives (and there is at least one). Used to
        /// detect record-of-primitives return types for the
        /// stream-result wrapper (metadata-hash-value etc.).
        /// </summary>
        private static bool IsPrimitiveRecord(Type t)
        {
            if (t == typeof(void)) return false;
            if (t.IsPrimitive || t.IsEnum) return false;
            if (t == typeof(string) || t == typeof(byte[])) return false;
            if (t.IsArray) return false;
            // Filter ValueTuple — it's structurally a record but
            // we treat it as a dedicated shape.
            if (t.IsGenericType
                && t.GetGenericTypeDefinition().Name.StartsWith(
                    "ValueTuple", StringComparison.Ordinal))
                return false;
            var fields = t.GetFields(
                BindingFlags.Public | BindingFlags.Instance);
            if (fields.Length == 0) return false;
            foreach (var f in fields)
                if (!IsPrimitive(f.FieldType)) return false;
            return true;
        }

        private static int PrimitiveByteSize(Type t)
        {
            if (t == typeof(bool) || t == typeof(byte)
                || t == typeof(sbyte)) return 1;
            if (t == typeof(short) || t == typeof(ushort)) return 2;
            if (t == typeof(int) || t == typeof(uint)
                || t == typeof(float)) return 4;
            if (t == typeof(long) || t == typeof(ulong)
                || t == typeof(double)) return 8;
            throw new NotSupportedException(
                "PrimitiveByteSize for " + t + " is unsupported.");
        }

        private static int AlignUp(int o, int a)
        {
            var rem = o % a;
            return rem == 0 ? o : o + (a - rem);
        }

        /// <summary>Write a primitive value at
        /// <paramref name="ptr"/> in <paramref name="memory"/>
        /// in little-endian order — the canonical-ABI default
        /// (and only) byte ordering for record fields.</summary>
        private static void WritePrimitiveLE(byte[] memory, int ptr,
            Type t, object v)
        {
            if (t == typeof(bool)) memory[ptr] = (bool)v ? (byte)1 : (byte)0;
            else if (t == typeof(byte)) memory[ptr] = (byte)v;
            else if (t == typeof(sbyte)) memory[ptr] = unchecked((byte)(sbyte)v);
            else if (t == typeof(short))
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(
                    memory.AsSpan(ptr, 2), (short)v);
            else if (t == typeof(ushort))
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                    memory.AsSpan(ptr, 2), (ushort)v);
            else if (t == typeof(int))
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                    memory.AsSpan(ptr, 4), (int)v);
            else if (t == typeof(uint))
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
                    memory.AsSpan(ptr, 4), (uint)v);
            else if (t == typeof(long))
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(
                    memory.AsSpan(ptr, 8), (long)v);
            else if (t == typeof(ulong))
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(
                    memory.AsSpan(ptr, 8), (ulong)v);
            else if (t == typeof(float))
            {
                // BinaryPrimitives.WriteSingleLittleEndian is
                // .NET 5+; netstandard2.1 needs the manual
                // bit-reinterpret via SingleToInt32Bits.
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                    memory.AsSpan(ptr, 4),
                    System.BitConverter.SingleToInt32Bits((float)v));
            }
            else if (t == typeof(double))
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(
                    memory.AsSpan(ptr, 8),
                    System.BitConverter.DoubleToInt64Bits((double)v));
            }
            else
                throw new NotSupportedException(
                    "WritePrimitiveLE for " + t + " is unsupported.");
        }

        /// <summary>True iff the return type is primitive /
        /// void and every param is either a primitive flat slot
        /// or a record-of-primitives (which canon-lowers to its
        /// fields flattened across wire slots).</summary>
        private static bool IsPrimitiveSignature(MethodInfo m)
        {
            if (!IsPrimitiveOrVoid(m.ReturnType)) return false;
            foreach (var p in m.GetParameters())
                if (!IsPrimitiveOrRecordOfPrimitives(p.ParameterType))
                    return false;
            return true;
        }

        /// <summary>True iff <paramref name="t"/> is a primitive
        /// OR a struct/class with at least one public field, all
        /// primitive-typed (the host C# representation of a WIT
        /// record-of-primitives param).</summary>
        private static bool IsPrimitiveOrRecordOfPrimitives(Type t)
        {
            if (IsPrimitive(t)) return true;
            if (!t.IsValueType && !t.IsClass) return false;
            // Exclude reference-type aggregates like string /
            // byte[] / arrays / collections — those need their
            // own wrapper paths.
            if (t == typeof(string) || t.IsArray) return false;
            var fields = t.GetFields(
                BindingFlags.Public | BindingFlags.Instance);
            if (fields.Length == 0) return false;
            foreach (var f in fields)
                if (!IsPrimitive(f.FieldType)) return false;
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
            // Enums on the WIT side are no-payload variants —
            // wire form is the underlying integer (typically
            // i32). Map to the C# enum's underlying type and
            // then through the same int-widening rules.
            if (t.IsEnum) return ToWireType(System.Enum.GetUnderlyingType(t));
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

            // Wire types are what WacsCore's BindHostFunction
            // accepts on its delegate parameter list — bool /
            // byte / sbyte / short / ushort etc. don't round-
            // trip through its ResultType validator, so we map
            // each to int / long and convert inside the body
            // before invoking the impl method. Record-of-
            // primitives params canon-lower to their fields
            // flattened across wire slots.
            var paramInfos = m.GetParameters();
            // Pre-compute wire-slot expansion: for each host
            // param, either a single wire type (primitive) or
            // one wire type per public field (record).
            var wireSlots = new System.Collections.Generic.List<Type>();
            wireSlots.Add(typeof(ExecContext));
            // Per-host-param: number of wire slots it occupies,
            // and (for records) the list of fields.
            var paramSlotCounts = new int[paramInfos.Length];
            var paramRecordFields = new FieldInfo[paramInfos.Length][];
            for (int i = 0; i < paramInfos.Length; i++)
            {
                var pt = paramInfos[i].ParameterType;
                if (IsPrimitive(pt))
                {
                    wireSlots.Add(ToWireType(pt));
                    paramSlotCounts[i] = 1;
                }
                else
                {
                    // Record-of-primitives: expand to fields.
                    var fields = pt.GetFields(
                        BindingFlags.Public | BindingFlags.Instance);
                    foreach (var f in fields)
                        wireSlots.Add(ToWireType(f.FieldType));
                    paramSlotCounts[i] = fields.Length;
                    paramRecordFields[i] = fields;
                }
            }
            var paramTypes = wireSlots.ToArray();

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
                allTypes[paramTypes.Length] = ToWireType(m.ReturnType);
                delegateType = OpenFuncType(allTypes.Length)
                    .MakeGenericType(allTypes);
            }

            // Expression tree: (ctx, w1, w2, …) =>
            //   (TRet)impl.Method(TParam1, TParam2, …)
            // Where each TParamI either reads a single wire
            // slot (primitive) or constructs a record from
            // multiple consecutive wire slots.
            var lambdaParams = new ParameterExpression[paramTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            int wireIdx = 1;
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (paramSlotCounts[i] == 1 && paramRecordFields[i] == null)
                {
                    lambdaParams[wireIdx] = Expression.Parameter(
                        paramTypes[wireIdx], paramInfos[i].Name);
                    wireIdx++;
                }
                else
                {
                    var fields = paramRecordFields[i];
                    for (int f = 0; f < fields.Length; f++)
                    {
                        lambdaParams[wireIdx] = Expression.Parameter(
                            paramTypes[wireIdx],
                            paramInfos[i].Name + "_" + fields[f].Name);
                        wireIdx++;
                    }
                }
            }

            var implExpr = Expression.Constant(impl);
            var argExprs = new Expression[paramInfos.Length];
            wireIdx = 1;
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (paramSlotCounts[i] == 1 && paramRecordFields[i] == null)
                {
                    Expression e = lambdaParams[wireIdx];
                    if (paramInfos[i].ParameterType != e.Type)
                    {
                        if (paramInfos[i].ParameterType == typeof(bool))
                            e = Expression.NotEqual(e,
                                Expression.Constant(0, e.Type));
                        else
                            e = Expression.Convert(e,
                                paramInfos[i].ParameterType);
                    }
                    argExprs[i] = e;
                    wireIdx++;
                }
                else
                {
                    // Build `new TRecord { Field0 = ...,
                    // Field1 = ..., }` with each field cast
                    // from its wire type to the field type.
                    // Structs don't have explicit parameterless
                    // ctors but Expression.New(Type) for value
                    // types creates a default value.
                    var fields = paramRecordFields[i];
                    var pt = paramInfos[i].ParameterType;
                    NewExpression newExpr;
                    if (pt.IsValueType)
                    {
                        newExpr = Expression.New(pt);
                    }
                    else
                    {
                        var ctor = pt.GetConstructor(Type.EmptyTypes);
                        if (ctor == null)
                            throw new InvalidOperationException(
                                "Record param type " + pt + " requires "
                                + "a parameterless constructor for the "
                                + "auto-binder's flattened-args path.");
                        newExpr = Expression.New(ctor);
                    }
                    var bindings = new MemberBinding[fields.Length];
                    for (int f = 0; f < fields.Length; f++)
                    {
                        Expression e = lambdaParams[wireIdx + f];
                        if (fields[f].FieldType != e.Type)
                        {
                            if (fields[f].FieldType == typeof(bool))
                                e = Expression.NotEqual(e,
                                    Expression.Constant(0, e.Type));
                            else
                                e = Expression.Convert(e,
                                    fields[f].FieldType);
                        }
                        bindings[f] = Expression.Bind(fields[f], e);
                    }
                    argExprs[i] = Expression.MemberInit(newExpr, bindings);
                    wireIdx += fields.Length;
                }
            }
            Expression call = Expression.Call(implExpr, m, argExprs);
            // Cast return value back to wire type if the impl's
            // return type is narrower (e.g. ushort → int).
            if (m.ReturnType != typeof(void)
                && m.ReturnType != ToWireType(m.ReturnType))
            {
                if (m.ReturnType == typeof(bool))
                    call = Expression.Condition(call,
                        Expression.Constant(1),
                        Expression.Constant(0));
                else
                    call = Expression.Convert(call,
                        ToWireType(m.ReturnType));
            }
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
            9 => typeof(Func<,,,,,,,,>),
            10 => typeof(Func<,,,,,,,,,>),
            11 => typeof(Func<,,,,,,,,,,>),
            12 => typeof(Func<,,,,,,,,,,,>),
            13 => typeof(Func<,,,,,,,,,,,,>),
            14 => typeof(Func<,,,,,,,,,,,,,>),
            15 => typeof(Func<,,,,,,,,,,,,,,>),
            16 => typeof(Func<,,,,,,,,,,,,,,,>),
            17 => typeof(Func<,,,,,,,,,,,,,,,,>),
            _ => throw new NotSupportedException(
                "Func arities above 17 are a follow-up."),
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
            9 => typeof(Action<,,,,,,,,>),
            10 => typeof(Action<,,,,,,,,,>),
            11 => typeof(Action<,,,,,,,,,,>),
            12 => typeof(Action<,,,,,,,,,,,>),
            13 => typeof(Action<,,,,,,,,,,,,>),
            14 => typeof(Action<,,,,,,,,,,,,,>),
            15 => typeof(Action<,,,,,,,,,,,,,,>),
            16 => typeof(Action<,,,,,,,,,,,,,,,>),
            _ => throw new NotSupportedException(
                "Action arities above 16 are a follow-up."),
        };
    }
}
