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
using Wacs.ComponentModel.Types;

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
            Type coreModuleClass,
            CtPackage? decodedWit = null)
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

            // Pre-emit named C# types from the decoded WIT so
            // `ComponentExports` methods can reference them as
            // return / param types. Cache by WIT type name.
            var emittedTypes = new Dictionary<string, Type>();
            if (decodedWit != null)
                EmitNamedTypes(module, @namespace, decodedWit,
                               coreIExports, coreModuleClass,
                               emittedTypes);

            foreach (var slot in emittable)
            {
                EmitExportMethod(typeBuilder, instanceField,
                                 coreIExports, slot, component.Types,
                                 emittedTypes, decodedWit);
            }

            return typeBuilder.CreateType();
        }

        /// <summary>Pre-emit C# types for every named WIT type
        /// (enum / variant / record / flags / resource) declared
        /// in the world. Each lands in the assembly under
        /// <paramref name="namespace"/>; the cache maps WIT-level
        /// names to the emitted <see cref="Type"/> so export
        /// methods can use them as return / param types.</summary>
        private static void EmitNamedTypes(
            ModuleBuilder module, string @namespace,
            CtPackage pkg, Type coreIExports, Type coreModuleClass,
            Dictionary<string, Type> emittedTypes)
        {
            // Worlds + interfaces both contribute named types.
            // Phase 1b focuses on worlds; cross-interface types
            // (declared in `interface foo { … }` and used by a
            // world) land when interface emission catches up.
            foreach (var world in pkg.Worlds)
                foreach (var named in world.Types)
                    EmitNamedType(module, @namespace, named,
                                  coreIExports, coreModuleClass, emittedTypes);
            foreach (var iface in pkg.Interfaces)
                foreach (var named in iface.Types)
                    EmitNamedType(module, @namespace, named,
                                  coreIExports, coreModuleClass, emittedTypes);
        }

        private static void EmitNamedType(
            ModuleBuilder module, string @namespace,
            CtNamedType named, Type coreIExports, Type coreModuleClass,
            Dictionary<string, Type> emittedTypes)
        {
            if (emittedTypes.ContainsKey(named.Name)) return;
            switch (named.Type)
            {
                case CtEnumType en:
                    emittedTypes[named.Name] =
                        EmitEnumType(module, @namespace, en);
                    return;
                case CtFlagsType fl:
                    emittedTypes[named.Name] =
                        EmitFlagsType(module, @namespace, fl);
                    return;
                case CtRecordType rec:
                    emittedTypes[named.Name] =
                        EmitRecordType(module, @namespace, named.Name, rec);
                    return;
                case CtVariantType vr:
                    var emitted = EmitVariantType(module, @namespace,
                                                   named.Name, vr);
                    if (emitted != null)
                        emittedTypes[named.Name] = emitted;
                    return;
                case CtResourceType res:
                    emittedTypes[named.Name] =
                        EmitResourceType(module, @namespace, named.Name, res,
                                         coreIExports, coreModuleClass);
                    return;
            }
        }

        /// <summary>Emit a public C# enum type for a WIT enum
        /// declaration. Backing storage is u8/u16/u32 per the
        /// canonical-ABI discriminant-size rule (≤256 cases →
        /// byte, ≤65536 → ushort, else uint).</summary>
        private static Type EmitEnumType(
            ModuleBuilder module, string @namespace, CtEnumType en)
        {
            var underlying = en.Cases.Count <= 256 ? typeof(byte)
                : en.Cases.Count <= 65536 ? typeof(ushort)
                : typeof(uint);
            var typeName = @namespace + "." + PascalCase(en.Name);
            var enumBuilder = module.DefineEnum(typeName,
                TypeAttributes.Public, underlying);
            for (int i = 0; i < en.Cases.Count; i++)
            {
                // Use the exact case's underlying integer literal
                // so reflection-driven callers can round-trip the
                // canonical-ABI discriminant byte to the named
                // case directly.
                object value = underlying == typeof(byte) ? (object)(byte)i
                    : underlying == typeof(ushort) ? (object)(ushort)i
                    : (object)(uint)i;
                enumBuilder.DefineLiteral(PascalCase(en.Cases[i]), value);
            }
            return enumBuilder.CreateType()!;
        }

        /// <summary>Emit a public C# class for a WIT resource
        /// declaration. Shape: a sealed class holding the wasm
        /// handle (i32) as a public readonly field. Internal
        /// constructor takes the handle directly. The
        /// <c>[resource-drop]Type</c> core export wires through
        /// to <see cref="System.IDisposable"/> in a Phase 1b
        /// follow-up — for now the wrapper is non-disposable
        /// and the handle leaks until the component's instance
        /// is replaced.
        ///
        /// <para>Resource methods (constructors, instance and
        /// static methods declared inside the WIT
        /// <c>resource</c> block) are also Phase 1b follow-ups —
        /// each becomes a C# instance method delegating to its
        /// matching <c>[method]Type.name</c> core export.</para>
        /// </summary>
        private static Type EmitResourceType(
            ModuleBuilder module, string @namespace,
            string witName, CtResourceType res,
            Type coreIExports, Type coreModuleClass)
        {
            var typeName = @namespace + "." + PascalCase(witName);
            // Wire IDisposable when the core module exports the
            // matching `[resource-drop]<Type>` function. Without
            // it the wrapper is non-disposable — the handle
            // leaks until the component instance is replaced.
            var dropMethod = FindResourceDropMethod(coreIExports, witName);
            var interfaces = dropMethod != null
                ? new[] { typeof(System.IDisposable) }
                : Type.EmptyTypes;
            var classBuilder = module.DefineType(typeName,
                TypeAttributes.Public | TypeAttributes.Sealed
                    | TypeAttributes.Class | TypeAttributes.AutoClass
                    | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                typeof(object), interfaces);

            var handleField = classBuilder.DefineField(
                "Handle", typeof(int),
                FieldAttributes.Public | FieldAttributes.InitOnly);

            FieldBuilder? instanceField = null;
            ConstructorBuilder? cctor = null;
            if (dropMethod != null)
            {
                instanceField = classBuilder.DefineField(
                    "_instance", coreModuleClass,
                    FieldAttributes.Private | FieldAttributes.Static
                        | FieldAttributes.InitOnly);
                cctor = classBuilder.DefineTypeInitializer();
                var cctorIl = cctor.GetILGenerator();
                var coreCtor = coreModuleClass.GetConstructor(Type.EmptyTypes);
                if (coreCtor != null)
                {
                    cctorIl.Emit(OpCodes.Newobj, coreCtor);
                    cctorIl.Emit(OpCodes.Stsfld, instanceField);
                }
                cctorIl.Emit(OpCodes.Ret);
            }

            var ctor = classBuilder.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                CallingConventions.Standard, new[] { typeof(int) });
            ctor.DefineParameter(1, ParameterAttributes.None, "handle");
            var ctorIl = ctor.GetILGenerator();
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Call,
                typeof(object).GetConstructor(Type.EmptyTypes)!);
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Ldarg_1);
            ctorIl.Emit(OpCodes.Stfld, handleField);
            ctorIl.Emit(OpCodes.Ret);

            if (dropMethod != null && instanceField != null)
            {
                // public void Dispose() {
                //     _instance.[resource-drop]<Type>(this.Handle);
                // }
                var dispose = classBuilder.DefineMethod(
                    "Dispose",
                    MethodAttributes.Public | MethodAttributes.Virtual
                        | MethodAttributes.HideBySig | MethodAttributes.NewSlot
                        | MethodAttributes.Final,
                    typeof(void), Type.EmptyTypes);
                var dIl = dispose.GetILGenerator();
                dIl.Emit(OpCodes.Ldsfld, instanceField);
                dIl.Emit(OpCodes.Ldarg_0);
                dIl.Emit(OpCodes.Ldfld, handleField);
                dIl.EmitCall(OpCodes.Callvirt, dropMethod, null);
                dIl.Emit(OpCodes.Ret);
                classBuilder.DefineMethodOverride(dispose,
                    typeof(System.IDisposable).GetMethod("Dispose")!);
            }

            _ = res;   // explicit-dtor field is followup
            return classBuilder.CreateType()!;
        }

        /// <summary>Find the core IExports method matching the
        /// canonical-ABI <c>[resource-drop]<i>witName</i></c>
        /// export the binding tools emit. Sanitizer turns
        /// <c>[</c> / <c>]</c> / <c>-</c> into <c>_</c>, so
        /// <c>[resource-drop]counter</c> ends up as
        /// <c>_resource_drop_counter</c> on the IExports
        /// interface.</summary>
        private static MethodInfo? FindResourceDropMethod(
            Type iExports, string witName)
        {
            var sanitized = SanitizeExportName("[resource-drop]" + witName);
            var pascal = PascalCase("[resource-drop]" + witName);
            foreach (var m in iExports.GetMethods())
            {
                if (string.Equals(m.Name, sanitized, StringComparison.OrdinalIgnoreCase))
                    return m;
                if (m.Name.IndexOf("resource_drop", StringComparison.OrdinalIgnoreCase) >= 0
                    && m.Name.IndexOf(witName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return m;
                if (string.Equals(m.Name, pascal, StringComparison.Ordinal))
                    return m;
            }
            return null;
        }

        /// <summary>Emit a public C# class for a WIT variant
        /// declaration. Shape: a sealed class with a public
        /// readonly <c>Tag</c> (byte / ushort / uint depending on
        /// case count), a public readonly field per
        /// payload-bearing case named after that case, and a
        /// public constructor taking (tag, all payload values
        /// positionally). Static factory methods land per case
        /// for ergonomic construction at the host side.
        ///
        /// <para>v0 supports primitive payloads only — variants
        /// with aggregate payloads (lists, records, nested
        /// variants) are Phase 2.</para>
        /// </summary>
        private static Type? EmitVariantType(
            ModuleBuilder module, string @namespace,
            string witName, CtVariantType vr)
        {
            // Bail on aggregate payloads — same constraint as
            // record fields. Caller skips the export.
            foreach (var c in vr.Cases)
                if (c.Payload != null && !(c.Payload is CtPrimType))
                    return null;

            var caseCount = vr.Cases.Count;
            var tagType = caseCount <= 256 ? typeof(byte)
                : caseCount <= 65536 ? typeof(ushort)
                : typeof(uint);

            var typeName = @namespace + "." + PascalCase(witName);
            var classBuilder = module.DefineType(typeName,
                TypeAttributes.Public | TypeAttributes.Sealed
                    | TypeAttributes.Class | TypeAttributes.AutoClass
                    | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                typeof(object));

            // Tag field.
            var tagField = classBuilder.DefineField(
                "Tag", tagType,
                FieldAttributes.Public | FieldAttributes.InitOnly);

            // Per-payload-case fields (only the cases with
            // payloads). Each named after the case in PascalCase.
            var payloadFieldByIdx = new Dictionary<int, FieldBuilder>();
            for (int i = 0; i < caseCount; i++)
            {
                if (!(vr.Cases[i].Payload is CtPrimType prim)) continue;
                var fld = classBuilder.DefineField(
                    PascalCase(vr.Cases[i].Name),
                    CtPrimToCs(prim.Kind),
                    FieldAttributes.Public | FieldAttributes.InitOnly);
                payloadFieldByIdx[i] = fld;
            }

            // Constructor: (tag, payload_field_0, payload_field_1, …)
            // — payload args appear in case order, omitting
            // no-payload cases.
            var ctorParamTypes = new List<Type> { tagType };
            var orderedPayloadIndices = new List<int>();
            for (int i = 0; i < caseCount; i++)
            {
                if (payloadFieldByIdx.ContainsKey(i))
                {
                    ctorParamTypes.Add(payloadFieldByIdx[i].FieldType);
                    orderedPayloadIndices.Add(i);
                }
            }
            var ctor = classBuilder.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                CallingConventions.Standard, ctorParamTypes.ToArray());
            ctor.DefineParameter(1, ParameterAttributes.None, "tag");
            for (int i = 0; i < orderedPayloadIndices.Count; i++)
                ctor.DefineParameter(i + 2, ParameterAttributes.None,
                    CamelCase(vr.Cases[orderedPayloadIndices[i]].Name));
            var ctorIl = ctor.GetILGenerator();
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Call,
                typeof(object).GetConstructor(Type.EmptyTypes)!);
            // tag = arg1
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Ldarg_1);
            ctorIl.Emit(OpCodes.Stfld, tagField);
            for (int i = 0; i < orderedPayloadIndices.Count; i++)
            {
                ctorIl.Emit(OpCodes.Ldarg_0);
                ctorIl.Emit(OpCodes.Ldarg, i + 2);
                ctorIl.Emit(OpCodes.Stfld,
                    payloadFieldByIdx[orderedPayloadIndices[i]]);
            }
            ctorIl.Emit(OpCodes.Ret);

            return classBuilder.CreateType()!;
        }

        /// <summary>Emit a public C# class for a WIT record
        /// declaration. Each field becomes a public readonly
        /// auto-property; a public constructor takes the fields
        /// in declaration order. Restricted to records with
        /// primitive fields in v0 — aggregate fields each need
        /// their own marshaling tactics and arrive incrementally.
        /// </summary>
        private static Type EmitRecordType(
            ModuleBuilder module, string @namespace,
            string witName, CtRecordType rec)
        {
            var typeName = @namespace + "." + PascalCase(witName);
            var classBuilder = module.DefineType(typeName,
                TypeAttributes.Public | TypeAttributes.Sealed
                    | TypeAttributes.AutoClass | TypeAttributes.Class
                    | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                typeof(object));

            var fieldCount = rec.Fields.Count;
            var fieldBuilders = new FieldBuilder[fieldCount];
            var fieldClrTypes = new Type[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                if (!(rec.Fields[i].Type is CtPrimType prim))
                    throw new NotSupportedException(
                        "EmitRecordType: aggregate field types are a follow-up.");
                fieldClrTypes[i] = CtPrimToCs(prim.Kind);
                fieldBuilders[i] = classBuilder.DefineField(
                    PascalCase(rec.Fields[i].Name),
                    fieldClrTypes[i],
                    FieldAttributes.Public | FieldAttributes.InitOnly);
            }

            // Constructor: Record(field0, field1, …) — assigns
            // each parameter to its corresponding field.
            var ctor = classBuilder.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                CallingConventions.Standard, fieldClrTypes);
            for (int i = 0; i < fieldCount; i++)
                ctor.DefineParameter(i + 1, ParameterAttributes.None,
                    CamelCase(rec.Fields[i].Name));
            var ctorIl = ctor.GetILGenerator();
            // Call object's parameterless ctor, then assign each
            // field from its incoming arg.
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Call,
                typeof(object).GetConstructor(Type.EmptyTypes)!);
            for (int i = 0; i < fieldCount; i++)
            {
                ctorIl.Emit(OpCodes.Ldarg_0);
                ctorIl.Emit(OpCodes.Ldarg, i + 1);
                ctorIl.Emit(OpCodes.Stfld, fieldBuilders[i]);
            }
            ctorIl.Emit(OpCodes.Ret);

            return classBuilder.CreateType()!;
        }

        /// <summary>Map a <see cref="CtPrim"/> to its CLR type.
        /// Mirror of <see cref="PrimToCs(ComponentPrim)"/> but for
        /// the WIT-decoded prim enum, which uses different
        /// numbering than the binary's <see cref="ComponentPrim"/>.
        /// </summary>
        private static Type CtPrimToCs(CtPrim p) => p switch
        {
            CtPrim.Bool   => typeof(bool),
            CtPrim.S8     => typeof(sbyte),
            CtPrim.U8     => typeof(byte),
            CtPrim.S16    => typeof(short),
            CtPrim.U16    => typeof(ushort),
            CtPrim.S32    => typeof(int),
            CtPrim.U32    => typeof(uint),
            CtPrim.S64    => typeof(long),
            CtPrim.U64    => typeof(ulong),
            CtPrim.F32    => typeof(float),
            CtPrim.F64    => typeof(double),
            CtPrim.Char   => typeof(uint),
            CtPrim.String => typeof(string),
            _ => throw new NotSupportedException("CtPrimToCs " + p),
        };

        /// <summary>Emit a public C# enum type marked with
        /// <see cref="System.FlagsAttribute"/> for a WIT flags
        /// declaration. Each flag gets a literal value of
        /// <c>1 &lt;&lt; i</c>. Underlying width tracks the
        /// canonical-ABI flag wire encoding: ≤8 → byte, ≤16 →
        /// ushort, ≤32 → uint.</summary>
        private static Type EmitFlagsType(
            ModuleBuilder module, string @namespace, CtFlagsType fl)
        {
            var underlying = fl.Flags.Count <= 8 ? typeof(byte)
                : fl.Flags.Count <= 16 ? typeof(ushort)
                : typeof(uint);
            var typeName = @namespace + "." + PascalCase(fl.Name);
            var enumBuilder = module.DefineEnum(typeName,
                TypeAttributes.Public, underlying);
            for (int i = 0; i < fl.Flags.Count; i++)
            {
                ulong bit = 1UL << i;
                object value = underlying == typeof(byte) ? (object)(byte)bit
                    : underlying == typeof(ushort) ? (object)(ushort)bit
                    : (object)(uint)bit;
                enumBuilder.DefineLiteral(PascalCase(fl.Flags[i]), value);
            }
            // Apply [Flags] so ToString() formats as bitmask.
            var flagsCtor = typeof(System.FlagsAttribute)
                .GetConstructor(Type.EmptyTypes)!;
            enumBuilder.SetCustomAttribute(
                new CustomAttributeBuilder(flagsCtor, Array.Empty<object>()));
            return enumBuilder.CreateType()!;
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
            var funcToCanon = component.ComponentFuncToCanon;
            var types = component.Types;
            foreach (var export in component.Exports)
            {
                if (export.Sort != ComponentSort.Func) continue;
                // Resolve via the RawSection-walking map, not a
                // flat index into component.Canons — wit-component
                // grows the component-func space through Func-kind
                // exports too, so flat-index lookup drops real
                // exports in multi-export components.
                if (!funcToCanon.TryGetValue(export.Index, out var lift))
                    continue;
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
            {
                if (p.Type.IsPrimitive) continue;
                if (TryResolveListOfPrim(p.Type, types, out _)) continue;
                return false;
            }
            if (fn.Results.Count == 0) return true;
            if (fn.Results.Count != 1) return false;
            var r = fn.Results[0];
            if (r.IsPrimitive) return true;
            // Type-table ref — list<prim>, option<prim>,
            // result<prim>, tuple<prims>.
            if (TryResolveListOfPrim(r, types, out _)) return true;
            if (TryResolveListOfString(r, types)) return true;
            if (TryResolveOptionOfPrim(r, types, out _)) return true;
            if (TryResolveOptionOfString(r, types)) return true;
            if (TryResolveResult(r, types, out _, out _)) return true;
            if (TryResolveTupleOfPrims(r, types, out _)) return true;
            if (TryResolveEnumReturn(r, types, out _)) return true;
            if (TryResolveFlagsReturn(r, types, out _)) return true;
            if (TryResolveRecordOfPrims(r, types, out _, out _)) return true;
            if (TryResolveVariantReturn(r, types, out var vrShape)
                && !vrShape.HasAggregatePayload) return true;
            if (TryResolveOwnReturn(r, types, out _)) return true;
            return false;
        }

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to a list<primitive> in <paramref name="types"/>;
        /// when so, <paramref name="elemPrim"/> captures the
        /// primitive element kind. Strings are excluded — they
        /// go through the <see cref="TryResolveListOfString"/>
        /// predicate since each element is a two-i32 pair rather
        /// than a raw primitive slot.</summary>
        private static bool TryResolveListOfPrim(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types,
            out ComponentPrim elemPrim)
        {
            elemPrim = default;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentListType list)) return false;
            if (!list.Element.IsPrimitive) return false;
            if (list.Element.Prim == ComponentPrim.String) return false;
            elemPrim = list.Element.Prim;
            return true;
        }

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to <c>list&lt;string&gt;</c>. Each element is a
        /// (strPtr, strLen) i32 pair at 4-byte alignment — the
        /// retArea holds (listPtr, count) at offsets 0/4, with
        /// the element array at listPtr.</summary>
        private static bool TryResolveListOfString(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types)
        {
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentListType list)) return false;
            if (!list.Element.IsPrimitive) return false;
            return list.Element.Prim == ComponentPrim.String;
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
            if (opt.Inner.Prim == ComponentPrim.String) return false;
            innerPrim = opt.Inner.Prim;
            return true;
        }

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to a structural enum type. Returns the case count via
        /// <paramref name="caseCount"/> so the caller can pick the
        /// right Conv.* opcode (byte / ushort / uint underlying).</summary>
        private static bool TryResolveEnumReturn(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types,
            out int caseCount)
        {
            caseCount = 0;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentEnumType en)) return false;
            caseCount = en.Cases.Count;
            return true;
        }

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to a structural flags type. Wire width is determined
        /// by flag count (≤8 → u8, ≤16 → u16, ≤32 → u32).</summary>
        private static bool TryResolveFlagsReturn(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types,
            out int flagCount)
        {
            flagCount = 0;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentFlagsType fl)) return false;
            flagCount = fl.Flags.Count;
            return true;
        }

        /// <summary>Description of a structural variant viewed
        /// from the binary type section: case names paired with
        /// optional primitive payloads. Aggregate-payload variants
        /// (Phase 2) signal as <see cref="HasAggregatePayload"/>;
        /// the IL emitter rejects them.</summary>
        private sealed class VariantShape
        {
            public IReadOnlyList<string> CaseNames { get; }
            public IReadOnlyList<ComponentPrim?> CasePayloadPrims { get; }
            public bool HasAggregatePayload { get; }
            public VariantShape(IReadOnlyList<string> names,
                                IReadOnlyList<ComponentPrim?> payloads,
                                bool hasAggregate)
            {
                CaseNames = names;
                CasePayloadPrims = payloads;
                HasAggregatePayload = hasAggregate;
            }
        }

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to a structural variant where every payload-bearing
        /// case carries a primitive (or no payload at all).
        /// Aggregate-payload variants are Phase 2 — rejected via
        /// <see cref="VariantShape.HasAggregatePayload"/>.</summary>
        private static bool TryResolveVariantReturn(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types,
            out VariantShape shape)
        {
            shape = null!;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentVariantType vr)) return false;
            var names = new string[vr.Cases.Count];
            var payloads = new ComponentPrim?[vr.Cases.Count];
            bool aggregate = false;
            for (int i = 0; i < vr.Cases.Count; i++)
            {
                names[i] = vr.Cases[i].Name;
                if (vr.Cases[i].Payload == null)
                {
                    payloads[i] = null;
                }
                else if (vr.Cases[i].Payload!.Value.IsPrimitive)
                {
                    payloads[i] = vr.Cases[i].Payload!.Value.Prim;
                }
                else
                {
                    aggregate = true;
                    payloads[i] = null;
                }
            }
            shape = new VariantShape(names, payloads, aggregate);
            return true;
        }

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to an <c>own&lt;R&gt;</c> handle pointing at a fresh
        /// resource type. <paramref name="resourceTypeIdx"/>
        /// captures the slot of the underlying resource so the
        /// emitter can resolve the C# class via the WIT
        /// decoder's named-type map.</summary>
        private static bool TryResolveOwnReturn(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types,
            out uint resourceTypeIdx)
        {
            resourceTypeIdx = 0;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentOwnType own)) return false;
            if (own.TypeIdx >= types.Count) return false;
            if (!(types[(int)own.TypeIdx] is ComponentResourceType)) return false;
            resourceTypeIdx = own.TypeIdx;
            return true;
        }

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to a structural record whose fields are all
        /// primitives. Records with aggregate fields land in a
        /// follow-up — each adds its own marshaling shape.</summary>
        private static bool TryResolveRecordOfPrims(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types,
            out ComponentPrim[] fieldPrims, out string[] fieldNames)
        {
            fieldPrims = Array.Empty<ComponentPrim>();
            fieldNames = Array.Empty<string>();
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentRecordType rec)) return false;
            var prims = new ComponentPrim[rec.Fields.Count];
            var names = new string[rec.Fields.Count];
            for (int i = 0; i < prims.Length; i++)
            {
                if (!rec.Fields[i].Type.IsPrimitive) return false;
                prims[i] = rec.Fields[i].Type.Prim;
                names[i] = rec.Fields[i].Name;
            }
            fieldPrims = prims;
            fieldNames = names;
            return true;
        }

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to <c>option&lt;string&gt;</c>. Separate predicate
        /// from <see cref="TryResolveOptionOfPrim"/> because the
        /// lift path is structurally different: the payload is
        /// (ptr, len) at offset 4 instead of a raw primitive
        /// value, and the C# surface is a nullable reference
        /// type (<c>string</c>) instead of <c>Nullable&lt;T&gt;</c>.</summary>
        private static bool TryResolveOptionOfString(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types)
        {
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentOptionType opt)) return false;
            if (!opt.Inner.IsPrimitive) return false;
            return opt.Inner.Prim == ComponentPrim.String;
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

        /// <summary>
        /// Classification for one side of a <c>result&lt;Ok, Err&gt;</c>.
        /// Captures the shape (absent / primitive / string) + any
        /// associated primitive kind. Larger aggregates (list,
        /// variant, record) are follow-ups — they'd extend this
        /// abstraction.
        /// </summary>
        private sealed class ResultSide
        {
            public enum SideKind { Absent, Primitive, String }
            public SideKind Kind { get; }
            public ComponentPrim Prim { get; }

            public static readonly ResultSide Absent =
                new ResultSide(SideKind.Absent, default);
            public static readonly ResultSide String =
                new ResultSide(SideKind.String, default);
            public static ResultSide Primitive(ComponentPrim p) =>
                new ResultSide(SideKind.Primitive, p);

            private ResultSide(SideKind kind, ComponentPrim prim)
            {
                Kind = kind; Prim = prim;
            }

            /// <summary>C# type for this side at the bound surface.
            /// Absent maps to <c>object</c> so the
            /// <c>ValueTuple&lt;bool, Ok, Err&gt;</c> generic
            /// remains buildable (the field will only ever be null
            /// / default). Primitive and string map direct.</summary>
            public Type ToCsType() => Kind switch
            {
                SideKind.Absent => typeof(object),
                SideKind.Primitive => PrimToCs(Prim),
                SideKind.String => typeof(string),
                _ => throw new InvalidOperationException(),
            };
        }

        /// <summary>True iff <paramref name="t"/> is a type-ref
        /// to <c>result&lt;Ok, Err&gt;</c> where each side is
        /// absent, a primitive, or a string. Aggregate payloads
        /// (list, variant, record) are follow-ups.</summary>
        private static bool TryResolveResult(
            ComponentValType t, IReadOnlyList<DefTypeEntry> types,
            out ResultSide okSide, out ResultSide errSide)
        {
            okSide = ResultSide.Absent;
            errSide = ResultSide.Absent;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= types.Count) return false;
            if (!(types[(int)t.TypeIdx] is ComponentResultType res)) return false;
            if (!TryResolveResultSide(res.Ok, out okSide)) return false;
            if (!TryResolveResultSide(res.Err, out errSide)) return false;
            return true;
        }

        private static bool TryResolveResultSide(
            ComponentValType? raw, out ResultSide side)
        {
            if (raw == null) { side = ResultSide.Absent; return true; }
            if (!raw.Value.IsPrimitive) { side = ResultSide.Absent; return false; }
            side = raw.Value.Prim == ComponentPrim.String
                ? ResultSide.String
                : ResultSide.Primitive(raw.Value.Prim);
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
            IReadOnlyList<DefTypeEntry> types,
            Dictionary<string, Type> emittedTypes,
            CtPackage? decodedWit)
        {
            var coreMethod = FindCoreMethod(coreIExports, export.Name);
            if (coreMethod == null) return;

            // C#-side types per the COMPONENT signature. Primitive
            // params map direct; list<prim> params map to T[].
            var paramTypes = new Type[export.Signature.Params.Count];
            for (int i = 0; i < paramTypes.Length; i++)
            {
                var pt = export.Signature.Params[i].Type;
                if (pt.IsPrimitive)
                {
                    paramTypes[i] = PrimToCs(pt.Prim);
                }
                else if (TryResolveListOfPrim(pt, types, out var listElem))
                {
                    paramTypes[i] = PrimToCs(listElem).MakeArrayType();
                }
                else
                {
                    return;   // shouldn't reach — IsEmittable rejects
                }
            }

            // Return-type dispatch: void / primitive (inline),
            // string (StringMarshal chokepoint), list<prim>
            // (ListMarshal closed generic), option<prim>
            // (disc switch + inline).
            bool isListReturn = false;
            bool isListStringReturn = false;
            bool isOptionReturn = false;
            bool isOptionStringReturn = false;
            bool isResultReturn = false;
            bool isTupleReturn = false;
            bool isEnumReturn = false;
            bool isFlagsReturn = false;
            bool isRecordReturn = false;
            bool isVariantReturn = false;
            bool isOwnReturn = false;
            ComponentPrim listElemPrim = default;
            ComponentPrim optionInnerPrim = default;
            ResultSide resultOkSide = ResultSide.Absent;
            ResultSide resultErrSide = ResultSide.Absent;
            ComponentPrim[] tupleElemPrims = Array.Empty<ComponentPrim>();
            int enumCaseCount = 0;
            int flagCount = 0;
            ComponentPrim[] recordFieldPrims = Array.Empty<ComponentPrim>();
            string[] recordFieldNames = Array.Empty<string>();
            VariantShape? variantShape = null;
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
            else if (TryResolveListOfString(
                export.Signature.Results[0], types))
            {
                isListStringReturn = true;
                returnType = typeof(string[]);
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
            else if (TryResolveOptionOfString(
                export.Signature.Results[0], types))
            {
                isOptionStringReturn = true;
                // wit-bindgen's option<string> maps to nullable
                // reference `string?`. At IL level that's a plain
                // string — null represents None.
                returnType = typeof(string);
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
            else if (TryResolveResult(
                export.Signature.Results[0], types,
                out resultOkSide, out resultErrSide))
            {
                isResultReturn = true;
                // ValueTuple<bool, Ok, Err> — scope plan's
                // `(bool ok, T value, E error)` mapping. Avoids
                // coupling the transpiled .dll to a generated
                // Result<Ok, Err> library type.
                returnType = typeof(ValueTuple<,,>).MakeGenericType(
                    typeof(bool),
                    resultOkSide.ToCsType(),
                    resultErrSide.ToCsType());
            }
            else if (TryResolveEnumReturn(
                export.Signature.Results[0], types, out enumCaseCount))
            {
                isEnumReturn = true;
                // Look up the WIT-decoded enum name to bind a
                // pre-emitted C# enum type. Without the decoder,
                // fall back to the underlying integer (byte/
                // ushort/uint per case count).
                returnType = ResolveEnumReturnType(
                    decodedWit, emittedTypes, export.Name,
                    enumCaseCount);
            }
            else if (TryResolveFlagsReturn(
                export.Signature.Results[0], types, out flagCount))
            {
                isFlagsReturn = true;
                returnType = ResolveFlagsReturnType(
                    decodedWit, emittedTypes, export.Name, flagCount);
            }
            else if (TryResolveRecordOfPrims(
                export.Signature.Results[0], types,
                out recordFieldPrims, out recordFieldNames))
            {
                isRecordReturn = true;
                // Resolve via decoded WIT — records need a
                // generated class for the user-facing surface.
                // Without a name we'd have no top-level type to
                // emit; reject the export in that case (keeps the
                // ComponentExports class clean of anonymous
                // nested types).
                var named = TryFindNamedReturn(decodedWit, emittedTypes,
                                                export.Name);
                if (named == null) return;
                returnType = named;
            }
            else if (TryResolveVariantReturn(
                export.Signature.Results[0], types, out variantShape)
                && !variantShape.HasAggregatePayload)
            {
                isVariantReturn = true;
                // Variants need a generated class — reject
                // unnamed cases.
                var named = TryFindNamedReturn(decodedWit, emittedTypes,
                                                export.Name);
                if (named == null) return;
                returnType = named;
            }
            else if (TryResolveOwnReturn(
                export.Signature.Results[0], types, out _))
            {
                isOwnReturn = true;
                // own<R> returns require a generated resource
                // class. Reject if no name is bound.
                var named = TryFindNamedReturn(decodedWit, emittedTypes,
                                                export.Name);
                if (named == null) return;
                returnType = named;
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
                                    types, tupleElemPrims, returnType);
            }
            else if (isResultReturn)
            {
                EmitResultReturnBody(il, instanceField, coreMethod, export,
                                     types, resultOkSide, resultErrSide,
                                     returnType);
            }
            else if (isOptionReturn)
            {
                EmitOptionReturnBody(il, instanceField, coreMethod, export,
                                     types, optionInnerPrim);
            }
            else if (isOptionStringReturn)
            {
                EmitOptionStringReturnBody(il, instanceField, coreMethod,
                                           export, types);
            }
            else if (isListReturn)
            {
                EmitListReturnBody(il, instanceField, coreMethod, export,
                                   types, listElemPrim);
            }
            else if (isListStringReturn)
            {
                EmitListStringReturnBody(il, instanceField, coreMethod,
                                         export, types);
            }
            else if (IsStringReturn(export.Signature))
            {
                EmitStringReturnBody(il, instanceField, coreMethod, export,
                                     types);
            }
            else if (isEnumReturn)
            {
                EmitEnumReturnBody(il, instanceField, coreMethod, export,
                                   types, returnType, enumCaseCount);
            }
            else if (isFlagsReturn)
            {
                EmitFlagsReturnBody(il, instanceField, coreMethod, export,
                                    types, returnType, flagCount);
            }
            else if (isRecordReturn)
            {
                EmitRecordReturnBody(il, instanceField, coreMethod, export,
                                     types, returnType, recordFieldPrims);
            }
            else if (isVariantReturn)
            {
                EmitVariantReturnBody(il, instanceField, coreMethod, export,
                                      types, returnType, variantShape!);
            }
            else if (isOwnReturn)
            {
                EmitOwnReturnBody(il, instanceField, coreMethod, export,
                                  types, returnType);
            }
            else
            {
                EmitPrimitiveBody(il, instanceField, coreMethod, export,
                                  types, returnType);
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
            IReadOnlyList<DefTypeEntry> types, Type returnType)
        {
            EmitCoreCall(il, instanceField, coreMethod, export, types);
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
            MethodInfo coreMethod, EmittableExport export,
            IReadOnlyList<DefTypeEntry> types)
        {
            EmitPrecomputeBufferParamLocals(
                il, instanceField, coreMethod, export, types,
                out var ptrLocals, out var countLocals);

            il.Emit(OpCodes.Ldsfld, instanceField);
            var coreParams = coreMethod.GetParameters();
            int coreParamIdx = 0;
            for (int i = 0; i < export.Signature.Params.Count; i++)
            {
                var pt = export.Signature.Params[i].Type;
                if (ptrLocals[i] != null)
                {
                    // String or list<prim> — the precompute step
                    // stashed (ptr, count) in locals. Push them
                    // as the two core args.
                    il.Emit(OpCodes.Ldloc, ptrLocals[i]!);
                    il.Emit(OpCodes.Ldloc, countLocals[i]!);
                    coreParamIdx += 2;
                }
                else
                {
                    il.Emit(OpCodes.Ldarg, i);
                    EmitParamCast(il, pt.Prim,
                                  coreParams[coreParamIdx].ParameterType);
                    coreParamIdx++;
                }
            }
            il.EmitCall(OpCodes.Callvirt, coreMethod, null);
        }

        /// <summary>
        /// For each aggregate-buffer-typed component param
        /// (string or list&lt;primitive&gt;), emit the encode +
        /// <c>cabi_realloc</c> + memcpy sequence and store
        /// (ptr, count) in locals. Primitive params stay
        /// untouched — they flow through as direct Ldarg+cast.
        /// Locals for primitive slots stay null.
        ///
        /// <para>For <c>string</c>: count = UTF-8 byte length
        /// (the canonical form both sides of the ABI agree on).
        /// For <c>list&lt;T&gt;</c>: count = element count (also
        /// canonical — core functions take count, not byte
        /// length, since element size is known from the type).</para>
        /// </summary>
        private static void EmitPrecomputeBufferParamLocals(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            IReadOnlyList<DefTypeEntry> types,
            out LocalBuilder?[] ptrLocals,
            out LocalBuilder?[] countLocals)
        {
            var paramCount = export.Signature.Params.Count;
            ptrLocals = new LocalBuilder?[paramCount];
            countLocals = new LocalBuilder?[paramCount];

            bool anyBuffer = false;
            for (int i = 0; i < paramCount; i++)
            {
                var pt = export.Signature.Params[i].Type;
                if (pt.IsPrimitive && pt.Prim == ComponentPrim.String)
                {
                    anyBuffer = true;
                    break;
                }
                if (!pt.IsPrimitive && TryResolveListOfPrim(pt, types, out _))
                {
                    anyBuffer = true;
                    break;
                }
            }
            if (!anyBuffer) return;

            var reallocMethod = FindCoreReallocMethod(coreMethod.DeclaringType!);
            if (reallocMethod == null)
                throw new InvalidOperationException(
                    "Aggregate-param component requires the core module to "
                    + "export `cabi_realloc` — not found on the core IExports.");
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "Aggregate-param component requires Module.Memory.");

            for (int i = 0; i < paramCount; i++)
            {
                var pt = export.Signature.Params[i].Type;
                if (pt.IsPrimitive && pt.Prim == ComponentPrim.String)
                {
                    EmitLowerStringParamToLocals(
                        il, instanceField, reallocMethod, memoryProp,
                        i, out ptrLocals[i], out countLocals[i]);
                }
                else if (!pt.IsPrimitive
                         && TryResolveListOfPrim(pt, types, out var elemPrim))
                {
                    EmitLowerListParamToLocals(
                        il, instanceField, reallocMethod, memoryProp,
                        i, elemPrim,
                        out ptrLocals[i], out countLocals[i]);
                }
            }
        }

        /// <summary>
        /// IL for the string-param lower path: UTF-8 encode →
        /// <c>cabi_realloc</c>(0, 0, 1, byteLen) → byte-copy.
        /// Stashes (ptr, byteLen) in locals for the caller to
        /// push at core-call time.
        /// </summary>
        private static void EmitLowerStringParamToLocals(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo reallocMethod, PropertyInfo memoryProp,
            int argIdx, out LocalBuilder ptrLocal,
            out LocalBuilder countLocal)
        {
            var encode = typeof(StringMarshal).GetMethod(
                nameof(StringMarshal.EncodeUtf8),
                new[] { typeof(string) })!;
            var copy = typeof(StringMarshal).GetMethod(
                nameof(StringMarshal.CopyToGuest),
                new[] { typeof(byte[]), typeof(byte[]), typeof(int) })!;

            var bytesLocal = il.DeclareLocal(typeof(byte[]));
            countLocal = il.DeclareLocal(typeof(int));
            ptrLocal = il.DeclareLocal(typeof(int));

            il.Emit(OpCodes.Ldarg, argIdx);
            il.EmitCall(OpCodes.Call, encode, null);
            il.Emit(OpCodes.Stloc, bytesLocal);

            il.Emit(OpCodes.Ldloc, bytesLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, countLocal);

            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Ldc_I4_0);     // oldPtr
            il.Emit(OpCodes.Ldc_I4_0);     // oldLen
            il.Emit(OpCodes.Ldc_I4_1);     // align = 1 (byte-aligned UTF-8)
            il.Emit(OpCodes.Ldloc, countLocal);
            il.EmitCall(OpCodes.Callvirt, reallocMethod, null);
            il.Emit(OpCodes.Stloc, ptrLocal);

            il.Emit(OpCodes.Ldloc, bytesLocal);
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.EmitCall(OpCodes.Callvirt, memoryProp.GetGetMethod()!, null);
            il.Emit(OpCodes.Ldloc, ptrLocal);
            il.EmitCall(OpCodes.Call, copy, null);
        }

        /// <summary>
        /// IL for the <c>list&lt;prim&gt;</c>-param lower path:
        /// <c>cabi_realloc</c>(0, 0, elemSize, count * elemSize)
        /// → byte-copy the array's native little-endian bytes
        /// into guest memory. Stashes (ptr, count) in locals so
        /// the caller can push them at core-call time. Alignment
        /// equals element size — the canonical-ABI rule for
        /// primitive-element lists.
        /// </summary>
        private static void EmitLowerListParamToLocals(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo reallocMethod, PropertyInfo memoryProp,
            int argIdx, ComponentPrim elemPrim,
            out LocalBuilder ptrLocal, out LocalBuilder countLocal)
        {
            var elemCs = PrimToCs(elemPrim);
            var elemSize = PrimByteSize(elemPrim);
            var arrayType = elemCs.MakeArrayType();

            // ListMarshal.CopyArrayToGuest is the unique generic
            // method on the class — plain GetMethod resolves it
            // cleanly (the open generic has a single overload).
            var copyOpen = typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.CopyArrayToGuest))!;
            var copyClosed = copyOpen.MakeGenericMethod(elemCs);

            countLocal = il.DeclareLocal(typeof(int));
            var byteLenLocal = il.DeclareLocal(typeof(int));
            ptrLocal = il.DeclareLocal(typeof(int));

            // count = values.Length
            il.Emit(OpCodes.Ldarg, argIdx);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, countLocal);

            // byteLen = count * elemSize
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Ldc_I4, elemSize);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Stloc, byteLenLocal);

            // ptr = instance.CabiRealloc(0, 0, elemSize, byteLen)
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4, elemSize);
            il.Emit(OpCodes.Ldloc, byteLenLocal);
            il.EmitCall(OpCodes.Callvirt, reallocMethod, null);
            il.Emit(OpCodes.Stloc, ptrLocal);

            // ListMarshal.CopyArrayToGuest<T>(values, instance.Memory, ptr)
            il.Emit(OpCodes.Ldarg, argIdx);
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.EmitCall(OpCodes.Callvirt, memoryProp.GetGetMethod()!, null);
            il.Emit(OpCodes.Ldloc, ptrLocal);
            il.EmitCall(OpCodes.Call, copyClosed, null);

            _ = arrayType;
        }

        /// <summary>Byte size of a primitive type's native
        /// little-endian encoding. Used for alignment + byte-len
        /// computation at list-param lower time.</summary>
        private static int PrimByteSize(ComponentPrim p) => p switch
        {
            ComponentPrim.Bool => 1,
            ComponentPrim.S8   => 1,
            ComponentPrim.U8   => 1,
            ComponentPrim.S16  => 2,
            ComponentPrim.U16  => 2,
            ComponentPrim.S32  => 4,
            ComponentPrim.U32  => 4,
            ComponentPrim.F32  => 4,
            ComponentPrim.Char => 4,
            ComponentPrim.S64  => 8,
            ComponentPrim.U64  => 8,
            ComponentPrim.F64  => 8,
            _ => throw new NotSupportedException(
                "PrimByteSize for " + p + " is a follow-up."),
        };

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
            MethodInfo coreMethod, EmittableExport export,
            IReadOnlyList<DefTypeEntry> types)
        {
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "String-returning component requires the core module to "
                    + "declare a memory — Module.Memory accessor is missing.");

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));

            // int P = core.hello(args...);  (args lowered via cabi_realloc if string)
            EmitCoreCall(il, instanceField, coreMethod, export, types);
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
            IReadOnlyList<DefTypeEntry> types, ComponentPrim elemPrim)
        {
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "List-returning component requires the core module to "
                    + "declare a memory — Module.Memory accessor is missing.");

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));

            // int P = core.bytes(args...);
            EmitCoreCall(il, instanceField, coreMethod, export, types);
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
        /// IL body for a <c>() -&gt; list&lt;string&gt;</c> export.
        /// Core returns the retArea pointer P; at P live
        /// (listPtr, count) as two i32s. Each element at
        /// listPtr + i*8 is a (strPtr, strLen) pair — delegated
        /// to <see cref="ListMarshal.LiftStringList"/> which
        /// iterates and calls through the StringMarshal UTF-8
        /// chokepoint per element.
        /// </summary>
        private static void EmitListStringReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            IReadOnlyList<DefTypeEntry> types)
        {
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "list<string>-returning component requires Module.Memory.");

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));

            EmitCoreCall(il, instanceField, coreMethod, export, types);
            il.Emit(OpCodes.Stloc, retAreaLocal);

            il.Emit(OpCodes.Ldsfld, instanceField);
            il.EmitCall(OpCodes.Callvirt, memoryProp.GetGetMethod()!, null);
            il.Emit(OpCodes.Stloc, memoryLocal);

            var bitCToInt32 = typeof(BitConverter).GetMethod(
                "ToInt32", new[] { typeof(byte[]), typeof(int) })!;
            var liftStringList = typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.LiftStringList),
                new[] { typeof(byte[]), typeof(int), typeof(int) })!;

            // ListMarshal.LiftStringList(memory, listPtr, count)
            il.Emit(OpCodes.Ldloc, memoryLocal);             // arg 1
            il.Emit(OpCodes.Ldloc, memoryLocal);             // for BitConv 1
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.EmitCall(OpCodes.Call, bitCToInt32, null);    // listPtr
            il.Emit(OpCodes.Ldloc, memoryLocal);             // for BitConv 2
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Add);
            il.EmitCall(OpCodes.Call, bitCToInt32, null);    // count
            il.EmitCall(OpCodes.Call, liftStringList, null); // string[]
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
            IReadOnlyList<DefTypeEntry> types, ComponentPrim innerPrim)
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
            EmitCoreCall(il, instanceField, coreMethod, export, types);
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
        /// IL body for <c>() -&gt; option&lt;string&gt;</c>.
        /// Layout per canonical-ABI alignment rules: disc byte at
        /// offset 0, payload aligned to 4 (pointer alignment),
        /// payload is (strPtr: i32, strLen: i32) at offsets 4, 8.
        /// C# surface: plain <c>string</c> where <c>null</c> =
        /// None and non-null = Some(value) — wit-bindgen-csharp's
        /// nullable-reference mapping.
        /// </summary>
        private static void EmitOptionStringReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            IReadOnlyList<DefTypeEntry> types)
        {
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "option<string>-returning component requires Module.Memory.");

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));
            var resultLocal = il.DeclareLocal(typeof(string));
            var noneLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            EmitCoreCall(il, instanceField, coreMethod, export, types);
            il.Emit(OpCodes.Stloc, retAreaLocal);

            il.Emit(OpCodes.Ldsfld, instanceField);
            il.EmitCall(OpCodes.Callvirt, memoryProp.GetGetMethod()!, null);
            il.Emit(OpCodes.Stloc, memoryLocal);

            // disc = memory[retArea];  if disc == 0 goto none
            il.Emit(OpCodes.Ldloc, memoryLocal);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Brfalse, noneLabel);

            // Some: StringMarshal.LiftUtf8(memory,
            //    BitConverter.ToInt32(memory, retArea + 4),
            //    BitConverter.ToInt32(memory, retArea + 8))
            var bitCToInt32 = typeof(BitConverter).GetMethod(
                "ToInt32", new[] { typeof(byte[]), typeof(int) })!;
            var liftMethod = typeof(StringMarshal).GetMethod(
                nameof(StringMarshal.LiftUtf8),
                new[] { typeof(byte[]), typeof(int), typeof(int) })!;

            il.Emit(OpCodes.Ldloc, memoryLocal);      // LiftUtf8 arg 1
            il.Emit(OpCodes.Ldloc, memoryLocal);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Add);
            il.EmitCall(OpCodes.Call, bitCToInt32, null);   // strPtr
            il.Emit(OpCodes.Ldloc, memoryLocal);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Add);
            il.EmitCall(OpCodes.Call, bitCToInt32, null);   // strLen
            il.EmitCall(OpCodes.Call, liftMethod, null);    // string
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Br, endLabel);

            // None: result = null
            il.MarkLabel(noneLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, resultLocal);

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
            IReadOnlyList<DefTypeEntry> types,
            ResultSide okSide, ResultSide errSide,
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

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));
            var resultLocal = il.DeclareLocal(tupleType);
            var errLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            EmitCoreCall(il, instanceField, coreMethod, export, types);
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
            if (okSide.Kind != ResultSide.SideKind.Absent)
            {
                il.Emit(OpCodes.Ldloca, resultLocal);
                EmitLoadResultPayload(il, memoryLocal, retAreaLocal,
                                      /*offset*/ 4, okSide);
                il.Emit(OpCodes.Stfld, tupleFields[1]);    // Item2 = ok payload
            }
            il.Emit(OpCodes.Br, endLabel);

            // Err branch: tuple = (false, default(Ok), errPayload)
            il.MarkLabel(errLabel);
            il.Emit(OpCodes.Ldloca, resultLocal);
            il.Emit(OpCodes.Initobj, tupleType);           // ok = false default
            if (errSide.Kind != ResultSide.SideKind.Absent)
            {
                il.Emit(OpCodes.Ldloca, resultLocal);
                EmitLoadResultPayload(il, memoryLocal, retAreaLocal,
                                      /*offset*/ 4, errSide);
                il.Emit(OpCodes.Stfld, tupleFields[2]);    // Item3 = err payload
            }

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        /// <summary>Emit IL that pushes a result's payload onto
        /// the stack at <c>retArea + offset</c>, typed per
        /// <paramref name="side"/>. Primitive payloads use
        /// <see cref="EmitReadPayloadAtOffset"/>; string payloads
        /// read (strPtr, strLen) at the offset and route through
        /// the StringMarshal UTF-8 chokepoint.</summary>
        private static void EmitLoadResultPayload(
            ILGenerator il, LocalBuilder memoryLocal,
            LocalBuilder retAreaLocal, int offset, ResultSide side)
        {
            switch (side.Kind)
            {
                case ResultSide.SideKind.Primitive:
                    EmitReadPayloadAtOffset(il, memoryLocal, retAreaLocal,
                                            offset, side.Prim);
                    return;
                case ResultSide.SideKind.String:
                {
                    var bitCToInt32 = typeof(BitConverter).GetMethod(
                        "ToInt32", new[] { typeof(byte[]), typeof(int) })!;
                    var liftMethod = typeof(StringMarshal).GetMethod(
                        nameof(StringMarshal.LiftUtf8),
                        new[] { typeof(byte[]), typeof(int), typeof(int) })!;
                    // StringMarshal.LiftUtf8(memory,
                    //   BitConverter.ToInt32(memory, retArea + off),
                    //   BitConverter.ToInt32(memory, retArea + off + 4))
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    il.Emit(OpCodes.Ldloc, retAreaLocal);
                    il.Emit(OpCodes.Ldc_I4, offset);
                    il.Emit(OpCodes.Add);
                    il.EmitCall(OpCodes.Call, bitCToInt32, null);
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    il.Emit(OpCodes.Ldloc, retAreaLocal);
                    il.Emit(OpCodes.Ldc_I4, offset + 4);
                    il.Emit(OpCodes.Add);
                    il.EmitCall(OpCodes.Call, bitCToInt32, null);
                    il.EmitCall(OpCodes.Call, liftMethod, null);
                    return;
                }
                default:
                    throw new InvalidOperationException(
                        "Absent result side has no payload to load.");
            }
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
        /// <summary>
        /// IL body for an enum return. Core returns the enum's
        /// integer discriminant on the stack as i32. Cast to the
        /// enum's underlying width (byte/ushort) when narrower
        /// than i32, then return — the CLR treats an enum value
        /// and its underlying integer as bit-identical on the
        /// evaluation stack, so no explicit "boxed enum" step is
        /// needed.
        /// </summary>
        private static void EmitEnumReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            IReadOnlyList<DefTypeEntry> types, Type returnType,
            int caseCount)
        {
            EmitCoreCall(il, instanceField, coreMethod, export, types);
            // Narrow the i32 to the enum's underlying width when
            // smaller. ≤256 cases → byte → Conv_U1; ≤65536 →
            // ushort → Conv_U2; else uint → no conversion needed.
            if (caseCount <= 256)
                il.Emit(OpCodes.Conv_U1);
            else if (caseCount <= 65536)
                il.Emit(OpCodes.Conv_U2);
            il.Emit(OpCodes.Ret);
            _ = returnType;   // type-check happens at the method signature
        }

        /// <summary>
        /// IL body for a flags return. Same wire as enum (small
        /// integer holding the bitmask); narrow per the canonical-
        /// ABI width rule (≤8 flags → u8, ≤16 → u16, ≤32 → u32).
        /// </summary>
        private static void EmitFlagsReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            IReadOnlyList<DefTypeEntry> types, Type returnType,
            int flagCount)
        {
            EmitCoreCall(il, instanceField, coreMethod, export, types);
            if (flagCount <= 8)
                il.Emit(OpCodes.Conv_U1);
            else if (flagCount <= 16)
                il.Emit(OpCodes.Conv_U2);
            il.Emit(OpCodes.Ret);
            _ = returnType;
        }

        /// <summary>
        /// IL body for a record return. Core returns the retArea
        /// pointer P; each field lives at its naturally-aligned
        /// offset starting from P. Read each field, push them on
        /// the stack in declaration order, and call the generated
        /// record constructor. v0 restricts to all-primitive
        /// records; aggregate fields land as their marshaling
        /// helpers fill in.
        /// </summary>
        private static void EmitRecordReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            IReadOnlyList<DefTypeEntry> types, Type recordType,
            ComponentPrim[] fieldPrims)
        {
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "Record-returning component requires Module.Memory.");

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));

            EmitCoreCall(il, instanceField, coreMethod, export, types);
            il.Emit(OpCodes.Stloc, retAreaLocal);

            il.Emit(OpCodes.Ldsfld, instanceField);
            il.EmitCall(OpCodes.Callvirt, memoryProp.GetGetMethod()!, null);
            il.Emit(OpCodes.Stloc, memoryLocal);

            // Field offsets follow canonical-ABI alignment: each
            // field starts at the next multiple of its own
            // alignment from the running offset.
            int offset = 0;
            for (int i = 0; i < fieldPrims.Length; i++)
            {
                var align = PrimByteSize(fieldPrims[i]);
                offset = AlignUp(offset, align);
                EmitReadPayloadAtOffset(il, memoryLocal, retAreaLocal,
                                        offset, fieldPrims[i]);
                offset += PrimByteSize(fieldPrims[i]);
            }

            // Call ctor matching the field types in order.
            var ctorParams = new Type[fieldPrims.Length];
            for (int i = 0; i < fieldPrims.Length; i++)
                ctorParams[i] = PrimToCs(fieldPrims[i]);
            var ctor = recordType.GetConstructor(ctorParams)!;
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);
        }

        private static int AlignUp(int offset, int alignment)
        {
            var rem = offset % alignment;
            return rem == 0 ? offset : offset + (alignment - rem);
        }

        /// <summary>
        /// IL body for an <c>own&lt;R&gt;</c> return. Core
        /// returns the handle as i32; the C# surface wraps that
        /// in <c>new R(handle)</c>. Ownership semantics — the
        /// host now owns the handle and is responsible for
        /// dropping it via the resource-drop core export — are
        /// surfaced through a follow-up <see cref="System.IDisposable"/>
        /// implementation on the resource class.
        /// </summary>
        private static void EmitOwnReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            IReadOnlyList<DefTypeEntry> types, Type resourceType)
        {
            EmitCoreCall(il, instanceField, coreMethod, export, types);
            // Wrap the i32 handle in `new ResourceClass(handle)`.
            var ctor = resourceType.GetConstructor(new[] { typeof(int) });
            if (ctor == null)
                throw new InvalidOperationException(
                    "Resource class missing (int) constructor for "
                    + export.Name + ".");
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// IL body for a variant return. Reads the disc byte at
        /// retArea, then for each payload-bearing case reads its
        /// payload at the variant payload offset (max alignment
        /// of payload types, applied to disc-width). All payload
        /// reads happen unconditionally — the cases that don't
        /// match the disc just produce default values that the
        /// constructor stores into their fields anyway. The cost
        /// of an always-unused field load is negligible vs. a
        /// per-case branch table; the user dispatches on Tag at
        /// the C# level.
        /// </summary>
        private static void EmitVariantReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            IReadOnlyList<DefTypeEntry> types, Type variantType,
            VariantShape shape)
        {
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "Variant-returning component requires Module.Memory.");

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));

            EmitCoreCall(il, instanceField, coreMethod, export, types);
            il.Emit(OpCodes.Stloc, retAreaLocal);

            il.Emit(OpCodes.Ldsfld, instanceField);
            il.EmitCall(OpCodes.Callvirt, memoryProp.GetGetMethod()!, null);
            il.Emit(OpCodes.Stloc, memoryLocal);

            // Discriminant width tracks case count.
            var caseCount = shape.CaseNames.Count;
            var discWidth = caseCount <= 256 ? 1
                : caseCount <= 65536 ? 2 : 4;
            // Payload alignment: max alignment over all
            // payload-bearing cases; 1 if none.
            int payloadAlign = 1;
            foreach (var p in shape.CasePayloadPrims)
                if (p.HasValue)
                    payloadAlign = System.Math.Max(payloadAlign,
                        PrimByteSize(p.Value));
            var payloadOffset = AlignUp(discWidth, payloadAlign);

            // Push tag (read disc byte, narrow as needed).
            il.Emit(OpCodes.Ldloc, memoryLocal);
            il.Emit(OpCodes.Ldloc, retAreaLocal);
            if (discWidth == 1)
            {
                il.Emit(OpCodes.Ldelem_U1);
            }
            else
            {
                var bitC = typeof(BitConverter).GetMethod(
                    discWidth == 2 ? "ToUInt16" : "ToUInt32",
                    new[] { typeof(byte[]), typeof(int) })!;
                il.EmitCall(OpCodes.Call, bitC, null);
            }

            // For each payload-bearing case, read its payload
            // value at the shared payload offset. Order matches
            // the variant's case-order in the binary which the
            // ctor expects.
            for (int i = 0; i < caseCount; i++)
            {
                if (!shape.CasePayloadPrims[i].HasValue) continue;
                EmitReadPayloadAtOffset(il, memoryLocal, retAreaLocal,
                    payloadOffset, shape.CasePayloadPrims[i]!.Value);
            }

            // new VariantClass(tag, payload0, payload1, …)
            var ctorParamTypes = new List<Type>();
            ctorParamTypes.Add(discWidth == 1 ? typeof(byte)
                : discWidth == 2 ? typeof(ushort)
                : typeof(uint));
            for (int i = 0; i < caseCount; i++)
                if (shape.CasePayloadPrims[i].HasValue)
                    ctorParamTypes.Add(PrimToCs(shape.CasePayloadPrims[i]!.Value));
            var ctor = variantType.GetConstructor(ctorParamTypes.ToArray());
            if (ctor == null)
                throw new InvalidOperationException(
                    "Variant class missing matching constructor for "
                    + export.Name + " — emitter parameter list must "
                    + "align with EmitVariantType's ctor signature.");
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Resolve the C# return type for an enum export. When
        /// <paramref name="decodedWit"/> is available and names the
        /// export's result type, look up the pre-emitted enum Type
        /// in <paramref name="emittedTypes"/>. Otherwise fall back
        /// to the underlying integer matching the discriminant
        /// width — sufficient for callers that don't need the
        /// named enum surface.
        /// </summary>
        private static Type ResolveEnumReturnType(
            CtPackage? decodedWit, Dictionary<string, Type> emittedTypes,
            string exportName, int caseCount)
        {
            var named = TryFindNamedReturn(decodedWit, emittedTypes, exportName);
            if (named != null) return named;
            return caseCount <= 256 ? typeof(byte)
                : caseCount <= 65536 ? typeof(ushort)
                : typeof(uint);
        }

        /// <summary>Resolve the C# return type for a flags export
        /// — same shape as enum but with [Flags]-attributed
        /// underlying. Falls back to the bitmask-width integer
        /// type when the WIT decoder hasn't named it.</summary>
        private static Type ResolveFlagsReturnType(
            CtPackage? decodedWit, Dictionary<string, Type> emittedTypes,
            string exportName, int flagCount)
        {
            var named = TryFindNamedReturn(decodedWit, emittedTypes, exportName);
            if (named != null) return named;
            return flagCount <= 8 ? typeof(byte)
                : flagCount <= 16 ? typeof(ushort)
                : typeof(uint);
        }

        /// <summary>Look up the named C# Type bound to an
        /// export's return type via the decoded WIT. Recognizes
        /// direct CtTypeRef as well as CtOwnType /
        /// CtBorrowType-wrapped refs (resource handles are still
        /// surfaced by the underlying resource's name).</summary>
        private static Type? TryFindNamedReturn(
            CtPackage? decodedWit, Dictionary<string, Type> emittedTypes,
            string exportName)
        {
            if (decodedWit == null) return null;
            foreach (var world in decodedWit.Worlds)
                foreach (var ex in world.Exports)
                {
                    if (ex.Name != exportName) continue;
                    if (!(ex.Spec is CtExternFunc fn)) continue;
                    var refName = NamedTypeFor(fn.Function.Result);
                    if (refName != null
                        && emittedTypes.TryGetValue(refName, out var t))
                        return t;
                }
            return null;
        }

        private static string? NamedTypeFor(CtValType? v) => v switch
        {
            CtTypeRef r => r.Name,
            CtOwnType own => NamedTypeFor(own.Resource),
            CtBorrowType bo => NamedTypeFor(bo.Resource),
            _ => null,
        };

        private static void EmitTupleReturnBody(
            ILGenerator il, FieldBuilder instanceField,
            MethodInfo coreMethod, EmittableExport export,
            IReadOnlyList<DefTypeEntry> types,
            ComponentPrim[] elementPrims, Type tupleType)
        {
            var memoryProp = instanceField.FieldType.GetProperty("Memory");
            if (memoryProp == null)
                throw new InvalidOperationException(
                    "Tuple-returning component requires Module.Memory.");

            var retAreaLocal = il.DeclareLocal(typeof(int));
            var memoryLocal = il.DeclareLocal(typeof(byte[]));
            var resultLocal = il.DeclareLocal(tupleType);

            EmitCoreCall(il, instanceField, coreMethod, export, types);
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
            // Try progressively looser matches — the core method
            // name has been through the transpiler's
            // <c>SanitizeName</c> (hyphen → underscore) while the
            // component export name is the raw WIT identifier,
            // so exact equality doesn't always hold.
            var sanitized = SanitizeExportName(exportName);
            foreach (var m in iExports.GetMethods())
            {
                if (string.Equals(m.Name, exportName,
                        StringComparison.OrdinalIgnoreCase))
                    return m;
                if (string.Equals(m.Name, sanitized,
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

        /// <summary>Replicates the transpiler's
        /// <c>InterfaceGenerator.SanitizeName</c> — replaces any
        /// non-identifier char with <c>_</c>. Used to bridge
        /// component-level export names (which may carry hyphens)
        /// back to the core IExports method the transpiler
        /// already generated under the sanitized spelling.</summary>
        private static string SanitizeExportName(string wasmName)
        {
            if (string.IsNullOrEmpty(wasmName)) return wasmName;
            var chars = wasmName.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                    chars[i] = '_';
            }
            return new string(chars);
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
