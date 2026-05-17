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

            // Pass 1: define the type shells so forward references
            // (variant payload → record, etc.) have something to bind to.
            // Enums + flags emit eagerly as complete enum types (no
            // payloads, no forward refs to chase) so they're added
            // straight into the registry as runtime types.
            foreach (var named in world.Types)
            {
                var structural = CanonicalAbi.Deref(named.Type);
                switch (structural)
                {
                    case CtRecordType rec:
                        var recName = $"{opts.Namespace}.{NameMangler.ToPascalCase(named.Name)}";
                        registry.Records[named.Name] = module.DefineType(
                            recName,
                            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
                        break;
                    case CtVariantType variant:
                        var varName = $"{opts.Namespace}.{NameMangler.ToPascalCase(named.Name)}";
                        registry.Variants[named.Name] = module.DefineType(
                            varName,
                            TypeAttributes.Public | TypeAttributes.Abstract);
                        break;
                    case CtEnumType en:
                        registry.Enums[named.Name] = EmitEnumType(module, opts.Namespace, named.Name, en);
                        break;
                    case CtFlagsType fl:
                        registry.Flags[named.Name] = EmitFlagsType(module, opts.Namespace, named.Name, fl);
                        break;
                    case CtPrimType:
                        // Type alias to a primitive — no CLR type to emit; lookups go straight to the primitive CLR type.
                        registry.PrimitiveAliases[named.Name] = structural;
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Harness emitter v0.2 doesn't emit type '{named.Name}' of kind {structural.GetType().Name}.");
                }
            }

            // Pass 2: fill in record fields + variant case hierarchies.
            foreach (var named in world.Types)
            {
                var structural = CanonicalAbi.Deref(named.Type);
                if (structural is CtRecordType rec)
                    PopulateRecord(registry.Records[named.Name], rec, registry);
                else if (structural is CtVariantType variant)
                    PopulateVariant(registry.Variants[named.Name], variant, registry);
            }

            // Pass 3: finalize the variant case subclasses (CreateType
            // on inner-most first), then variant bases, then records.
            // Inner sealed subclasses must finalize before their parent.
            foreach (var (_, subs) in registry.VariantCases)
                foreach (var sub in subs.Values)
                    sub.CreateType();
            foreach (var v in registry.Variants.Values)
                v.CreateType();
            foreach (var r in registry.Records.Values)
                r.CreateType();

            return registry;
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
            registry.RecordGetters[rec.Name] = getters;

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
            registry.RecordCtors[rec.Name] = ctor;
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
            registry.VariantCases[variant.Name] = subs;
            registry.VariantCaseCtors[variant.Name] = subCtors;
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
                    if (registry.Records.TryGetValue(rec.Name, out var recBuilder))
                        return recBuilder;
                    throw new NotSupportedException(
                        $"Anonymous record types not supported in v0.2 ({context}).");

                case CtVariantType variant:
                    if (registry.Variants.TryGetValue(variant.Name, out var varBuilder))
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
                    if (registry.Enums.TryGetValue(en.Name, out var enType))
                        return enType;
                    throw new NotSupportedException(
                        $"Anonymous enum types not supported in v0.2 ({context}).");

                case CtFlagsType fl:
                    if (registry.Flags.TryGetValue(fl.Name, out var flType))
                        return flType;
                    throw new NotSupportedException(
                        $"Anonymous flag types not supported in v0.2 ({context}).");

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
        public Dictionary<string, TypeBuilder> Records { get; } = new();
        public Dictionary<string, ConstructorBuilder> RecordCtors { get; } = new();
        // Record name → (field name → getter MethodBuilder).
        public Dictionary<string, Dictionary<string, MethodBuilder>> RecordGetters { get; } = new();
        public Dictionary<string, TypeBuilder> Variants { get; } = new();
        public Dictionary<string, Dictionary<string, TypeBuilder>> VariantCases { get; } = new();
        // Variant name → (case name → ctor). Unit-case ctors are parameterless;
        // payload-case ctors take one arg of the payload's CLR type.
        public Dictionary<string, Dictionary<string, ConstructorBuilder>> VariantCaseCtors { get; } = new();
        public Dictionary<string, CtValType> PrimitiveAliases { get; } = new();
        // Enums + flags emit eagerly to full Types (no forward refs);
        // stored as runtime System.Type rather than TypeBuilder.
        public Dictionary<string, Type> Enums { get; } = new();
        public Dictionary<string, Type> Flags { get; } = new();
    }
}
