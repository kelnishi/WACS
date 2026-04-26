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
                if (IsResourceReturnPrimitiveParams(m))
                {
                    BindResourceReturnMethod(
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
                if (IsRecordOfPrimitivesReturnPrimitiveParams(m))
                {
                    BindRecordReturnMethod(
                        runtime, namespaceName, impl, m);
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
            foreach (var m in resourceType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.DeclaringType == typeof(object)) continue;
                if (m.IsSpecialName) continue;
                BindResourceMethod(runtime, namespaceName,
                    witName, table, resourceType, m);
            }

            // [resource-drop]Name — wasm passes i32 handle,
            // we drop the table entry (and Dispose).
            BindResourceDrop(runtime, namespaceName, witName, table);
        }

        /// <summary>Bind one method on a resource class. Wire
        /// form: first i32 is the handle (self via
        /// borrow&lt;T&gt;), then host params follow. Method
        /// return is dispatched the same way as a regular host
        /// method — primitive / aggregate / etc.</summary>
        private static void BindResourceMethod(
            WasmRuntime runtime, string namespaceName,
            string witResourceName, ResourceTable table,
            Type resourceType, MethodInfo m)
        {
            var importName = "[method]" + witResourceName + "."
                + ToKebabCase(m.Name);
            var paramInfos = m.GetParameters();

            // Only primitive-shaped methods for v0; aggregate
            // returns / params are follow-ups (they'd reuse the
            // existing wrappers but with a leading handle slot).
            if (!IsPrimitiveOrVoid(m.ReturnType)) return;
            foreach (var p in paramInfos)
                if (!IsPrimitive(p.ParameterType)) return;

            // Wrapper signature: ExecContext, handle (i32),
            // [host params...] → wire return type (or void).
            var wireParamTypes = new Type[paramInfos.Length + 2];
            wireParamTypes[0] = typeof(ExecContext);
            wireParamTypes[1] = typeof(int);
            for (int i = 0; i < paramInfos.Length; i++)
                wireParamTypes[i + 2] = ToWireType(paramInfos[i].ParameterType);

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
            for (int i = 0; i < paramInfos.Length; i++)
                lambdaParams[i + 2] = Expression.Parameter(
                    wireParamTypes[i + 2], paramInfos[i].Name);

            // instance = (T)table.Get(self)
            var tableConst = Expression.Constant(table);
            var getMethod = typeof(ResourceTable).GetMethod(
                nameof(ResourceTable.Get))!;
            var instanceExpr = Expression.Convert(
                Expression.Call(tableConst, getMethod, lambdaParams[1]),
                resourceType);

            var argExprs = new Expression[paramInfos.Length];
            for (int i = 0; i < paramInfos.Length; i++)
            {
                Expression e = lambdaParams[i + 2];
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

            // Wire types are what WacsCore's BindHostFunction
            // accepts on its delegate parameter list — bool /
            // byte / sbyte / short / ushort etc. don't round-
            // trip through its ResultType validator, so we map
            // each to int / long and convert inside the body
            // before invoking the impl method.
            var paramInfos = m.GetParameters();
            var paramTypes = new Type[paramInfos.Length + 1];
            paramTypes[0] = typeof(ExecContext);
            for (int i = 0; i < paramInfos.Length; i++)
                paramTypes[i + 1] = ToWireType(paramInfos[i].ParameterType);

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

            // Expression tree: (ctx, p1, p2, …) =>
            //   (TRet)impl.Method((TParam)p1, (TParam)p2, …)
            // The casts handle wire-type ↔ host-type mismatches
            // (e.g. wire int → host bool / byte). The
            // ExecContext parameter is accepted but ignored —
            // primitive WASI methods don't read host state.
            var lambdaParams = new ParameterExpression[paramTypes.Length];
            lambdaParams[0] = Expression.Parameter(typeof(ExecContext), "ctx");
            for (int i = 0; i < paramInfos.Length; i++)
                lambdaParams[i + 1] = Expression.Parameter(
                    paramTypes[i + 1], paramInfos[i].Name);

            var implExpr = Expression.Constant(impl);
            var argExprs = new Expression[paramInfos.Length];
            for (int i = 0; i < paramInfos.Length; i++)
            {
                Expression e = lambdaParams[i + 1];
                if (paramInfos[i].ParameterType != e.Type)
                {
                    // bool isn't directly castable from int —
                    // express as `wireVal != 0`.
                    if (paramInfos[i].ParameterType == typeof(bool))
                        e = Expression.NotEqual(e,
                            Expression.Constant(0, e.Type));
                    else
                        e = Expression.Convert(e,
                            paramInfos[i].ParameterType);
                }
                argExprs[i] = e;
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
