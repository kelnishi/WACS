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
                    EmitImportCall(il, site, moduleInst);
                    break;

                case CallStrategy.TableIndirect:
                    EmitIndirectCall(il, site, moduleInst);
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
        /// ImportDispatch: load typed delegate from ctx.ImportDelegates[idx], invoke directly.
        /// No Value[] marshaling, no OpStack. Delegate signature matches WASM function type.
        /// </summary>
        private static void EmitImportCall(ILGenerator il, CallSite site, ModuleInstance moduleInst)
        {
            EmitTypedDelegateCall(il, site, ImportDelegatesField, site.FuncIdx, moduleInst);
        }

        /// <summary>
        /// TableIndirect: pack params into object[], call InvokeIndirect, unbox result.
        /// Uses DynamicInvoke for type-safe dispatch that properly traps on type mismatches.
        /// Boundary wrap (doc 2 §3): GC-ref args spilled as object and wrapped
        /// into Value before boxing, so the object[] slot holds a boxed Value
        /// (matching the interpreter's DynamicInvoke type expectation).
        /// </summary>
        private static void EmitIndirectCall(ILGenerator il, CallSite site, ModuleInstance moduleInst)
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

            bool multiReturn = resultTypes.Length > 1;

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

            // return_call_indirect terminates the caller (doc 1 §6.2). We
            // can't use the CLR `tail.` prefix through DynamicInvoke, but
            // semantic correctness only requires that execution not fall
            // through to subsequent IL: unbox the result and emit ret.
            if (site.IsTailCall)
            {
                if (multiReturn)
                    EmitUnboxResultArray(il, resultTypes, moduleInst);
                else
                    EmitUnboxResult(il, resultTypes, moduleInst);
                il.Emit(OpCodes.Ret);
                return;
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
            {
                int expectedHash;
                if (ctx.Module?.Types != null && ctx.Module.Types.Contains((TypeIdx)expectedTypeIdx))
                    expectedHash = ctx.Module.Types[(TypeIdx)expectedTypeIdx].GetHashCode();
                else if (ctx.TypeHashes != null && expectedTypeIdx < ctx.TypeHashes.Length)
                    expectedHash = ctx.TypeHashes[expectedTypeIdx];
                else
                    expectedHash = 0; // can't check — skip

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
                        ok = true; // no metadata — defer to CLR signature check below
                    }
                    if (!ok) throw new TrapException("indirect call type mismatch");
                }
            }

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

            // Type check: verify delegate parameter count matches args.
            // Full WASM type checking compares param AND result types — we check
            // param count here and catch type mismatches from DynamicInvoke below.
            var invokeMethod = del.GetType().GetMethod("Invoke");
            if (invokeMethod != null)
            {
                var delParams = invokeMethod.GetParameters();
                if (delParams.Length != args.Length)
                    throw new TrapException("indirect call type mismatch");

                // Check parameter types match
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] != null && !delParams[i].ParameterType.IsInstanceOfType(args[i]))
                        throw new TrapException("indirect call type mismatch");
                }

                // Check return type matches caller's expectation
                if (expectedReturn != null && invokeMethod.ReturnType != expectedReturn)
                    throw new TrapException("indirect call type mismatch");
                if (expectedReturn == null && invokeMethod.ReturnType != typeof(void))
                    throw new TrapException("indirect call type mismatch");
                if (expectedReturn != null && expectedReturn != typeof(void) && invokeMethod.ReturnType == typeof(void))
                    throw new TrapException("indirect call type mismatch");
            }

            try
            {
                return del.DynamicInvoke(args);
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
                return null; // unreachable
            }
            catch (Exception ex) when (ex is System.Reflection.TargetParameterCountException
                or System.ArgumentException or System.InvalidCastException)
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

            var mi = MultiReturnMethodRegistry.Get(ctx.InitDataId, resolvedFuncIdx);
            if (mi == null)
                throw new TrapException("uninitialized element");

            // Static method signature: (ThinContext ctx, p0..pN-1, out r1, …, out r_{K-1})
            // First result r0 is the return value; r1..r_{K-1} come back via
            // out-params. Total CLR args: 1 (ctx) + args.Length + (resultCount - 1).
            int outCount = resultCount - 1;
            var invokeArgs = new object?[1 + args.Length + outCount];
            invokeArgs[0] = ctx;
            for (int i = 0; i < args.Length; i++) invokeArgs[1 + i] = args[i];
            // out slots default to null — reflection fills them.

            object? r0;
            try
            {
                r0 = mi.Invoke(null, invokeArgs);
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
                return null!; // unreachable
            }
            catch (Exception ex) when (ex is System.Reflection.TargetParameterCountException
                or System.ArgumentException or System.InvalidCastException)
            {
                throw new TrapException("indirect call type mismatch");
            }

            var results = new object?[resultCount];
            results[0] = r0;
            for (int i = 0; i < outCount; i++)
                results[1 + i] = invokeArgs[1 + args.Length + i];
            return results;
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

            try
            {
                return del.DynamicInvoke(args);
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
                return null; // unreachable
            }
            catch (Exception ex) when (ex is System.Reflection.TargetParameterCountException
                or System.ArgumentException or System.InvalidCastException)
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
