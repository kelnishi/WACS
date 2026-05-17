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
        private static readonly MethodInfo MemoryHelpers_WriteI32LE =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.WriteI32LE))!;
        private static readonly MethodInfo MemoryHelpers_WriteU8 =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.WriteU8))!;
        private static readonly MethodInfo MemoryHelpers_WriteI16LE =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.WriteI16LE))!;
        private static readonly MethodInfo MemoryHelpers_WriteI64LE =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.WriteI64LE))!;
        private static readonly MethodInfo MemoryHelpers_WriteF32LE =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.WriteF32LE))!;
        private static readonly MethodInfo MemoryHelpers_WriteF64LE =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.WriteF64LE))!;
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

            // For exports whose direct return is an anonymous
            // aggregate (option / result / tuple), emit a per-export
            // synthetic Lift method so the wrapper can use the same
            // retArea-tail path the named record/variant returns use.
            foreach (var fe in funcExports)
            {
                if (fe.Spec.HasNoResult) continue;
                var ret = CanonicalAbi.Deref(fe.Spec.Result!);
                if (ret is CtOptionType || ret is CtResultType || ret is CtTupleType)
                {
                    var clr = WitTypeEmit.MapClrType(ret, registry, $"return of '{fe.Name}'");
                    var mb = typeBuilder.DefineMethod(
                        "Lift__ret_" + fe.Name.Replace('-', '_'),
                        MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
                        clr,
                        new[] { typeof(MemoryInstance), typeof(int) });
                    mb.DefineParameter(1, ParameterAttributes.None, "memory");
                    mb.DefineParameter(2, ParameterAttributes.None, "ptr");
                    var il = mb.GetILGenerator();
                    LiftEmit.EmitLiftField(il, ret, 0, registry, liftMethods);
                    il.Emit(OpCodes.Ret);
                    fe.ReturnLiftMethod = mb;
                }
            }

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
            // For anonymous-aggregate direct returns (option<T>,
            // result<T,E>, tuple<...>) we emit a per-export Lift
            // helper since these have no name to register under in
            // the world-level liftMethods dictionary.
            public MethodBuilder? ReturnLiftMethod;
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
                else if (r is CtRecordType || r is CtVariantType || r is CtListType
                         || r is CtOptionType || r is CtResultType || r is CtTupleType)
                {
                    // Aggregate return — wasm returns a ret-area i32
                    // pointing at the value laid out per canonical ABI.
                    // NeedsPostReturn iff the value transitively
                    // contains strings or lists (those carry pointers
                    // that need cabi_post freeing). Pure-primitive
                    // records / variants / options / results / tuples
                    // are inert and don't need a post-return call.
                    // Lists ALWAYS need post-return (the element-array
                    // body lives on the wasm-side heap allocator).
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
            if (deref is CtListType)
            {
                sink.Add(typeof(int));  // ptr
                sink.Add(typeof(int));  // count
                return;
            }
            if (deref is CtEnumType || deref is CtFlagsType)
            {
                sink.Add(typeof(int));
                return;
            }
            if (deref is CtOptionType opt)
            {
                sink.Add(typeof(int));  // disc
                AppendLoweredType(sink, opt.Inner, $"{context} → option<T>");
                return;
            }
            if (deref is CtResultType res)
            {
                sink.Add(typeof(int));  // disc
                // Joined slot shape — for v1, the present side's flat
                // (or empty if both elided). IsFlatLowerable has
                // already vetted matching widths.
                if (res.Ok != null) AppendLoweredType(sink, res.Ok, $"{context} → result ok");
                else if (res.Err != null) AppendLoweredType(sink, res.Err, $"{context} → result err");
                return;
            }
            if (deref is CtVariantType variant)
            {
                sink.Add(typeof(int));  // disc (wasm boundary widens to i32)
                var joined = ComputeVariantJoinedSlots(variant);
                sink.AddRange(joined);
                return;
            }
            if (deref is CtTupleType tup)
            {
                foreach (var e in tup.Elements)
                    AppendLoweredType(sink, e, $"{context} → tuple element");
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
                case CtOptionType opt: return ContainsStringOrList(opt.Inner);
                case CtResultType res:
                    if (res.Ok != null && ContainsStringOrList(res.Ok)) return true;
                    if (res.Err != null && ContainsStringOrList(res.Err)) return true;
                    return false;
                case CtTupleType tup:
                    foreach (var e in tup.Elements)
                        if (ContainsStringOrList(e)) return true;
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
                    // Bool lowers to i32 (0 / 1) at the wasm
                    // boundary. CLR bool on the stack is already
                    // i4-sized, so the wrapper's ldarg of a bool
                    // arg is directly assignable to the invoker's
                    // int slot — no explicit conv emit needed.
                    CtPrim.Bool => typeof(int),
                    CtPrim.S8 or CtPrim.U8 or CtPrim.S16 or CtPrim.U16
                        or CtPrim.S32 or CtPrim.U32 or CtPrim.Char => typeof(int),
                    CtPrim.S64 or CtPrim.U64 => typeof(long),
                    CtPrim.F32 => typeof(float),
                    CtPrim.F64 => typeof(double),
                    _ => throw new NotSupportedException(
                        $"Harness emitter v0 does not yet support {p.Kind} ({context})."),
                };
            }
            throw new NotSupportedException(
                $"Harness emitter v0 does not yet support {t.GetType().Name} ({context}).");
        }

        // Open generic Action / Func types indexed by arity. Arity 0
        // for Action is the parameterless typeof(Action); arity 1 for
        // Func is typeof(Func<TResult>).
        private static readonly Type[] OpenActions =
        {
            typeof(Action),
            typeof(Action<>),
            typeof(Action<,>),
            typeof(Action<,,>),
            typeof(Action<,,,>),
            typeof(Action<,,,,>),
            typeof(Action<,,,,,>),
            typeof(Action<,,,,,,>),
            typeof(Action<,,,,,,,>),
            typeof(Action<,,,,,,,,>),
            typeof(Action<,,,,,,,,,>),
            typeof(Action<,,,,,,,,,,>),
            typeof(Action<,,,,,,,,,,,>),
            typeof(Action<,,,,,,,,,,,,>),
            typeof(Action<,,,,,,,,,,,,,>),
            typeof(Action<,,,,,,,,,,,,,,>),
            typeof(Action<,,,,,,,,,,,,,,,>),
        };
        private static readonly Type[] OpenFuncs =
        {
            // Func<TResult> .. Func<T1..T16, TResult>
            typeof(Func<>),
            typeof(Func<,>),
            typeof(Func<,,>),
            typeof(Func<,,,>),
            typeof(Func<,,,,>),
            typeof(Func<,,,,,>),
            typeof(Func<,,,,,,>),
            typeof(Func<,,,,,,,>),
            typeof(Func<,,,,,,,,>),
            typeof(Func<,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,,,,,,,>),
        };

        private static Type MakeInvokerDelegateType(Type[] paramTypes, Type? returnType)
        {
            if (returnType == null)
            {
                if (paramTypes.Length >= OpenActions.Length)
                    throw new NotSupportedException(
                        $"Lowered param arity {paramTypes.Length} exceeds Action<…> BCL ceiling ({OpenActions.Length - 1}).");
                if (paramTypes.Length == 0) return typeof(Action);
                return OpenActions[paramTypes.Length].MakeGenericType(paramTypes);
            }
            var all = paramTypes.Append(returnType).ToArray();
            int funcIndex = all.Length - 1;  // Func<TResult> is OpenFuncs[0]
            if (funcIndex < 0 || funcIndex >= OpenFuncs.Length)
                throw new NotSupportedException(
                    $"Lowered param+return arity {all.Length} exceeds Func<…> BCL ceiling ({OpenFuncs.Length}).");
            return OpenFuncs[funcIndex].MakeGenericType(all);
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
                EmitFlatLowered(il, fe, memoryField, reallocField, registry, liftMethods);
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
            if (d is CtPrimType) return true;  // primitives + strings (strings lower to (ptr,len))
            if (d is CtListType list) return IsFlatLowerable(list.Element);
            if (d is CtEnumType) return true;  // single i32 disc
            if (d is CtFlagsType) return true; // single i32 bits
            if (d is CtOptionType opt) return IsFlatLowerable(opt.Inner);
            if (d is CtResultType res)
            {
                // v1: both sides must have either matching flat shape
                // or one side elided. Mismatched-width joins (e.g.
                // result<u32, u64>) not yet supported.
                var okFlat = new List<Type>();
                var errFlat = new List<Type>();
                if (res.Ok != null)
                {
                    if (!IsFlatLowerable(res.Ok)) return false;
                    AppendLoweredType(okFlat, res.Ok, "result ok");
                }
                if (res.Err != null)
                {
                    if (!IsFlatLowerable(res.Err)) return false;
                    AppendLoweredType(errFlat, res.Err, "result err");
                }
                return okFlat.Count == 0 || errFlat.Count == 0
                    || SlotsMatch(okFlat, errFlat);
            }
            if (d is CtTupleType tup)
            {
                foreach (var e in tup.Elements)
                    if (!IsFlatLowerable(e)) return false;
                return true;
            }
            if (d is CtRecordType rec)
            {
                foreach (var f in rec.Fields)
                    if (!IsFlatLowerable(f.Type)) return false;
                return true;
            }
            if (d is CtVariantType variant)
            {
                foreach (var c in variant.Cases)
                    if (c.Payload != null && !IsFlatLowerable(c.Payload)) return false;
                // v1: cases must have matching slot types at each
                // shared position (cautious join — no type widening).
                try { ComputeVariantJoinedSlots(variant); return true; }
                catch (NotSupportedException) { return false; }
            }
            return false;
        }

        /// <summary>
        /// Compute the canonical-ABI joined flat slot shape for a
        /// variant (excluding the leading discriminator). For each
        /// slot position, every case that has a slot at that position
        /// must contribute the same CLR slot type — otherwise the
        /// strict v1 form refuses (full join-algorithm widening is
        /// deferred to a later slice).
        /// </summary>
        private static List<Type> ComputeVariantJoinedSlots(CtVariantType variant)
        {
            var caseSlots = new List<List<Type>>(variant.Cases.Count);
            foreach (var c in variant.Cases)
            {
                var slots = new List<Type>();
                if (c.Payload != null)
                    AppendLoweredType(slots, c.Payload, $"variant '{variant.Name}' case '{c.Name}'");
                caseSlots.Add(slots);
            }
            int maxLen = 0;
            foreach (var s in caseSlots) if (s.Count > maxLen) maxLen = s.Count;
            var joined = new List<Type>(maxLen);
            for (int i = 0; i < maxLen; i++)
            {
                Type? slotType = null;
                foreach (var s in caseSlots)
                {
                    if (i >= s.Count) continue;
                    if (slotType == null) slotType = s[i];
                    else if (slotType != s[i])
                        throw new NotSupportedException(
                            $"Variant '{variant.Name}' joined slot {i}: mismatched case types " +
                            $"({slotType} vs {s[i]}); the harness v1 join algorithm requires matching " +
                            $"types per slot.");
                }
                joined.Add(slotType!);
            }
            return joined;
        }

        private static bool SlotsMatch(List<Type> a, List<Type> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
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
            ILGenerator il, FunctionExport fe,
            FieldBuilder memoryField, FieldBuilder reallocField,
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
                EmitFlattenedArg(il, argIdx, fe.Spec.Params[i].Type,
                    memoryField, reallocField, registry);
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
            if (retDeref is CtPrimType retPrim)
            {
                if (retPrim.Kind == CtPrim.String)
                {
                    // String return is indirect (retArea ptr → (ptr, len)).
                    // The invoker returned an i32 retArea; lift via
                    // ReadI32LE + StringCoding.LiftUtf8 + cabi_post.
                    EmitLiftStringReturn(il, fe, memoryField);
                    return;
                }
                // Other primitives — invoker already produced the value.
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
            if (retDeref is CtOptionType || retDeref is CtResultType || retDeref is CtTupleType)
            {
                EmitLiftReturnViaRetArea(il, fe, memoryField, fe.ReturnLiftMethod!);
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
        /// <summary>
        /// Direct string-return tail: stash retArea, read (ptr, len)
        /// from the area, lift via <see cref="StringCoding.LiftUtf8"/>,
        /// stash result, call <c>cabi_post_&lt;name&gt;</c>, push
        /// result, return. Mirrors the string-out half of
        /// <c>EmitStringInStringOut</c> but starts from the
        /// invoker's already-on-stack i32 result.
        /// </summary>
        private static void EmitLiftStringReturn(
            ILGenerator il, FunctionExport fe, FieldBuilder memoryField)
        {
            var retArea = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, retArea);

            // outPtr = ReadI32LE(_memory, retArea);
            var outPtr = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            il.Emit(OpCodes.Ldloc, retArea);
            il.Emit(OpCodes.Call,
                typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.ReadI32LE))!);
            il.Emit(OpCodes.Stloc, outPtr);

            // outLen = ReadI32LE(_memory, retArea + 4);
            var outLen = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            il.Emit(OpCodes.Ldloc, retArea);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Call,
                typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.ReadI32LE))!);
            il.Emit(OpCodes.Stloc, outLen);

            // string result = StringCoding.LiftUtf8(_memory, outPtr, outLen);
            var result = il.DeclareLocal(typeof(string));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            il.Emit(OpCodes.Ldloc, outPtr);
            il.Emit(OpCodes.Ldloc, outLen);
            il.Emit(OpCodes.Call,
                typeof(StringCoding).GetMethod(nameof(StringCoding.LiftUtf8))!);
            il.Emit(OpCodes.Stloc, result);

            if (fe.NeedsPostReturn && fe.PostInvokerField != null)
            {
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, fe.PostInvokerField);
                il.Emit(OpCodes.Ldloc, retArea);
                il.Emit(OpCodes.Callvirt, typeof(Action<int>).GetMethod("Invoke")!);
            }

            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ret);
        }

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
        private static void EmitFlattenedArg(
            ILGenerator il, int argIdx, CtValType t,
            FieldBuilder memoryField, FieldBuilder reallocField,
            TypeRegistry registry)
        {
            var d = CanonicalAbi.Deref(t);
            if (d is CtPrimType prim && prim.Kind != CtPrim.String)
            {
                EmitLdarg(il, argIdx);
                return;
            }
            if (d is CtPrimType s && s.Kind == CtPrim.String)
            {
                EmitLowerStringArg(il, argIdx, memoryField, reallocField);
                return;
            }
            if (d is CtListType list)
            {
                EmitLowerListArg(il, argIdx, list, memoryField, reallocField, registry);
                return;
            }
            if (d is CtEnumType || d is CtFlagsType)
            {
                // CLR enum value on the stack IS its underlying integer;
                // no conversion needed before pushing into the i32 slot.
                EmitLdarg(il, argIdx);
                return;
            }
            if (d is CtOptionType opt)
            {
                EmitLowerOptionArg(il, argIdx, opt, memoryField, reallocField, registry);
                return;
            }
            if (d is CtResultType res)
            {
                EmitLowerResultArg(il, argIdx, res, memoryField, reallocField, registry);
                return;
            }
            if (d is CtVariantType variant)
            {
                EmitLowerVariantArg(il, argIdx, variant, memoryField, reallocField, registry);
                return;
            }
            if (d is CtTupleType tup)
            {
                // Reflection.Emit's Ldfld with a closed runtime
                // ValueTuple generic produces a missing MemberRef
                // token (PersistedAssemblyBuilder serializes the
                // open generic field). Calling a generic static
                // accessor on Harness.Runtime side-steps the issue:
                // the JIT closes the method's generics from the
                // call-site arg's type, returning the right element.
                var tupleClr = WitTypeEmit.MapClrType(d, registry, "tuple param");
                var elemClrs = tupleClr.GetGenericArguments();
                for (int i = 0; i < tup.Elements.Count; i++)
                {
                    var accessorOpen = typeof(Wacs.ComponentModel.Harness.WitTupleAccess)
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .First(m => m.Name == "Item" + (i + 1)
                                    && m.GetGenericArguments().Length == elemClrs.Length);
                    var accessor = accessorOpen.MakeGenericMethod(elemClrs);

                    var elemD = CanonicalAbi.Deref(tup.Elements[i]);
                    if (elemD is CtPrimType ep && ep.Kind == CtPrim.String)
                    {
                        var strLocal = il.DeclareLocal(typeof(string));
                        EmitLdarg(il, argIdx);
                        il.Emit(OpCodes.Call, accessor);
                        il.Emit(OpCodes.Stloc, strLocal);
                        var ptr = il.DeclareLocal(typeof(int));
                        var len = il.DeclareLocal(typeof(int));
                        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                        il.Emit(OpCodes.Ldloc, strLocal);
                        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reallocField);
                        il.Emit(OpCodes.Ldloca, ptr);
                        il.Emit(OpCodes.Ldloca, len);
                        il.Emit(OpCodes.Call, StringCoding_LowerUtf8);
                        il.Emit(OpCodes.Ldloc, ptr);
                        il.Emit(OpCodes.Ldloc, len);
                    }
                    else if (elemD is CtPrimType || elemD is CtEnumType || elemD is CtFlagsType)
                    {
                        EmitLdarg(il, argIdx);
                        il.Emit(OpCodes.Call, accessor);
                    }
                    else
                    {
                        throw new NotSupportedException(
                            $"Tuple element of type {elemD.GetType().Name} not yet supported in lower path.");
                    }
                }
                return;
            }
            if (d is CtRecordType rec)
            {
                // Records-as-params flatten field-by-field per canonical
                // ABI. For each field: load the arg, call the getter to
                // pull the field value out, then flatten that value via
                // the same field-type dispatch. Strings, lists, enums,
                // flags, tuples (and nested records of those) all work
                // by reusing the per-element lower path above.
                var getters = registry.RecordGetters[rec.Name];
                foreach (var f in rec.Fields)
                {
                    EmitFlattenRecordField(il, argIdx, getters[f.Name], f.Type,
                        memoryField, reallocField, registry);
                }
                return;
            }
            throw new NotSupportedException(
                $"EmitFlattenedArg doesn't support {d.GetType().Name}.");
        }

        /// <summary>
        /// Pull a field out of a record-typed arg and flatten it onto
        /// the invoker stack. Stashes the getter result into a local
        /// of the field's CLR type and dispatches via the same
        /// type-tree the top-level arg flattening uses — so nested
        /// records, strings-in-records, lists-in-records, enums /
        /// flags / tuples in records all work without duplicating
        /// the per-shape lower IL.
        /// </summary>
        private static void EmitFlattenRecordField(
            ILGenerator il, int recordArgIdx, MethodInfo getter, CtValType fieldType,
            FieldBuilder memoryField, FieldBuilder reallocField,
            TypeRegistry registry)
        {
            var d = CanonicalAbi.Deref(fieldType);

            // Primitive (non-string): getter result is the slot value.
            if (d is CtPrimType pp && pp.Kind != CtPrim.String)
            {
                EmitLdarg(il, recordArgIdx);
                il.Emit(OpCodes.Callvirt, getter);
                return;
            }
            // Enum / flags: same — getter's return is already the int.
            if (d is CtEnumType || d is CtFlagsType)
            {
                EmitLdarg(il, recordArgIdx);
                il.Emit(OpCodes.Callvirt, getter);
                return;
            }
            // String field → LowerUtf8 on the getter's string result.
            if (d is CtPrimType sp && sp.Kind == CtPrim.String)
            {
                var strLocal = il.DeclareLocal(typeof(string));
                EmitLdarg(il, recordArgIdx);
                il.Emit(OpCodes.Callvirt, getter);
                il.Emit(OpCodes.Stloc, strLocal);
                var ptr = il.DeclareLocal(typeof(int));
                var len = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                il.Emit(OpCodes.Ldloc, strLocal);
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reallocField);
                il.Emit(OpCodes.Ldloca, ptr);
                il.Emit(OpCodes.Ldloca, len);
                il.Emit(OpCodes.Call, StringCoding_LowerUtf8);
                il.Emit(OpCodes.Ldloc, ptr);
                il.Emit(OpCodes.Ldloc, len);
                return;
            }
            // List, nested record, tuple → stash into a typed local
            // then re-dispatch via the synthetic-arg pattern.
            var fieldClr = WitTypeEmit.MapClrType(d, registry, "record field as param");
            var local = il.DeclareLocal(fieldClr);
            EmitLdarg(il, recordArgIdx);
            il.Emit(OpCodes.Callvirt, getter);
            il.Emit(OpCodes.Stloc, local);
            EmitFlattenLocal(il, local, d, memoryField, reallocField, registry);
        }

        /// <summary>
        /// Flatten a value stored in a local onto the invoker stack
        /// using the same per-shape dispatch as the top-level arg
        /// path. Used for record-of-{list, tuple, nested-record}
        /// where we need to act on a stashed sub-value rather than
        /// an arg slot.
        /// </summary>
        private static void EmitFlattenLocal(
            ILGenerator il, LocalBuilder local, CtValType t,
            FieldBuilder memoryField, FieldBuilder reallocField,
            TypeRegistry registry)
        {
            var d = CanonicalAbi.Deref(t);
            if (d is CtListType list)
            {
                EmitLowerListFromLocal(il, local, list, memoryField, reallocField, registry);
                return;
            }
            if (d is CtTupleType tup)
            {
                var tupleClr = WitTypeEmit.MapClrType(d, registry, "tuple param");
                var elemClrs = tupleClr.GetGenericArguments();
                for (int i = 0; i < tup.Elements.Count; i++)
                {
                    var accessorOpen = typeof(Wacs.ComponentModel.Harness.WitTupleAccess)
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .First(m => m.Name == "Item" + (i + 1)
                                    && m.GetGenericArguments().Length == elemClrs.Length);
                    var accessor = accessorOpen.MakeGenericMethod(elemClrs);
                    var elemD = CanonicalAbi.Deref(tup.Elements[i]);
                    if (elemD is CtPrimType ep && ep.Kind == CtPrim.String)
                    {
                        var strLocal = il.DeclareLocal(typeof(string));
                        il.Emit(OpCodes.Ldloc, local);
                        il.Emit(OpCodes.Call, accessor);
                        il.Emit(OpCodes.Stloc, strLocal);
                        var ptr = il.DeclareLocal(typeof(int));
                        var len = il.DeclareLocal(typeof(int));
                        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                        il.Emit(OpCodes.Ldloc, strLocal);
                        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reallocField);
                        il.Emit(OpCodes.Ldloca, ptr);
                        il.Emit(OpCodes.Ldloca, len);
                        il.Emit(OpCodes.Call, StringCoding_LowerUtf8);
                        il.Emit(OpCodes.Ldloc, ptr);
                        il.Emit(OpCodes.Ldloc, len);
                    }
                    else if (elemD is CtPrimType || elemD is CtEnumType || elemD is CtFlagsType)
                    {
                        il.Emit(OpCodes.Ldloc, local);
                        il.Emit(OpCodes.Call, accessor);
                    }
                    else
                    {
                        throw new NotSupportedException(
                            $"Tuple element of type {elemD.GetType().Name} not supported in record-of-tuple param.");
                    }
                }
                return;
            }
            if (d is CtRecordType nested)
            {
                // Re-dispatch: load the local's getters per field.
                var getters = registry.RecordGetters[nested.Name];
                foreach (var f in nested.Fields)
                {
                    // synth: we have the nested instance in `local`,
                    // need to invoke its getter then flatten the
                    // result. Use a synthetic helper that uses ldloc.
                    EmitFlattenSubRecordField(il, local, getters[f.Name], f.Type,
                        memoryField, reallocField, registry);
                }
                return;
            }
            throw new NotSupportedException(
                $"EmitFlattenLocal doesn't support {d.GetType().Name}.");
        }

        private static void EmitFlattenSubRecordField(
            ILGenerator il, LocalBuilder recordLocal, MethodInfo getter, CtValType fieldType,
            FieldBuilder memoryField, FieldBuilder reallocField,
            TypeRegistry registry)
        {
            var d = CanonicalAbi.Deref(fieldType);
            if (d is CtPrimType pp && pp.Kind != CtPrim.String)
            {
                il.Emit(OpCodes.Ldloc, recordLocal);
                il.Emit(OpCodes.Callvirt, getter);
                return;
            }
            if (d is CtEnumType || d is CtFlagsType)
            {
                il.Emit(OpCodes.Ldloc, recordLocal);
                il.Emit(OpCodes.Callvirt, getter);
                return;
            }
            if (d is CtPrimType sp && sp.Kind == CtPrim.String)
            {
                var strLocal = il.DeclareLocal(typeof(string));
                il.Emit(OpCodes.Ldloc, recordLocal);
                il.Emit(OpCodes.Callvirt, getter);
                il.Emit(OpCodes.Stloc, strLocal);
                var ptr = il.DeclareLocal(typeof(int));
                var len = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                il.Emit(OpCodes.Ldloc, strLocal);
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reallocField);
                il.Emit(OpCodes.Ldloca, ptr);
                il.Emit(OpCodes.Ldloca, len);
                il.Emit(OpCodes.Call, StringCoding_LowerUtf8);
                il.Emit(OpCodes.Ldloc, ptr);
                il.Emit(OpCodes.Ldloc, len);
                return;
            }
            var fieldClr = WitTypeEmit.MapClrType(d, registry, "record field as param");
            var local = il.DeclareLocal(fieldClr);
            il.Emit(OpCodes.Ldloc, recordLocal);
            il.Emit(OpCodes.Callvirt, getter);
            il.Emit(OpCodes.Stloc, local);
            EmitFlattenLocal(il, local, d, memoryField, reallocField, registry);
        }

        /// <summary>
        /// Lower a <c>list&lt;T&gt;</c> stored in a local (rather than
        /// an arg slot) — mirrors <see cref="EmitLowerListArg"/> but
        /// uses the supplied local as the source array. Used when a
        /// list value comes from a record field or tuple element.
        /// </summary>
        private static void EmitLowerListFromLocal(
            ILGenerator il, LocalBuilder arrLocal, CtListType list,
            FieldBuilder memoryField, FieldBuilder reallocField,
            TypeRegistry registry)
        {
            var elemDeref = CanonicalAbi.Deref(list.Element);
            int elemSize = CanonicalAbi.SizeOf(elemDeref);
            int elemAlign = CanonicalAbi.AlignOf(elemDeref);
            var countLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, countLocal);

            var basePtr = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reallocField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4, elemAlign);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Ldc_I4, elemSize);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Callvirt, typeof(Func<int, int, int, int, int>).GetMethod("Invoke")!);
            il.Emit(OpCodes.Stloc, basePtr);

            var iLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, iLocal);
            var loopHead = il.DefineLabel();
            var loopCond = il.DefineLabel();
            il.Emit(OpCodes.Br, loopCond);
            il.MarkLabel(loopHead);
            EmitLowerListElement(il, elemDeref, arrLocal, iLocal, basePtr,
                elemSize, memoryField, reallocField, registry);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, iLocal);
            il.MarkLabel(loopCond);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Blt, loopHead);

            il.Emit(OpCodes.Ldloc, basePtr);
            il.Emit(OpCodes.Ldloc, countLocal);
        }

        /// <summary>
        /// Lower an option&lt;T&gt; arg per the canonical-ABI flat
        /// shape: <c>(i32 disc, T_flat…)</c>. None pushes
        /// <c>(0, zeros for T_flat slots)</c>; Some pushes
        /// <c>(1, T's lowered values)</c>. Works for any T whose
        /// flat lowering is known (primitives, strings, enum, flags).
        /// </summary>
        private static void EmitLowerOptionArg(
            ILGenerator il, int argIdx, CtOptionType opt,
            FieldBuilder memoryField, FieldBuilder reallocField,
            TypeRegistry registry)
        {
            var innerD = CanonicalAbi.Deref(opt.Inner);
            var innerClr = WitTypeEmit.MapClrType(innerD, registry, "option inner");
            // Compute the inner type's flat slot shape.
            var innerSlots = new List<Type>();
            AppendLoweredType(innerSlots, innerD, "option inner");

            var noneLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            // Branch on "has value".
            if (innerClr.IsValueType)
            {
                // arg is Nullable<T>; HasValue via ldarga + call.
                EmitLdarga(il, argIdx);
                var hasValue = typeof(System.Nullable<>).MakeGenericType(innerClr)
                    .GetProperty("HasValue")!.GetGetMethod()!;
                il.Emit(OpCodes.Call, hasValue);
            }
            else
            {
                // arg is a reference; null is None.
                EmitLdarg(il, argIdx);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ceq);  // !(arg == null) → HasValue
            }
            il.Emit(OpCodes.Brfalse, noneLabel);

            // Some: push disc=1, then lower the inner value.
            il.Emit(OpCodes.Ldc_I4_1);
            if (innerClr.IsValueType)
            {
                // Unwrap Nullable<T> → T, stash to local of inner CLR
                // type, then re-use the lower path via a synthetic
                // "arg" — easiest: a local-based dispatcher mirroring
                // the arg-based one for the few inner shapes that
                // matter.
                EmitLdarga(il, argIdx);
                var nullableT = typeof(System.Nullable<>).MakeGenericType(innerClr);
                var getValue = nullableT.GetProperty("Value")!.GetGetMethod()!;
                il.Emit(OpCodes.Call, getValue);
                var innerLocal = il.DeclareLocal(innerClr);
                il.Emit(OpCodes.Stloc, innerLocal);
                EmitLowerInnerFromLocal(il, innerLocal, innerD, memoryField, reallocField);
            }
            else
            {
                // Reference type: just stash and reuse local lower.
                var innerLocal = il.DeclareLocal(innerClr);
                EmitLdarg(il, argIdx);
                il.Emit(OpCodes.Stloc, innerLocal);
                EmitLowerInnerFromLocal(il, innerLocal, innerD, memoryField, reallocField);
            }
            il.Emit(OpCodes.Br, endLabel);

            // None: push disc=0, then zeros for each flat slot.
            il.MarkLabel(noneLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            foreach (var slot in innerSlots)
                EmitDefaultForSlot(il, slot);

            il.MarkLabel(endLabel);
        }

        /// <summary>
        /// Lower a <c>result&lt;TOk, TErr&gt;</c> arg per canonical-ABI
        /// flat shape: <c>(i32 disc, ...joined-payload)</c>. Disc 0 =
        /// Ok, 1 = Err. For v1 the joined slots equal whichever
        /// side has a payload (or both, if they have identical flat
        /// shapes); mismatched widths throw at <c>IsFlatLowerable</c>
        /// time. Elided sides on the non-active branch push zero
        /// defaults for the joined slot shape.
        /// </summary>
        private static void EmitLowerResultArg(
            ILGenerator il, int argIdx, CtResultType res,
            FieldBuilder memoryField, FieldBuilder reallocField,
            TypeRegistry registry)
        {
            var okClr = res.Ok == null
                ? typeof(System.ValueTuple)
                : WitTypeEmit.MapClrType(res.Ok, registry, "result ok");
            var errClr = res.Err == null
                ? typeof(System.ValueTuple)
                : WitTypeEmit.MapClrType(res.Err, registry, "result err");
            var resultType = typeof(Wacs.ComponentModel.Harness.WitResult<,>)
                .MakeGenericType(okClr, errClr);

            // Joined slot shape (excludes disc; disc is always int).
            var joinedSlots = new List<Type>();
            if (res.Ok != null) AppendLoweredType(joinedSlots, res.Ok, "result joined");
            else if (res.Err != null) AppendLoweredType(joinedSlots, res.Err, "result joined");

            var isOkGetter = resultType.GetProperty("IsOk")!.GetGetMethod()!;
            var okValueGetter = resultType.GetProperty("OkValue")!.GetGetMethod()!;
            var errValueGetter = resultType.GetProperty("ErrValue")!.GetGetMethod()!;

            var errLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            EmitLdarga(il, argIdx);
            il.Emit(OpCodes.Call, isOkGetter);
            il.Emit(OpCodes.Brfalse, errLabel);

            // Ok: disc = 0
            il.Emit(OpCodes.Ldc_I4_0);
            if (res.Ok != null)
            {
                EmitLdarga(il, argIdx);
                il.Emit(OpCodes.Call, okValueGetter);
                var okLocal = il.DeclareLocal(okClr);
                il.Emit(OpCodes.Stloc, okLocal);
                EmitLowerInnerFromLocal(il, okLocal, res.Ok, memoryField, reallocField);
            }
            else
            {
                // Ok is elided; push zero defaults for joined shape
                foreach (var slot in joinedSlots) EmitDefaultForSlot(il, slot);
            }
            il.Emit(OpCodes.Br, endLabel);

            // Err: disc = 1
            il.MarkLabel(errLabel);
            il.Emit(OpCodes.Ldc_I4_1);
            if (res.Err != null)
            {
                EmitLdarga(il, argIdx);
                il.Emit(OpCodes.Call, errValueGetter);
                var errLocal = il.DeclareLocal(errClr);
                il.Emit(OpCodes.Stloc, errLocal);
                EmitLowerInnerFromLocal(il, errLocal, res.Err, memoryField, reallocField);
            }
            else
            {
                foreach (var slot in joinedSlots) EmitDefaultForSlot(il, slot);
            }

            il.MarkLabel(endLabel);
        }

        /// <summary>
        /// Lower a variant arg per canonical-ABI flat shape:
        /// <c>(i32 disc, ...joined-slots)</c>. Disc is the ordinal
        /// case index. Dispatches via <c>isinst</c> on each case
        /// subclass; the matched case lifts its payload (if any)
        /// to the stack, then zero-pads the trailing joined slots
        /// the case doesn't fill. Falls through to throw on an
        /// instance that matches no case (defensive — won't happen
        /// for a well-typed harness call).
        /// </summary>
        private static void EmitLowerVariantArg(
            ILGenerator il, int argIdx, CtVariantType variant,
            FieldBuilder memoryField, FieldBuilder reallocField,
            TypeRegistry registry)
        {
            var joinedSlots = ComputeVariantJoinedSlots(variant);
            var caseSubclasses = registry.VariantCases[variant.Name];

            var caseLabels = new System.Reflection.Emit.Label[variant.Cases.Count];
            for (int i = 0; i < variant.Cases.Count; i++)
                caseLabels[i] = il.DefineLabel();
            var defaultLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            // Per-case isinst dispatch.
            for (int i = 0; i < variant.Cases.Count; i++)
            {
                var caseSub = caseSubclasses[variant.Cases[i].Name];
                EmitLdarg(il, argIdx);
                il.Emit(OpCodes.Isinst, caseSub);
                il.Emit(OpCodes.Brtrue, caseLabels[i]);
            }
            il.Emit(OpCodes.Br, defaultLabel);

            // Per-case body.
            for (int i = 0; i < variant.Cases.Count; i++)
            {
                il.MarkLabel(caseLabels[i]);
                var c = variant.Cases[i];

                // Push disc = case index.
                il.Emit(OpCodes.Ldc_I4, i);

                // Compute this case's slot list to know how much
                // we fill vs how much we pad.
                var thisCaseSlots = new List<Type>();
                if (c.Payload != null)
                    AppendLoweredType(thisCaseSlots, c.Payload, $"variant case '{c.Name}'");

                if (c.Payload != null)
                {
                    var caseSub = caseSubclasses[c.Name];
                    var payloadClr = WitTypeEmit.MapClrType(c.Payload, registry,
                        $"variant case '{c.Name}' payload");
                    // Cast and load Value.
                    EmitLdarg(il, argIdx);
                    il.Emit(OpCodes.Castclass, caseSub);
                    var valueGetter = caseSub.GetMethod("get_Value")!;
                    il.Emit(OpCodes.Callvirt, valueGetter);
                    var payloadLocal = il.DeclareLocal(payloadClr);
                    il.Emit(OpCodes.Stloc, payloadLocal);
                    EmitLowerInnerFromLocal(il, payloadLocal, c.Payload, memoryField, reallocField);
                }
                // Zero-pad trailing slots this case doesn't fill.
                for (int s = thisCaseSlots.Count; s < joinedSlots.Count; s++)
                    EmitDefaultForSlot(il, joinedSlots[s]);

                il.Emit(OpCodes.Br, endLabel);
            }

            il.MarkLabel(defaultLabel);
            il.Emit(OpCodes.Ldstr,
                "No matching case for variant '" + variant.Name + "' instance.");
            il.Emit(OpCodes.Newobj,
                typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(endLabel);
        }

        private static void EmitLowerInnerFromLocal(
            ILGenerator il, LocalBuilder valueLocal, CtValType innerType,
            FieldBuilder memoryField, FieldBuilder reallocField)
        {
            var d = CanonicalAbi.Deref(innerType);
            if (d is CtPrimType prim && prim.Kind == CtPrim.String)
            {
                var ptr = il.DeclareLocal(typeof(int));
                var len = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                il.Emit(OpCodes.Ldloc, valueLocal);
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reallocField);
                il.Emit(OpCodes.Ldloca, ptr);
                il.Emit(OpCodes.Ldloca, len);
                il.Emit(OpCodes.Call, StringCoding_LowerUtf8);
                il.Emit(OpCodes.Ldloc, ptr);
                il.Emit(OpCodes.Ldloc, len);
                return;
            }
            if (d is CtPrimType || d is CtEnumType || d is CtFlagsType)
            {
                il.Emit(OpCodes.Ldloc, valueLocal);
                return;
            }
            throw new NotSupportedException(
                $"option<T> with inner type {d.GetType().Name} not yet supported in lower path.");
        }

        private static void EmitDefaultForSlot(ILGenerator il, Type slot)
        {
            if (slot == typeof(int)) { il.Emit(OpCodes.Ldc_I4_0); return; }
            if (slot == typeof(long)) { il.Emit(OpCodes.Ldc_I8, 0L); return; }
            if (slot == typeof(float)) { il.Emit(OpCodes.Ldc_R4, 0f); return; }
            if (slot == typeof(double)) { il.Emit(OpCodes.Ldc_R8, 0.0); return; }
            throw new NotSupportedException(
                $"No default emit known for flat slot type {slot}.");
        }

        private static void EmitLdarga(ILGenerator il, int idx)
        {
            if (idx <= byte.MaxValue) il.Emit(OpCodes.Ldarga_S, (byte)idx);
            else il.Emit(OpCodes.Ldarga, (short)idx);
        }

        /// <summary>
        /// Lower a string arg: call <c>StringCoding.LowerUtf8</c> to
        /// alloc + copy into wasm memory via cabi_realloc, then push
        /// <c>(ptr, len)</c> onto the invoker's argument stack.
        /// </summary>
        private static void EmitLowerStringArg(
            ILGenerator il, int argIdx,
            FieldBuilder memoryField, FieldBuilder reallocField)
        {
            var ptr = il.DeclareLocal(typeof(int));
            var len = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            EmitLdarg(il, argIdx);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reallocField);
            il.Emit(OpCodes.Ldloca, ptr);
            il.Emit(OpCodes.Ldloca, len);
            il.Emit(OpCodes.Call, StringCoding_LowerUtf8);
            il.Emit(OpCodes.Ldloc, ptr);
            il.Emit(OpCodes.Ldloc, len);
        }

        /// <summary>
        /// Lower a <c>list&lt;T&gt;</c> arg: alloc a wasm-side block
        /// sized <c>count * elemSize</c> via cabi_realloc, write
        /// each element into linear memory using the per-element
        /// lower path, then push <c>(ptr, count)</c> onto the invoker
        /// argument stack. Supports list elements that are
        /// primitives or strings (string elements recursively lower
        /// each one via LowerUtf8, writing the (ptr, len) pair into
        /// the element slot).
        /// </summary>
        private static void EmitLowerListArg(
            ILGenerator il, int argIdx, CtListType list,
            FieldBuilder memoryField, FieldBuilder reallocField,
            TypeRegistry registry)
        {
            var elemDeref = CanonicalAbi.Deref(list.Element);
            int elemSize = CanonicalAbi.SizeOf(elemDeref);
            int elemAlign = CanonicalAbi.AlignOf(elemDeref);
            var elemClr = WitTypeEmit.MapClrType(elemDeref, registry, "list arg element");

            // 1. arr = arg; count = arr.Length
            var arrLocal = il.DeclareLocal(elemClr.MakeArrayType());
            EmitLdarg(il, argIdx);
            il.Emit(OpCodes.Stloc, arrLocal);
            var countLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, countLocal);

            // 2. bytes = count * elemSize; basePtr = realloc(0, 0, align, bytes)
            var basePtr = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reallocField);
            il.Emit(OpCodes.Ldc_I4_0);  // oldPtr
            il.Emit(OpCodes.Ldc_I4_0);  // oldLen
            il.Emit(OpCodes.Ldc_I4, elemAlign);  // align
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Ldc_I4, elemSize);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Callvirt, typeof(Func<int, int, int, int, int>).GetMethod("Invoke")!);
            il.Emit(OpCodes.Stloc, basePtr);

            // 3. for (i = 0; i < count; i++) write arr[i] at basePtr + i*elemSize.
            var iLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, iLocal);
            var loopHead = il.DefineLabel();
            var loopCond = il.DefineLabel();
            il.Emit(OpCodes.Br, loopCond);
            il.MarkLabel(loopHead);

            EmitLowerListElement(il, elemDeref, arrLocal, iLocal, basePtr,
                elemSize, memoryField, reallocField, registry);

            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, iLocal);
            il.MarkLabel(loopCond);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldloc, countLocal);
            il.Emit(OpCodes.Blt, loopHead);

            // 4. push (basePtr, count) onto invoker stack.
            il.Emit(OpCodes.Ldloc, basePtr);
            il.Emit(OpCodes.Ldloc, countLocal);
        }

        /// <summary>
        /// Write one element of a list into linear memory at
        /// <c>basePtr + i * elemSize</c>. Primitives use the
        /// matching MemoryHelpers.Write* path; strings go through
        /// <c>LowerUtf8</c> per element, writing the produced
        /// (innerPtr, innerLen) pair into the slot.
        /// </summary>
        private static void EmitLowerListElement(
            ILGenerator il, CtValType elemType,
            LocalBuilder arrLocal, LocalBuilder iLocal, LocalBuilder basePtr,
            int elemSize,
            FieldBuilder memoryField, FieldBuilder reallocField,
            TypeRegistry registry)
        {
            // address = basePtr + i * elemSize
            if (elemType is CtPrimType prim)
            {
                switch (prim.Kind)
                {
                    case CtPrim.String:
                        {
                            // Lower the per-element string first.
                            var innerPtr = il.DeclareLocal(typeof(int));
                            var innerLen = il.DeclareLocal(typeof(int));
                            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                            il.Emit(OpCodes.Ldloc, arrLocal);
                            il.Emit(OpCodes.Ldloc, iLocal);
                            il.Emit(OpCodes.Ldelem_Ref);
                            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reallocField);
                            il.Emit(OpCodes.Ldloca, innerPtr);
                            il.Emit(OpCodes.Ldloca, innerLen);
                            il.Emit(OpCodes.Call, StringCoding_LowerUtf8);
                            // Write innerPtr at slot offset 0
                            EmitWriteIntAt(il, memoryField, basePtr, iLocal, elemSize, 0, innerPtr);
                            EmitWriteIntAt(il, memoryField, basePtr, iLocal, elemSize, 4, innerLen);
                            return;
                        }
                    case CtPrim.Bool:
                    case CtPrim.S8:
                    case CtPrim.U8:
                        EmitWriteByteAtElement(il, memoryField, basePtr, iLocal, elemSize, arrLocal);
                        return;
                    case CtPrim.S16:
                    case CtPrim.U16:
                    case CtPrim.Char:
                        EmitWriteI16AtElement(il, memoryField, basePtr, iLocal, elemSize, arrLocal);
                        return;
                    case CtPrim.S32:
                    case CtPrim.U32:
                        EmitWriteI32AtElement(il, memoryField, basePtr, iLocal, elemSize, arrLocal);
                        return;
                    case CtPrim.S64:
                    case CtPrim.U64:
                        EmitWriteI64AtElement(il, memoryField, basePtr, iLocal, elemSize, arrLocal);
                        return;
                    case CtPrim.F32:
                        EmitWriteF32AtElement(il, memoryField, basePtr, iLocal, elemSize, arrLocal);
                        return;
                    case CtPrim.F64:
                        EmitWriteF64AtElement(il, memoryField, basePtr, iLocal, elemSize, arrLocal);
                        return;
                }
            }
            if (elemType is CtRecordType rec)
            {
                EmitLowerRecordElement(il, rec, arrLocal, iLocal, basePtr,
                    elemSize, memoryField, reallocField, registry);
                return;
            }
            throw new NotSupportedException(
                $"List-element lower for {elemType.GetType().Name} not yet supported.");
        }

        /// <summary>
        /// Write a record-typed list element to wasm memory at
        /// <c>basePtr + i * elemSize</c>. Walks each field, computes
        /// its in-record offset, and writes via the matching
        /// MemoryHelpers.Write* helper. Strings inside the record
        /// recursively lower via LowerUtf8 then the (ptr, len) pair
        /// is written into the corresponding slot offsets.
        /// </summary>
        private static void EmitLowerRecordElement(
            ILGenerator il, CtRecordType rec,
            LocalBuilder arrLocal, LocalBuilder iLocal, LocalBuilder basePtr,
            int elemSize,
            FieldBuilder memoryField, FieldBuilder reallocField,
            TypeRegistry registry)
        {
            var fieldOffsets = CanonicalAbi.RecordFieldOffsets(rec);
            var getters = registry.RecordGetters[rec.Name];

            // Pull the element record into a local once, reused per
            // field write.
            var recClr = registry.Records[rec.Name];
            var recLocal = il.DeclareLocal(recClr);
            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stloc, recLocal);

            for (int fi = 0; fi < rec.Fields.Count; fi++)
            {
                var f = rec.Fields[fi];
                var fd = CanonicalAbi.Deref(f.Type);
                int fieldOffset = fieldOffsets[fi];
                EmitLowerRecordFieldToMemory(il, fd, getters[f.Name], recLocal,
                    basePtr, iLocal, elemSize, fieldOffset,
                    memoryField, reallocField);
            }
        }

        /// <summary>
        /// Write a single record-field value into wasm memory at
        /// <c>basePtr + i*elemSize + fieldOffset</c>.
        /// </summary>
        private static void EmitLowerRecordFieldToMemory(
            ILGenerator il, CtValType fieldType, MethodInfo getter, LocalBuilder recLocal,
            LocalBuilder basePtr, LocalBuilder iLocal, int elemSize, int fieldOffset,
            FieldBuilder memoryField, FieldBuilder reallocField)
        {
            if (fieldType is CtPrimType prim)
            {
                switch (prim.Kind)
                {
                    case CtPrim.Bool:
                    case CtPrim.S8:
                    case CtPrim.U8:
                        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                        EmitElementAddr(il, basePtr, iLocal, elemSize, fieldOffset);
                        il.Emit(OpCodes.Ldloc, recLocal);
                        il.Emit(OpCodes.Callvirt, getter);
                        il.Emit(OpCodes.Call, MemoryHelpers_WriteU8);
                        return;
                    case CtPrim.S16:
                    case CtPrim.U16:
                    case CtPrim.Char:
                        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                        EmitElementAddr(il, basePtr, iLocal, elemSize, fieldOffset);
                        il.Emit(OpCodes.Ldloc, recLocal);
                        il.Emit(OpCodes.Callvirt, getter);
                        il.Emit(OpCodes.Call, MemoryHelpers_WriteI16LE);
                        return;
                    case CtPrim.S32:
                    case CtPrim.U32:
                        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                        EmitElementAddr(il, basePtr, iLocal, elemSize, fieldOffset);
                        il.Emit(OpCodes.Ldloc, recLocal);
                        il.Emit(OpCodes.Callvirt, getter);
                        il.Emit(OpCodes.Call, MemoryHelpers_WriteI32LE);
                        return;
                    case CtPrim.S64:
                    case CtPrim.U64:
                        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                        EmitElementAddr(il, basePtr, iLocal, elemSize, fieldOffset);
                        il.Emit(OpCodes.Ldloc, recLocal);
                        il.Emit(OpCodes.Callvirt, getter);
                        il.Emit(OpCodes.Call, MemoryHelpers_WriteI64LE);
                        return;
                    case CtPrim.F32:
                        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                        EmitElementAddr(il, basePtr, iLocal, elemSize, fieldOffset);
                        il.Emit(OpCodes.Ldloc, recLocal);
                        il.Emit(OpCodes.Callvirt, getter);
                        il.Emit(OpCodes.Call, MemoryHelpers_WriteF32LE);
                        return;
                    case CtPrim.F64:
                        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                        EmitElementAddr(il, basePtr, iLocal, elemSize, fieldOffset);
                        il.Emit(OpCodes.Ldloc, recLocal);
                        il.Emit(OpCodes.Callvirt, getter);
                        il.Emit(OpCodes.Call, MemoryHelpers_WriteF64LE);
                        return;
                    case CtPrim.String:
                        {
                            // Lower the string via LowerUtf8, then write
                            // (ptr, len) into the slot at fieldOffset.
                            var ptr = il.DeclareLocal(typeof(int));
                            var len = il.DeclareLocal(typeof(int));
                            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                            il.Emit(OpCodes.Ldloc, recLocal);
                            il.Emit(OpCodes.Callvirt, getter);
                            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, reallocField);
                            il.Emit(OpCodes.Ldloca, ptr);
                            il.Emit(OpCodes.Ldloca, len);
                            il.Emit(OpCodes.Call, StringCoding_LowerUtf8);
                            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                            EmitElementAddr(il, basePtr, iLocal, elemSize, fieldOffset);
                            il.Emit(OpCodes.Ldloc, ptr);
                            il.Emit(OpCodes.Call, MemoryHelpers_WriteI32LE);
                            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
                            EmitElementAddr(il, basePtr, iLocal, elemSize, fieldOffset + 4);
                            il.Emit(OpCodes.Ldloc, len);
                            il.Emit(OpCodes.Call, MemoryHelpers_WriteI32LE);
                            return;
                        }
                }
            }
            throw new NotSupportedException(
                $"Lower-to-memory of record field of type {fieldType.GetType().Name} not yet supported.");
        }

        private static void EmitElementAddr(
            ILGenerator il, LocalBuilder basePtr, LocalBuilder iLocal, int elemSize, int offsetWithinElem)
        {
            il.Emit(OpCodes.Ldloc, basePtr);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldc_I4, elemSize);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Add);
            if (offsetWithinElem != 0)
            {
                il.Emit(OpCodes.Ldc_I4, offsetWithinElem);
                il.Emit(OpCodes.Add);
            }
        }

        private static void EmitWriteIntAt(
            ILGenerator il, FieldBuilder memoryField,
            LocalBuilder basePtr, LocalBuilder iLocal, int elemSize, int offsetWithinElem,
            LocalBuilder srcLocal)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            EmitElementAddr(il, basePtr, iLocal, elemSize, offsetWithinElem);
            il.Emit(OpCodes.Ldloc, srcLocal);
            il.Emit(OpCodes.Call, MemoryHelpers_WriteI32LE);
        }

        private static void EmitWriteByteAtElement(
            ILGenerator il, FieldBuilder memoryField,
            LocalBuilder basePtr, LocalBuilder iLocal, int elemSize, LocalBuilder arrLocal)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            EmitElementAddr(il, basePtr, iLocal, elemSize, 0);
            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Call, MemoryHelpers_WriteU8);
        }

        private static void EmitWriteI16AtElement(
            ILGenerator il, FieldBuilder memoryField,
            LocalBuilder basePtr, LocalBuilder iLocal, int elemSize, LocalBuilder arrLocal)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            EmitElementAddr(il, basePtr, iLocal, elemSize, 0);
            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldelem_I2);
            il.Emit(OpCodes.Call, MemoryHelpers_WriteI16LE);
        }

        private static void EmitWriteI32AtElement(
            ILGenerator il, FieldBuilder memoryField,
            LocalBuilder basePtr, LocalBuilder iLocal, int elemSize, LocalBuilder arrLocal)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            EmitElementAddr(il, basePtr, iLocal, elemSize, 0);
            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldelem_I4);
            il.Emit(OpCodes.Call, MemoryHelpers_WriteI32LE);
        }

        private static void EmitWriteI64AtElement(
            ILGenerator il, FieldBuilder memoryField,
            LocalBuilder basePtr, LocalBuilder iLocal, int elemSize, LocalBuilder arrLocal)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            EmitElementAddr(il, basePtr, iLocal, elemSize, 0);
            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldelem_I8);
            il.Emit(OpCodes.Call, MemoryHelpers_WriteI64LE);
        }

        private static void EmitWriteF32AtElement(
            ILGenerator il, FieldBuilder memoryField,
            LocalBuilder basePtr, LocalBuilder iLocal, int elemSize, LocalBuilder arrLocal)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            EmitElementAddr(il, basePtr, iLocal, elemSize, 0);
            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldelem_R4);
            il.Emit(OpCodes.Call, MemoryHelpers_WriteF32LE);
        }

        private static void EmitWriteF64AtElement(
            ILGenerator il, FieldBuilder memoryField,
            LocalBuilder basePtr, LocalBuilder iLocal, int elemSize, LocalBuilder arrLocal)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, memoryField);
            EmitElementAddr(il, basePtr, iLocal, elemSize, 0);
            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldelem_R8);
            il.Emit(OpCodes.Call, MemoryHelpers_WriteF64LE);
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
