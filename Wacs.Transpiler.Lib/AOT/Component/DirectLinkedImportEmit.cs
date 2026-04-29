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
            FunctionType wasmType)
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
                        if (!method.IsStatic) return false;
                        // Constructor: wasm returns exactly one i32
                        // (the handle). The CLR factory returns the
                        // instance; emit allocates the handle.
                        if (wasmResults.Length != 1
                            || wasmResults[0] != ValType.I32) return false;
                        if (method.ReturnType == typeof(void)) return false;
                        if (!binding.InterfaceType.IsAssignableFrom(
                            method.ReturnType)) return false;
                        break;
                    default:
                        return false;
                }
            }

            // Each CLR param contributes its canon-ABI flat-slot
            // count to the wasm-side. Sum and check against
            // wasmParams (after skipping the resource handle slot).
            int expectedRemainingWasm = 0;
            for (int i = 0; i < clrParams.Length; i++)
            {
                int slots = CanonicalSlotCount(clrParams[i].ParameterType,
                    out var perWasmType);
                if (slots < 0) return false;
                expectedRemainingWasm += slots;
                // Each contributing wasm slot must match the CLR
                // param's expected wire shape (i32 / i64 / etc.).
                for (int s = 0; s < slots; s++)
                {
                    int wIdx = wasmParamOffset + (expectedRemainingWasm - slots) + s;
                    if (wIdx >= wasmParams.Length) return false;
                    if (wasmParams[wIdx] != perWasmType[s]) return false;
                }
            }
            if (wasmParamOffset + expectedRemainingWasm
                != wasmParams.Length) return false;
            if (wasmResults.Length > 1) return false;

            // Constructor return: already validated as i32 (handle)
            // wire / interface CLR. Skip the primitive-compat check.
            bool isConstructor = binding.IsResourceMethod
                && binding.ResourceKind ==
                    HostPackageResolver.ResourceMethodKind.Constructor;
            if (!isConstructor)
            {
                if (wasmResults.Length == 1)
                {
                    if (method.ReturnType == typeof(void)) return false;
                    if (!IsPrimitiveCompatible(method.ReturnType,
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
            // v0 constructors are zero-arg. Multi-arg constructor
            // shapes (e.g. own<fields> param) ride incrementally.
            if (isConstructor && clrParams.Length != 0) return false;
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
            Type? resourcesType = null)
        {
            if (bundleType == null) throw new ArgumentNullException(
                nameof(bundleType));
            if (binding.IsResourceMethod && resourcesType == null)
                throw new ArgumentNullException(
                    nameof(resourcesType),
                    "resourcesType is required for resource-method bindings");

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

            // Push the `this` arg for the callvirt — only for
            // free-function or instance-method calls. Static and
            // constructor methods are static dispatch.
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
            int wasmCursor = wasmParamOffset;
            for (int i = 0; i < clrParamCount; i++)
            {
                var clrType = clrParams[i].ParameterType;
                int slots = CanonicalSlotCount(clrType, out _);
                EmitLiftForType(il, clrType, wasmParams, temps,
                    wasmCursor);
                wasmCursor += slots;
            }

            // Static and constructor methods use static dispatch.
            // Instance and free-function methods use callvirt
            // (free fns go through a typed-interface property).
            if (isStatic || isConstructor)
                il.Emit(OpCodes.Call, method);
            else
                il.Emit(OpCodes.Callvirt, method);

            if (isConstructor)
            {
                // Constructor's CLR factory just left the new
                // instance on the stack. Allocate a handle for it
                // via the resources class's
                // `int AllocateResource(Type, object)` convention,
                // then leave the handle as the wasm i32 return.
                //
                //   stack: [instance]
                // emit:  ldarg_0; ldfld Resources; castclass <Res>;
                //        ldtoken <IFace>; call typeof; <swap>;
                //        callvirt AllocateResource(Type, object) → int
                var allocate = ResolveAllocateResourceMethod(
                    resourcesType!);

                // Stash the instance, then build the call args in
                // order: resources, type, instance. Then callvirt.
                var instLocal = il.DeclareLocal(binding.InterfaceType);
                il.Emit(OpCodes.Stloc, instLocal);

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
                // Convert C# return type back to the wasm wire type
                // if the CIL stack form differs. Most narrow-int
                // returns are already i32 on the stack (CIL widens
                // to i32); ulong returns are already i64. So this
                // is usually a no-op. But e.g. C# bool returned
                // from an interface method is a 1-byte stack slot
                // in some scenarios — emit a conv.i4 defensively.
                var wasmRet = wasmType.ResultType.Types[0];
                EmitReturnConversionIfNeeded(il, method.ReturnType,
                    wasmRet);
            }
        }

        // ---- Lift dispatcher -----------------------------------------

        // Emit IL that consumes the wasm slots starting at
        // <paramref name="wasmCursor"/> and leaves the corresponding
        // CLR value on the stack. Recursive: Option<T> calls itself
        // for the inner T's value-slot lift.
        private static void EmitLiftForType(ILGenerator il,
            Type clrType, ValType[] wasmParams,
            LocalBuilder[] temps, int wasmCursor)
        {
            if (clrType == typeof(string))
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, MemoriesField);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldfld, MemoryDataField);
                il.Emit(OpCodes.Ldloc, temps[wasmCursor]);     // ptr
                il.Emit(OpCodes.Ldloc, temps[wasmCursor + 1]); // len
                il.Emit(OpCodes.Call, LiftUtf8Method);
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
                il.Emit(OpCodes.Ldfld, MemoryDataField);
                il.Emit(OpCodes.Ldloc, temps[wasmCursor]);     // ptr
                il.Emit(OpCodes.Ldloc, temps[wasmCursor + 1]); // len
                il.Emit(OpCodes.Call,
                    ResolveLiftPrimMethod(clrType.GetElementType()!));
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
                    wasmCursor + 1);
                il.Emit(OpCodes.Call, ResolveOptionSomeMethod(inner));
                il.Emit(OpCodes.Br, endLabel);
                il.MarkLabel(noneLabel);
                il.Emit(OpCodes.Call, ResolveOptionNoneGetter(inner));
                il.MarkLabel(endLabel);
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

        private static readonly FieldInfo MemoryDataField =
            typeof(MemoryInstance).GetField(
                nameof(MemoryInstance.Data))!;

        private static readonly MethodInfo LiftUtf8Method =
            typeof(StringMarshal).GetMethod(
                nameof(StringMarshal.LiftUtf8),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(byte[]), typeof(int), typeof(int) },
                modifiers: null)!;

        // ListMarshal.LiftPrim<T> is generic. Cache the per-T
        // instantiation so each emit reuses the same MethodInfo
        // and we never pay a MakeGenericMethod call after the
        // first per element type.
        private static readonly MethodInfo LiftPrimGenericMethod =
            typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.LiftPrim),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(byte[]), typeof(int), typeof(int) },
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

        // Per-CLR-type wasm flat-slot count for canonical-ABI lower:
        //   primitive (compat with i32/i64/f32/f64) → 1
        //   string                                  → 2 (ptr, len)
        //   byte[]  (list<u8>)                      → 2 (ptr, len)
        // Returns -1 when the CLR type isn't supported by the v0
        // direct-linked emit. Out-param `wasmTypes` is the
        // per-slot wire type sequence the wasm side must provide.
        private static int CanonicalSlotCount(Type clrType,
            out ValType[] wasmTypes)
        {
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
            // Option<T>: 1 disc i32 + N value slots (whatever T's
            // canonical-ABI flat-form lowers to). Per spec.
            if (clrType.IsGenericType
                && clrType.GetGenericTypeDefinition() == typeof(Option<>))
            {
                var inner = clrType.GetGenericArguments()[0];
                int innerSlots = CanonicalSlotCount(inner,
                    out var innerWasm);
                if (innerSlots > 0)
                {
                    var combined = new ValType[1 + innerSlots];
                    combined[0] = ValType.I32;
                    Array.Copy(innerWasm, 0, combined, 1, innerSlots);
                    wasmTypes = combined;
                    return 1 + innerSlots;
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
        private static void EmitConversionIfNeeded(ILGenerator il,
            ValType wasmType, Type clrType)
        {
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
