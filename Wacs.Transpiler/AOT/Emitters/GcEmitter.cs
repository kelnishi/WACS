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
using Wacs.Core.Instructions.GC;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for 0xFB prefix (GC) instructions using emitted CLR types.
    ///
    /// struct.new → new WasmStruct_N(); obj.field_0 = pop; ...
    /// struct.get → obj.field_N (direct typed field access, no boxing)
    /// array.new → new WasmArray_N(); arr.elements = new T[len]; ...
    /// ref.test/cast → structural hash comparison
    /// i31 → int boxing/unboxing
    /// </summary>
    internal static class GcEmitter
    {
        public static bool CanEmit(GcCode op, GcTypeEmitter gcTypes)
        {
            byte b = (byte)op;

            // Struct ops: 0x00-0x05
            if (b <= 0x05) return true;

            // All array ops: 0x06-0x13
            if (b >= 0x06 && b <= 0x13) return true;

            // ref.test/cast: 0x14-0x17
            // br_on_cast/fail: 0x18-0x19
            if (b >= 0x14 && b <= 0x19) return true;

            // Conversion: 0x1A-0x1B
            if (op == GcCode.AnyConvertExtern || op == GcCode.ExternConvertAny)
                return true;

            // i31: 0x1C-0x1E
            if (op == GcCode.RefI31 || op == GcCode.I31GetS || op == GcCode.I31GetU)
                return true;

            return false;
        }

        /// <summary>
        /// branchTarget is a callback to resolve label depth → IL Label (from block stack).
        /// Needed for br_on_cast/fail.
        /// </summary>
        public static void Emit(ILGenerator il, InstructionBase inst, GcCode op,
            GcTypeEmitter gcTypes, ModuleInstance moduleInst,
            Func<int, System.Reflection.Emit.Label>? branchTarget = null)
        {
            switch (op)
            {
                // === Struct ops ===
                case GcCode.StructNew:
                    EmitStructNew(il, (InstStructNew)inst, gcTypes, moduleInst);
                    break;
                case GcCode.StructNewDefault:
                    EmitStructNewDefault(il, (InstStructNewDefault)inst, gcTypes);
                    break;
                case GcCode.StructGet:
                case GcCode.StructGetS:
                case GcCode.StructGetU:
                    EmitStructGet(il, (InstStructGet)inst, gcTypes);
                    break;
                case GcCode.StructSet:
                    EmitStructSet(il, (InstStructSet)inst, gcTypes);
                    break;

                // === Array ops ===
                case GcCode.ArrayNew:
                    EmitArrayNew(il, (InstArrayNew)inst, gcTypes, moduleInst);
                    break;
                case GcCode.ArrayNewDefault:
                    EmitArrayNewDefault(il, (InstArrayNewDefault)inst, gcTypes, moduleInst);
                    break;
                case GcCode.ArrayGet:
                case GcCode.ArrayGetS:
                case GcCode.ArrayGetU:
                    EmitArrayGet(il, (InstArrayGet)inst, gcTypes, moduleInst);
                    break;
                case GcCode.ArraySet:
                    EmitArraySet(il, (InstArraySet)inst, gcTypes, moduleInst);
                    break;
                case GcCode.ArrayLen:
                    EmitArrayLen(il);
                    break;
                case GcCode.ArrayNewFixed:
                    EmitArrayNewFixed(il, (InstArrayNewFixed)inst, gcTypes, moduleInst);
                    break;
                case GcCode.ArrayNewData:
                    EmitArrayNewData(il, (InstArrayNewData)inst, gcTypes);
                    break;
                case GcCode.ArrayNewElem:
                    EmitArrayNewElem(il, (InstArrayNewElem)inst, gcTypes);
                    break;
                case GcCode.ArrayFill:
                    EmitArrayFill(il, (InstArrayFill)inst, gcTypes);
                    break;
                case GcCode.ArrayCopy:
                    EmitArrayCopy(il, (InstArrayCopy)inst, gcTypes);
                    break;
                case GcCode.ArrayInitData:
                    EmitArrayInitData(il, (InstArrayInitData)inst, gcTypes);
                    break;
                case GcCode.ArrayInitElem:
                    EmitArrayInitElem(il, (InstArrayInitElem)inst, gcTypes);
                    break;

                // === ref.test / ref.cast ===
                case GcCode.RefTest:
                case GcCode.RefTestNull:
                    EmitRefTest(il, (InstRefTest)inst);
                    break;
                case GcCode.RefCast:
                case GcCode.RefCastNull:
                    EmitRefCast(il, (InstRefCast)inst);
                    break;

                // === br_on_cast ===
                case GcCode.BrOnCast:
                    EmitBrOnCast(il, (InstBrOnCast)inst, branchTarget!, castFail: false);
                    break;
                case GcCode.BrOnCastFail:
                    EmitBrOnCast(il, (InstBrOnCastFail)inst, branchTarget!, castFail: true);
                    break;

                // === Conversions ===
                case GcCode.AnyConvertExtern:
                case GcCode.ExternConvertAny:
                    // These are identity operations for our representation
                    // (both are Value on the CIL stack)
                    break;

                // === i31 ===
                case GcCode.RefI31:
                    EmitRefI31(il);
                    break;
                case GcCode.I31GetS:
                    EmitI31Get(il, signed: true);
                    break;
                case GcCode.I31GetU:
                    EmitI31Get(il, signed: false);
                    break;

                default:
                    throw new TranspilerException($"GcEmitter: unhandled opcode {op}");
            }
        }

        // struct.new X: pop N fields (reverse order), create instance, store fields
        private static void EmitStructNew(ILGenerator il, InstStructNew inst,
            GcTypeEmitter gcTypes, ModuleInstance moduleInst)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"struct.new: type {inst.TypeIndex} not emitted");

            var structDef = moduleInst.Types[(TypeIdx)inst.TypeIndex].Expansion as StructType;
            if (structDef == null)
                throw new TranspilerException($"struct.new: type {inst.TypeIndex} is not a struct");

            int fieldCount = structDef.Arity;

            // Spill fields from stack (reverse order — last field is on top)
            var temps = new LocalBuilder[fieldCount];
            for (int i = fieldCount - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(gcType.Fields[i].FieldType);
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Create instance
            il.Emit(OpCodes.Newobj, gcType.ClrType.GetConstructor(Type.EmptyTypes)!);

            // Store fields
            for (int i = 0; i < fieldCount; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldloc, temps[i]);
                il.Emit(OpCodes.Stfld, gcType.Fields[i]);
            }

            EmitWrapGcRef(il, inst.TypeIndex);
        }

        // struct.new_default X: create instance (fields already zero-initialized by CLR)
        private static void EmitStructNewDefault(ILGenerator il, InstStructNewDefault inst,
            GcTypeEmitter gcTypes)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"struct.new_default: type {inst.TypeIndex} not emitted");
            il.Emit(OpCodes.Newobj, gcType.ClrType.GetConstructor(Type.EmptyTypes)!);
            EmitWrapGcRef(il, inst.TypeIndex);
        }

        // struct.get X Y: pop structref, load field Y
        private static void EmitStructGet(ILGenerator il, InstStructGet inst,
            GcTypeEmitter gcTypes)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"struct.get: type {inst.TypeIndex} not emitted");

            // Stack: [structref (Value)]
            EmitUnwrapGcRef(il, gcType.ClrType);
            il.Emit(OpCodes.Ldfld, gcType.Fields[inst.FieldIndex]);

            // Handle packed field sign extension
            if (inst.SignExtension == PackedExt.Signed)
            {
                // Byte → sbyte → int (sign extend)
                il.Emit(OpCodes.Conv_I1);
                il.Emit(OpCodes.Conv_I4);
            }
            // Unsigned packed fields: byte naturally zero-extends to int
        }

        // struct.set X Y: pop structref, pop value, store field Y
        private static void EmitStructSet(ILGenerator il, InstStructSet inst,
            GcTypeEmitter gcTypes)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"struct.set: type {inst.TypeIndex} not emitted");

            // Stack: [structref (Value), value]
            var valLocal = il.DeclareLocal(gcType.Fields[inst.FieldIndex].FieldType);
            il.Emit(OpCodes.Stloc, valLocal);
            EmitUnwrapGcRef(il, gcType.ClrType);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Stfld, gcType.Fields[inst.FieldIndex]);
        }

        // array.new X: pop [init, length], create array instance
        private static void EmitArrayNew(ILGenerator il, InstArrayNew inst,
            GcTypeEmitter gcTypes, ModuleInstance moduleInst)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"array.new: type {inst.TypeIndex} not emitted");

            // Stack: [initVal, length]
            var lenLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, lenLocal);
            var initLocal = il.DeclareLocal(gcType.Fields[0].FieldType.GetElementType()!);
            il.Emit(OpCodes.Stloc, initLocal);

            // Create instance
            il.Emit(OpCodes.Newobj, gcType.ClrType.GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Dup);

            // arr.elements = new T[length]
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Newarr, gcType.Fields[0].FieldType.GetElementType()!);
            il.Emit(OpCodes.Stfld, gcType.Fields[0]); // elements

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Stfld, gcType.Fields[1]); // length

            // TODO: fill with init value

            // Wrap the CLR object reference into a Value struct.
            // The CIL stack has WasmArray_N (an IGcRef). Method signatures use Value.
            EmitWrapGcRef(il, inst.TypeIndex);
        }

        // array.new_default X: pop [length], create zero-initialized array
        private static void EmitArrayNewDefault(ILGenerator il, InstArrayNewDefault inst,
            GcTypeEmitter gcTypes, ModuleInstance moduleInst)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"array.new_default: type {inst.TypeIndex} not emitted");

            var lenLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, lenLocal);

            il.Emit(OpCodes.Newobj, gcType.ClrType.GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Newarr, gcType.Fields[0].FieldType.GetElementType()!);
            il.Emit(OpCodes.Stfld, gcType.Fields[0]);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Stfld, gcType.Fields[1]);

            EmitWrapGcRef(il, inst.TypeIndex);
        }

        // array.get X: pop [arrayref, index], load element
        private static void EmitArrayGet(ILGenerator il, InstArrayGet inst,
            GcTypeEmitter gcTypes, ModuleInstance moduleInst)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"array.get: type {inst.TypeIndex} not emitted");

            // Stack: [arrayref (Value), index]
            var idxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, idxLocal);
            EmitUnwrapGcRef(il, gcType.ClrType);
            il.Emit(OpCodes.Ldfld, gcType.Fields[0]); // elements array
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldelem, gcType.Fields[0].FieldType.GetElementType()!);

            if (inst.SignExtension == PackedExt.Signed)
            {
                il.Emit(OpCodes.Conv_I1);
                il.Emit(OpCodes.Conv_I4);
            }
        }

        // array.set X: pop [arrayref, index, value], store element
        private static void EmitArraySet(ILGenerator il, InstArraySet inst,
            GcTypeEmitter gcTypes, ModuleInstance moduleInst)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"array.set: type {inst.TypeIndex} not emitted");

            // Stack: [arrayref (Value), index, value]
            var valLocal = il.DeclareLocal(gcType.Fields[0].FieldType.GetElementType()!);
            il.Emit(OpCodes.Stloc, valLocal);
            var idxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, idxLocal);
            EmitUnwrapGcRef(il, gcType.ClrType);
            il.Emit(OpCodes.Ldfld, gcType.Fields[0]); // elements array
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Stelem, gcType.Fields[0].FieldType.GetElementType()!);
        }

        // array.len: pop [arrayref], push length
        private static void EmitArrayLen(ILGenerator il)
        {
            // All array types have a 'length' field at Fields[1]
            // But we don't know the type here. Use a helper.
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.ArrayLen), BindingFlags.Public | BindingFlags.Static)!);
        }

        // array.new_fixed X N: pop N values, create array
        private static void EmitArrayNewFixed(ILGenerator il, InstArrayNewFixed inst,
            GcTypeEmitter gcTypes, ModuleInstance moduleInst)
        {
            int n = inst.FixedCount;
            int typeIdx = inst.TypeIndex;
            // Spill N values, push ctx + typeIdx + n + values as args to helper
            var temps = new LocalBuilder[n];
            for (int i = n - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(typeof(Value));
                il.Emit(OpCodes.Stloc, temps[i]);
            }
            // Call helper: ArrayNewFixed(ctx, typeIdx, Value[] vals) → object
            il.Emit(OpCodes.Ldc_I4, n);
            il.Emit(OpCodes.Newarr, typeof(Value));
            for (int i = 0; i < n; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, temps[i]);
                il.Emit(OpCodes.Stelem, typeof(Value));
            }
            il.Emit(OpCodes.Ldarg_0); // ctx
            il.Emit(OpCodes.Ldc_I4, typeIdx);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.ArrayNewFixed), BindingFlags.Public | BindingFlags.Static)!);
        }

        // array.new_data/new_elem/fill/copy/init_data/init_elem: dispatch to helpers
        private static void EmitArrayNewData(ILGenerator il, InstArrayNewData inst, GcTypeEmitter gcTypes)
        {
            EmitArrayHelper(il, inst.TypeIndex, inst.DataIndex, nameof(GcRuntimeHelpers.ArrayNewData), 2);
        }

        private static void EmitArrayNewElem(ILGenerator il, InstArrayNewElem inst, GcTypeEmitter gcTypes)
        {
            EmitArrayHelper(il, inst.TypeIndex, inst.ElemIndex, nameof(GcRuntimeHelpers.ArrayNewElem), 2);
        }

        private static void EmitArrayFill(ILGenerator il, InstArrayFill inst, GcTypeEmitter gcTypes)
        {
            // Stack: [arrayref, offset, val, len]
            var len = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, len);
            var val = il.DeclareLocal(typeof(Value)); il.Emit(OpCodes.Stloc, val);
            var off = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, off);
            var arr = il.DeclareLocal(typeof(Value)); il.Emit(OpCodes.Stloc, arr);
            il.Emit(OpCodes.Ldloc, arr);
            il.Emit(OpCodes.Ldloc, off);
            il.Emit(OpCodes.Ldloc, val);
            il.Emit(OpCodes.Ldloc, len);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.ArrayFill), BindingFlags.Public | BindingFlags.Static)!);
        }

        private static void EmitArrayCopy(ILGenerator il, InstArrayCopy inst, GcTypeEmitter gcTypes)
        {
            // Stack: [dstref, dstoff, srcref, srcoff, len]
            // Store as Value structs (not object) to avoid boxing corrupted refs
            var len = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, len);
            var srcoff = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, srcoff);
            var srcref = il.DeclareLocal(typeof(Value)); il.Emit(OpCodes.Stloc, srcref);
            var dstoff = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, dstoff);
            var dstref = il.DeclareLocal(typeof(Value)); il.Emit(OpCodes.Stloc, dstref);
            il.Emit(OpCodes.Ldloc, dstref);
            il.Emit(OpCodes.Ldloc, dstoff);
            il.Emit(OpCodes.Ldloc, srcref);
            il.Emit(OpCodes.Ldloc, srcoff);
            il.Emit(OpCodes.Ldloc, len);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.ArrayCopyValues), BindingFlags.Public | BindingFlags.Static)!);
        }

        private static void EmitArrayInitData(ILGenerator il, InstArrayInitData inst, GcTypeEmitter gcTypes)
        {
            // Stack: [arrayref, dstoff, srcoff, len]
            var len = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, len);
            var srcoff = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, srcoff);
            var dstoff = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, dstoff);
            var arr = il.DeclareLocal(typeof(Value)); il.Emit(OpCodes.Stloc, arr);
            il.Emit(OpCodes.Ldarg_0); // ctx
            il.Emit(OpCodes.Ldloc, arr);
            il.Emit(OpCodes.Ldloc, dstoff);
            il.Emit(OpCodes.Ldc_I4, inst.DataIndex);
            il.Emit(OpCodes.Ldloc, srcoff);
            il.Emit(OpCodes.Ldloc, len);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.ArrayInitData), BindingFlags.Public | BindingFlags.Static)!);
        }

        private static void EmitArrayInitElem(ILGenerator il, InstArrayInitElem inst, GcTypeEmitter gcTypes)
        {
            var len = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, len);
            var srcoff = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, srcoff);
            var dstoff = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, dstoff);
            var arr = il.DeclareLocal(typeof(Value)); il.Emit(OpCodes.Stloc, arr);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, arr);
            il.Emit(OpCodes.Ldloc, dstoff);
            il.Emit(OpCodes.Ldc_I4, inst.ElemIndex);
            il.Emit(OpCodes.Ldloc, srcoff);
            il.Emit(OpCodes.Ldloc, len);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.ArrayInitElem), BindingFlags.Public | BindingFlags.Static)!);
        }

        // Helper for array.new_data/new_elem pattern: pop [offset, length], call helper
        private static void EmitArrayHelper(ILGenerator il, int typeIdx, int segIdx, string helperName, int stackArgs)
        {
            var len = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, len);
            var off = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, off);
            il.Emit(OpCodes.Ldarg_0); // ctx
            il.Emit(OpCodes.Ldc_I4, typeIdx);
            il.Emit(OpCodes.Ldc_I4, segIdx);
            il.Emit(OpCodes.Ldloc, off);
            il.Emit(OpCodes.Ldloc, len);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                helperName, BindingFlags.Public | BindingFlags.Static)!);
        }

        // br_on_cast / br_on_cast_fail
        private static void EmitBrOnCast(ILGenerator il, InstructionBase inst,
            Func<int, System.Reflection.Emit.Label> branchTarget, bool castFail)
        {
            int label;
            ValType targetType;
            if (inst is InstBrOnCast boc)
            {
                label = boc.Label;
                targetType = boc.TargetType;
            }
            else
            {
                var bocf = (InstBrOnCastFail)inst;
                label = bocf.Label;
                targetType = bocf.TargetType;
            }

            // Stack: [ref (object)]
            // Test type, branch if match (br_on_cast) or mismatch (br_on_cast_fail)
            il.Emit(OpCodes.Dup); // keep ref on stack for both paths
            il.Emit(OpCodes.Ldc_I4, (int)targetType);
            il.Emit(OpCodes.Ldc_I4, 1); // nullable
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.RefTest), BindingFlags.Public | BindingFlags.Static)!);

            var target = branchTarget(label);
            if (castFail)
                il.Emit(OpCodes.Brfalse, target); // branch if test FAILS
            else
                il.Emit(OpCodes.Brtrue, target);  // branch if test SUCCEEDS
        }


        private static void EmitRefTest(ILGenerator il, InstRefTest inst)
        {
            il.Emit(OpCodes.Ldc_I4, (int)inst.HeapType);
            il.Emit(OpCodes.Ldc_I4, inst.IsNullable ? 1 : 0);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.RefTest), BindingFlags.Public | BindingFlags.Static)!);
        }

        // ref.cast: pop [ref], push [ref] (or trap)
        private static void EmitRefCast(ILGenerator il, InstRefCast inst)
        {
            il.Emit(OpCodes.Ldc_I4, (int)inst.HeapType);
            il.Emit(OpCodes.Ldc_I4, inst.IsNullable ? 1 : 0);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.RefCast), BindingFlags.Public | BindingFlags.Static)!);
        }

        // ref.i31: pop [i32], push [i31ref (as object)]
        private static void EmitRefI31(ILGenerator il)
        {
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.RefI31), BindingFlags.Public | BindingFlags.Static)!);
        }

        // i31.get_s / i31.get_u: pop [i31ref], push [i32]
        private static void EmitI31Get(ILGenerator il, bool signed)
        {
            il.Emit(OpCodes.Ldc_I4, signed ? 1 : 0);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.I31Get), BindingFlags.Public | BindingFlags.Static)!);
        }

        // ==================================================================
        // GC ref wrapping/unwrapping for CIL stack type safety.
        //
        // WASM uses opaque ref types on the stack. The CIL representation is
        // Value (a struct). GC emitters produce CLR object references that
        // must be wrapped into Value for the rest of the IL to work.
        // ==================================================================

        /// <summary>
        /// Wrap a CLR GC object reference (IGcRef) on the CIL stack into a Value.
        /// Stack: [object ref] → [Value]
        /// </summary>
        private static void EmitWrapGcRef(ILGenerator il, int typeIndex)
        {
            // Stack has the CLR object implementing IGcRef.
            // Call GcRuntimeHelpers.WrapRef(object) → Value
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.WrapRef), BindingFlags.Public | BindingFlags.Static)!);
        }

        /// <summary>
        /// Unwrap a Value on the CIL stack to a typed CLR GC object reference.
        /// Stack: [Value] → [typed object ref]
        /// </summary>
        private static void EmitUnwrapGcRef(ILGenerator il, Type targetClrType)
        {
            // Call GcRuntimeHelpers.UnwrapRef(Value) → object, then cast
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.UnwrapRef), BindingFlags.Public | BindingFlags.Static)!);
            il.Emit(OpCodes.Castclass, targetClrType);
        }
    }

    /// <summary>
    /// Runtime helpers for GC operations called from transpiled IL.
    /// These handle the Value-based operations that can't be done with pure CLR types.
    /// </summary>
    public static class GcRuntimeHelpers
    {
        /// <summary>
        /// Wrap a CLR GC object into a Value. The object must implement IGcRef.
        /// </summary>
        public static Value WrapRef(IGcRef gcRef)
        {
            // Use the 3-arg constructor that doesn't switch on StoreIndex
            return new Value(ValType.Nil, 0, gcRef);
        }

        /// <summary>
        /// Unwrap a Value to get the underlying CLR GC object.
        /// Returns the IGcRef as object for casting.
        /// </summary>
        public static object UnwrapRef(Value val)
        {
            if (val.IsNullRef)
                throw new TrapException("null reference");
            return val.GcRef ?? throw new TrapException("null reference");
        }

        /// <summary>
        /// Check if an object reference is null, including boxed Value structs
        /// representing WASM null references.
        /// </summary>
        private static bool IsNullRef(object? obj)
        {
            if (obj == null) return true;
            if (obj is Value v && v.IsNullRef) return true;
            return false;
        }

        public static int ArrayLen(Value arrayRef)
        {
            if (arrayRef.IsNullRef)
                throw new TrapException("null array reference");
            var gcRef = arrayRef.GcRef;
            if (gcRef == null)
                throw new TrapException("null array reference");
            // All emitted array types have a public 'length' field
            var field = gcRef.GetType().GetField("length");
            if (field == null)
                throw new TrapException("not an array type");
            return (int)field.GetValue(gcRef)!;
        }

        public static int RefTest(object val, int heapType, int nullable)
        {
            // Simplified: check if non-null (full type checking requires TypesSpace)
            if (val == null) return nullable != 0 ? 1 : 0;
            return 1;
        }

        public static object RefCast(object val, int heapType, int nullable)
        {
            if (val == null)
            {
                if (nullable != 0) return null!;
                throw new TrapException("cast failure: null reference");
            }
            return val;
        }

        public static object RefI31(int value)
        {
            // i31 is a 31-bit integer stored as a tagged reference
            // For CLR, box it
            return (object)(value & 0x7FFFFFFF);
        }

        public static int I31Get(object i31Ref, int signed)
        {
            if (i31Ref == null)
                throw new TrapException("null i31 reference");
            int val = (int)i31Ref & 0x7FFFFFFF;
            if (signed != 0 && (val & 0x40000000) != 0)
                val |= unchecked((int)0x80000000); // sign extend bit 30
            return val;
        }

        public static object ArrayNewFixed(Value[] values, TranspiledContext ctx, int typeIdx)
        {
            // Create array instance and populate from values
            var gcType = ctx.Module?.Types[(TypeIdx)typeIdx];
            // For now, return a simple wrapper
            var arr = new object[values.Length];
            for (int i = 0; i < values.Length; i++)
                arr[i] = values[i].GcRef!;
            return arr;
        }

        public static object ArrayNewData(TranspiledContext ctx, int typeIdx, int dataIdx, int offset, int length)
        {
            if (ctx.Store == null || ctx.Module == null)
                throw new TrapException("array.new_data requires runtime store");
            // Simplified: create array from data segment bytes
            var dataAddr = ctx.Module.DataAddrs[(DataIdx)dataIdx];
            var data = ctx.Store[dataAddr];
            if (offset + length > data.Data.Length)
                throw new TrapException("out of bounds data access");
            var result = new byte[length];
            System.Buffer.BlockCopy(data.Data, offset, result, 0, length);
            return result;
        }

        public static object ArrayNewElem(TranspiledContext ctx, int typeIdx, int elemIdx, int offset, int length)
        {
            if (ctx.Store == null || ctx.Module == null)
                throw new TrapException("array.new_elem requires runtime store");
            var elemAddr = ctx.Module.ElemAddrs[(ElemIdx)elemIdx];
            var elem = ctx.Store[elemAddr];
            if (offset + length > elem.Elements.Count)
                throw new TrapException("out of bounds element access");
            var result = new object[length];
            for (int i = 0; i < length; i++)
                result[i] = elem.Elements[offset + i];
            return result;
        }

        public static void ArrayFill(Value arrayRef, int offset, Value value, int length)
        {
            if (arrayRef.IsNullRef) throw new TrapException("null array reference");
            var gcRef = arrayRef.GcRef;
            if (gcRef == null) throw new TrapException("null array reference");
            var elementsField = gcRef.GetType().GetField("elements");
            if (elementsField == null) throw new TrapException("not an array type");
            var elements = (System.Array)elementsField.GetValue(gcRef)!;
            for (int i = 0; i < length; i++)
                elements.SetValue(value, offset + i);
        }

        public static void ArrayCopy(object dstRef, int dstOff, object srcRef, int srcOff, int length)
        {
            if (IsNullRef(dstRef) || IsNullRef(srcRef)) throw new TrapException("null array reference");
            var dstField = dstRef.GetType().GetField("elements");
            var srcField = srcRef.GetType().GetField("elements");
            if (dstField == null || srcField == null) throw new TrapException("not an array type");
            var dst = (System.Array)dstField.GetValue(dstRef)!;
            var src = (System.Array)srcField.GetValue(srcRef)!;
            if (length == 0) return; // No-op copy is always valid
            System.Array.Copy(src, srcOff, dst, dstOff, length);
        }

        /// <summary>
        /// Array copy taking Value structs directly to avoid boxing null refs
        /// into invalid CLR object references.
        /// </summary>
        public static void ArrayCopyValues(Value dstRef, int dstOff, Value srcRef, int srcOff, int length)
        {
            if (dstRef.IsNullRef || srcRef.IsNullRef)
                throw new TrapException("null array reference");
            if (length == 0) return;
            // Extract underlying GcRef objects (IGcRef) and use reflection
            var dstGc = dstRef.GcRef;
            var srcGc = srcRef.GcRef;
            if (dstGc == null || srcGc == null)
                throw new TrapException("null array reference");
            var dstField = dstGc.GetType().GetField("elements");
            var srcField = srcGc.GetType().GetField("elements");
            if (dstField == null || srcField == null) throw new TrapException("not an array type");
            System.Array.Copy(
                (System.Array)srcField.GetValue(srcGc)!, srcOff,
                (System.Array)dstField.GetValue(dstGc)!, dstOff,
                length);
        }

        public static void ArrayInitData(TranspiledContext ctx, Value arrayRef, int dstOff,
            int dataIdx, int srcOff, int length)
        {
            if (ctx.Store == null || ctx.Module == null)
                throw new TrapException("array.init_data requires runtime store");
            if (arrayRef.IsNullRef) throw new TrapException("null array reference");
            var gcRef = arrayRef.GcRef;
            if (gcRef == null) throw new TrapException("null array reference");
            var dataAddr = ctx.Module.DataAddrs[(DataIdx)dataIdx];
            var data = ctx.Store[dataAddr];
            var field = gcRef.GetType().GetField("elements");
            if (field == null) throw new TrapException("not an array type");
            var elements = (System.Array)field.GetValue(gcRef)!;
            // Byte-level copy from data segment to array elements
            for (int i = 0; i < length; i++)
                elements.SetValue(data.Data[srcOff + i], dstOff + i);
        }

        public static void ArrayInitElem(TranspiledContext ctx, Value arrayRef, int dstOff,
            int elemIdx, int srcOff, int length)
        {
            if (ctx.Store == null || ctx.Module == null)
                throw new TrapException("array.init_elem requires runtime store");
            if (arrayRef.IsNullRef) throw new TrapException("null array reference");
            var gcRef = arrayRef.GcRef;
            if (gcRef == null) throw new TrapException("null array reference");
            var elemAddr = ctx.Module.ElemAddrs[(ElemIdx)elemIdx];
            var elem = ctx.Store[elemAddr];
            var field = gcRef.GetType().GetField("elements");
            if (field == null) throw new TrapException("not an array type");
            var elements = (System.Array)field.GetValue(gcRef)!;
            for (int i = 0; i < length; i++)
                elements.SetValue(elem.Elements[srcOff + i], dstOff + i);
        }
    }
}
