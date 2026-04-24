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
using Wacs.ComponentModel.CanonicalABI;
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
                                 coreIExports, slot, component.Types);
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
                if (!IsEmittable(fn, types)) continue;
                list.Add(new EmittableExport(export.Name, fn,
                                             lift.CoreFuncIdx));
            }
            return list;
        }

        /// <summary>Gate for whether the emitter can handle this
        /// function type. Primitive params (all kinds) + either a
        /// primitive return, a single-result string return, or a
        /// single-result list&lt;primitive&gt; return are in scope
        /// for v0. Other aggregate returns / aggregate params
        /// land as further canonical-ABI IL support arrives.</summary>
        private static bool IsEmittable(
            ComponentFuncType fn, IReadOnlyList<DefTypeEntry> types)
        {
            foreach (var p in fn.Params)
                if (!p.Type.IsPrimitive) return false;
            if (fn.Results.Count == 0) return true;
            if (fn.Results.Count != 1) return false;
            var r = fn.Results[0];
            if (r.IsPrimitive) return true;
            // Type-table ref — only list<primitive> for now.
            if (TryResolveListOfPrim(r, types, out _)) return true;
            return false;
        }

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to a list<primitive> in <paramref name="types"/>;
        /// when so, <paramref name="elemPrim"/> captures the
        /// primitive element kind.</summary>
        private static bool TryResolveListOfPrim(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types,
            out ComponentPrim elemPrim)
        {
            elemPrim = default;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentListType list)) return false;
            if (!list.Element.IsPrimitive) return false;
            elemPrim = list.Element.Prim;
            return true;
        }

        private static bool IsStringReturn(ComponentFuncType fn) =>
            fn.Results.Count == 1 && fn.Results[0].IsPrimitive
                && fn.Results[0].Prim == ComponentPrim.String;

        private static void EmitExportMethod(
            TypeBuilder typeBuilder,
            FieldBuilder instanceField,
            Type coreIExports,
            EmittableExport export,
            IReadOnlyList<DefTypeEntry> types)
        {
            var coreMethod = FindCoreMethod(coreIExports, export.Name);
            if (coreMethod == null) return;

            // C#-side types per the COMPONENT signature.
            var paramTypes = new Type[export.Signature.Params.Count];
            for (int i = 0; i < paramTypes.Length; i++)
                paramTypes[i] = PrimToCs(export.Signature.Params[i].Type.Prim);

            // Return-type dispatch: void / primitive (inline),
            // string (StringMarshal chokepoint), list<prim>
            // (ListMarshal chokepoint via a closed generic).
            bool isListReturn = false;
            ComponentPrim listElemPrim = default;
            Type returnType;
            if (export.Signature.Results.Count == 0)
            {
                returnType = typeof(void);
            }
            else if (export.Signature.Results[0].IsPrimitive)
            {
                returnType = PrimToCs(export.Signature.Results[0].Prim);
            }
            else if (TryResolveListOfPrim(
                export.Signature.Results[0], types, out listElemPrim))
            {
                isListReturn = true;
                returnType = PrimToCs(listElemPrim).MakeArrayType();
            }
            else
            {
                return;   // shouldn't reach — IsEmittable rejects
            }

            var methodName = PascalCase(export.Name);
            var method = typeBuilder.DefineMethod(
                methodName,
                MethodAttributes.Public | MethodAttributes.Static,
                returnType, paramTypes);
            for (int i = 0; i < export.Signature.Params.Count; i++)
                method.DefineParameter(i + 1, ParameterAttributes.None,
                                       CamelCase(export.Signature.Params[i].Name));

            var il = method.GetILGenerator();
            if (isListReturn)
            {
                EmitListReturnBody(il, instanceField, coreMethod, export,
                                   listElemPrim);
            }
            else if (IsStringReturn(export.Signature))
            {
                EmitStringReturnBody(il, instanceField, coreMethod, export);
            }
            else
            {
                EmitPrimitiveBody(il, instanceField, coreMethod, export,
                                  returnType);
            }
        }

        /// <summary>
        /// IL body for primitive-signature exports. Direct call
        /// into the core IExports method, with per-param +
        /// return-side canonical-ABI casts applied at the IL
        /// stack level (mostly no-op; narrow ints + bool get
        /// real conversion ops).
        /// </summary>
        private static void EmitPrimitiveBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            Type returnType)
        {
            var paramTypes = new Type[export.Signature.Params.Count];
            for (int i = 0; i < paramTypes.Length; i++)
                paramTypes[i] = PrimToCs(export.Signature.Params[i].Type.Prim);

            il.Emit(OpCodes.Ldsfld, instanceField);
            for (int i = 0; i < paramTypes.Length; i++)
            {
                il.Emit(OpCodes.Ldarg, i);
                EmitParamCast(il, export.Signature.Params[i].Type.Prim,
                              coreMethod.GetParameters()[i].ParameterType);
            }
            il.EmitCall(OpCodes.Callvirt, coreMethod, null);
            if (returnType != typeof(void))
            {
                EmitReturnCast(il, coreMethod.ReturnType,
                               export.Signature.Results[0].Prim);
            }
            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// IL body for a <c>() -&gt; string</c> export — the
        /// return-area lift path. Core function returns an i32
        /// pointer P into linear memory; the 8 bytes at P are
        /// (strPtr, strLen). Dispatch:
        /// <code>
        ///     int P = core.hello();
        ///     byte[] memory = instance.Memory;
        ///     int strPtr = BitConverter.ToInt32(memory, P);
        ///     int strLen = BitConverter.ToInt32(memory, P + 4);
        ///     return StringMarshal.LiftUtf8(memory, strPtr, strLen);
        /// </code>
        /// Param-taking string returns follow the same shape —
        /// load args before the core call. Not yet exercised;
        /// primitive-only params are the v0 gate.
        /// </summary>
        private static void EmitStringReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export)
        {
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "String-returning component requires the core module to "
                    + "declare a memory — Module.Memory accessor is missing.");

            var paramTypes = new Type[export.Signature.Params.Count];
            for (int i = 0; i < paramTypes.Length; i++)
                paramTypes[i] = PrimToCs(export.Signature.Params[i].Type.Prim);

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));

            // int P = core.hello(args...);
            il.Emit(OpCodes.Ldsfld, instanceField);
            for (int i = 0; i < paramTypes.Length; i++)
            {
                il.Emit(OpCodes.Ldarg, i);
                EmitParamCast(il, export.Signature.Params[i].Type.Prim,
                              coreMethod.GetParameters()[i].ParameterType);
            }
            il.EmitCall(OpCodes.Callvirt, coreMethod, null);
            il.Emit(OpCodes.Stloc, retAreaLocal);

            // byte[] memory = instance.Memory;
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.EmitCall(OpCodes.Callvirt, memoryProp.GetGetMethod()!, null);
            il.Emit(OpCodes.Stloc, memoryLocal);

            // StringMarshal.LiftUtf8(memory, strPtr, strLen)
            //   where strPtr = BitConverter.ToInt32(memory, P)
            //         strLen = BitConverter.ToInt32(memory, P+4)
            var bitCToInt32 = typeof(BitConverter).GetMethod(
                "ToInt32", new[] { typeof(byte[]), typeof(int) });
            var liftMethod = typeof(StringMarshal).GetMethod(
                nameof(StringMarshal.LiftUtf8),
                new[] { typeof(byte[]), typeof(int), typeof(int) });

            il.Emit(OpCodes.Ldloc, memoryLocal);            // source
            il.Emit(OpCodes.Ldloc, memoryLocal);            // strPtr arg: memory
            il.Emit(OpCodes.Ldloc, retAreaLocal);           //             P
            il.EmitCall(OpCodes.Call, bitCToInt32!, null);  // → strPtr
            il.Emit(OpCodes.Ldloc, memoryLocal);            // strLen arg: memory
            il.Emit(OpCodes.Ldloc, retAreaLocal);           //             P
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Add);                           //             P+4
            il.EmitCall(OpCodes.Call, bitCToInt32!, null);  // → strLen
            il.EmitCall(OpCodes.Call, liftMethod!, null);   // → string
            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// IL body for a <c>() -&gt; list&lt;prim&gt;</c> export.
        /// Structurally identical to the string-return path: core
        /// returns the retArea pointer P, then (ptr, count) live
        /// at memory[P..P+8]. The element type is a primitive; we
        /// resolve <see cref="ListMarshal.LiftPrim{T}"/> as a
        /// closed generic and call it with (memory, byteOffset,
        /// count) to pull a <c>T[]</c> back out of guest memory.
        /// </summary>
        private static void EmitListReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            ComponentPrim elemPrim)
        {
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "List-returning component requires the core module to "
                    + "declare a memory — Module.Memory accessor is missing.");

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));

            // int P = core.bytes();
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.EmitCall(OpCodes.Callvirt, coreMethod, null);
            il.Emit(OpCodes.Stloc, retAreaLocal);

            // byte[] memory = instance.Memory;
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.EmitCall(OpCodes.Callvirt, memoryProp.GetGetMethod()!, null);
            il.Emit(OpCodes.Stloc, memoryLocal);

            // var elemCs = PrimToCs(elemPrim);
            // return ListMarshal.LiftPrim<elemCs>(memory, dataPtr, count)
            //   where dataPtr = BitConverter.ToInt32(memory, P)
            //         count   = BitConverter.ToInt32(memory, P + 4)
            var elemType = PrimToCs(elemPrim);
            var bitCToInt32 = typeof(BitConverter).GetMethod(
                "ToInt32", new[] { typeof(byte[]), typeof(int) });
            var liftOpen = typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.LiftPrim),
                1,
                new[] { typeof(byte[]), typeof(int), typeof(int) })!;
            var liftClosed = liftOpen.MakeGenericMethod(elemType);

            il.Emit(OpCodes.Ldloc, memoryLocal);            // source
            il.Emit(OpCodes.Ldloc, memoryLocal);            // for BitConv 1
            il.Emit(OpCodes.Ldloc, retAreaLocal);           // P
            il.EmitCall(OpCodes.Call, bitCToInt32!, null);  // dataPtr
            il.Emit(OpCodes.Ldloc, memoryLocal);            // for BitConv 2
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Add);                           // P+4
            il.EmitCall(OpCodes.Call, bitCToInt32!, null);  // count
            il.EmitCall(OpCodes.Call, liftClosed, null);    // T[]
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
        /// int32, etc. — same 32 bits, reinterpret is free). The
        /// narrow types (bool, s8, u8, s16, u16) widen implicitly
        /// on the evaluation stack, so passing them to an i32
        /// parameter requires no IL either — C#'s stack already
        /// promotes.</summary>
        private static void EmitParamCast(ILGenerator il,
                                          ComponentPrim componentSide,
                                          Type coreWire)
        {
            if (componentSide == ComponentPrim.Bool)
            {
                // bool → i32: true = 1, false = 0. C# bool on
                // the stack is already 0/1 as a 32-bit int,
                // but guarantee explicit conversion to i32.
                il.Emit(OpCodes.Conv_I4);
            }
            // Other small-prim + wide-prim + float cases: the
            // C# type and the core wire type share the same IL
            // stack representation — no conversion needed.
        }

        /// <summary>Cast the core's return type into the
        /// component-side primitive type. For 32-bit types
        /// (u32 from int32, bool from i32, char from i32) the
        /// CLR stack treats them identically — no IL needed.
        /// For narrow types (s8 / u8 / s16 / u16) emit the
        /// corresponding Conv.* opcode so the caller sees the
        /// truncated-and-resigned value.</summary>
        private static void EmitReturnCast(ILGenerator il, Type coreRet,
                                           ComponentPrim componentPrim)
        {
            switch (componentPrim)
            {
                case ComponentPrim.S8:
                    il.Emit(OpCodes.Conv_I1);
                    break;
                case ComponentPrim.U8:
                    il.Emit(OpCodes.Conv_U1);
                    break;
                case ComponentPrim.S16:
                    il.Emit(OpCodes.Conv_I2);
                    break;
                case ComponentPrim.U16:
                    il.Emit(OpCodes.Conv_U2);
                    break;
                case ComponentPrim.Bool:
                    // i32 → bool: compare-to-zero + invert.
                    // `result != 0 ? true : false`.
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Cgt_Un);
                    break;
                // 32-bit + 64-bit + float: no conversion — the
                // stack slot's bit pattern already matches the
                // target. C#'s verifier accepts u32/i32 and
                // u64/i64 interchangeably on the stack.
            }
            _ = coreRet;
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
