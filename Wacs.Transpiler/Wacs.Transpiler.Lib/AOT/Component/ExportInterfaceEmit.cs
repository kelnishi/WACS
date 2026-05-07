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
using Wacs.ComponentModel.Types;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Phase B chain mode: emit <c>[WitSource]</c>-tagged
    /// <c>I{Iface}</c> interface types into the dynamic assembly
    /// alongside the consumer-side <c>ComponentExports</c> class.
    /// These interfaces are the bridge that lets a transpiled
    /// <c>.dll</c> serve as a host-package for downstream
    /// transpiles — the resolver walks the same <c>[WitSource]</c>
    /// surface the source-generator path emits, so a chain like
    /// <c>B.wasm → B.dll → A.wasm uses --host-package B.dll</c>
    /// resolves through the identical lookup machinery.
    ///
    /// <para>v0 scope: emit interface types with <c>[WitSource]</c>
    /// at the interface granularity, plus methods for functions
    /// whose signature uses only the simple flat-field types
    /// (primitive / string / byte[] / Option-of-primitive /
    /// Result-of-primitives / list&lt;primitive&gt;). Methods
    /// referencing records / variants / enums / resource handles
    /// are skipped — those need named CLR types pre-emitted by
    /// <see cref="ComponentExportsEmit"/>'s
    /// <c>EmitNamedTypes</c>, and threading that across emitter
    /// boundaries is a follow-up. Skipped methods are still safe:
    /// the interface stub remains discoverable, just without a
    /// callable signature for those particular functions.</para>
    /// </summary>
    public static class ExportInterfaceEmit
    {
        /// <summary>
        /// Emit one <c>I{Iface}</c> interface per interface in
        /// <paramref name="decodedWit"/>. No-op when the WIT
        /// decoder didn't produce a package (e.g., the component
        /// shipped no <c>component-type:*</c> custom section).
        /// </summary>
        // Per-module name → Type registry, populated by
        // ComponentExportsEmit (record / variant / enum / flags /
        // resource emit sites) and consumed by LookupNamedType.
        // Replaces a ModuleBuilder.GetType call that's unimplemented
        // on .NET 9 PersistedAssemblyBuilder. ConditionalWeakTable so
        // entries don't outlive the ModuleBuilder.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
            ModuleBuilder, Dictionary<string, Type>> NamedTypeRegistry = new();

        /// <summary>
        /// Register pre-emitted named WIT types so subsequent
        /// <see cref="EmitInterfaces"/> calls can resolve TypeRefs by
        /// FullName. Idempotent additions; later entries overwrite.
        /// </summary>
        internal static void RegisterNamedTypes(
            ModuleBuilder module,
            IReadOnlyDictionary<string, Type> namedTypesByWitName,
            string @namespace)
        {
            var dict = NamedTypeRegistry.GetOrCreateValue(module);
            foreach (var kv in namedTypesByWitName)
                dict[@namespace + "." + ToPascal(kv.Key)] = kv.Value;
        }

        public static IReadOnlyList<Type> EmitInterfaces(
            ModuleBuilder module,
            string @namespace,
            CtPackage? decodedWit)
        {
            var emitted = new List<Type>();
            if (decodedWit == null) return emitted;
            foreach (var iface in decodedWit.Interfaces)
            {
                var t = EmitInterface(module, @namespace, iface);
                if (t != null) emitted.Add(t);
            }
            // Synthesize an I{World}World interface for any world
            // that exports freestanding functions (not interface
            // refs). Mirrors wit-bindgen-csharp's pattern for
            // top-level world exports.
            foreach (var world in decodedWit.Worlds)
            {
                var freeFns = new List<CtInterfaceFunction>();
                foreach (var ex in world.Exports)
                {
                    if (ex.Spec is CtExternFunc fn)
                        freeFns.Add(new CtInterfaceFunction(ex.Name, fn.Function));
                }
                if (freeFns.Count == 0) continue;
                var synth = new CtInterfaceType(
                    decodedWit.Name, world.Name + "-world",
                    System.Array.Empty<CtNamedType>(),
                    freeFns,
                    System.Array.Empty<CtUse>(),
                    System.Array.Empty<CtNamedType>());
                var t = EmitInterface(module, @namespace, synth);
                if (t != null) emitted.Add(t);
            }
            return emitted;
        }

        private static Type? EmitInterface(
            ModuleBuilder module, string @namespace,
            CtInterfaceType iface)
        {
            var typeName = @namespace + ".I" + ToPascal(iface.Name);
            var typeBuilder = module.DefineType(
                typeName,
                TypeAttributes.Public | TypeAttributes.Interface
                    | TypeAttributes.Abstract);

            // Interface-level [WitSource("interface ...",
            // Package=..., Interface=...)]. The Source is a
            // synthesized fragment — wit-bindgen emits the
            // verbatim WIT text here when available; we don't
            // have it from the decoded CtPackage so use a
            // canonical reconstruction.
            ApplyWitSource(typeBuilder,
                "interface " + iface.Name,
                PackageString(iface.Package),
                iface.Name,
                item: null);

            foreach (var fn in iface.Functions)
            {
                EmitMethod(typeBuilder, module, @namespace, fn,
                    PackageString(iface.Package), iface.Name);
            }

            return typeBuilder.CreateType();
        }

        private static void EmitMethod(
            TypeBuilder tb, ModuleBuilder module, string @namespace,
            CtInterfaceFunction fn,
            string packageStr, string ifaceName)
        {
            var paramTypes = MapParams(fn.Type.Params, module, @namespace);
            if (paramTypes == null) return;
            var returnType = MapReturn(fn.Type.Result, module, @namespace);
            if (returnType == null) return;

            var method = tb.DefineMethod(
                ToPascal(fn.Name),
                MethodAttributes.Public | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.HideBySig
                    | MethodAttributes.NewSlot,
                returnType,
                paramTypes);

            // Method-level [WitSource("name: func(...)", Package,
            // Interface, Item)]. Same canonical-reconstruction
            // approach as the interface-level attribute.
            ApplyWitSource(method,
                fn.Name + ": func(...)",
                packageStr, ifaceName, fn.Name);
        }

        // CtFuncParam list → Type[] for DefineMethod. Returns null
        // when any param uses an unsupported type (skip the method).
        private static Type[]? MapParams(IReadOnlyList<CtFuncParam> ps,
            ModuleBuilder module, string @namespace)
        {
            var types = new Type[ps.Count];
            for (int i = 0; i < ps.Count; i++)
            {
                var t = MapType(ps[i].Type, module, @namespace);
                if (t == null) return null;
                types[i] = t;
            }
            return types;
        }

        private static Type? MapReturn(CtValType? r,
            ModuleBuilder module, string @namespace)
        {
            if (r == null) return typeof(void);
            return MapType(r, module, @namespace);
        }

        // CtValType → CLR Type. Resolves named types
        // (records / variants / enums / flags / resource handles)
        // through the @namespace + PascalCase lookup against types
        // ComponentExportsEmit pre-emitted into the module.
        private static Type? MapType(CtValType t,
            ModuleBuilder module, string @namespace)
        {
            if (t is CtPrimType p) return MapPrimitive(p.Kind);
            if (t is CtListType list) return MapListType(list, module, @namespace);
            if (t is CtOptionType opt) return MapOptionType(opt, module, @namespace);
            if (t is CtResultType res) return MapResultType(res, module, @namespace);
            // Named-type references: CtTypeRef points at a record /
            // variant / enum / flags by WIT name. ComponentExportsEmit
            // emits each as `@namespace.PascalCase(name)`. Look it
            // up through the module — it's already baked by the
            // time ExportInterfaceEmit runs.
            if (t is CtTypeRef tr)
                return LookupNamedType(module, @namespace, tr.Name);
            // Directly-embedded named types — resolve by their own
            // declared name (same naming convention as TypeRef).
            if (t is CtRecordType rec) return LookupNamedType(module, @namespace, rec.Name);
            if (t is CtVariantType var) return LookupNamedType(module, @namespace, var.Name);
            if (t is CtEnumType en) return LookupNamedType(module, @namespace, en.Name);
            if (t is CtFlagsType fl) return LookupNamedType(module, @namespace, fl.Name);
            if (t is CtResourceType rt) return LookupNamedType(module, @namespace, rt.Name);
            // own<R> / borrow<R>: resource handle → the CLR class
            // ComponentExportsEmit emitted for the resource. v1
            // surfaces it as the consumer-side handle class
            // (matches what wit-bindgen-csharp non-host mode does);
            // host-mode interface emission is a follow-up.
            if (t is CtOwnType own)
            {
                if (own.Resource is CtTypeRef ownRef)
                    return LookupNamedType(module, @namespace, ownRef.Name);
                if (own.Resource is CtResourceType ownRes)
                    return LookupNamedType(module, @namespace, ownRes.Name);
            }
            if (t is CtBorrowType bo)
            {
                if (bo.Resource is CtTypeRef boRef)
                    return LookupNamedType(module, @namespace, boRef.Name);
                if (bo.Resource is CtResourceType boRes)
                    return LookupNamedType(module, @namespace, boRes.Name);
            }
            // Tuple → ValueTuple<...>
            if (t is CtTupleType tup) return MapTupleType(tup, module, @namespace);
            return null;
        }

        private static Type? LookupNamedType(ModuleBuilder module,
            string @namespace, string witName)
        {
            // Prefer the per-module registry primed by ComponentTranspiler
            // before EmitInterfaces — that's the PersistedAssemblyBuilder-
            // safe path. Fall back to module.GetType for tests that drive
            // EmitInterfaces directly against a legacy AssemblyBuilder
            // (Run/RunAndCollect), where GetType is implemented.
            // PersistedModuleBuilder throws NotImplementedException; swallow.
            var key = @namespace + "." + ToPascal(witName);
            if (NamedTypeRegistry.TryGetValue(module, out var dict)
                && dict.TryGetValue(key, out var t))
            {
                return t;
            }
            try
            {
                return module.GetType(key);
            }
            catch (NotImplementedException)
            {
                return null;
            }
        }

        private static Type? MapTupleType(CtTupleType tup,
            ModuleBuilder module, string @namespace)
        {
            var elements = new Type[tup.Elements.Count];
            for (int i = 0; i < tup.Elements.Count; i++)
            {
                var et = MapType(tup.Elements[i], module, @namespace);
                if (et == null) return null;
                elements[i] = et;
            }
            // ValueTuple<...> per arity. 1..7 are direct; nested
            // TRest at 8+ defers (uncommon for WIT).
            return elements.Length switch
            {
                1 => typeof(ValueTuple<>).MakeGenericType(elements),
                2 => typeof(ValueTuple<,>).MakeGenericType(elements),
                3 => typeof(ValueTuple<,,>).MakeGenericType(elements),
                4 => typeof(ValueTuple<,,,>).MakeGenericType(elements),
                5 => typeof(ValueTuple<,,,,>).MakeGenericType(elements),
                6 => typeof(ValueTuple<,,,,,>).MakeGenericType(elements),
                7 => typeof(ValueTuple<,,,,,,>).MakeGenericType(elements),
                _ => null,
            };
        }

        private static Type? MapPrimitive(CtPrim kind) => kind switch
        {
            CtPrim.Bool => typeof(bool),
            CtPrim.S8 => typeof(sbyte),
            CtPrim.S16 => typeof(short),
            CtPrim.S32 => typeof(int),
            CtPrim.S64 => typeof(long),
            CtPrim.U8 => typeof(byte),
            CtPrim.U16 => typeof(ushort),
            CtPrim.U32 => typeof(uint),
            CtPrim.U64 => typeof(ulong),
            CtPrim.F32 => typeof(float),
            CtPrim.F64 => typeof(double),
            CtPrim.Char => typeof(uint),  // wit-bindgen-csharp uses uint for char
            CtPrim.String => typeof(string),
            _ => null,
        };

        private static Type? MapListType(CtListType list,
            ModuleBuilder module, string @namespace)
        {
            // list<u8> → byte[]; list<T> → T[] when T maps to a
            // CLR type. Defer resource arrays / nested aggregates
            // (the wit-bindgen-csharp shape uses T[] uniformly so
            // the type mapping is straight-through once T is
            // resolved).
            if (list.Element is CtPrimType prim
                && prim.Kind == CtPrim.U8)
                return typeof(byte[]);
            var elem = MapType(list.Element, module, @namespace);
            if (elem == null) return null;
            return elem.MakeArrayType();
        }

        private static Type? MapOptionType(CtOptionType opt,
            ModuleBuilder module, string @namespace)
        {
            var inner = MapType(opt.Inner, module, @namespace);
            if (inner == null) return null;
            return typeof(Option<>).MakeGenericType(inner);
        }

        private static Type? MapResultType(CtResultType res,
            ModuleBuilder module, string @namespace)
        {
            // result<T, E> with both arms — needs both sides.
            // Elided arms (result, result<T>, result<_,E>) need
            // unit/void on the missing side. Defer those for v1;
            // emit only the both-sides case here.
            if (res.Ok == null || res.Err == null) return null;
            var ok = MapType(res.Ok, module, @namespace);
            var err = MapType(res.Err, module, @namespace);
            if (ok == null || err == null) return null;
            return typeof(Result<,>).MakeGenericType(ok, err);
        }

        private static string PackageString(CtPackageName? pkg)
        {
            if (pkg == null) return "";
            // wit-bindgen-csharp's WitSource Package format is
            // "<ns>:<path>@<version>". Mirrors the format the
            // source-generator emits, so HostPackageResolver can
            // index transpiled .dlls and source-gen .dlls
            // uniformly.
            var path = string.Join(":", pkg.Path);
            var ver = pkg.Version != null ? "@" + pkg.Version : "";
            return pkg.Namespace + ":" + path + ver;
        }

        // Apply [WitSource(source, Package=..., Interface=...,
        // Item=...)] to a TypeBuilder or MethodBuilder.
        private static void ApplyWitSource(
            TypeBuilder tb, string source, string package,
            string ifaceName, string? item)
        {
            tb.SetCustomAttribute(BuildWitSource(source, package,
                ifaceName, item));
        }

        private static void ApplyWitSource(
            MethodBuilder mb, string source, string package,
            string ifaceName, string? item)
        {
            mb.SetCustomAttribute(BuildWitSource(source, package,
                ifaceName, item));
        }

        private static CustomAttributeBuilder BuildWitSource(
            string source, string package, string ifaceName,
            string? item)
        {
            var ctor = typeof(WitSourceAttribute)
                .GetConstructor(new[] { typeof(string) })!;
            var packageProp = typeof(WitSourceAttribute)
                .GetProperty(nameof(WitSourceAttribute.Package))!;
            var interfaceProp = typeof(WitSourceAttribute)
                .GetProperty(nameof(WitSourceAttribute.Interface))!;
            var itemProp = typeof(WitSourceAttribute)
                .GetProperty(nameof(WitSourceAttribute.Item))!;

            // Named property setters: skip null Item (so the
            // interface-level attribute doesn't carry a stray
            // Item="").
            var props = new List<PropertyInfo> { packageProp, interfaceProp };
            var values = new List<object> { package, ifaceName };
            if (item != null)
            {
                props.Add(itemProp);
                values.Add(item);
            }
            return new CustomAttributeBuilder(
                ctor,
                new object[] { source },
                props.ToArray(),
                values.ToArray());
        }

        // kebab-case → PascalCase. Mirrors what wit-bindgen-csharp
        // does so type names line up with what the source generator
        // would emit.
        private static string ToPascal(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder();
            bool upperNext = true;
            foreach (var ch in s)
            {
                if (ch == '-' || ch == '_')
                {
                    upperNext = true;
                    continue;
                }
                if (upperNext)
                {
                    sb.Append(char.ToUpperInvariant(ch));
                    upperNext = false;
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }
    }
}
