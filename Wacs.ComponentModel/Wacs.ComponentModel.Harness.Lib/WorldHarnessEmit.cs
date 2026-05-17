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
using Wacs.ComponentModel.Harness;
using Wacs.ComponentModel.Types;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace Wacs.ComponentModel.Harness.Lib
{
    /// <summary>
    /// Per-world IL emitter. Walks a <see cref="CtWorldType"/>'s
    /// exports and emits a sealed harness class with a
    /// <c>static LoadFrom(byte[], Action&lt;WasmRuntime&gt;?)</c>
    /// factory plus one typed method per WIT export. v0 implements
    /// the shape the wit-harness-spike-hello fixture exercises:
    /// inline string-in / string-out exports on a single world.
    /// Records, variants, multi-result returns, and inline-interface
    /// exports surface as <see cref="NotSupportedException"/> at
    /// emit time so the gap is loud, not silently mis-emitted.
    /// </summary>
    internal static class WorldHarnessEmit
    {
        // Cached MethodInfos for the runtime targets the emitted IL
        // invokes. Resolved once on first use (the emitter is a
        // single-threaded build-time tool — no need for Lazy<T>).
        private static readonly MethodInfo HarnessLoader_Load =
            typeof(HarnessLoader).GetMethod(nameof(HarnessLoader.Load), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo HarnessLoader_RequireMemoryExport =
            typeof(HarnessLoader).GetMethod(nameof(HarnessLoader.RequireMemoryExport), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo HarnessLoader_RequireFunctionExport =
            typeof(HarnessLoader).GetMethod(nameof(HarnessLoader.RequireFunctionExport), BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo LoadedComponent_Runtime =
            typeof(LoadedComponent).GetProperty(nameof(LoadedComponent.Runtime))!.GetGetMethod()!;
        private static readonly MethodInfo LoadedComponent_Module =
            typeof(LoadedComponent).GetProperty(nameof(LoadedComponent.Module))!.GetGetMethod()!;

        private static readonly MethodInfo MemoryHelpers_ReadI32LE =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.ReadI32LE))!;
        private static readonly MethodInfo StringCoding_LowerUtf8 =
            typeof(StringCoding).GetMethod(nameof(StringCoding.LowerUtf8))!;
        private static readonly MethodInfo StringCoding_LiftUtf8 =
            typeof(StringCoding).GetMethod(nameof(StringCoding.LiftUtf8))!;

        // Cabi-realloc invoker is always Func<int,int,int,int,int>.
        private static readonly Type CabiReallocInvokerType = typeof(Func<int, int, int, int, int>);

        public static TypeBuilder EmitWorldHarness(
            ModuleBuilder module, CtWorldType world, HarnessOptions opts,
            string contractText)
        {
            // Pass A: emit CLR types for every world-level record /
            // variant declared. Records of primitives + variants of
            // unit/primitive/record cases are supported in v0.2.
            var registry = WitTypeEmit.EmitWorldTypes(module, world, opts);

            var worldPascal = NameMangler.ToPascalCase(world.Name);
            var interfaceName = $"{opts.Namespace}.I{worldPascal}";
            var typeName = $"{opts.Namespace}.{worldPascal}Harness";

            // Pass A2: emit the I{World} symmetric interface. Both
            // the harness class (interpreter side) and the future
            // transpiler-emitted class (AOT side) implement this
            // surface, so embedder code can swap engines without
            // touching call sites — per the
            // `feedback_symmetric_engines` invariant.
            var interfaceBuilder = EmitWorldInterface(module, world, registry, interfaceName);

            var typeBuilder = module.DefineType(
                typeName,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                parent: typeof(object),
                interfaces: new[] { interfaceBuilder });

            // Pass B: emit Lift{TypeName} private static helpers on
            // the harness class for every named record / variant.
            // Per-export wrappers call these for return lifts.
            var liftMethods = LiftEmit.EmitLifts(typeBuilder, world, registry);

            // Collect the function exports we'll emit wrappers for.
            // (Interface exports — `export wasi:foo/iface` — defer to
            // a future slice; v0 handles only `export name: func(...)`.)
            var funcExports = new List<FunctionExport>();
            foreach (var port in world.Exports)
            {
                if (port.Spec is CtExternFunc fn)
                {
                    funcExports.Add(BuildFunctionExport(port.Name, fn.Function, registry));
                }
                else
                {
                    throw new NotSupportedException(
                        $"Harness emitter v0 supports only inline-function exports; "
                        + $"export '{port.Name}' is a {port.Spec.GetType().Name}.");
                }
            }

            // Common fields: runtime, memory, cabi_realloc invoker.
            var runtimeField = typeBuilder.DefineField(
                "_runtime", typeof(WasmRuntime),
                FieldAttributes.Private | FieldAttributes.InitOnly);
            var memoryField = typeBuilder.DefineField(
                "_memory", typeof(MemoryInstance),
                FieldAttributes.Private | FieldAttributes.InitOnly);
            var reallocField = typeBuilder.DefineField(
                "_cabiRealloc", CabiReallocInvokerType,
                FieldAttributes.Private | FieldAttributes.InitOnly);

            // Per-export: an invoker delegate + (if return needs
            // freeing) a cabi_post_<name> invoker.
            foreach (var fe in funcExports)
            {
                fe.InvokerField = typeBuilder.DefineField(
                    "_invoke_" + fe.Name.Replace('-', '_'),
                    fe.InvokerType,
                    FieldAttributes.Private | FieldAttributes.InitOnly);
                if (fe.NeedsPostReturn)
                {
                    fe.PostInvokerField = typeBuilder.DefineField(
                        "_post_" + fe.Name.Replace('-', '_'),
                        typeof(Action<int>),
                        FieldAttributes.Private | FieldAttributes.InitOnly);
                }
            }

            // _WitContract: public static readonly string carrying
            // the raw WIT source. Transpiler-side AddHarnessContract
            // reads this at compile time to diff against the loaded
            // component's WIT custom section; LoadFrom can also
            // validate at runtime.
            EmitWitContractField(typeBuilder, contractText);

            var ctor = EmitConstructor(typeBuilder, runtimeField, memoryField, reallocField, funcExports);
            EmitLoadFrom(typeBuilder, ctor, funcExports);
            foreach (var fe in funcExports)
                EmitTypedWrapper(typeBuilder, memoryField, reallocField, fe, registry, liftMethods);

            // Finalize: interface first (so the harness's CreateType
            // sees the interface's methods bound), then the harness.
            interfaceBuilder.CreateType();
            typeBuilder.CreateType();
            return typeBuilder;
        }

        private static TypeBuilder EmitWorldInterface(
            ModuleBuilder module, CtWorldType world, TypeRegistry registry, string interfaceName)
        {
            var iface = module.DefineType(
                interfaceName,
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);

            foreach (var port in world.Exports)
            {
                if (port.Spec is not CtExternFunc fn) continue;

                var methodName = NameMangler.ToPascalCase(port.Name);
                var paramTypes = fn.Function.Params.Select(p => MapHostParamType(p.Type, registry)).ToArray();
                var returnType = ResolveReturnClrType(fn.Function, registry);

                var method = iface.DefineMethod(
                    methodName,
                    MethodAttributes.Public | MethodAttributes.Abstract
                        | MethodAttributes.Virtual | MethodAttributes.HideBySig
                        | MethodAttributes.NewSlot,
                    returnType,
                    paramTypes);

                for (int i = 0; i < fn.Function.Params.Count; i++)
                    method.DefineParameter(i + 1, ParameterAttributes.None,
                        NameMangler.ToCamelCase(fn.Function.Params[i].Name));
            }

            return iface;
        }

        private static void EmitWitContractField(TypeBuilder tb, string contractText)
        {
            var field = tb.DefineField(
                "_WitContract", typeof(string),
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);

            var cctor = tb.DefineTypeInitializer();
            var il = cctor.GetILGenerator();
            il.Emit(OpCodes.Ldstr, contractText);
            il.Emit(OpCodes.Stsfld, field);
            il.Emit(OpCodes.Ret);
        }

        // ===== Per-export metadata =====

        private sealed class FunctionExport
        {
            public string Name = "";          // WIT name (kebab-case)
            public string PascalName = "";    // C# method name
            public CtFunctionType Spec = null!;

            // Invoker delegate type (e.g. Func<int,int,int>) and the
            // lowered (params + result) types as the wasm function sees them.
            public Type[] LoweredParams = Array.Empty<Type>();
            public Type? LoweredReturn;
            public Type InvokerType = typeof(object);
            public bool NeedsPostReturn;

            public FieldBuilder InvokerField = null!;
            public FieldBuilder? PostInvokerField;
        }

        private static FunctionExport BuildFunctionExport(string witName, CtFunctionType fn, TypeRegistry registry)
        {
            var fe = new FunctionExport
            {
                Name = witName,
                PascalName = NameMangler.ToPascalCase(witName),
                Spec = fn,
            };

            // Lower params via canonical-ABI rules. v0.2 handles
            // primitives (1:1), strings (→ ptr+len), and records of
            // primitives (recursively flattened). Lists, options,
            // results, variants-by-value (rare in real WIT) throw.
            var loweredParams = new List<Type>();
            foreach (var p in fn.Params)
                AppendLoweredType(loweredParams, p.Type, $"parameter '{p.Name}' of '{witName}'");
            fe.LoweredParams = loweredParams.ToArray();

            // Lower return.
            if (fn.HasNoResult)
            {
                fe.LoweredReturn = null;
                fe.NeedsPostReturn = false;
            }
            else if (fn.NamedResults != null)
            {
                throw new NotSupportedException(
                    $"Harness emitter v0 does not yet support named (multi-) results "
                    + $"on export '{witName}'.");
            }
            else
            {
                var r = CanonicalAbi.Deref(fn.Result!);
                if (IsStringType(r))
                {
                    // String return is indirect — the function returns an
                    // i32 pointer to a 2 * i32 (ptr, len) tuple. Caller
                    // owns the freeing via cabi_post_<name>.
                    fe.LoweredReturn = typeof(int);
                    fe.NeedsPostReturn = true;
                }
                else if (r is CtRecordType || r is CtVariantType || r is CtListType)
                {
                    // Aggregate return — wasm returns a ret-area i32
                    // pointing at the value laid out per canonical ABI.
                    // NeedsPostReturn iff the value transitively
                    // contains strings or lists (those carry pointers
                    // that need cabi_post freeing). Pure-primitive
                    // records / variants are inert and don't need a
                    // post-return call. Lists ALWAYS need post-return
                    // (the element-array body lives on the wasm-side
                    // heap allocator).
                    fe.LoweredReturn = typeof(int);
                    fe.NeedsPostReturn = ContainsStringOrList(r);
                }
                else
                {
                    fe.LoweredReturn = MapPrimitiveToClrType(r, $"return of '{witName}'");
                    fe.NeedsPostReturn = false;
                }
            }

            fe.InvokerType = MakeInvokerDelegateType(fe.LoweredParams, fe.LoweredReturn);
            return fe;
        }

        /// <summary>
        /// Recursively flatten a WIT type into the wasm-level i32/i64/
        /// f32/f64 args its canonical-ABI lowering uses. Strings → two
        /// i32 (ptr, len). Records of primitives → one slot per field.
        /// Nested records → recurse. Variants/lists throw (v0.2 doesn't
        /// pass them by-value yet).
        /// </summary>
        private static void AppendLoweredType(List<Type> sink, CtValType wit, string context)
        {
            var deref = CanonicalAbi.Deref(wit);
            if (IsStringType(deref))
            {
                sink.Add(typeof(int));  // ptr
                sink.Add(typeof(int));  // len
                return;
            }
            if (deref is CtRecordType rec)
            {
                foreach (var f in rec.Fields)
                    AppendLoweredType(sink, f.Type, $"{context} → field '{f.Name}'");
                return;
            }
            sink.Add(MapPrimitiveToClrType(deref, context));
        }

        /// <summary>
        /// True if <paramref name="t"/> transitively contains a
        /// <c>string</c> or <c>list&lt;...&gt;</c> — i.e. carries
        /// memory the canonical ABI requires a cabi_post_* call to
        /// free.
        /// </summary>
        private static bool ContainsStringOrList(CtValType t)
        {
            var d = CanonicalAbi.Deref(t);
            switch (d)
            {
                case CtPrimType p: return p.Kind == CtPrim.String;
                case CtListType: return true;
                case CtRecordType rec:
                    foreach (var f in rec.Fields)
                        if (ContainsStringOrList(f.Type)) return true;
                    return false;
                case CtVariantType v:
                    foreach (var c in v.Cases)
                        if (c.Payload != null && ContainsStringOrList(c.Payload)) return true;
                    return false;
                default: return false;
            }
        }

        private static bool IsStringType(CtValType t) =>
            t is CtPrimType p && p.Kind == CtPrim.String;

        private static Type MapPrimitiveToClrType(CtValType t, string context)
        {
            if (t is CtPrimType p)
            {
                return p.Kind switch
                {
                    CtPrim.S32 => typeof(int),
                    CtPrim.U32 => typeof(int),
                    CtPrim.S64 => typeof(long),
                    CtPrim.U64 => typeof(long),
                    CtPrim.F32 => typeof(float),
                    CtPrim.F64 => typeof(double),
                    _ => throw new NotSupportedException(
                        $"Harness emitter v0 does not yet support {p.Kind} ({context})."),
                };
            }
            throw new NotSupportedException(
                $"Harness emitter v0 does not yet support {t.GetType().Name} ({context}).");
        }

        private static Type MakeInvokerDelegateType(Type[] paramTypes, Type? returnType)
        {
            if (returnType == null)
            {
                return paramTypes.Length switch
                {
                    0 => typeof(Action),
                    1 => typeof(Action<>).MakeGenericType(paramTypes),
                    2 => typeof(Action<,>).MakeGenericType(paramTypes),
                    3 => typeof(Action<,,>).MakeGenericType(paramTypes),
                    4 => typeof(Action<,,,>).MakeGenericType(paramTypes),
                    _ => throw new NotSupportedException(
                        $"Harness emitter v0 supports up to 4 lowered params for void returns."),
                };
            }
            var all = paramTypes.Append(returnType).ToArray();
            return all.Length switch
            {
                1 => typeof(Func<>).MakeGenericType(all),
                2 => typeof(Func<,>).MakeGenericType(all),
                3 => typeof(Func<,,>).MakeGenericType(all),
                4 => typeof(Func<,,,>).MakeGenericType(all),
                5 => typeof(Func<,,,,>).MakeGenericType(all),
                _ => throw new NotSupportedException(
                    $"Harness emitter v0 supports up to 4 lowered params for value returns."),
            };
        }

        // ===== Constructor =====

        private static ConstructorBuilder EmitConstructor(
            TypeBuilder typeBuilder,
            FieldBuilder runtimeField, FieldBuilder memoryField, FieldBuilder reallocField,
            List<FunctionExport> funcExports)
        {
            // Build param list: runtime, memory, realloc, [invoker, post?] per export.
            var paramTypes = new List<Type>
            {
                typeof(WasmRuntime),
                typeof(MemoryInstance),
                CabiReallocInvokerType,
            };
            foreach (var fe in funcExports)
            {
                paramTypes.Add(fe.InvokerType);
                if (fe.NeedsPostReturn) paramTypes.Add(typeof(Action<int>));
            }

            var ctor = typeBuilder.DefineConstructor(
                MethodAttributes.Private | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                CallingConventions.HasThis,
                paramTypes.ToArray());

            var il = ctor.GetILGenerator();
            // base ctor
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);

            // Assign fields in declared order.
            int argIdx = 1;
            EmitStoreFromArg(il, runtimeField, argIdx++);
            EmitStoreFromArg(il, memoryField, argIdx++);
            EmitStoreFromArg(il, reallocField, argIdx++);
            foreach (var fe in funcExports)
            {
                EmitStoreFromArg(il, fe.InvokerField, argIdx++);
                if (fe.NeedsPostReturn)
                    EmitStoreFromArg(il, fe.PostInvokerField!, argIdx++);
            }

            il.Emit(OpCodes.Ret);
            return ctor;
        }

        private static void EmitStoreFromArg(ILGenerator il, FieldBuilder field, int argIdx)
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitLdarg(il, argIdx);
            il.Emit(OpCodes.Stfld, field);
        }

        // ===== Static factory: LoadFrom(byte[], Action<WasmRuntime>?) =====

        private static void EmitLoadFrom(
            TypeBuilder typeBuilder,
            ConstructorBuilder ctor,
            List<FunctionExport> funcExports)
        {
            var loadFrom = typeBuilder.DefineMethod(
                "LoadFrom",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                typeBuilder,
                new[] { typeof(byte[]), typeof(Action<WasmRuntime>) });
            loadFrom.DefineParameter(1, ParameterAttributes.None, "componentBytes");
            loadFrom.DefineParameter(2, ParameterAttributes.Optional | ParameterAttributes.HasDefault, "bindImports")
                .SetConstant(null);

            var il = loadFrom.GetILGenerator();

            // var loaded = HarnessLoader.Load(componentBytes, bindImports, "harness");
            var loadedLocal = il.DeclareLocal(typeof(LoadedComponent));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "harness");
            il.Emit(OpCodes.Call, HarnessLoader_Load);
            il.Emit(OpCodes.Stloc, loadedLocal);

            // var runtime = loaded.Runtime;
            var runtimeLocal = il.DeclareLocal(typeof(WasmRuntime));
            il.Emit(OpCodes.Ldloc, loadedLocal);
            il.Emit(OpCodes.Callvirt, LoadedComponent_Runtime);
            il.Emit(OpCodes.Stloc, runtimeLocal);

            // var module = loaded.Module;
            var moduleLocal = il.DeclareLocal(typeof(ModuleInstance));
            il.Emit(OpCodes.Ldloc, loadedLocal);
            il.Emit(OpCodes.Callvirt, LoadedComponent_Module);
            il.Emit(OpCodes.Stloc, moduleLocal);

            // var memory = HarnessLoader.RequireMemoryExport(runtime, module, "memory");
            var memoryLocal = il.DeclareLocal(typeof(MemoryInstance));
            il.Emit(OpCodes.Ldloc, runtimeLocal);
            il.Emit(OpCodes.Ldloc, moduleLocal);
            il.Emit(OpCodes.Ldstr, "memory");
            il.Emit(OpCodes.Call, HarnessLoader_RequireMemoryExport);
            il.Emit(OpCodes.Stloc, memoryLocal);

            // var reallocAddr = HarnessLoader.RequireFunctionExport(module, "cabi_realloc");
            var reallocAddrLocal = il.DeclareLocal(typeof(FuncAddr));
            il.Emit(OpCodes.Ldloc, moduleLocal);
            il.Emit(OpCodes.Ldstr, "cabi_realloc");
            il.Emit(OpCodes.Call, HarnessLoader_RequireFunctionExport);
            il.Emit(OpCodes.Stloc, reallocAddrLocal);

            // var realloc = runtime.CreateInvokerFunc<int,int,int,int,int>(reallocAddr);
            var reallocLocal = il.DeclareLocal(CabiReallocInvokerType);
            EmitCreateInvokerFunc(il, runtimeLocal, reallocAddrLocal,
                new[] { typeof(int), typeof(int), typeof(int), typeof(int) },
                typeof(int));
            il.Emit(OpCodes.Stloc, reallocLocal);

            // Per-export: addr + invoker + optional post-return invoker.
            var perExportLocals = new List<(LocalBuilder Invoker, LocalBuilder? Post)>();
            foreach (var fe in funcExports)
            {
                var addrLocal = il.DeclareLocal(typeof(FuncAddr));
                il.Emit(OpCodes.Ldloc, moduleLocal);
                il.Emit(OpCodes.Ldstr, fe.Name);
                il.Emit(OpCodes.Call, HarnessLoader_RequireFunctionExport);
                il.Emit(OpCodes.Stloc, addrLocal);

                var invokerLocal = il.DeclareLocal(fe.InvokerType);
                EmitCreateInvokerFunc(il, runtimeLocal, addrLocal, fe.LoweredParams, fe.LoweredReturn);
                il.Emit(OpCodes.Stloc, invokerLocal);

                LocalBuilder? postLocal = null;
                if (fe.NeedsPostReturn)
                {
                    var postAddrLocal = il.DeclareLocal(typeof(FuncAddr));
                    il.Emit(OpCodes.Ldloc, moduleLocal);
                    il.Emit(OpCodes.Ldstr, "cabi_post_" + fe.Name);
                    il.Emit(OpCodes.Call, HarnessLoader_RequireFunctionExport);
                    il.Emit(OpCodes.Stloc, postAddrLocal);

                    postLocal = il.DeclareLocal(typeof(Action<int>));
                    EmitCreateInvokerFunc(il, runtimeLocal, postAddrLocal,
                        new[] { typeof(int) }, null);
                    il.Emit(OpCodes.Stloc, postLocal);
                }

                perExportLocals.Add((invokerLocal, postLocal));
            }

            // Construct harness instance. The ctor's metadata token is
            // available directly via the ConstructorBuilder — no
            // GetConstructor lookup needed on an in-flight TypeBuilder.
            il.Emit(OpCodes.Ldloc, runtimeLocal);
            il.Emit(OpCodes.Ldloc, memoryLocal);
            il.Emit(OpCodes.Ldloc, reallocLocal);
            foreach (var (invoker, post) in perExportLocals)
            {
                il.Emit(OpCodes.Ldloc, invoker);
                if (post != null) il.Emit(OpCodes.Ldloc, post);
            }
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Ret);
        }

        // ===== Typed wrapper for a single export =====

        private static void EmitTypedWrapper(
            TypeBuilder typeBuilder,
            FieldBuilder memoryField, FieldBuilder reallocField,
            FunctionExport fe,
            TypeRegistry registry,
            System.Collections.Generic.Dictionary<string, MethodBuilder> liftMethods)
        {
            // Compute C# method signature from the WIT spec.
            var paramTypes = fe.Spec.Params.Select(p => MapHostParamType(p.Type, registry)).ToArray();
            var returnType = ResolveReturnClrType(fe.Spec, registry);

            // Virtual + Final + NewSlot: required for the method to
            // implicitly implement the matching I{World} interface
            // method (Reflection.Emit needs the slot to be allocated;
            // Final keeps the class sealed-method behavior C# users
            // expect for non-extensible harness types).
            var method = typeBuilder.DefineMethod(
                fe.PascalName,
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.NewSlot,
                returnType,
                paramTypes);

            for (int i = 0; i < fe.Spec.Params.Count; i++)
                method.DefineParameter(i + 1, ParameterAttributes.None,
                    NameMangler.ToCamelCase(fe.Spec.Params[i].Name));

            var il = method.GetILGenerator();

            // String-in / string-out — the spike fixture's exact shape.
            // Kept as a focused path until the generic flat-lowered
            // emitter handles strings too (deferred — string lower needs
            // ptr+len in the flat lowering, which the generic path
            // doesn't yet thread through).
            if (fe.Spec.Params.Count == 1 && IsStringType(fe.Spec.Params[0].Type)
                && fe.Spec.Result is not null && IsStringType(fe.Spec.Result))
            {
                EmitStringInStringOut(il, memoryField, reallocField, fe);
                return;
            }

            // Generic flat-lowered case: primitive / record-of-
            // primitives params, primitive / record / variant return.
            // NeedsPostReturn (string-containing returns) handled
            // inline by EmitFlatLowered — calls cabi_post_<name>
            // after lifting.
            if (AllParamsAreFlatLowerable(fe.Spec))
            {
                EmitFlatLowered(il, fe, memoryField, registry, liftMethods);
                return;
            }

            throw new NotSupportedException(
                $"Harness emitter v0.2 doesn't yet support export '{fe.Name}' — "
                + $"params/return outside the supported flat-lowered shape.");
        }

        private static bool AllParamsAreFlatLowerable(CtFunctionType fn)
        {
            foreach (var p in fn.Params)
                if (!IsFlatLowerable(p.Type)) return false;
            return true;
        }

        private static bool IsFlatLowerable(CtValType t)
        {
            var d = CanonicalAbi.Deref(t);
            if (d is CtPrimType prim)
            {
                return prim.Kind != CtPrim.String;  // string is "flat" (ptr+len) but handled specially
            }
            if (d is CtRecordType rec)
            {
                foreach (var f in rec.Fields)
                    if (!IsFlatLowerable(f.Type)) return false;
                return true;
            }
            return false;
        }

        private static Type ResolveReturnClrType(CtFunctionType fn, TypeRegistry registry)
        {
            if (fn.HasNoResult) return typeof(void);
            var d = CanonicalAbi.Deref(fn.Result!);
            return IsStringType(d) ? typeof(string) : WitTypeEmit.MapClrType(d, registry, "return");
        }

        /// <summary>
        /// Generic flat-lowered wrapper emission: walk each typed
        /// param, push its lowered primitive fields onto the invoker
        /// call stack, call the invoker, then lift the return (direct
        /// primitive or indirect record/variant via the registered
        /// Lift{Name} helper).
        /// </summary>
        private static void EmitFlatLowered(
            ILGenerator il, FunctionExport fe, FieldBuilder memoryField,
            TypeRegistry registry,
            System.Collections.Generic.Dictionary<string, MethodBuilder> liftMethods)
        {
            // Push `this._invoker_<name>` onto the stack first; we
            // then push the lowered args and finally callvirt.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, fe.InvokerField);

            // For each user-facing arg slot, push lowered primitive(s).
            for (int i = 0; i < fe.Spec.Params.Count; i++)
            {
                int argIdx = i + 1;  // Ldarg_0 is `this`.
                EmitFlattenedArg(il, argIdx, fe.Spec.Params[i].Type, registry);
            }

            // Call the invoker.
            il.Emit(OpCodes.Callvirt, fe.InvokerType.GetMethod("Invoke")!);

            // Lift the return.
            if (fe.Spec.HasNoResult)
            {
                il.Emit(OpCodes.Pop);  // there shouldn't be anything; safety.
                il.Emit(OpCodes.Ret);
                return;
            }

            var retDeref = CanonicalAbi.Deref(fe.Spec.Result!);
            if (retDeref is CtPrimType)
            {
                // Direct primitive — invoker already produced the value.
                il.Emit(OpCodes.Ret);
                return;
            }
            if (retDeref is CtRecordType rec)
            {
                EmitLiftReturnViaRetArea(il, fe, memoryField, liftMethods[rec.Name]);
                return;
            }
            if (retDeref is CtVariantType variant)
            {
                EmitLiftReturnViaRetArea(il, fe, memoryField, liftMethods[variant.Name]);
                return;
            }
            if (retDeref is CtListType list)
            {
                EmitLiftListReturn(il, fe, list, memoryField, registry, liftMethods);
                return;
            }

            throw new NotSupportedException(
                $"Flat-lowered return path doesn't yet support {retDeref.GetType().Name}.");
        }

        /// <summary>
        /// Direct list-return tail: stash retArea, lift the list
        /// inline via <see cref="LiftEmit.EmitLiftListFromBase"/>
        /// (treating retArea as the base ptr at offset 0), capture
        /// the typed <c>T[]</c>, call <c>cabi_post_&lt;name&gt;</c>
        /// to free the element-array body + retArea, then return
        /// the array. Mirrors <see cref="EmitLiftReturnViaRetArea"/>
        /// but uses the inline list-from-base helper instead of a
        /// pre-emitted Lift method (lists are anonymous structural
        /// types — no per-type Lift method to register).
        /// </summary>
        private static void EmitLiftListReturn(
            ILGenerator il, FunctionExport fe, CtListType list,
            FieldBuilder memoryField, TypeRegistry registry,
            System.Collections.Generic.Dictionary<string, MethodBuilder> liftMethods)
        {
            var retArea = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, retArea);
            // The wrapper's arg.0 is `this`, not memory — load
            // memory from the field into a local so EmitLiftListFromBase
            // can use the same memoryLocal contract field-level lifts use.
            var memoryLocal = il.DeclareLocal(typeof(MemoryInstance));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            il.Emit(OpCodes.Stloc, memoryLocal);

            LiftEmit.EmitLiftListFromBase(il, list, memoryLocal, retArea, 0, registry, liftMethods);

            if (fe.NeedsPostReturn && fe.PostInvokerField != null)
            {
                var arrClr = WitTypeEmit.MapClrType(list, registry, "list return");
                var arr = il.DeclareLocal(arrClr);
                il.Emit(OpCodes.Stloc, arr);
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, fe.PostInvokerField);
                il.Emit(OpCodes.Ldloc, retArea);
                il.Emit(OpCodes.Callvirt, typeof(Action<int>).GetMethod("Invoke")!);
                il.Emit(OpCodes.Ldloc, arr);
            }

            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Emit the indirect-return tail: stash the retArea pointer
        /// the invoker returned, call the lift helper, capture the
        /// typed value, optionally call <c>cabi_post_&lt;name&gt;</c>
        /// to free the retArea + any heap memory the lift consumed
        /// (string bodies, list elements), then return the typed
        /// value. Assumes the invoker's result (the int retArea
        /// ptr) is already on the stack.
        /// </summary>
        private static void EmitLiftReturnViaRetArea(
            ILGenerator il, FunctionExport fe, FieldBuilder memoryField,
            MethodBuilder liftMethod)
        {
            var retArea = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, retArea);

            // Lift the typed value first (before freeing memory it
            // may have read from).
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            il.Emit(OpCodes.Ldloc, retArea);
            il.Emit(OpCodes.Call, liftMethod);

            if (fe.NeedsPostReturn && fe.PostInvokerField != null)
            {
                // Stash the lifted result (records and variants are
                // reference types — independent of the freed memory
                // — so we can safely free the retArea now).
                var lifted = il.DeclareLocal(liftMethod.ReturnType);
                il.Emit(OpCodes.Stloc, lifted);
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, fe.PostInvokerField);
                il.Emit(OpCodes.Ldloc, retArea);
                il.Emit(OpCodes.Callvirt, typeof(Action<int>).GetMethod("Invoke")!);
                il.Emit(OpCodes.Ldloc, lifted);
            }

            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Push the lowered primitive args of one user-facing
        /// parameter onto the IL stack. Primitive → bare ldarg.
        /// Record → for each field, recursively load via the field
        /// getter, then flatten the field.
        /// </summary>
        private static void EmitFlattenedArg(ILGenerator il, int argIdx, CtValType t, TypeRegistry registry)
        {
            var d = CanonicalAbi.Deref(t);
            if (d is CtPrimType prim && prim.Kind != CtPrim.String)
            {
                EmitLdarg(il, argIdx);
                return;
            }
            if (d is CtRecordType rec)
            {
                var getters = registry.RecordGetters[rec.Name];
                foreach (var f in rec.Fields)
                {
                    // For each field: load arg, call getter, recurse.
                    EmitLdarg(il, argIdx);
                    il.Emit(OpCodes.Callvirt, getters[f.Name]);
                    // If the field is itself a primitive, this is enough;
                    // for nested records, the getter returns the nested
                    // record and we'd need to recurse. v0.2 supports
                    // depth-1 only — IsFlatLowerable already vetted this.
                    if (CanonicalAbi.Deref(f.Type) is not CtPrimType)
                        throw new NotSupportedException(
                            "Nested records in flat-lowered params not supported in v0.2.");
                }
                return;
            }
            throw new NotSupportedException(
                $"EmitFlattenedArg v0.2 doesn't support {d.GetType().Name}.");
        }

        private static void EmitStringInStringOut(
            ILGenerator il,
            FieldBuilder memoryField, FieldBuilder reallocField,
            FunctionExport fe)
        {
            // StringCoding.LowerUtf8(_memory, name, _realloc, out int inPtr, out int inLen);
            var inPtr = il.DeclareLocal(typeof(int));
            var inLen = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reallocField);
            il.Emit(OpCodes.Ldloca, inPtr);
            il.Emit(OpCodes.Ldloca, inLen);
            il.Emit(OpCodes.Call, StringCoding_LowerUtf8);

            // int retArea = _invoke_<name>(inPtr, inLen);
            var retArea = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, fe.InvokerField);
            il.Emit(OpCodes.Ldloc, inPtr);
            il.Emit(OpCodes.Ldloc, inLen);
            il.Emit(OpCodes.Callvirt, fe.InvokerType.GetMethod("Invoke")!);
            il.Emit(OpCodes.Stloc, retArea);

            // int outPtr = MemoryHelpers.ReadI32LE(_memory, retArea);
            var outPtr = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            il.Emit(OpCodes.Ldloc, retArea);
            il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
            il.Emit(OpCodes.Stloc, outPtr);

            // int outLen = MemoryHelpers.ReadI32LE(_memory, retArea + 4);
            var outLen = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            il.Emit(OpCodes.Ldloc, retArea);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
            il.Emit(OpCodes.Stloc, outLen);

            // string result = StringCoding.LiftUtf8(_memory, outPtr, outLen);
            var result = il.DeclareLocal(typeof(string));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            il.Emit(OpCodes.Ldloc, outPtr);
            il.Emit(OpCodes.Ldloc, outLen);
            il.Emit(OpCodes.Call, StringCoding_LiftUtf8);
            il.Emit(OpCodes.Stloc, result);

            // _post_<name>(retArea);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, fe.PostInvokerField!);
            il.Emit(OpCodes.Ldloc, retArea);
            il.Emit(OpCodes.Callvirt, typeof(Action<int>).GetMethod("Invoke")!);

            // return result;
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ret);
        }

        // ===== Helpers =====

        // The user-facing C# type for a parameter — string for WIT string,
        // primitive for WIT primitives, emitted CLR record/variant
        // type for those.
        private static Type MapHostParamType(CtValType t, TypeRegistry registry)
        {
            var d = CanonicalAbi.Deref(t);
            if (IsStringType(d)) return typeof(string);
            return WitTypeEmit.MapClrType(d, registry, "parameter");
        }

        /// <summary>
        /// Emit a call to one of WasmRuntime's CreateInvokerFunc /
        /// CreateInvokerAction generic overloads, instantiated with the
        /// supplied lowered param + return types. The closed generic
        /// method is baked into the metadata at emit time, so IL2CPP
        /// sees every instantiation statically rooted by the harness's
        /// LoadFrom call sites.
        ///
        /// The runtime + addr arguments must already be on the local
        /// vars passed in; this helper emits the loads + call.
        /// </summary>
        private static void EmitCreateInvokerFunc(
            ILGenerator il,
            LocalBuilder runtimeLocal, LocalBuilder addrLocal,
            Type[] paramTypes, Type? returnType)
        {
            string methodName;
            int genericArity;
            Type[] genericArgs;

            if (returnType == null)
            {
                methodName = "CreateInvokerAction";
                genericArity = paramTypes.Length;  // CreateInvokerAction<T1,...,Tn>
                genericArgs = paramTypes;
            }
            else
            {
                methodName = "CreateInvokerFunc";
                genericArity = paramTypes.Length + 1;  // CreateInvokerFunc<T1,...,Tn,TResult>
                genericArgs = paramTypes.Append(returnType).ToArray();
            }

            // Find the matching open generic method.
            var openMethod = typeof(WasmRuntime).GetMethods(
                    BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == methodName
                            && m.IsGenericMethodDefinition
                            && m.GetGenericArguments().Length == genericArity);
            var closedMethod = openMethod.MakeGenericMethod(genericArgs);

            il.Emit(OpCodes.Ldloc, runtimeLocal);
            il.Emit(OpCodes.Ldloc, addrLocal);
            // CreateInvokerFunc takes (FuncAddr, InvokerOptions? = null) — pass null.
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, closedMethod);
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
}
