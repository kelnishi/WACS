// Copyright 2025 Kelvin Nishikawa
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
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Reference;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for all WebAssembly call instructions.
    ///
    /// Each call is first resolved to a CallSite (analytical representation)
    /// then emitted according to its strategy. This separation makes the
    /// transpiler's assumptions about calling context explicit.
    /// </summary>
    internal static class CallEmitter
    {
        public static bool CanEmit(WasmOpCode op)
        {
            return op == WasmOpCode.Call
                || op == WasmOpCode.CallIndirect
                || op == WasmOpCode.CallRef
                || op == WasmOpCode.ReturnCall
                || op == WasmOpCode.ReturnCallIndirect
                || op == WasmOpCode.ReturnCallRef;
        }

        // ================================================================
        // Call site resolution — determines strategy at transpile time
        // ================================================================

        /// <summary>
        /// Resolve a call instruction to a CallSite describing the dispatch strategy.
        /// </summary>
        /// <summary>
        /// allFunctionTypes: types for ALL functions in the module index space (imports + locals).
        /// </summary>
        public static CallSite ResolveCallSite(
            InstructionBase inst, WasmOpCode op,
            FunctionInstance[] siblingFunctions, int importCount,
            ModuleInstance moduleInst,
            FunctionType[] allFunctionTypes)
        {
            switch (op)
            {
                case WasmOpCode.Call:
                case WasmOpCode.ReturnCall:
                {
                    bool tail = op == WasmOpCode.ReturnCall;
                    int funcIdx = op == WasmOpCode.Call
                        ? (int)((InstCall)inst).X.Value
                        : (int)((InstReturnCall)inst).X.Value;

                    if (funcIdx < importCount)
                    {
                        return CallSite.Import(allFunctionTypes[funcIdx], funcIdx, tail);
                    }

                    int localIdx = funcIdx - importCount;
                    var calleeType = siblingFunctions[localIdx].Type;
                    return CallSite.Direct(calleeType, localIdx, tail);
                }

                case WasmOpCode.CallIndirect:
                case WasmOpCode.ReturnCallIndirect:
                {
                    bool tail = op == WasmOpCode.ReturnCallIndirect;
                    int tableIdx, typeIdx;
                    if (op == WasmOpCode.CallIndirect)
                    {
                        var ci = (InstCallIndirect)inst;
                        tableIdx = ci.TableIndex;
                        typeIdx = ci.TypeIndex;
                    }
                    else
                    {
                        var rci = (InstReturnCallIndirect)inst;
                        tableIdx = rci.TableIndex;
                        typeIdx = rci.TypeIndex;
                    }
                    var funcType = moduleInst.Types[(TypeIdx)typeIdx].Expansion as FunctionType
                        ?? throw new TranspilerException($"Type {typeIdx} is not a function type");
                    return CallSite.Indirect(funcType, tableIdx, typeIdx, tail);
                }

                case WasmOpCode.CallRef:
                case WasmOpCode.ReturnCallRef:
                {
                    // Both use opcode 0x15 — distinguish by concrete type
                    bool tail = inst is InstReturnCallRef;
                    int typeIdx = tail
                        ? ((InstReturnCallRef)inst).TypeIndex
                        : ((InstCallRef)inst).TypeIndex;
                    var funcType = moduleInst.Types[(TypeIdx)typeIdx].Expansion as FunctionType
                        ?? throw new TranspilerException($"Type {typeIdx} is not a function type");
                    return CallSite.Ref(funcType, typeIdx, tail);
                }

                default:
                    throw new TranspilerException($"CallEmitter: unexpected opcode {op}");
            }
        }

        // ================================================================
        // IL emission — dispatches on CallSite.Strategy
        // ================================================================

        /// <summary>
        /// Emit IL for a resolved call site.
        /// </summary>
        public static void EmitCallSite(
            ILGenerator il, CallSite site,
            MethodBuilder[] siblingMethods,
            ModuleInstance moduleInst,
            TranspilerOptions? options = null)
        {
            switch (site.Strategy)
            {
                case CallStrategy.DirectSibling:
                    EmitDirectCall(il, site, siblingMethods, moduleInst, options);
                    break;

                case CallStrategy.ImportDispatch:
                    EmitImportCall(il, site, moduleInst, options);
                    break;

                case CallStrategy.TableIndirect:
                    EmitIndirectCall(il, site, moduleInst, options);
                    break;

                case CallStrategy.RefDispatch:
                    EmitRefCall(il, site, moduleInst);
                    break;
            }
        }

        /// <summary>
        /// DirectSibling: insert ThinContext under params, call MethodBuilder directly.
        /// For multi-value returns: declare locals for out params, pass ldloca, destructure after.
        /// Boundary wrap (doc 2 §3): spill GC-ref args as object, wrap to Value
        /// before call; unwrap result Value → object after call.
        /// </summary>
        private static void EmitDirectCall(
            ILGenerator il, CallSite site, MethodBuilder[] siblingMethods,
            ModuleInstance moduleInst, TranspilerOptions? options)
        {
            var targetMethod = siblingMethods[site.LocalFuncIndex];
            int paramCount = site.FuncType.ParameterTypes.Arity;
            var resultTypes = site.FuncType.ResultType.Types;
            int outParamCount = resultTypes.Length > 1 ? resultTypes.Length - 1 : 0;

            // tail. prefix: reuse stack frame for return_call to sibling.
            // Only valid when there are no out params (CLR constraint: tail. requires
            // that the callee's return value is the caller's return value directly).
            bool emitTail = site.IsTailCall
                && (options?.EmitTailCallPrefix ?? false)
                && outParamCount == 0;

            // Spill WASM params from CIL stack using INTERNAL types so GC refs
            // arrive as object; wrap to Value during the push below.
            var paramTypes = site.FuncType.ParameterTypes.Types;
            var temps = new LocalBuilder[paramCount];
            for (int i = paramCount - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(ModuleTranspiler.MapValTypeInternal(paramTypes[i], moduleInst));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Declare locals for out results. Signature uses Value at boundary.
            var outLocals = new LocalBuilder[outParamCount];
            for (int r = 0; r < outParamCount; r++)
            {
                outLocals[r] = il.DeclareLocal(ModuleTranspiler.MapValType(resultTypes[r + 1]));
            }

            // Push: ctx, params (wrapping ref temps to Value for signature), &out0, &out1, ...
            il.Emit(OpCodes.Ldarg_0);
            for (int i = 0; i < paramCount; i++)
            {
                il.Emit(OpCodes.Ldloc, temps[i]);
                if (ModuleTranspiler.IsGcRefType(paramTypes[i], moduleInst))
                {
                    il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                        nameof(GcRuntimeHelpers.WrapRef),
                        BindingFlags.Public | BindingFlags.Static)!);
                }
            }
            for (int r = 0; r < outParamCount; r++)
                il.Emit(OpCodes.Ldloca, outLocals[r]);

            if (emitTail)
                il.Emit(OpCodes.Tailcall);

            il.Emit(OpCodes.Call, targetMethod);

            if (emitTail)
            {
                il.Emit(OpCodes.Ret);
            }
            else
            {
                // Result 0 is now on the CIL stack (CLR return value, as Value
                // for ref types). Unwrap to object for GC-ref results.
                if (resultTypes.Length > 0 && ModuleTranspiler.IsGcRefType(resultTypes[0], moduleInst))
                {
                    il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                        nameof(GcRuntimeHelpers.UnwrapRef),
                        BindingFlags.Public | BindingFlags.Static)!);
                }
                // Push out results (multi-return): Value stored in out locals
                // is loaded and unwrapped if GC ref.
                for (int r = 0; r < outParamCount; r++)
                {
                    il.Emit(OpCodes.Ldloc, outLocals[r]);
                    if (ModuleTranspiler.IsGcRefType(resultTypes[r + 1], moduleInst))
                    {
                        il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                            nameof(GcRuntimeHelpers.UnwrapRef),
                            BindingFlags.Public | BindingFlags.Static)!);
                    }
                }
            }
        }

        private static readonly FieldInfo ImportDelegatesField =
            typeof(ThinContext).GetField(nameof(ThinContext.ImportDelegates))!;
        private static readonly FieldInfo FuncTableField =
            typeof(ThinContext).GetField(nameof(ThinContext.FuncTable))!;

        /// <summary>
        /// ImportDispatch: by default, load typed delegate from
        /// ctx.ImportDelegates[idx] and invoke directly. When
        /// <paramref name="options"/> carries a
        /// <see cref="TranspilerOptions.ResolverImportBindings"/> map
        /// AND this import has a binding AND the binding's typed-
        /// interface signature is primitive-compatible with the wasm
        /// function type, route to <see cref="Component.DirectLinkedImportEmit"/>
        /// instead — the call lowers to inline IL through the
        /// <see cref="ThinContext.HostBundle"/> field, skipping the
        /// delegate table entirely.
        /// </summary>
        private static void EmitImportCall(ILGenerator il, CallSite site,
            ModuleInstance moduleInst, TranspilerOptions? options)
        {
            var bindings = options?.ResolverImportBindings;
            if (bindings != null
                && bindings.TryGetValue(site.FuncIdx, out var binding)
                && options!.Resolver?.PreferredBundleType != null
                && Component.DirectLinkedImportEmit.CanEmitDirect(
                    binding, site.FuncType, options.Resolver)
                // Resource methods need a resolved resources type;
                // without one the call still falls back to the
                // legacy delegate dispatch.
                && (!binding.IsResourceMethod
                    || options.Resolver.PreferredResourcesType != null))
            {
                Component.DirectLinkedImportEmit.Emit(il, binding,
                    site.FuncType,
                    options.Resolver.PreferredBundleType,
                    options.Resolver.PreferredResourcesType,
                    options.Resolver);
                return;
            }
            EmitTypedDelegateCall(il, site, ImportDelegatesField, site.FuncIdx, moduleInst);
        }

        /// <summary>
        /// TableIndirect: dispatch <c>call_indirect</c>.
        /// <para>
        /// When eligible (single-or-zero result, non-tail) and gated on
        /// <see cref="TranspilerOptions.EmitCalliIndirect"/>, emits a
        /// dual-path body:
        /// </para>
        /// <list type="number">
        ///   <item>spill <c>elemIdx</c> + params to typed locals;</item>
        ///   <item>call <see cref="CallHelpers.ResolveIndirectFnPtr"/> —
        ///   throws TrapException on range / null-funcref / type-mismatch
        ///   exactly like the legacy path; otherwise returns either a CIL
        ///   function pointer or <c>IntPtr.Zero</c>;</item>
        ///   <item>on non-zero, push <c>ctx + args + fnPtr</c> and
        ///   <c>calli</c> directly to the local function — no allocation,
        ///   no boxing;</item>
        ///   <item>on zero (cross-module bound delegate, import slot,
        ///   unpopulated entry), branch into the legacy <c>InvokeIndirect</c>
        ///   path that builds an <c>object[]</c> + dispatches via the cached
        ///   typed wrapper.</item>
        /// </list>
        /// <para>
        /// Multi-return and tail-call paths skip the dispatcher and use the
        /// legacy emit unchanged: multi-return needs byref out-args (a
        /// future calli expansion), and the tail path already uses the
        /// 0-allocation typed-delegate <c>tail. callvirt</c> via
        /// <see cref="TryEmitTailInvoke"/>.
        /// </para>
        /// </summary>
        private static void EmitIndirectCall(
            ILGenerator il, CallSite site, ModuleInstance moduleInst,
            TranspilerOptions? options)
        {
            int paramCount = site.FuncType.ParameterTypes.Arity;
            var resultTypes = site.FuncType.ResultType.Types;

            // return_call_indirect tail path: see EmitRefCall for the rationale.
            if (site.IsTailCall)
            {
                var typedDelType = ThinContext.BuildDelegateTypeForFunc(site.FuncType);
                if (typedDelType != null && TryEmitTailInvoke(il, site, typedDelType,
                    isRef: false, moduleInst))
                    return;
            }

            // Stack: [p0, p1, ..., pN-1, elemIdx (i32 or i64 for table64)]
            il.Emit(OpCodes.Conv_I4); // safe: i32→i32 is no-op, i64→i32 truncates
            var elemIdxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, elemIdxLocal);

            // Spill params using INTERNAL types (object for GC refs).
            var paramTypes = site.FuncType.ParameterTypes.Types;
            var temps = new LocalBuilder[paramCount];
            for (int i = paramCount - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(ModuleTranspiler.MapValTypeInternal(paramTypes[i], moduleInst));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            bool multiReturn = resultTypes.Length > 1;
            bool useCalliPath =
                (options?.EmitCalliIndirect ?? false)
                && !site.IsTailCall
                && !multiReturn;

            if (useCalliPath)
            {
                // call CallHelpers.ResolveIndirectFnPtr(ctx, tableIdx,
                //   elemIdx, expectedTypeIdx) -> IntPtr. Throws on trap;
                // returns IntPtr.Zero to signal "fall back to legacy."
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, site.TableIdx);
                il.Emit(OpCodes.Ldloc, elemIdxLocal);
                il.Emit(OpCodes.Ldc_I4, site.TypeIdx);
                il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                    nameof(CallHelpers.ResolveIndirectFnPtr),
                    BindingFlags.Public | BindingFlags.Static)!);
                var fnPtrLocal = il.DeclareLocal(typeof(IntPtr));
                il.Emit(OpCodes.Stloc, fnPtrLocal);

                // if (fnPtr == 0) goto slowPath
                var slowPath = il.DefineLabel();
                var endLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, fnPtrLocal);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Conv_I);
                il.Emit(OpCodes.Beq, slowPath);

                // --- fast path: ctx, args, fnPtr → calli ---
                il.Emit(OpCodes.Ldarg_0);
                for (int i = 0; i < paramCount; i++)
                {
                    il.Emit(OpCodes.Ldloc, temps[i]);
                    if (ModuleTranspiler.IsGcRefType(paramTypes[i], moduleInst))
                    {
                        // Wrap object → Value at the signature boundary,
                        // matching what direct/sibling calls do today.
                        il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                            nameof(GcRuntimeHelpers.WrapRef),
                            BindingFlags.Public | BindingFlags.Static)!);
                    }
                }
                il.Emit(OpCodes.Ldloc, fnPtrLocal);

                // PersistedAssemblyBuilder serializes the calli signature
                // blob from EmitCalli's Type[] argument; the lower-level
                // Emit(OpCodes.Calli, SignatureHelper) overload yields a
                // BadImageFormatException at load. Build the param type
                // array (ctx + WASM params) and use the typed convenience.
                var calliParams = new Type[paramCount + 1];
                calliParams[0] = typeof(ThinContext);
                for (int i = 0; i < paramCount; i++)
                    calliParams[i + 1] = ModuleTranspiler.MapValType(paramTypes[i]);
                var calliReturn = resultTypes.Length == 1
                    ? ModuleTranspiler.MapValType(resultTypes[0])
                    : typeof(void);
                il.EmitCalli(OpCodes.Calli, CallingConventions.Standard,
                    calliReturn, calliParams, optionalParameterTypes: null);

                // For GC-ref results, unwrap Value → object after the call.
                if (resultTypes.Length == 1
                    && ModuleTranspiler.IsGcRefType(resultTypes[0], moduleInst))
                {
                    il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                        nameof(GcRuntimeHelpers.UnwrapRef),
                        BindingFlags.Public | BindingFlags.Static)!);
                }
                il.Emit(OpCodes.Br, endLabel);

                // --- slow path: legacy InvokeIndirect using the same temps ---
                il.MarkLabel(slowPath);
                EmitLegacyInvokeIndirect(il, site, moduleInst,
                    temps, paramTypes, paramCount, elemIdxLocal,
                    resultTypes, multiReturn: false);
                il.MarkLabel(endLabel);
                return;
            }

            // No calli — single legacy emit body, same as pre-phase 2.
            EmitLegacyInvokeIndirect(il, site, moduleInst,
                temps, paramTypes, paramCount, elemIdxLocal,
                resultTypes, multiReturn);

            // return_call_indirect terminates the caller. Legacy path
            // can't use CLR `tail.` through the InvokeIndirect helper,
            // but spec correctness only needs no fall-through; the
            // unboxed result is on the stack ready for ret.
            if (site.IsTailCall)
                il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// The pre-phase-2 InvokeIndirect emit body, factored out so the
        /// dual-path emit can reuse it as its IntPtr.Zero fallback. Reads
        /// from caller-supplied locals: <paramref name="temps"/> hold the
        /// already-spilled params, <paramref name="elemIdxLocal"/> holds
        /// the truncated table index. Builds the boxed <c>object[]</c>,
        /// calls <see cref="CallHelpers.InvokeIndirect"/> or
        /// <see cref="CallHelpers.InvokeIndirectMulti"/>, then unboxes.
        /// </summary>
        private static void EmitLegacyInvokeIndirect(
            ILGenerator il, CallSite site, ModuleInstance moduleInst,
            LocalBuilder[] temps, ValType[] paramTypes, int paramCount,
            LocalBuilder elemIdxLocal, ValType[] resultTypes, bool multiReturn)
        {
            // Build object[] args. Elements are boxed Value (for refs/v128) or
            // boxed primitives (for scalars). GC-ref temps are wrapped to Value
            // then boxed (not stored as raw CLR objects) so the consumer can
            // unbox uniformly.
            il.Emit(OpCodes.Ldc_I4, paramCount);
            il.Emit(OpCodes.Newarr, typeof(object));
            for (int i = 0; i < paramCount; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, temps[i]);
                if (ModuleTranspiler.IsGcRefType(paramTypes[i], moduleInst))
                {
                    il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                        nameof(GcRuntimeHelpers.WrapRef),
                        BindingFlags.Public | BindingFlags.Static)!);
                    il.Emit(OpCodes.Box, typeof(Value));
                }
                else
                {
                    il.Emit(OpCodes.Box, ModuleTranspiler.MapValType(paramTypes[i]));
                }
                il.Emit(OpCodes.Stelem_Ref);
            }
            var argsLocal = il.DeclareLocal(typeof(object[]));
            il.Emit(OpCodes.Stloc, argsLocal);

            // Call InvokeIndirect(ctx, tableIdx, elemIdx, args, expectedReturn, expectedTypeIdx)
            // or InvokeIndirectMulti(ctx, tableIdx, elemIdx, args, resultCount, expectedTypeIdx)
            // for multi-return functions (delegate dispatch can't represent
            // byref out-params, so those fall back to MethodInfo invocation
            // via MultiReturnMethodRegistry).
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, site.TableIdx);
            il.Emit(OpCodes.Ldloc, elemIdxLocal);
            il.Emit(OpCodes.Ldloc, argsLocal);

            if (multiReturn)
            {
                il.Emit(OpCodes.Ldc_I4, resultTypes.Length);
                il.Emit(OpCodes.Ldc_I4, site.TypeIdx);
                il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                    nameof(CallHelpers.InvokeIndirectMulti), BindingFlags.Public | BindingFlags.Static)!);
            }
            else
            {
                // Push expected return type for type checking
                if (resultTypes.Length > 0)
                {
                    il.Emit(OpCodes.Ldtoken, ModuleTranspiler.MapValType(resultTypes[0]));
                    il.Emit(OpCodes.Call, typeof(Type).GetMethod(
                        nameof(Type.GetTypeFromHandle), new[] { typeof(RuntimeTypeHandle) })!);
                }
                else
                {
                    il.Emit(OpCodes.Ldtoken, typeof(void));
                    il.Emit(OpCodes.Call, typeof(Type).GetMethod(
                        nameof(Type.GetTypeFromHandle), new[] { typeof(RuntimeTypeHandle) })!);
                }

                // Push expected WASM type idx so InvokeIndirect can verify
                // sub-supertype match against the function's declared type
                // (doc 1 §6.2). -1 when the site didn't carry one.
                il.Emit(OpCodes.Ldc_I4, site.TypeIdx);

                il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                    nameof(CallHelpers.InvokeIndirect), BindingFlags.Public | BindingFlags.Static)!);
            }

            // Unbox result
            if (multiReturn)
                EmitUnboxResultArray(il, resultTypes, moduleInst);
            else
                EmitUnboxResult(il, resultTypes, moduleInst);
        }

        /// <summary>
        /// RefDispatch: pack params into object[], call InvokeRef, unbox result.
        /// funcref stays as Value throughout (doc 2 §1 invariant 3); GC-ref
        /// args wrap at boundary before boxing.
        /// </summary>
        /// <summary>
        /// Emit a typed tail-call path for return_call_ref / return_call_indirect.
        /// Spills args to locals typed to match the target delegate, resolves the
        /// delegate via a helper, castclass to the typed delegate, then
        /// <c>tail. callvirt Invoke</c> + <c>ret</c>. Returns false when the signature
        /// is too wide for Action/Func (>16 params) — caller falls back to the
        /// DynamicInvoke path (which isn't tail-call capable).
        /// </summary>
        private static bool TryEmitTailInvoke(
            ILGenerator il, CallSite site, Type typedDelType, bool isRef,
            ModuleInstance moduleInst)
        {
            int paramCount = site.FuncType.ParameterTypes.Arity;
            var resultTypes = site.FuncType.ResultType.Types;
            // Multi-return tail calls would need CLR support for matching
            // out-param signatures on both sides — not supported yet.
            if (resultTypes.Length > 1) return false;

            var paramTypes = site.FuncType.ParameterTypes.Types;
            var argLocals = new LocalBuilder[paramCount];

            if (isRef)
            {
                // Stack: [p0, ..., pN-1, funcref (Value)]
                var funcRefLocal = il.DeclareLocal(typeof(Value));
                il.Emit(OpCodes.Stloc, funcRefLocal);

                // Spill params in reverse (stack top first).
                for (int i = paramCount - 1; i >= 0; i--)
                {
                    argLocals[i] = il.DeclareLocal(
                        ModuleTranspiler.MapValTypeInternal(paramTypes[i], moduleInst));
                    il.Emit(OpCodes.Stloc, argLocals[i]);
                }

                // Resolve delegate: ctx + funcref → Delegate
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, funcRefLocal);
                il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                    nameof(CallHelpers.ResolveRefDelegate),
                    BindingFlags.Public | BindingFlags.Static)!);
            }
            else
            {
                // Stack: [p0, ..., pN-1, elemIdx (i32 or i64)]
                il.Emit(OpCodes.Conv_I4);
                var elemIdxLocal = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Stloc, elemIdxLocal);

                for (int i = paramCount - 1; i >= 0; i--)
                {
                    argLocals[i] = il.DeclareLocal(
                        ModuleTranspiler.MapValTypeInternal(paramTypes[i], moduleInst));
                    il.Emit(OpCodes.Stloc, argLocals[i]);
                }

                // Resolve delegate: ctx + tableIdx + elemIdx + expectedTypeIdx → Delegate
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, site.TableIdx);
                il.Emit(OpCodes.Ldloc, elemIdxLocal);
                il.Emit(OpCodes.Ldc_I4, site.TypeIdx);
                il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                    nameof(CallHelpers.ResolveIndirectDelegate),
                    BindingFlags.Public | BindingFlags.Static)!);
            }

            il.Emit(OpCodes.Castclass, typedDelType);

            // Push typed args. GC-ref params flow as object internally; wrap
            // to Value at the signature boundary (MapValType = Value for refs).
            for (int i = 0; i < paramCount; i++)
            {
                il.Emit(OpCodes.Ldloc, argLocals[i]);
                if (ModuleTranspiler.IsGcRefType(paramTypes[i], moduleInst))
                {
                    il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                        nameof(GcRuntimeHelpers.WrapRef),
                        BindingFlags.Public | BindingFlags.Static)!);
                }
            }

            var invokeMethod = typedDelType.GetMethod("Invoke")!;
            il.Emit(OpCodes.Tailcall);
            il.Emit(OpCodes.Callvirt, invokeMethod);
            il.Emit(OpCodes.Ret);
            return true;
        }

        private static void EmitRefCall(ILGenerator il, CallSite site, ModuleInstance moduleInst)
        {
            int paramCount = site.FuncType.ParameterTypes.Arity;
            var resultTypes = site.FuncType.ResultType.Types;

            // return_call_ref tail path: resolve the delegate via helper,
            // castclass to the typed delegate, tail. callvirt Invoke, ret.
            // Bypasses InvokeRef's DynamicInvoke (which would both allocate
            // and block the CLR's tail-call optimization). Falls through
            // to the generic DynamicInvoke path when no typed delegate can
            // be built (>16 params or an unusual signature).
            if (site.IsTailCall)
            {
                var typedDelType = ThinContext.BuildDelegateTypeForFunc(site.FuncType);
                if (typedDelType != null && TryEmitTailInvoke(il, site, typedDelType,
                    isRef: true, moduleInst))
                    return;
            }

            // Stack: [p0, ..., pN-1, funcref (Value)]
            var funcRefLocal = il.DeclareLocal(typeof(Value));
            il.Emit(OpCodes.Stloc, funcRefLocal);

            var paramTypes = site.FuncType.ParameterTypes.Types;
            var temps = new LocalBuilder[paramCount];
            for (int i = paramCount - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(ModuleTranspiler.MapValTypeInternal(paramTypes[i], moduleInst));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Build object[] args. GC-ref params wrap to Value before boxing;
            // scalars/Value params box directly.
            il.Emit(OpCodes.Ldc_I4, paramCount);
            il.Emit(OpCodes.Newarr, typeof(object));
            for (int i = 0; i < paramCount; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, temps[i]);
                if (ModuleTranspiler.IsGcRefType(paramTypes[i], moduleInst))
                {
                    il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                        nameof(GcRuntimeHelpers.WrapRef),
                        BindingFlags.Public | BindingFlags.Static)!);
                    il.Emit(OpCodes.Box, typeof(Value));
                }
                else
                {
                    il.Emit(OpCodes.Box, ModuleTranspiler.MapValType(paramTypes[i]));
                }
                il.Emit(OpCodes.Stelem_Ref);
            }
            var argsLocal = il.DeclareLocal(typeof(object[]));
            il.Emit(OpCodes.Stloc, argsLocal);

            // Call InvokeRef(ctx, funcref, args)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, funcRefLocal);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                nameof(CallHelpers.InvokeRef), BindingFlags.Public | BindingFlags.Static)!);

            if (site.IsTailCall)
            {
                // return_call_ref terminates the caller (doc 1 §6.2) — unbox
                // and ret so subsequent IL doesn't fall through.
                EmitUnboxResult(il, resultTypes, moduleInst);
                il.Emit(OpCodes.Ret);
                return;
            }

            // Unbox result
            EmitUnboxResult(il, resultTypes, moduleInst);
        }

        /// <summary>
        /// Unbox the object? result from DynamicInvoke to the expected CIL stack type.
        /// GC-ref results are unboxed as Value, then unwrapped to object so they
        /// land on the internal stack in object form (doc 2 §3).
        /// </summary>
        private static void EmitUnboxResult(ILGenerator il, ValType[] resultTypes, ModuleInstance moduleInst)
        {
            if (resultTypes.Length == 0)
            {
                il.Emit(OpCodes.Pop); // Discard null from DynamicInvoke
                return;
            }

            var resultClrType = ModuleTranspiler.MapValType(resultTypes[0]);
            il.Emit(OpCodes.Unbox_Any, resultClrType);
            if (ModuleTranspiler.IsGcRefType(resultTypes[0], moduleInst))
            {
                il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                    nameof(GcRuntimeHelpers.UnwrapRef),
                    BindingFlags.Public | BindingFlags.Static)!);
            }
        }

        /// <summary>
        /// Unpack a multi-return result array (object?[] on the stack, length =
        /// resultTypes.Length) into N typed values on the CIL stack. Stored to a
        /// local so each element can be loaded and unboxed in WASM stack order
        /// (result[0] deepest, result[N-1] on top).
        /// </summary>
        private static void EmitUnboxResultArray(ILGenerator il, ValType[] resultTypes, ModuleInstance moduleInst)
        {
            var arrLocal = il.DeclareLocal(typeof(object[]));
            il.Emit(OpCodes.Stloc, arrLocal);
            for (int i = 0; i < resultTypes.Length; i++)
            {
                il.Emit(OpCodes.Ldloc, arrLocal);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);
                var clr = ModuleTranspiler.MapValType(resultTypes[i]);
                il.Emit(OpCodes.Unbox_Any, clr);
                if (ModuleTranspiler.IsGcRefType(resultTypes[i], moduleInst))
                {
                    il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                        nameof(GcRuntimeHelpers.UnwrapRef),
                        BindingFlags.Public | BindingFlags.Static)!);
                }
            }
        }

        /// <summary>
        /// Emit a typed delegate invocation from a delegate array field.
        /// Boundary wrap (doc 2 §3): GC-ref args spilled as object, wrapped to
        /// Value before Invoke (delegate signature is Value for refs); ref
        /// result Value → object after Invoke.
        /// </summary>
        private static void EmitTypedDelegateCall(ILGenerator il, CallSite site, FieldInfo arrayField, int index, ModuleInstance moduleInst)
        {
            int paramCount = site.FuncType.ParameterTypes.Arity;
            var paramTypes = site.FuncType.ParameterTypes.Types;
            var resultTypes = site.FuncType.ResultType.Types;

            // Spill params using INTERNAL types (object for GC refs).
            var temps = new LocalBuilder[paramCount];
            for (int i = paramCount - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(ModuleTranspiler.MapValTypeInternal(paramTypes[i], moduleInst));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Load delegate: ctx.arrayField[index]
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, arrayField);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldelem_Ref);

            // Cast to typed Func<>/Action<> (signature uses Value for refs).
            var delegateType = BuildDelegateType(site.FuncType);
            il.Emit(OpCodes.Castclass, delegateType);

            // Push params, wrapping GC refs to Value for signature match.
            for (int i = 0; i < paramCount; i++)
            {
                il.Emit(OpCodes.Ldloc, temps[i]);
                if (ModuleTranspiler.IsGcRefType(paramTypes[i], moduleInst))
                {
                    il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                        nameof(GcRuntimeHelpers.WrapRef),
                        BindingFlags.Public | BindingFlags.Static)!);
                }
            }

            // Invoke
            il.Emit(OpCodes.Callvirt, delegateType.GetMethod("Invoke")!);

            // Unwrap Value → object for GC-ref result.
            if (resultTypes.Length > 0 && ModuleTranspiler.IsGcRefType(resultTypes[0], moduleInst))
            {
                il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                    nameof(GcRuntimeHelpers.UnwrapRef),
                    BindingFlags.Public | BindingFlags.Static)!);
            }
        }

        /// <summary>
        /// Build the CLR delegate type matching a WASM function signature.
        /// (param i32 i64) (result f32) → Func&lt;int, long, float&gt;
        /// </summary>
        internal static Type? BuildDelegateType(FunctionType funcType)
        {
            var paramClrTypes = funcType.ParameterTypes.Types
                .Select(t => ModuleTranspiler.MapValType(t)).ToArray();
            var resultTypes = funcType.ResultType.Types;

            if (resultTypes.Length == 0)
            {
                return paramClrTypes.Length switch
                {
                    0  => typeof(Action),
                    1  => typeof(Action<>).MakeGenericType(paramClrTypes),
                    2  => typeof(Action<,>).MakeGenericType(paramClrTypes),
                    3  => typeof(Action<,,>).MakeGenericType(paramClrTypes),
                    4  => typeof(Action<,,,>).MakeGenericType(paramClrTypes),
                    5  => typeof(Action<,,,,>).MakeGenericType(paramClrTypes),
                    6  => typeof(Action<,,,,,>).MakeGenericType(paramClrTypes),
                    7  => typeof(Action<,,,,,,>).MakeGenericType(paramClrTypes),
                    8  => typeof(Action<,,,,,,,>).MakeGenericType(paramClrTypes),
                    9  => typeof(Action<,,,,,,,,>).MakeGenericType(paramClrTypes),
                    10 => typeof(Action<,,,,,,,,,>).MakeGenericType(paramClrTypes),
                    11 => typeof(Action<,,,,,,,,,,>).MakeGenericType(paramClrTypes),
                    12 => typeof(Action<,,,,,,,,,,,>).MakeGenericType(paramClrTypes),
                    13 => typeof(Action<,,,,,,,,,,,,>).MakeGenericType(paramClrTypes),
                    14 => typeof(Action<,,,,,,,,,,,,,>).MakeGenericType(paramClrTypes),
                    15 => typeof(Action<,,,,,,,,,,,,,,>).MakeGenericType(paramClrTypes),
                    16 => typeof(Action<,,,,,,,,,,,,,,,>).MakeGenericType(paramClrTypes),
                    _  => null // >16 params not supported by Action<>
                };
            }

            var returnType = ModuleTranspiler.MapValType(resultTypes[0]);
            var allTypes = paramClrTypes.Append(returnType).ToArray();
            return allTypes.Length switch
            {
                1  => typeof(Func<>).MakeGenericType(allTypes),
                2  => typeof(Func<,>).MakeGenericType(allTypes),
                3  => typeof(Func<,,>).MakeGenericType(allTypes),
                4  => typeof(Func<,,,>).MakeGenericType(allTypes),
                5  => typeof(Func<,,,,>).MakeGenericType(allTypes),
                6  => typeof(Func<,,,,,>).MakeGenericType(allTypes),
                7  => typeof(Func<,,,,,,>).MakeGenericType(allTypes),
                8  => typeof(Func<,,,,,,,>).MakeGenericType(allTypes),
                9  => typeof(Func<,,,,,,,,>).MakeGenericType(allTypes),
                10 => typeof(Func<,,,,,,,,,>).MakeGenericType(allTypes),
                11 => typeof(Func<,,,,,,,,,,>).MakeGenericType(allTypes),
                12 => typeof(Func<,,,,,,,,,,,>).MakeGenericType(allTypes),
                13 => typeof(Func<,,,,,,,,,,,,>).MakeGenericType(allTypes),
                14 => typeof(Func<,,,,,,,,,,,,,>).MakeGenericType(allTypes),
                15 => typeof(Func<,,,,,,,,,,,,,,>).MakeGenericType(allTypes),
                16 => typeof(Func<,,,,,,,,,,,,,,,>).MakeGenericType(allTypes),
                17 => typeof(Func<,,,,,,,,,,,,,,,,>).MakeGenericType(allTypes),
                _  => null // >16 params + return not supported by Func<>
            };
        }

        // ================================================================
        // Value[] marshaling helpers
        // ================================================================

        private static void EmitSpillParamsToArray(
            ILGenerator il, ValType[] paramTypes, int count, out LocalBuilder arrayLocal)
        {
            var temps = new LocalBuilder[count];
            for (int i = count - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(ModuleTranspiler.MapValType(paramTypes[i]));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            arrayLocal = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Ldc_I4, count);
            il.Emit(OpCodes.Newarr, typeof(Value));
            il.Emit(OpCodes.Stloc, arrayLocal);

            for (int i = 0; i < count; i++)
            {
                il.Emit(OpCodes.Ldloc, arrayLocal);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, temps[i]);
                EmitBoxToValue(il, paramTypes[i]);
                il.Emit(OpCodes.Stelem, typeof(Value));
            }
        }

        private static void EmitUnpackResults(ILGenerator il, ValType[] resultTypes, int count)
        {
            if (count == 0)
            {
                il.Emit(OpCodes.Pop);
                return;
            }

            var resultsLocal = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Stloc, resultsLocal);

            for (int i = 0; i < count; i++)
            {
                il.Emit(OpCodes.Ldloc, resultsLocal);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem, typeof(Value));
                EmitUnboxFromValue(il, resultTypes[i]);
            }
        }

        private static readonly FieldInfo DataField =
            typeof(Value).GetField(nameof(Value.Data))!;
        private static readonly FieldInfo Int32Field =
            typeof(DUnion).GetField(nameof(DUnion.Int32))!;
        private static readonly FieldInfo Int64Field =
            typeof(DUnion).GetField(nameof(DUnion.Int64))!;
        private static readonly FieldInfo Float32Field =
            typeof(DUnion).GetField(nameof(DUnion.Float32))!;
        private static readonly FieldInfo Float64Field =
            typeof(DUnion).GetField(nameof(DUnion.Float64))!;

        private static void EmitBoxToValue(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(int) })!);
                    break;
                case ValType.I64:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(long) })!);
                    break;
                case ValType.F32:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(float) })!);
                    break;
                case ValType.F64:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(double) })!);
                    break;
                default:
                    break; // Reference types are already Value
            }
        }

        private static void EmitUnboxFromValue(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32:
                case ValType.I64:
                case ValType.F32:
                case ValType.F64:
                {
                    var local = il.DeclareLocal(typeof(Value));
                    il.Emit(OpCodes.Stloc, local);
                    il.Emit(OpCodes.Ldloca, local);
                    il.Emit(OpCodes.Ldflda, DataField);
                    il.Emit(OpCodes.Ldfld, type switch
                    {
                        ValType.I32 => Int32Field,
                        ValType.I64 => Int64Field,
                        ValType.F32 => Float32Field,
                        ValType.F64 => Float64Field,
                        _ => throw new TranspilerException("unreachable")
                    });
                    break;
                }
                default:
                    break; // Reference types stay as Value
            }
        }
    }

    /// <summary>
    /// Runtime helpers for call resolution.
    /// These resolve funcref/table lookups to FuncTable indices.
    /// The actual invocation is a typed delegate call emitted by the transpiler.
    /// No OpStack, no ExecContext, no Value[] marshaling.
    /// </summary>
    public static class CallHelpers
    {
        /// <summary>
        /// Doc 1 §6.2 type-equivalence check shared by every
        /// call_indirect resolver path: declared type of the callee
        /// must be a subtype of the call_indirect's type operand.
        /// Throws TrapException("indirect call type mismatch") on
        /// failure; returns silently when the runtime can't verify
        /// (no type metadata for the call site or callee — falls back
        /// to the CLR signature check at dispatch time).
        /// </summary>
        private static void VerifyFuncTypeMatch(
            ThinContext ctx, int resolvedFuncIdx, int expectedTypeIdx)
        {
            int expectedHash;
            if (ctx.Module?.Types != null && ctx.Module.Types.Contains((TypeIdx)expectedTypeIdx))
                expectedHash = ctx.Module.Types[(TypeIdx)expectedTypeIdx].GetHashCode();
            else if (ctx.TypeHashes != null && expectedTypeIdx < ctx.TypeHashes.Length)
                expectedHash = ctx.TypeHashes[expectedTypeIdx];
            else
                return; // can't check — skip

            if (expectedHash == 0) return;

            bool ok;
            if (ctx.FuncTypeSuperHashes != null
                && resolvedFuncIdx >= 0 && resolvedFuncIdx < ctx.FuncTypeSuperHashes.Length
                && ctx.FuncTypeSuperHashes[resolvedFuncIdx] != null)
            {
                var chain = ctx.FuncTypeSuperHashes[resolvedFuncIdx];
                ok = false;
                for (int i = 0; i < chain.Length; i++)
                    if (chain[i] == expectedHash) { ok = true; break; }
            }
            else if (ctx.FuncTypeHashes != null
                && resolvedFuncIdx >= 0 && resolvedFuncIdx < ctx.FuncTypeHashes.Length)
            {
                ok = ctx.FuncTypeHashes[resolvedFuncIdx] == expectedHash;
            }
            else
            {
                ok = true; // no metadata — defer to CLR signature check
            }
            if (!ok) throw new TrapException("indirect call type mismatch");
        }

        /// <summary>
        /// Resolve call_indirect: table lookup + null check → FuncTable index.
        /// Returns the FuncAddr value which is the index into FuncTable.
        /// </summary>
        public static int ResolveIndirect(ThinContext ctx, int tableIdx, int elemIdx)
        {
            var table = ctx.Tables[tableIdx];
            if (elemIdx < 0 || elemIdx >= table.Elements.Count)
                throw new TrapException($"undefined element {elemIdx}");

            var r = table.Elements[elemIdx];
            if (r.IsNullRef)
                throw new TrapException("uninitialized element");

            // Extract FuncAddr from the funcref Value
            if (ctx.Types != null)
                return (int)r.GetFuncAddr(ctx.Types).Value;

            // Standalone fallback: FuncAddr is stored in Data.Ptr
            return (int)r.Data.Ptr;
        }

        /// <summary>
        /// Fast-path resolver for the calli emit (gated by
        /// <see cref="TranspilerOptions.EmitCalliIndirect"/>). Performs the
        /// same range / null-funcref / type-equivalence checks as
        /// <see cref="InvokeIndirect"/> — traps with identical messages —
        /// and then either returns the local function's CIL function
        /// pointer from <see cref="ThinContext.LocalFnPtrs"/> or
        /// <c>IntPtr.Zero</c> when calli can't dispatch the call (cross-
        /// module bound delegate, import slot, or unpopulated entry).
        /// The emitted IL branches on zero and falls back to the legacy
        /// <see cref="InvokeIndirect"/> path; spec-correctness is
        /// independent of which path runs.
        /// </summary>
        public static IntPtr ResolveIndirectFnPtr(
            ThinContext ctx, int tableIdx, int elemIdx, int expectedTypeIdx)
        {
            var table = ctx.Tables[tableIdx];
            if ((uint)elemIdx >= (uint)table.Elements.Count)
                throw new TrapException($"undefined element {elemIdx}");

            var r = table.Elements[elemIdx];
            if (r.IsNullRef)
                throw new TrapException("uninitialized element");

            int resolvedFuncIdx = ctx.Types != null
                ? (int)r.GetFuncAddr(ctx.Types).Value
                : (int)r.Data.Ptr;

            if (expectedTypeIdx >= 0)
                VerifyFuncTypeMatch(ctx, resolvedFuncIdx, expectedTypeIdx);

            // Cross-module bound delegate: there's no local IntPtr
            // because the target lives in another module's emit.
            // Fall back to the delegate path.
            if (r.GcRef is DelegateRef) return IntPtr.Zero;

            var ptrs = ctx.LocalFnPtrs;
            if (ptrs == null
                || (uint)resolvedFuncIdx >= (uint)ptrs.Length)
                return IntPtr.Zero;

            // Zero entry = import slot or otherwise unpopulated. Fall
            // back to the legacy delegate dispatch — InvokeIndirect
            // will still trap on missing FuncTable entries.
            return ptrs[resolvedFuncIdx];
        }

        /// <summary>
        /// Resolve and invoke call_indirect in one step.
        /// Returns the result as object (null for void).
        /// Validates delegate signature matches expected arg count before invoking.
        /// </summary>
        public static object? InvokeIndirect(
            ThinContext ctx, int tableIdx, int elemIdx, object?[] args,
            Type? expectedReturn = null,
            int expectedTypeIdx = -1)
        {
            var table = ctx.Tables[tableIdx];
            if (elemIdx < 0 || elemIdx >= table.Elements.Count)
                throw new TrapException($"undefined element {elemIdx}");

            var r = table.Elements[elemIdx];
            if (r.IsNullRef)
                throw new TrapException("uninitialized element");

            // Resolve funcIdx once so we can check the WASM-declared type
            // (including sub-supertypes) before dispatching. The delegate's
            // CLR signature alone is lossy: two WASM func types may lower
            // to the same CLR delegate shape yet not be subtype-related.
            int resolvedFuncIdx = -1;
            if (ctx.Types != null)
                resolvedFuncIdx = (int)r.GetFuncAddr(ctx.Types).Value;
            else
                resolvedFuncIdx = (int)r.Data.Ptr;

            // WASM type-equivalence check (doc 1 §6.2): declared type of the
            // callee must be a subtype of the call_indirect's type operand.
            // Skip when the call site didn't carry a type idx (call_ref path
            // routes here without one).
            if (expectedTypeIdx >= 0)
                VerifyFuncTypeMatch(ctx, resolvedFuncIdx, expectedTypeIdx);

            // Try to get delegate directly from the table element (cross-module path).
            // Bound delegates are stored as DelegateRef in GcRef on funcref Values.
            Delegate? del = (r.GcRef as DelegateRef)?.Target;

            if (del == null)
            {
                if (resolvedFuncIdx < 0 || resolvedFuncIdx >= ctx.FuncTable.Length)
                    throw new TrapException("undefined element");

                del = ctx.FuncTable[resolvedFuncIdx];
                if (del == null)
                    throw new TrapException("uninitialized element");
            }

            // WASM validation guaranteed type compatibility at this call
            // site (argc + types checked at module validation, plus the
            // subtype hash check above). Skip per-call reflection and call
            // through a cached typed wrapper — `Delegate.DynamicInvoke` is
            // a known hot spot (fib/fac/runaway via call_indirect ran ~46
            // minutes on CI because every indirect call went through
            // reflection). The wrapper is JIT-compiled once per delegate
            // type: unbox each boxed arg, call the typed Invoke directly,
            // box the result.
            var invoker = TypedDelegateInvoker.GetOrBuild(del.GetType());
            try
            {
                return invoker(del, args);
            }
            catch (InvalidCastException)
            {
                throw new TrapException("indirect call type mismatch");
            }
        }

        /// <summary>
        /// Multi-return variant of InvokeIndirect. Dispatches to a
        /// MethodInfo registered in MultiReturnMethodRegistry (the target
        /// function's FuncTable slot is null because Action/Func can't
        /// represent byref out-params). Invokes with `ctx + args + out-slots`
        /// and returns all N results packed in an object[] so the caller can
        /// unbox each one in place.
        /// </summary>
        public static object?[] InvokeIndirectMulti(
            ThinContext ctx, int tableIdx, int elemIdx, object?[] args,
            int resultCount, int expectedTypeIdx = -1)
        {
            var table = ctx.Tables[tableIdx];
            if (elemIdx < 0 || elemIdx >= table.Elements.Count)
                throw new TrapException($"undefined element {elemIdx}");
            var r = table.Elements[elemIdx];
            if (r.IsNullRef)
                throw new TrapException("uninitialized element");

            int resolvedFuncIdx = ctx.Types != null
                ? (int)r.GetFuncAddr(ctx.Types).Value
                : (int)r.Data.Ptr;

            if (expectedTypeIdx >= 0)
            {
                int expectedHash;
                if (ctx.Module?.Types != null && ctx.Module.Types.Contains((TypeIdx)expectedTypeIdx))
                    expectedHash = ctx.Module.Types[(TypeIdx)expectedTypeIdx].GetHashCode();
                else if (ctx.TypeHashes != null && expectedTypeIdx < ctx.TypeHashes.Length)
                    expectedHash = ctx.TypeHashes[expectedTypeIdx];
                else
                    expectedHash = 0;

                if (expectedHash != 0)
                {
                    bool ok = false;
                    if (ctx.FuncTypeSuperHashes != null
                        && resolvedFuncIdx >= 0 && resolvedFuncIdx < ctx.FuncTypeSuperHashes.Length
                        && ctx.FuncTypeSuperHashes[resolvedFuncIdx] != null)
                    {
                        var chain = ctx.FuncTypeSuperHashes[resolvedFuncIdx];
                        for (int i = 0; i < chain.Length; i++)
                            if (chain[i] == expectedHash) { ok = true; break; }
                    }
                    else if (ctx.FuncTypeHashes != null
                        && resolvedFuncIdx >= 0 && resolvedFuncIdx < ctx.FuncTypeHashes.Length)
                    {
                        ok = ctx.FuncTypeHashes[resolvedFuncIdx] == expectedHash;
                    }
                    else
                    {
                        ok = true;
                    }
                    if (!ok) throw new TrapException("indirect call type mismatch");
                }
            }

            var invoker = MultiReturnMethodRegistry.Get(ctx.InitDataId, resolvedFuncIdx);
            if (invoker == null)
                throw new TrapException("uninitialized element");

            // The invoker is a DynamicMethod-compiled adapter that calls the
            // target's static method directly. Pre-reflection mi.Invoke was
            // ~100x slower, which meant call_indirect.wast took 46+ minutes
            // in CI on Linux x64 tight loops; the JIT-compiled path brings
            // it back into the seconds range.
            try
            {
                return invoker(ctx, args);
            }
            catch (InvalidCastException)
            {
                throw new TrapException("indirect call type mismatch");
            }
        }

        /// <summary>
        /// Resolve and invoke call_ref in one step.
        /// </summary>
        public static object? InvokeRef(
            ThinContext ctx, Value funcRef, object?[] args)
        {
            if (funcRef.IsNullRef)
                throw new TrapException("null function reference");

            // Try delegate from the Value itself (cross-module path)
            Delegate? del = (funcRef.GcRef as DelegateRef)?.Target;

            if (del == null)
            {
                // Fallback: module-local FuncTable
                int funcIdx = ResolveRef(ctx, funcRef);
                if (funcIdx < 0 || funcIdx >= ctx.FuncTable.Length)
                    throw new TrapException("undefined element");
                del = ctx.FuncTable[funcIdx];
            }
            if (del == null)
                throw new TrapException("uninitialized element");

            // Same typed-wrapper fast path as InvokeIndirect — avoids
            // DynamicInvoke's reflection overhead in call_ref hot loops.
            var invoker = TypedDelegateInvoker.GetOrBuild(del.GetType());
            try
            {
                return invoker(del, args);
            }
            catch (InvalidCastException)
            {
                throw new TrapException("indirect call type mismatch");
            }
        }

        /// <summary>
        /// Resolve call_ref: funcref → FuncTable index.
        /// </summary>
        public static int ResolveRef(ThinContext ctx, Value funcRef)
        {
            if (funcRef.IsNullRef)
                throw new TrapException("null function reference");

            if (ctx.Types != null)
                return (int)funcRef.GetFuncAddr(ctx.Types).Value;

            return (int)funcRef.Data.Ptr;
        }

        /// <summary>
        /// Resolve a funcref Value to its bound CLR Delegate for typed dispatch.
        /// Used by return_call_ref's tail-call emission so the call site can
        /// castclass the result to the typed delegate and <c>tail. callvirt Invoke</c>
        /// directly (no DynamicInvoke overhead, no extra stack frame for the helper
        /// to hold).
        /// </summary>
        public static Delegate ResolveRefDelegate(ThinContext ctx, Value funcRef)
        {
            if (funcRef.IsNullRef)
                throw new TrapException("null function reference");
            var del = (funcRef.GcRef as DelegateRef)?.Target;
            if (del != null) return del;
            int funcIdx = ctx.Types != null
                ? (int)funcRef.GetFuncAddr(ctx.Types).Value
                : (int)funcRef.Data.Ptr;
            if (funcIdx < 0 || funcIdx >= ctx.FuncTable.Length)
                throw new TrapException("undefined element");
            var tableDel = ctx.FuncTable[funcIdx];
            if (tableDel == null)
                throw new TrapException("uninitialized element");
            return tableDel;
        }

        /// <summary>
        /// Resolve a table element to its bound CLR Delegate, with a WASM-level
        /// sub-supertype check against <paramref name="expectedTypeIdx"/>. Used by
        /// return_call_indirect's tail-call emission (see <see cref="ResolveRefDelegate"/>).
        /// </summary>
        public static Delegate ResolveIndirectDelegate(
            ThinContext ctx, int tableIdx, int elemIdx, int expectedTypeIdx)
        {
            var table = ctx.Tables[tableIdx];
            if (elemIdx < 0 || elemIdx >= table.Elements.Count)
                throw new TrapException($"undefined element {elemIdx}");
            var r = table.Elements[elemIdx];
            if (r.IsNullRef) throw new TrapException("uninitialized element");

            int resolvedFuncIdx = ctx.Types != null
                ? (int)r.GetFuncAddr(ctx.Types).Value
                : (int)r.Data.Ptr;

            // WASM-level sub-supertype check (doc 1 §6.2).
            if (expectedTypeIdx >= 0)
            {
                int expectedHash;
                if (ctx.Module?.Types != null && ctx.Module.Types.Contains((TypeIdx)expectedTypeIdx))
                    expectedHash = ctx.Module.Types[(TypeIdx)expectedTypeIdx].GetHashCode();
                else if (ctx.TypeHashes != null && expectedTypeIdx < ctx.TypeHashes.Length)
                    expectedHash = ctx.TypeHashes[expectedTypeIdx];
                else
                    expectedHash = 0;

                if (expectedHash != 0)
                {
                    bool ok = false;
                    if (ctx.FuncTypeSuperHashes != null
                        && resolvedFuncIdx >= 0 && resolvedFuncIdx < ctx.FuncTypeSuperHashes.Length
                        && ctx.FuncTypeSuperHashes[resolvedFuncIdx] != null)
                    {
                        var chain = ctx.FuncTypeSuperHashes[resolvedFuncIdx];
                        for (int i = 0; i < chain.Length; i++)
                            if (chain[i] == expectedHash) { ok = true; break; }
                    }
                    else if (ctx.FuncTypeHashes != null
                        && resolvedFuncIdx >= 0 && resolvedFuncIdx < ctx.FuncTypeHashes.Length)
                    {
                        ok = ctx.FuncTypeHashes[resolvedFuncIdx] == expectedHash;
                    }
                    else
                    {
                        ok = true;
                    }
                    if (!ok) throw new TrapException("indirect call type mismatch");
                }
            }

            var del = (r.GcRef as DelegateRef)?.Target;
            if (del != null) return del;
            if (resolvedFuncIdx < 0 || resolvedFuncIdx >= ctx.FuncTable.Length)
                throw new TrapException("undefined element");
            var tableDel = ctx.FuncTable[resolvedFuncIdx];
            if (tableDel == null) throw new TrapException("uninitialized element");
            return tableDel;
        }

        /// <summary>
        /// Fallback dispatch for functions that weren't transpiled.
        /// Packs arguments into Value[], invokes through the interpreter's ExecContext,
        /// and returns results as Value[].
        ///
        /// Called from the fallback method body instead of throwing NotSupportedException.
        /// In standalone mode (no ExecContext), throws NotSupportedException as before.
        /// </summary>
        public static Value[] InvokeFallback(ThinContext ctx, int funcIndex, Value[] args)
        {
            if (ctx.ExecContext == null || ctx.Module == null)
                throw new NotSupportedException(
                    $"Function {funcIndex} not transpiled and no interpreter available");

            // Get the FuncAddr for this function from the module's index space
            int idx = 0;
            foreach (var addr in ctx.Module.FuncAddrs)
            {
                if (idx == funcIndex)
                {
                    // Push args onto OpStack
                    for (int i = 0; i < args.Length; i++)
                        ctx.ExecContext.OpStack.PushValue(args[i]);

                    // Invoke through interpreter
                    ctx.ExecContext.Invoke(addr);

                    // Pop results
                    var func = ctx.Store![addr];
                    int resultCount = func.Type.ResultType.Arity;
                    var results = new Value[resultCount];
                    for (int r = resultCount - 1; r >= 0; r--)
                        results[r] = ctx.ExecContext.OpStack.PopAny();
                    return results;
                }
                idx++;
            }

            throw new TrapException($"Function index {funcIndex} not found in module");
        }
    }
}
