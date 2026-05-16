// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Wacs.ComponentModel.Runtime;

// ComponentBridge is the cross-engine adapter: it wires an
// interpreted ComponentInstance to a transpiler-shaped typed
// surface via DispatchProxy + MakeGenericMethod. The runtime
// interface synthesis is fundamentally incompatible with AOT
// (NativeAOT / Unity IL2CPP). AOT-targeting consumers should use
// the build-time-transpiled typed harness — see
// docs/wit-harness-plan.md. The public entry points carry
// [RequiresDynamicCode] + [RequiresUnreferencedCode] for that
// reason; the interior reflection is suppressed file-wide here.
#pragma warning disable IL2060, IL2070, IL2111, IL3050

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>
    /// Cross-engine adapter at the component boundary. Exposes
    /// <see cref="ComponentInstance"/> as something a transpiled
    /// component's typed host bundle can consume, and exposes a
    /// transpiled component's typed exports as host functions an
    /// interpreted component can import.
    ///
    /// <para>The shared seam is <c>[WitSource]</c>-tagged C#
    /// interfaces — the same shape <c>wit-bindgen-csharp</c> and
    /// <c>ExportInterfaceEmit</c> both produce. This means
    /// composition is symmetric across engines: the typed surface
    /// is the contract, and either side can be interpreter-backed
    /// or transpile-backed.</para>
    /// </summary>
    public static class ComponentBridge
    {
        /// <summary>
        /// Build an instance of <typeparamref name="TInterface"/>
        /// (a <c>[WitSource]</c>-tagged interface) backed by an
        /// interpreted <see cref="ComponentInstance"/>. Each
        /// interface method's <c>[WitSource].Item</c> is used as
        /// the export name passed to
        /// <see cref="ComponentInstance.Invoke"/>; pass an
        /// <paramref name="exportNameMapper"/> to override (useful
        /// for nested-interface exports where the wasm-side path
        /// doesn't match the interface method name directly).
        /// </summary>
        [RequiresDynamicCode("Synthesizes a typed proxy of TInterface " +
            "via DispatchProxy.Create + MakeGenericMethod. AOT consumers " +
            "must use the build-time typed harness surface instead.")]
        [RequiresUnreferencedCode("Reflects over TInterface's methods + " +
            "their parameter / return types at runtime.")]
        public static TInterface AsTypedInterface<TInterface>(
            ComponentInstance instance,
            Func<MethodInfo, string>? exportNameMapper = null)
            where TInterface : class
        {
            return (TInterface)AsTypedInterface(typeof(TInterface),
                instance, exportNameMapper);
        }

        /// <summary>
        /// Non-generic counterpart — useful when the interface type
        /// is reflected at runtime (e.g. iterating bundle ctor params).
        /// </summary>
        [RequiresDynamicCode("Synthesizes a typed proxy of interfaceType " +
            "via DispatchProxy.Create + MakeGenericMethod. AOT consumers " +
            "must use the build-time typed harness surface instead.")]
        [RequiresUnreferencedCode("Reflects over the interface's methods " +
            "+ their parameter / return types at runtime.")]
        public static object AsTypedInterface(Type interfaceType,
            ComponentInstance instance,
            Func<MethodInfo, string>? exportNameMapper = null)
        {
            if (interfaceType == null) throw new ArgumentNullException(nameof(interfaceType));
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (!interfaceType.IsInterface)
                throw new ArgumentException(
                    "Type " + interfaceType.FullName + " is not an interface.",
                    nameof(interfaceType));

            // DispatchProxy.Create<TInterface, TProxy>() — invoke
            // generically through reflection so the interface type
            // can be supplied at runtime.
            var createMethod = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create"
                    && m.GetGenericArguments().Length == 2);
            var typed = createMethod.MakeGenericMethod(
                interfaceType, typeof(InterpretedInterfaceProxy));
            var proxy = typed.Invoke(null, null)!;
            ((InterpretedInterfaceProxy)proxy).Configure(
                instance, exportNameMapper);
            return proxy;
        }

        /// <summary>
        /// Build a host bundle (a class whose public read-only
        /// properties are <c>[WitSource]</c>-tagged interfaces)
        /// backed by an interpreted <see cref="ComponentInstance"/>.
        /// The bundle is what a transpiled component's generated
        /// Module ctor takes as its <c>object hostBundle</c>
        /// argument — so this closes the cross-engine loop:
        /// transpiled A consumes interpreted B without per-import
        /// glue.
        ///
        /// <para>The bundle type's constructor must take one
        /// parameter per interface property, in property-declaration
        /// order. <see cref="WasiPreview2Bundle"/>-style classes
        /// match this shape.</para>
        /// </summary>
        [RequiresDynamicCode("Reflects bundleType's constructor + " +
            "synthesizes proxies for each WitSource interface property. " +
            "AOT consumers must use the build-time typed harness " +
            "surface instead.")]
        [RequiresUnreferencedCode("Reflects over the bundle type's " +
            "constructor parameters at runtime.")]
        public static object AsHostBundle(ComponentInstance instance,
            Type bundleType,
            Func<MethodInfo, string>? exportNameMapper = null)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (bundleType == null) throw new ArgumentNullException(nameof(bundleType));

            // Reflect the bundle's ctor — its param types are the
            // interface set we need to proxy.
            var ctors = bundleType.GetConstructors();
            if (ctors.Length == 0)
                throw new ArgumentException(
                    "Bundle type " + bundleType.FullName
                    + " has no public constructor.", nameof(bundleType));
            // Pick the widest ctor — wit-bindgen / WasiPreview2Bundle
            // ship a single one-per-interface ctor, so that's the
            // shape we expect.
            var ctor = ctors.OrderByDescending(c =>
                c.GetParameters().Length).First();

            var ctorParams = ctor.GetParameters();
            var args = new object[ctorParams.Length];
            for (int i = 0; i < ctorParams.Length; i++)
            {
                var pType = ctorParams[i].ParameterType;
                if (!pType.IsInterface)
                    throw new InvalidOperationException(
                        "Bundle ctor param '" + ctorParams[i].Name
                        + "' is not an interface — bridge only supports "
                        + "interface-shaped bundle parameters.");
                args[i] = AsTypedInterface(pType, instance, exportNameMapper);
            }
            return ctor.Invoke(args);
        }

        /// <summary>
        /// Inverse of <see cref="AsHostBundle"/>: bind a transpiled
        /// component's typed exports as host functions on
        /// <paramref name="runtime"/> so an interpreted component
        /// can import them by their wit-qualified
        /// <c>(module, entity)</c> name.
        ///
        /// <para>Walks <paramref name="exportsInterface"/> for
        /// methods carrying <c>[WitSource]</c> attributes;
        /// computes <c>module = Package + "/" + Interface</c> and
        /// <c>entity = Item</c>, then binds a delegate that calls
        /// the typed method on
        /// <paramref name="exportsImplementation"/>.</para>
        /// </summary>
        public static int BindAsImports(
            Wacs.Core.Runtime.WasmRuntime runtime,
            object exportsImplementation,
            Type exportsInterface)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (exportsImplementation == null)
                throw new ArgumentNullException(nameof(exportsImplementation));
            if (exportsInterface == null) throw new ArgumentNullException(nameof(exportsInterface));

            var ifaceAttr = exportsInterface
                .GetCustomAttribute<WitSourceAttribute>();
            if (ifaceAttr == null)
                throw new ArgumentException(
                    "Interface " + exportsInterface.FullName
                    + " has no [WitSource] attribute — cannot derive "
                    + "(module, entity) names.", nameof(exportsInterface));
            string module = ifaceAttr.Package != null && ifaceAttr.Interface != null
                ? ifaceAttr.Package + "/" + ifaceAttr.Interface
                : (ifaceAttr.Interface ?? "");

            int bound = 0;
            foreach (var m in exportsInterface.GetMethods(
                BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.DeclaredOnly))
            {
                var ws = m.GetCustomAttribute<WitSourceAttribute>();
                if (ws == null) continue;
                var entity = ws.Item ?? KebabCase(m.Name);

                runtime.BindHostFunction(
                    (module, entity),
                    new TypedExportFunction(m, exportsImplementation,
                        module, entity));
                bound++;
            }
            return bound;
        }

        // ---- internals ---------------------------------------------

        // DispatchProxy implementation that routes each interface
        // method call to ComponentInstance.Invoke. Configured via
        // a separate Configure() call (DispatchProxy ctor is
        // restricted to parameterless). InvokeAsync would be a
        // follow-up — v0 is sync only.
        public class InterpretedInterfaceProxy : DispatchProxy
        {
            private ComponentInstance _instance = null!;
            private Func<MethodInfo, string>? _exportNameMapper;

            internal void Configure(ComponentInstance instance,
                Func<MethodInfo, string>? exportNameMapper)
            {
                _instance = instance;
                _exportNameMapper = exportNameMapper;
            }

            protected override object? Invoke(MethodInfo? targetMethod,
                object?[]? args)
            {
                if (targetMethod == null) return null;
                string exportName = ResolveExportName(targetMethod);
                var result = _instance.Invoke(exportName,
                    args ?? Array.Empty<object?>());
                return CoerceReturn(result, targetMethod.ReturnType);
            }

            private string ResolveExportName(MethodInfo m)
            {
                if (_exportNameMapper != null)
                    return _exportNameMapper(m);
                var ws = m.GetCustomAttribute<WitSourceAttribute>();
                if (ws?.Item != null) return ws.Item;
                return KebabCase(m.Name);
            }

            private static object? CoerceReturn(object? value, Type expected)
            {
                if (expected == typeof(void)) return null;
                if (value == null) return null;
                if (expected.IsInstanceOfType(value)) return value;
                // Numeric widening / signed-unsigned reshaping —
                // ComponentInstance.Invoke returns the canonical
                // .NET form (uint for u32, etc.). When the user
                // declared the interface with a different sign-
                // ness, convert.
                try
                {
                    return Convert.ChangeType(value, expected);
                }
                catch (Exception)
                {
                    return value;
                }
            }
        }

        // IFunctionInstance shape that adapts a typed export method
        // (on a transpiled component's IExports / ComponentExports)
        // to the interpreter's host-function dispatch.
        private sealed class TypedExportFunction
            : Wacs.Core.Runtime.Types.IFunctionInstance
        {
            private readonly MethodInfo _method;
            private readonly object _target;
            private readonly string _module;
            private readonly string _entity;

            public TypedExportFunction(MethodInfo method, object target,
                string module, string entity)
            {
                _method = method;
                _target = target;
                _module = module;
                _entity = entity;
                Type = BuildFunctionType(method);
            }

            public Wacs.Core.Types.FunctionType Type { get; }
            public string Id => _module + "." + _entity;
            public bool IsAsync => false;
            public string Name => _entity;
            public bool IsExport { get; set; }
            public void SetName(string name) { }

            public void Invoke(Wacs.Core.Runtime.ExecContext context)
            {
                var pars = _method.GetParameters();
                var args = new object?[pars.Length];
                // Pop in reverse order — interpreter pushes left-to-
                // right, top-of-stack is the rightmost arg.
                for (int i = pars.Length - 1; i >= 0; i--)
                    args[i] = ConvertFromValue(
                        context.OpStack.PopAny(), pars[i].ParameterType);

                var result = _method.Invoke(_target, args);

                if (_method.ReturnType != typeof(void) && result != null)
                    PushReturn(context.OpStack, result, _method.ReturnType);
            }

            // Build a coarse FunctionType from the C# method
            // signature — counts each param/return as one wire
            // slot. Aggregate types (string, list, etc.) actually
            // lower to multi-slot, but we never validate against
            // this Type at the interpreter dispatch site (the
            // delegate adapter inside the interpreter just calls
            // through). Keep simple.
            private static Wacs.Core.Types.FunctionType BuildFunctionType(
                MethodInfo m)
            {
                var paramTypes = m.GetParameters()
                    .Select(p => ClrToValType(p.ParameterType))
                    .ToArray();
                var resultTypes = m.ReturnType == typeof(void)
                    ? Array.Empty<Wacs.Core.Types.Defs.ValType>()
                    : new[] { ClrToValType(m.ReturnType) };
                return new Wacs.Core.Types.FunctionType(
                    new Wacs.Core.Types.ResultType(paramTypes),
                    new Wacs.Core.Types.ResultType(resultTypes));
            }

            private static Wacs.Core.Types.Defs.ValType ClrToValType(Type t)
            {
                if (t == typeof(int) || t == typeof(uint)
                    || t == typeof(short) || t == typeof(ushort)
                    || t == typeof(byte) || t == typeof(sbyte)
                    || t == typeof(bool))
                    return Wacs.Core.Types.Defs.ValType.I32;
                if (t == typeof(long) || t == typeof(ulong))
                    return Wacs.Core.Types.Defs.ValType.I64;
                if (t == typeof(float)) return Wacs.Core.Types.Defs.ValType.F32;
                if (t == typeof(double)) return Wacs.Core.Types.Defs.ValType.F64;
                // Aggregate types lower through the canon-ABI to
                // multiple i32 slots; the interpreter's import-side
                // resolver doesn't introspect Type for those, so
                // i32 is a safe placeholder.
                return Wacs.Core.Types.Defs.ValType.I32;
            }

            private static object? ConvertFromValue(
                Wacs.Core.Runtime.Value v, Type expected)
            {
                if (expected == typeof(int)) return v.Data.Int32;
                if (expected == typeof(uint)) return v.Data.UInt32;
                if (expected == typeof(long)) return v.Data.Int64;
                if (expected == typeof(ulong)) return v.Data.UInt64;
                if (expected == typeof(float)) return v.Data.Float32;
                if (expected == typeof(double)) return v.Data.Float64;
                if (expected == typeof(bool)) return v.Data.Int32 != 0;
                if (expected == typeof(short)) return (short)v.Data.Int32;
                if (expected == typeof(ushort)) return (ushort)v.Data.Int32;
                if (expected == typeof(byte)) return (byte)v.Data.Int32;
                if (expected == typeof(sbyte)) return (sbyte)v.Data.Int32;
                return v.Data.Int32;
            }

            private static void PushReturn(
                Wacs.Core.Runtime.OpStack stack, object boxed, Type t)
            {
                if (t == typeof(int)) stack.PushI32((int)boxed);
                else if (t == typeof(uint)) stack.PushU32((uint)boxed);
                else if (t == typeof(long)) stack.PushI64((long)boxed);
                else if (t == typeof(ulong)) stack.PushU64((ulong)boxed);
                else if (t == typeof(float)) stack.PushF32((float)boxed);
                else if (t == typeof(double)) stack.PushF64((double)boxed);
                else if (t == typeof(bool)) stack.PushI32(((bool)boxed) ? 1 : 0);
                else if (t == typeof(short)) stack.PushI32((short)boxed);
                else if (t == typeof(ushort)) stack.PushI32((ushort)boxed);
                else if (t == typeof(byte)) stack.PushI32((byte)boxed);
                else if (t == typeof(sbyte)) stack.PushI32((sbyte)boxed);
                else stack.PushI32(0);
            }
        }

        private static string KebabCase(string pascal)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (char.IsUpper(c) && i > 0)
                    sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
