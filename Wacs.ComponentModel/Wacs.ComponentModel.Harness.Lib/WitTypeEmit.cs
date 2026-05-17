// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.ComponentModel.CSharpEmit;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.Harness.Lib
{
    /// <summary>
    /// Emits CLR types for WIT user-defined types — records as sealed
    /// classes with readonly properties + a positional ctor, variants
    /// as abstract bases + nested sealed per-case subclasses. Owned by
    /// the harness emitter and shared with the lift-IL emitter: the
    /// <see cref="TypeRegistry"/> maps named WIT types to the
    /// <see cref="TypeBuilder"/> emitted here so per-export bodies can
    /// reference them without name lookups.
    ///
    /// <para>v0.2 surface — primitives + records of primitives +
    /// variants of {unit, primitive, record} cases. Strings inside
    /// records / variants, lists, options, results, tuples, flags,
    /// enums, and resources throw at emit time.</para>
    /// </summary>
    internal static class WitTypeEmit
    {
        /// <summary>
        /// Define a CLR class for each user-defined WIT type the world
        /// references. Returns a populated <see cref="TypeRegistry"/>.
        /// Two passes: first define the shells (so cross-type
        /// references like variant payloads pointing at records can
        /// resolve), then fill in members.
        /// </summary>
        public static TypeRegistry EmitWorldTypes(
            ModuleBuilder module, CtWorldType world, HarnessOptions opts)
        {
            var registry = new TypeRegistry();

            // Collect every named type the harness needs to emit, paired
            // with the C# namespace it goes in: world types live at the
            // world namespace; interface-export types live at
            // <world-namespace>.<interface-segment> (one Pascal token per
            // interface — WacsCliRun, WasiIoStreams).
            var allTypes = EnumerateAllTypes(world, opts).ToList();

            // Pass 1: define the type shells so forward references
            // (variant payload → record, etc.) have something to bind
            // to. Enums + flags emit eagerly as complete enum types
            // (no payloads, no forward refs to chase).
            foreach (var (named, ns) in allTypes)
            {
                var structural = CanonicalAbi.Deref(named.Type);
                switch (structural)
                {
                    case CtRecordType rec:
                        registry.Records[rec] = module.DefineType(
                            $"{ns}.{NameMangler.ToPascalCase(named.Name)}",
                            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
                        break;
                    case CtVariantType variant:
                        registry.Variants[variant] = module.DefineType(
                            $"{ns}.{NameMangler.ToPascalCase(named.Name)}",
                            TypeAttributes.Public | TypeAttributes.Abstract);
                        break;
                    case CtEnumType en:
                        registry.Enums[en] = EmitEnumType(module, ns, named.Name, en);
                        break;
                    case CtResourceType res:
                        registry.Resources[res] = module.DefineType(
                            $"{ns}.{NameMangler.ToPascalCase(named.Name)}",
                            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
                        break;
                    case CtFlagsType fl:
                        registry.Flags[fl] = EmitFlagsType(module, ns, named.Name, fl);
                        break;
                    case CtPrimType:
                        // Type alias to a primitive — no CLR type to
                        // emit; lookups go straight to the primitive
                        // CLR type via MapClrType's prim case.
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Harness emitter v0.2 doesn't emit type '{named.Name}' of kind {structural.GetType().Name}.");
                }
            }

            // Pass 2: fill in record fields + variant case hierarchies.
            foreach (var (named, _) in allTypes)
            {
                var structural = CanonicalAbi.Deref(named.Type);
                if (structural is CtRecordType rec)
                    PopulateRecord(registry.Records[rec], rec, registry);
                else if (structural is CtVariantType variant)
                    PopulateVariant(registry.Variants[variant], variant, registry);
                else if (structural is CtResourceType res)
                    PopulateResource(registry.Resources[res], res, registry);
            }

            // Pass 3: finalize the variant case subclasses (CreateType
            // on inner-most first), then variant bases, then records,
            // then resources.
            foreach (var (_, subs) in registry.VariantCases)
                foreach (var sub in subs.Values)
                    sub.CreateType();
            foreach (var v in registry.Variants.Values)
                v.CreateType();
            foreach (var r in registry.Records.Values)
                r.CreateType();
            foreach (var r in registry.Resources.Values)
                r.CreateType();

            return registry;
        }

        /// <summary>
        /// Emit a resource class: sealed, IDisposable, with internal
        /// <c>_handle</c> + <c>_drop</c> fields and a public Dispose()
        /// that calls drop and zeros the handle. The (handle, drop)
        /// ctor is internal — only the harness's wrapper IL constructs
        /// resource instances (e.g., when lifting a return).
        /// </summary>
        private static void PopulateResource(TypeBuilder tb, CtResourceType res, TypeRegistry registry)
        {
            tb.AddInterfaceImplementation(typeof(IDisposable));

            var handleField = tb.DefineField("_handle", typeof(int),
                FieldAttributes.Private);
            var dropField = tb.DefineField("_drop", typeof(Action<int>),
                FieldAttributes.Private | FieldAttributes.InitOnly);
            registry.ResourceHandleFields[res] = handleField;

            // Internal ctor (int handle, Action<int> drop)
            var ctor = tb.DefineConstructor(
                MethodAttributes.Assembly | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                CallingConventions.HasThis,
                new[] { typeof(int), typeof(Action<int>) });
            var cil = ctor.GetILGenerator();
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Ldarg_1);
            cil.Emit(OpCodes.Stfld, handleField);
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Ldarg_2);
            cil.Emit(OpCodes.Stfld, dropField);
            cil.Emit(OpCodes.Ret);
            registry.ResourceCtors[res] = ctor;

            // public void Dispose() {
            //     if (_handle != 0) _drop(_handle);
            //     _handle = 0;
            // }
            var dispose = tb.DefineMethod("Dispose",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig
                    | MethodAttributes.NewSlot | MethodAttributes.Final,
                typeof(void), Type.EmptyTypes);
            var dil = dispose.GetILGenerator();
            var skipDrop = dil.DefineLabel();
            // if (_handle != 0)
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Ldfld, handleField);
            dil.Emit(OpCodes.Brfalse, skipDrop);
            // _drop(_handle);
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Ldfld, dropField);
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Ldfld, handleField);
            dil.Emit(OpCodes.Callvirt, typeof(Action<int>).GetMethod("Invoke")!);
            dil.MarkLabel(skipDrop);
            // _handle = 0;
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Ldc_I4_0);
            dil.Emit(OpCodes.Stfld, handleField);
            dil.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(dispose, typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!);

            // public int Handle => _handle;  (read-only; needed for
            // lower IL to extract the handle when passing as an arg)
            var handleProp = tb.DefineProperty("Handle", PropertyAttributes.None,
                typeof(int), null);
            var getter = tb.DefineMethod("get_Handle",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                typeof(int), Type.EmptyTypes);
            var gil = getter.GetILGenerator();
            gil.Emit(OpCodes.Ldarg_0);
            gil.Emit(OpCodes.Ldfld, handleField);
            gil.Emit(OpCodes.Ret);
            handleProp.SetGetMethod(getter);
        }

        /// <summary>
        /// Enumerate every named type the harness needs to emit, paired
        /// with the C# namespace it should live in. World-level types
        /// land at <see cref="HarnessOptions.Namespace"/>; interface-
        /// export types land at <c>{Namespace}.{InterfaceSegment}</c>.
        /// </summary>
        internal static IEnumerable<(CtNamedType Named, string Namespace)> EnumerateAllTypes(
            CtWorldType world, HarnessOptions opts)
        {
            foreach (var t in world.Types)
                yield return (t, opts.Namespace);

            foreach (var port in world.Exports)
            {
                CtInterfaceType? iface = port.Spec switch
                {
                    CtExternInterfaceRef iref => iref.Target,
                    CtExternInlineInterface inline => inline.Interface,
                    _ => null,
                };
                if (iface == null) continue;
                var ifaceNs = $"{opts.Namespace}.{HarnessNaming.InterfaceSegment(iface)}";
                foreach (var t in iface.Types)
                    yield return (t, ifaceNs);
            }
        }

        /// <summary>
        /// Emit a CLR enum type for a WIT <c>enum</c> declaration.
        /// Backing storage width (byte / ushort / uint) is sized to
        /// the case count per the canonical-ABI discriminant rule —
        /// matches what the lift IL will read from memory.
        /// </summary>
        private static Type EmitEnumType(
            ModuleBuilder module, string @namespace, string witName, CtEnumType en)
        {
            var underlying = EnumUnderlyingForCases(en.Cases.Count);
            var typeName = $"{@namespace}.{NameMangler.ToPascalCase(witName)}";
            var eb = module.DefineEnum(typeName, TypeAttributes.Public, underlying);
            for (int i = 0; i < en.Cases.Count; i++)
            {
                object value = underlying == typeof(byte) ? (object)(byte)i
                    : underlying == typeof(ushort) ? (object)(ushort)i
                    : (object)(uint)i;
                eb.DefineLiteral(NameMangler.ToPascalCase(en.Cases[i]), value);
            }
            return eb.CreateType()!;
        }

        /// <summary>
        /// Emit a CLR <c>[Flags]</c> enum for a WIT <c>flags</c>
        /// declaration. Each flag is a 1-bit literal at bit position
        /// matching its declaration order; backing width is byte /
        /// ushort / uint sized to the flag count (≤ 8 / ≤ 16 / ≤ 32).
        /// </summary>
        private static Type EmitFlagsType(
            ModuleBuilder module, string @namespace, string witName, CtFlagsType fl)
        {
            var width = CanonicalAbi.FlagsByteWidth(fl.Flags.Count);
            Type underlying = width == 1 ? typeof(byte) : width == 2 ? typeof(ushort) : typeof(uint);
            var typeName = $"{@namespace}.{NameMangler.ToPascalCase(witName)}";
            var eb = module.DefineEnum(typeName, TypeAttributes.Public, underlying);

            var flagsAttrCtor = typeof(FlagsAttribute).GetConstructor(Type.EmptyTypes)!;
            eb.SetCustomAttribute(new CustomAttributeBuilder(flagsAttrCtor, Array.Empty<object>()));

            for (int i = 0; i < fl.Flags.Count; i++)
            {
                object value = underlying == typeof(byte) ? (object)(byte)(1 << i)
                    : underlying == typeof(ushort) ? (object)(ushort)(1 << i)
                    : (object)(uint)(1u << i);
                eb.DefineLiteral(NameMangler.ToPascalCase(fl.Flags[i]), value);
            }
            return eb.CreateType()!;
        }

        private static Type EnumUnderlyingForCases(int count) =>
            count <= 256 ? typeof(byte)
            : count <= 65536 ? typeof(ushort)
            : typeof(uint);

        private static void PopulateRecord(TypeBuilder tb, CtRecordType rec, TypeRegistry registry)
        {
            var fieldClrTypes = new Type[rec.Fields.Count];
            var fields = new FieldBuilder[rec.Fields.Count];
            var getters = new Dictionary<string, MethodBuilder>();

            // Define a private readonly backing field + public read-only
            // property for each WIT field.
            for (int i = 0; i < rec.Fields.Count; i++)
            {
                var f = rec.Fields[i];
                var clr = MapClrType(f.Type, registry, $"field '{f.Name}' of record '{rec.Name}'");
                fieldClrTypes[i] = clr;

                var backing = tb.DefineField(
                    "_" + NameMangler.ToCamelCase(f.Name),
                    clr,
                    FieldAttributes.Private | FieldAttributes.InitOnly);
                fields[i] = backing;

                var propName = NameMangler.ToPascalCase(f.Name);
                var prop = tb.DefineProperty(propName, PropertyAttributes.None, clr, null);
                var getter = tb.DefineMethod("get_" + propName,
                    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                    clr, Type.EmptyTypes);
                var gil = getter.GetILGenerator();
                gil.Emit(OpCodes.Ldarg_0);
                gil.Emit(OpCodes.Ldfld, backing);
                gil.Emit(OpCodes.Ret);
                prop.SetGetMethod(getter);
                getters[f.Name] = getter;
            }
            registry.RecordGetters[rec] = getters;

            // Positional ctor: takes all fields, assigns backings.
            var ctor = tb.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                CallingConventions.HasThis,
                fieldClrTypes);
            var cil = ctor.GetILGenerator();
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            for (int i = 0; i < rec.Fields.Count; i++)
            {
                cil.Emit(OpCodes.Ldarg_0);
                EmitLdarg(cil, i + 1);
                cil.Emit(OpCodes.Stfld, fields[i]);
            }
            cil.Emit(OpCodes.Ret);
            registry.RecordCtors[rec] = ctor;
        }

        private static void PopulateVariant(TypeBuilder tb, CtVariantType variant, TypeRegistry registry)
        {
            // Abstract base — protected parameterless ctor (so subclasses can chain).
            var baseCtor = tb.DefineConstructor(
                MethodAttributes.Family | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                CallingConventions.HasThis,
                Type.EmptyTypes);
            var bil = baseCtor.GetILGenerator();
            bil.Emit(OpCodes.Ldarg_0);
            bil.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            bil.Emit(OpCodes.Ret);

            // Per-case: a nested sealed subclass.
            var subs = new Dictionary<string, TypeBuilder>();
            var subCtors = new Dictionary<string, ConstructorBuilder>();
            foreach (var c in variant.Cases)
            {
                var caseName = NameMangler.ToPascalCase(c.Name);
                var subBuilder = tb.DefineNestedType(
                    caseName,
                    TypeAttributes.NestedPublic | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                    tb);
                subs[c.Name] = subBuilder;

                if (c.Payload == null)
                {
                    // Parameterless ctor → base ctor → ret.
                    var ctor = subBuilder.DefineConstructor(
                        MethodAttributes.Public | MethodAttributes.HideBySig
                            | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                        CallingConventions.HasThis,
                        Type.EmptyTypes);
                    var il = ctor.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, baseCtor);
                    il.Emit(OpCodes.Ret);
                    subCtors[c.Name] = ctor;
                }
                else
                {
                    // Single readonly `Value` property + ctor taking payload.
                    var payloadClr = MapClrType(c.Payload, registry,
                        $"case '{c.Name}' of variant '{variant.Name}'");
                    var backing = subBuilder.DefineField("_value", payloadClr,
                        FieldAttributes.Private | FieldAttributes.InitOnly);

                    var prop = subBuilder.DefineProperty("Value", PropertyAttributes.None,
                        payloadClr, null);
                    var getter = subBuilder.DefineMethod("get_Value",
                        MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                        payloadClr, Type.EmptyTypes);
                    var gil = getter.GetILGenerator();
                    gil.Emit(OpCodes.Ldarg_0);
                    gil.Emit(OpCodes.Ldfld, backing);
                    gil.Emit(OpCodes.Ret);
                    prop.SetGetMethod(getter);

                    var ctor = subBuilder.DefineConstructor(
                        MethodAttributes.Public | MethodAttributes.HideBySig
                            | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                        CallingConventions.HasThis,
                        new[] { payloadClr });
                    var il = ctor.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, baseCtor);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Stfld, backing);
                    il.Emit(OpCodes.Ret);
                    subCtors[c.Name] = ctor;
                }
            }
            registry.VariantCases[variant] = subs;
            registry.VariantCaseCtors[variant] = subCtors;
        }

        /// <summary>
        /// Map a <see cref="CtValType"/> to the CLR type the harness
        /// surface exposes. Primitives → CLR primitives, named records
        /// → their emitted TypeBuilder, named variants → their emitted
        /// abstract base TypeBuilder. Strings / lists / etc. throw —
        /// not implemented in v0.2.
        /// </summary>
        public static Type MapClrType(CtValType t, TypeRegistry registry, string context)
        {
            var deref = CanonicalAbi.Deref(t);
            switch (deref)
            {
                case CtPrimType prim:
                    return prim.Kind switch
                    {
                        CtPrim.Bool => typeof(bool),
                        CtPrim.S8 => typeof(sbyte),
                        CtPrim.U8 => typeof(byte),
                        CtPrim.S16 => typeof(short),
                        CtPrim.U16 => typeof(ushort),
                        CtPrim.S32 => typeof(int),
                        CtPrim.U32 => typeof(uint),
                        CtPrim.S64 => typeof(long),
                        CtPrim.U64 => typeof(ulong),
                        CtPrim.F32 => typeof(float),
                        CtPrim.F64 => typeof(double),
                        CtPrim.Char => typeof(char),
                        CtPrim.String => typeof(string),
                        _ => throw new NotSupportedException($"Unhandled primitive {prim.Kind} ({context})."),
                    };

                case CtRecordType rec:
                    if (registry.Records.TryGetValue(rec, out var recBuilder))
                        return recBuilder;
                    throw new NotSupportedException(
                        $"Anonymous record types not supported in v0.2 ({context}).");

                case CtVariantType variant:
                    if (registry.Variants.TryGetValue(variant, out var varBuilder))
                        return varBuilder;
                    throw new NotSupportedException(
                        $"Anonymous variant types not supported in v0.2 ({context}).");

                case CtListType list:
                    // list<T> exposes as T[] — most direct CLR shape
                    // for the canonical-ABI "contiguous array of
                    // element-typed slots" model. IReadOnlyList<T>
                    // would carry better immutability semantics but
                    // arrays let the lift emit a single newarr +
                    // stelem loop without an extra wrapper.
                    var elem = MapClrType(list.Element, registry, $"{context} list element");
                    return elem.MakeArrayType();

                case CtEnumType en:
                    if (registry.Enums.TryGetValue(en, out var enType))
                        return enType;
                    throw new NotSupportedException(
                        $"Anonymous enum types not supported in v0.2 ({context}).");

                case CtFlagsType fl:
                    if (registry.Flags.TryGetValue(fl, out var flType))
                        return flType;
                    throw new NotSupportedException(
                        $"Anonymous flag types not supported in v0.2 ({context}).");

                case CtResourceType res:
                    if (registry.Resources.TryGetValue(res, out var resBuilder))
                        return resBuilder;
                    throw new InvalidOperationException(
                        $"Unregistered resource '{res.Name}' ({context}).");

                case CtOwnType own:
                    return MapClrType(own.Resource, registry, $"{context} own<R>");

                case CtBorrowType brw:
                    return MapClrType(brw.Resource, registry, $"{context} borrow<R>");

                case CtOptionType opt:
                    // option<T>:
                    //   - T is a CLR value type (int, enum, struct) →
                    //     Nullable<T>; HasValue maps to the some/none
                    //     disc.
                    //   - T is a reference type (string, class, array)
                    //     → T itself with null carrying the "none"
                    //     signal. Saves the Nullable<T> wrapper for
                    //     types that already have a null sentinel.
                    var innerClr = MapClrType(opt.Inner, registry, $"{context} option<T>");
                    return innerClr.IsValueType
                        ? typeof(System.Nullable<>).MakeGenericType(innerClr)
                        : innerClr;

                case CtResultType res:
                    {
                        // Elided sides → System.ValueTuple (empty struct).
                        var okClr = res.Ok == null
                            ? typeof(System.ValueTuple)
                            : MapClrType(res.Ok, registry, $"{context} result ok");
                        var errClr = res.Err == null
                            ? typeof(System.ValueTuple)
                            : MapClrType(res.Err, registry, $"{context} result err");
                        return typeof(Wacs.ComponentModel.Harness.WitResult<,>)
                            .MakeGenericType(okClr, errClr);
                    }

                case CtTupleType tup:
                    {
                        if (tup.Elements.Count < 1 || tup.Elements.Count > 7)
                            throw new NotSupportedException(
                                $"Harness emitter v1 supports tuple arities 1..7 (got {tup.Elements.Count}, {context}).");
                        var elemClrs = new Type[tup.Elements.Count];
                        for (int i = 0; i < tup.Elements.Count; i++)
                            elemClrs[i] = MapClrType(tup.Elements[i], registry, $"{context} tuple element {i}");
                        return tup.Elements.Count switch
                        {
                            1 => typeof(ValueTuple<>).MakeGenericType(elemClrs),
                            2 => typeof(ValueTuple<,>).MakeGenericType(elemClrs),
                            3 => typeof(ValueTuple<,,>).MakeGenericType(elemClrs),
                            4 => typeof(ValueTuple<,,,>).MakeGenericType(elemClrs),
                            5 => typeof(ValueTuple<,,,,>).MakeGenericType(elemClrs),
                            6 => typeof(ValueTuple<,,,,,>).MakeGenericType(elemClrs),
                            7 => typeof(ValueTuple<,,,,,,>).MakeGenericType(elemClrs),
                            _ => throw new NotSupportedException(),
                        };
                    }

                default:
                    throw new NotSupportedException(
                        $"Harness emitter v0.2 does not yet support {deref.GetType().Name} ({context}).");
            }
        }

        private static void EmitLdarg(ILGenerator il, int idx)
        {
            switch (idx)
            {
                case 0: il.Emit(OpCodes.Ldarg_0); break;
                case 1: il.Emit(OpCodes.Ldarg_1); break;
                case 2: il.Emit(OpCodes.Ldarg_2); break;
                case 3: il.Emit(OpCodes.Ldarg_3); break;
                default:
                    if (idx <= byte.MaxValue) il.Emit(OpCodes.Ldarg_S, (byte)idx);
                    else il.Emit(OpCodes.Ldarg, (short)idx);
                    break;
            }
        }
    }

    /// <summary>
    /// Maps WIT named types to their emitted CLR <see cref="TypeBuilder"/>
    /// plus the constructor / getter <see cref="MethodBase"/> tokens
    /// that lift / wrapper IL needs to reference. Owned by
    /// <see cref="WitTypeEmit"/>; consumed by lift / lower IL emitters
    /// — the stashed refs survive cross-type calls without TypeBuilder
    /// reflection-after-bake gymnastics.
    /// </summary>
    internal sealed class TypeRegistry
    {
        // All registries key by the structural type reference (not
        // by WIT name string). The parser creates one structural
        // type per WIT declaration, so reference identity gives us
        // collision-free keys even when two interfaces both declare
        // a type called "error".
        public Dictionary<CtRecordType, TypeBuilder> Records { get; } = new();
        public Dictionary<CtRecordType, ConstructorBuilder> RecordCtors { get; } = new();
        public Dictionary<CtRecordType, Dictionary<string, MethodBuilder>> RecordGetters { get; } = new();
        public Dictionary<CtVariantType, TypeBuilder> Variants { get; } = new();
        public Dictionary<CtVariantType, Dictionary<string, TypeBuilder>> VariantCases { get; } = new();
        public Dictionary<CtVariantType, Dictionary<string, ConstructorBuilder>> VariantCaseCtors { get; } = new();
        // Enums + flags emit eagerly to full Types (no forward refs);
        // stored as runtime System.Type rather than TypeBuilder.
        public Dictionary<CtEnumType, Type> Enums { get; } = new();
        public Dictionary<CtFlagsType, Type> Flags { get; } = new();
        // Resources emit as sealed CLR classes implementing IDisposable.
        // The (int handle, Action<int> drop) ctor is the only way to
        // construct one — lift sites pass the resource's drop delegate
        // held by the harness instance.
        public Dictionary<CtResourceType, TypeBuilder> Resources { get; } = new();
        // Per-resource ctor: (int handle, Action<int> drop) → resource.
        public Dictionary<CtResourceType, ConstructorBuilder> ResourceCtors { get; } = new();
        // Per-resource _handle field (so lift/lower can read/write it).
        public Dictionary<CtResourceType, FieldBuilder> ResourceHandleFields { get; } = new();
        // Per-resource Action<int> drop field on the harness class.
        // Wrapper IL that lifts a resource return pushes this field
        // before newobj-ing the resource class.
        public Dictionary<CtResourceType, FieldBuilder> HarnessDropFields { get; } = new();
    }
}
