// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Generates one <see cref="StandaloneContInvoker"/> subclass
    /// per unique continuation typeidx referenced by the module's
    /// cont.* instructions. Each subclass overrides <c>Invoke</c>
    /// with strongly-typed IL: cast
    /// <c>cont.StandaloneDelegate</c> to the typed
    /// <c>Func&lt;ThinContext, T1, …, TR&gt;</c> or
    /// <c>Action&lt;…&gt;</c>, unbox the <see cref="Value"/> args
    /// per the signature's params, call <c>Invoke</c>, wrap the
    /// typed result back into <c>Value[]</c>.
    ///
    /// <para>Each generated class exposes a public static
    /// <c>Instance</c> singleton. Emit sites <c>ldsfld</c> this
    /// field and pass the value to the
    /// <see cref="StackSwitchingHelpers.Resume"/> /
    /// <see cref="StackSwitchingHelpers.Switch"/> helper as the
    /// optional <c>standaloneInvoker</c> argument.</para>
    ///
    /// <para>Virtual dispatch, no reflection. AOT-safe.</para>
    /// </summary>
    internal sealed class ContInvokerEmitter
    {
        private readonly ModuleBuilder _moduleBuilder;
        private readonly string _namespace;
        private readonly ModuleInstance _moduleInst;

        // contTypeIdx → FieldInfo of the public-static Instance
        // singleton on the generated invoker class. Emit sites
        // look up by typeidx and emit ldsfld.
        private readonly Dictionary<int, FieldInfo> _instanceFields = new();

        public ContInvokerEmitter(
            ModuleBuilder mb, string @namespace, ModuleInstance moduleInst)
        {
            _moduleBuilder = mb;
            _namespace = @namespace;
            _moduleInst = moduleInst;
        }

        /// <summary>
        /// Walk every function body in the module, find cont.*
        /// instructions, collect distinct continuation typeidxs,
        /// then generate one invoker class per typeidx.
        /// </summary>
        public void EmitInvokers(IEnumerable<FunctionInstance> wasmFunctions)
        {
            var contTypeIdxs = new HashSet<int>();
            foreach (var f in wasmFunctions)
                CollectContTypeIdxs(f.Body.Instructions, contTypeIdxs);

            foreach (var ctIdx in contTypeIdxs)
                EmitInvokerFor(ctIdx);
        }

        /// <summary>
        /// Returns the static <c>Instance</c> field for the
        /// invoker class generated for <paramref name="contTypeIdx"/>.
        /// Null when no invoker was generated for this typeidx
        /// (none of the module's cont.* sites referenced it).
        /// </summary>
        public FieldInfo? GetInvokerField(int contTypeIdx) =>
            _instanceFields.TryGetValue(contTypeIdx, out var f) ? f : null;

        /// <summary>
        /// Direct access to the generated invoker registry for
        /// downstream consumers (FunctionCodegen passes this to
        /// StackSwitchingEmitter so each call site can ldsfld the
        /// right Instance singleton).
        /// </summary>
        public Dictionary<int, FieldInfo> InvokerFields => _instanceFields;

        private static void CollectContTypeIdxs(
            IEnumerable<InstructionBase> instructions, HashSet<int> sink)
        {
            foreach (var inst in instructions)
            {
                switch (inst)
                {
                    case InstContNew cn: sink.Add(cn.TypeIndex); break;
                    case InstContBind cb:
                        sink.Add(cb.TypeIndex1); sink.Add(cb.TypeIndex2); break;
                    case InstResume re: sink.Add(re.TypeIndex); break;
                    case InstResumeThrow rt: sink.Add(rt.TypeIndex); break;
                    case InstSwitch sw: sink.Add(sw.TypeIndex); break;
                }
                if (inst is IBlockInstruction bi)
                {
                    for (int i = 0; i < bi.Count; i++)
                        CollectContTypeIdxs(bi.GetBlock(i).Instructions, sink);
                }
            }
        }

        private void EmitInvokerFor(int contTypeIdx)
        {
            // Resolve $ft from $ct = cont $ft.
            var defType = _moduleInst.Types[(TypeIdx)contTypeIdx];
            if (defType.Expansion is not ContType ct) return;
            var innerDef = _moduleInst.Types[ct.FuncTypeRef.Index()];
            if (innerDef.Expansion is not FunctionType ft) return;

            // Build the strongly-typed delegate type the
            // continuation's underlying function presents at
            // runtime. The transpiler closes the ThinContext into
            // the delegate's target when populating FuncTable, so
            // the delegate signature is over (T1, …, TN) → TR —
            // no ThinContext-typed parameter.
            var paramClr = new List<Type>();
            foreach (var pt in ft.ParameterTypes.Types)
                paramClr.Add(ModuleTranspiler.MapValTypeInternal(pt, _moduleInst));

            Type delegateType;
            int resultArity = ft.ResultType.Arity;
            if (resultArity == 0)
            {
                delegateType = System.Linq.Expressions.Expression.GetActionType(paramClr.ToArray());
            }
            else if (resultArity == 1)
            {
                var returnClr = ModuleTranspiler.MapValTypeInternal(
                    ft.ResultType.Types[0], _moduleInst);
                paramClr.Add(returnClr);
                delegateType = System.Linq.Expressions.Expression.GetFuncType(paramClr.ToArray());
            }
            else
            {
                // Multi-result not yet supported in standalone
                // invokers; leave the dict entry absent and the
                // emit site falls back to the
                // NotSupportedException path documented on the
                // helper.
                return;
            }

            // Define the invoker class. Sealed + nested singleton
            // Instance field initialized in the static ctor.
            var tb = _moduleBuilder.DefineType(
                $"{_namespace}.Invoker_Cont{contTypeIdx}",
                TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(StandaloneContInvoker));

            // Default ctor.
            var ctor = tb.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                CallingConventions.Standard, Type.EmptyTypes);
            var ctorIl = ctor.GetILGenerator();
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Call,
                typeof(StandaloneContInvoker).GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null, Type.EmptyTypes, null)!);
            ctorIl.Emit(OpCodes.Ret);

            // public static readonly Instance.
            var instanceField = tb.DefineField(
                "Instance", tb,
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);

            // Static ctor: Instance = new <self>();
            var staticCtor = tb.DefineTypeInitializer();
            var scIl = staticCtor.GetILGenerator();
            scIl.Emit(OpCodes.Newobj, ctor);
            scIl.Emit(OpCodes.Stsfld, instanceField);
            scIl.Emit(OpCodes.Ret);

            // override Invoke(IContinuationContext, ContInstance, Value[]) → Value[]
            var invoke = tb.DefineMethod(
                "Invoke",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final,
                typeof(Value[]),
                new[]
                {
                    typeof(IContinuationContext),
                    typeof(ContInstance),
                    typeof(Value[])
                });
            tb.DefineMethodOverride(invoke,
                typeof(StandaloneContInvoker).GetMethod(
                    nameof(StandaloneContInvoker.Invoke))!);

            EmitInvokeBody(invoke.GetILGenerator(), delegateType, ft);

            var concrete = tb.CreateType()!;
            _instanceFields[contTypeIdx] = concrete.GetField("Instance",
                BindingFlags.Public | BindingFlags.Static)!;
        }

        // Body of the generated Invoke override.
        // Args:
        //   ldarg.0 = this
        //   ldarg.1 = IContinuationContext hctx
        //   ldarg.2 = ContInstance cont
        //   ldarg.3 = Value[] args
        private void EmitInvokeBody(ILGenerator il, Type delegateType, FunctionType ft)
        {
            var standaloneDelegateField =
                typeof(ContInstance).GetField(
                    nameof(ContInstance.StandaloneDelegate))!;
            var delLocal = il.DeclareLocal(delegateType);

            // typedDel = (DelegateType)cont.StandaloneDelegate
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldfld, standaloneDelegateField);
            il.Emit(OpCodes.Castclass, delegateType);
            il.Emit(OpCodes.Stloc, delLocal);

            // Push delegate + per-arg typed values for the
            // Invoke call. ThinContext is closed into the
            // delegate's target by the transpiler at module
            // load, so it is NOT passed here.
            il.Emit(OpCodes.Ldloc, delLocal);

            for (int i = 0; i < ft.ParameterTypes.Arity; i++)
            {
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem, typeof(Value));
                EmitUnwrapFromValue(il, ft.ParameterTypes.Types[i]);
            }

            il.Emit(OpCodes.Callvirt, delegateType.GetMethod("Invoke")!);

            // Wrap result(s) as Value[] for the caller.
            int resultArity = ft.ResultType.Arity;
            if (resultArity == 0)
            {
                il.Emit(OpCodes.Call,
                    typeof(Array).GetMethod(nameof(Array.Empty))!
                        .MakeGenericMethod(typeof(Value)));
            }
            else
            {
                // Single-result path: stash, build new Value[1].
                var resultClr = ModuleTranspiler.MapValTypeInternal(
                    ft.ResultType.Types[0], _moduleInst);
                var resultLocal = il.DeclareLocal(resultClr);
                il.Emit(OpCodes.Stloc, resultLocal);

                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Newarr, typeof(Value));
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldloc, resultLocal);
                EmitWrapAsValue(il, ft.ResultType.Types[0]);
                il.Emit(OpCodes.Stelem, typeof(Value));
            }
            il.Emit(OpCodes.Ret);
        }

        private static void EmitWrapAsValue(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32:
                    il.Emit(OpCodes.Newobj,
                        typeof(Value).GetConstructor(new[] { typeof(int) })!);
                    break;
                case ValType.I64:
                    il.Emit(OpCodes.Newobj,
                        typeof(Value).GetConstructor(new[] { typeof(long) })!);
                    break;
                case ValType.F32:
                    il.Emit(OpCodes.Newobj,
                        typeof(Value).GetConstructor(new[] { typeof(float) })!);
                    break;
                case ValType.F64:
                    il.Emit(OpCodes.Newobj,
                        typeof(Value).GetConstructor(new[] { typeof(double) })!);
                    break;
                default:
                    // Reference types are already Value on the
                    // CIL stack; no wrap needed.
                    break;
            }
        }

        private static void EmitUnwrapFromValue(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32:
                case ValType.I64:
                case ValType.F32:
                case ValType.F64:
                {
                    var dataField = typeof(Value).GetField(nameof(Value.Data))!;
                    var local = il.DeclareLocal(typeof(Value));
                    il.Emit(OpCodes.Stloc, local);
                    il.Emit(OpCodes.Ldloca, local);
                    il.Emit(OpCodes.Ldflda, dataField);
                    il.Emit(OpCodes.Ldfld, type switch
                    {
                        ValType.I32 => typeof(DUnion).GetField(nameof(DUnion.Int32))!,
                        ValType.I64 => typeof(DUnion).GetField(nameof(DUnion.Int64))!,
                        ValType.F32 => typeof(DUnion).GetField(nameof(DUnion.Float32))!,
                        ValType.F64 => typeof(DUnion).GetField(nameof(DUnion.Float64))!,
                        _ => throw new TranspilerException("unreachable"),
                    });
                    break;
                }
                default:
                    // Reference types stay as Value.
                    break;
            }
        }
    }
}
