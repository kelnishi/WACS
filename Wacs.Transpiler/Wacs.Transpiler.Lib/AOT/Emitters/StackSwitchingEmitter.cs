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

        public static bool CanEmit(WasmOpCode op)
        {
            // Three of six. The other three need the dispatch
            // machinery described in the class summary.
            return op == WasmOpCode.ContNew
                   || op == WasmOpCode.ContBind
                   || op == WasmOpCode.Suspend;
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
        /// Emit CIL for the three supported opcodes. Callers
        /// should gate dispatch on <see cref="CanEmit"/>.
        /// </summary>
        public static void Emit(ILGenerator il, InstructionBase inst, ModuleInstance moduleInst)
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
