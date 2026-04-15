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

        private static readonly FieldInfo ImportDelegatesField =
            typeof(TranspiledContext).GetField(nameof(TranspiledContext.ImportDelegates))!;
        private static readonly FieldInfo FuncTableField =
            typeof(TranspiledContext).GetField(nameof(TranspiledContext.FuncTable))!;

        /// <summary>
        /// ImportDispatch: load typed delegate from ctx.ImportDelegates[idx], invoke directly.
        /// No Value[] marshaling, no OpStack. Delegate signature matches WASM function type.
        /// </summary>
        private static void EmitImportCall(ILGenerator il, CallSite site)
        {
            EmitTypedDelegateCall(il, site, ImportDelegatesField, site.FuncIdx);
        }

        /// <summary>
        /// TableIndirect: pack params into object[], call InvokeIndirect, unbox result.
        /// Uses DynamicInvoke for type-safe dispatch that properly traps on type mismatches.
        /// </summary>
        private static void EmitIndirectCall(ILGenerator il, CallSite site)
        {
            int paramCount = site.FuncType.ParameterTypes.Arity;
            var resultTypes = site.FuncType.ResultType.Types;

            // Stack: [p0, p1, ..., pN-1, elemIdx]
            var elemIdxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, elemIdxLocal);

            // Spill params and pack into object[]
            var paramTypes = site.FuncType.ParameterTypes.Types;
            var temps = new LocalBuilder[paramCount];
            for (int i = paramCount - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(ModuleTranspiler.MapValType(paramTypes[i]));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Build object[] args
            il.Emit(OpCodes.Ldc_I4, paramCount);
            il.Emit(OpCodes.Newarr, typeof(object));
            for (int i = 0; i < paramCount; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, temps[i]);
                il.Emit(OpCodes.Box, ModuleTranspiler.MapValType(paramTypes[i]));
                il.Emit(OpCodes.Stelem_Ref);
            }
            var argsLocal = il.DeclareLocal(typeof(object[]));
            il.Emit(OpCodes.Stloc, argsLocal);

            // Call InvokeIndirect(ctx, tableIdx, elemIdx, args)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, site.TableIdx);
            il.Emit(OpCodes.Ldloc, elemIdxLocal);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                nameof(CallHelpers.InvokeIndirect), BindingFlags.Public | BindingFlags.Static)!);

            // Unbox result
            EmitUnboxResult(il, resultTypes);
        }

        /// <summary>
        /// RefDispatch: pack params into object[], call InvokeRef, unbox result.
        /// </summary>
        private static void EmitRefCall(ILGenerator il, CallSite site)
        {
            int paramCount = site.FuncType.ParameterTypes.Arity;
            var resultTypes = site.FuncType.ResultType.Types;

            // Stack: [p0, ..., pN-1, funcref (Value)]
            var funcRefLocal = il.DeclareLocal(typeof(Value));
            il.Emit(OpCodes.Stloc, funcRefLocal);

            var paramTypes = site.FuncType.ParameterTypes.Types;
            var temps = new LocalBuilder[paramCount];
            for (int i = paramCount - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(ModuleTranspiler.MapValType(paramTypes[i]));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Build object[] args
            il.Emit(OpCodes.Ldc_I4, paramCount);
            il.Emit(OpCodes.Newarr, typeof(object));
            for (int i = 0; i < paramCount; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, temps[i]);
                il.Emit(OpCodes.Box, ModuleTranspiler.MapValType(paramTypes[i]));
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

            // Unbox result
            EmitUnboxResult(il, resultTypes);
        }

        /// <summary>
        /// Unbox the object? result from DynamicInvoke to the expected CIL stack type.
        /// </summary>
        private static void EmitUnboxResult(ILGenerator il, ValType[] resultTypes)
        {
            if (resultTypes.Length == 0)
            {
                il.Emit(OpCodes.Pop); // Discard null from DynamicInvoke
                return;
            }

            var resultClrType = ModuleTranspiler.MapValType(resultTypes[0]);
            il.Emit(OpCodes.Unbox_Any, resultClrType);
        }

        /// <summary>
        /// Emit a typed delegate invocation from a delegate array field.
        /// </summary>
        private static void EmitTypedDelegateCall(ILGenerator il, CallSite site, FieldInfo arrayField, int index)
        {
            int paramCount = site.FuncType.ParameterTypes.Arity;
            var paramTypes = site.FuncType.ParameterTypes.Types;

            // Spill params
            var temps = new LocalBuilder[paramCount];
            for (int i = paramCount - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(ModuleTranspiler.MapValType(paramTypes[i]));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Load delegate: ctx.arrayField[index]
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, arrayField);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldelem_Ref);

            // Cast to typed Func<>/Action<>
            var delegateType = BuildDelegateType(site.FuncType);
            il.Emit(OpCodes.Castclass, delegateType);

            // Push params
            for (int i = 0; i < paramCount; i++)
                il.Emit(OpCodes.Ldloc, temps[i]);

            // Invoke
            il.Emit(OpCodes.Callvirt, delegateType.GetMethod("Invoke")!);
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
        public static int ResolveIndirect(TranspiledContext ctx, int tableIdx, int elemIdx)
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
        /// Converts InvalidCastException to WASM indirect call type mismatch trap.
        /// </summary>
        public static object? InvokeIndirect(
            TranspiledContext ctx, int tableIdx, int elemIdx, object?[] args)
        {
            int funcIdx = ResolveIndirect(ctx, tableIdx, elemIdx);

            if (funcIdx < 0 || funcIdx >= ctx.FuncTable.Length)
                throw new TrapException("undefined element");

            var del = ctx.FuncTable[funcIdx];
            if (del == null)
                throw new TrapException("uninitialized element");

            try
            {
                return del.DynamicInvoke(args);
            }
            catch (System.Reflection.TargetParameterCountException)
            {
                throw new TrapException("indirect call type mismatch");
            }
            catch (System.ArgumentException)
            {
                throw new TrapException("indirect call type mismatch");
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
                return null; // unreachable
            }
        }

        /// <summary>
        /// Resolve and invoke call_ref in one step.
        /// </summary>
        public static object? InvokeRef(
            TranspiledContext ctx, Value funcRef, object?[] args)
        {
            int funcIdx = ResolveRef(ctx, funcRef);

            if (funcIdx < 0 || funcIdx >= ctx.FuncTable.Length)
                throw new TrapException("undefined element");

            var del = ctx.FuncTable[funcIdx];
            if (del == null)
                throw new TrapException("uninitialized element");

            try
            {
                return del.DynamicInvoke(args);
            }
            catch (System.Reflection.TargetParameterCountException)
            {
                throw new TrapException("indirect call type mismatch");
            }
            catch (System.ArgumentException)
            {
                throw new TrapException("indirect call type mismatch");
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
                return null; // unreachable
            }
        }

        /// <summary>
        /// Resolve call_ref: funcref → FuncTable index.
        /// </summary>
        public static int ResolveRef(TranspiledContext ctx, Value funcRef)
        {
            if (funcRef.IsNullRef)
                throw new TrapException("null function reference");

            if (ctx.Types != null)
                return (int)funcRef.GetFuncAddr(ctx.Types).Value;

            return (int)funcRef.Data.Ptr;
        }
    }
}
