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
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for WASM exception instructions using CLR native exception
    /// handling. Implements doc 1 §13 and doc 2 §§5, 14.
    ///
    /// Key design points:
    ///
    /// * Tag identity is `TagInstance` reference equality (doc 2 §5). Each
    ///   tag is fetched via `ctx.Tags[tagidx]`; the linker wires imported
    ///   tags to share the exporter's TagInstance.
    /// * `throw` constructs `new WasmException(TagInstance, Value[])` and
    ///   throws — no Store/AllocateExn round-trip.
    /// * `try_table` uses `BeginExceptionBlock` / `BeginCatchBlock(WasmException)`
    ///   with inline tag-ref comparison; mismatched catches Rethrow.
    /// * Catch dispatch unpacks exception fields back to the internal CIL
    ///   stack representation (doc 1 §2.1): scalars as typed primitives,
    ///   GC refs as object (via UnwrapRef), funcref/externref/v128 as Value.
    /// * `exnref` (from catch_ref / catch_all_ref) is the `WasmException`
    ///   object itself on the internal stack.
    /// * Branches out of try-regions must use `Leave` — that's tracked by
    ///   the caller (FunctionCodegen._tryDepth, doc 2 §14).
    /// </summary>
    internal static class ExceptionEmitter
    {
        private static readonly FieldInfo TagsField =
            typeof(ThinContext).GetField(nameof(ThinContext.Tags))!;

        private static readonly MethodInfo WasmException_get_Tag =
            typeof(WasmException).GetProperty(nameof(WasmException.Tag))!.GetGetMethod()!;

        private static readonly MethodInfo WasmException_get_Fields =
            typeof(WasmException).GetProperty(nameof(WasmException.Fields))!.GetGetMethod()!;

        private static readonly ConstructorInfo WasmException_ctor =
            typeof(WasmException).GetConstructor(new[] { typeof(TagInstance), typeof(Value[]) })!;

        public static bool CanEmit(WasmOpCode op)
        {
            return op == WasmOpCode.Throw
                || op == WasmOpCode.ThrowRef
                || op == WasmOpCode.TryTable;
        }

        /// <summary>
        /// Emit `throw $tag`: gather N field values from the CIL stack, wrap
        /// each to Value, build Value[], Newobj WasmException, Throw.
        /// Doc 1 §13.3.
        /// </summary>
        public static void EmitThrow(ILGenerator il, InstThrow inst, ModuleInstance moduleInst)
        {
            int tagIdx = inst.TagIndex;
            var fieldTypes = ResolveTagFieldTypes(moduleInst, (TagIdx)tagIdx);
            int fieldCount = fieldTypes.Length;

            // Spill each field from the stack into a temp of its internal type
            // (object for GC ref, typed scalar otherwise, Value for v128/func/externref).
            var fieldTemps = new LocalBuilder[fieldCount];
            for (int i = fieldCount - 1; i >= 0; i--)
            {
                var internalType = ModuleTranspiler.MapValTypeInternal(fieldTypes[i], moduleInst);
                fieldTemps[i] = il.DeclareLocal(internalType);
                il.Emit(OpCodes.Stloc, fieldTemps[i]);
            }

            // Build Value[] — each slot holds a Value with the correct tag and data.
            il.Emit(OpCodes.Ldc_I4, fieldCount);
            il.Emit(OpCodes.Newarr, typeof(Value));
            for (int i = 0; i < fieldCount; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, fieldTemps[i]);
                EmitConvertToValueForStorage(il, fieldTypes[i], moduleInst);
                il.Emit(OpCodes.Stelem, typeof(Value));
            }

            // Now: stack has Value[]. Fetch TagInstance from ctx.Tags[tagIdx],
            // push fields, Newobj WasmException, Throw.
            var fieldsLocal = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Stloc, fieldsLocal);
            il.Emit(OpCodes.Ldarg_0);            // ThinContext
            il.Emit(OpCodes.Ldfld, TagsField);    // TagInstance[]
            il.Emit(OpCodes.Ldc_I4, tagIdx);
            il.Emit(OpCodes.Ldelem_Ref);          // TagInstance
            il.Emit(OpCodes.Ldloc, fieldsLocal);
            il.Emit(OpCodes.Newobj, WasmException_ctor);
            il.Emit(OpCodes.Throw);
        }

        /// <summary>
        /// Emit `throw_ref`: pop exnref (WasmException on internal stack),
        /// null check → trap, else Throw. Doc 1 §13.4.
        /// </summary>
        public static void EmitThrowRef(ILGenerator il)
        {
            // Stack: [WasmException]. Dup for null check so we can throw the
            // original on success.
            var okLabel = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, okLabel);
            il.Emit(OpCodes.Ldstr, "null exception reference");
            il.Emit(OpCodes.Newobj,
                typeof(TrapException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(okLabel);
            il.Emit(OpCodes.Throw);
        }

        /// <summary>
        /// Emit try_table via CLR structured EH. See doc 1 §13.5 and doc 2 §14.
        ///
        /// Structure:
        ///   BeginExceptionBlock
        ///     (body)
        ///     Leave endLabel
        ///   BeginCatchBlock(WasmException)
        ///     Stloc exnLocal
        ///     for each clause: if match → Leave dispatch_i
        ///     Rethrow  (no match)
        ///   EndExceptionBlock
        ///   dispatch_i:
        ///     unpack fields + exnref per clause mode
        ///     Br enclosing_catch_label
        ///   endLabel:
        /// </summary>
        public static void EmitTryTable(
            ILGenerator il,
            InstTryTable inst,
            Stack<EmitBlock> blockStack,
            EmitInstructionDelegate emitInstruction,
            ModuleInstance moduleInst,
            Action<int> incTryDepth,
            Action<int> decTryDepth)
        {
            var catches = inst.Catches;
            var endLabel = il.DefineLabel();

            // try_table introduces a block label at this depth.
            blockStack.Push(new EmitBlock
            {
                BranchTarget = endLabel,
                Kind = WasmOpCode.Block
            });

            var dispatchLabels = new System.Reflection.Emit.Label[catches.Length];
            for (int i = 0; i < catches.Length; i++)
                dispatchLabels[i] = il.DefineLabel();

            var exnLocal = il.DeclareLocal(typeof(WasmException));

            il.BeginExceptionBlock();
            incTryDepth(1);

            // Emit body. Branches out of the try-region pick Leave vs Br via
            // the caller's _tryDepth tracking (doc 2 §14).
            var block = inst.GetBlock(0);
            foreach (var child in block.Instructions)
                emitInstruction(il, child);

            il.Emit(OpCodes.Leave, endLabel);

            il.BeginCatchBlock(typeof(WasmException));
            il.Emit(OpCodes.Stloc, exnLocal);

            // Clause dispatch. Tag match = reference equality on TagInstance.
            for (int i = 0; i < catches.Length; i++)
            {
                var handler = catches[i];
                switch (handler.Mode)
                {
                    case CatchFlags.None:
                    case CatchFlags.CatchRef:
                    {
                        // Compare exn.Tag == ctx.Tags[expected]
                        il.Emit(OpCodes.Ldloc, exnLocal);
                        il.Emit(OpCodes.Callvirt, WasmException_get_Tag);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldfld, TagsField);
                        il.Emit(OpCodes.Ldc_I4, (int)handler.X.Value);
                        il.Emit(OpCodes.Ldelem_Ref);
                        il.Emit(OpCodes.Ceq);
                        var nextClause = il.DefineLabel();
                        il.Emit(OpCodes.Brfalse, nextClause);
                        il.Emit(OpCodes.Leave, dispatchLabels[i]);
                        il.MarkLabel(nextClause);
                        break;
                    }
                    case CatchFlags.CatchAll:
                    case CatchFlags.CatchAllRef:
                        il.Emit(OpCodes.Leave, dispatchLabels[i]);
                        break;
                }
            }

            // No clause matched → propagate.
            il.Emit(OpCodes.Rethrow);
            il.EndExceptionBlock();
            decTryDepth(1);

            // Dispatch labels sit outside the try/catch. Each pushes the
            // target label's expected operands onto the internal stack.
            for (int i = 0; i < catches.Length; i++)
            {
                il.MarkLabel(dispatchLabels[i]);
                var handler = catches[i];
                int labelDepth = (int)handler.L.Value;

                switch (handler.Mode)
                {
                    case CatchFlags.None:
                        EmitPushFieldsFromExn(il, exnLocal, handler.X, moduleInst);
                        break;
                    case CatchFlags.CatchRef:
                        EmitPushFieldsFromExn(il, exnLocal, handler.X, moduleInst);
                        il.Emit(OpCodes.Ldloc, exnLocal); // exnref (WasmException)
                        break;
                    case CatchFlags.CatchAll:
                        break;
                    case CatchFlags.CatchAllRef:
                        il.Emit(OpCodes.Ldloc, exnLocal); // exnref only
                        break;
                }

                // Branch to the target catch label (resolve from block stack).
                // Depth 0 = try_table's own end (we pushed that); higher depths
                // walk outward.
                int idx = 0;
                foreach (var target in blockStack)
                {
                    if (idx == labelDepth)
                    {
                        il.Emit(OpCodes.Br, target.BranchTarget);
                        break;
                    }
                    idx++;
                }
            }

            blockStack.Pop();
            il.MarkLabel(endLabel);
        }

        /// <summary>
        /// Push exception fields from exn.Fields[] onto the internal CIL stack,
        /// converting each Value to its internal representation:
        /// scalars → typed primitive; GC ref → object (via UnwrapRef);
        /// funcref/externref/v128 → Value passthrough.
        /// </summary>
        private static void EmitPushFieldsFromExn(ILGenerator il, LocalBuilder exnLocal,
            TagIdx tagIdx, ModuleInstance moduleInst)
        {
            var fieldTypes = ResolveTagFieldTypes(moduleInst, tagIdx);
            if (fieldTypes.Length == 0) return;

            // Cache the fields array in a local.
            il.Emit(OpCodes.Ldloc, exnLocal);
            il.Emit(OpCodes.Callvirt, WasmException_get_Fields);
            var fieldsLocal = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Stloc, fieldsLocal);

            for (int f = 0; f < fieldTypes.Length; f++)
            {
                il.Emit(OpCodes.Ldloc, fieldsLocal);
                il.Emit(OpCodes.Ldc_I4, f);
                il.Emit(OpCodes.Ldelem, typeof(Value));
                EmitConvertFromValueToInternal(il, fieldTypes[f], moduleInst);
            }
        }

        /// <summary>
        /// Resolve the tag's declared field types (i.e., the function type's
        /// parameter types). Works for local and imported tags.
        /// </summary>
        private static ValType[] ResolveTagFieldTypes(ModuleInstance moduleInst, TagIdx tagIdx)
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

        /// <summary>
        /// Convert a value on the stack (in internal representation for its WASM
        /// type) to a Value suitable for Value[] storage. Mirrors the boundary
        /// wrap used for global.set / table.set / call args (doc 2 §3).
        /// </summary>
        private static void EmitConvertToValueForStorage(ILGenerator il, ValType type, ModuleInstance moduleInst)
        {
            switch (type)
            {
                case ValType.I32:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(int) })!);
                    return;
                case ValType.I64:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(long) })!);
                    return;
                case ValType.F32:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(float) })!);
                    return;
                case ValType.F64:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(double) })!);
                    return;
            }
            if (ModuleTranspiler.IsGcRefType(type, moduleInst))
            {
                il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                    nameof(GcRuntimeHelpers.WrapRef), BindingFlags.Public | BindingFlags.Static)!);
                return;
            }
            // funcref/externref/v128: already a Value on the stack.
        }

        /// <summary>
        /// Convert a Value on the stack to the internal representation for its
        /// WASM type. Mirrors global.get / table.get / call result unwrap.
        /// </summary>
        private static void EmitConvertFromValueToInternal(ILGenerator il, ValType type, ModuleInstance moduleInst)
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
                    il.Emit(OpCodes.Ldflda, typeof(Value).GetField(nameof(Value.Data))!);
                    var field = type switch
                    {
                        ValType.I32 => typeof(DUnion).GetField(nameof(DUnion.Int32))!,
                        ValType.I64 => typeof(DUnion).GetField(nameof(DUnion.Int64))!,
                        ValType.F32 => typeof(DUnion).GetField(nameof(DUnion.Float32))!,
                        ValType.F64 => typeof(DUnion).GetField(nameof(DUnion.Float64))!,
                        _ => throw new InvalidOperationException()
                    };
                    il.Emit(OpCodes.Ldfld, field);
                    return;
                }
            }
            if (ModuleTranspiler.IsGcRefType(type, moduleInst))
            {
                il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                    nameof(GcRuntimeHelpers.UnwrapRef), BindingFlags.Public | BindingFlags.Static)!);
                return;
            }
            // funcref/externref/v128: leave Value on the stack.
        }
    }
}
