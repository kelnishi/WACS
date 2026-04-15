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
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for WebAssembly call instructions.
    ///
    /// For intra-module calls to sibling transpiled functions:
    /// The WASM stack has [param0, param1, ...] on top. The CIL method expects
    /// (TranspiledContext ctx, param0, param1, ...). We need to insert ctx
    /// underneath the params by spilling them to temp locals.
    ///
    /// Phase 3 handles: call (direct intra-module)
    /// Deferred: call_indirect, call_ref, return_call variants
    /// </summary>
    internal static class CallEmitter
    {
        public static bool CanEmit(WasmOpCode op)
        {
            return op == WasmOpCode.Call
                || op == WasmOpCode.CallIndirect
                || op == WasmOpCode.ReturnCall
                || op == WasmOpCode.ReturnCallIndirect;
        }

        /// <summary>
        /// Emit a direct call to a sibling function within the same module.
        /// </summary>
        /// <param name="il">IL generator</param>
        /// <param name="inst">The call instruction (has FuncIdx X)</param>
        /// <param name="siblingFunctions">FunctionInstance array for locally-defined functions</param>
        /// <param name="siblingMethods">MethodBuilder array for locally-defined functions</param>
        /// <param name="importCount">Number of imported functions (offset into FuncAddrs)</param>
        public static void EmitCall(
            ILGenerator il,
            InstCall inst,
            FunctionInstance[] siblingFunctions,
            MethodBuilder[] siblingMethods,
            int importCount)
        {
            int funcIdx = (int)inst.X.Value;

            if (funcIdx < importCount)
            {
                throw new TranspilerException(
                    $"CallEmitter: calls to imported functions not yet supported (funcIdx={funcIdx})");
            }

            int localIdx = funcIdx - importCount;
            if (localIdx < 0 || localIdx >= siblingMethods.Length)
            {
                throw new TranspilerException(
                    $"CallEmitter: function index {funcIdx} out of range (imports={importCount}, locals={siblingMethods.Length})");
            }

            var targetMethod = siblingMethods[localIdx];
            var calleeType = siblingFunctions[localIdx].Type;
            int wasmParamCount = calleeType.ParameterTypes.Arity;

            if (wasmParamCount == 0)
            {
                il.Emit(OpCodes.Ldarg_0); // TranspiledContext
                il.Emit(OpCodes.Call, targetMethod);
            }
            else
            {
                // Spill WASM params to temps, insert ctx underneath, push back
                var paramTypes = calleeType!.ParameterTypes.Types;
                var temps = new LocalBuilder[wasmParamCount];
                for (int i = wasmParamCount - 1; i >= 0; i--)
                {
                    temps[i] = il.DeclareLocal(ModuleTranspiler.MapValType(paramTypes[i]));
                    il.Emit(OpCodes.Stloc, temps[i]);
                }

                il.Emit(OpCodes.Ldarg_0); // TranspiledContext

                for (int i = 0; i < wasmParamCount; i++)
                {
                    il.Emit(OpCodes.Ldloc, temps[i]);
                }

                il.Emit(OpCodes.Call, targetMethod);
            }
        }

        /// <summary>
        /// Emit call_indirect: table lookup + type check + dispatch.
        /// Stack has: [param0, param1, ..., paramN, i32 tableIndex]
        /// The function type is known at compile time from TypeIdx.
        ///
        /// Strategy: spill all params + table index, call a helper that does
        /// the full dispatch through ExecContext (OpStack marshaling).
        /// </summary>
        public static void EmitCallIndirect(
            ILGenerator il,
            InstCallIndirect inst,
            ModuleInstance moduleInst)
        {
            EmitIndirectDispatch(il, inst.TableIndex, inst.TypeIndex, moduleInst);
        }

        /// <summary>
        /// Shared logic for call_indirect and return_call_indirect.
        /// </summary>
        private static void EmitIndirectDispatch(
            ILGenerator il, int tableIdx, int typeIdx, ModuleInstance moduleInst)
        {
            var funcType = moduleInst.Types[(TypeIdx)typeIdx].Expansion as FunctionType;
            if (funcType == null)
                throw new TranspilerException("call_indirect: could not resolve function type");

            int paramCount = funcType.ParameterTypes.Arity;
            int resultCount = funcType.ResultType.Arity;

            // Stack: [p0, p1, ..., pN-1, elemIdx]
            var elemIdxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, elemIdxLocal);

            EmitSpillParamsToArray(il, funcType.ParameterTypes.Types, paramCount, out var paramsLocal);

            il.Emit(OpCodes.Ldarg_0); // TranspiledContext
            il.Emit(OpCodes.Ldc_I4, tableIdx);
            il.Emit(OpCodes.Ldc_I4, typeIdx);
            il.Emit(OpCodes.Ldloc, elemIdxLocal);
            il.Emit(OpCodes.Ldloc, paramsLocal);
            il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                nameof(CallHelpers.CallIndirect), BindingFlags.Public | BindingFlags.Static)!);

            EmitUnpackResults(il, funcType.ResultType.Types, resultCount);
        }

        /// <summary>
        /// Emit return_call: tail-call to a sibling function.
        /// Semantically identical to call + return. We emit it as a regular call + ret
        /// since CIL tail. prefix has restrictions that may not be met.
        /// </summary>
        public static void EmitReturnCall(
            ILGenerator il,
            InstReturnCall inst,
            FunctionInstance[] siblingFunctions,
            MethodBuilder[] siblingMethods,
            int importCount)
        {
            // return_call has the same FuncIdx field as call
            int funcIdx = (int)inst.X.Value;

            if (funcIdx < importCount)
                throw new TranspilerException($"CallEmitter: return_call to imports not yet supported");

            int localIdx = funcIdx - importCount;
            var targetMethod = siblingMethods[localIdx];
            var calleeType = siblingFunctions[localIdx].Type;
            int wasmParamCount = calleeType.ParameterTypes.Arity;

            if (wasmParamCount == 0)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, targetMethod);
            }
            else
            {
                var paramTypes = calleeType.ParameterTypes.Types;
                var temps = new LocalBuilder[wasmParamCount];
                for (int i = wasmParamCount - 1; i >= 0; i--)
                {
                    temps[i] = il.DeclareLocal(ModuleTranspiler.MapValType(paramTypes[i]));
                    il.Emit(OpCodes.Stloc, temps[i]);
                }
                il.Emit(OpCodes.Ldarg_0);
                for (int i = 0; i < wasmParamCount; i++)
                    il.Emit(OpCodes.Ldloc, temps[i]);
                il.Emit(OpCodes.Call, targetMethod);
            }
            // Implicit return — the value is on the stack, ret follows from End
        }

        /// <summary>
        /// Emit return_call_indirect: tail-call through table.
        /// Emitted as call_indirect + implicit return.
        /// </summary>
        public static void EmitReturnCallIndirect(
            ILGenerator il,
            InstReturnCallIndirect inst,
            ModuleInstance moduleInst)
        {
            // Reuse call_indirect logic — same table/type indices, same dispatch
            EmitIndirectDispatch(il, inst.TableIndex, inst.TypeIndex, moduleInst);
        }

        /// <summary>
        /// Spill N typed values from the CIL stack into a Value[] local.
        /// </summary>
        private static void EmitSpillParamsToArray(
            ILGenerator il, ValType[] paramTypes, int count, out LocalBuilder arrayLocal)
        {
            // First spill each param to a typed temp (reverse order — top of stack first)
            var temps = new LocalBuilder[count];
            for (int i = count - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(ModuleTranspiler.MapValType(paramTypes[i]));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Create Value[] and populate
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

        /// <summary>
        /// Unpack Value[] results onto the CIL stack as typed values.
        /// Assumes the Value[] is on top of the stack.
        /// </summary>
        private static void EmitUnpackResults(ILGenerator il, ValType[] resultTypes, int count)
        {
            if (count == 0)
            {
                il.Emit(OpCodes.Pop); // discard Value[]
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

        /// <summary>
        /// Convert a typed CIL value to Value struct (for array storage).
        /// </summary>
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
                    // Reference types are already Value on the CIL stack
                    break;
            }
        }

        /// <summary>
        /// Extract typed CIL value from a Value struct.
        /// </summary>
        private static void EmitUnboxFromValue(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32:
                {
                    var local = il.DeclareLocal(typeof(Value));
                    il.Emit(OpCodes.Stloc, local);
                    il.Emit(OpCodes.Ldloca, local);
                    il.Emit(OpCodes.Ldflda, DataField);
                    il.Emit(OpCodes.Ldfld, Int32Field);
                    break;
                }
                case ValType.I64:
                {
                    var local = il.DeclareLocal(typeof(Value));
                    il.Emit(OpCodes.Stloc, local);
                    il.Emit(OpCodes.Ldloca, local);
                    il.Emit(OpCodes.Ldflda, DataField);
                    il.Emit(OpCodes.Ldfld, Int64Field);
                    break;
                }
                case ValType.F32:
                {
                    var local = il.DeclareLocal(typeof(Value));
                    il.Emit(OpCodes.Stloc, local);
                    il.Emit(OpCodes.Ldloca, local);
                    il.Emit(OpCodes.Ldflda, DataField);
                    il.Emit(OpCodes.Ldfld, Float32Field);
                    break;
                }
                case ValType.F64:
                {
                    var local = il.DeclareLocal(typeof(Value));
                    il.Emit(OpCodes.Stloc, local);
                    il.Emit(OpCodes.Ldloca, local);
                    il.Emit(OpCodes.Ldflda, DataField);
                    il.Emit(OpCodes.Ldfld, Float64Field);
                    break;
                }
                default:
                    // Reference types stay as Value
                    break;
            }
        }
    }

    /// <summary>
    /// Static helpers for indirect/ref calls from transpiled code.
    /// These marshal through the ExecContext OpStack to invoke any IFunctionInstance.
    /// </summary>
    public static class CallHelpers
    {
        public static Value[] CallIndirect(
            TranspiledContext ctx, int tableIdx, int typeIdx, int elemIdx, Value[] args)
        {
            if (ctx.ExecContext == null || ctx.Store == null || ctx.Module == null)
                throw new TrapException("call_indirect requires runtime context");

            var table = ctx.Tables[tableIdx];
            if (elemIdx < 0 || elemIdx >= table.Elements.Count)
                throw new TrapException($"undefined element {elemIdx}");

            var r = table.Elements[elemIdx];
            if (r.IsNullRef)
                throw new TrapException("uninitialized element");

            var funcAddr = r.GetFuncAddr(ctx.Module.Types);
            if (!ctx.Store.Contains(funcAddr))
                throw new TrapException("call_indirect: function not found");

            var funcInst = ctx.Store[funcAddr];

            // Type check
            var expectedType = ctx.Module.Types[(TypeIdx)typeIdx];
            if (funcInst is FunctionInstance fi)
            {
                if (!fi.DefType.Matches(expectedType, ctx.Module.Types))
                    throw new TrapException("indirect call type mismatch");
            }
            var funcType = funcInst.Type;
            if (!funcType.Matches(expectedType.Unroll.Body, ctx.Module.Types))
                throw new TrapException("indirect call type mismatch");

            // Marshal through ExecContext
            var execCtx = ctx.ExecContext;
            execCtx.OpStack.PushValues(args);
            funcInst.Invoke(execCtx);

            // Pop results
            var results = new Value[funcType.ResultType.Arity];
            for (int i = results.Length - 1; i >= 0; i--)
                results[i] = execCtx.OpStack.PopAny();

            return results;
        }
    }
}
