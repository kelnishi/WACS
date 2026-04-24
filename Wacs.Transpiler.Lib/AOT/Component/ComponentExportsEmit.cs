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
            // Type-table ref — list<prim>, option<prim>,
            // result<prim>, tuple<prims>.
            if (TryResolveListOfPrim(r, types, out _)) return true;
            if (TryResolveOptionOfPrim(r, types, out _)) return true;
            if (TryResolveResultOfPrim(r, types, out _, out _)) return true;
            if (TryResolveTupleOfPrims(r, types, out _)) return true;
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

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to <c>option&lt;primitive&gt;</c>. Scoped to small
        /// primitives — wider-prim alignment + aggregate-inner
        /// payloads are follow-ups.</summary>
        private static bool TryResolveOptionOfPrim(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types,
            out ComponentPrim innerPrim)
        {
            innerPrim = default;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentOptionType opt)) return false;
            if (!opt.Inner.IsPrimitive) return false;
            innerPrim = opt.Inner.Prim;
            return true;
        }

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to <c>tuple&lt;prim, prim, …&gt;</c>. All-primitive
        /// elements for v0; aggregates inside tuples are
        /// follow-ups once the underlying adapter helpers handle
        /// them.</summary>
        private static bool TryResolveTupleOfPrims(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types,
            out ComponentPrim[] elementPrims)
        {
            elementPrims = Array.Empty<ComponentPrim>();
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentTupleType tup)) return false;
            var prims = new ComponentPrim[tup.Elements.Count];
            for (int i = 0; i < prims.Length; i++)
            {
                if (!tup.Elements[i].IsPrimitive) return false;
                prims[i] = tup.Elements[i].Prim;
            }
            elementPrims = prims;
            return true;
        }

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to <c>result&lt;prim-or-void, prim-or-void&gt;</c>.
        /// Scoped to small-primitive Ok/Err payloads for now.
        /// Aggregate payloads (string, list, variant) are
        /// incremental follow-ups.</summary>
        private static bool TryResolveResultOfPrim(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types,
            out ComponentPrim? okPrim, out ComponentPrim? errPrim)
        {
            okPrim = null;
            errPrim = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentResultType res)) return false;
            if (res.Ok != null)
            {
                if (!res.Ok.Value.IsPrimitive) return false;
                okPrim = res.Ok.Value.Prim;
            }
            if (res.Err != null)
            {
                if (!res.Err.Value.IsPrimitive) return false;
                errPrim = res.Err.Value.Prim;
            }
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
            // (ListMarshal closed generic), option<prim>
            // (disc switch + inline).
            bool isListReturn = false;
            bool isOptionReturn = false;
            bool isResultReturn = false;
            bool isTupleReturn = false;
            ComponentPrim listElemPrim = default;
            ComponentPrim optionInnerPrim = default;
            ComponentPrim? resultOkPrim = null;
            ComponentPrim? resultErrPrim = null;
            ComponentPrim[] tupleElemPrims = Array.Empty<ComponentPrim>();
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
            else if (TryResolveOptionOfPrim(
                export.Signature.Results[0], types, out optionInnerPrim))
            {
                isOptionReturn = true;
                // Nullable<T> at the C# surface — wit-bindgen's
                // mapping for option<primitive>.
                returnType = typeof(Nullable<>).MakeGenericType(
                    PrimToCs(optionInnerPrim));
            }
            else if (TryResolveTupleOfPrims(
                export.Signature.Results[0], types, out tupleElemPrims))
            {
                isTupleReturn = true;
                var csTypes = new Type[tupleElemPrims.Length];
                for (int i = 0; i < csTypes.Length; i++)
                    csTypes[i] = PrimToCs(tupleElemPrims[i]);
                returnType = MakeValueTuple(csTypes);
            }
            else if (TryResolveResultOfPrim(
                export.Signature.Results[0], types,
                out resultOkPrim, out resultErrPrim))
            {
                isResultReturn = true;
                // ValueTuple<bool, Ok, Err> — scope plan's
                // `(bool ok, T value, E error)` mapping. Avoids
                // coupling the transpiled .dll to a generated
                // Result<Ok, Err> library type.
                var okType = resultOkPrim.HasValue
                    ? PrimToCs(resultOkPrim.Value) : typeof(object);
                var errType = resultErrPrim.HasValue
                    ? PrimToCs(resultErrPrim.Value) : typeof(object);
                returnType = typeof(ValueTuple<,,>).MakeGenericType(
                    typeof(bool), okType, errType);
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
            if (isTupleReturn)
            {
                EmitTupleReturnBody(il, instanceField, coreMethod, export,
                                    tupleElemPrims, returnType);
            }
            else if (isResultReturn)
            {
                EmitResultReturnBody(il, instanceField, coreMethod, export,
                                     resultOkPrim, resultErrPrim, returnType);
            }
            else if (isOptionReturn)
            {
                EmitOptionReturnBody(il, instanceField, coreMethod, export,
                                     optionInnerPrim);
            }
            else if (isListReturn)
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
            EmitCoreCall(il, instanceField, coreMethod, export);
            if (returnType != typeof(void))
            {
                EmitReturnCast(il, coreMethod.ReturnType,
                               export.Signature.Results[0].Prim);
            }
            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Emit the universal prologue: lower all component-side
        /// params to their core-wire form (primitive narrow /
        /// string via UTF-8 encode + <c>cabi_realloc</c> + memcpy
        /// into guest memory), push the cached instance, re-push
        /// each lowered arg, and call the core method. Leaves the
        /// core return value on top of the stack; the caller
        /// handles any return-side lift.
        ///
        /// <para>String params are compiled as:
        /// <code>
        ///     byte[] bytes = StringMarshal.EncodeUtf8(arg);
        ///     int    len   = bytes.Length;
        ///     int    ptr   = instance.CabiRealloc(0, 0, 1, len);
        ///     StringMarshal.CopyToGuest(bytes, instance.Memory, ptr);
        ///     // then push (ptr, len) as two core args
        /// </code>
        /// <c>cabi_realloc</c> is looked up by substring match on
        /// the core IExports method names (the existing transpiler
        /// sanitizes dashes and underscores to valid CLR chars —
        /// substring match is resilient to either convention).</para>
        /// </summary>
        private static void EmitCoreCall(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export)
        {
            EmitPrecomputeStringParamLocals(
                il, instanceField, coreMethod, export,
                out var stringPtrLocals, out var stringLenLocals);

            il.Emit(OpCodes.Ldsfld, instanceField);
            var coreParams = coreMethod.GetParameters();
            int coreParamIdx = 0;
            for (int i = 0; i < export.Signature.Params.Count; i++)
            {
                var prim = export.Signature.Params[i].Type.Prim;
                if (prim == ComponentPrim.String)
                {
                    il.Emit(OpCodes.Ldloc, stringPtrLocals[i]!);
                    il.Emit(OpCodes.Ldloc, stringLenLocals[i]!);
                    coreParamIdx += 2;
                }
                else
                {
                    il.Emit(OpCodes.Ldarg, i);
                    EmitParamCast(il, prim,
                                  coreParams[coreParamIdx].ParameterType);
                    coreParamIdx++;
                }
            }
            il.EmitCall(OpCodes.Callvirt, coreMethod, null);
        }

        /// <summary>
        /// For each string-typed component param, emit the UTF-8
        /// encode + <c>cabi_realloc</c> + memcpy sequence and
        /// store (ptr, len) in locals. Leaves primitive params
        /// untouched — they flow through as direct Ldarg+cast.
        /// Locals for primitive slots stay null.
        /// </summary>
        private static void EmitPrecomputeStringParamLocals(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            out LocalBuilder?[] ptrLocals,
            out LocalBuilder?[] lenLocals)
        {
            var paramCount = export.Signature.Params.Count;
            ptrLocals = new LocalBuilder?[paramCount];
            lenLocals = new LocalBuilder?[paramCount];

            bool anyString = false;
            for (int i = 0; i < paramCount; i++)
            {
                if (export.Signature.Params[i].Type.IsPrimitive
                    && export.Signature.Params[i].Type.Prim == ComponentPrim.String)
                {
                    anyString = true;
                    break;
                }
            }
            if (!anyString) return;

            var reallocMethod = FindCoreReallocMethod(coreMethod.DeclaringType!);
            if (reallocMethod == null)
                throw new InvalidOperationException(
                    "String-param component requires the core module to export "
                    + "`cabi_realloc` — not found on the core IExports.");
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "String-param component requires Module.Memory.");

            var encode = typeof(StringMarshal).GetMethod(
                nameof(StringMarshal.EncodeUtf8),
                new[] { typeof(string) })!;
            var copy = typeof(StringMarshal).GetMethod(
                nameof(StringMarshal.CopyToGuest),
                new[] { typeof(byte[]), typeof(byte[]), typeof(int) })!;

            for (int i = 0; i < paramCount; i++)
            {
                if (!export.Signature.Params[i].Type.IsPrimitive) continue;
                if (export.Signature.Params[i].Type.Prim != ComponentPrim.String)
                    continue;

                var bytesLocal = il.DeclareLocal(typeof(byte[]));
                var lenLocal = il.DeclareLocal(typeof(int));
                var ptrLocal = il.DeclareLocal(typeof(int));

                // bytes = StringMarshal.EncodeUtf8(arg_i)
                il.Emit(OpCodes.Ldarg, i);
                il.EmitCall(OpCodes.Call, encode, null);
                il.Emit(OpCodes.Stloc, bytesLocal);

                // len = bytes.Length
                il.Emit(OpCodes.Ldloc, bytesLocal);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Stloc, lenLocal);

                // ptr = instance.CabiRealloc(0, 0, 1, len)
                il.Emit(OpCodes.Ldsfld, instanceField);
                il.Emit(OpCodes.Ldc_I4_0);     // oldPtr
                il.Emit(OpCodes.Ldc_I4_0);     // oldLen
                il.Emit(OpCodes.Ldc_I4_1);     // align = 1 for byte-aligned UTF-8
                il.Emit(OpCodes.Ldloc, lenLocal);
                il.EmitCall(OpCodes.Callvirt, reallocMethod, null);
                il.Emit(OpCodes.Stloc, ptrLocal);

                // StringMarshal.CopyToGuest(bytes, instance.Memory, ptr)
                il.Emit(OpCodes.Ldloc, bytesLocal);
                il.Emit(OpCodes.Ldsfld, instanceField);
                il.EmitCall(OpCodes.Callvirt, memoryProp.GetGetMethod()!, null);
                il.Emit(OpCodes.Ldloc, ptrLocal);
                il.EmitCall(OpCodes.Call, copy, null);

                ptrLocals[i] = ptrLocal;
                lenLocals[i] = lenLocal;
            }
        }

        /// <summary>
        /// Substring-match find for the core IExports'
        /// <c>cabi_realloc</c>. Tolerant of the transpiler's
        /// name sanitization which may swap <c>-</c> for <c>_</c>
        /// (or leave <c>_</c> alone).
        /// </summary>
        private static MethodInfo? FindCoreReallocMethod(Type iExports)
        {
            foreach (var m in iExports.GetMethods())
            {
                if (m.Name.IndexOf("realloc", StringComparison.OrdinalIgnoreCase) >= 0)
                    return m;
            }
            return null;
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

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));

            // int P = core.hello(args...);  (args lowered via cabi_realloc if string)
            EmitCoreCall(il, instanceField, coreMethod, export);
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

            // int P = core.bytes(args...);
            EmitCoreCall(il, instanceField, coreMethod, export);
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

        /// <summary>
        /// IL body for a <c>() -&gt; option&lt;prim&gt;</c> export.
        /// Canonical-ABI layout: at retArea P sits a 2-word
        /// struct — byte 0 is the discriminant (0 = None, 1 =
        /// Some), then payload aligned to max(1, sizeof(T))
        /// which for small prims is offset 4. We read the disc
        /// via OptionMarshal.IsSome, branch to a Some path that
        /// reads the payload via BitConverter, or a None path
        /// that returns <c>default(T?)</c>.
        /// </summary>
        private static void EmitOptionReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            ComponentPrim innerPrim)
        {
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "Option-returning component requires Module.Memory.");

            var innerCs = PrimToCs(innerPrim);
            var nullableCs = typeof(Nullable<>).MakeGenericType(innerCs);

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));
            var resultLocal = il.DeclareLocal(nullableCs);
            var noneLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            // int P = core.find(args...);
            EmitCoreCall(il, instanceField, coreMethod, export);
            il.Emit(OpCodes.Stloc, retAreaLocal);

            // byte[] memory = instance.Memory;
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.EmitCall(OpCodes.Callvirt, memoryProp.GetGetMethod()!, null);
            il.Emit(OpCodes.Stloc, memoryLocal);

            // if (!OptionMarshal.IsSome(memory.AsSpan(), P)) goto noneLabel;
            // Shortcut: read the disc byte directly via
            // BitConverter-style span load. The OptionMarshal
            // helper is designed for caller-side IL that already
            // has a ReadOnlySpan<byte>; for the AsSpan ceremony
            // we go direct here and inline an equivalent check.
            il.Emit(OpCodes.Ldloc, memoryLocal);            // memory[]
            il.Emit(OpCodes.Ldloc, retAreaLocal);           // P
            il.Emit(OpCodes.Ldelem_U1);                     // memory[P]
            il.Emit(OpCodes.Brfalse, noneLabel);            // disc == 0 ? None

            // Some branch: payload at P + 4 (for small prims).
            // Load T via BitConverter.ToInt32 / ToSingle etc.,
            // wrap in Nullable<T> constructor, store to result.
            var bitCToInt32 = typeof(BitConverter).GetMethod(
                "ToInt32", new[] { typeof(byte[]), typeof(int) })!;
            il.Emit(OpCodes.Ldloc, memoryLocal);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Add);                           // P+4
            il.EmitCall(OpCodes.Call, bitCToInt32, null);   // int at P+4
            // For u32, the i32 loads bitwise-equivalent; for
            // narrower primitives, apply the narrowing cast.
            switch (innerPrim)
            {
                case ComponentPrim.S8:  il.Emit(OpCodes.Conv_I1); break;
                case ComponentPrim.U8:  il.Emit(OpCodes.Conv_U1); break;
                case ComponentPrim.S16: il.Emit(OpCodes.Conv_I2); break;
                case ComponentPrim.U16: il.Emit(OpCodes.Conv_U2); break;
                case ComponentPrim.Bool:
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Cgt_Un);
                    break;
                // 32-bit prims (S32/U32/F32/Char): no cast.
                // Wide prims (S64/U64/F64) would need ToInt64 /
                // ToDouble — follow-up once 64-bit option
                // alignment path is added.
            }
            var nullableCtor = nullableCs.GetConstructor(new[] { innerCs })!;
            il.Emit(OpCodes.Newobj, nullableCtor);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Br, endLabel);

            // None branch: result = default(T?) — already the
            // zero-initialized slot, so just leave it.
            il.MarkLabel(noneLabel);
            il.Emit(OpCodes.Ldloca, resultLocal);
            il.Emit(OpCodes.Initobj, nullableCs);

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// IL body for a <c>() -&gt; result&lt;prim, prim&gt;</c>
        /// export. Layout: byte 0 = disc (0 = Ok, 1 = Err),
        /// payload at offset 4 (small-prim alignment). C# surface
        /// is <c>ValueTuple&lt;bool, Ok, Err&gt;</c> per the scope
        /// plan's error-taxonomy choice. Disc=0 → (true,
        /// okPayload, default(Err)); Disc=1 → (false, default(Ok),
        /// errPayload).
        /// </summary>
        private static void EmitResultReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            ComponentPrim? okPrim, ComponentPrim? errPrim,
            Type tupleType)
        {
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "Result-returning component requires Module.Memory.");

            var tupleFields = new[] {
                tupleType.GetField("Item1")!,
                tupleType.GetField("Item2")!,
                tupleType.GetField("Item3")!,
            };
            var okType = tupleFields[1].FieldType;
            var errType = tupleFields[2].FieldType;

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));
            var resultLocal = il.DeclareLocal(tupleType);
            var errLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            EmitCoreCall(il, instanceField, coreMethod, export);
            il.Emit(OpCodes.Stloc, retAreaLocal);

            il.Emit(OpCodes.Ldsfld, instanceField);
            il.EmitCall(OpCodes.Callvirt, memoryProp.GetGetMethod()!, null);
            il.Emit(OpCodes.Stloc, memoryLocal);

            // if (memory[P] != 0) goto errLabel;
            il.Emit(OpCodes.Ldloc, memoryLocal);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Brtrue, errLabel);

            // Ok branch: tuple = (true, okPayload, default(Err))
            il.Emit(OpCodes.Ldloca, resultLocal);
            il.Emit(OpCodes.Initobj, tupleType);           // zero-init → default
            il.Emit(OpCodes.Ldloca, resultLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, tupleFields[0]);        // ok = true
            if (okPrim.HasValue)
            {
                il.Emit(OpCodes.Ldloca, resultLocal);
                EmitReadPayloadAtOffset(il, memoryLocal, retAreaLocal, 4, okPrim.Value);
                il.Emit(OpCodes.Stfld, tupleFields[1]);    // Item2 = ok payload
            }
            il.Emit(OpCodes.Br, endLabel);

            // Err branch: tuple = (false, default(Ok), errPayload)
            il.MarkLabel(errLabel);
            il.Emit(OpCodes.Ldloca, resultLocal);
            il.Emit(OpCodes.Initobj, tupleType);           // ok = false default
            if (errPrim.HasValue)
            {
                il.Emit(OpCodes.Ldloca, resultLocal);
                EmitReadPayloadAtOffset(il, memoryLocal, retAreaLocal, 4, errPrim.Value);
                il.Emit(OpCodes.Stfld, tupleFields[2]);    // Item3 = err payload
            }

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Emit IL that pushes the primitive payload at
        /// <c>memory[retArea + offset]</c> onto the stack,
        /// typed per <paramref name="prim"/>. 32-bit prims load
        /// via BitConverter.ToInt32; narrower apply Conv.*; wide
        /// prims (s64/u64/f64) are a follow-up.
        /// </summary>
        private static void EmitReadPayloadAtOffset(
            ILGenerator il, LocalBuilder memoryLocal,
            LocalBuilder retAreaLocal, int offset, ComponentPrim prim)
        {
            var bitCToInt32 = typeof(BitConverter).GetMethod(
                "ToInt32", new[] { typeof(byte[]), typeof(int) })!;
            il.Emit(OpCodes.Ldloc, memoryLocal);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            if (offset != 0)
            {
                il.Emit(OpCodes.Ldc_I4, offset);
                il.Emit(OpCodes.Add);
            }
            il.EmitCall(OpCodes.Call, bitCToInt32, null);
            switch (prim)
            {
                case ComponentPrim.S8:  il.Emit(OpCodes.Conv_I1); break;
                case ComponentPrim.U8:  il.Emit(OpCodes.Conv_U1); break;
                case ComponentPrim.S16: il.Emit(OpCodes.Conv_I2); break;
                case ComponentPrim.U16: il.Emit(OpCodes.Conv_U2); break;
                case ComponentPrim.Bool:
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Cgt_Un);
                    break;
                // 32-bit prims (S32/U32/F32/Char) load as-is.
                // Wide prims + strings + aggregates — follow-up.
            }
        }

        /// <summary>
        /// IL body for a <c>() -&gt; tuple&lt;prim, prim, …&gt;</c>
        /// export. No discriminant — the payload is a flat
        /// sequence of elements at their natural-aligned offsets.
        /// For all-32-bit-primitive tuples the offsets are 0, 4,
        /// 8, … . Wide prims (8-byte) + mixed-width alignment
        /// are incremental follow-ups.
        /// </summary>
        private static void EmitTupleReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            ComponentPrim[] elementPrims, Type tupleType)
        {
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "Tuple-returning component requires Module.Memory.");

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));
            var resultLocal = il.DeclareLocal(tupleType);

            EmitCoreCall(il, instanceField, coreMethod, export);
            il.Emit(OpCodes.Stloc, retAreaLocal);

            il.Emit(OpCodes.Ldsfld, instanceField);
            il.EmitCall(OpCodes.Callvirt, memoryProp.GetGetMethod()!, null);
            il.Emit(OpCodes.Stloc, memoryLocal);

            il.Emit(OpCodes.Ldloca, resultLocal);
            il.Emit(OpCodes.Initobj, tupleType);

            // Emit one field-store per tuple element. Offsets are
            // cumulative based on each element's width — for all-
            // 32-bit primitive v0 that's 4-byte stride.
            int offset = 0;
            for (int i = 0; i < elementPrims.Length; i++)
            {
                var fld = tupleType.GetField("Item" + (i + 1))!;
                il.Emit(OpCodes.Ldloca, resultLocal);
                EmitReadPayloadAtOffset(il, memoryLocal, retAreaLocal,
                                        offset, elementPrims[i]);
                il.Emit(OpCodes.Stfld, fld);
                offset += 4;   // v0: all small-prim → 4-byte stride
            }

            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Build a <c>ValueTuple&lt;…&gt;</c> type from a flat
        /// list of element types. Handles 1-7 elements directly;
        /// 8+ elements nest via the <c>TRest</c> convention.
        /// </summary>
        private static Type MakeValueTuple(Type[] elements)
        {
            if (elements.Length == 0)
                throw new ArgumentException("Empty tuple not supported.");
            if (elements.Length > 7)
                throw new NotImplementedException(
                    "8+-element tuples (nested TRest) not yet supported.");
            var openGeneric = elements.Length switch
            {
                1 => typeof(ValueTuple<>),
                2 => typeof(ValueTuple<,>),
                3 => typeof(ValueTuple<,,>),
                4 => typeof(ValueTuple<,,,>),
                5 => typeof(ValueTuple<,,,,>),
                6 => typeof(ValueTuple<,,,,,>),
                7 => typeof(ValueTuple<,,,,,,>),
                _ => throw new InvalidOperationException(),
            };
            return openGeneric.MakeGenericType(elements);
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
