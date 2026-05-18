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
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// CIL emission for the WebAssembly Stack Switching opcodes
    /// — three of six today.
    ///
    /// <para><strong>Emitted</strong> (call out to
    /// <see cref="StackSwitchingHelpers"/>):
    /// <c>cont.new</c>, <c>cont.bind</c>, <c>suspend</c>. These
    /// three are straight-line operations with no non-local
    /// control transfer back into the caller: cont.new and
    /// cont.bind allocate a ContInstance and return a Value;
    /// suspend throws a SuspensionException that propagates up.
    /// The emitted IL packs CIL-stack operands into Values,
    /// calls the helper, and unpacks the result.</para>
    ///
    /// <para><strong>Not yet emitted</strong> (functions
    /// containing them fall back to interpreter execution):
    /// <c>resume</c>, <c>resume_throw</c>, <c>switch</c>. These
    /// invoke a continuation's function and route any
    /// SuspensionException back to enclosing handler labels in
    /// the caller's own CIL body — same try/catch + Leave-to-
    /// dispatch-label pattern <c>ExceptionEmitter.EmitTryTable</c>
    /// uses for try_table. Substantial separate work.</para>
    ///
    /// <para><strong>Mixed-mode only.</strong> The helpers
    /// require a live <c>ThinContext.ExecContext</c>. Standalone
    /// mode raises <see cref="NotSupportedException"/> with an
    /// explanatory message from the helpers themselves.</para>
    /// </summary>
    internal static class StackSwitchingEmitter
    {
        private static readonly FieldInfo ExecContextField =
            typeof(ThinContext).GetField(nameof(ThinContext.ExecContext))!;

        private static readonly MethodInfo ContNewMethod =
            typeof(StackSwitchingHelpers).GetMethod(
                nameof(StackSwitchingHelpers.ContNew),
                BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo ContBindMethod =
            typeof(StackSwitchingHelpers).GetMethod(
                nameof(StackSwitchingHelpers.ContBind),
                BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo SuspendMethod =
            typeof(StackSwitchingHelpers).GetMethod(
                nameof(StackSwitchingHelpers.Suspend),
                BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo ResumeMethod =
            typeof(StackSwitchingHelpers).GetMethod(
                nameof(StackSwitchingHelpers.Resume),
                BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo ResumeThrowMethod =
            typeof(StackSwitchingHelpers).GetMethod(
                nameof(StackSwitchingHelpers.ResumeThrow),
                BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo SwitchMethod =
            typeof(StackSwitchingHelpers).GetMethod(
                nameof(StackSwitchingHelpers.Switch),
                BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo FindHandlerMatchMethod =
            typeof(StackSwitchingHelpers).GetMethod(
                nameof(StackSwitchingHelpers.FindHandlerMatch),
                BindingFlags.Public | BindingFlags.Static)!;
        private static readonly MethodInfo ReifyHandlerArgsMethod =
            typeof(StackSwitchingHelpers).GetMethod(
                nameof(StackSwitchingHelpers.ReifyHandlerArgs),
                BindingFlags.Public | BindingFlags.Static)!;

        public static bool CanEmit(WasmOpCode op)
        {
            // All six now emit; resume / resume_throw / switch
            // wrap their helper calls in a CIL try/catch with a
            // tag-dispatch arm that branches to enclosing wasm
            // handler labels via the block stack.
            return op == WasmOpCode.ContNew
                   || op == WasmOpCode.ContBind
                   || op == WasmOpCode.Suspend
                   || op == WasmOpCode.Resume
                   || op == WasmOpCode.ResumeThrow
                   || op == WasmOpCode.Switch;
        }

        /// <summary>
        /// True iff <paramref name="op"/> is one of the six Stack
        /// Switching opcodes — distinct from <see cref="CanEmit"/>
        /// so callers can distinguish "we know this opcode but
        /// can't emit it" from "we don't recognize this opcode at
        /// all".
        /// </summary>
        public static bool IsStackSwitchingOpcode(WasmOpCode op)
        {
            return op == WasmOpCode.ContNew
                   || op == WasmOpCode.ContBind
                   || op == WasmOpCode.Suspend
                   || op == WasmOpCode.Resume
                   || op == WasmOpCode.ResumeThrow
                   || op == WasmOpCode.Switch;
        }

        /// <summary>
        /// Emit CIL for stack-switching opcodes. Callers should
        /// gate on <see cref="CanEmit"/>. The block stack is
        /// consulted for resume / resume_throw handler-label
        /// resolution; <paramref name="incTryDepth"/> /
        /// <paramref name="decTryDepth"/> bracket the try-region
        /// emission so branch-out instructions inside the helper
        /// call (none today, but symmetric with try_table) pick
        /// Leave over Br when appropriate.
        /// </summary>
        public static void Emit(
            ILGenerator il, InstructionBase inst, ModuleInstance moduleInst,
            System.Collections.Generic.Stack<EmitBlock> blockStack,
            Action<int> incTryDepth, Action<int> decTryDepth)
        {
            switch (inst)
            {
                case InstContNew cn:
                    EmitContNew(il, cn);
                    return;
                case InstContBind cb:
                    EmitContBind(il, cb, moduleInst);
                    return;
                case InstSuspend su:
                    EmitSuspend(il, su, moduleInst);
                    return;
                case InstResume re:
                    EmitResume(il, re, moduleInst, blockStack, incTryDepth, decTryDepth);
                    return;
                case InstResumeThrow rt:
                    EmitResumeThrow(il, rt, moduleInst, blockStack, incTryDepth, decTryDepth);
                    return;
                case InstSwitch sw:
                    EmitSwitch(il, sw, moduleInst);
                    return;
            }
            throw new TranspilerException(
                $"StackSwitchingEmitter.Emit called with unsupported instruction 0x{(byte)inst.Op.x00:X2}");
        }

        // ---- cont.new $ct ---------------------------------------
        //
        // CIL stack: [funcRef:Value]
        // Emitted:
        //   stloc tmpFunc
        //   ldarg.0; ldfld ThinContext.ExecContext
        //   ldc.i4 typeIdx
        //   ldloc tmpFunc
        //   call StackSwitchingHelpers.ContNew
        // CIL stack after: [contRef:Value]
        private static void EmitContNew(ILGenerator il, InstContNew inst)
        {
            var funcLocal = il.DeclareLocal(typeof(Value));
            il.Emit(OpCodes.Stloc, funcLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ExecContextField);
            il.Emit(OpCodes.Ldc_I4, inst.TypeIndex);
            il.Emit(OpCodes.Ldloc, funcLocal);
            il.Emit(OpCodes.Call, ContNewMethod);
        }

        // ---- cont.bind $ct1 $ct2 --------------------------------
        //
        // CIL stack: [prefix0, prefix1, ..., prefixN-1, contRef:Value]
        // The number of prefix args is ft1.params - ft2.params; each
        // prefix arg lives on the CIL stack as its native CLR
        // primitive (or as a Value for ref types). The emitter
        // wraps each into a Value and packs them into a Value[]
        // in source order, then calls the helper.
        private static void EmitContBind(ILGenerator il, InstContBind inst, ModuleInstance moduleInst)
        {
            var ft1 = ResolveFuncType(moduleInst, inst.TypeIndex1);
            var ft2 = ResolveFuncType(moduleInst, inst.TypeIndex2);
            int bindCount = ft1.ParameterTypes.Arity - ft2.ParameterTypes.Arity;
            if (bindCount < 0)
                throw new TranspilerException(
                    $"cont.bind: source params arity {ft1.ParameterTypes.Arity} " +
                    $"must be >= target params arity {ft2.ParameterTypes.Arity}.");

            // Save the cont (top of stack) and each prefix arg
            // to typed locals so we can re-load them in source
            // order when building the Value[].
            var contLocal = il.DeclareLocal(typeof(Value));
            il.Emit(OpCodes.Stloc, contLocal);

            var prefixLocals = new LocalBuilder[bindCount];
            for (int i = bindCount - 1; i >= 0; i--)
            {
                var paramType = ft1.ParameterTypes.Types[i];
                var clrType = ModuleTranspiler.MapValTypeInternal(paramType, moduleInst);
                prefixLocals[i] = il.DeclareLocal(clrType);
                il.Emit(OpCodes.Stloc, prefixLocals[i]);
            }

            // Build the Value[] of prefix args in source order.
            il.Emit(OpCodes.Ldc_I4, bindCount);
            il.Emit(OpCodes.Newarr, typeof(Value));
            for (int i = 0; i < bindCount; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, prefixLocals[i]);
                EmitWrapAsValue(il, ft1.ParameterTypes.Types[i]);
                il.Emit(OpCodes.Stelem, typeof(Value));
            }
            // CIL stack now: [valueArray]
            // Call sig: ContBind(ExecContext, int, Value cont, Value[] prefix)
            // Need to reorder: push ctx, typeidx, cont before the array.
            // Swap is awkward in CIL; easier to save the array to a
            // local and push in correct order.
            var arrLocal = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Stloc, arrLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ExecContextField);
            il.Emit(OpCodes.Ldc_I4, inst.TypeIndex2);
            il.Emit(OpCodes.Ldloc, contLocal);
            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Call, ContBindMethod);
        }

        // ---- suspend $tag ---------------------------------------
        //
        // CIL stack: [arg0, arg1, ..., argM-1]
        // Builds a Value[] of the tag's params, calls the helper
        // (which throws SuspensionException). The CIL stack after
        // the throw is unreachable from this point.
        private static void EmitSuspend(ILGenerator il, InstSuspend inst, ModuleInstance moduleInst)
        {
            var tagParamTypes = ResolveTagParamTypes(moduleInst, (TagIdx)(uint)inst.TagIndex);
            int paramCount = tagParamTypes.Length;

            // Save each arg to a typed local in reverse-pop order.
            var paramLocals = new LocalBuilder[paramCount];
            for (int i = paramCount - 1; i >= 0; i--)
            {
                var paramType = tagParamTypes[i];
                var clrType = ModuleTranspiler.MapValTypeInternal(paramType, moduleInst);
                paramLocals[i] = il.DeclareLocal(clrType);
                il.Emit(OpCodes.Stloc, paramLocals[i]);
            }

            // Pack into Value[].
            il.Emit(OpCodes.Ldc_I4, paramCount);
            il.Emit(OpCodes.Newarr, typeof(Value));
            for (int i = 0; i < paramCount; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, paramLocals[i]);
                EmitWrapAsValue(il, tagParamTypes[i]);
                il.Emit(OpCodes.Stelem, typeof(Value));
            }

            var arrLocal = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Stloc, arrLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ExecContextField);
            il.Emit(OpCodes.Ldc_I4, inst.TagIndex);
            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Call, SuspendMethod);
        }

        // ---- switch $ct $tag -----------------------------------
        //
        // CIL stack: [t1*, cont:Value] where t1* = tag's params
        // minus the trailing self-ref. Helper pops cont, builds a
        // placeholder reified-self, pushes t1* + placeholder onto
        // the runtime OpStack, invokes the target's function. On
        // normal completion the target's t2* results come back on
        // the runtime OpStack — helper returns them as Value[].
        // Switch does not install a new handler frame.
        private static void EmitSwitch(
            ILGenerator il, InstSwitch inst, ModuleInstance moduleInst)
        {
            var tagParamTypes = ResolveTagParamTypes(moduleInst, (TagIdx)(uint)inst.TagIndex);
            int t1Arity = tagParamTypes.Length - 1; // last is self-ref, not on stack
            if (t1Arity < 0) t1Arity = 0;

            var targetFt = ResolveFuncType(moduleInst, inst.TypeIndex);

            var contLocal = il.DeclareLocal(typeof(Value));
            il.Emit(OpCodes.Stloc, contLocal);

            var t1Locals = new LocalBuilder[t1Arity];
            for (int i = t1Arity - 1; i >= 0; i--)
            {
                var pt = tagParamTypes[i];
                var clr = ModuleTranspiler.MapValTypeInternal(pt, moduleInst);
                t1Locals[i] = il.DeclareLocal(clr);
                il.Emit(OpCodes.Stloc, t1Locals[i]);
            }

            // Pack t1Locals into Value[].
            il.Emit(OpCodes.Ldc_I4, t1Arity);
            il.Emit(OpCodes.Newarr, typeof(Value));
            for (int i = 0; i < t1Arity; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, t1Locals[i]);
                EmitWrapAsValue(il, tagParamTypes[i]);
                il.Emit(OpCodes.Stelem, typeof(Value));
            }
            var argsLocal = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Stloc, argsLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ExecContextField);
            il.Emit(OpCodes.Ldc_I4, inst.TypeIndex);
            il.Emit(OpCodes.Ldc_I4, inst.TagIndex);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Ldloc, contLocal);
            il.Emit(OpCodes.Call, SwitchMethod);

            // Results: Value[] on CIL stack. Unpack to typed
            // CIL stack values per target's ResultType.
            EmitUnpackResultArray(il, targetFt.ResultType.Types, moduleInst);
        }

        // ---- resume $ct handler* -------------------------------
        //
        // CIL stack: [t1*, cont:Value] where t1* = cont's $ft
        // params. Wraps the helper call in BeginExceptionBlock +
        // catch(SuspensionException). On normal completion, the
        // helper returns the cont's t2* results which we unpack
        // onto the CIL stack. On caught suspension we test the
        // tag against each handler; on a match we Leave to a
        // dispatch label that reifies + branches to the
        // corresponding wasm enclosing label.
        private static void EmitResume(
            ILGenerator il, InstResume inst, ModuleInstance moduleInst,
            System.Collections.Generic.Stack<EmitBlock> blockStack,
            Action<int> incTryDepth, Action<int> decTryDepth)
        {
            var contFt = ResolveFuncType(moduleInst, inst.TypeIndex);
            int t1Arity = contFt.ParameterTypes.Arity;

            EmitResumeFamilyShared(
                il, moduleInst, blockStack, incTryDepth, decTryDepth,
                t1ArgTypes: contFt.ParameterTypes.Types,
                handlers: inst.Handlers,
                resultTypes: contFt.ResultType.Types,
                emitHelperCall: (saveArgsLocal, saveContLocal, saveHandlersLocal) =>
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, ExecContextField);
                    il.Emit(OpCodes.Ldc_I4, inst.TypeIndex);
                    il.Emit(OpCodes.Ldloc, saveArgsLocal);
                    il.Emit(OpCodes.Ldloc, saveContLocal);
                    il.Emit(OpCodes.Ldloc, saveHandlersLocal);
                    il.Emit(OpCodes.Call, ResumeMethod);
                });
        }

        // ---- resume_throw $ct $tag handler* --------------------
        //
        // CIL stack: [t*, cont:Value] where t* = tag's params.
        // Same try/catch + dispatch shape as resume; helper
        // allocates the exception and injects it at the cont's
        // entry via InstThrowRef.ExecuteInstruction.
        private static void EmitResumeThrow(
            ILGenerator il, InstResumeThrow inst, ModuleInstance moduleInst,
            System.Collections.Generic.Stack<EmitBlock> blockStack,
            Action<int> incTryDepth, Action<int> decTryDepth)
        {
            var contFt = ResolveFuncType(moduleInst, inst.TypeIndex);
            var excTagParams = ResolveTagParamTypes(moduleInst, (TagIdx)(uint)inst.TagIndex);

            EmitResumeFamilyShared(
                il, moduleInst, blockStack, incTryDepth, decTryDepth,
                t1ArgTypes: excTagParams,
                handlers: inst.Handlers,
                resultTypes: contFt.ResultType.Types,
                emitHelperCall: (saveArgsLocal, saveContLocal, saveHandlersLocal) =>
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, ExecContextField);
                    il.Emit(OpCodes.Ldc_I4, inst.TypeIndex);
                    il.Emit(OpCodes.Ldc_I4, inst.TagIndex);
                    il.Emit(OpCodes.Ldloc, saveArgsLocal);
                    il.Emit(OpCodes.Ldloc, saveContLocal);
                    il.Emit(OpCodes.Ldloc, saveHandlersLocal);
                    il.Emit(OpCodes.Call, ResumeThrowMethod);
                });
        }

        // Shared scaffolding for resume / resume_throw: save args
        // + cont, build handler-tag int[], wrap helper call in
        // try/catch + dispatch, unpack results on normal exit.
        private static void EmitResumeFamilyShared(
            ILGenerator il, ModuleInstance moduleInst,
            System.Collections.Generic.Stack<EmitBlock> blockStack,
            Action<int> incTryDepth, Action<int> decTryDepth,
            ValType[] t1ArgTypes,
            StackSwitchHandler[] handlers,
            ValType[] resultTypes,
            Action<LocalBuilder, LocalBuilder, LocalBuilder> emitHelperCall)
        {
            int t1Arity = t1ArgTypes.Length;
            var contLocal = il.DeclareLocal(typeof(Value));
            il.Emit(OpCodes.Stloc, contLocal);

            // Save t1* args to typed locals in reverse-pop order.
            var argLocals = new LocalBuilder[t1Arity];
            for (int i = t1Arity - 1; i >= 0; i--)
            {
                var clr = ModuleTranspiler.MapValTypeInternal(t1ArgTypes[i], moduleInst);
                argLocals[i] = il.DeclareLocal(clr);
                il.Emit(OpCodes.Stloc, argLocals[i]);
            }

            // Build Value[] of t1* args.
            il.Emit(OpCodes.Ldc_I4, t1Arity);
            il.Emit(OpCodes.Newarr, typeof(Value));
            for (int i = 0; i < t1Arity; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, argLocals[i]);
                EmitWrapAsValue(il, t1ArgTypes[i]);
                il.Emit(OpCodes.Stelem, typeof(Value));
            }
            var argsLocal = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Stloc, argsLocal);

            // Build int[] of handler tag indices.
            il.Emit(OpCodes.Ldc_I4, handlers.Length);
            il.Emit(OpCodes.Newarr, typeof(int));
            for (int i = 0; i < handlers.Length; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldc_I4, (int)handlers[i].Tag.Value);
                il.Emit(OpCodes.Stelem_I4);
            }
            var handlersLocal = il.DeclareLocal(typeof(int[]));
            il.Emit(OpCodes.Stloc, handlersLocal);

            // Storage for the normal-completion result array,
            // and for the SuspensionException in the catch arm.
            var resultsLocal = il.DeclareLocal(typeof(Value[]));
            var exnLocal = il.DeclareLocal(typeof(SuspensionException));

            // Per-handler dispatch labels live outside the
            // exception block. endLabel is past the dispatch
            // region so normal completion bypasses it.
            var dispatchLabels = new System.Reflection.Emit.Label[handlers.Length];
            for (int i = 0; i < handlers.Length; i++)
                dispatchLabels[i] = il.DefineLabel();
            var endLabel = il.DefineLabel();

            il.BeginExceptionBlock();
            incTryDepth(1);

            emitHelperCall(argsLocal, contLocal, handlersLocal);
            il.Emit(OpCodes.Stloc, resultsLocal);
            // Auto-Leave from BeginCatchBlock/EndExceptionBlock.

            il.BeginCatchBlock(typeof(SuspensionException));
            il.Emit(OpCodes.Stloc, exnLocal);

            // Find matching handler via helper:
            //   FindHandlerMatch(ctx.ExecContext, handlersLocal, exnLocal)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ExecContextField);
            il.Emit(OpCodes.Ldloc, handlersLocal);
            il.Emit(OpCodes.Ldloc, exnLocal);
            il.Emit(OpCodes.Call, FindHandlerMatchMethod);
            var matchIdxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, matchIdxLocal);

            // if matchIdx == -1, rethrow; else Leave to
            // dispatchLabels[matchIdx]. CIL has no general
            // "switch then Leave" — emit a chain of compares.
            for (int i = 0; i < handlers.Length; i++)
            {
                il.Emit(OpCodes.Ldloc, matchIdxLocal);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ceq);
                var skip = il.DefineLabel();
                il.Emit(OpCodes.Brfalse, skip);
                il.Emit(OpCodes.Leave, dispatchLabels[i]);
                il.MarkLabel(skip);
            }
            il.Emit(OpCodes.Rethrow);

            il.EndExceptionBlock();
            decTryDepth(1);

            // Normal try-exit bypasses dispatch.
            il.Emit(OpCodes.Br, endLabel);

            // Dispatch labels: reify cont + payload, push values
            // onto CIL stack, branch to the wasm handler label.
            for (int i = 0; i < handlers.Length; i++)
            {
                il.MarkLabel(dispatchLabels[i]);

                // Resolve the wasm handler's tag-param types
                // — these are what the handler label expects to
                // arrive with (in addition to the trailing
                // reified cont ref).
                var hTagParams = ResolveTagParamTypes(
                    moduleInst, handlers[i].Tag);

                // arr = ReifyHandlerArgs(ctx.ExecContext,
                //                        contLocal, exnLocal)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ExecContextField);
                il.Emit(OpCodes.Ldloc, contLocal);
                il.Emit(OpCodes.Ldloc, exnLocal);
                il.Emit(OpCodes.Call, ReifyHandlerArgsMethod);
                var arrLocal = il.DeclareLocal(typeof(Value[]));
                il.Emit(OpCodes.Stloc, arrLocal);

                // Unbox payload values onto CIL stack in source
                // order, then push the trailing reified cont.
                for (int j = 0; j < hTagParams.Length; j++)
                {
                    il.Emit(OpCodes.Ldloc, arrLocal);
                    il.Emit(OpCodes.Ldc_I4, j);
                    il.Emit(OpCodes.Ldelem, typeof(Value));
                    EmitUnwrapFromValue(il, hTagParams[j]);
                }
                // Reified cont sits at arr[hTagParams.Length].
                il.Emit(OpCodes.Ldloc, arrLocal);
                il.Emit(OpCodes.Ldc_I4, hTagParams.Length);
                il.Emit(OpCodes.Ldelem, typeof(Value));

                // Branch to the wasm handler label via block stack.
                int depth = (int)handlers[i].Label.Value;
                var target = ControlEmitter.PeekLabel(blockStack, depth);
                il.Emit(OpCodes.Br, target.BranchTarget);
            }

            il.MarkLabel(endLabel);
            // Unpack normal-completion result array to CIL stack.
            EmitUnpackResultArrayFromLocal(il, resultsLocal, resultTypes, moduleInst);
        }

        // Helper for switch: unpack a Value[] just produced on
        // the CIL stack to typed CIL values.
        private static void EmitUnpackResultArray(
            ILGenerator il, ValType[] resultTypes, ModuleInstance moduleInst)
        {
            var local = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Stloc, local);
            EmitUnpackResultArrayFromLocal(il, local, resultTypes, moduleInst);
        }

        private static void EmitUnpackResultArrayFromLocal(
            ILGenerator il, LocalBuilder arrLocal,
            ValType[] resultTypes, ModuleInstance moduleInst)
        {
            for (int i = 0; i < resultTypes.Length; i++)
            {
                il.Emit(OpCodes.Ldloc, arrLocal);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem, typeof(Value));
                EmitUnwrapFromValue(il, resultTypes[i]);
            }
        }

        // Inverse of EmitWrapAsValue.
        private static void EmitUnwrapFromValue(ILGenerator il, ValType type)
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
                    var dataField = typeof(Value).GetField(nameof(Value.Data))!;
                    il.Emit(OpCodes.Ldflda, dataField);
                    var dunionField = type switch
                    {
                        ValType.I32 => typeof(DUnion).GetField(nameof(DUnion.Int32))!,
                        ValType.I64 => typeof(DUnion).GetField(nameof(DUnion.Int64))!,
                        ValType.F32 => typeof(DUnion).GetField(nameof(DUnion.Float32))!,
                        ValType.F64 => typeof(DUnion).GetField(nameof(DUnion.Float64))!,
                        _ => throw new TranspilerException("unreachable"),
                    };
                    il.Emit(OpCodes.Ldfld, dunionField);
                    break;
                }
                default:
                    // Reference types stay as Value on the CIL stack.
                    break;
            }
        }

        // Wrap a CIL-stack primitive into a Value via the
        // appropriate ctor. Reference types are already Value on
        // the CIL stack — no-op.
        private static void EmitWrapAsValue(ILGenerator il, ValType type)
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
                    // Reference types and V128 already on stack as Value.
                    break;
            }
        }

        // Resolve a tag's parameter types from a ModuleInstance —
        // imported tags vs. locally-defined tags use different
        // index spaces, matching ExceptionEmitter.ResolveTagFieldTypes.
        private static ValType[] ResolveTagParamTypes(ModuleInstance moduleInst, TagIdx tagIdx)
        {
            int importedTagCount = 0;
            foreach (var import in moduleInst.Repr.Imports)
                if (import.Desc is Wacs.Core.Module.ImportDesc.TagDesc) importedTagCount++;

            FunctionType? ft = null;
            if ((int)tagIdx.Value < importedTagCount)
            {
                int ti = 0;
                foreach (var import in moduleInst.Repr.Imports)
                {
                    if (import.Desc is Wacs.Core.Module.ImportDesc.TagDesc td)
                    {
                        if (ti == (int)tagIdx.Value)
                        {
                            ft = moduleInst.Types[td.TagDef.TypeIndex].Expansion as FunctionType;
                            break;
                        }
                        ti++;
                    }
                }
            }
            else
            {
                int localIdx = (int)tagIdx.Value - importedTagCount;
                var tag = moduleInst.Repr.Tags[localIdx];
                ft = moduleInst.Types[tag.TypeIndex].Expansion as FunctionType;
            }
            return ft?.ParameterTypes.Types ?? Array.Empty<ValType>();
        }

        private static FunctionType ResolveFuncType(ModuleInstance moduleInst, int typeIdx)
        {
            var defType = moduleInst.Types[(TypeIdx)typeIdx];
            // For a continuation type, drill through to the inner FunctionType.
            if (defType.Expansion is ContType ct)
            {
                var inner = moduleInst.Types[ct.FuncTypeRef.Index()];
                if (inner.Expansion is FunctionType fnInner) return fnInner;
            }
            if (defType.Expansion is FunctionType fn) return fn;
            throw new TranspilerException(
                $"Type index {typeIdx} does not resolve to a function type.");
        }
    }
}
