// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Emit a <c>public static class ComponentExports</c> into
    /// the transpiled assembly carrying one method per
    /// component-level export. Each method is typed per the
    /// component's own type-section signature (u32 vs i32, char
    /// vs uint, …) and delegates to the underlying core-module
    /// export via the generated <c>Module</c> class.
    ///
    /// <para><b>v0 scope:</b> primitive-only signatures. Canonical
    /// ABI is trivial in this slice — a <c>(Tcomponent)coreResult</c>
    /// cast and a <c>(Tcore)argument</c> cast at each boundary.
    /// Aggregate marshaling (strings, lists, options, …) plugs
    /// in once the canonical-ABI IL emitter (task #296) lands.</para>
    ///
    /// <para>Only attempts emission for <see cref="ComponentExportEntry"/>
    /// whose sort is <see cref="ComponentSort.Func"/> and whose
    /// lift's type resolves to a <see cref="ComponentFuncType"/>
    /// with primitive params/results. Non-emittable exports
    /// are silently skipped — the class still emits if any
    /// export qualifies.</para>
    /// </summary>
    public static class ComponentExportsEmit
    {
        /// <summary>
        /// Emit the <c>ComponentExports</c> class into
        /// <paramref name="module"/>. Returns the finished
        /// <see cref="Type"/> so the caller can cache or
        /// reflect on it; returns null if the component has no
        /// emittable exports.
        /// </summary>
        public static Type? EmitComponentExportsClass(
            ModuleBuilder module,
            string @namespace,
            ComponentModule component,
            Type coreIExports,
            Type coreModuleClass)
        {
            var emittable = FindEmittableExports(component);
            if (emittable.Count == 0) return null;

            var typeBuilder = module.DefineType(
                @namespace + ".ComponentExports",
                TypeAttributes.Public | TypeAttributes.Abstract
                    | TypeAttributes.Sealed);

            // Cached default Module instance — every export
            // call reuses it. Imports-less components only.
            // Imports-having components need a factory method;
            // left for the multi-module / composition pass.
            var instanceField = typeBuilder.DefineField(
                "_instance",
                coreModuleClass,
                FieldAttributes.Private | FieldAttributes.Static
                    | FieldAttributes.InitOnly);

            var cctor = typeBuilder.DefineTypeInitializer();
            var cctorIl = cctor.GetILGenerator();
            var ctor = coreModuleClass.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
                return null;   // imports-required — defer
            cctorIl.Emit(OpCodes.Newobj, ctor);
            cctorIl.Emit(OpCodes.Stsfld, instanceField);
            cctorIl.Emit(OpCodes.Ret);

            foreach (var slot in emittable)
            {
                EmitExportMethod(typeBuilder, instanceField,
                                 coreIExports, slot);
            }

            return typeBuilder.CreateType();
        }

        /// <summary>
        /// One export's mapping: name + component-level signature
        /// + the core function index (into the core module's
        /// function table) the canon lift resolves to. Populated
        /// by joining Exports × Canons × Types.
        /// </summary>
        private sealed class EmittableExport
        {
            public string Name;
            public ComponentFuncType Signature;
            public uint CoreFuncIdx;

            public EmittableExport(string name, ComponentFuncType sig,
                                   uint coreFuncIdx)
            {
                Name = name;
                Signature = sig;
                CoreFuncIdx = coreFuncIdx;
            }
        }

        private static List<EmittableExport> FindEmittableExports(
            ComponentModule component)
        {
            var list = new List<EmittableExport>();
            var canons = component.Canons;
            var types = component.Types;
            foreach (var export in component.Exports)
            {
                if (export.Sort != ComponentSort.Func) continue;
                if (export.Index >= canons.Count) continue;
                if (!(canons[(int)export.Index] is CanonLift lift)) continue;
                if (lift.TypeIdx >= types.Count) continue;
                if (!(types[(int)lift.TypeIdx] is ComponentFuncType fn))
                    continue;
                if (!AllPrimitive(fn)) continue;
                list.Add(new EmittableExport(export.Name, fn,
                                             lift.CoreFuncIdx));
            }
            return list;
        }

        private static bool AllPrimitive(ComponentFuncType fn)
        {
            foreach (var p in fn.Params)
                if (!p.Type.IsPrimitive) return false;
            foreach (var r in fn.Results)
                if (!r.IsPrimitive) return false;
            return true;
        }

        private static void EmitExportMethod(
            TypeBuilder typeBuilder,
            FieldBuilder instanceField,
            Type coreIExports,
            EmittableExport export)
        {
            var coreMethod = FindCoreMethod(coreIExports, export.Name);
            if (coreMethod == null) return;

            // C#-side types per the COMPONENT signature.
            var paramTypes = new Type[export.Signature.Params.Count];
            for (int i = 0; i < paramTypes.Length; i++)
                paramTypes[i] = PrimToCs(export.Signature.Params[i].Type.Prim);
            var returnType = export.Signature.Results.Count == 0
                ? typeof(void)
                : PrimToCs(export.Signature.Results[0].Prim);

            var methodName = PascalCase(export.Name);
            var method = typeBuilder.DefineMethod(
                methodName,
                MethodAttributes.Public | MethodAttributes.Static,
                returnType, paramTypes);
            for (int i = 0; i < export.Signature.Params.Count; i++)
                method.DefineParameter(i + 1, ParameterAttributes.None,
                                       CamelCase(export.Signature.Params[i].Name));

            var il = method.GetILGenerator();
            // Load the cached instance for the interface-method call.
            il.Emit(OpCodes.Ldsfld, instanceField);
            // Load each arg, applying the canonical-ABI param cast.
            for (int i = 0; i < paramTypes.Length; i++)
            {
                il.Emit(OpCodes.Ldarg, i);
                EmitParamCast(il, export.Signature.Params[i].Type.Prim,
                              coreMethod.GetParameters()[i].ParameterType);
            }
            il.EmitCall(OpCodes.Callvirt, coreMethod, null);
            // Apply the canonical-ABI return cast (if non-void).
            if (returnType != typeof(void))
            {
                EmitReturnCast(il, coreMethod.ReturnType,
                               export.Signature.Results[0].Prim);
            }
            il.Emit(OpCodes.Ret);
        }

        /// <summary>Case-insensitive search for the core-level
        /// export — wit-bindgen and the existing transpiler use
        /// varying casings (kebab / camelCase / PascalCase).</summary>
        private static MethodInfo? FindCoreMethod(Type iExports, string exportName)
        {
            foreach (var m in iExports.GetMethods())
            {
                if (string.Equals(m.Name, exportName,
                        StringComparison.OrdinalIgnoreCase))
                    return m;
                if (string.Equals(m.Name, PascalCase(exportName),
                        StringComparison.Ordinal))
                    return m;
                if (string.Equals(m.Name, CamelCase(exportName),
                        StringComparison.Ordinal))
                    return m;
            }
            return null;
        }

        /// <summary>C# type for a component primitive, matching
        /// wit-bindgen-csharp's mapping.</summary>
        private static Type PrimToCs(ComponentPrim p) => p switch
        {
            ComponentPrim.Bool   => typeof(bool),
            ComponentPrim.S8     => typeof(sbyte),
            ComponentPrim.U8     => typeof(byte),
            ComponentPrim.S16    => typeof(short),
            ComponentPrim.U16    => typeof(ushort),
            ComponentPrim.S32    => typeof(int),
            ComponentPrim.U32    => typeof(uint),
            ComponentPrim.S64    => typeof(long),
            ComponentPrim.U64    => typeof(ulong),
            ComponentPrim.F32    => typeof(float),
            ComponentPrim.F64    => typeof(double),
            ComponentPrim.Char   => typeof(uint),
            ComponentPrim.String => typeof(string),
            _ => throw new NotSupportedException(
                "PrimToCs for " + p + " is a follow-up."),
        };

        /// <summary>Narrow / re-sign a C# value of the component
        /// primitive type into the core's wire-type before the
        /// core call. Empty for bitwise-compatible cases (u32 ↔
        /// int32, etc. — same 32 bits, reinterpret is free).</summary>
        private static void EmitParamCast(ILGenerator il,
                                          ComponentPrim componentSide,
                                          Type coreWire)
        {
            // u32 / s32 / bool / char <-> int32: no IL needed;
            // the stack slot's content survives reinterpretation.
            // u64 / s64 <-> int64: same.
            // f32 / f64: no cast needed either.
            // For narrower types (s8 / u8 / s16 / u16), the core
            // wire type is still i32; widening is implicit in
            // C#'s stack.
            _ = componentSide;
            _ = coreWire;
        }

        /// <summary>Cast the core's return type into the
        /// component-side primitive type. For bitwise-equal
        /// shapes (u32 from int32, etc.) a no-op works at the IL
        /// stack level.</summary>
        private static void EmitReturnCast(ILGenerator il, Type coreRet,
                                           ComponentPrim componentPrim)
        {
            _ = coreRet;
            _ = componentPrim;
        }

        private static string PascalCase(string kebab)
        {
            if (string.IsNullOrEmpty(kebab)) return kebab;
            var sb = new System.Text.StringBuilder();
            bool upper = true;
            foreach (var c in kebab)
            {
                if (c == '-') { upper = true; continue; }
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            return sb.ToString();
        }

        private static string CamelCase(string kebab)
        {
            var p = PascalCase(kebab);
            if (string.IsNullOrEmpty(p)) return p;
            return char.ToLowerInvariant(p[0]) + p.Substring(1);
        }
    }
}
