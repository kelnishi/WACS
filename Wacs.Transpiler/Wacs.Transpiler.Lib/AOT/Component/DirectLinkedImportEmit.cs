// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.ComponentModel.CanonicalABI;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Emits inline IL for a guest <c>call $import</c> that
    /// <see cref="HostPackageResolver"/> matched against a
    /// <c>[WitSource]</c>-tagged typed interface.
    ///
    /// <para>v0 scope: free-function imports with zero or more
    /// CLR-primitive-shaped params and zero or one primitive return.
    /// CLR types that differ from the wasm wire form by a narrow
    /// conversion (byte/sbyte/short/ushort/uint/ulong/bool ↔ i32/i64)
    /// emit the matching <c>conv.*</c> opcode. Aggregate types
    /// (string / list / option / result / record / tuple / variant)
    /// and resource methods are out of scope here — the call site
    /// keeps falling back to the legacy delegate-table dispatch
    /// until <c>CanEmitDirect</c> reports true for them.</para>
    ///
    /// <para>Generated IL shape for a typical zero-param,
    /// primitive-return free-function import:
    /// <code>
    /// ldarg_0                  // ThinContext ctx
    /// ldfld HostBundle         // object?
    /// castclass &lt;BundleType&gt; // typed bundle
    /// callvirt get_&lt;Iface&gt;   // typed I*
    /// callvirt I.Method        // returns primitive on CIL stack
    /// </code>
    /// For multi-param calls, params already on the CIL stack are
    /// spilled to locals, the bundle/interface is pushed, then the
    /// params are re-pushed with conversions in argument order.</para>
    /// </summary>
    public static class DirectLinkedImportEmit
    {
        /// <summary>
        /// True when the (binding, function-type) pair lowers cleanly
        /// to direct IL with the v0 primitive-only emitter. False
        /// means the call site should fall back to the legacy
        /// <c>ImportDelegates[]</c> dispatch.
        ///
        /// <para>Resource methods are accepted only when the binding
        /// kind is <see cref="HostPackageResolver.ResourceMethodKind.Instance"/>
        /// — the wasm side carries a leading i32 handle that's
        /// translated via <see cref="ThinContext.Resources"/>'s
        /// <c>GetResource(Type, int)</c> lookup, and the typed C#
        /// instance method runs on the resolved instance. Static
        /// resource methods and constructors stay deferred.</para>
        /// </summary>
        public static bool CanEmitDirect(HostPackageResolver.Binding binding,
            FunctionType wasmType,
            HostPackageResolver? resolver = null)
        {
            var method = binding.Method;
            var clrParams = method.GetParameters();
            var wasmParams = wasmType.ParameterTypes.Types;
            var wasmResults = wasmType.ResultType.Types;

            // Resource-method shape:
            //   [method]X.foo (Instance)    : wasm has a leading i32
            //                                 handle (the resolved
            //                                 instance becomes `this`).
            //   [static]X.foo (Static)      : no leading handle,
            //                                 wasm shape == clr shape.
            //   [constructor]X (Constructor): no leading handle,
            //                                 wasm result == 1×i32
            //                                 (the handle for the
            //                                 newly-allocated instance).
            int wasmParamOffset = 0;
            if (binding.IsResourceMethod)
            {
                switch (binding.ResourceKind)
                {
                    case HostPackageResolver.ResourceMethodKind.Instance:
                        wasmParamOffset = 1;
                        if (wasmParams.Length < 1
                            || wasmParams[0] != ValType.I32) return false;
                        if (!method.IsVirtual && !method.IsAbstract)
                            return false;   // must be a callvirt target
                        break;
                    case HostPackageResolver.ResourceMethodKind.Static:
                        if (!method.IsStatic) return false;
                        break;
                    case HostPackageResolver.ResourceMethodKind.Constructor:
                        // Constructor: wasm returns exactly one i32
                        // (the handle). Two CLR shapes are accepted:
                        //   (a) Static factory returning the resource
                        //       instance (test bindings + hand-written
                        //       hosts that prefer static factories).
                        //   (b) Instance method `void Create(args)` on
                        //       the resource interface itself
                        //       (Wacs.ComponentModel.Bindgen.SourceGen
                        //       output — the resource class has a
                        //       parameterless ctor + Create separation
                        //       for exactly this lift). The IL emit
                        //       discovers the impl type from the
                        //       resolver's host packages.
                        if (wasmResults.Length != 1
                            || wasmResults[0] != ValType.I32) return false;
                        if (method.IsStatic)
                        {
                            if (method.ReturnType == typeof(void)) return false;
                            if (!binding.InterfaceType.IsAssignableFrom(
                                method.ReturnType)) return false;
                        }
                        else
                        {
                            // SourceGen shape — must be void return AND
                            // resolver must be able to find a concrete
                            // impl class with a parameterless ctor.
                            if (method.ReturnType != typeof(void)) return false;
                            if (resolver == null
                                || !resolver.TryFindResourceImpl(
                                    binding.InterfaceType, out _))
                                return false;
                        }
                        break;
                    default:
                        return false;
                }
            }

            // Hoist isConstructor here so the aggregate-return
            // detection can exclude constructors (which already
            // have their own retArea-shaped wire — i32 handle).
            bool isConstructorEarly = binding.IsResourceMethod
                && binding.ResourceKind ==
                    HostPackageResolver.ResourceMethodKind.Constructor;

            // Aggregate-return detection: if the wasm sig has 0
            // returns AND the CLR returns a non-void aggregate AND
            // the last wasm param is i32, we're in retArea-return
            // mode — the trailing i32 is the retArea ptr (NOT a
            // CLR-side param). Reduce the effective wasm-param
            // count by 1 for the per-CLR-param walk.
            bool isAggregateReturn = false;
            if (!isConstructorEarly
                && wasmResults.Length == 0
                && method.ReturnType != typeof(void)
                && wasmParams.Length >= 1
                && wasmParams[wasmParams.Length - 1] == ValType.I32
                && IsAggregateReturnSupported(method.ReturnType, resolver))
            {
                isAggregateReturn = true;
            }
            int effectiveWasmCount = isAggregateReturn
                ? wasmParams.Length - 1
                : wasmParams.Length;

            // Each CLR param contributes its canon-ABI flat-slot
            // count to the wasm-side. Sum and check against
            // wasmParams (after skipping the resource handle slot
            // and any retArea slot).
            int expectedRemainingWasm = 0;
            for (int i = 0; i < clrParams.Length; i++)
            {
                int slots = CanonicalSlotCount(clrParams[i].ParameterType,
                    out var perWasmType, resolver);
                if (slots < 0) return false;
                expectedRemainingWasm += slots;
                // Each contributing wasm slot must match the CLR
                // param's expected wire shape (i32 / i64 / etc.).
                for (int s = 0; s < slots; s++)
                {
                    int wIdx = wasmParamOffset + (expectedRemainingWasm - slots) + s;
                    if (wIdx >= effectiveWasmCount) return false;
                    if (wasmParams[wIdx] != perWasmType[s]) return false;
                }
            }
            if (wasmParamOffset + expectedRemainingWasm
                != effectiveWasmCount) return false;
            if (wasmResults.Length > 1) return false;

            // Constructor return: already validated as i32 (handle)
            // wire / interface CLR. Skip the primitive-compat check.
            bool isConstructor = binding.IsResourceMethod
                && binding.ResourceKind ==
                    HostPackageResolver.ResourceMethodKind.Constructor;
            if (!isConstructor && !isAggregateReturn)
            {
                if (wasmResults.Length == 1)
                {
                    if (method.ReturnType == typeof(void)) return false;
                    // own<R> return: wasm i32 (handle) + CLR
                    // resource interface. The IL allocates a handle
                    // from the returned instance via Resources.
                    bool isOwnReturn = wasmResults[0] == ValType.I32
                        && resolver != null
                        && resolver.IsResourceInterface(method.ReturnType)
                        && resolver.PreferredResourcesType != null;
                    if (!isOwnReturn
                        && !IsPrimitiveCompatible(method.ReturnType,
                            wasmResults[0])) return false;
                }
                else
                {
                    // Wasm void return — the C# method must also
                    // return void OR Unit (Unit is a struct;
                    // zero-size). v0 only accepts plain void.
                    if (method.ReturnType != typeof(void)) return false;
                }
            }
            // Constructor args ride through the same per-CLR-param
            // dispatcher as resource-method args (own<R>, primitive,
            // aggregate, etc.) — IL emit pushes them in order before
            // calling the static factory. No additional gating
            // beyond the per-param IsPrimitiveCompatible /
            // CanonicalSlotCount checks above.
            return true;
        }

        /// <summary>
        /// Emit the inline IL. Caller has already pushed wasm params
        /// onto the CIL stack in declaration order. Caller must also
        /// have already pushed <c>ThinContext ctx</c> as arg-0 of
        /// the enclosing static method (the standard transpiled
        /// function-method shape).
        ///
        /// <para>For resource-method bindings the
        /// <paramref name="resourcesType"/> is required — it's the
        /// CLR type that exposes <c>object GetResource(Type, int)</c>
        /// for handle resolution. Free-function bindings ignore it.</para>
        /// </summary>
        public static void Emit(ILGenerator il,
            HostPackageResolver.Binding binding,
            FunctionType wasmType,
            Type bundleType,
            Type? resourcesType = null,
            HostPackageResolver? resolver = null)
        {
            if (bundleType == null) throw new ArgumentNullException(
                nameof(bundleType));
            if (binding.IsResourceMethod && resourcesType == null)
                throw new ArgumentNullException(
                    nameof(resourcesType),
                    "resourcesType is required for resource-method binding "
                    + binding.Module + "." + binding.Entity);

            // CanEmitDirect is the gate. If we're called for a binding
            // it would refuse, the per-shape branches below will hit
            // their defensive throws — but pre-check here so the
            // message names the offending (module, entity) up front.
            if (!CanEmitDirect(binding, wasmType, resolver))
                throw new InvalidOperationException(
                    "DirectLinkedImportEmit.Emit was called for a binding "
                    + "that CanEmitDirect rejects: " + binding.Module + "."
                    + binding.Entity + " (CLR method "
                    + binding.Method.DeclaringType?.FullName + "."
                    + binding.Method.Name + "). The call-site emitter "
                    + "should have routed this to the legacy delegate "
                    + "dispatch instead — file a bug if this fires.");

            var method = binding.Method;
            var clrParams = method.GetParameters();
            int clrParamCount = clrParams.Length;
            var wasmParams = wasmType.ParameterTypes.Types;
            int wasmParamCount = wasmParams.Length;

            // Spill wasm params already on the CIL stack into locals.
            // Order on the stack is param0..paramN-1 (top of stack =
            // last param), so we pop in reverse. For resource methods
            // the FIRST spill local is the i32 handle.
            var temps = new LocalBuilder[wasmParamCount];
            for (int i = wasmParamCount - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(WasmStackType(wasmParams[i]));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Resource-method classification (one of):
            //   FreeFunction:        free function (not a resource)
            //   ResourceInstance:    [method]X.foo — pop handle, look up
            //   ResourceStatic:      [static]X.foo — no handle, call static
            //   ResourceConstructor: [constructor]X — no handle, factory
            //                        returns instance, IL allocates handle
            bool isInstance = binding.IsResourceMethod
                && binding.ResourceKind ==
                    HostPackageResolver.ResourceMethodKind.Instance;
            bool isStatic = binding.IsResourceMethod
                && binding.ResourceKind ==
                    HostPackageResolver.ResourceMethodKind.Static;
            bool isConstructor = binding.IsResourceMethod
                && binding.ResourceKind ==
                    HostPackageResolver.ResourceMethodKind.Constructor;

            int wasmParamOffset = isInstance ? 1 : 0;

            // SourceGen-shape constructor: instance `void Create(args)`
            // method on the resource interface, with a separately-
            // discovered impl class that has a public parameterless
            // ctor. The IL `Newobj`s a fresh impl, stashes it for the
            // post-call AllocateResource, and pushes it as `this` for
            // the upcoming Callvirt.
            bool isVoidInstanceCtor = isConstructor
                && !method.IsStatic
                && method.ReturnType == typeof(void);
            LocalBuilder? voidCtorInstance = null;
            if (isVoidInstanceCtor)
            {
                if (resolver == null
                    || !resolver.TryFindResourceImpl(
                        binding.InterfaceType, out var implType))
                    return; // gate already validated this; defensive.
                var implCtor = implType
                    .GetConstructor(System.Type.EmptyTypes)!;
                voidCtorInstance = il.DeclareLocal(binding.InterfaceType);
                il.Emit(OpCodes.Newobj, implCtor);
                il.Emit(OpCodes.Dup);                     // [inst, inst]
                il.Emit(OpCodes.Stloc, voidCtorInstance); // [inst]
                // Cast to interface for the upcoming Callvirt's
                // method declaring-type.
                il.Emit(OpCodes.Castclass, binding.InterfaceType);
            }

            // Push the `this` arg for the callvirt — only for
            // free-function or instance-method calls. Static and
            // constructor methods are static dispatch (except the
            // void-instance ctor shape handled above).
            if (isInstance)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ResourcesField);
                il.Emit(OpCodes.Castclass, resourcesType!);

                // Type literal for the resource interface — pushed
                // as RuntimeTypeHandle then converted via Type.GetTypeFromHandle.
                il.Emit(OpCodes.Ldtoken, binding.InterfaceType);
                il.Emit(OpCodes.Call, GetTypeFromHandleMethod);

                // Push the handle (first wasm param).
                il.Emit(OpCodes.Ldloc, temps[0]);

                // Resolve via convention: public method
                // `object GetResource(System.Type, int)` on the
                // resources class. Lookup at IL-emit time so trim/
                // AOT analysis sees the exact MethodInfo.
                var getResource = ResolveGetResourceMethod(resourcesType!);
                il.Emit(OpCodes.Callvirt, getResource);
                il.Emit(OpCodes.Castclass, binding.InterfaceType);
            }
            else if (!isStatic && !isConstructor)
            {
                // Free function — `this` for the typed-interface callvirt.
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, HostBundleField);
                il.Emit(OpCodes.Castclass, bundleType);

                var bundleProperty = ResolveBundleProperty(bundleType,
                    binding.InterfaceType);
                il.Emit(OpCodes.Callvirt, bundleProperty.GetGetMethod()!);
            }
            // Static / Constructor: no `this` push — static dispatch.

            // Re-push remaining params from temps. Each CLR param
            // peels CanonicalSlotCount(clr) wasm slots (1 for prim,
            // 2 for string ptr+len) and lifts them to the typed
            // representation. The wasm-side cursor advances by the
            // peeled slot count.
            // Aggregate-return mode mirrors the CanEmitDirect
            // detection: trailing wasm i32 is the retArea ptr.
            // Resource INSTANCE methods can also return aggregates
            // (e.g. wasi-hello's [method]output-stream.blocking-
            // write-and-flush returning Result<Unit, StreamError>).
            // Constructors are excluded — their wasm result IS the
            // i32 handle, not a retArea ptr. Statics behave like
            // free functions here.
            bool emitAggregateReturn = !isConstructor
                && wasmType.ResultType.Types.Length == 0
                && method.ReturnType != typeof(void)
                && wasmParams.Length >= 1
                && wasmParams[wasmParams.Length - 1] == ValType.I32
                && IsAggregateReturnSupported(method.ReturnType, resolver);
            int retAreaSlot = emitAggregateReturn
                ? wasmParams.Length - 1 : -1;

            var stringEncoding = binding.StringEncoding;
            int wasmCursor = wasmParamOffset;
            for (int i = 0; i < clrParamCount; i++)
            {
                var clrType = clrParams[i].ParameterType;
                int slots = CanonicalSlotCount(clrType, out _, resolver);
                EmitLiftForType(il, clrType, wasmParams, temps,
                    wasmCursor, resolver, resourcesType,
                    stringEncoding);
                wasmCursor += slots;
            }

            // Static and constructor methods use static dispatch.
            // Instance and free-function methods use callvirt
            // (free fns go through a typed-interface property).
            // Static + static-factory constructor → static dispatch.
            // Void-instance constructor → callvirt against the
            // freshly-created impl pushed earlier.
            // Instance / free-function → callvirt.
            if ((isStatic || isConstructor) && !isVoidInstanceCtor)
                il.Emit(OpCodes.Call, method);
            else
                il.Emit(OpCodes.Callvirt, method);

            if (emitAggregateReturn)
            {
                // Stash the returned aggregate, then walk its
                // fields in declaration order, computing canon-ABI
                // -aligned offsets, and write each field into
                // memory[retAreaPtr+offset] via the matching
                // PrimitiveStore.StoreXxx helper. Records use
                // property getters; ValueTuple<...> uses Item1/2/...
                // fields directly.
                var returnLocal = il.DeclareLocal(method.ReturnType);
                il.Emit(OpCodes.Stloc, returnLocal);

                bool isTuple = method.ReturnType.IsGenericType
                    && IsValueTupleType(method.ReturnType
                        .GetGenericTypeDefinition());
                bool isOption = method.ReturnType.IsGenericType
                    && method.ReturnType.GetGenericTypeDefinition()
                        == typeof(Option<>);
                bool isResult = method.ReturnType.IsGenericType
                    && method.ReturnType.GetGenericTypeDefinition()
                        == typeof(Result<,>);
                bool isVariant = IsLikelyVariantBase(method.ReturnType);
                bool isStringReturn = method.ReturnType == typeof(string);
                bool isByteArrayReturn = method.ReturnType == typeof(byte[]);
                bool isStringArrayReturn = method.ReturnType.IsArray
                    && method.ReturnType.GetArrayRank() == 1
                    && method.ReturnType.GetElementType() == typeof(string);
                bool isByteArrayListReturn = method.ReturnType.IsArray
                    && method.ReturnType.GetArrayRank() == 1
                    && method.ReturnType.GetElementType() == typeof(byte[]);
                bool isPrimJaggedArrayReturn = !isByteArrayListReturn
                    && method.ReturnType.IsArray
                    && method.ReturnType.GetArrayRank() == 1
                    && method.ReturnType.GetElementType()!.IsArray
                    && method.ReturnType.GetElementType()!
                        .GetArrayRank() == 1
                    && IsListPrimitiveElement(
                        method.ReturnType.GetElementType()!
                            .GetElementType()!);
                bool isStringJaggedArrayReturn = method.ReturnType.IsArray
                    && method.ReturnType.GetArrayRank() == 1
                    && method.ReturnType.GetElementType()!.IsArray
                    && method.ReturnType.GetElementType()!
                        .GetArrayRank() == 1
                    && method.ReturnType.GetElementType()!
                        .GetElementType() == typeof(string);
                bool isByteArrayJaggedArrayReturn = method.ReturnType.IsArray
                    && method.ReturnType.GetArrayRank() == 1
                    && method.ReturnType.GetElementType()!.IsArray
                    && method.ReturnType.GetElementType()!
                        .GetArrayRank() == 1
                    && method.ReturnType.GetElementType()!
                        .GetElementType() == typeof(byte[]);
                bool isPrimArrayReturn = !isByteArrayReturn
                    && !isStringArrayReturn
                    && !isByteArrayListReturn
                    && !isPrimJaggedArrayReturn
                    && method.ReturnType.IsArray
                    && method.ReturnType.GetArrayRank() == 1
                    && IsListPrimitiveElement(
                        method.ReturnType.GetElementType()!);
                bool isAggregateArrayReturn = method.ReturnType.IsArray
                    && method.ReturnType.GetArrayRank() == 1
                    && (IsTupleOfPrimitives(
                            method.ReturnType.GetElementType()!)
                        || IsRecordOfPrimitives(
                            method.ReturnType.GetElementType()!)
                        // Resource-bearing tuple/record elements use
                        // the same packed-stride layout but require
                        // resolver + resourcesType for per-field
                        // AllocateResource on store.
                        || IsTupleOfFlatFields(
                            method.ReturnType.GetElementType()!,
                            resolver)
                        || IsRecordOfFlatFields(
                            method.ReturnType.GetElementType()!,
                            resolver));
                bool isResourceArrayReturn = method.ReturnType.IsArray
                    && method.ReturnType.GetArrayRank() == 1
                    && resolver != null
                    && resolver.IsResourceInterface(
                        method.ReturnType.GetElementType()!)
                    && resourcesType != null;
                bool isOptionOrResultArrayReturn = method.ReturnType.IsArray
                    && method.ReturnType.GetArrayRank() == 1
                    && ((method.ReturnType.GetElementType()!.IsGenericType
                            && (method.ReturnType.GetElementType()!
                                    .GetGenericTypeDefinition() == typeof(Option<>)
                                || method.ReturnType.GetElementType()!
                                    .GetGenericTypeDefinition() == typeof(Result<,>)))
                        || IsLikelyVariantBase(
                            method.ReturnType.GetElementType()!));

                int offsetSoFar = 0;
                if (isOptionOrResultArrayReturn)
                {
                    EmitListOfOptionOrResultReturn(il,
                        method.ReturnType.GetElementType()!,
                        returnLocal, temps[retAreaSlot],
                        resolver, resourcesType, stringEncoding);
                }
                else if (isResourceArrayReturn)
                {
                    EmitListOfResourceReturn(il,
                        method.ReturnType.GetElementType()!,
                        returnLocal, temps[retAreaSlot],
                        resourcesType!);
                }
                else if (isAggregateArrayReturn)
                {
                    EmitListOfRecordOrTupleReturn(il,
                        method.ReturnType.GetElementType()!,
                        returnLocal, temps[retAreaSlot],
                        stringEncoding,
                        resolver, resourcesType);
                }
                else
                if (isStringReturn || isByteArrayReturn
                    || isPrimArrayReturn || isStringArrayReturn
                    || isByteArrayListReturn
                    || isPrimJaggedArrayReturn
                    || isStringJaggedArrayReturn
                    || isByteArrayJaggedArrayReturn)
                {
                    // String / list<T> retArea layout: i32 ptr @0,
                    // i32 len/count @4. Encode (UTF-8 for string;
                    // raw LE bytes for primitive arrays; outer
                    // (ptr,len) pairs for string[]), call
                    // cabi_realloc to allocate guest buffer(s),
                    // copy bytes, write (ptr, len) pair.
                    //
                    //   StoreXxx(memory_instance, retArea, value,
                    //            ctx.CabiRealloc)
                    // Pass MemoryInstance (not byte[].Data) so the
                    // helper re-fetches mem.Data after each
                    // cabi_realloc — that call may grow memory and
                    // reassign Data, leaving any captured reference
                    // stale.
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, MemoriesField);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ldelem_Ref);
                    il.Emit(OpCodes.Ldloc, temps[retAreaSlot]);
                    il.Emit(OpCodes.Ldloc, returnLocal);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, CabiReallocField);
                    MethodInfo storeMethod;
                    if (isStringReturn) storeMethod =
                        ResolveStoreStringMethod(stringEncoding);
                    else if (isByteArrayReturn) storeMethod = StoreByteArrayMethod;
                    else if (isStringArrayReturn) storeMethod = StoreStringListMethod;
                    else if (isByteArrayListReturn) storeMethod = StoreByteArrayListMethod;
                    else if (isStringJaggedArrayReturn) storeMethod = StoreListOfStringListMethod;
                    else if (isByteArrayJaggedArrayReturn) storeMethod = StoreListOfByteArrayListMethod;
                    else if (isPrimJaggedArrayReturn) storeMethod =
                        ResolveStorePrimArrayListMethod(
                            method.ReturnType.GetElementType()!
                                .GetElementType()!);
                    else storeMethod = ResolveStorePrimitiveArrayMethod(
                        method.ReturnType.GetElementType()!);
                    il.Emit(OpCodes.Call, storeMethod);
                }
                else if (isVariant)
                {
                    EmitVariantStoreAt(il, method.ReturnType,
                        returnLocal, temps[retAreaSlot], 0,
                        resolver, resourcesType, stringEncoding);
                }
                else
                if (isOption)
                {
                    EmitOptionStoreAt(il, method.ReturnType,
                        returnLocal, temps[retAreaSlot], 0,
                        resolver, resourcesType, stringEncoding);
                }
                else if (isResult)
                {
                    EmitResultStoreAt(il, method.ReturnType,
                        returnLocal, temps[retAreaSlot], 0,
                        resolver, resourcesType, stringEncoding);
                }
                else if (isTuple)
                {
                    EmitInlineRecordOrTupleStore(il, method.ReturnType,
                        temps[retAreaSlot], 0, returnLocal,
                        stringEncoding,
                        resolver, resourcesType);
                }
                else
                {
                    EmitInlineRecordOrTupleStore(il, method.ReturnType,
                        temps[retAreaSlot], 0, returnLocal,
                        stringEncoding,
                        resolver, resourcesType);
                }
                // Wasm sig has 0 returns; nothing further on the stack.
            }
            else if (isConstructor)
            {
                // Allocate a handle for the constructed instance via
                // `Resources.AllocateResource(Type, object) → int`.
                // For the static-factory shape the instance is on the
                // stack from the call we just emitted; for the void-
                // instance-method shape the call returned void and we
                // saved the instance to a local before the call.
                var allocate = ResolveAllocateResourceMethod(
                    resourcesType!);

                LocalBuilder instLocal;
                if (isVoidInstanceCtor)
                {
                    // Stack is empty (Create returned void); reuse
                    // the local we stashed pre-call.
                    instLocal = voidCtorInstance!;
                }
                else
                {
                    // Stack: [instance from static factory] — stash.
                    instLocal = il.DeclareLocal(binding.InterfaceType);
                    il.Emit(OpCodes.Stloc, instLocal);
                }

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ResourcesField);
                il.Emit(OpCodes.Castclass, resourcesType!);

                il.Emit(OpCodes.Ldtoken, binding.InterfaceType);
                il.Emit(OpCodes.Call, GetTypeFromHandleMethod);

                il.Emit(OpCodes.Ldloc, instLocal);
                il.Emit(OpCodes.Callvirt, allocate);
                // Stack now has the i32 handle, which IS the wasm
                // i32 return. No further conversion.
            }
            else if (wasmType.ResultType.Types.Length == 1)
            {
                var wasmRet = wasmType.ResultType.Types[0];
                // own<R> return: the CLR returned a typed resource
                // instance; allocate a handle via Resources and
                // leave the i32 handle on the stack as the wasm
                // result.
                if (wasmRet == ValType.I32
                    && resolver != null
                    && resolver.IsResourceInterface(method.ReturnType)
                    && resourcesType != null)
                {
                    var instLocal = il.DeclareLocal(method.ReturnType);
                    il.Emit(OpCodes.Stloc, instLocal);

                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, ResourcesField);
                    il.Emit(OpCodes.Castclass, resourcesType);

                    il.Emit(OpCodes.Ldtoken, method.ReturnType);
                    il.Emit(OpCodes.Call, GetTypeFromHandleMethod);

                    il.Emit(OpCodes.Ldloc, instLocal);
                    il.Emit(OpCodes.Callvirt,
                        ResolveAllocateResourceMethod(resourcesType));
                }
                else
                {
                    // Convert C# return type back to the wasm wire
                    // type if the CIL stack form differs. Most
                    // narrow-int returns are already i32 on the
                    // stack (CIL widens to i32); ulong returns are
                    // already i64. So this is usually a no-op. But
                    // e.g. C# bool returned from an interface
                    // method is a 1-byte stack slot in some
                    // scenarios — emit a conv.i4 defensively.
                    EmitReturnConversionIfNeeded(il, method.ReturnType,
                        wasmRet);
                }
            }
        }

        // ---- Lift dispatcher -----------------------------------------

        // Emit IL that consumes the wasm slots starting at
        // <paramref name="wasmCursor"/> and leaves the corresponding
        // CLR value on the stack. Recursive: Option<T> calls itself
        // for the inner T's value-slot lift.
        private static void EmitLiftForType(ILGenerator il,
            Type clrType, ValType[] wasmParams,
            LocalBuilder[] temps, int wasmCursor,
            HostPackageResolver? resolver,
            Type? resourcesType,
            CanonOption.Kind stringEncoding =
                CanonOption.Kind.StringUtf8)
        {
            if (clrType == typeof(string))
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, MemoriesField);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldloc, temps[wasmCursor]);     // ptr
                il.Emit(OpCodes.Ldloc, temps[wasmCursor + 1]); // len
                il.Emit(OpCodes.Call, ResolveLiftStringMethod(
                    stringEncoding));
                return;
            }
            if (clrType.IsArray
                && IsSupportedPrimitiveArrayElement(
                    clrType.GetElementType()!))
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, MemoriesField);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldloc, temps[wasmCursor]);     // ptr
                il.Emit(OpCodes.Ldloc, temps[wasmCursor + 1]); // len
                il.Emit(OpCodes.Call,
                    ResolveLiftPrimMethod(clrType.GetElementType()!));
                return;
            }
            if (clrType == typeof(string[]))
            {
                // ListMarshal.LiftStringList(memory, listPtr, count)
                // walks `count` (ptr, len) pairs starting at listPtr
                // and lifts each via UTF-8.
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, MemoriesField);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldloc, temps[wasmCursor]);     // listPtr
                il.Emit(OpCodes.Ldloc, temps[wasmCursor + 1]); // count
                il.Emit(OpCodes.Call, LiftStringListMethod);
                return;
            }
            if (clrType == typeof(byte[][]))
            {
                // ListMarshal.LiftByteArrayList(memory, listPtr, count)
                // walks `count` (inner_ptr, inner_len) pairs starting
                // at listPtr and copies each inner byte[] out of
                // guest memory. The SLM's wasi:nn/graph-funcs.load
                // takes `list<list<u8>>` for builders; this is the
                // PARAM-side lift (gap 19).
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, MemoriesField);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldloc, temps[wasmCursor]);     // listPtr
                il.Emit(OpCodes.Ldloc, temps[wasmCursor + 1]); // count
                il.Emit(OpCodes.Call, LiftByteArrayListMethod);
                return;
            }
            // (T1, T2, ..., Tn)[] — list<tuple-of-flat-fields>.
            // Wire form: outer (i32 ptr, i32 count) + per-element
            // tuple at i*elemSize. SLM compute's `inputs:
            // list<tuple<string, own<tensor>>>` PARAM is the
            // canonical example (gap 22 follow-up — the lift
            // counterpart to round-13's byte[][] PARAM fix).
            if (clrType.IsArray
                && clrType.GetArrayRank() == 1
                && (IsTupleOfPrimitives(clrType.GetElementType()!)
                    || IsTupleOfFlatFields(
                        clrType.GetElementType()!, resolver)))
            {
                EmitLiftListOfRecordOrTuple(il,
                    clrType.GetElementType()!,
                    temps[wasmCursor],     // listPtr
                    temps[wasmCursor + 1], // count
                    resolver, resourcesType, stringEncoding);
                return;
            }
            if (clrType.IsGenericType
                && clrType.GetGenericTypeDefinition() == typeof(Option<>))
            {
                // Conditional Option<T>::Some(value) / None.
                //   ldloc disc
                //   brfalse none
                //   <emit lift for inner T at cursor+1>
                //   call Option<T>::Some(T)
                //   br end
                // none:
                //   call Option<T>::get_None
                // end:
                var inner = clrType.GetGenericArguments()[0];
                var noneLabel = il.DefineLabel();
                var endLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, temps[wasmCursor]);
                il.Emit(OpCodes.Brfalse, noneLabel);
                EmitLiftForType(il, inner, wasmParams, temps,
                    wasmCursor + 1, resolver, resourcesType,
                    stringEncoding);
                il.Emit(OpCodes.Call, ResolveOptionSomeMethod(inner));
                il.Emit(OpCodes.Br, endLabel);
                il.MarkLabel(noneLabel);
                il.Emit(OpCodes.Call, ResolveOptionNoneGetter(inner));
                il.MarkLabel(endLabel);
                return;
            }
            // ValueTuple<T1, ..., TN>: lift each element in
            // declaration order via recursive EmitLiftForType,
            // then construct via the matching ValueTuple ctor.
            // Wasm slots are concatenated per element in the same
            // order. Supports up to 7-element ValueTuple; nested
            // TRest defers (System.ValueTuple<...,TRest> takes a
            // ValueTuple as its 8th arg).
            if (clrType.IsGenericType
                && IsValueTupleType(clrType.GetGenericTypeDefinition()))
            {
                var elements = clrType.GetGenericArguments();
                int eltCursor = wasmCursor;
                foreach (var e in elements)
                {
                    int es = CanonicalSlotCount(e, out _, resolver);
                    EmitLiftForType(il, e, wasmParams, temps,
                        eltCursor, resolver, resourcesType,
                        stringEncoding);
                    eltCursor += es;
                }
                il.Emit(OpCodes.Newobj,
                    ResolveValueTupleCtor(clrType));
                return;
            }
            // Variant: switch on disc; each case constructs the
            // matching nested sealed class. Empty cases use the
            // parameterless ctor; payload cases lift the payload
            // (recursive EmitLiftForType) and pass it to the
            // case ctor. Joined-flat lift uses the max-payload's
            // wire layout — empty cases simply ignore the trailing
            // payload slots since they newobj a parameterless ctor.
            if (IsLikelyVariantBase(clrType))
            {
                var cases = GetVariantCases(clrType);
                var caseLabels = new System.Reflection.Emit.Label[cases.Length];
                for (int i = 0; i < cases.Length; i++)
                    caseLabels[i] = il.DefineLabel();
                var endLabel = il.DefineLabel();

                // Switch on disc.
                il.Emit(OpCodes.Ldloc, temps[wasmCursor]);
                il.Emit(OpCodes.Switch, caseLabels);
                // Default fallthrough: invalid disc. For valid wasm
                // this is unreachable; jump to the first case as a
                // benign fallback rather than introducing a trap.
                il.Emit(OpCodes.Br, caseLabels[0]);

                for (int i = 0; i < cases.Length; i++)
                {
                    il.MarkLabel(caseLabels[i]);
                    var c = cases[i];
                    if (c.Payload == null)
                    {
                        // Empty case — parameterless ctor.
                        il.Emit(OpCodes.Newobj,
                            ResolveCaseCtor(c.CaseType,
                                Type.EmptyTypes));
                    }
                    else
                    {
                        // Payload case — lift, then ctor(payload).
                        EmitLiftForType(il, c.Payload, wasmParams,
                            temps, wasmCursor + 1, resolver,
                            resourcesType, stringEncoding);
                        il.Emit(OpCodes.Newobj,
                            ResolveCaseCtor(c.CaseType,
                                new[] { c.Payload }));
                    }
                    il.Emit(OpCodes.Br, endLabel);
                }
                il.MarkLabel(endLabel);
                return;
            }
            // User-class record: newobj parameterless ctor, then for
            // each public property in declaration order:
            //   dup
            //   <emit lift for property type at current cursor>
            //   callvirt set_<Property>
            //   advance cursor by property's flat-slot count
            if (IsLikelyRecordType(clrType))
            {
                var ctor = ResolveRecordCtor(clrType);
                il.Emit(OpCodes.Newobj, ctor);
                int recCursor = wasmCursor;
                foreach (var p in GetRecordProperties(clrType))
                {
                    int ps = CanonicalSlotCount(p.PropertyType,
                        out _, resolver);
                    il.Emit(OpCodes.Dup);
                    EmitLiftForType(il, p.PropertyType, wasmParams,
                        temps, recCursor, resolver, resourcesType,
                        stringEncoding);
                    il.Emit(OpCodes.Callvirt,
                        p.GetSetMethod()!);
                    recCursor += ps;
                }
                return;
            }
            // Result<TOk, TErr>: same recursive shape as Option but
            // with a 2-case (Ok=0 / Err=1) discriminant routing to
            // the correct construction helper. v0 requires Ok and
            // Err to share the joined-flat shape — guarded by
            // CanonicalSlotCount before we get here.
            if (clrType.IsGenericType
                && clrType.GetGenericTypeDefinition() == typeof(Result<,>))
            {
                //   ldloc disc
                //   brfalse ok            ;; disc==0 → Ok branch
                //   <emit lift for TErr at cursor+1>
                //   call Result<TOk, TErr>::FromErr(TErr)
                //   br end
                // ok:
                //   <emit lift for TOk at cursor+1>
                //   call Result<TOk, TErr>::FromOk(TOk)
                // end:
                var args = clrType.GetGenericArguments();
                var okLabel = il.DefineLabel();
                var endLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, temps[wasmCursor]);
                il.Emit(OpCodes.Brfalse, okLabel);
                EmitLiftForType(il, args[1], wasmParams, temps,
                    wasmCursor + 1, resolver, resourcesType,
                    stringEncoding);
                il.Emit(OpCodes.Call, ResolveResultFromErrMethod(args[0], args[1]));
                il.Emit(OpCodes.Br, endLabel);
                il.MarkLabel(okLabel);
                EmitLiftForType(il, args[0], wasmParams, temps,
                    wasmCursor + 1, resolver, resourcesType,
                    stringEncoding);
                il.Emit(OpCodes.Call, ResolveResultFromOkMethod(args[0], args[1]));
                il.MarkLabel(endLabel);
                return;
            }
            // own<R> / borrow<R>: a typed resource interface as a
            // CLR param maps to a single i32 wasm handle. Lift via
            // ctx.Resources.GetResource(typeof(IR), handle) → cast.
            if (resolver != null
                && resolver.IsResourceInterface(clrType)
                && resourcesType != null)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ResourcesField);
                il.Emit(OpCodes.Castclass, resourcesType);
                il.Emit(OpCodes.Ldtoken, clrType);
                il.Emit(OpCodes.Call, GetTypeFromHandleMethod);
                il.Emit(OpCodes.Ldloc, temps[wasmCursor]);   // handle
                il.Emit(OpCodes.Callvirt,
                    ResolveGetResourceMethod(resourcesType));
                il.Emit(OpCodes.Castclass, clrType);
                return;
            }
            // IResource[] — list<borrow<R>> / list<own<R>>. Outer
            // (listPtr, count) at temps[wasmCursor..cursor+1]; each
            // element is a 4-byte i32 handle. Loop reading handles,
            // calling resources.GetResource(typeof(R), handle),
            // building a typed IResource[] for the C# call site.
            // wasi:io/poll.poll uses this exact shape.
            if (clrType.IsArray
                && clrType.GetArrayRank() == 1
                && resolver != null
                && resolver.IsResourceInterface(
                    clrType.GetElementType()!)
                && resourcesType != null)
            {
                var elemType = clrType.GetElementType()!;
                var arrLocal = il.DeclareLocal(clrType);
                var idxLocal = il.DeclareLocal(typeof(int));
                var loopTop = il.DefineLabel();
                var loopCond = il.DefineLabel();

                // arr = new T[count]
                il.Emit(OpCodes.Ldloc, temps[wasmCursor + 1]); // count
                il.Emit(OpCodes.Newarr, elemType);
                il.Emit(OpCodes.Stloc, arrLocal);
                // i = 0
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stloc, idxLocal);
                il.Emit(OpCodes.Br, loopCond);
                // loopTop:
                il.MarkLabel(loopTop);
                // handle = *(int*)(mem + listPtr + i*4)  via
                // ReadI32LE through MemoryInstance helpers — emit
                // inline. Push: arr, i, then value.
                il.Emit(OpCodes.Ldloc, arrLocal);
                il.Emit(OpCodes.Ldloc, idxLocal);
                // Lift one handle from listPtr + i*4
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, MemoriesField);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);                  // MemoryInstance
                il.Emit(OpCodes.Ldloc, temps[wasmCursor]);    // listPtr
                il.Emit(OpCodes.Ldloc, idxLocal);
                il.Emit(OpCodes.Ldc_I4_4);
                il.Emit(OpCodes.Mul);
                il.Emit(OpCodes.Add);                         // listPtr + i*4
                il.Emit(OpCodes.Call, PrimitiveReadI32LEMethod);
                // resources.GetResource(typeof(elem), handle) → cast
                var handleLocal = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Stloc, handleLocal);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ResourcesField);
                il.Emit(OpCodes.Castclass, resourcesType);
                il.Emit(OpCodes.Ldtoken, elemType);
                il.Emit(OpCodes.Call, GetTypeFromHandleMethod);
                il.Emit(OpCodes.Ldloc, handleLocal);
                il.Emit(OpCodes.Callvirt,
                    ResolveGetResourceMethod(resourcesType));
                il.Emit(OpCodes.Castclass, elemType);
                il.Emit(OpCodes.Stelem_Ref);
                // i++
                il.Emit(OpCodes.Ldloc, idxLocal);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, idxLocal);
                // loopCond: i < count?
                il.MarkLabel(loopCond);
                il.Emit(OpCodes.Ldloc, idxLocal);
                il.Emit(OpCodes.Ldloc, temps[wasmCursor + 1]);
                il.Emit(OpCodes.Blt, loopTop);
                // Push the result array.
                il.Emit(OpCodes.Ldloc, arrLocal);
                return;
            }
            // Primitive — load the spilled local and apply any
            // narrow CLR conversion.
            il.Emit(OpCodes.Ldloc, temps[wasmCursor]);
            EmitConversionIfNeeded(il, wasmParams[wasmCursor], clrType);
        }

        // ---- Internals ------------------------------------------------

        private static readonly FieldInfo HostBundleField =
            typeof(ThinContext).GetField(
                nameof(ThinContext.HostBundle))!;

        private static readonly FieldInfo ResourcesField =
            typeof(ThinContext).GetField(
                nameof(ThinContext.Resources))!;

        private static readonly MethodInfo GetTypeFromHandleMethod =
            typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle),
                BindingFlags.Public | BindingFlags.Static)!;

        private static readonly FieldInfo MemoriesField =
            typeof(ThinContext).GetField(
                nameof(ThinContext.Memories))!;

        private static readonly MethodInfo LiftUtf8Method =
            typeof(StringMarshal).GetMethod(
                nameof(StringMarshal.LiftUtf8),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(MemoryInstance), typeof(int), typeof(int) },
                modifiers: null)!;

        private static readonly MethodInfo LiftUtf16Method =
            typeof(StringMarshal).GetMethod(
                nameof(StringMarshal.LiftUtf16),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(MemoryInstance), typeof(int), typeof(int) },
                modifiers: null)!;

        private static readonly MethodInfo LiftLatin1OrUtf16Method =
            typeof(StringMarshal).GetMethod(
                nameof(StringMarshal.LiftLatin1OrUtf16),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(MemoryInstance), typeof(int), typeof(int) },
                modifiers: null)!;

        // Resolve the LiftXxx for a string read at the binding's
        // declared canon-ABI string encoding. Default = UTF-8.
        private static MethodInfo ResolveLiftStringMethod(
            CanonOption.Kind encoding) => encoding switch
            {
                CanonOption.Kind.StringUtf16 => LiftUtf16Method,
                CanonOption.Kind.StringLatin1OrUtf16
                    => LiftLatin1OrUtf16Method,
                _ => LiftUtf8Method,
            };

        // ListMarshal.LiftPrim<T> is generic. Cache the per-T
        // instantiation so each emit reuses the same MethodInfo
        // and we never pay a MakeGenericMethod call after the
        // first per element type.
        private static readonly MethodInfo LiftPrimGenericMethod =
            typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.LiftPrim),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(MemoryInstance), typeof(int), typeof(int) },
                modifiers: null)!;

        private static readonly ConcurrentDictionary<Type, MethodInfo>
            LiftPrimCache = new();

        // Set of unmanaged primitive element types ListMarshal.LiftPrim
        // accepts. Keep in lockstep with the actual `where T : unmanaged`
        // surface — adding a new shape here is enough to enable it
        // for direct-linked import emission.
        private static bool IsSupportedPrimitiveArrayElement(Type t) =>
            t == typeof(byte) || t == typeof(sbyte)
            || t == typeof(short) || t == typeof(ushort)
            || t == typeof(int) || t == typeof(uint)
            || t == typeof(long) || t == typeof(ulong)
            || t == typeof(float) || t == typeof(double);

        private static MethodInfo ResolveLiftPrimMethod(Type elementType)
            => LiftPrimCache.GetOrAdd(elementType,
                t => LiftPrimGenericMethod.MakeGenericMethod(t));

        // ListMarshal.LiftStringList — single non-generic helper
        // for the WASI list<string> shape (per-element ptr+len
        // pairs in memory). Captured at type-init.
        private static readonly MethodInfo LiftStringListMethod =
            typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.LiftStringList),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(MemoryInstance), typeof(int), typeof(int) },
                modifiers: null)!;

        // ListMarshal.LiftByteArrayList — list<list<u8>> param lift.
        // Per-element (inner_ptr, inner_len) pairs in memory; each
        // inner buffer is copied out to a byte[]. Closes the
        // wasi-nn `graph-funcs.load(builders, ...)` PARAM gap.
        private static readonly MethodInfo LiftByteArrayListMethod =
            typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.LiftByteArrayList),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(MemoryInstance), typeof(int), typeof(int) },
                modifiers: null)!;

        private static readonly FieldInfo CabiReallocField =
            typeof(ThinContext).GetField(
                nameof(ThinContext.CabiRealloc))!;

        // PrimitiveStore.ReadI32LE(memory, offset) → int — read a
        // little-endian i32 from guest memory. Used by the list-of-
        // resource-handles lift path to walk a packed handle array.
        private static readonly MethodInfo PrimitiveReadI32LEMethod =
            typeof(PrimitiveStore).GetMethod(
                nameof(PrimitiveStore.ReadI32LE),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(MemoryInstance), typeof(int) },
                modifiers: null)!;

        private static readonly MethodInfo StoreStringMethod =
            typeof(PrimitiveStore).GetMethod(
                nameof(PrimitiveStore.StoreString),
                BindingFlags.Public | BindingFlags.Static)!;

        private static readonly MethodInfo StoreStringUtf16Method =
            typeof(PrimitiveStore).GetMethod(
                nameof(PrimitiveStore.StoreStringUtf16),
                BindingFlags.Public | BindingFlags.Static)!;

        private static readonly MethodInfo StoreStringLatin1OrUtf16Method =
            typeof(PrimitiveStore).GetMethod(
                nameof(PrimitiveStore.StoreStringLatin1OrUtf16),
                BindingFlags.Public | BindingFlags.Static)!;

        // Resolve the StoreXxx for a string write at the binding's
        // declared canon-ABI string encoding.
        private static MethodInfo ResolveStoreStringMethod(
            CanonOption.Kind encoding) => encoding switch
            {
                CanonOption.Kind.StringUtf16 => StoreStringUtf16Method,
                CanonOption.Kind.StringLatin1OrUtf16
                    => StoreStringLatin1OrUtf16Method,
                _ => StoreStringMethod,  // utf8 default
            };

        private static readonly MethodInfo StoreByteArrayMethod =
            typeof(PrimitiveStore).GetMethod(
                nameof(PrimitiveStore.StoreByteArray),
                BindingFlags.Public | BindingFlags.Static)!;

        // Open generic StorePrimitiveArray<T>; close per-T at emit
        // time via the cache below.
        private static readonly MethodInfo StorePrimitiveArrayOpenMethod =
            typeof(PrimitiveStore).GetMethod(
                nameof(PrimitiveStore.StorePrimitiveArray),
                BindingFlags.Public | BindingFlags.Static)!;

        private static readonly MethodInfo StoreStringListMethod =
            typeof(PrimitiveStore).GetMethod(
                nameof(PrimitiveStore.StoreStringList),
                BindingFlags.Public | BindingFlags.Static)!;

        private static readonly MethodInfo StoreByteArrayListMethod =
            typeof(PrimitiveStore).GetMethod(
                nameof(PrimitiveStore.StoreByteArrayList),
                BindingFlags.Public | BindingFlags.Static)!;

        private static readonly MethodInfo StoreListOfStringListMethod =
            typeof(PrimitiveStore).GetMethod(
                nameof(PrimitiveStore.StoreListOfStringList),
                BindingFlags.Public | BindingFlags.Static)!;

        private static readonly MethodInfo StoreListOfByteArrayListMethod =
            typeof(PrimitiveStore).GetMethod(
                nameof(PrimitiveStore.StoreListOfByteArrayList),
                BindingFlags.Public | BindingFlags.Static)!;

        // Open generic StorePrimArrayList<T> for jagged primitive
        // returns (int[][], long[][], etc.).
        private static readonly MethodInfo StorePrimArrayListOpenMethod =
            typeof(PrimitiveStore).GetMethod(
                nameof(PrimitiveStore.StorePrimArrayList),
                BindingFlags.Public | BindingFlags.Static)!;

        private static readonly ConcurrentDictionary<Type, MethodInfo>
            StorePrimArrayListCache = new();

        private static MethodInfo ResolveStorePrimArrayListMethod(
            Type elementType)
            => StorePrimArrayListCache.GetOrAdd(elementType,
                t => StorePrimArrayListOpenMethod.MakeGenericMethod(t));

        private static readonly ConcurrentDictionary<Type, MethodInfo>
            StorePrimArrayCache = new();

        private static MethodInfo ResolveStorePrimitiveArrayMethod(
            Type elementType)
            => StorePrimArrayCache.GetOrAdd(elementType,
                t => StorePrimitiveArrayOpenMethod.MakeGenericMethod(t));

        // True when T is a canon-ABI list<> element type that maps
        // 1:1 to a CLR unmanaged primitive: s8/u8/s16/u16/s32/u32/
        // s64/u64/f32/f64/bool. canon-ABI bools are u8 wire (0=false,
        // 1=true); .NET represents bool as a 1-byte value with the
        // same encoding, so MemoryMarshal.AsBytes round-trips.
        private static bool IsListPrimitiveElement(Type t)
        {
            if (t.IsEnum) t = Enum.GetUnderlyingType(t);
            return t == typeof(byte) || t == typeof(sbyte)
                || t == typeof(short) || t == typeof(ushort)
                || t == typeof(int) || t == typeof(uint)
                || t == typeof(long) || t == typeof(ulong)
                || t == typeof(float) || t == typeof(double)
                || t == typeof(bool);
        }

        // Option<T>::Some(T) and Option<T>::get_None are the
        // construction surface for direct-linked Option<T> param
        // emit. Cache the per-T MethodInfos; first emit per T pays
        // one MakeGenericType + GetMethod, subsequent emits reuse.
        private static readonly ConcurrentDictionary<Type, MethodInfo>
            OptionSomeCache = new();

        private static readonly ConcurrentDictionary<Type, MethodInfo>
            OptionNoneCache = new();

        private static MethodInfo ResolveOptionSomeMethod(Type inner)
            => OptionSomeCache.GetOrAdd(inner, t =>
            {
                var optionT = typeof(Option<>).MakeGenericType(t);
                return optionT.GetMethod("Some",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { t },
                    modifiers: null)!;
            });

        private static MethodInfo ResolveOptionNoneGetter(Type inner)
            => OptionNoneCache.GetOrAdd(inner, t =>
            {
                var optionT = typeof(Option<>).MakeGenericType(t);
                return optionT.GetProperty("None",
                    BindingFlags.Public | BindingFlags.Static)!
                    .GetGetMethod()!;
            });

        // Result<TOk, TErr>::FromOk(TOk) and FromErr(TErr) cached
        // per (TOk, TErr) pair so each direct-linked Result<,>
        // emit reuses the same MethodInfos.
        private static readonly ConcurrentDictionary<(Type, Type), MethodInfo>
            ResultFromOkCache = new();

        private static readonly ConcurrentDictionary<(Type, Type), MethodInfo>
            ResultFromErrCache = new();

        private static MethodInfo ResolveResultFromOkMethod(Type ok, Type err)
            => ResultFromOkCache.GetOrAdd((ok, err), key =>
            {
                var resultT = typeof(Result<,>).MakeGenericType(
                    key.Item1, key.Item2);
                return resultT.GetMethod("FromOk",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { key.Item1 },
                    modifiers: null)!;
            });

        private static MethodInfo ResolveResultFromErrMethod(Type ok, Type err)
            => ResultFromErrCache.GetOrAdd((ok, err), key =>
            {
                var resultT = typeof(Result<,>).MakeGenericType(
                    key.Item1, key.Item2);
                return resultT.GetMethod("FromErr",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { key.Item2 },
                    modifiers: null)!;
            });

        // Recognized ValueTuple<...> open generic defs. System
        // provides 1-arity through 8-arity (8 uses TRest for
        // nesting); v0 supports 1..7. ValueTuple<> (zero-arity)
        // is a struct with no fields and never appears as a WIT
        // tuple shape — exclude.
        private static bool IsValueTupleType(Type def) =>
            def == typeof(ValueTuple<>)
            || def == typeof(ValueTuple<,>)
            || def == typeof(ValueTuple<,,>)
            || def == typeof(ValueTuple<,,,>)
            || def == typeof(ValueTuple<,,,,>)
            || def == typeof(ValueTuple<,,,,,>)
            || def == typeof(ValueTuple<,,,,,,>);

        private static readonly ConcurrentDictionary<Type, ConstructorInfo>
            ValueTupleCtorCache = new();

        private static ConstructorInfo ResolveValueTupleCtor(Type tupleType)
            => ValueTupleCtorCache.GetOrAdd(tupleType, t =>
                t.GetConstructor(t.GetGenericArguments())!);

        // Heuristic: is this CLR type a "record" (a sealed class
        // with public parameterless ctor + ≥1 public {get;set;}
        // properties) suitable for direct-linked field-by-field
        // construction? Matches what WitHostInterfaceGenerator
        // emits for WIT record types AND user-defined records that
        // follow the same convention.
        //
        // Conservative — exclude built-in framework types,
        // generics (already handled by Option/Result/Tuple cases),
        // and anything not a class. Keeps the false-positive
        // risk low: record-like POCOs in user code are accepted;
        // System.Tuple and similar are not.
        private static bool IsLikelyRecordType(Type t)
        {
            if (t.IsValueType || t.IsArray || t.IsInterface) return false;
            if (t == typeof(string) || t == typeof(object)) return false;
            if (t.IsGenericType) return false;       // Option/Result/Tuple paths
            if (t.IsAbstract) return false;
            if (t.GetConstructor(Type.EmptyTypes) == null) return false;
            // Must have at least one public read/write property — the
            // shape WitHostInterfaceGenerator emits.
            var props = t.GetProperties(BindingFlags.Public
                | BindingFlags.Instance);
            foreach (var p in props)
                if (p.CanRead && p.CanWrite) return true;
            return false;
        }

        private static readonly ConcurrentDictionary<Type, PropertyInfo[]>
            RecordPropertiesCache = new();

        // Public read/write instance properties in MetadataToken
        // order — this is the canonical field-declaration order
        // for properties emitted by Roslyn. The wire layout is
        // declaration order so this needs to match exactly.
        private static PropertyInfo[] GetRecordProperties(Type t)
            => RecordPropertiesCache.GetOrAdd(t, type =>
            {
                var list = new System.Collections.Generic.List<PropertyInfo>();
                foreach (var p in type.GetProperties(BindingFlags.Public
                    | BindingFlags.Instance))
                {
                    if (p.CanRead && p.CanWrite)
                        list.Add(p);
                }
                list.Sort((a, b) => a.MetadataToken.CompareTo(
                    b.MetadataToken));
                return list.ToArray();
            });

        private static readonly ConcurrentDictionary<Type, ConstructorInfo>
            RecordCtorCache = new();

        private static ConstructorInfo ResolveRecordCtor(Type t)
            => RecordCtorCache.GetOrAdd(t, type =>
                type.GetConstructor(Type.EmptyTypes)!);

        // ---- Variant detection + case enumeration --------------

        // Heuristic: variant types are abstract classes carrying ≥1
        // public sealed nested class that derives from them. The
        // source generator emits this exact shape for WIT variants.
        private static bool IsLikelyVariantBase(Type t)
        {
            if (!t.IsAbstract || t.IsInterface || t.IsValueType)
                return false;
            if (t.IsGenericType) return false;
            if (t == typeof(object)) return false;
            // Must have at least one public sealed nested type
            // that inherits from this abstract.
            foreach (var n in t.GetNestedTypes(BindingFlags.Public))
            {
                if (n.IsSealed && n.BaseType == t) return true;
            }
            return false;
        }

        public sealed class VariantCase
        {
            public Type CaseType { get; }
            public Type? Payload { get; }
            public VariantCase(Type caseType, Type? payload)
            {
                CaseType = caseType;
                Payload = payload;
            }
        }

        private static readonly ConcurrentDictionary<Type, VariantCase[]>
            VariantCasesCache = new();

        private static VariantCase[] GetVariantCases(Type variantBase)
            => VariantCasesCache.GetOrAdd(variantBase, t =>
            {
                var list = new System.Collections.Generic.List<VariantCase>();
                foreach (var n in t.GetNestedTypes(BindingFlags.Public))
                {
                    if (!n.IsSealed) continue;
                    if (n.BaseType != t) continue;
                    // Payload detected via single-arg ctor whose
                    // arg matches a public Value property. Empty
                    // cases have only the implicit parameterless
                    // ctor (no Value property).
                    var valueProp = n.GetProperty("Value",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (valueProp != null && valueProp.CanRead)
                        list.Add(new VariantCase(n, valueProp.PropertyType));
                    else
                        list.Add(new VariantCase(n, null));
                }
                // Declaration order = MetadataToken order — matches
                // the WIT case index.
                list.Sort((a, b) =>
                    a.CaseType.MetadataToken.CompareTo(
                        b.CaseType.MetadataToken));
                return list.ToArray();
            });

        private static readonly ConcurrentDictionary<(Type, int), ConstructorInfo>
            VariantCaseCtorCache = new();

        private static ConstructorInfo ResolveCaseCtor(Type caseType,
            Type[] argTypes)
        {
            // Cache key: (caseType, argTypes.Length) — different
            // arities don't collide because the source generator
            // emits exactly one (parameterless OR single-arg) ctor
            // per case class.
            var key = (caseType, argTypes.Length);
            return VariantCaseCtorCache.GetOrAdd(key, _ =>
                caseType.GetConstructor(argTypes)!);
        }

        // ---- Aggregate-return support --------------------------

        // True when this CLR return type can be serialized into
        // wasm linear memory at the retArea pointer. v0: records
        // (sealed POCOs), ValueTuple<...>, Option<primitive>,
        // Option<own<R>>, and Result<primitive, primitive>.
        // String / list / variant returns defer until per-shape
        // store emit lands (the variable-length ones need
        // cabi_realloc).
        private static bool IsAggregateReturnSupported(Type t,
            HostPackageResolver? resolver = null)
        {
            // string + byte[] returns: 8-byte retArea (i32 ptr +
            // i32 len/count). Both go through cabi_realloc; the
            // null-check fires at first call if the module didn't
            // export cabi_realloc.
            if (t == typeof(string)) return true;
            if (t == typeof(byte[])) return true;
            // list<T> for unmanaged primitive T (s8..f64) — same wire
            // form as byte[] (8-byte retArea: ptr + count) but with
            // per-T element width. list<string> rides via two-level
            // cabi_realloc (outer array of (ptr,len) + per-element
            // UTF-8 buffers).
            if (t.IsArray && t.GetArrayRank() == 1)
            {
                var elem = t.GetElementType()!;
                if (IsListPrimitiveElement(elem)) return true;
                if (elem == typeof(string)) return true;
                // list<list<T>> (T[][]) for primitive T — two-level
                // cabi_realloc (outer pair-array + per-sub raw bytes).
                if (elem.IsArray && elem.GetArrayRank() == 1
                    && IsListPrimitiveElement(elem.GetElementType()!))
                    return true;
                // list<list<string>> (string[][]) — three-level
                // cabi_realloc.
                if (elem.IsArray && elem.GetArrayRank() == 1
                    && elem.GetElementType() == typeof(string))
                    return true;
                // list<list<list<u8>>> (byte[][][]) — three-level
                // cabi_realloc with raw byte buffers.
                if (elem.IsArray && elem.GetArrayRank() == 1
                    && elem.GetElementType() == typeof(byte[]))
                    return true;
                // list<tuple-of-primitives> / list<record-of-primitives>:
                // outer (ptr, count) at retArea + contiguous packed
                // elements at *(ptr). cabi_realloc once for the outer
                // buffer; per-element fields written inline.
                if (IsTupleOfPrimitives(elem)) return true;
                if (IsRecordOfPrimitives(elem)) return true;
                // list<tuple-of-flat-fields-with-resources> /
                // list<record-of-flat-fields-with-resources>: same
                // wire layout as the primitives variant, with per-
                // resource-field allocation through
                // ctx.Resources.AllocateResource on store. Closes
                // the gap-9 shape (preopens.get-directories returns
                // list&lt;tuple&lt;own&lt;descriptor&gt;, string&gt;&gt;).
                if (IsTupleOfFlatFields(elem, resolver)
                    || IsRecordOfFlatFields(elem, resolver))
                    return true;
                // list<own<R>>: per-element AllocateResource + i32
                // handle write into the outer buffer.
                if (resolver != null
                    && resolver.IsResourceInterface(elem)
                    && resolver.PreferredResourcesType != null)
                    return true;
                // list<Option<X>> / list<Result<X,Y>>: per-element
                // recursive Option/Result write at outerPtr+i*elemSize.
                if (elem.IsGenericType)
                {
                    var elemDef = elem.GetGenericTypeDefinition();
                    if ((elemDef == typeof(Option<>)
                            || elemDef == typeof(Result<,>))
                        && IsAggregateReturnSupported(elem, resolver))
                        return true;
                }
                // list<variant<...>>: per-element variant write
                // via EmitVariantStoreAt at outerPtr+i*elemSize.
                if (IsLikelyVariantBase(elem)
                    && IsAggregateReturnSupported(elem, resolver))
                    return true;
            }
            if (IsLikelyRecordType(t))
            {
                // Resolver-aware: a record field can be a primitive,
                // string, byte[], own<R> resource handle, OR an
                // Option<X> with X a recognized aggregate. The last
                // case is what carries shapes like
                // wasi:filesystem/types.descriptor-stat (record with
                // option<datetime> fields).
                foreach (var p in GetRecordProperties(t))
                    if (!IsFlatField(p.PropertyType, resolver)) return false;
                return true;
            }
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (IsValueTupleType(def))
                {
                    foreach (var ta in t.GetGenericArguments())
                        if (!IsFlatField(ta, resolver)) return false;
                    return true;
                }
                // Option<primitive> OR Option<own<R>> OR
                // Option<string> OR Option<byte[]> — wire form:
                // u8 disc + (primitive value | i32 handle | (ptr, len)).
                // Resource case requires the resolver to recognize
                // the inner type AND a resources class. String and
                // byte[] cases require cabi_realloc at runtime.
                if (def == typeof(Option<>))
                {
                    var inner = t.GetGenericArguments()[0];
                    if (IsStorablePrimitive(inner)) return true;
                    if (inner == typeof(string)) return true;
                    if (inner == typeof(byte[])) return true;
                    if (resolver != null
                        && resolver.IsResourceInterface(inner)
                        && resolver.PreferredResourcesType != null)
                        return true;
                    // Option<tuple-of-primitives> /
                    // Option<record-of-primitives>: write each field
                    // at its own offset within the option's value
                    // slot. valueOffset = Align(1, max-field-align).
                    if (IsTupleOfPrimitives(inner)) return true;
                    if (IsRecordOfPrimitives(inner)) return true;
                    // Option<list<T>> (primitive T) / Option<string[]>:
                    // outer (ptr, count) at retArea+valueOffset.
                    if (inner.IsArray && inner.GetArrayRank() == 1)
                    {
                        var elem = inner.GetElementType()!;
                        if (IsListPrimitiveElement(elem)) return true;
                        if (elem == typeof(string)) return true;
                    }
                    // Option<Option<X>> / Option<Result<X,Y>>:
                    // recursive — inner must itself be a supported
                    // aggregate.
                    if (inner.IsGenericType)
                    {
                        var innerDef = inner.GetGenericTypeDefinition();
                        if ((innerDef == typeof(Option<>)
                                || innerDef == typeof(Result<,>))
                            && IsAggregateReturnSupported(inner, resolver))
                            return true;
                    }
                    return false;
                }
                // Result<TOk, TErr>: each arm must be storable
                // (primitive OR resource interface for own<R>).
                // Wire form: u8 disc + value (joined-flat at max
                // alignment of Ok/Err; resource handles are i32
                // alignment 4).
                if (def == typeof(Result<,>))
                {
                    var args = t.GetGenericArguments();
                    return IsResultArmStorable(args[0], resolver,
                            allowVariableLength: true)
                        && IsResultArmStorable(args[1], resolver,
                            allowVariableLength: true);
                }
            }
            // Variant base type. Wire form: u8 disc + joined-flat
            // value at Align(1, max-payload-align). v0 supports
            // empty cases, primitive-payload cases, resource-handle-
            // payload (own<R>) cases, AND string/byte[] payloads
            // via cabi_realloc. Mixed payload widths are fine — the
            // joined-flat slot reserves max-width and each case
            // writes its own width at the common valueOffset.
            if (IsLikelyVariantBase(t))
            {
                var cases = GetVariantCases(t);
                if (cases.Length == 0) return false;
                foreach (var c in cases)
                {
                    if (c.Payload == null) continue;
                    if (!IsResultArmStorable(c.Payload, resolver,
                            allowVariableLength: true))
                        return false;
                }
                return true;
            }
            return false;
        }

        // True when t is ValueTuple<...> with all elements being
        // canon-ABI primitives. Used for Option<tuple>/Result-arm
        // tuple support without recursion into nested aggregates.
        // True when t is ValueTuple<...> with all elements being
        // flat fields (primitives, strings, byte[]). The store
        // emit dispatches per-field on the actual type.
        private static bool IsTupleOfPrimitives(Type t)
        {
            if (!t.IsGenericType) return false;
            if (!IsValueTupleType(t.GetGenericTypeDefinition())) return false;
            foreach (var ta in t.GetGenericArguments())
                if (!IsFlatField(ta)) return false;
            return true;
        }

        // Resolver-aware variant: also accepts tuples where one or
        // more fields are resource interfaces (own&lt;R&gt; → i32
        // handle). Used by IsAggregateReturnSupported to recognize
        // list&lt;tuple&lt;own&lt;R&gt;, string&gt;&gt; — preopens.get-directories,
        // http.headers.entries (when fields are resources), and the
        // broader resource+label shape class.
        private static bool IsTupleOfFlatFields(Type t,
            HostPackageResolver? resolver)
        {
            if (!t.IsGenericType) return false;
            if (!IsValueTupleType(t.GetGenericTypeDefinition())) return false;
            foreach (var ta in t.GetGenericArguments())
                if (!IsFlatField(ta, resolver)) return false;
            return true;
        }

        // True when t is a sealed POCO with all property types
        // being flat fields (primitives, strings, byte[]).
        private static bool IsRecordOfPrimitives(Type t)
        {
            if (!IsLikelyRecordType(t)) return false;
            foreach (var p in GetRecordProperties(t))
                if (!IsFlatField(p.PropertyType)) return false;
            return true;
        }

        // Resolver-aware variant: see IsTupleOfFlatFields rationale.
        private static bool IsRecordOfFlatFields(Type t,
            HostPackageResolver? resolver)
        {
            if (!IsLikelyRecordType(t)) return false;
            foreach (var p in GetRecordProperties(t))
                if (!IsFlatField(p.PropertyType, resolver)) return false;
            return true;
        }

        // Max field alignment across a tuple/record. string/byte[]
        // contribute align 4 (i32 ptr); primitives use their own
        // alignment; resource interfaces contribute align 4 (i32
        // handle) when a resolver is supplied; Option<X> contributes
        // MaxAlignOf(X) so adjacent fields after an option pack at
        // the inner type's alignment.
        private static int MaxFieldAlign(Type t,
            HostPackageResolver? resolver = null)
        {
            int maxA = 1;
            if (t.IsGenericType
                && IsValueTupleType(t.GetGenericTypeDefinition()))
            {
                foreach (var ta in t.GetGenericArguments())
                {
                    int a = AlignOfFlatField(ta, resolver);
                    if (a > maxA) maxA = a;
                }
            }
            else
            {
                foreach (var p in GetRecordProperties(t))
                {
                    int a = AlignOfFlatField(p.PropertyType, resolver);
                    if (a > maxA) maxA = a;
                }
            }
            return maxA;
        }

        // Emit IL that stores a record/tuple of primitives whose
        // base address (i32) is pushed onto the stack via
        // pushBaseAddress. Each field write computes
        // dest[base + fieldOffset] inline. Returns the post-write
        // end offset (relative to base). Reads each field/property
        // via the appropriate access pattern (ValueTuple uses
        // ldloca+ldfld; record uses ldloc+callvirt(getter)).
        //
        // For top-level records/tuples at retArea+baseOffset:
        //   pushBaseAddress = il2 => {
        //     il2.Emit(Ldloc, retAreaLocal);
        //     if (baseOffset != 0) { Ldc + Add; }
        //   }
        // For list-of-record per-element at outerPtr + i*elemSize:
        //   pushBaseAddress = il2 => {
        //     il2.Emit(Ldloc, outerPtrLocal);
        //     il2.Emit(Ldloc, indexLocal);
        //     il2.Emit(Ldc, elemSize); Mul;
        //     Add;
        //   }
        private static int EmitInlineRecordOrTupleStore(
            ILGenerator il, Type type,
            Action<ILGenerator> pushBaseAddress,
            LocalBuilder valueLocal,
            CanonOption.Kind stringEncoding =
                CanonOption.Kind.StringUtf8,
            HostPackageResolver? resolver = null,
            Type? resourcesType = null)
        {
            int offsetSoFar = 0;
            bool isTuple = type.IsGenericType
                && IsValueTupleType(type.GetGenericTypeDefinition());

            if (isTuple)
            {
                var elements = type.GetGenericArguments();
                for (int i = 0; i < elements.Length; i++)
                {
                    var et = elements[i];
                    int fieldAlign = AlignOfFlatField(et, resolver);
                    int fieldOffset = Align(offsetSoFar, fieldAlign);

                    // PersistedAssemblyBuilder mis-encodes Ldfld against
                    // closed-generic ValueTuple.ItemN (MissingFieldException
                    // at JIT). Route reads through a Roslyn-compiled
                    // TupleFieldAccess.ItemPofN<…> helper so the field
                    // reference is baked by the C# compiler, not by PAB.
                    var itemReader = TupleFieldAccess.ResolveItemReader(type, i + 1);
                    EmitTupleOrRecordFieldStore(il, et, fieldOffset,
                        pushBaseAddress,
                        push => {
                            push.Emit(OpCodes.Ldloc, valueLocal);
                            push.Emit(OpCodes.Call, itemReader);
                        },
                        stringEncoding,
                        resolver, resourcesType);

                    offsetSoFar = fieldOffset
                        + SizeOfFlatField(et, resolver);
                }
            }
            else
            {
                foreach (var p in GetRecordProperties(type))
                {
                    int fieldAlign = AlignOfFlatField(p.PropertyType, resolver);
                    int fieldOffset = Align(offsetSoFar, fieldAlign);

                    EmitTupleOrRecordFieldStore(il, p.PropertyType,
                        fieldOffset, pushBaseAddress,
                        push => {
                            push.Emit(OpCodes.Ldloc, valueLocal);
                            push.Emit(OpCodes.Callvirt,
                                p.GetGetMethod()!);
                        },
                        stringEncoding,
                        resolver, resourcesType);

                    offsetSoFar = fieldOffset
                        + SizeOfFlatField(p.PropertyType, resolver);
                }
            }
            return offsetSoFar;
        }

        // Per-field store helper: dispatches on the field type to
        // either a plain primitive store (StoreXxx), the
        // cabi_realloc-backed variable-length store (StoreString /
        // StoreByteArray), or the resource-handle store
        // (Resources.AllocateResource → i32 → StoreI32) when the
        // field is a resource interface (own&lt;R&gt;).
        private static void EmitTupleOrRecordFieldStore(
            ILGenerator il, Type fieldType, int fieldOffset,
            Action<ILGenerator> pushBaseAddress,
            Action<ILGenerator> pushFieldValue,
            CanonOption.Kind stringEncoding,
            HostPackageResolver? resolver = null,
            Type? resourcesType = null)
        {
            bool isString = fieldType == typeof(string);
            bool isByteArray = fieldType == typeof(byte[]);
            bool isResource = resolver != null
                && resourcesType != null
                && resolver.IsResourceInterface(fieldType);
            bool isOption = fieldType.IsGenericType
                && fieldType.GetGenericTypeDefinition() == typeof(Option<>);

            if (isOption)
            {
                // Option<X> field: stash the option value into a
                // local, compute the field's absolute base address
                // (parent_base + fieldOffset) into a separate local,
                // and dispatch to EmitOptionStoreAt with that local
                // standing in for retArea + a zero baseOffset.
                // EmitOptionStoreAt handles the disc + value layout
                // and recurses into Option<Option<X>> /
                // Option<Result<X,Y>> / Option<record-of-primitives>
                // shapes the same way it does for top-level returns.
                var optionLocal = il.DeclareLocal(fieldType);
                pushFieldValue(il);
                il.Emit(OpCodes.Stloc, optionLocal);

                var fieldBaseLocal = il.DeclareLocal(typeof(int));
                pushBaseAddress(il);
                if (fieldOffset != 0)
                {
                    il.Emit(OpCodes.Ldc_I4, fieldOffset);
                    il.Emit(OpCodes.Add);
                }
                il.Emit(OpCodes.Stloc, fieldBaseLocal);

                EmitOptionStoreAt(il, fieldType, optionLocal,
                    fieldBaseLocal, 0,
                    resolver, resourcesType, stringEncoding);
                return;
            }

            if (isString || isByteArray)
            {
                // Variable-length store: pass MemoryInstance (not
                // mem.Data) so the helper re-fetches mem.Data after
                // its internal cabi_realloc — that call may grow
                // memory and stale-out any captured byte[] reference.
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, MemoriesField);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                pushBaseAddress(il);
                if (fieldOffset != 0)
                {
                    il.Emit(OpCodes.Ldc_I4, fieldOffset);
                    il.Emit(OpCodes.Add);
                }
                pushFieldValue(il);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, CabiReallocField);
                il.Emit(OpCodes.Call, isString
                    ? ResolveStoreStringMethod(stringEncoding)
                    : StoreByteArrayMethod);
                return;
            }

            // Fixed-width store (primitive / resource handle): the
            // PrimitiveStore.StoreXxx helpers take byte[] dest, no
            // cabi_realloc, no grow risk.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            // dest_offset = base + fieldOffset
            pushBaseAddress(il);
            if (fieldOffset != 0)
            {
                il.Emit(OpCodes.Ldc_I4, fieldOffset);
                il.Emit(OpCodes.Add);
            }

            if (isResource)
            {
                // handle = ctx.Resources.AllocateResource(typeof(IRes), value)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ResourcesField);
                il.Emit(OpCodes.Castclass, resourcesType!);
                il.Emit(OpCodes.Ldtoken, fieldType);
                il.Emit(OpCodes.Call, GetTypeFromHandleMethod);
                pushFieldValue(il);
                il.Emit(OpCodes.Callvirt,
                    ResolveAllocateResourceMethod(resourcesType!));
                // StoreI32(dest_array, dest_offset, handle)
                il.Emit(OpCodes.Call, ResolveStoreMethod(typeof(int)));
            }
            else
            {
                pushFieldValue(il);
                il.Emit(OpCodes.Call, ResolveStoreMethod(fieldType));
            }
        }

        // Convenience overload: matches the previous (retArea +
        // baseOffset) signature so existing call sites don't change.
        private static int EmitInlineRecordOrTupleStore(
            ILGenerator il, Type type, LocalBuilder retAreaLocal,
            int baseOffset, LocalBuilder valueLocal,
            CanonOption.Kind stringEncoding =
                CanonOption.Kind.StringUtf8,
            HostPackageResolver? resolver = null,
            Type? resourcesType = null)
        {
            return EmitInlineRecordOrTupleStore(il, type,
                il2 =>
                {
                    il2.Emit(OpCodes.Ldloc, retAreaLocal);
                    if (baseOffset != 0)
                    {
                        il2.Emit(OpCodes.Ldc_I4, baseOffset);
                        il2.Emit(OpCodes.Add);
                    }
                },
                valueLocal, stringEncoding,
                resolver, resourcesType);
        }

        // Emit IL for a list<tuple|record> top-level return.
        //   1. Compute count = arrayLocal.Length
        //   2. Compute byteCount = count * elemSize
        //   3. Call cabi_realloc(0, 0, elemAlign, byteCount) → outerPtr
        //   4. Loop i from 0 to count-1, write each element at
        //      outerPtr + i*elemSize using the per-field write
        //      machinery (with a dynamic base address).
        //   5. Write (outerPtr, count) to retArea+0/+4.
        private static void EmitListOfRecordOrTupleReturn(
            ILGenerator il, Type elemType, LocalBuilder arrayLocal,
            LocalBuilder retAreaLocal,
            CanonOption.Kind stringEncoding =
                CanonOption.Kind.StringUtf8,
            HostPackageResolver? resolver = null,
            Type? resourcesType = null,
            int baseOffset = 0)
        {
            int elemSize = SizeOfRecordOrTuple(elemType, resolver);
            int elemAlign = MaxFieldAlign(elemType, resolver);

            // count = arrayLocal.Length
            var countLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldloc, arrayLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, countLocal);

            // outerPtr = ctx.CabiRealloc(0, 0, elemAlign, count*elemSize)
            var outerPtrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, CabiReallocField);
            // Null-check at runtime via dup+brfalse then throw isn't
            // strictly necessary — calling a null Func throws NRE
            // which the test sees. Skip the explicit guard.
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4, elemAlign);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Ldc_I4, elemSize);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Callvirt,
                typeof(Func<int, int, int, int, int>)
                    .GetMethod("Invoke")!);
            il.Emit(OpCodes.Stloc, outerPtrLocal);

            // for (int i = 0; i < count; i++)
            var indexLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, indexLocal);

            var loopStart = il.DefineLabel();
            var loopCheck = il.DefineLabel();

            il.Emit(OpCodes.Br, loopCheck);
            il.MarkLabel(loopStart);

            // Stash element[i] in elemLocal.
            var elemLocal = il.DeclareLocal(elemType);
            il.Emit(OpCodes.Ldloc, arrayLocal);
            il.Emit(OpCodes.Ldloc, indexLocal);
            if (elemType.IsValueType)
            {
                // ValueTuple is a struct — Ldelem for value types
                // takes the element via Ldelema + Ldobj, or use Ldelem
                // with the type token.
                il.Emit(OpCodes.Ldelem, elemType);
            }
            else
            {
                il.Emit(OpCodes.Ldelem_Ref);
            }
            il.Emit(OpCodes.Stloc, elemLocal);

            // Write fields at outerPtr + i*elemSize + fieldOffset.
            EmitInlineRecordOrTupleStore(il, elemType,
                il2 =>
                {
                    il2.Emit(OpCodes.Ldloc, outerPtrLocal);
                    il2.Emit(OpCodes.Ldloc, indexLocal);
                    il2.Emit(OpCodes.Ldc_I4, elemSize);
                    il2.Emit(OpCodes.Mul);
                    il2.Emit(OpCodes.Add);
                },
                elemLocal, stringEncoding,
                resolver, resourcesType);

            // i++
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, indexLocal);

            il.MarkLabel(loopCheck);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Blt, loopStart);

            // Write (outerPtr, count) to retArea + baseOffset+0 and
            // +4. baseOffset != 0 when this list is the Ok arm of a
            // Result wrapper (gap 22) — the arm's valueOffset shifts
            // the (ptr, count) pair down to the joined-flat slot.
            // ptr @ baseOffset+0
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            if (baseOffset != 0)
            {
                il.Emit(OpCodes.Ldc_I4, baseOffset);
                il.Emit(OpCodes.Add);
            }
            il.Emit(OpCodes.Ldloc, outerPtrLocal);
            il.Emit(OpCodes.Call, ResolveStoreMethod(typeof(int)));

            // count @ baseOffset+4
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.Emit(OpCodes.Ldc_I4, baseOffset + 4);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Call, ResolveStoreMethod(typeof(int)));
        }

        // Emit IL that lifts a (T1, T2, ..., Tn)[] PARAM from guest
        // memory. Mirror of EmitListOfRecordOrTupleReturn on the
        // store side. Wire form: outer (i32 ptr, i32 count) at the
        // wasm slot pair; element area packs each tuple at
        // i*elemSize. Used by direct-link for shapes like the SLM's
        // wasi:nn/inference.compute(inputs: list<tuple<string,
        // own<tensor>>>) PARAM (gap 22 follow-up).
        //
        // Emit shape:
        //   result = new T[count]
        //   for (int i = 0; i < count; i++):
        //     elemBase = listPtr + i*elemSize
        //     result[i] = <lift each field at elemBase+fieldOffset>
        //                 <ctor ValueTuple<...>(field0, field1, ...)>
        //   leave result on stack
        private static void EmitLiftListOfRecordOrTuple(
            ILGenerator il, Type elemType,
            LocalBuilder listPtrLocal, LocalBuilder countLocal,
            HostPackageResolver? resolver, Type? resourcesType,
            CanonOption.Kind stringEncoding =
                CanonOption.Kind.StringUtf8)
        {
            int elemSize = SizeOfRecordOrTuple(elemType, resolver);

            // result = new T[count]
            var resultLocal = il.DeclareLocal(elemType.MakeArrayType());
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Newarr, elemType);
            il.Emit(OpCodes.Stloc, resultLocal);

            // for (int i = 0; i < count; i++)
            var indexLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, indexLocal);

            var loopStart = il.DefineLabel();
            var loopCheck = il.DefineLabel();
            il.Emit(OpCodes.Br, loopCheck);
            il.MarkLabel(loopStart);

            // elemBase = listPtr + i*elemSize
            var elemBaseLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldloc, listPtrLocal);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldc_I4, elemSize);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, elemBaseLocal);

            // result[i] = <lifted tuple/record>
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, indexLocal);

            EmitInlineRecordOrTupleLift(il, elemType, elemBaseLocal,
                resolver, resourcesType, stringEncoding);

            // ValueTuple is a struct → Stelem with the type token.
            // Records are reference types → Stelem_Ref.
            if (elemType.IsValueType)
                il.Emit(OpCodes.Stelem, elemType);
            else
                il.Emit(OpCodes.Stelem_Ref);

            // i++
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, indexLocal);

            il.MarkLabel(loopCheck);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Blt, loopStart);

            il.Emit(OpCodes.Ldloc, resultLocal);
        }

        // Emit IL that reads each tuple field at its canon-ABI
        // offset within the element area (base = elemBaseLocal),
        // pushes the field values onto the stack in declaration
        // order, then calls the matching ValueTuple ctor. Records
        // are not yet supported in v0 (no test harness for shape
        // of `record { string name; own<R> handle }`); throw if
        // a record is passed in.
        private static void EmitInlineRecordOrTupleLift(
            ILGenerator il, Type type, LocalBuilder elemBaseLocal,
            HostPackageResolver? resolver, Type? resourcesType,
            CanonOption.Kind stringEncoding)
        {
            bool isTuple = type.IsGenericType
                && IsValueTupleType(type.GetGenericTypeDefinition());
            if (!isTuple)
                throw new NotSupportedException(
                    "PARAM-side per-element lift currently supports "
                    + "ValueTuple<...> only. Record-of-flat-fields "
                    + "(POCO) lift is unblocked by adding the "
                    + "set-property emit path here. Type: "
                    + type.FullName);

            var elements = type.GetGenericArguments();
            int offsetSoFar = 0;
            for (int i = 0; i < elements.Length; i++)
            {
                var et = elements[i];
                int fieldAlign = AlignOfFlatField(et, resolver);
                int fieldOffset = Align(offsetSoFar, fieldAlign);

                EmitLiftFieldFromMem(il, et, elemBaseLocal,
                    fieldOffset, resolver, resourcesType,
                    stringEncoding);

                offsetSoFar = fieldOffset
                    + SizeOfFlatField(et, resolver);
            }

            il.Emit(OpCodes.Newobj, ResolveValueTupleCtor(type));
        }

        // Per-field lift helper: dispatches on field type and
        // pushes the lifted value onto the stack. Mirror of
        // EmitTupleOrRecordFieldStore on the store side.
        //   string  : LiftUtf8(memory, ReadI32(elemBase+off+0),
        //                              ReadI32(elemBase+off+4))
        //   byte[]  : LiftPrim<byte>(memory, ReadI32(elemBase+off+0),
        //                                    ReadI32(elemBase+off+4))
        //   own<R>  : ctx.Resources.GetResource(typeof(IR),
        //                ReadI32(elemBase+off)) → cast
        //   prim    : ReadXxxLE(memory, elemBase+off)
        private static void EmitLiftFieldFromMem(ILGenerator il,
            Type fieldType, LocalBuilder elemBaseLocal, int fieldOffset,
            HostPackageResolver? resolver, Type? resourcesType,
            CanonOption.Kind stringEncoding)
        {
            bool isString = fieldType == typeof(string);
            bool isByteArray = fieldType == typeof(byte[]);
            bool isResource = resolver != null
                && resourcesType != null
                && resolver.IsResourceInterface(fieldType);

            if (isString || isByteArray)
            {
                // Lift sequence:
                //   ptr = ReadI32(memory, elemBase+off+0)
                //   len = ReadI32(memory, elemBase+off+4)
                //   stack-result = LiftXxx(memory, ptr, len)
                //
                // We stash ptr/len in locals because (a) the
                // ReadI32 Calls each consume their own (memory,
                // offset) pair, so the values can't sit on the
                // stack between reads, and (b) LiftXxx needs the
                // (memory, ptr, len) triple in order.
                LocalBuilder strPtrLocal = il.DeclareLocal(typeof(int));
                LocalBuilder strLenLocal = il.DeclareLocal(typeof(int));
                // ptr = ReadI32(memory, elemBase+off+0)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, MemoriesField);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldloc, elemBaseLocal);
                if (fieldOffset != 0)
                {
                    il.Emit(OpCodes.Ldc_I4, fieldOffset);
                    il.Emit(OpCodes.Add);
                }
                il.Emit(OpCodes.Call, ResolveLoadMethod(typeof(int)));
                il.Emit(OpCodes.Stloc, strPtrLocal);
                // len = ReadI32(memory, elemBase+off+4)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, MemoriesField);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldloc, elemBaseLocal);
                il.Emit(OpCodes.Ldc_I4, fieldOffset + 4);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Call, ResolveLoadMethod(typeof(int)));
                il.Emit(OpCodes.Stloc, strLenLocal);
                // Lift call: (memory, ptr, len) → string / byte[]
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, MemoriesField);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldloc, strPtrLocal);
                il.Emit(OpCodes.Ldloc, strLenLocal);
                if (isString)
                    il.Emit(OpCodes.Call, ResolveLiftStringMethod(
                        stringEncoding));
                else
                    il.Emit(OpCodes.Call, ResolveLiftPrimMethod(
                        typeof(byte)));
                return;
            }

            if (isResource)
            {
                // ctx.Resources.GetResource(typeof(IR),
                //     ReadI32(memory, elemBase+off)) → cast
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ResourcesField);
                il.Emit(OpCodes.Castclass, resourcesType!);
                il.Emit(OpCodes.Ldtoken, fieldType);
                il.Emit(OpCodes.Call, GetTypeFromHandleMethod);
                // handle = ReadI32(memory, elemBase+off)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, MemoriesField);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldloc, elemBaseLocal);
                if (fieldOffset != 0)
                {
                    il.Emit(OpCodes.Ldc_I4, fieldOffset);
                    il.Emit(OpCodes.Add);
                }
                il.Emit(OpCodes.Call, ResolveLoadMethod(typeof(int)));
                il.Emit(OpCodes.Callvirt,
                    ResolveGetResourceMethod(resourcesType!));
                il.Emit(OpCodes.Castclass, fieldType);
                return;
            }

            // Primitive: ReadXxxLE(memory, elemBase+off).
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, elemBaseLocal);
            if (fieldOffset != 0)
            {
                il.Emit(OpCodes.Ldc_I4, fieldOffset);
                il.Emit(OpCodes.Add);
            }
            il.Emit(OpCodes.Call, ResolveLoadMethod(fieldType));
        }

        // Emit IL that stores an Option<inner> at retArea+baseOffset.
        // Reads the Option value from optionLocal (top-level: the
        // function's return; nested Option<Option<X>>: a stashed
        // local holding the inner Option<X>).
        //
        // Wire form: u8 disc @ retArea+baseOffset + value at
        // retArea+baseOffset+valueOffset where valueOffset =
        // Align(1, MaxAlignOf(inner)).
        //
        // Inner shapes handled:
        //   - primitive: PrimitiveStore.StoreXxx
        //   - resource (own<R>): Resources.AllocateResource + StoreI32
        //   - string / byte[]: cabi_realloc + StoreString/StoreByteArray
        //   - primitive[] / string[]: cabi_realloc + StoreXxxList
        //   - tuple / record of primitives: per-field write
        //   - Option<X>: recursive call (Option<Option<...>>)
        private static void EmitOptionStoreAt(
            ILGenerator il, Type optionType, LocalBuilder optionLocal,
            LocalBuilder retAreaLocal, int baseOffset,
            HostPackageResolver? resolver, Type? resourcesType,
            CanonOption.Kind stringEncoding =
                CanonOption.Kind.StringUtf8)
        {
            var inner = optionType.GetGenericArguments()[0];
            bool innerIsResource = resolver != null
                && resolver.IsResourceInterface(inner)
                && resourcesType != null;
            bool innerIsString = inner == typeof(string);
            bool innerIsByteArray = inner == typeof(byte[]);
            bool innerIsTuple = IsTupleOfPrimitives(inner);
            bool innerIsRecord = IsRecordOfPrimitives(inner);
            bool innerIsPrimArray = !innerIsByteArray
                && inner.IsArray
                && inner.GetArrayRank() == 1
                && IsListPrimitiveElement(
                    inner.GetElementType()!);
            bool innerIsStringArray = inner.IsArray
                && inner.GetArrayRank() == 1
                && inner.GetElementType() == typeof(string);
            bool innerIsOption = inner.IsGenericType
                && inner.GetGenericTypeDefinition() == typeof(Option<>);
            bool innerIsResult = inner.IsGenericType
                && inner.GetGenericTypeDefinition() == typeof(Result<,>);

            int valueAlign = MaxAlignOf(inner, resolver);
            int valueOffset = Align(1, valueAlign);

            var hasValueGetter = optionType.GetProperty("HasValue",
                BindingFlags.Public | BindingFlags.Instance)!
                .GetGetMethod()!;
            var valueGetter = optionType.GetProperty("Value",
                BindingFlags.Public | BindingFlags.Instance)!
                .GetGetMethod()!;

            var noneLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldloca, optionLocal);
            il.Emit(OpCodes.Call, hasValueGetter);
            il.Emit(OpCodes.Brfalse, noneLabel);

            // Some: write disc=1 at retArea+baseOffset
            EmitWriteByteAt(il, retAreaLocal, baseOffset, 1);

            if (innerIsOption)
            {
                // Recurse: stash inner Option, recursively call self
                // with baseOffset += valueOffset.
                var innerLocal = il.DeclareLocal(inner);
                il.Emit(OpCodes.Ldloca, optionLocal);
                il.Emit(OpCodes.Call, valueGetter);
                il.Emit(OpCodes.Stloc, innerLocal);
                EmitOptionStoreAt(il, inner, innerLocal, retAreaLocal,
                    baseOffset + valueOffset, resolver, resourcesType,
                    stringEncoding);
            }
            else if (innerIsResult)
            {
                // Option<Result<X, Y>>: stash inner Result, dispatch
                // to the Result helper at our value slot.
                var innerLocal = il.DeclareLocal(inner);
                il.Emit(OpCodes.Ldloca, optionLocal);
                il.Emit(OpCodes.Call, valueGetter);
                il.Emit(OpCodes.Stloc, innerLocal);
                EmitResultStoreAt(il, inner, innerLocal, retAreaLocal,
                    baseOffset + valueOffset, resolver, resourcesType,
                    stringEncoding);
            }
            else if (innerIsTuple || innerIsRecord)
            {
                var innerLocal = il.DeclareLocal(inner);
                il.Emit(OpCodes.Ldloca, optionLocal);
                il.Emit(OpCodes.Call, valueGetter);
                il.Emit(OpCodes.Stloc, innerLocal);
                EmitInlineRecordOrTupleStore(il, inner, retAreaLocal,
                    baseOffset + valueOffset, innerLocal,
                    stringEncoding);
            }
            else
            {
                int totalOffset = baseOffset + valueOffset;

                if (innerIsString || innerIsByteArray
                    || innerIsPrimArray || innerIsStringArray)
                {
                    // Variable-length value: pass MemoryInstance so
                    // the helper re-fetches mem.Data post-cabi_realloc
                    // (the alloc may have grown linear memory).
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, MemoriesField);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ldelem_Ref);
                    il.Emit(OpCodes.Ldloc, retAreaLocal);
                    if (totalOffset != 0)
                    {
                        il.Emit(OpCodes.Ldc_I4, totalOffset);
                        il.Emit(OpCodes.Add);
                    }
                    il.Emit(OpCodes.Ldloca, optionLocal);
                    il.Emit(OpCodes.Call, valueGetter);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, CabiReallocField);
                    MethodInfo storeMethod;
                    if (innerIsString)
                        storeMethod = ResolveStoreStringMethod(stringEncoding);
                    else if (innerIsByteArray)
                        storeMethod = StoreByteArrayMethod;
                    else if (innerIsStringArray)
                        storeMethod = StoreStringListMethod;
                    else
                        storeMethod = ResolveStorePrimitiveArrayMethod(
                            inner.GetElementType()!);
                    il.Emit(OpCodes.Call, storeMethod);
                }
                else
                {
                    // Fixed-width value: pass byte[] dest.
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, MemoriesField);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ldelem_Ref);
                    il.Emit(OpCodes.Ldloc, retAreaLocal);
                    if (totalOffset != 0)
                    {
                        il.Emit(OpCodes.Ldc_I4, totalOffset);
                        il.Emit(OpCodes.Add);
                    }
                    if (innerIsResource)
                    {
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, ResourcesField);
                        il.Emit(OpCodes.Castclass, resourcesType!);
                        il.Emit(OpCodes.Ldtoken, inner);
                        il.Emit(OpCodes.Call, GetTypeFromHandleMethod);
                        il.Emit(OpCodes.Ldloca, optionLocal);
                        il.Emit(OpCodes.Call, valueGetter);
                        il.Emit(OpCodes.Callvirt,
                            ResolveAllocateResourceMethod(resourcesType!));
                        il.Emit(OpCodes.Call,
                            ResolveStoreMethod(typeof(int)));
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldloca, optionLocal);
                        il.Emit(OpCodes.Call, valueGetter);
                        il.Emit(OpCodes.Call, ResolveStoreMethod(inner));
                    }
                }
            }

            il.Emit(OpCodes.Br, endLabel);

            // None: write disc=0 at retArea+baseOffset
            il.MarkLabel(noneLabel);
            EmitWriteByteAt(il, retAreaLocal, baseOffset, 0);

            il.MarkLabel(endLabel);
        }

        // Emit IL that stores a variant<...> at retArea+baseOffset.
        // Reads the variant value from variantLocal.
        //
        // Wire form: u8 disc @ retArea+baseOffset + joined-flat
        // payload at retArea+baseOffset+valueOffset where
        // valueOffset = Align(1, max-payload-align). Empty cases
        // leave the payload slot uninitialized.
        //
        // Per-case payload dispatch handles all the same shapes
        // EmitResultArmStore handles: primitive, resource (own<R>),
        // string/byte[] + primitive[]/string[] (cabi_realloc),
        // tuple/record-of-flat-fields, nested Option<X>/Result<X,Y>.
        private static void EmitVariantStoreAt(
            ILGenerator il, Type variantType, LocalBuilder variantLocal,
            LocalBuilder retAreaLocal, int baseOffset,
            HostPackageResolver? resolver, Type? resourcesType,
            CanonOption.Kind stringEncoding =
                CanonOption.Kind.StringUtf8)
        {
            var cases = GetVariantCases(variantType);
            int maxPayloadAlign = 1;
            foreach (var c in cases)
            {
                if (c.Payload == null) continue;
                int a = AlignOfResultArm(c.Payload);
                if (a > maxPayloadAlign) maxPayloadAlign = a;
            }
            int valueOffset = Align(1, maxPayloadAlign);

            var endLabel = il.DefineLabel();
            var caseStoreLabels =
                new System.Reflection.Emit.Label[cases.Length];
            for (int i = 0; i < cases.Length; i++)
                caseStoreLabels[i] = il.DefineLabel();

            // Chain: for each case, ldloc variantLocal;
            // isinst <CaseType>; brtrue case_i_label.
            // The last case falls through to its store
            // (no branch needed).
            for (int i = 0; i < cases.Length - 1; i++)
            {
                il.Emit(OpCodes.Ldloc, variantLocal);
                il.Emit(OpCodes.Isinst, cases[i].CaseType);
                il.Emit(OpCodes.Brtrue, caseStoreLabels[i]);
            }
            il.Emit(OpCodes.Br, caseStoreLabels[cases.Length - 1]);

            for (int i = 0; i < cases.Length; i++)
            {
                il.MarkLabel(caseStoreLabels[i]);
                // disc=i at retArea+baseOffset
                EmitWriteByteAt(il, retAreaLocal, baseOffset, i);

                var c = cases[i];
                if (c.Payload != null)
                {
                    int payloadAddrOffset = baseOffset + valueOffset;
                    bool isResourcePayload = resolver != null
                        && resolver.IsResourceInterface(c.Payload)
                        && resourcesType != null;

                    var valueProp = c.CaseType.GetProperty("Value",
                        BindingFlags.Public | BindingFlags.Instance)!;

                    bool isStringPayload = c.Payload == typeof(string);
                    bool isByteArrayPayload = c.Payload == typeof(byte[]);
                    bool isPrimArrayPayload = !isByteArrayPayload
                        && c.Payload.IsArray
                        && c.Payload.GetArrayRank() == 1
                        && IsListPrimitiveElement(
                            c.Payload.GetElementType()!);
                    bool isStringArrayPayload =
                        c.Payload.IsArray
                        && c.Payload.GetArrayRank() == 1
                        && c.Payload.GetElementType() == typeof(string);
                    bool isAggregatePayload =
                        IsTupleOfPrimitives(c.Payload)
                        || IsRecordOfPrimitives(c.Payload);
                    bool isOptionPayload =
                        c.Payload.IsGenericType
                        && c.Payload.GetGenericTypeDefinition()
                            == typeof(Option<>);
                    bool isResultPayload =
                        c.Payload.IsGenericType
                        && c.Payload.GetGenericTypeDefinition()
                            == typeof(Result<,>);

                    if (isOptionPayload || isResultPayload)
                    {
                        var payloadLocal = il.DeclareLocal(c.Payload);
                        il.Emit(OpCodes.Ldloc, variantLocal);
                        il.Emit(OpCodes.Castclass, c.CaseType);
                        il.Emit(OpCodes.Callvirt,
                            valueProp.GetGetMethod()!);
                        il.Emit(OpCodes.Stloc, payloadLocal);
                        if (isOptionPayload)
                            EmitOptionStoreAt(il, c.Payload,
                                payloadLocal, retAreaLocal,
                                payloadAddrOffset, resolver,
                                resourcesType, stringEncoding);
                        else
                            EmitResultStoreAt(il, c.Payload,
                                payloadLocal, retAreaLocal,
                                payloadAddrOffset, resolver,
                                resourcesType, stringEncoding);
                    }
                    else if (isAggregatePayload)
                    {
                        var payloadLocal = il.DeclareLocal(c.Payload);
                        il.Emit(OpCodes.Ldloc, variantLocal);
                        il.Emit(OpCodes.Castclass, c.CaseType);
                        il.Emit(OpCodes.Callvirt,
                            valueProp.GetGetMethod()!);
                        il.Emit(OpCodes.Stloc, payloadLocal);
                        EmitInlineRecordOrTupleStore(il, c.Payload,
                            retAreaLocal, payloadAddrOffset,
                            payloadLocal, stringEncoding);
                    }
                    else
                    {
                        if (isStringPayload || isByteArrayPayload
                            || isPrimArrayPayload || isStringArrayPayload)
                        {
                            // Variable-length payload: pass MemoryInstance.
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, MemoriesField);
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ldelem_Ref);
                            il.Emit(OpCodes.Ldloc, retAreaLocal);
                            if (payloadAddrOffset != 0)
                            {
                                il.Emit(OpCodes.Ldc_I4, payloadAddrOffset);
                                il.Emit(OpCodes.Add);
                            }
                            il.Emit(OpCodes.Ldloc, variantLocal);
                            il.Emit(OpCodes.Castclass, c.CaseType);
                            il.Emit(OpCodes.Callvirt,
                                valueProp.GetGetMethod()!);
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, CabiReallocField);
                            MethodInfo storeMethod;
                            if (isStringPayload)
                                storeMethod = ResolveStoreStringMethod(stringEncoding);
                            else if (isByteArrayPayload)
                                storeMethod = StoreByteArrayMethod;
                            else if (isStringArrayPayload)
                                storeMethod = StoreStringListMethod;
                            else
                                storeMethod = ResolveStorePrimitiveArrayMethod(
                                    c.Payload.GetElementType()!);
                            il.Emit(OpCodes.Call, storeMethod);
                        }
                        else
                        {
                            // Fixed-width payload: pass byte[] dest.
                            il.Emit(OpCodes.Ldarg_0);
                            il.Emit(OpCodes.Ldfld, MemoriesField);
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ldelem_Ref);
                            il.Emit(OpCodes.Ldloc, retAreaLocal);
                            if (payloadAddrOffset != 0)
                            {
                                il.Emit(OpCodes.Ldc_I4, payloadAddrOffset);
                                il.Emit(OpCodes.Add);
                            }

                            if (isResourcePayload)
                            {
                                il.Emit(OpCodes.Ldarg_0);
                                il.Emit(OpCodes.Ldfld, ResourcesField);
                                il.Emit(OpCodes.Castclass, resourcesType!);
                                il.Emit(OpCodes.Ldtoken, c.Payload);
                                il.Emit(OpCodes.Call, GetTypeFromHandleMethod);
                                il.Emit(OpCodes.Ldloc, variantLocal);
                                il.Emit(OpCodes.Castclass, c.CaseType);
                                il.Emit(OpCodes.Callvirt,
                                    valueProp.GetGetMethod()!);
                                il.Emit(OpCodes.Callvirt,
                                    ResolveAllocateResourceMethod(resourcesType!));
                                il.Emit(OpCodes.Call,
                                    ResolveStoreMethod(typeof(int)));
                            }
                            else
                            {
                                il.Emit(OpCodes.Ldloc, variantLocal);
                                il.Emit(OpCodes.Castclass, c.CaseType);
                                il.Emit(OpCodes.Callvirt,
                                    valueProp.GetGetMethod()!);
                                il.Emit(OpCodes.Call,
                                    ResolveStoreMethod(c.Payload));
                            }
                        }
                    }
                }
                il.Emit(OpCodes.Br, endLabel);
            }
            il.MarkLabel(endLabel);
        }

        // Emit IL that stores a Result<TOk, TErr> at retArea+baseOffset.
        // Reads the Result value from resultLocal.
        //
        // Wire form: u8 disc @ retArea+baseOffset (0=Ok, 1=Err) +
        // joined-flat value at retArea+baseOffset+valueOffset where
        // valueOffset = Align(1, max(MaxAlignOf(Ok), MaxAlignOf(Err))).
        private static void EmitResultStoreAt(
            ILGenerator il, Type resultType, LocalBuilder resultLocal,
            LocalBuilder retAreaLocal, int baseOffset,
            HostPackageResolver? resolver, Type? resourcesType,
            CanonOption.Kind stringEncoding =
                CanonOption.Kind.StringUtf8)
        {
            var args = resultType.GetGenericArguments();
            int okAlign = MaxAlignOf(args[0], resolver);
            int errAlign = MaxAlignOf(args[1], resolver);
            int valueAlign = okAlign > errAlign ? okAlign : errAlign;
            int valueOffset = Align(1, valueAlign);

            var isOkGetter = resultType.GetProperty("IsOk",
                BindingFlags.Public | BindingFlags.Instance)!
                .GetGetMethod()!;
            var okGetter = resultType.GetProperty("Ok",
                BindingFlags.Public | BindingFlags.Instance)!
                .GetGetMethod()!;
            var errGetter = resultType.GetProperty("Err",
                BindingFlags.Public | BindingFlags.Instance)!
                .GetGetMethod()!;

            var errLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldloca, resultLocal);
            il.Emit(OpCodes.Call, isOkGetter);
            il.Emit(OpCodes.Brfalse, errLabel);

            // Ok: write disc=0 + Ok value
            EmitWriteByteAt(il, retAreaLocal, baseOffset, 0);
            EmitResultArmStore(il, args[0], resultLocal,
                okGetter, retAreaLocal, baseOffset + valueOffset,
                resolver, resourcesType, stringEncoding);
            il.Emit(OpCodes.Br, endLabel);

            // Err: write disc=1 + Err value
            il.MarkLabel(errLabel);
            EmitWriteByteAt(il, retAreaLocal, baseOffset, 1);
            EmitResultArmStore(il, args[1], resultLocal,
                errGetter, retAreaLocal, baseOffset + valueOffset,
                resolver, resourcesType, stringEncoding);

            il.MarkLabel(endLabel);
        }

        // Helper: emit IL that writes a single u8 at retArea+offset.
        private static void EmitWriteByteAt(ILGenerator il,
            LocalBuilder retAreaLocal, int offset, int value)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            if (offset != 0)
            {
                il.Emit(OpCodes.Ldc_I4, offset);
                il.Emit(OpCodes.Add);
            }
            il.Emit(OpCodes.Ldc_I4, value);
            il.Emit(OpCodes.Call, ResolveStoreMethod(typeof(byte)));
        }

        // Recursive maximum alignment for any supported aggregate
        // shape. Used by Option/Result emit to compute valueOffset.
        private static int MaxAlignOf(Type t,
            HostPackageResolver? resolver)
        {
            if (IsStorablePrimitive(t)) return AlignOfPrimitive(t);
            if (IsTupleOfPrimitives(t) || IsRecordOfPrimitives(t))
                return MaxFieldAlign(t, resolver);
            // Tuple/record-with-flat-fields (incl. Option fields):
            // align is the max of recursive MaxAlignOf across fields.
            if (IsTupleOfFlatFields(t, resolver)
                || IsRecordOfFlatFields(t, resolver))
                return MaxFieldAlign(t, resolver);
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(Option<>))
                    return MaxAlignOf(t.GetGenericArguments()[0],
                        resolver);
                if (def == typeof(Result<,>))
                {
                    var args = t.GetGenericArguments();
                    int a0 = MaxAlignOf(args[0], resolver);
                    int a1 = MaxAlignOf(args[1], resolver);
                    return a0 > a1 ? a0 : a1;
                }
            }
            // Variant: max alignment across payload-bearing cases.
            // Empty cases contribute the disc's u8 alignment (1).
            if (IsLikelyVariantBase(t))
            {
                int maxA = 1;
                foreach (var c in GetVariantCases(t))
                {
                    if (c.Payload == null) continue;
                    int a = AlignOfResultArm(c.Payload);
                    if (a > maxA) maxA = a;
                }
                return maxA;
            }
            // string / byte[] / list<X> / resource interface — all
            // i32-aligned (ptr or handle).
            return 4;
        }

        // Emit IL for a list<own<R>> top-level return.
        //   1. count = arrayLocal.Length
        //   2. outerPtr = ctx.CabiRealloc(0, 0, 4, count*4)
        //   3. for (int i = 0; i < count; i++):
        //        handle = ctx.Resources.AllocateResource(typeof(IRes), array[i])
        //        StoreI32(memory, outerPtr + i*4, handle)
        //   4. Write (outerPtr, count) to retArea+0/+4.
        private static void EmitListOfResourceReturn(
            ILGenerator il, Type resInterface, LocalBuilder arrayLocal,
            LocalBuilder retAreaLocal, Type resourcesType)
        {
            // count = arrayLocal.Length
            var countLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldloc, arrayLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, countLocal);

            // outerPtr = ctx.CabiRealloc(0, 0, 4, count*4)
            var outerPtrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, CabiReallocField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Callvirt,
                typeof(Func<int, int, int, int, int>)
                    .GetMethod("Invoke")!);
            il.Emit(OpCodes.Stloc, outerPtrLocal);

            // for i = 0; i < count; i++
            var indexLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, indexLocal);

            var loopStart = il.DefineLabel();
            var loopCheck = il.DefineLabel();
            il.Emit(OpCodes.Br, loopCheck);
            il.MarkLabel(loopStart);

            // Push: dest_array, dest_offset = outerPtr + i*4
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, outerPtrLocal);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Add);

            // handle = ctx.Resources.AllocateResource(typeof(IRes), array[i])
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ResourcesField);
            il.Emit(OpCodes.Castclass, resourcesType);
            il.Emit(OpCodes.Ldtoken, resInterface);
            il.Emit(OpCodes.Call, GetTypeFromHandleMethod);
            il.Emit(OpCodes.Ldloc, arrayLocal);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Callvirt,
                ResolveAllocateResourceMethod(resourcesType));

            // StoreI32(dest_array, dest_offset, handle)
            il.Emit(OpCodes.Call, ResolveStoreMethod(typeof(int)));

            // i++
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, indexLocal);

            il.MarkLabel(loopCheck);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Blt, loopStart);

            // Write (outerPtr, count) to retArea + 0 / + 4.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.Emit(OpCodes.Ldloc, outerPtrLocal);
            il.Emit(OpCodes.Call, ResolveStoreMethod(typeof(int)));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Call, ResolveStoreMethod(typeof(int)));
        }

        // Emit IL for a list<Option<X>> or list<Result<X,Y>> top-
        // level return.
        //   1. count = arrayLocal.Length
        //   2. outerPtr = ctx.CabiRealloc(0, 0, elemAlign,
        //                                  count*elemSize)
        //   3. for (int i = 0; i < count; i++):
        //        perElementBase = outerPtr + i*elemSize  (in local)
        //        elem = array[i] (Option/Result, in local)
        //        EmitOptionStoreAt(elem, perElementBase, baseOffset=0)
        //          OR EmitResultStoreAt(elem, perElementBase, 0)
        //   4. Write (outerPtr, count) to retArea+0/+4.
        // The reuse trick: pass perElementBaseLocal as
        // retAreaLocal + baseOffset=0 to the existing helpers — they
        // compute disc/value offsets relative to retAreaLocal already.
        private static void EmitListOfOptionOrResultReturn(
            ILGenerator il, Type elemType, LocalBuilder arrayLocal,
            LocalBuilder retAreaLocal,
            HostPackageResolver? resolver, Type? resourcesType,
            CanonOption.Kind stringEncoding)
        {
            int elemSize = SizeOf(elemType, resolver);
            int elemAlign = MaxAlignOf(elemType, resolver);
            bool isVariant = IsLikelyVariantBase(elemType);
            bool isOption = !isVariant
                && elemType.GetGenericTypeDefinition() == typeof(Option<>);

            // count = arrayLocal.Length
            var countLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldloc, arrayLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, countLocal);

            // outerPtr = ctx.CabiRealloc(0, 0, elemAlign, count*elemSize)
            var outerPtrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, CabiReallocField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4, elemAlign);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Ldc_I4, elemSize);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Callvirt,
                typeof(Func<int, int, int, int, int>)
                    .GetMethod("Invoke")!);
            il.Emit(OpCodes.Stloc, outerPtrLocal);

            // for i = 0; i < count; i++
            var indexLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, indexLocal);

            var loopStart = il.DefineLabel();
            var loopCheck = il.DefineLabel();
            il.Emit(OpCodes.Br, loopCheck);
            il.MarkLabel(loopStart);

            // perElementBase = outerPtr + i * elemSize
            var perElementBaseLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldloc, outerPtrLocal);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldc_I4, elemSize);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, perElementBaseLocal);

            // elem = array[i]
            var elemLocal = il.DeclareLocal(elemType);
            il.Emit(OpCodes.Ldloc, arrayLocal);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldelem, elemType);
            il.Emit(OpCodes.Stloc, elemLocal);

            // Write: pass perElementBase as retArea, baseOffset=0.
            if (isVariant)
                EmitVariantStoreAt(il, elemType, elemLocal,
                    perElementBaseLocal, 0,
                    resolver, resourcesType, stringEncoding);
            else if (isOption)
                EmitOptionStoreAt(il, elemType, elemLocal,
                    perElementBaseLocal, 0,
                    resolver, resourcesType, stringEncoding);
            else
                EmitResultStoreAt(il, elemType, elemLocal,
                    perElementBaseLocal, 0,
                    resolver, resourcesType, stringEncoding);

            // i++
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, indexLocal);

            il.MarkLabel(loopCheck);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Blt, loopStart);

            // Write (outerPtr, count) to retArea+0/+4.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.Emit(OpCodes.Ldloc, outerPtrLocal);
            il.Emit(OpCodes.Call, ResolveStoreMethod(typeof(int)));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Call, ResolveStoreMethod(typeof(int)));
        }

        // Total wire size of a tuple/record-of-primitives, accounting
        // for per-field alignment padding. Used to compute the
        // per-element stride in list<record>/list<tuple>.
        // Resolver-aware: when a field is a resource interface, its
        // wire size is 4 (i32 handle) rather than the 8-byte ptr+len
        // default.
        private static int SizeOfRecordOrTuple(Type type,
            HostPackageResolver? resolver = null)
        {
            int offsetSoFar = 0;
            bool isTuple = type.IsGenericType
                && IsValueTupleType(type.GetGenericTypeDefinition());
            if (isTuple)
            {
                foreach (var et in type.GetGenericArguments())
                {
                    int fa = AlignOfFlatField(et, resolver);
                    offsetSoFar = Align(offsetSoFar, fa)
                        + SizeOfFlatField(et, resolver);
                }
            }
            else
            {
                foreach (var p in GetRecordProperties(type))
                {
                    int fa = AlignOfFlatField(p.PropertyType, resolver);
                    offsetSoFar = Align(offsetSoFar, fa)
                        + SizeOfFlatField(p.PropertyType, resolver);
                }
            }
            // canon-ABI: pad the total to the type's alignment so
            // arrays of this type pack contiguously.
            int maxA = MaxFieldAlign(type, resolver);
            return Align(offsetSoFar, maxA);
        }

        // Recursive total size of a supported aggregate type,
        // padded to the type's max alignment so arrays of this
        // type pack contiguously.
        //   Option<T> : Align(1, MaxAlignOf(T)) + SizeOf(T)
        //              padded to MaxAlignOf(T).
        //   Result<X,Y>: Align(1, max(MaxAlignOf(X), MaxAlignOf(Y)))
        //               + max(SizeOf(X), SizeOf(Y))
        //              padded to max(MaxAlignOf(X), MaxAlignOf(Y)).
        //   tuple/record: SizeOfRecordOrTuple
        //   primitive: SizeOfPrimitive
        //   resource handle: 4
        //   string/byte[]/list-style: 8 (i32 ptr + i32 len/count)
        private static int SizeOf(Type t,
            HostPackageResolver? resolver)
        {
            if (IsStorablePrimitive(t)) return SizeOfPrimitive(t);
            if (IsTupleOfPrimitives(t) || IsRecordOfPrimitives(t))
                return SizeOfRecordOrTuple(t, resolver);
            // Tuple/record-with-flat-fields (incl. Option-typed
            // fields): walk fields via SizeOfRecordOrTuple, which
            // dispatches per-field through SizeOfFlatField.
            if (IsTupleOfFlatFields(t, resolver)
                || IsRecordOfFlatFields(t, resolver))
                return SizeOfRecordOrTuple(t, resolver);
            if (resolver != null
                && resolver.IsResourceInterface(t))
                return 4;
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(Option<>))
                {
                    var inner = t.GetGenericArguments()[0];
                    int innerAlign = MaxAlignOf(inner, resolver);
                    int valueOffset = Align(1, innerAlign);
                    int total = valueOffset + SizeOf(inner, resolver);
                    return Align(total, innerAlign);
                }
                if (def == typeof(Result<,>))
                {
                    var args = t.GetGenericArguments();
                    int a0 = MaxAlignOf(args[0], resolver);
                    int a1 = MaxAlignOf(args[1], resolver);
                    int armAlign = a0 > a1 ? a0 : a1;
                    int s0 = SizeOf(args[0], resolver);
                    int s1 = SizeOf(args[1], resolver);
                    int armSize = s0 > s1 ? s0 : s1;
                    int valueOffset = Align(1, armAlign);
                    return Align(valueOffset + armSize, armAlign);
                }
            }
            // Variant: u8 disc + max payload size at common
            // valueOffset, padded to max payload alignment.
            if (IsLikelyVariantBase(t))
            {
                int maxAlign = 1;
                int maxSize = 0;
                foreach (var c in GetVariantCases(t))
                {
                    if (c.Payload == null) continue;
                    int a = AlignOfResultArm(c.Payload);
                    if (a > maxAlign) maxAlign = a;
                    int s = SizeOf(c.Payload, resolver);
                    if (s > maxSize) maxSize = s;
                }
                int valueOffset = Align(1, maxAlign);
                return Align(valueOffset + maxSize, maxAlign);
            }
            // Fallback for string / byte[] / list<T>.
            return 8;
        }

        // True when this CLR type maps directly to a single
        // canon-ABI primitive store (a PrimitiveStore.StoreXxx
        // helper exists for it). Subset of IsPrimitiveCompatible
        // — excludes types whose wire shape needs alignment that
        // isn't trivially derivable from the CLR type alone.
        private static bool IsStorablePrimitive(Type t)
        {
            if (t.IsEnum) t = Enum.GetUnderlyingType(t);
            return t == typeof(byte) || t == typeof(sbyte)
                || t == typeof(short) || t == typeof(ushort)
                || t == typeof(int) || t == typeof(uint)
                || t == typeof(long) || t == typeof(ulong)
                || t == typeof(float) || t == typeof(double)
                || t == typeof(bool);
        }

        // True when the Ok or Err arm of a Result return is
        // storable: primitive, resource interface (Resources class
        // mints a handle), string/byte[]/primitive[]/string[]
        // (cabi_realloc), tuple/record-of-primitives (per-field
        // write at common arm offset), OR — when allowVariableLength
        // — nested Option<X>/Result<X,Y> (recurse to corresponding
        // store helper).
        private static bool IsResultArmStorable(Type t,
            HostPackageResolver? resolver,
            bool allowVariableLength = false)
        {
            // Unit — the empty arm of result<_, X> / option<_>.
            // Wire form: nothing past the discriminant. Storable
            // by emitting only the disc write, no value store.
            if (t == typeof(Unit)) return true;
            if (IsStorablePrimitive(t)) return true;
            if (resolver != null
                && resolver.IsResourceInterface(t)
                && resolver.PreferredResourcesType != null)
                return true;
            if (allowVariableLength
                && (t == typeof(string) || t == typeof(byte[])))
                return true;
            if (allowVariableLength
                && (IsTupleOfPrimitives(t)
                    || IsRecordOfPrimitives(t)
                    // Records / tuples whose fields include Option<X>
                    // or own<R> resource handles — covers shapes
                    // like Result<descriptor-stat, error-code>'s OK
                    // arm (record with option<datetime> fields).
                    || IsTupleOfFlatFields(t, resolver)
                    || IsRecordOfFlatFields(t, resolver)))
                return true;
            if (allowVariableLength
                && t.IsArray && t.GetArrayRank() == 1)
            {
                var elem = t.GetElementType()!;
                if (IsListPrimitiveElement(elem)) return true;
                if (elem == typeof(string)) return true;
                // list<tuple-of-flat-fields> / list<record-of-flat-fields>
                // including the resource-bearing variant. SLM compute
                // returns Result<list<tuple<string, own<tensor>>>,
                // own<error>>; without this, the Ok arm fell through
                // to the fixed-width fallback and stored garbage
                // (gap 22). The emit side dispatches into
                // EmitListOfRecordOrTupleReturn at the arm's
                // valueOffset.
                if (IsTupleOfPrimitives(elem)
                    || IsRecordOfPrimitives(elem)
                    || IsTupleOfFlatFields(elem, resolver)
                    || IsRecordOfFlatFields(elem, resolver))
                    return true;
            }
            if (allowVariableLength && t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if ((def == typeof(Option<>)
                        || def == typeof(Result<,>))
                    && IsAggregateReturnSupported(t, resolver))
                    return true;
            }
            // Variant-base arms (e.g. Result<Unit, EmptyVariant>):
            // recurse into EmitResultArmStore which dispatches
            // through EmitVariantStoreAt at the arm offset.
            if (allowVariableLength && IsLikelyVariantBase(t))
            {
                var cases = GetVariantCases(t);
                if (cases.Length == 0) return false;
                foreach (var c in cases)
                {
                    if (c.Payload == null) continue;
                    if (!IsResultArmStorable(c.Payload, resolver,
                            allowVariableLength: true))
                        return false;
                }
                return true;
            }
            return false;
        }

        // Alignment for a Result-arm type. Resource handles are i32,
        // string/byte[]/list<T>/string[] are (i32 ptr + i32 len/count)
        // → all align 4. Tuple/record arms use the max field
        // alignment.
        private static int AlignOfResultArm(Type t)
        {
            if (IsStorablePrimitive(t)) return AlignOfPrimitive(t);
            if (IsTupleOfPrimitives(t) || IsRecordOfPrimitives(t))
                return MaxFieldAlign(t);
            return 4;  // resource handle / list-style (ptr, len/count)
        }

        // Emit IL that stores a Result arm's value at
        // retArea+valueOffset. Dispatches on arm shape:
        //   - primitive          : direct PrimitiveStore.StoreXxx
        //   - resource interface : Resources.AllocateResource → StoreI32
        //   - string / byte[]    : cabi_realloc + StoreString/StoreByteArray
        //   - tuple / record-of-primitives: per-field write via
        //                          EmitInlineRecordOrTupleStore
        // Used by the Result-return Emit for both Ok and Err sides.
        private static void EmitResultArmStore(ILGenerator il,
            Type armType, LocalBuilder returnLocal,
            MethodInfo armGetter, LocalBuilder retAreaLocal,
            int valueOffset, HostPackageResolver? resolver,
            Type? resourcesType,
            CanonOption.Kind stringEncoding =
                CanonOption.Kind.StringUtf8)
        {
            // Unit — empty arm, no value to write past the
            // discriminant byte. Caller already stored disc.
            if (armType == typeof(Unit)) return;

            // Resource-interface arm without a configured resources
            // class is unreachable per CanEmitDirect (the
            // IsResultArmStorable check requires PreferredResourcesType
            // to be non-null), but if it ever happens, surface the
            // gap clearly instead of falling into ResolveStoreMethod
            // which would emit an obscure "no StoreMethod" throw.
            if (resolver != null && resolver.IsResourceInterface(armType)
                && resourcesType == null)
                throw new InvalidOperationException(
                    "Cannot emit Result arm of resource-interface type "
                    + armType.FullName + " without a configured resources "
                    + "class. Pass resourcesType to "
                    + "HostPackageResolver.FromAssemblies or use a host "
                    + "package whose resolver auto-discovers one (e.g. "
                    + "WasiPreview2Resources).");

            // Variant base type — recurse into EmitVariantStoreAt
            // at the arm's offset. Mirrors the nested Option/Result
            // patterns below.
            if (IsLikelyVariantBase(armType))
            {
                var armLocal = il.DeclareLocal(armType);
                il.Emit(OpCodes.Ldloca, returnLocal);
                il.Emit(OpCodes.Call, armGetter);
                il.Emit(OpCodes.Stloc, armLocal);
                EmitVariantStoreAt(il, armType, armLocal,
                    retAreaLocal, valueOffset, resolver,
                    resourcesType, stringEncoding);
                return;
            }

            bool isResource = resolver != null
                && resolver.IsResourceInterface(armType)
                && resourcesType != null;
            bool isString = armType == typeof(string);
            bool isByteArray = armType == typeof(byte[]);
            bool isPrimArray = !isByteArray
                && armType.IsArray
                && armType.GetArrayRank() == 1
                && IsListPrimitiveElement(armType.GetElementType()!);
            bool isStringArray = armType.IsArray
                && armType.GetArrayRank() == 1
                && armType.GetElementType() == typeof(string);
            bool isAggregateArray = armType.IsArray
                && armType.GetArrayRank() == 1
                && (IsTupleOfPrimitives(armType.GetElementType()!)
                    || IsRecordOfPrimitives(armType.GetElementType()!)
                    || IsTupleOfFlatFields(armType.GetElementType()!,
                        resolver)
                    || IsRecordOfFlatFields(armType.GetElementType()!,
                        resolver));
            bool isAggregate = IsTupleOfPrimitives(armType)
                || IsRecordOfPrimitives(armType)
                || IsTupleOfFlatFields(armType, resolver)
                || IsRecordOfFlatFields(armType, resolver);
            bool isNestedOption = armType.IsGenericType
                && armType.GetGenericTypeDefinition() == typeof(Option<>);
            bool isNestedResult = armType.IsGenericType
                && armType.GetGenericTypeDefinition() == typeof(Result<,>);

            // Nested Option/Result arms recurse to the corresponding
            // store helper at the arm's offset within retArea.
            if (isNestedOption)
            {
                var armLocal = il.DeclareLocal(armType);
                il.Emit(OpCodes.Ldloca, returnLocal);
                il.Emit(OpCodes.Call, armGetter);
                il.Emit(OpCodes.Stloc, armLocal);
                EmitOptionStoreAt(il, armType, armLocal, retAreaLocal,
                    valueOffset, resolver, resourcesType,
                    stringEncoding);
                return;
            }
            if (isNestedResult)
            {
                var armLocal = il.DeclareLocal(armType);
                il.Emit(OpCodes.Ldloca, returnLocal);
                il.Emit(OpCodes.Call, armGetter);
                il.Emit(OpCodes.Stloc, armLocal);
                EmitResultStoreAt(il, armType, armLocal, retAreaLocal,
                    valueOffset, resolver, resourcesType,
                    stringEncoding);
                return;
            }

            // Aggregate arms write field-by-field with their own
            // memory pushes — bypass the common pre-push. Resolver
            // and resourcesType are forwarded so flat-fields-with-
            // resources / option fields dispatch correctly.
            if (isAggregate)
            {
                var armLocal = il.DeclareLocal(armType);
                il.Emit(OpCodes.Ldloca, returnLocal);
                il.Emit(OpCodes.Call, armGetter);
                il.Emit(OpCodes.Stloc, armLocal);
                EmitInlineRecordOrTupleStore(il, armType, retAreaLocal,
                    valueOffset, armLocal, stringEncoding,
                    resolver, resourcesType);
                return;
            }

            // list<tuple-of-flat-fields> / list<record-of-flat-fields>
            // arms (gap 22): cabi_realloc the outer buffer, walk the
            // T[] writing each element field-by-field, then store the
            // (outerPtr, count) pair at retArea + valueOffset. SLM
            // compute returns Result<list<tuple<string, own<tensor>>>,
            // own<error>> — this branch carries the Ok side.
            if (isAggregateArray)
            {
                var armLocal = il.DeclareLocal(armType);
                il.Emit(OpCodes.Ldloca, returnLocal);
                il.Emit(OpCodes.Call, armGetter);
                il.Emit(OpCodes.Stloc, armLocal);
                EmitListOfRecordOrTupleReturn(il,
                    armType.GetElementType()!,
                    armLocal, retAreaLocal, stringEncoding,
                    resolver, resourcesType, valueOffset);
                return;
            }

            if (isString || isByteArray
                || isPrimArray || isStringArray)
            {
                // Variable-length arm: pass MemoryInstance so the
                // helper re-fetches mem.Data after its internal
                // cabi_realloc (which may grow memory).
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, MemoriesField);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldloc, retAreaLocal);
                if (valueOffset != 0)
                {
                    il.Emit(OpCodes.Ldc_I4, valueOffset);
                    il.Emit(OpCodes.Add);
                }
                il.Emit(OpCodes.Ldloca, returnLocal);
                il.Emit(OpCodes.Call, armGetter);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, CabiReallocField);
                MethodInfo storeMethod;
                if (isString) storeMethod = ResolveStoreStringMethod(stringEncoding);
                else if (isByteArray) storeMethod = StoreByteArrayMethod;
                else if (isStringArray) storeMethod = StoreStringListMethod;
                else storeMethod = ResolveStorePrimitiveArrayMethod(
                    armType.GetElementType()!);
                il.Emit(OpCodes.Call, storeMethod);
                return;
            }

            // Fixed-width arm (resource handle / primitive): pass
            // byte[] dest. No cabi_realloc, no grow risk.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            if (valueOffset != 0)
            {
                il.Emit(OpCodes.Ldc_I4, valueOffset);
                il.Emit(OpCodes.Add);
            }
            if (isResource)
            {
                // Allocate handle for the arm's instance, store i32.
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ResourcesField);
                il.Emit(OpCodes.Castclass, resourcesType!);
                il.Emit(OpCodes.Ldtoken, armType);
                il.Emit(OpCodes.Call, GetTypeFromHandleMethod);
                il.Emit(OpCodes.Ldloca, returnLocal);
                il.Emit(OpCodes.Call, armGetter);
                il.Emit(OpCodes.Callvirt,
                    ResolveAllocateResourceMethod(resourcesType!));
                il.Emit(OpCodes.Call, ResolveStoreMethod(typeof(int)));
            }
            else
            {
                il.Emit(OpCodes.Ldloca, returnLocal);
                il.Emit(OpCodes.Call, armGetter);
                il.Emit(OpCodes.Call, ResolveStoreMethod(armType));
            }
        }

        // canon-ABI alignment for a primitive type, in bytes.
        private static int AlignOfPrimitive(Type t)
        {
            if (t.IsEnum) t = Enum.GetUnderlyingType(t);
            if (t == typeof(byte) || t == typeof(sbyte)
                || t == typeof(bool)) return 1;
            if (t == typeof(short) || t == typeof(ushort)) return 2;
            if (t == typeof(int) || t == typeof(uint)
                || t == typeof(float)) return 4;
            if (t == typeof(long) || t == typeof(ulong)
                || t == typeof(double)) return 8;
            return 1;
        }

        private static int SizeOfPrimitive(Type t)
        {
            if (t.IsEnum) t = Enum.GetUnderlyingType(t);
            if (t == typeof(byte) || t == typeof(sbyte)
                || t == typeof(bool)) return 1;
            if (t == typeof(short) || t == typeof(ushort)) return 2;
            if (t == typeof(int) || t == typeof(uint)
                || t == typeof(float)) return 4;
            if (t == typeof(long) || t == typeof(ulong)
                || t == typeof(double)) return 8;
            return 1;
        }

        private static int Align(int offset, int alignment)
            => (offset + alignment - 1) & ~(alignment - 1);

        // True when t is a "flat" tuple/record field — primitive,
        // string, or byte[] — that EmitInlineRecordOrTupleStore
        // can write at a per-field offset within the parent's
        // record layout.
        private static bool IsFlatField(Type t)
        {
            if (IsStorablePrimitive(t)) return true;
            if (t == typeof(string)) return true;
            if (t == typeof(byte[])) return true;
            return false;
        }

        // Resolver-aware overload: also accepts resource interfaces
        // (own&lt;R&gt; lowers to a single i32 handle, align 4) so the
        // caller can recognize tuples like (own&lt;descriptor&gt;, string)
        // — used by preopens.get-directories, http headers.entries,
        // and other "list of (resource, label)" shapes.
        private static bool IsFlatField(Type t,
            HostPackageResolver? resolver)
        {
            if (IsFlatField(t)) return true;
            if (resolver != null && resolver.IsResourceInterface(t)
                && resolver.PreferredResourcesType != null)
                return true;
            // Option<X> as a record/tuple field — wire form is u8
            // disc + value at Align(1, MaxAlignOf(X)). Inner X must
            // itself be a recognized aggregate. Lets records like
            // wasi:filesystem/types.descriptor-stat (with three
            // option<datetime> fields) flow through
            // EmitOptionStoreAt at the field's base offset.
            if (t.IsGenericType
                && t.GetGenericTypeDefinition() == typeof(Option<>)
                && IsAggregateReturnSupported(t, resolver))
                return true;
            return false;
        }

        // Alignment for a flat field. string/byte[] = 4 (i32 ptr).
        private static int AlignOfFlatField(Type t)
        {
            if (IsStorablePrimitive(t)) return AlignOfPrimitive(t);
            // Both string/byte[] (ptr+len pair) and resource interfaces
            // (i32 handle) are i32-aligned. The flat-field tier doesn't
            // distinguish resource interfaces here because their wire
            // alignment matches the (ptr-pair) default; the per-field
            // store dispatches on type to pick the right encoding.
            return 4;
        }

        // Resolver-aware overload. Option<X> aligns to MaxAlignOf(X)
        // — Option<datetime> aligns to 8 because Datetime's u64 field
        // does. Other shapes match the non-resolver overload.
        private static int AlignOfFlatField(Type t,
            HostPackageResolver? resolver)
        {
            if (IsStorablePrimitive(t)) return AlignOfPrimitive(t);
            if (t.IsGenericType
                && t.GetGenericTypeDefinition() == typeof(Option<>))
                return MaxAlignOf(t, resolver);
            return 4;
        }

        // Size for a flat field. string/byte[] = 8 (ptr + len);
        // resource interface = 4 (i32 handle); Option<X> dispatches
        // to the recursive SizeOf for the full disc + value layout.
        private static int SizeOfFlatField(Type t,
            HostPackageResolver? resolver = null)
        {
            if (IsStorablePrimitive(t)) return SizeOfPrimitive(t);
            if (resolver != null && resolver.IsResourceInterface(t))
                return 4;
            if (t.IsGenericType
                && t.GetGenericTypeDefinition() == typeof(Option<>))
                return SizeOf(t, resolver);
            return 8;  // string / byte[] (ptr + len pair)
        }

        // Cache the per-primitive-type StoreXxx MethodInfo so each
        // emit reuses the lookup.
        private static readonly ConcurrentDictionary<Type, MethodInfo>
            StoreMethodCache = new();
        private static readonly ConcurrentDictionary<Type, MethodInfo>
            LoadMethodCache = new();

        // Per-primitive-type ReadXxxLE MethodInfo on PrimitiveStore.
        // Used by EmitInlineRecordOrTupleLift (gap 22, round-17
        // follow-up) to read each tuple field at its canon-ABI
        // offset within the per-element area on the PARAM lift
        // side. Symmetric with ResolveStoreMethod.
        private static MethodInfo ResolveLoadMethod(Type clrType)
            => LoadMethodCache.GetOrAdd(clrType, t =>
            {
                var underlying = t.IsEnum
                    ? Enum.GetUnderlyingType(t) : t;
                string name;
                if (underlying == typeof(byte)) name = "ReadU8";
                else if (underlying == typeof(sbyte)) name = "ReadI8";
                else if (underlying == typeof(short)) name = "ReadI16LE";
                else if (underlying == typeof(ushort)) name = "ReadU16LE";
                else if (underlying == typeof(int)) name = "ReadI32LE";
                else if (underlying == typeof(uint)) name = "ReadU32LE";
                else if (underlying == typeof(long)) name = "ReadI64LE";
                else if (underlying == typeof(ulong)) name = "ReadU64LE";
                else if (underlying == typeof(float)) name = "ReadF32LE";
                else if (underlying == typeof(double)) name = "ReadF64LE";
                else if (underlying == typeof(bool)) name = "ReadBool";
                else throw new InvalidOperationException(
                    "Direct-linked emit fell through to a primitive load "
                    + "for an unsupported type: " + t.FullName + ". The "
                    + "PARAM-side lift only handles canon-ABI-storable "
                    + "primitives within tuple fields.");
                return typeof(PrimitiveStore).GetMethod(name,
                    BindingFlags.Public | BindingFlags.Static)!;
            });

        private static MethodInfo ResolveStoreMethod(Type clrType)
            => StoreMethodCache.GetOrAdd(clrType, t =>
            {
                var underlying = t.IsEnum
                    ? Enum.GetUnderlyingType(t) : t;
                string name;
                if (underlying == typeof(byte)) name = "StoreU8";
                else if (underlying == typeof(sbyte)) name = "StoreI8";
                else if (underlying == typeof(short)) name = "StoreI16";
                else if (underlying == typeof(ushort)) name = "StoreU16";
                else if (underlying == typeof(int)) name = "StoreI32";
                else if (underlying == typeof(uint)) name = "StoreU32";
                else if (underlying == typeof(long)) name = "StoreI64";
                else if (underlying == typeof(ulong)) name = "StoreU64";
                else if (underlying == typeof(float)) name = "StoreF32";
                else if (underlying == typeof(double)) name = "StoreF64";
                else if (underlying == typeof(bool)) name = "StoreBool";
                else throw new InvalidOperationException(
                    "Direct-linked emit fell through to a primitive store "
                    + "for an unsupported type: " + t.FullName + ". This "
                    + "usually means a Result-arm / variant-case / "
                    + "Option-inner shape was recognized as storable but "
                    + "the per-shape branch in EmitResultArmStore / "
                    + "EmitVariantStoreAt / EmitOptionStoreAt didn't fire. "
                    + "Either the IsAggregateReturnSupported recognition "
                    + "is over-eager for this shape, or a per-shape "
                    + "branch is missing. Falling back to delegate "
                    + "dispatch by removing the matching IsResultArmStorable "
                    + "branch is the safe workaround.");
                return typeof(PrimitiveStore).GetMethod(name,
                    BindingFlags.Public | BindingFlags.Static)!;
            });

        // Per-CLR-type wasm flat-slot count for canonical-ABI lower:
        //   primitive (compat with i32/i64/f32/f64) → 1
        //   string                                  → 2 (ptr, len)
        //   byte[]  (list<u8>)                      → 2 (ptr, len)
        //   own<R>/borrow<R> (resource interface)   → 1 (i32 handle)
        // Returns -1 when the CLR type isn't supported by the v0
        // direct-linked emit. Out-param `wasmTypes` is the
        // per-slot wire type sequence the wasm side must provide.
        private static int CanonicalSlotCount(Type clrType,
            out ValType[] wasmTypes,
            HostPackageResolver? resolver = null)
        {
            // Resource interface as CLR param → 1 i32 (handle).
            // Checked first so it doesn't fall through to the
            // primitive-or-unsupported tail.
            if (resolver != null
                && resolver.IsResourceInterface(clrType))
            {
                wasmTypes = new[] { ValType.I32 };
                return 1;
            }
            // Enum (incl. [Flags]) lowers as its underlying integral
            // type — same wire as a plain int. The CLR enum value
            // shares the i32 stack form with the primitive, so the
            // existing primitive path handles it once we recurse.
            if (clrType.IsEnum)
            {
                return CanonicalSlotCount(
                    Enum.GetUnderlyingType(clrType),
                    out wasmTypes, resolver);
            }
            if (clrType == typeof(string))
            {
                wasmTypes = new[] { ValType.I32, ValType.I32 };
                return 2;
            }
            if (clrType.IsArray
                && IsSupportedPrimitiveArrayElement(
                    clrType.GetElementType()!))
            {
                wasmTypes = new[] { ValType.I32, ValType.I32 };
                return 2;
            }
            // string[] — same wire shape (ptr, count) but the
            // per-element memory layout is (ptr, len) pairs that
            // StringMarshal/ListMarshal.LiftStringList handles.
            if (clrType == typeof(string[]))
            {
                wasmTypes = new[] { ValType.I32, ValType.I32 };
                return 2;
            }
            // byte[][] — `list<list<u8>>` outer header (i32 ptr,
            // i32 count); each element at outer_ptr+i*8 is a
            // (inner_ptr, inner_len) pair. Lifted via
            // ListMarshal.LiftByteArrayList. Closes the wasi-nn
            // SLM `wasi:nn/graph-funcs.load(builders, ...)` PARAM
            // direct-link gap (gap 19).
            if (clrType == typeof(byte[][]))
            {
                wasmTypes = new[] { ValType.I32, ValType.I32 };
                return 2;
            }
            // (T1, T2, ..., Tn)[] — `list<tuple-of-flat-fields>`
            // PARAM. Outer (i32 ptr, i32 count) + per-element area.
            // Recurse into element-type validation (resource +
            // string + primitive fields supported via the lift
            // emit; non-flat fields fall through). Closes the
            // wasi-nn SLM `inference.compute(inputs:
            // list<tuple<string, own<tensor>>>)` PARAM direct-link
            // gap (gap 22 follow-up).
            if (clrType.IsArray
                && clrType.GetArrayRank() == 1
                && (IsTupleOfPrimitives(clrType.GetElementType()!)
                    || IsTupleOfFlatFields(
                        clrType.GetElementType()!, resolver)))
            {
                wasmTypes = new[] { ValType.I32, ValType.I32 };
                return 2;
            }
            // IResource[] — `list<borrow<R>>` / `list<own<R>>` PARAM.
            // Outer (i32 ptr, i32 count) + per-element 4-byte handles.
            // wasi-gfx's `wasi:io/poll.poll(list<borrow<pollable>>)`
            // is the canonical example. Element is a resource
            // interface — recurse into IsResourceInterface, then
            // emit a loop that lifts each handle via
            // resources.GetResource(typeof(R), handle).
            if (clrType.IsArray
                && clrType.GetArrayRank() == 1
                && resolver != null
                && resolver.IsResourceInterface(
                    clrType.GetElementType()!))
            {
                wasmTypes = new[] { ValType.I32, ValType.I32 };
                return 2;
            }
            // Option<T>: 1 disc i32 + N value slots (whatever T's
            // canonical-ABI flat-form lowers to). Per spec.
            if (clrType.IsGenericType
                && clrType.GetGenericTypeDefinition() == typeof(Option<>))
            {
                var inner = clrType.GetGenericArguments()[0];
                int innerSlots = CanonicalSlotCount(inner,
                    out var innerWasm, resolver);
                if (innerSlots > 0)
                {
                    var combined = new ValType[1 + innerSlots];
                    combined[0] = ValType.I32;
                    Array.Copy(innerWasm, 0, combined, 1, innerSlots);
                    wasmTypes = combined;
                    return 1 + innerSlots;
                }
            }
            // ValueTuple<T1, T2, ...>: concatenated flat-slot
            // sequences of each element. Per canon-ABI tuple lower.
            // Supports the open-generic ValueTuple<,>...<,,,,,,,>
            // surface (System provides up to 7-element + TRest).
            if (clrType.IsGenericType
                && IsValueTupleType(clrType.GetGenericTypeDefinition()))
            {
                var elements = clrType.GetGenericArguments();
                var combined = new System.Collections.Generic.List<ValType>();
                int total = 0;
                bool ok = true;
                foreach (var e in elements)
                {
                    int es = CanonicalSlotCount(e, out var ew, resolver);
                    if (es <= 0) { ok = false; break; }
                    total += es;
                    combined.AddRange(ew);
                }
                if (ok)
                {
                    wasmTypes = combined.ToArray();
                    return total;
                }
            }
            // Variant: 1 disc i32 + max(case-payload-flat) joined-
            // flat slots. v0 handles variants whose payload cases
            // all share the same flat-slot shape (e.g. HttpMethod
            // / HttpScheme: 9 empty + 1 string-payload). Wire =
            // (i32 disc, then the max-payload's wire slots).
            if (IsLikelyVariantBase(clrType))
            {
                var cases = GetVariantCases(clrType);
                int maxPayloadSlots = 0;
                ValType[]? maxPayloadWire = null;
                foreach (var c in cases)
                {
                    if (c.Payload == null) continue;
                    int ps = CanonicalSlotCount(c.Payload,
                        out var pw, resolver);
                    if (ps < 0) { maxPayloadSlots = -1; break; }
                    if (ps > maxPayloadSlots)
                    {
                        maxPayloadSlots = ps;
                        maxPayloadWire = pw;
                    }
                    else if (ps == maxPayloadSlots
                        && maxPayloadWire != null)
                    {
                        // All max-sized payloads must share the
                        // same wire-type sequence — joined-flat
                        // widening across mismatched primitive
                        // shapes (i32 ↔ f32) is non-trivial; defer.
                        for (int i = 0; i < ps; i++)
                            if (pw[i] != maxPayloadWire[i])
                            { maxPayloadSlots = -1; break; }
                        if (maxPayloadSlots == -1) break;
                    }
                }
                if (maxPayloadSlots >= 0)
                {
                    if (maxPayloadSlots == 0)
                    {
                        wasmTypes = new[] { ValType.I32 };
                        return 1;
                    }
                    var combined = new ValType[1 + maxPayloadSlots];
                    combined[0] = ValType.I32;
                    Array.Copy(maxPayloadWire!, 0, combined, 1,
                        maxPayloadSlots);
                    wasmTypes = combined;
                    return 1 + maxPayloadSlots;
                }
            }
            // User-class record: declaration-order concatenation of
            // each public auto-property's flat-slot count. Detected
            // by the IsLikelyRecordType heuristic (sealed class with
            // a public parameterless ctor + ≥1 public {get;set;} —
            // matches what WitHostInterfaceGenerator emits for WIT
            // record types, plus user-defined records following the
            // same convention).
            if (IsLikelyRecordType(clrType))
            {
                var props = GetRecordProperties(clrType);
                var combined = new System.Collections.Generic.List<ValType>();
                int total = 0;
                bool ok = true;
                foreach (var p in props)
                {
                    int ps = CanonicalSlotCount(p.PropertyType,
                        out var pw, resolver);
                    if (ps <= 0) { ok = false; break; }
                    total += ps;
                    combined.AddRange(pw);
                }
                if (ok && total > 0)
                {
                    wasmTypes = combined.ToArray();
                    return total;
                }
            }
            // Result<TOk, TErr>: 1 disc i32 + max(flat(Ok), flat(Err))
            // joined-flat value slots. v0 only handles the case where
            // Ok and Err have the same flat-slot count AND the same
            // wire-type sequence — matches the WASI common pattern of
            // result<T, error-code> where both sides are 1×i32.
            // Mismatched widths require canon-ABI joined-flat with
            // per-slot widening which is non-trivial to emit; defer.
            if (clrType.IsGenericType
                && clrType.GetGenericTypeDefinition() == typeof(Result<,>))
            {
                var args = clrType.GetGenericArguments();
                int okSlots = CanonicalSlotCount(args[0],
                    out var okWasm, resolver);
                int errSlots = CanonicalSlotCount(args[1],
                    out var errWasm, resolver);
                if (okSlots > 0 && errSlots > 0
                    && okSlots == errSlots)
                {
                    bool sameShape = true;
                    for (int s = 0; s < okSlots; s++)
                        if (okWasm[s] != errWasm[s]) { sameShape = false; break; }
                    if (sameShape)
                    {
                        var combined = new ValType[1 + okSlots];
                        combined[0] = ValType.I32;
                        Array.Copy(okWasm, 0, combined, 1, okSlots);
                        wasmTypes = combined;
                        return 1 + okSlots;
                    }
                }
            }
            if (clrType == typeof(int) || clrType == typeof(uint)
                || clrType == typeof(bool)
                || clrType == typeof(byte) || clrType == typeof(sbyte)
                || clrType == typeof(short) || clrType == typeof(ushort))
            {
                wasmTypes = new[] { ValType.I32 };
                return 1;
            }
            if (clrType == typeof(long) || clrType == typeof(ulong))
            {
                wasmTypes = new[] { ValType.I64 };
                return 1;
            }
            if (clrType == typeof(float))
            {
                wasmTypes = new[] { ValType.F32 };
                return 1;
            }
            if (clrType == typeof(double))
            {
                wasmTypes = new[] { ValType.F64 };
                return 1;
            }
            wasmTypes = Array.Empty<ValType>();
            return -1;
        }

        // The host package's resources class must expose a public
        // `object GetResource(System.Type resourceInterface, int handle)`
        // method. Lookup throws at IL-emit time if the convention
        // isn't met — better to fail fast in the transpiler than
        // emit IL that crashes at first call.
        private static MethodInfo ResolveGetResourceMethod(Type resourcesType)
        {
            var m = resourcesType.GetMethod("GetResource",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(Type), typeof(int) },
                modifiers: null);
            if (m == null || m.ReturnType != typeof(object))
                throw new InvalidOperationException(
                    "Host-package resources type "
                    + resourcesType.FullName
                    + " must expose `object GetResource(System.Type, int)`.");
            return m;
        }

        // For [constructor]X bindings: the resources class must
        // expose `int AllocateResource(System.Type, object)` —
        // mints a handle for a newly-constructed instance and
        // returns it as the wasm i32 result.
        private static MethodInfo ResolveAllocateResourceMethod(Type resourcesType)
        {
            var m = resourcesType.GetMethod("AllocateResource",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(Type), typeof(object) },
                modifiers: null);
            if (m == null || m.ReturnType != typeof(int))
                throw new InvalidOperationException(
                    "Host-package resources type "
                    + resourcesType.FullName
                    + " must expose `int AllocateResource(System.Type, object)`.");
            return m;
        }

        private static PropertyInfo ResolveBundleProperty(Type bundleType,
            Type interfaceType)
        {
            // Convention (matches WasiPreview2Bundle): one public
            // get-only property per typed interface, named with the
            // interface name minus the leading "I". E.g. IRandom →
            // Random. If a future bundle ships a different naming
            // convention, we can extend this lookup with attribute-
            // based discovery.
            string propName = StripInterfacePrefix(interfaceType.Name);
            var prop = bundleType.GetProperty(propName,
                BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || prop.PropertyType != interfaceType)
            {
                // Fall back: scan all public instance properties for
                // one whose type matches.
                foreach (var p in bundleType.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.PropertyType == interfaceType) return p;
                }
                throw new InvalidOperationException(
                    "Bundle " + bundleType.FullName + " has no public "
                    + "property exposing " + interfaceType.FullName
                    + " (expected '" + propName + "').");
            }
            return prop;
        }

        private static string StripInterfacePrefix(string name)
        {
            if (name.Length > 1 && name[0] == 'I'
                && char.IsUpper(name[1]))
                return name.Substring(1);
            return name;
        }

        // True when the C# parameter / return type can host a wasm
        // wire-form value with at most a narrow CIL conversion. The
        // primitive table:
        //   i32  ↔ int / uint / bool / byte / sbyte / short / ushort
        //   i64  ↔ long / ulong
        //   f32  ↔ float
        //   f64  ↔ double
        private static bool IsPrimitiveCompatible(Type clrType,
            ValType wasmType)
        {
            if (clrType.IsEnum)
                clrType = Enum.GetUnderlyingType(clrType);
            switch (wasmType)
            {
                case ValType.I32:
                    return clrType == typeof(int)
                        || clrType == typeof(uint)
                        || clrType == typeof(bool)
                        || clrType == typeof(byte)
                        || clrType == typeof(sbyte)
                        || clrType == typeof(short)
                        || clrType == typeof(ushort);
                case ValType.I64:
                    return clrType == typeof(long)
                        || clrType == typeof(ulong);
                case ValType.F32:
                    return clrType == typeof(float);
                case ValType.F64:
                    return clrType == typeof(double);
                default:
                    return false;
            }
        }

        // The CIL stack type for a wasm primitive — used when
        // declaring spill locals so re-pushed values don't drift type.
        private static Type WasmStackType(ValType t)
        {
            return t switch
            {
                ValType.I32 => typeof(int),
                ValType.I64 => typeof(long),
                ValType.F32 => typeof(float),
                ValType.F64 => typeof(double),
                _ => typeof(int),
            };
        }

        // Narrow / sign-adjust a wasm wire value before calling a
        // typed interface that uses a different CLR primitive. The
        // CIL stack uses i32 for everything narrower-than-i32, so a
        // wasm i32 reaching a C# byte param needs `conv.u1`.
        // Enum CLR types are treated as their underlying type (CLR
        // enums share stack representation with their underlying
        // primitive — the typed callvirt accepts the integer
        // directly without needing a Box / cast).
        private static void EmitConversionIfNeeded(ILGenerator il,
            ValType wasmType, Type clrType)
        {
            if (clrType.IsEnum)
                clrType = Enum.GetUnderlyingType(clrType);
            if (wasmType == ValType.I32)
            {
                if (clrType == typeof(byte)) il.Emit(OpCodes.Conv_U1);
                else if (clrType == typeof(sbyte)) il.Emit(OpCodes.Conv_I1);
                else if (clrType == typeof(short)) il.Emit(OpCodes.Conv_I2);
                else if (clrType == typeof(ushort)) il.Emit(OpCodes.Conv_U2);
                // bool / int / uint share the i32 stack form — no op.
            }
            // I64 / F32 / F64 — no narrowing needed for the
            // primitive-compatible matches IsPrimitiveCompatible
            // accepts (long↔ulong, float, double all stack-compatible).
        }

        private static void EmitReturnConversionIfNeeded(ILGenerator il,
            Type clrReturn, ValType wasmReturn)
        {
            if (wasmReturn == ValType.I32)
            {
                // Narrow C# return promotes to i32 on the CIL stack
                // automatically; bool slot might be 1 byte, force
                // widen for consistency with wasm i32 expectations.
                if (clrReturn == typeof(bool))
                    il.Emit(OpCodes.Conv_I4);
                // Other narrow returns (byte/sbyte/short/ushort) are
                // already i32 on the stack — no op.
            }
        }
    }
}
