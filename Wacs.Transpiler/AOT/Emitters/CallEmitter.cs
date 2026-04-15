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
            ModuleInstance moduleInst)
        {
            switch (site.Strategy)
            {
                case CallStrategy.DirectSibling:
                    EmitDirectCall(il, site, siblingMethods);
                    break;

                case CallStrategy.ImportDispatch:
                    EmitImportCall(il, site);
                    break;

                case CallStrategy.TableIndirect:
                    EmitIndirectCall(il, site);
                    break;

                case CallStrategy.RefDispatch:
                    EmitRefCall(il, site);
                    break;
            }
        }

        /// <summary>
        /// DirectSibling: insert TranspiledContext under params, call MethodBuilder directly.
        /// For multi-value returns: declare locals for out params, pass ldloca, destructure after.
        /// </summary>
        private static void EmitDirectCall(ILGenerator il, CallSite site, MethodBuilder[] siblingMethods)
        {
            var targetMethod = siblingMethods[site.LocalFuncIndex];
            int paramCount = site.FuncType.ParameterTypes.Arity;
            var resultTypes = site.FuncType.ResultType.Types;
            int outParamCount = resultTypes.Length > 1 ? resultTypes.Length - 1 : 0;

            // Spill WASM params from CIL stack
            var paramTypes = site.FuncType.ParameterTypes.Types;
            var temps = new LocalBuilder[paramCount];
            for (int i = paramCount - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(ModuleTranspiler.MapValType(paramTypes[i]));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Declare locals for out results
            var outLocals = new LocalBuilder[outParamCount];
            for (int r = 0; r < outParamCount; r++)
            {
                outLocals[r] = il.DeclareLocal(ModuleTranspiler.MapValType(resultTypes[r + 1]));
            }

            // Push: ctx, params, &out0, &out1, ...
            il.Emit(OpCodes.Ldarg_0);
            for (int i = 0; i < paramCount; i++)
                il.Emit(OpCodes.Ldloc, temps[i]);
            for (int r = 0; r < outParamCount; r++)
                il.Emit(OpCodes.Ldloca, outLocals[r]);

            il.Emit(OpCodes.Call, targetMethod);

            // Result 0 is now on the CIL stack (CLR return value).
            // Push out results onto CIL stack in order: result1, result2, ...
            // WASM expects stack to have [r0, r1, r2, ...] with r0 deepest.
            // r0 is already there from the call return. Push out results on top.
            for (int r = 0; r < outParamCount; r++)
            {
                il.Emit(OpCodes.Ldloc, outLocals[r]);
            }
        }

        /// <summary>
        /// ImportDispatch: marshal params to Value[], call helper with FuncIdx.
        /// Spec: call to imported function. Resolved at module instantiation via FuncAddrs.
        /// </summary>
        private static void EmitImportCall(ILGenerator il, CallSite site)
        {
            int paramCount = site.FuncType.ParameterTypes.Arity;
            int resultCount = site.FuncType.ResultType.Arity;

            EmitSpillParamsToArray(il, site.FuncType.ParameterTypes.Types, paramCount, out var paramsLocal);

            il.Emit(OpCodes.Ldarg_0); // TranspiledContext
            il.Emit(OpCodes.Ldc_I4, site.FuncIdx);
            il.Emit(OpCodes.Ldloc, paramsLocal);
            il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                nameof(CallHelpers.CallImport), BindingFlags.Public | BindingFlags.Static)!);

            EmitUnpackResults(il, site.FuncType.ResultType.Types, resultCount);
        }

        /// <summary>
        /// TableIndirect: pop elem index, marshal params, call helper with table/type indices.
        /// Spec: resolve from table, type check against DefType, dispatch.
        /// </summary>
        private static void EmitIndirectCall(ILGenerator il, CallSite site)
        {
            int paramCount = site.FuncType.ParameterTypes.Arity;
            int resultCount = site.FuncType.ResultType.Arity;

            // Stack: [p0, p1, ..., pN-1, elemIdx]
            var elemIdxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, elemIdxLocal);

            EmitSpillParamsToArray(il, site.FuncType.ParameterTypes.Types, paramCount, out var paramsLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, site.TableIdx);
            il.Emit(OpCodes.Ldc_I4, site.TypeIdx);
            il.Emit(OpCodes.Ldloc, elemIdxLocal);
            il.Emit(OpCodes.Ldloc, paramsLocal);
            il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                nameof(CallHelpers.CallIndirect), BindingFlags.Public | BindingFlags.Static)!);

            EmitUnpackResults(il, site.FuncType.ResultType.Types, resultCount);
        }

        /// <summary>
        /// RefDispatch: pop funcref from stack, marshal params, call helper.
        /// Spec: pop funcref, null check, type check, dispatch.
        /// </summary>
        private static void EmitRefCall(ILGenerator il, CallSite site)
        {
            int paramCount = site.FuncType.ParameterTypes.Arity;
            int resultCount = site.FuncType.ResultType.Arity;

            // Stack: [p0, p1, ..., pN-1, funcref (Value)]
            var funcRefLocal = il.DeclareLocal(typeof(Value));
            il.Emit(OpCodes.Stloc, funcRefLocal);

            EmitSpillParamsToArray(il, site.FuncType.ParameterTypes.Types, paramCount, out var paramsLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, site.TypeIdx);
            il.Emit(OpCodes.Ldloc, funcRefLocal);
            il.Emit(OpCodes.Ldloc, paramsLocal);
            il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                nameof(CallHelpers.CallRef), BindingFlags.Public | BindingFlags.Static)!);

            EmitUnpackResults(il, site.FuncType.ResultType.Types, resultCount);
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
    /// Runtime dispatch helpers for call instructions.
    /// Called from transpiled IL when the target isn't a direct sibling call.
    ///
    /// These match the interpreter's execution semantics:
    /// - CallImport: resolves FuncAddr from module FuncAddrs, invokes via ExecContext
    /// - CallIndirect: table lookup, null check, type check, invoke
    /// - CallRef: funcref extraction, null check, type check, invoke
    ///
    /// All marshal parameters through ExecContext.OpStack to invoke any IFunctionInstance.
    /// </summary>
    public static class CallHelpers
    {
        /// <summary>
        /// Dispatch a call to an imported or cross-module function by FuncIdx.
        /// Matches interpreter: InstCall.Execute → context.Invoke(FuncAddrs[X])
        /// </summary>
        public static Value[] CallImport(
            TranspiledContext ctx, int funcIdx, Value[] args)
        {
            if (ctx.ExecContext == null || ctx.Module == null)
                throw new TrapException("call to import requires runtime context");

            var funcAddr = ctx.Module.FuncAddrs[(FuncIdx)funcIdx];
            return InvokeFunction(ctx, funcAddr, args);
        }

        /// <summary>
        /// Dispatch a call_indirect: table lookup + type check + invoke.
        /// Matches interpreter: InstCallIndirect.Execute steps 2-19.
        /// </summary>
        public static Value[] CallIndirect(
            TranspiledContext ctx, int tableIdx, int typeIdx, int elemIdx, Value[] args)
        {
            if (ctx.ExecContext == null || ctx.Store == null || ctx.Module == null)
                throw new TrapException("call_indirect requires runtime context");

            // Steps 2-5: table lookup
            var table = ctx.Tables[tableIdx];
            if (elemIdx < 0 || elemIdx >= table.Elements.Count)
                throw new TrapException($"undefined element {elemIdx}");

            // Step 11: element fetch
            var r = table.Elements[elemIdx];
            // Step 12: null check
            if (r.IsNullRef)
                throw new TrapException("uninitialized element");

            // Steps 13-14: funcref validation + addr extraction
            var funcAddr = r.GetFuncAddr(ctx.Module.Types);
            if (!ctx.Store.Contains(funcAddr))
                throw new TrapException("call_indirect: function not found");

            // Steps 16-18: type check (matches interpreter exactly)
            var funcInst = ctx.Store[funcAddr];
            var expectedType = ctx.Module.Types[(TypeIdx)typeIdx];
            if (funcInst is FunctionInstance fi)
            {
                if (!fi.DefType.Matches(expectedType, ctx.Module.Types))
                    throw new TrapException("indirect call type mismatch");
            }
            if (!funcInst.Type.Matches(expectedType.Unroll.Body, ctx.Module.Types))
                throw new TrapException("indirect call type mismatch");

            // Step 19: invoke
            return InvokeFunction(ctx, funcAddr, args);
        }

        /// <summary>
        /// Dispatch a call_ref: pop funcref, null check, type check, invoke.
        /// Matches interpreter: InstCallRef.Execute steps 1-8.
        /// </summary>
        public static Value[] CallRef(
            TranspiledContext ctx, int typeIdx, Value funcRef, Value[] args)
        {
            if (ctx.ExecContext == null || ctx.Store == null || ctx.Module == null)
                throw new TrapException("call_ref requires runtime context");

            // Step 3: null check
            if (funcRef.IsNullRef)
                throw new TrapException("null function reference");

            // Step 5: extract FuncAddr
            var funcAddr = funcRef.GetFuncAddr(ctx.Module.Types);
            if (!ctx.Store.Contains(funcAddr))
                throw new TrapException("call_ref: function not found");

            // Step 7: type check
            var funcInst = ctx.Store[funcAddr];
            var expectedType = ctx.Module.Types[(TypeIdx)typeIdx];
            if (!funcInst.Type.Matches(expectedType.Unroll.Body, ctx.Module.Types))
                throw new TrapException("call_ref type mismatch");

            // Step 8: invoke
            return InvokeFunction(ctx, funcAddr, args);
        }

        /// <summary>
        /// Invoke a function by FuncAddr with Value[] args, returning Value[] results.
        ///
        /// Dispatch strategy:
        /// - TranspiledFunction: call the .NET method directly via Invoke
        /// - HostFunction: call via a dedicated OpStack frame (not shared with interpreter)
        /// - FunctionInstance: call via a dedicated OpStack frame
        ///
        /// This avoids corrupting the interpreter's shared OpStack state.
        /// </summary>
        private static Value[] InvokeFunction(TranspiledContext ctx, FuncAddr funcAddr, Value[] args)
        {
            var funcInst = ctx.Store![funcAddr];

            // Fast path: TranspiledFunction has its own invoke that doesn't use OpStack
            if (funcInst is TranspiledFunction tf)
            {
                // TranspiledFunction.Invoke uses ExecContext.OpStack internally.
                // Instead, call the underlying method directly.
                return InvokeTranspiledDirect(tf, ctx, args);
            }

            // For HostFunction and FunctionInstance: we need ExecContext + OpStack.
            // Use the shared ExecContext but save/restore OpStack height to prevent corruption.
            if (ctx.ExecContext == null)
                throw new TrapException("Cross-module call requires runtime context");

            var execCtx = ctx.ExecContext;
            int savedHeight = execCtx.OpStack.Count;

            execCtx.OpStack.PushValues(args);
            funcInst.Invoke(execCtx);

            var results = new Value[funcInst.Type.ResultType.Arity];
            for (int i = results.Length - 1; i >= 0; i--)
                results[i] = execCtx.OpStack.PopAny();

            // Verify we didn't corrupt the stack
            while (execCtx.OpStack.Count > savedHeight)
                execCtx.OpStack.PopAny();

            return results;
        }

        /// <summary>
        /// Call a TranspiledFunction's underlying method directly, bypassing OpStack.
        /// </summary>
        private static Value[] InvokeTranspiledDirect(TranspiledFunction tf, TranspiledContext ctx, Value[] args)
        {
            var method = tf.Method;
            var funcType = tf.Type;
            int paramCount = funcType.ParameterTypes.Arity;

            var paramBuffer = new object?[1 + paramCount + (funcType.ResultType.Arity > 1 ? funcType.ResultType.Arity - 1 : 0)];
            paramBuffer[0] = ctx;
            for (int i = 0; i < paramCount; i++)
            {
                paramBuffer[i + 1] = ConvertFromValue(args[i], funcType.ParameterTypes.Types[i]);
            }

            object? result;
            try
            {
                result = method.Invoke(null, paramBuffer);
            }
            catch (System.Reflection.TargetInvocationException tie)
            {
                if (tie.InnerException != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
                throw;
            }

            int resultCount = funcType.ResultType.Arity;
            if (resultCount == 0)
                return Array.Empty<Value>();

            var results = new Value[resultCount];
            if (result != null)
                results[0] = ConvertToValue(result, funcType.ResultType.Types[0]);
            // Out params for multi-value
            for (int i = 1; i < resultCount; i++)
                results[i] = ConvertToValue(paramBuffer[1 + paramCount + (i - 1)]!, funcType.ResultType.Types[i]);

            return results;
        }

        private static object ConvertFromValue(Value val, ValType type) => type switch
        {
            ValType.I32 => val.Data.Int32,
            ValType.I64 => val.Data.Int64,
            ValType.F32 => val.Data.Float32,
            ValType.F64 => val.Data.Float64,
            _ => (object)val
        };

        private static Value ConvertToValue(object obj, ValType type) => type switch
        {
            ValType.I32 => new Value((int)obj),
            ValType.I64 => new Value((long)obj),
            ValType.F32 => new Value((float)obj),
            ValType.F64 => new Value((double)obj),
            _ => (Value)obj
        };
    }
}
