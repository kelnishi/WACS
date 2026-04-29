// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Runtime;
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
        /// </summary>
        public static bool CanEmitDirect(HostPackageResolver.Binding binding,
            FunctionType wasmType)
        {
            // Resource methods: deferred — they need
            // ResourceTable.Get/Allocate IL emission and the wire-prefix
            // shape inference. Free functions only in v0.
            if (binding.IsResourceMethod) return false;

            var method = binding.Method;
            var clrParams = method.GetParameters();
            var wasmParams = wasmType.ParameterTypes.Types;
            var wasmResults = wasmType.ResultType.Types;

            if (clrParams.Length != wasmParams.Length) return false;
            if (wasmResults.Length > 1) return false;

            for (int i = 0; i < clrParams.Length; i++)
            {
                if (!IsPrimitiveCompatible(clrParams[i].ParameterType,
                    wasmParams[i])) return false;
            }

            if (wasmResults.Length == 1)
            {
                if (method.ReturnType == typeof(void)) return false;
                if (!IsPrimitiveCompatible(method.ReturnType,
                    wasmResults[0])) return false;
            }
            else
            {
                // Wasm void return — the C# method must also return
                // void OR Unit (Unit is a struct; zero-size). v0 only
                // accepts plain void.
                if (method.ReturnType != typeof(void)) return false;
            }
            return true;
        }

        /// <summary>
        /// Emit the inline IL. Caller has already pushed wasm params
        /// onto the CIL stack in declaration order. Caller must also
        /// have already pushed <c>ThinContext ctx</c> as arg-0 of
        /// the enclosing static method (the standard transpiled
        /// function-method shape).
        /// </summary>
        public static void Emit(ILGenerator il,
            HostPackageResolver.Binding binding,
            FunctionType wasmType,
            Type bundleType)
        {
            if (bundleType == null) throw new ArgumentNullException(
                nameof(bundleType));

            var method = binding.Method;
            var clrParams = method.GetParameters();
            int paramCount = clrParams.Length;

            // Spill wasm params already on the CIL stack into locals.
            // Order on the stack is param0..paramN-1 (top of stack =
            // last param), so we pop in reverse.
            var temps = new LocalBuilder[paramCount];
            var wasmParams = wasmType.ParameterTypes.Types;
            for (int i = paramCount - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(WasmStackType(wasmParams[i]));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Push the typed interface as the `this` arg for callvirt.
            // ctx → HostBundle → cast → typed-interface property.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, HostBundleField);
            il.Emit(OpCodes.Castclass, bundleType);

            var bundleProperty = ResolveBundleProperty(bundleType,
                binding.InterfaceType);
            il.Emit(OpCodes.Callvirt, bundleProperty.GetGetMethod()!);

            // Re-push params from temps, applying any narrow CLR
            // conversion the typed-interface signature requires
            // (e.g. wasm i32 → C# byte: conv.u1).
            for (int i = 0; i < paramCount; i++)
            {
                il.Emit(OpCodes.Ldloc, temps[i]);
                EmitConversionIfNeeded(il, wasmParams[i],
                    clrParams[i].ParameterType);
            }

            il.Emit(OpCodes.Callvirt, method);

            // Convert C# return type back to the wasm wire type if the
            // CIL stack form differs. Most narrow-int returns are
            // already i32 on the stack (CIL widens to i32); ulong
            // returns are already i64. So this is usually a no-op.
            // But e.g. C# bool returned from an interface method is
            // a 1-byte stack slot in some scenarios — emit a conv.i4
            // defensively.
            if (wasmType.ResultType.Types.Length == 1)
            {
                var wasmRet = wasmType.ResultType.Types[0];
                EmitReturnConversionIfNeeded(il, method.ReturnType,
                    wasmRet);
            }
        }

        // ---- Internals ------------------------------------------------

        private static readonly FieldInfo HostBundleField =
            typeof(ThinContext).GetField(
                nameof(ThinContext.HostBundle))!;

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
