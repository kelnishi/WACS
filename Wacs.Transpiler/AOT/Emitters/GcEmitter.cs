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
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.GC;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.GC;
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
            Func<int, System.Reflection.Emit.Label>? branchTarget = null,
            CilValidator? cv = null)
        {
            switch (op)
            {
                // === Struct ops ===
                case GcCode.StructNew:
                {
                    var si = (InstStructNew)inst;
                    var gcType = gcTypes.GetGcType(si.TypeIndex);
                    int fieldCount = gcType?.Fields.Length ?? 0;
                    cv?.Pop(fieldCount, "struct.new fields");
                    EmitStructNew(il, si, gcTypes, moduleInst);
                    cv?.Push(typeof(Value)); // structref as Value
                    break;
                }
                case GcCode.StructNewDefault:
                    EmitStructNewDefault(il, (InstStructNewDefault)inst, gcTypes);
                    cv?.Push(typeof(Value)); // structref
                    break;
                case GcCode.StructGet:
                case GcCode.StructGetS:
                case GcCode.StructGetU:
                {
                    cv?.Pop(typeof(Value), "struct.get ref");
                    EmitStructGet(il, (InstStructGet)inst, gcTypes);
                    // Result type depends on field type — scalar or Value(ref)
                    var sgi = (InstStructGet)inst;
                    var gt = gcTypes.GetGcType(sgi.TypeIndex);
                    var ft = gt?.Fields[sgi.FieldIndex].FieldType;
                    cv?.Push(ft != null && IsScalarType(ft) ? ft : typeof(Value));
                    break;
                }
                case GcCode.StructSet:
                {
                    var ssi = (InstStructSet)inst;
                    var gt = gcTypes.GetGcType(ssi.TypeIndex);
                    var ft = gt?.Fields[ssi.FieldIndex].FieldType;
                    cv?.Pop(context: "struct.set val");
                    cv?.Pop(typeof(Value), "struct.set ref");
                    EmitStructSet(il, ssi, gcTypes);
                    break;
                }

                // === Array ops ===
                case GcCode.ArrayNew:
                    cv?.Pop(typeof(int), "array.new len");
                    cv?.Pop(context: "array.new init");
                    EmitArrayNew(il, (InstArrayNew)inst, gcTypes, moduleInst);
                    cv?.Push(typeof(Value)); // arrayref
                    break;
                case GcCode.ArrayNewDefault:
                    cv?.Pop(typeof(int), "array.new_default len");
                    EmitArrayNewDefault(il, (InstArrayNewDefault)inst, gcTypes, moduleInst);
                    cv?.Push(typeof(Value)); // arrayref
                    break;
                case GcCode.ArrayGet:
                case GcCode.ArrayGetS:
                case GcCode.ArrayGetU:
                {
                    cv?.Pop(typeof(int), "array.get idx");
                    cv?.Pop(typeof(Value), "array.get ref");
                    EmitArrayGet(il, (InstArrayGet)inst, gcTypes, moduleInst);
                    // Result: scalar or Value depending on element type
                    var agi = (InstArrayGet)inst;
                    var agt = gcTypes.GetGcType(agi.TypeIndex);
                    var eft = agt?.Fields.Length > 0 ? agt.Fields[0].FieldType.GetElementType() : null;
                    cv?.Push(eft != null && IsScalarType(eft) ? eft : typeof(Value));
                    break;
                }
                case GcCode.ArraySet:
                    cv?.Pop(context: "array.set val");
                    cv?.Pop(typeof(int), "array.set idx");
                    cv?.Pop(typeof(Value), "array.set ref");
                    EmitArraySet(il, (InstArraySet)inst, gcTypes, moduleInst);
                    break;
                case GcCode.ArrayLen:
                    cv?.Pop(typeof(Value), "array.len ref");
                    EmitArrayLen(il);
                    cv?.Push(typeof(int));
                    break;
                case GcCode.ArrayNewFixed:
                {
                    var anfi = (InstArrayNewFixed)inst;
                    cv?.Pop(anfi.FixedCount, "array.new_fixed elems");
                    EmitArrayNewFixed(il, anfi, gcTypes, moduleInst);
                    cv?.Push(typeof(Value)); // arrayref
                    break;
                }
                case GcCode.ArrayNewData:
                    cv?.Pop(typeof(int), "array.new_data len");
                    cv?.Pop(typeof(int), "array.new_data offset");
                    EmitArrayNewData(il, (InstArrayNewData)inst, gcTypes);
                    cv?.Push(typeof(Value)); // arrayref
                    break;
                case GcCode.ArrayNewElem:
                    cv?.Pop(typeof(int), "array.new_elem len");
                    cv?.Pop(typeof(int), "array.new_elem offset");
                    EmitArrayNewElem(il, (InstArrayNewElem)inst, gcTypes);
                    cv?.Push(typeof(Value)); // arrayref
                    break;
                case GcCode.ArrayFill:
                    cv?.Pop(typeof(int), "array.fill len");
                    cv?.Pop(context: "array.fill val");
                    cv?.Pop(typeof(int), "array.fill offset");
                    cv?.Pop(typeof(Value), "array.fill ref");
                    EmitArrayFill(il, (InstArrayFill)inst, gcTypes);
                    break;
                case GcCode.ArrayCopy:
                    cv?.Pop(typeof(int), "array.copy len");
                    cv?.Pop(typeof(int), "array.copy src_off");
                    cv?.Pop(typeof(Value), "array.copy src_ref");
                    cv?.Pop(typeof(int), "array.copy dst_off");
                    cv?.Pop(typeof(Value), "array.copy dst_ref");
                    EmitArrayCopy(il, (InstArrayCopy)inst, gcTypes);
                    break;
                case GcCode.ArrayInitData:
                    cv?.Pop(typeof(int), "array.init_data len");
                    cv?.Pop(typeof(int), "array.init_data src_off");
                    cv?.Pop(typeof(int), "array.init_data dst_off");
                    cv?.Pop(typeof(Value), "array.init_data ref");
                    EmitArrayInitData(il, (InstArrayInitData)inst, gcTypes);
                    break;
                case GcCode.ArrayInitElem:
                    cv?.Pop(typeof(int), "array.init_elem len");
                    cv?.Pop(typeof(int), "array.init_elem src_off");
                    cv?.Pop(typeof(int), "array.init_elem dst_off");
                    cv?.Pop(typeof(Value), "array.init_elem ref");
                    EmitArrayInitElem(il, (InstArrayInitElem)inst, gcTypes);
                    break;

                // === ref.test / ref.cast ===
                case GcCode.RefTest:
                case GcCode.RefTestNull:
                    cv?.Pop(typeof(Value), "ref.test val");
                    EmitRefTest(il, (InstRefTest)inst);
                    cv?.Push(typeof(int)); // i32 result
                    break;
                case GcCode.RefCast:
                case GcCode.RefCastNull:
                    cv?.Pop(typeof(Value), "ref.cast val");
                    EmitRefCast(il, (InstRefCast)inst);
                    cv?.Push(typeof(Value)); // casted ref
                    break;

                // === br_on_cast ===
                case GcCode.BrOnCast:
                    EmitBrOnCast(il, (InstBrOnCast)inst, branchTarget!, castFail: false);
                    break;
                case GcCode.BrOnCastFail:
                    EmitBrOnCast(il, (InstBrOnCastFail)inst, branchTarget!, castFail: true);
                    break;

                // === Conversions ===
                case GcCode.ExternConvertAny:
                    cv?.Pop(typeof(Value), "extern.convert_any");
                    il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                        nameof(GcRuntimeHelpers.ExternConvertAny), BindingFlags.Public | BindingFlags.Static)!);
                    cv?.Push(typeof(Value)); // externref
                    break;
                case GcCode.AnyConvertExtern:
                    cv?.Pop(typeof(Value), "any.convert_extern");
                    il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                        nameof(GcRuntimeHelpers.AnyConvertExtern), BindingFlags.Public | BindingFlags.Static)!);
                    cv?.Push(typeof(Value)); // anyref
                    break;

                // === i31 ===
                case GcCode.RefI31:
                    cv?.Pop(typeof(int), "ref.i31");
                    EmitRefI31(il);
                    cv?.Push(typeof(Value)); // i31ref
                    break;
                case GcCode.I31GetS:
                    cv?.Pop(typeof(Value), "i31.get_s");
                    EmitI31Get(il, signed: true);
                    cv?.Push(typeof(int));
                    break;
                case GcCode.I31GetU:
                    cv?.Pop(typeof(Value), "i31.get_u");
                    EmitI31Get(il, signed: false);
                    cv?.Push(typeof(int));
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
            var fieldType = gcType.Fields[inst.FieldIndex].FieldType;
            il.Emit(OpCodes.Ldfld, gcType.Fields[inst.FieldIndex]);

            // Handle packed field sign extension
            if (inst.SignExtension == PackedExt.Signed)
            {
                il.Emit(OpCodes.Conv_I1);
                il.Emit(OpCodes.Conv_I4);
            }

            if (!IsScalarType(fieldType))
            {
                EmitWrapGcRef(il, inst.TypeIndex);
            }
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

            // Stack: [arrayref, index]
            // arrayref might be Value (from function params/returns) or raw CLR object
            // (from a preceding array.get that returned a ref element).
            // We handle both by checking the array type and using Castclass.
            var idxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, idxLocal);

            // Unwrap Value to CLR object if the stack has Value
            // For nested gets, the stack might already have a CLR object
            EmitUnwrapGcRef(il, gcType.ClrType);

            il.Emit(OpCodes.Ldfld, gcType.Fields[0]); // elements array
            il.Emit(OpCodes.Ldloc, idxLocal);

            var elementClrType = gcType.Fields[0].FieldType.GetElementType()!;
            il.Emit(OpCodes.Ldelem, elementClrType);

            if (inst.SignExtension == PackedExt.Signed)
            {
                il.Emit(OpCodes.Conv_I1);
                il.Emit(OpCodes.Conv_I4);
            }

            // Reference-typed elements must be wrapped back to Value for the
            // WASM stack (method signatures use Value for all ref types).
            if (!IsScalarType(elementClrType))
            {
                EmitWrapGcRef(il, inst.TypeIndex);
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

        // array.new_fixed X N: pop N values, create array directly
        private static void EmitArrayNewFixed(ILGenerator il, InstArrayNewFixed inst,
            GcTypeEmitter gcTypes, ModuleInstance moduleInst)
        {
            int n = inst.FixedCount;
            int typeIdx = inst.TypeIndex;
            var gcType = gcTypes.GetGcType(typeIdx);
            if (gcType == null)
                throw new TranspilerException($"array.new_fixed: type {typeIdx} not emitted");

            var elemClrType = gcType.Fields[0].FieldType.GetElementType()!;

            // Spill N values from CIL stack using the actual element type
            var temps = new LocalBuilder[n];
            for (int i = n - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(IsScalarType(elemClrType) ? elemClrType : typeof(Value));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Create WasmArray_N instance directly
            il.Emit(OpCodes.Newobj, gcType.ClrType.GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Dup);

            // arr.elements = new T[n]
            il.Emit(OpCodes.Ldc_I4, n);
            il.Emit(OpCodes.Newarr, elemClrType);

            // Fill elements
            for (int i = 0; i < n; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, temps[i]);
                il.Emit(OpCodes.Stelem, elemClrType);
            }

            il.Emit(OpCodes.Stfld, gcType.Fields[0]); // elements

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, n);
            il.Emit(OpCodes.Stfld, gcType.Fields[1]); // length

            EmitWrapGcRef(il, typeIdx);
        }

        // array.new_data/new_elem/fill/copy/init_data/init_elem: dispatch to helpers
        private static void EmitArrayNewData(ILGenerator il, InstArrayNewData inst, GcTypeEmitter gcTypes)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"array.new_data: type {inst.TypeIndex} not emitted");

            // Call helper to get raw byte data
            EmitArrayHelper(il, inst.TypeIndex, inst.DataIndex, nameof(GcRuntimeHelpers.ArrayNewData), 2);
            // Stack: [byte[]]
            EmitWrapRawArrayInGcType(il, gcType);
        }

        private static void EmitArrayNewElem(ILGenerator il, InstArrayNewElem inst, GcTypeEmitter gcTypes)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"array.new_elem: type {inst.TypeIndex} not emitted");

            // Call helper to get raw element data
            EmitArrayHelper(il, inst.TypeIndex, inst.ElemIndex, nameof(GcRuntimeHelpers.ArrayNewElem), 2);
            // Stack: [object[]]
            EmitWrapRawArrayInGcType(il, gcType);
        }

        /// <summary>
        /// Wrap a raw array (byte[], object[], etc.) returned by a helper into the
        /// proper emitted GC type. Creates a WasmArray_N instance and stores the
        /// raw array in its elements field.
        /// Stack: [raw array] → [Value (wrapped GC ref)]
        /// </summary>
        private static void EmitWrapRawArrayInGcType(ILGenerator il, EmittedGcType gcType)
        {
            var rawLocal = il.DeclareLocal(typeof(object));
            il.Emit(OpCodes.Stloc, rawLocal);

            // Create GC type instance
            il.Emit(OpCodes.Newobj, gcType.ClrType.GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Dup);

            // Store elements — cast the raw array to the expected field type
            il.Emit(OpCodes.Ldloc, rawLocal);
            il.Emit(OpCodes.Castclass, gcType.Fields[0].FieldType); // cast to T[]
            il.Emit(OpCodes.Stfld, gcType.Fields[0]); // elements

            // Store length
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldloc, rawLocal);
            il.Emit(OpCodes.Castclass, typeof(Array));
            il.Emit(OpCodes.Call, typeof(Array).GetProperty("Length")!.GetGetMethod()!);
            il.Emit(OpCodes.Stfld, gcType.Fields[1]); // length

            EmitWrapGcRef(il, gcType.TypeIndex);
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

            // Stack: [ref (Value)]
            // Test type, branch if match (br_on_cast) or mismatch (br_on_cast_fail)
            il.Emit(OpCodes.Dup); // keep ref on stack for both paths
            il.Emit(OpCodes.Ldc_I4, (int)targetType);
            il.Emit(OpCodes.Ldc_I4, targetType.IsNullable() ? 1 : 0);
            il.Emit(OpCodes.Ldarg_0); // ThinContext
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.RefTestValue), BindingFlags.Public | BindingFlags.Static)!);

            var target = branchTarget(label);
            if (castFail)
                il.Emit(OpCodes.Brfalse, target); // branch if test FAILS
            else
                il.Emit(OpCodes.Brtrue, target);  // branch if test SUCCEEDS
        }


        private static void EmitRefTest(ILGenerator il, InstRefTest inst)
        {
            // Stack: [Value]
            il.Emit(OpCodes.Ldc_I4, (int)inst.HeapType);
            il.Emit(OpCodes.Ldc_I4, inst.IsNullable ? 1 : 0);
            il.Emit(OpCodes.Ldarg_0); // ThinContext for type lookup
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.RefTestValue), BindingFlags.Public | BindingFlags.Static)!);
        }

        // ref.cast: pop [ref (Value)], push [ref (Value)] (or trap)
        private static void EmitRefCast(ILGenerator il, InstRefCast inst)
        {
            il.Emit(OpCodes.Ldc_I4, (int)inst.HeapType);
            il.Emit(OpCodes.Ldc_I4, inst.IsNullable ? 1 : 0);
            il.Emit(OpCodes.Ldarg_0); // ThinContext for type lookup
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.RefCastValue), BindingFlags.Public | BindingFlags.Static)!);
        }

        // ref.i31: pop [i32], push [i31ref (as Value)]
        private static void EmitRefI31(ILGenerator il)
        {
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.RefI31Value), BindingFlags.Public | BindingFlags.Static)!);
        }

        // i31.get_s / i31.get_u: pop [i31ref (Value)], push [i32]
        private static void EmitI31Get(ILGenerator il, bool signed)
        {
            il.Emit(OpCodes.Ldc_I4, signed ? 1 : 0);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.I31GetValue), BindingFlags.Public | BindingFlags.Static)!);
        }

        // ==================================================================
        // GC ref wrapping/unwrapping for CIL stack type safety.
        //
        // WASM uses opaque ref types on the stack. The CIL representation is
        // Value (a struct). GC emitters produce CLR object references that
        // must be wrapped into Value for the rest of the IL to work.
        // ==================================================================

        /// <summary>
        /// Check if a CLR type is a scalar (stays on CIL stack as-is).
        /// Non-scalar types (object, interface, emitted GC types) need wrap/unwrap.
        /// </summary>
        private static bool IsScalarType(Type t)
        {
            return t == typeof(int) || t == typeof(long)
                || t == typeof(float) || t == typeof(double)
                || t == typeof(byte) || t == typeof(sbyte)
                || t == typeof(short) || t == typeof(ushort)
                || t == typeof(Value);
        }

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
        public static Value WrapRef(object? gcRef)
        {
            if (gcRef == null)
                return new Value(ValType.Any); // null ref
            var valType = DeriveValType(gcRef);
            Debug.Assert(valType != ValType.Nil, $"WrapRef: derived Nil type for {gcRef.GetType().Name}");
            if (gcRef is IGcRef igc)
                return new Value(valType, 0, igc);
            return new Value(valType, 0, new GcObjectAdapter(gcRef));
        }

        private static ValType DeriveValType(object gcRef)
        {
            var catField = gcRef.GetType().GetField("TypeCategory",
                BindingFlags.Public | BindingFlags.Static);
            if (catField != null)
            {
                int cat = (int)catField.GetValue(null)!;
                return cat switch
                {
                    0 => ValType.Struct,
                    1 => ValType.Array,
                    _ => ValType.Any,
                };
            }
            return ValType.Any;
        }

        /// <summary>
        /// Adapter that wraps a plain CLR object as IGcRef.
        /// Used for emitted GC types that don't implement IGcRef directly.
        /// </summary>
        private class GcObjectAdapter : IGcRef
        {
            public readonly object Target;
            public GcObjectAdapter(object target) => Target = target;
            public RefIdx StoreIndex => default(PtrIdx);
        }

        /// <summary>
        /// Unwrap a Value to get the underlying CLR GC object.
        /// Returns the IGcRef as object for casting.
        /// </summary>
        public static object UnwrapRef(Value val)
        {
            if (val.IsNullRef)
                throw new TrapException("null reference");
            var gcRef = val.GcRef ?? throw new TrapException("null reference");
            // Unwrap the adapter if present
            if (gcRef is GcObjectAdapter adapter)
                return adapter.Target;
            return gcRef;
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

        /// <summary>
        /// Unwrap a Value to the underlying emitted GC object, handling GcObjectAdapter.
        /// </summary>
        private static object UnwrapArrayRef(Value arrayRef)
        {
            if (arrayRef.IsNullRef) throw new TrapException("null array reference");
            var gcRef = arrayRef.GcRef ?? throw new TrapException("null array reference");
            return gcRef is GcObjectAdapter adapter ? adapter.Target : gcRef;
        }

        public static int ArrayLen(Value arrayRef)
        {
            var target = UnwrapArrayRef(arrayRef);
            var field = target.GetType().GetField("length");
            if (field == null)
                throw new TrapException("not an array type");
            return (int)field.GetValue(target)!;
        }

        public static int RefTest(object val, int heapType, int nullable)
        {
            if (val == null) return nullable != 0 ? 1 : 0;
            return 1;
        }

        /// <summary>
        /// Test if a Value matches a heap type. Layer 0: CLR inheritance + abstract types.
        /// heapType encoding: negative = abstract (HeapType enum), non-negative = concrete type index.
        /// </summary>
        // Mask to extract raw type index from ValType (strips Ref/Nullable bits)
        private const int ValTypeIndexMask = ~(0x4000_0000 | 0x2000_0000); // ~NullableRef

        public static int RefTestValue(Value val, int heapType, int nullable, ThinContext ctx)
        {
            if (val.IsNullRef) return nullable != 0 ? 1 : 0;

            // Extract raw index (strip Ref/Nullable bits)
            int rawIndex = heapType & ValTypeIndexMask;

            // Concrete type index (non-negative after masking, and not an abstract type byte)
            if (rawIndex >= 0 && rawIndex < 0x60) // abstract types are 0x6A-0x74
                return TestConcreteType(val, rawIndex, ctx) ? 1 : 0;

            // Abstract heap type — the low byte identifies the heap type
            byte ht = (byte)(heapType & 0xFF);
            return TestAbstractType(val, ht) ? 1 : 0;
        }

        private static bool TestAbstractType(Value val, byte ht)
        {
            return ht switch
            {
                (byte)Wacs.Core.Types.Defs.HeapType.Any => true,
                (byte)Wacs.Core.Types.Defs.HeapType.Eq => IsI31(val) || IsGcCategory(val, 0) || IsGcCategory(val, 1),
                (byte)Wacs.Core.Types.Defs.HeapType.I31 => IsI31(val),
                (byte)Wacs.Core.Types.Defs.HeapType.Struct => IsGcCategory(val, 0),
                (byte)Wacs.Core.Types.Defs.HeapType.Array => IsGcCategory(val, 1),
                (byte)Wacs.Core.Types.Defs.HeapType.Func => val.Type == ValType.FuncRef,
                (byte)Wacs.Core.Types.Defs.HeapType.Extern => val.Type == ValType.ExternRef,
                (byte)Wacs.Core.Types.Defs.HeapType.None
                    or (byte)Wacs.Core.Types.Defs.HeapType.NoFunc
                    or (byte)Wacs.Core.Types.Defs.HeapType.NoExtern => false, // bottom types
                _ => true, // unknown abstract type — permissive
            };
        }

        private static bool IsI31(Value val) =>
            val.Type == ValType.I31;

        private static bool IsGcCategory(Value val, int expectedCategory)
        {
            var gcRef = val.GcRef;
            if (gcRef == null) return false;
            object target = gcRef is GcObjectAdapter adapter ? adapter.Target : gcRef;
            var catField = target.GetType().GetField("TypeCategory",
                BindingFlags.Public | BindingFlags.Static);
            if (catField == null) return false;
            return (int)catField.GetValue(null)! == expectedCategory;
        }

        private static bool TestConcreteType(Value val, int typeIndex, ThinContext ctx)
        {
            var gcRef = val.GcRef;
            if (gcRef == null) return false;
            object target = gcRef is GcObjectAdapter adapter ? adapter.Target : gcRef;

            if (ctx.InitDataId < 0) return false;

            var targetClrType = GcTypeRegistry.Get(ctx.InitDataId, typeIndex);
            if (targetClrType == null) return false;

            // Layer 0: CLR IsInstanceOfType (handles subtype via direct inheritance)
            if (targetClrType.IsInstanceOfType(target))
                return true;

            // Layer 1: Structural hash equivalence (equi-recursive type canonicalization)
            // Walk the actual object's CLR type hierarchy, checking structural hash at each level
            int? targetHash = GetStructuralHash(targetClrType);
            if (targetHash == null) return false;

            var checkType = target.GetType();
            while (checkType != null && checkType != typeof(object))
            {
                int? checkHash = GetStructuralHash(checkType);
                if (checkHash != null && checkHash.Value == targetHash.Value)
                    return true;
                checkType = checkType.BaseType;
            }

            return false;
        }

        private static int? GetStructuralHash(Type clrType)
        {
            var field = clrType.GetField("StructuralHash",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field != null)
                return (int)field.GetValue(null)!;
            return null;
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

        /// <summary>Cast a Value to a heap type, trapping on failure.</summary>
        public static Value RefCastValue(Value val, int heapType, int nullable, ThinContext ctx)
        {
            if (val.IsNullRef)
            {
                if (nullable != 0) return val;
                throw new TrapException("cast failure: null reference");
            }
            if (RefTestValue(val, heapType, nullable, ctx) == 0)
                throw new TrapException("cast failure");
            return val;
        }

        public static object RefI31(int value)
        {
            return (object)(value & 0x7FFFFFFF);
        }

        /// <summary>Value-typed version for CIL stack compatibility.</summary>
        public static Value RefI31Value(int value)
        {
            int masked = value & 0x7FFFFFFF;
            return new Value(ValType.I31, masked, new I31Ref(masked));
        }

        public static int I31Get(object i31Ref, int signed)
        {
            if (i31Ref == null)
                throw new TrapException("null i31 reference");
            int val = (int)i31Ref & 0x7FFFFFFF;
            if (signed != 0 && (val & 0x40000000) != 0)
                val |= unchecked((int)0x80000000);
            return val;
        }

        /// <summary>Value-typed version for CIL stack compatibility.</summary>
        public static int I31GetValue(Value i31Ref, int signed)
        {
            if (i31Ref.IsNullRef)
                throw new TrapException("null i31 reference");
            int val = (int)i31Ref.Data.Int32 & 0x7FFFFFFF;
            if (signed != 0 && (val & 0x40000000) != 0)
                val |= unchecked((int)0x80000000);
            return val;
        }

        /// <summary>extern.convert_any: anyref → externref. Re-tags type, preserving data.</summary>
        public static Value ExternConvertAny(Value val)
        {
            if (val.IsNullRef)
                return new Value(ValType.ExternRef);
            var result = val;
            result.Type = val.Type.IsNullable() ? ValType.ExternRef : ValType.Extern;
            Debug.Assert(result.Type != ValType.Nil, "ExternConvertAny produced Nil type");
            return result;
        }

        /// <summary>any.convert_extern: externref → anyref. Recovers internal type from GcRef.</summary>
        public static Value AnyConvertExtern(Value val)
        {
            if (val.IsNullRef)
                return new Value(ValType.Any);
            bool nullable = val.Type.IsNullable();
            var gcRef = val.GcRef;
            if (gcRef is I31Ref)
            {
                val.Type = nullable ? ValType.I31 : ValType.I31NN;
                return val;
            }
            if (gcRef != null)
            {
                object target = gcRef is GcObjectAdapter adapter ? adapter.Target : gcRef;
                var catField = target.GetType().GetField("TypeCategory",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (catField != null)
                {
                    int cat = (int)catField.GetValue(null)!;
                    val.Type = cat switch
                    {
                        0 => nullable ? ValType.Struct : ValType.StructNN,
                        1 => nullable ? ValType.Array : ValType.ArrayNN,
                        _ => nullable ? ValType.Any : ValType.AnyNN,
                    };
                    return val;
                }
            }
            // Opaque external reference → anyref
            val.Type = nullable ? ValType.Any : ValType.AnyNN;
            return val;
        }

        public static object ArrayNewFixed(Value[] values, ThinContext ctx, int typeIdx)
        {
            // Create array instance and populate from values
            var gcType = ctx.Module?.Types[(TypeIdx)typeIdx];
            // For now, return a simple wrapper
            var arr = new object[values.Length];
            for (int i = 0; i < values.Length; i++)
                arr[i] = values[i].GcRef!;
            return arr;
        }

        public static object ArrayNewData(ThinContext ctx, int typeIdx, int dataIdx, int offset, int length)
        {
            byte[]? segData = null;
            if (ctx.Store != null && ctx.Module != null)
            {
                var dataAddr = ctx.Module.DataAddrs[(DataIdx)dataIdx];
                segData = ctx.Store[dataAddr].Data;
            }
            else
            {
                int segId = ctx.DataSegmentBaseId + dataIdx;
                segData = ModuleInit.GetDataSegmentData(segId);
            }
            if (segData == null)
                throw new TrapException("data segment not found");

            // Determine element type and size from the GC type registry
            var clrType = ctx.InitDataId >= 0 ? GcTypeRegistry.Get(ctx.InitDataId, typeIdx) : null;
            Type elemType = typeof(byte);
            if (clrType != null)
            {
                var elemField = clrType.GetField("elements");
                if (elemField != null)
                    elemType = elemField.FieldType.GetElementType() ?? typeof(byte);
            }
            int elemSize = elemType == typeof(byte) || elemType == typeof(sbyte) ? 1
                : elemType == typeof(short) || elemType == typeof(ushort) ? 2
                : elemType == typeof(int) || elemType == typeof(float) ? 4
                : elemType == typeof(long) || elemType == typeof(double) ? 8
                : 1;

            long srcBytes = (long)(uint)offset + (long)(uint)length * elemSize;
            if (srcBytes > segData.Length)
                throw new TrapException("out of bounds memory access");

            var result = System.Array.CreateInstance(elemType, length);
            if (length > 0)
                System.Buffer.BlockCopy(segData, offset, result, 0, length * elemSize);
            return result;
        }

        public static object ArrayNewElem(ThinContext ctx, int typeIdx, int elemIdx, int offset, int length)
        {
            // Determine the CLR element type for the array
            Type elemClrType = typeof(object);
            var clrType = ctx.InitDataId >= 0 ? GcTypeRegistry.Get(ctx.InitDataId, typeIdx) : null;
            if (clrType != null)
            {
                var elemField = clrType.GetField("elements");
                if (elemField != null)
                    elemClrType = elemField.FieldType.GetElementType() ?? typeof(object);
            }

            if (ctx.Store != null && ctx.Module != null)
            {
                var elemAddr = ctx.Module.ElemAddrs[(ElemIdx)elemIdx];
                var elem = ctx.Store[elemAddr];
                if ((long)(uint)offset + (long)(uint)length > elem.Elements.Count)
                    throw new TrapException("out of bounds table access");
                var result = System.Array.CreateInstance(elemClrType, length);
                for (int i = 0; i < length; i++)
                {
                    var v = elem.Elements[offset + i];
                    result.SetValue(ExtractElemValue(v, elemClrType), i);
                }
                return result;
            }
            int segId = ctx.ElemSegmentBaseId + elemIdx;
            var values = ModuleInit.GetElemSegment(segId);
            if (values == null)
                throw new TrapException("element segment not found");
            if ((long)(uint)offset + (long)(uint)length > values.Length)
                throw new TrapException("out of bounds table access");
            var result2 = System.Array.CreateInstance(elemClrType, length);
            for (int i = 0; i < length; i++)
            {
                result2.SetValue(ExtractElemValue(values[offset + i], elemClrType), i);
            }
            return result2;
        }

        /// <summary>Extract a Value into the appropriate CLR element for array storage.</summary>
        private static object? ExtractElemValue(Value val, Type elemClrType)
        {
            if (elemClrType.IsValueType) return ConvertValueToElement(val, elemClrType);
            // Reference type: unwrap to CLR object
            if (val.IsNullRef) return null;
            var gcRef = val.GcRef;
            object? target = gcRef is GcObjectAdapter adapter ? adapter.Target : gcRef;

            // If the target is already the right CLR type, use it directly
            if (target != null && elemClrType.IsInstanceOfType(target))
                return target;

            // Convert interpreter GC types to emitted CLR types
            if (target is Wacs.Core.Runtime.StoreArray sa)
                return ConvertStoreArray(sa, elemClrType);
            if (target is Wacs.Core.Runtime.GC.StoreStruct ss)
                return ConvertStoreStruct(ss, elemClrType);

            // Funcref/externref: no GcRef, box the entire Value as-is
            if (target == null) return (object)val;
            return target;
        }

        /// <summary>Convert an interpreter StoreArray to an emitted CLR array type.</summary>
        private static object? ConvertStoreArray(Wacs.Core.Runtime.StoreArray sa, Type targetClrType)
        {
            // Create an instance of the emitted CLR type and copy element data
            var instance = System.Activator.CreateInstance(targetClrType);
            if (instance == null) return null;

            var elemField = targetClrType.GetField("elements");
            var lenField = targetClrType.GetField("length");
            if (elemField == null) return instance;

            int len = sa.Length;
            var elemType = elemField.FieldType.GetElementType()!;
            var arr = System.Array.CreateInstance(elemType, len);

            // Copy data from StoreArray's Value[] to the typed CLR array
            for (int i = 0; i < len; i++)
            {
                var v = sa[i];
                if (elemType.IsValueType)
                    arr.SetValue(ConvertValueToElement(v, elemType), i);
                else if (!v.IsNullRef)
                {
                    var inner = v.GcRef is GcObjectAdapter a ? a.Target : v.GcRef;
                    if (inner is Wacs.Core.Runtime.StoreArray innerSa)
                        arr.SetValue(ConvertStoreArray(innerSa, elemType), i);
                    else
                        arr.SetValue(inner, i);
                }
            }

            elemField.SetValue(instance, arr);
            lenField?.SetValue(instance, len);
            return instance;
        }

        /// <summary>Convert an interpreter StoreStruct to an emitted CLR struct type.</summary>
        private static object? ConvertStoreStruct(Wacs.Core.Runtime.GC.StoreStruct ss, Type targetClrType)
        {
            var instance = System.Activator.CreateInstance(targetClrType);
            if (instance == null) return null;

            var fields = targetClrType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                Value v;
                try { v = ss[(FieldIdx)(uint)i]; }
                catch { break; }
                var ft = fields[i].FieldType;
                if (ft.IsValueType)
                    fields[i].SetValue(instance, ConvertValueToElement(v, ft));
                else if (!v.IsNullRef && v.GcRef != null)
                {
                    var inner = v.GcRef is GcObjectAdapter a ? a.Target : v.GcRef;
                    fields[i].SetValue(instance, inner);
                }
            }
            return instance;
        }

        public static void ArrayFill(Value arrayRef, int offset, Value value, int length)
        {
            var target = UnwrapArrayRef(arrayRef);
            var elementsField = target.GetType().GetField("elements");
            if (elementsField == null) throw new TrapException("not an array type");
            var elements = (System.Array)elementsField.GetValue(target)!;
            if (offset + length > elements.Length)
                throw new TrapException("out of bounds array access");
            var elemType = elements.GetType().GetElementType()!;
            var fillVal = ConvertValueToElement(value, elemType);
            for (int i = 0; i < length; i++)
                elements.SetValue(fillVal, offset + i);
        }

        /// <summary>Convert a WASM Value to the CLR element type for array storage.</summary>
        private static object ConvertValueToElement(Value val, Type elemType)
        {
            if (elemType == typeof(byte) || elemType == typeof(sbyte))
                return Convert.ChangeType(val.Data.Int32 & 0xFF, elemType);
            if (elemType == typeof(short) || elemType == typeof(ushort))
                return Convert.ChangeType(val.Data.Int32 & 0xFFFF, elemType);
            if (elemType == typeof(int)) return val.Data.Int32;
            if (elemType == typeof(long)) return val.Data.Int64;
            if (elemType == typeof(float)) return val.Data.Float32;
            if (elemType == typeof(double)) return val.Data.Float64;
            // Reference-typed elements: unwrap to CLR object
            if (val.GcRef != null)
            {
                if (val.GcRef is GcObjectAdapter adapter) return adapter.Target;
                return val.GcRef;
            }
            return val; // fallback
        }

        public static void ArrayCopy(object dstRef, int dstOff, object srcRef, int srcOff, int length)
        {
            if (IsNullRef(dstRef) || IsNullRef(srcRef)) throw new TrapException("null array reference");
            // Unwrap GcObjectAdapter if present
            var dstTarget = dstRef is GcObjectAdapter da ? da.Target : dstRef;
            var srcTarget = srcRef is GcObjectAdapter sa ? sa.Target : srcRef;
            var dstField = dstTarget.GetType().GetField("elements");
            var srcField = srcTarget.GetType().GetField("elements");
            if (dstField == null || srcField == null) throw new TrapException("not an array type");
            var dst = (System.Array)dstField.GetValue(dstTarget)!;
            var src = (System.Array)srcField.GetValue(srcTarget)!;
            if (length == 0) return;
            System.Array.Copy(src, srcOff, dst, dstOff, length);
        }

        /// <summary>
        /// Array copy taking Value structs directly to avoid boxing null refs
        /// into invalid CLR object references.
        /// </summary>
        public static void ArrayCopyValues(Value dstRef, int dstOff, Value srcRef, int srcOff, int length)
        {
            // Null check must happen before any early exit (spec requires trap)
            var dst = UnwrapArrayRef(dstRef);
            var src = UnwrapArrayRef(srcRef);
            var dstField = dst.GetType().GetField("elements");
            var srcField = src.GetType().GetField("elements");
            if (dstField == null || srcField == null) throw new TrapException("not an array type");
            var dstArr = (System.Array)dstField.GetValue(dst)!;
            var srcArr = (System.Array)srcField.GetValue(src)!;
            // Bounds check: offset+length must be in bounds even for length=0
            if (dstOff + length > dstArr.Length || srcOff + length > srcArr.Length)
                throw new TrapException("out of bounds array access");
            if (length == 0) return;
            System.Array.Copy(srcArr, srcOff, dstArr, dstOff, length);
        }

        public static void ArrayInitData(ThinContext ctx, Value arrayRef, int dstOff,
            int dataIdx, int srcOff, int length)
        {
            var target = UnwrapArrayRef(arrayRef);
            byte[]? segData = null;
            if (ctx.Store != null && ctx.Module != null)
            {
                var dataAddr = ctx.Module.DataAddrs[(DataIdx)dataIdx];
                segData = ctx.Store[dataAddr].Data;
            }
            else
            {
                int segId = ctx.DataSegmentBaseId + dataIdx;
                segData = ModuleInit.GetDataSegmentData(segId);
            }
            if (segData == null)
                throw new TrapException("data segment not found");
            var field = target.GetType().GetField("elements");
            if (field == null) throw new TrapException("not an array type");
            var elements = (System.Array)field.GetValue(target)!;
            var elemType = elements.GetType().GetElementType()!;
            int elemSize = System.Runtime.InteropServices.Marshal.SizeOf(elemType);
            long srcBytes = (long)(uint)srcOff + (long)(uint)length * elemSize;
            if ((long)(uint)dstOff + (long)(uint)length > elements.Length || srcBytes > segData.Length)
                throw new TrapException("out of bounds memory access");
            for (int i = 0; i < length; i++)
            {
                int byteOff = srcOff + i * elemSize;
                object val = elemSize switch
                {
                    1 => segData[byteOff],
                    2 => BitConverter.ToInt16(segData, byteOff),
                    4 => BitConverter.ToInt32(segData, byteOff),
                    8 => BitConverter.ToInt64(segData, byteOff),
                    _ => segData[byteOff]
                };
                elements.SetValue(Convert.ChangeType(val, elemType), dstOff + i);
            }
        }

        public static void ArrayInitElem(ThinContext ctx, Value arrayRef, int dstOff,
            int elemIdx, int srcOff, int length)
        {
            var target = UnwrapArrayRef(arrayRef);
            Value[]? values = null;
            if (ctx.Store != null && ctx.Module != null)
            {
                var elemAddr = ctx.Module.ElemAddrs[(ElemIdx)elemIdx];
                var elem = ctx.Store[elemAddr];
                values = elem.Elements.ToArray();
            }
            else
            {
                int segId = ctx.ElemSegmentBaseId + elemIdx;
                values = ModuleInit.GetElemSegment(segId);
            }
            if (values == null)
                throw new TrapException("element segment not found");
            var field = target.GetType().GetField("elements");
            if (field == null) throw new TrapException("not an array type");
            var elements = (System.Array)field.GetValue(target)!;
            var elemType = elements.GetType().GetElementType()!;
            if ((long)(uint)dstOff + (long)(uint)length > elements.Length
                || (long)(uint)srcOff + (long)(uint)length > values.Length)
                throw new TrapException("out of bounds table access");
            for (int i = 0; i < length; i++)
            {
                elements.SetValue(ExtractElemValue(values[srcOff + i], elemType), dstOff + i);
            }
        }
    }
}
