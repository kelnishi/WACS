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
using System.Linq;
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
            // GC ref types flow as `object` on the internal CIL stack (doc 2 §1).
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
                    cv?.Push(typeof(object));
                    break;
                }
                case GcCode.StructNewDefault:
                    EmitStructNewDefault(il, (InstStructNewDefault)inst, gcTypes);
                    cv?.Push(typeof(object));
                    break;
                case GcCode.StructGet:
                case GcCode.StructGetS:
                case GcCode.StructGetU:
                {
                    cv?.Pop(typeof(object), "struct.get ref");
                    EmitStructGet(il, (InstStructGet)inst, gcTypes);
                    var sgi = (InstStructGet)inst;
                    var gt = gcTypes.GetGcType(sgi.TypeIndex);
                    var ft = gt?.Fields[sgi.FieldIndex].FieldType;
                    // Ref-typed fields push object; scalar fields push their CLR type.
                    cv?.Push(ft != null && IsScalarType(ft) ? ft : typeof(object));
                    break;
                }
                case GcCode.StructSet:
                {
                    var ssi = (InstStructSet)inst;
                    cv?.Pop(context: "struct.set val");
                    cv?.Pop(typeof(object), "struct.set ref");
                    EmitStructSet(il, ssi, gcTypes);
                    break;
                }

                // === Array ops ===
                case GcCode.ArrayNew:
                    cv?.Pop(typeof(int), "array.new len");
                    cv?.Pop(context: "array.new init");
                    EmitArrayNew(il, (InstArrayNew)inst, gcTypes, moduleInst);
                    cv?.Push(typeof(object));
                    break;
                case GcCode.ArrayNewDefault:
                    cv?.Pop(typeof(int), "array.new_default len");
                    EmitArrayNewDefault(il, (InstArrayNewDefault)inst, gcTypes, moduleInst);
                    cv?.Push(typeof(object));
                    break;
                case GcCode.ArrayGet:
                case GcCode.ArrayGetS:
                case GcCode.ArrayGetU:
                {
                    cv?.Pop(typeof(int), "array.get idx");
                    cv?.Pop(typeof(object), "array.get ref");
                    EmitArrayGet(il, (InstArrayGet)inst, gcTypes, moduleInst);
                    var agi = (InstArrayGet)inst;
                    var agt = gcTypes.GetGcType(agi.TypeIndex);
                    var eft = agt?.Fields.Length > 0 ? agt.Fields[0].FieldType.GetElementType() : null;
                    cv?.Push(eft != null && IsScalarType(eft) ? eft : typeof(object));
                    break;
                }
                case GcCode.ArraySet:
                    cv?.Pop(context: "array.set val");
                    cv?.Pop(typeof(int), "array.set idx");
                    cv?.Pop(typeof(object), "array.set ref");
                    EmitArraySet(il, (InstArraySet)inst, gcTypes, moduleInst);
                    break;
                case GcCode.ArrayLen:
                    cv?.Pop(typeof(object), "array.len ref");
                    EmitArrayLen(il);
                    cv?.Push(typeof(int));
                    break;
                case GcCode.ArrayNewFixed:
                {
                    var anfi = (InstArrayNewFixed)inst;
                    cv?.Pop(anfi.FixedCount, "array.new_fixed elems");
                    EmitArrayNewFixed(il, anfi, gcTypes, moduleInst);
                    cv?.Push(typeof(object));
                    break;
                }
                case GcCode.ArrayNewData:
                    cv?.Pop(typeof(int), "array.new_data len");
                    cv?.Pop(typeof(int), "array.new_data offset");
                    EmitArrayNewData(il, (InstArrayNewData)inst, gcTypes);
                    cv?.Push(typeof(object));
                    break;
                case GcCode.ArrayNewElem:
                    cv?.Pop(typeof(int), "array.new_elem len");
                    cv?.Pop(typeof(int), "array.new_elem offset");
                    EmitArrayNewElem(il, (InstArrayNewElem)inst, gcTypes);
                    cv?.Push(typeof(object));
                    break;
                case GcCode.ArrayFill:
                    cv?.Pop(typeof(int), "array.fill len");
                    cv?.Pop(context: "array.fill val");
                    cv?.Pop(typeof(int), "array.fill offset");
                    cv?.Pop(typeof(object), "array.fill ref");
                    EmitArrayFill(il, (InstArrayFill)inst, gcTypes);
                    break;
                case GcCode.ArrayCopy:
                    cv?.Pop(typeof(int), "array.copy len");
                    cv?.Pop(typeof(int), "array.copy src_off");
                    cv?.Pop(typeof(object), "array.copy src_ref");
                    cv?.Pop(typeof(int), "array.copy dst_off");
                    cv?.Pop(typeof(object), "array.copy dst_ref");
                    EmitArrayCopy(il, (InstArrayCopy)inst, gcTypes);
                    break;
                case GcCode.ArrayInitData:
                    cv?.Pop(typeof(int), "array.init_data len");
                    cv?.Pop(typeof(int), "array.init_data src_off");
                    cv?.Pop(typeof(int), "array.init_data dst_off");
                    cv?.Pop(typeof(object), "array.init_data ref");
                    EmitArrayInitData(il, (InstArrayInitData)inst, gcTypes);
                    break;
                case GcCode.ArrayInitElem:
                    cv?.Pop(typeof(int), "array.init_elem len");
                    cv?.Pop(typeof(int), "array.init_elem src_off");
                    cv?.Pop(typeof(int), "array.init_elem dst_off");
                    cv?.Pop(typeof(object), "array.init_elem ref");
                    EmitArrayInitElem(il, (InstArrayInitElem)inst, gcTypes);
                    break;

                // === ref.test / ref.cast ===
                // Dispatch on operand representation (doc 2 §1): GC refs are
                // `object` on the internal stack, funcref/externref are `Value`.
                // Both representations need to reach the RefTestValue /
                // RefTestObject helpers with a matching argument type.
                case GcCode.RefTest:
                case GcCode.RefTestNull:
                {
                    var topType = cv?.Peek() ?? typeof(object);
                    bool isObject = topType == typeof(object);
                    cv?.Pop(isObject ? typeof(object) : typeof(Value), "ref.test val");
                    EmitRefTest(il, (InstRefTest)inst, isObject);
                    cv?.Push(typeof(int));
                    break;
                }
                case GcCode.RefCast:
                case GcCode.RefCastNull:
                {
                    var topType = cv?.Peek() ?? typeof(object);
                    bool isObject = topType == typeof(object);
                    cv?.Pop(isObject ? typeof(object) : typeof(Value), "ref.cast val");
                    EmitRefCast(il, (InstRefCast)inst, isObject);
                    cv?.Push(isObject ? typeof(object) : typeof(Value));
                    break;
                }

                // === br_on_cast === (unreachable here — FunctionCodegen intercepts)
                case GcCode.BrOnCast:
                    EmitBrOnCast(il, (InstBrOnCast)inst, branchTarget!, castFail: false);
                    break;
                case GcCode.BrOnCastFail:
                    EmitBrOnCast(il, (InstBrOnCastFail)inst, branchTarget!, castFail: true);
                    break;

                // === Conversions ===
                // `extern.convert_any` changes an anyref's type label to externref
                // (doc 1 §11.11). Crosses representations: anyref (object on internal
                // stack) → externref (Value). Wrap with ExternRef type tag.
                case GcCode.ExternConvertAny:
                    cv?.Pop(typeof(object), "extern.convert_any");
                    il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                        nameof(GcRuntimeHelpers.ExternConvertAnyWrap), BindingFlags.Public | BindingFlags.Static)!);
                    cv?.Push(typeof(Value));
                    break;
                // `any.convert_extern` is the reverse: externref (Value) → anyref
                // (object). Uses a dedicated unwrap so host externrefs (Value
                // with Data.Ptr but no GcRef) preserve their address via
                // HostExternRef rather than collapsing to null.
                case GcCode.AnyConvertExtern:
                    cv?.Pop(typeof(Value), "any.convert_extern");
                    il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                        nameof(GcRuntimeHelpers.AnyConvertExternUnwrap), BindingFlags.Public | BindingFlags.Static)!);
                    cv?.Push(typeof(object));
                    break;

                // === i31 ===
                case GcCode.RefI31:
                    cv?.Pop(typeof(int), "ref.i31");
                    EmitRefI31(il);
                    cv?.Push(typeof(object));
                    break;
                case GcCode.I31GetS:
                    cv?.Pop(typeof(object), "i31.get_s");
                    EmitI31Get(il, signed: true);
                    cv?.Push(typeof(int));
                    break;
                case GcCode.I31GetU:
                    cv?.Pop(typeof(object), "i31.get_u");
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

            // Spill fields from stack (reverse order — last field is on top).
            // Ref-typed fields: the stack has object (internal CIL stack
            // convention). Declare the temp as object and Castclass on reload.
            // Scalar / Value fields: declare with the field's CLR type directly.
            var temps = new LocalBuilder[fieldCount];
            var needsCast = new bool[fieldCount];
            for (int i = fieldCount - 1; i >= 0; i--)
            {
                var ft = gcType.Fields[i].FieldType;
                bool gcRef = !IsScalarType(ft) && ft != typeof(Value);
                temps[i] = il.DeclareLocal(gcRef ? typeof(object) : ft);
                needsCast[i] = gcRef;
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Create instance
            il.Emit(OpCodes.Newobj, gcType.ClrType.GetConstructor(Type.EmptyTypes)!);

            // Store fields
            for (int i = 0; i < fieldCount; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldloc, temps[i]);
                if (needsCast[i])
                    il.Emit(OpCodes.Castclass, gcType.Fields[i].FieldType);
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

            // Stack: [structref (object)]
            EmitNullGuard(il, "null structure reference");
            EmitUnwrapGcRef(il, gcType.ClrType);
            var fieldType = gcType.Fields[inst.FieldIndex].FieldType;
            il.Emit(OpCodes.Ldfld, gcType.Fields[inst.FieldIndex]);

            // Packed-field extension. CIL Ldfld on byte leaves a zero-extended
            // int; Ldfld on short leaves a sign-extended int. struct.get_s /
            // struct.get_u override that with the WASM-requested extension.
            if (inst.SignExtension == PackedExt.Signed)
            {
                if (fieldType == typeof(byte))
                {
                    il.Emit(OpCodes.Conv_I1);
                    il.Emit(OpCodes.Conv_I4);
                }
                else if (fieldType == typeof(short))
                {
                    il.Emit(OpCodes.Conv_I2);
                    il.Emit(OpCodes.Conv_I4);
                }
            }
            else if (inst.SignExtension == PackedExt.Unsigned)
            {
                if (fieldType == typeof(short))
                    il.Emit(OpCodes.Conv_U2);
                // byte field is already zero-extended by Ldfld — nothing to do.
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

            // Stack: [structref (object), value]. Ref-typed values arrive as
            // object on the internal stack — spill to object and Castclass
            // on reload so Stfld matches the field's declared type.
            var ft = gcType.Fields[inst.FieldIndex].FieldType;
            bool gcRefField = !IsScalarType(ft) && ft != typeof(Value);
            var valLocal = il.DeclareLocal(gcRefField ? typeof(object) : ft);
            il.Emit(OpCodes.Stloc, valLocal);
            EmitNullGuard(il, "null structure reference");
            EmitUnwrapGcRef(il, gcType.ClrType);
            il.Emit(OpCodes.Ldloc, valLocal);
            if (gcRefField)
                il.Emit(OpCodes.Castclass, ft);
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
            var elemClrType = gcType.Fields[0].FieldType.GetElementType()!;
            bool gcRefElem = !IsScalarType(elemClrType) && elemClrType != typeof(Value);

            var lenLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, lenLocal);
            // init value on stack is object for GC-ref elements; spill as object
            // and Castclass on reload.
            var initLocal = il.DeclareLocal(gcRefElem ? typeof(object) : elemClrType);
            il.Emit(OpCodes.Stloc, initLocal);

            // Create instance
            il.Emit(OpCodes.Newobj, gcType.ClrType.GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Dup);

            // arr.elements = new T[length]
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Newarr, elemClrType);
            il.Emit(OpCodes.Stfld, gcType.Fields[0]); // elements

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Stfld, gcType.Fields[1]); // length

            // TODO: fill with init value

            // Internal stack convention: GC-ref arrays remain as object
            // (doc 2 §1). EmitWrapGcRef is a no-op per doc 2 §1.
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

            // Stack: [arrayref (object), index]
            var idxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, idxLocal);

            // Null-guard (doc 1 §11.6: array.get traps on null). Then cast to
            // the typed emitted CLR class so Ldfld / Ldelem have the right type.
            EmitNullGuard(il, "null array reference");
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

            // Stack: [arrayref (object), index, value]. Ref-typed value arrives
            // as object — spill and Castclass on reload.
            var elemClrType = gcType.Fields[0].FieldType.GetElementType()!;
            bool gcRefElem = !IsScalarType(elemClrType) && elemClrType != typeof(Value);
            var valLocal = il.DeclareLocal(gcRefElem ? typeof(object) : elemClrType);
            il.Emit(OpCodes.Stloc, valLocal);
            var idxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, idxLocal);
            EmitNullGuard(il, "null array reference");
            EmitUnwrapGcRef(il, gcType.ClrType);
            il.Emit(OpCodes.Ldfld, gcType.Fields[0]); // elements array
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldloc, valLocal);
            if (gcRefElem)
                il.Emit(OpCodes.Castclass, elemClrType);
            il.Emit(OpCodes.Stelem, elemClrType);
        }

        // array.len: pop [arrayref (object)], push length
        private static void EmitArrayLen(ILGenerator il)
        {
            // The ref is an object on the internal stack; helper reads `length`.
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.ArrayLenObject), BindingFlags.Public | BindingFlags.Static)!);
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

            // Spill N values from CIL stack. Scalar elements use their CLR
            // type directly; Value-typed elements (funcref/externref/v128) use
            // Value; GC-ref elements arrive as object on the stack, so spill
            // to object and Castclass on reload.
            bool scalar = IsScalarType(elemClrType);
            bool gcRef = !scalar && elemClrType != typeof(Value);
            Type tempType = scalar ? elemClrType : (gcRef ? typeof(object) : typeof(Value));
            var temps = new LocalBuilder[n];
            for (int i = n - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(tempType);
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
                if (gcRef)
                    il.Emit(OpCodes.Castclass, elemClrType);
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
            // Stack: [arrayref (object), offset, val, len]
            var len = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, len);
            var val = il.DeclareLocal(typeof(Value)); il.Emit(OpCodes.Stloc, val);
            var off = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, off);
            var arr = il.DeclareLocal(typeof(object)); il.Emit(OpCodes.Stloc, arr);
            il.Emit(OpCodes.Ldloc, arr);
            il.Emit(OpCodes.Ldloc, off);
            il.Emit(OpCodes.Ldloc, val);
            il.Emit(OpCodes.Ldloc, len);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.ArrayFillObject), BindingFlags.Public | BindingFlags.Static)!);
        }

        private static void EmitArrayCopy(ILGenerator il, InstArrayCopy inst, GcTypeEmitter gcTypes)
        {
            // Stack: [dstref (object), dstoff, srcref (object), srcoff, len]
            var len = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, len);
            var srcoff = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, srcoff);
            var srcref = il.DeclareLocal(typeof(object)); il.Emit(OpCodes.Stloc, srcref);
            var dstoff = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, dstoff);
            var dstref = il.DeclareLocal(typeof(object)); il.Emit(OpCodes.Stloc, dstref);
            il.Emit(OpCodes.Ldloc, dstref);
            il.Emit(OpCodes.Ldloc, dstoff);
            il.Emit(OpCodes.Ldloc, srcref);
            il.Emit(OpCodes.Ldloc, srcoff);
            il.Emit(OpCodes.Ldloc, len);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.ArrayCopyObject), BindingFlags.Public | BindingFlags.Static)!);
        }

        private static void EmitArrayInitData(ILGenerator il, InstArrayInitData inst, GcTypeEmitter gcTypes)
        {
            // Stack: [arrayref (object), dstoff, srcoff, len]
            var len = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, len);
            var srcoff = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, srcoff);
            var dstoff = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, dstoff);
            var arr = il.DeclareLocal(typeof(object)); il.Emit(OpCodes.Stloc, arr);
            il.Emit(OpCodes.Ldarg_0); // ctx
            il.Emit(OpCodes.Ldloc, arr);
            il.Emit(OpCodes.Ldloc, dstoff);
            il.Emit(OpCodes.Ldc_I4, inst.DataIndex);
            il.Emit(OpCodes.Ldloc, srcoff);
            il.Emit(OpCodes.Ldloc, len);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.ArrayInitDataObject), BindingFlags.Public | BindingFlags.Static)!);
        }

        private static void EmitArrayInitElem(ILGenerator il, InstArrayInitElem inst, GcTypeEmitter gcTypes)
        {
            var len = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, len);
            var srcoff = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, srcoff);
            var dstoff = il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Stloc, dstoff);
            var arr = il.DeclareLocal(typeof(object)); il.Emit(OpCodes.Stloc, arr);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, arr);
            il.Emit(OpCodes.Ldloc, dstoff);
            il.Emit(OpCodes.Ldc_I4, inst.ElemIndex);
            il.Emit(OpCodes.Ldloc, srcoff);
            il.Emit(OpCodes.Ldloc, len);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.ArrayInitElemObject), BindingFlags.Public | BindingFlags.Static)!);
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

        // br_on_cast / br_on_cast_fail — this path is unreachable because
        // FunctionCodegen intercepts InstBrOnCast/InstBrOnCastFail before
        // GcEmitter.Emit is called. Kept for completeness; the live path is
        // FunctionCodegen.EmitBrOnCastWithExcess.
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

            // Stack: [ref (object)]. Save, test, conditionally branch with ref restored.
            var refLocal = il.DeclareLocal(typeof(object));
            il.Emit(OpCodes.Stloc, refLocal);
            il.Emit(OpCodes.Ldloc, refLocal);
            il.Emit(OpCodes.Ldc_I4, (int)targetType);
            il.Emit(OpCodes.Ldc_I4, targetType.IsNullable() ? 1 : 0);
            il.Emit(OpCodes.Ldarg_0); // ThinContext
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.RefTestObject), BindingFlags.Public | BindingFlags.Static)!);

            var skipLabel = il.DefineLabel();
            if (castFail)
                il.Emit(OpCodes.Brtrue, skipLabel); // test passed → don't branch
            else
                il.Emit(OpCodes.Brfalse, skipLabel); // test failed → don't branch
            il.Emit(OpCodes.Ldloc, refLocal);
            il.Emit(OpCodes.Br, branchTarget(label));
            il.MarkLabel(skipLabel);
            il.Emit(OpCodes.Ldloc, refLocal);
        }


        private static void EmitRefTest(ILGenerator il, InstRefTest inst, bool isObject)
        {
            // Stack: [ref] (object for GC refs, Value for funcref/externref).
            il.Emit(OpCodes.Ldc_I4, (int)inst.HeapType);
            il.Emit(OpCodes.Ldc_I4, inst.IsNullable ? 1 : 0);
            il.Emit(OpCodes.Ldarg_0); // ThinContext for type lookup
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                isObject
                    ? nameof(GcRuntimeHelpers.RefTestObject)
                    : nameof(GcRuntimeHelpers.RefTestValue),
                BindingFlags.Public | BindingFlags.Static)!);
        }

        // ref.cast: pop [ref], push [ref] (or trap).
        // Operand representation preserves (object→object, Value→Value).
        private static void EmitRefCast(ILGenerator il, InstRefCast inst, bool isObject)
        {
            il.Emit(OpCodes.Ldc_I4, (int)inst.HeapType);
            il.Emit(OpCodes.Ldc_I4, inst.IsNullable ? 1 : 0);
            il.Emit(OpCodes.Ldarg_0); // ThinContext for type lookup
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                isObject
                    ? nameof(GcRuntimeHelpers.RefCastObject)
                    : nameof(GcRuntimeHelpers.RefCastValue),
                BindingFlags.Public | BindingFlags.Static)!);
        }

        // ref.i31: pop [i32], push [i31ref (object — boxed I31Ref)]
        private static void EmitRefI31(ILGenerator il)
        {
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.RefI31Object), BindingFlags.Public | BindingFlags.Static)!);
        }

        // i31.get_s / i31.get_u: pop [i31ref (object)], push [i32]
        private static void EmitI31Get(ILGenerator il, bool signed)
        {
            il.Emit(OpCodes.Ldc_I4, signed ? 1 : 0);
            il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                nameof(GcRuntimeHelpers.I31GetObject), BindingFlags.Public | BindingFlags.Static)!);
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
        /// Emit: if (top of stack is null) throw TrapException(message); else
        /// leave the top intact. Used before struct.get / array.get / etc.
        /// to convert a would-be NullReferenceException into a WASM trap
        /// per doc 1 §11.6 / §11.7 (struct.get and array.get trap on null).
        /// Expects `typeof(object)` (or assignment-compatible) on the stack.
        /// </summary>
        private static void EmitNullGuard(ILGenerator il, string message)
        {
            var ok = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, ok);
            il.Emit(OpCodes.Ldstr, message);
            il.Emit(OpCodes.Newobj,
                typeof(TrapException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(ok);
        }

        /// <summary>
        /// GC refs flow as `object` on the internal CIL stack (doc 2 §1).
        /// This is a no-op — newly constructed objects stay on the stack as-is.
        /// Retained as a documentation hook at call sites.
        /// </summary>
        private static void EmitWrapGcRef(ILGenerator il, int typeIndex)
        {
        }

        /// <summary>
        /// Cast the object on the stack to the typed CLR GC class reference.
        /// GC refs already flow as object (doc 2 §1); no Value→object unwrap.
        /// `Castclass` passes null through unchanged, matching WASM null-ref
        /// semantics for operations that tolerate null (cast to nullable).
        /// </summary>
        private static void EmitUnwrapGcRef(ILGenerator il, Type targetClrType)
        {
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
            // HostExternRef carries its address in Data.Ptr, not via IGcRef
            // store-index; preserve it explicitly.
            if (gcRef is HostExternRef host)
                return new Value(valType, host.Address, host);
            if (gcRef is IGcRef igc)
                return new Value(valType, 0, igc);
            return new Value(valType, 0, new GcObjectAdapter(gcRef));
        }

        /// <summary>
        /// WrapRef variant that tags the Value with an explicit target type
        /// rather than deriving from the object's runtime class. Used at
        /// function-return / global-set / table-set boundaries where the
        /// declared WASM type governs the Value.Type field (a `ref.extern`
        /// passed through `any.convert_extern` lands in an anyref-typed
        /// context — the Value must carry ValType.Any, not ExternRef).
        /// </summary>
        public static Value WrapRefAs(object? gcRef, int targetType)
        {
            var vt = (ValType)targetType;
            if (gcRef == null) return new Value(vt);
            if (gcRef is HostExternRef host)
                return new Value(vt, host.Address, host);
            if (gcRef is IGcRef igc)
                return new Value(vt, 0, igc);
            return new Value(vt, 0, new GcObjectAdapter(gcRef));
        }

        private static ValType DeriveValType(object gcRef)
        {
            // Core-type refs carry their own WASM type identity.
            if (gcRef is I31Ref) return ValType.I31;
            if (gcRef is HostExternRef) return ValType.ExternRef;
            if (gcRef is VecRef) return ValType.V128;

            // Emitted GC types (struct/array) expose a static TypeCategory
            // field: 0 = struct, 1 = array.
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
        internal class GcObjectAdapter : IGcRef
        {
            public readonly object Target;
            public GcObjectAdapter(object target) => Target = target;
            public RefIdx StoreIndex => default(PtrIdx);
        }

        /// <summary>
        /// Unwrap a Value (the storage / signature form) to the underlying CLR
        /// object that sits on the internal CIL stack (doc 2 §3). Null Value
        /// maps to null object — the transpiler emits explicit trap instructions
        /// (ref.as_non_null, struct.get, array.get, call_ref, throw_ref, …)
        /// where a null ref is illegal; this helper is a pure boundary
        /// conversion and MUST NOT trap on its own.
        /// </summary>
        public static object? UnwrapRef(Value val)
        {
            if (val.IsNullRef) return null;
            var gcRef = val.GcRef;
            if (gcRef != null)
            {
                if (gcRef is GcObjectAdapter adapter) return adapter.Target;
                return gcRef;
            }
            // No GcRef but non-null ref (Data.Ptr carries the address, e.g.
            // host ref from `ref.extern N` / `ref.host N`). Intern a
            // HostExternRef so the address survives round-trip through
            // the object-side of the boundary and ref.eq identity holds.
            return HostExternRef.Intern(val.Data.Ptr);
        }

        /// <summary>
        /// Test if a Value matches a heap type. Layer 0: CLR inheritance + abstract types.
        /// heapType encoding: negative = abstract (HeapType enum), non-negative = concrete type index.
        /// Retained because <see cref="RefTestObject"/> dispatches through a
        /// synthetic Value, and <see cref="RefCastValue"/> / br_on_cast's Value
        /// path call this directly (doc 2 §1: funcref/externref stay as Value).
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
            // Function type check: funcref values use Data.Ptr, not GcRef
            if (val.Type == ValType.FuncRef)
                return TestFuncType(val, typeIndex, ctx);


            // GC struct/array type check
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
            int? targetHash2 = GetStructuralHash(targetClrType);
            if (targetHash2 == null) return false;

            var checkType = target.GetType();
            while (checkType != null && checkType != typeof(object))
            {
                int? checkHash = GetStructuralHash(checkType);
                if (checkHash != null && checkHash.Value == targetHash2.Value)
                    return true;
                checkType = checkType.BaseType;
            }

            return false;
        }

        /// <summary>
        /// Test if a funcref's function type is a subtype of (or equal to)
        /// the declared type at <paramref name="typeIndex"/>. Uses the
        /// pre-computed supertype-hash chain when present so subtyping
        /// works without consulting the interpreter TypesSpace — falls
        /// back to direct hash equality via <see cref="ThinContext.FuncTypeHashes"/>
        /// when the chain hasn't been populated (older assemblies).
        /// </summary>
        private static bool TestFuncType(Value val, int typeIndex, ThinContext ctx)
        {
            if (ctx.FuncTypeHashes == null) return false;
            int funcIdx = (int)val.Data.Ptr;
            if (funcIdx < 0 || funcIdx >= ctx.FuncTypeHashes.Length)
                return false;

            // Resolve target type's hash. Prefer the module's own TypesSpace
            // when available (mixed-mode); otherwise fall back to the
            // transpile-time-baked TypeHashes / TypeIsFunc tables so the
            // standalone runtime path doesn't need the interpreter.
            int targetHash;
            if (ctx.Module?.Types != null && ctx.Module.Types.Contains((TypeIdx)typeIndex))
            {
                var targetDefType = ctx.Module.Types[(TypeIdx)typeIndex];
                if (!(targetDefType.Expansion is FunctionType)) return false;
                targetHash = targetDefType.GetHashCode();
            }
            else if (ctx.TypeHashes != null && typeIndex >= 0 && typeIndex < ctx.TypeHashes.Length)
            {
                if (ctx.TypeIsFunc != null && !ctx.TypeIsFunc[typeIndex]) return false;
                targetHash = ctx.TypeHashes[typeIndex];
            }
            else
            {
                return false;
            }

            // Subtype check: walk the supertype chain for this function.
            // If the chain isn't populated fall back to direct equality
            // (previous behaviour).
            var chain = ctx.FuncTypeSuperHashes?[funcIdx];
            if (chain != null)
            {
                for (int i = 0; i < chain.Length; i++)
                    if (chain[i] == targetHash) return true;
                return false;
            }
            return ctx.FuncTypeHashes[funcIdx] == targetHash;
        }

        private static int? GetStructuralHash(Type clrType)
        {
            var field = clrType.GetField("StructuralHash",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field != null)
                return (int)field.GetValue(null)!;
            return null;
        }

        /// <summary>
        /// Build a <see cref="Value"/> wrapping an i31 ref, for constant-expression
        /// evaluation at transpile / init time (doc 1 §2.4). The runtime
        /// ref.i31 instruction goes through <see cref="RefI31Object"/>.
        /// </summary>
        public static Value RefI31Value(int value)
        {
            int masked = value & 0x7FFFFFFF;
            return new Value(ValType.I31, masked, new I31Ref(masked));
        }

        /// <summary>
        /// ref.cast on a Value-typed ref (funcref / externref). Traps on cast
        /// failure per doc 1 §11.8. Nullable casts pass null through.
        /// </summary>
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

        // =========================================================================
        // Object-based helpers for the split-stack model (doc 1 §2.1, doc 2 §§1, 3).
        // GC refs flow as plain `object` on the internal CIL stack; these helpers
        // consume and produce `object` directly. The boundary helpers WrapRef /
        // UnwrapRef above are called only at signature / storage / call / exception
        // boundaries, not between GC ops.
        // =========================================================================

        /// <summary>Unwrap adapter to reach the underlying CLR object.</summary>
        private static object? Peel(object? obj)
            => obj is GcObjectAdapter a ? a.Target : obj;

        /// <summary>ref.test on an object-typed ref. See doc 1 §11.8.</summary>
        public static int RefTestObject(object? val, int heapType, int nullable, ThinContext ctx)
        {
            if (val == null) return nullable != 0 ? 1 : 0;
            // Build a synthetic Value so we can reuse the layered type-test logic
            // in RefTestValue (CLR inheritance → structural hash → abstract types).
            var v = WrapRef(val);
            return RefTestValue(v, heapType, nullable, ctx);
        }

        /// <summary>ref.cast on an object-typed ref. See doc 1 §11.8.</summary>
        public static object? RefCastObject(object? val, int heapType, int nullable, ThinContext ctx)
        {
            if (val == null)
            {
                if (nullable != 0) return null;
                throw new TrapException("cast failure: null reference");
            }
            if (RefTestObject(val, heapType, nullable, ctx) == 0)
                throw new TrapException("cast failure");
            return val;
        }

        /// <summary>ref.i31: box an int into an object. Uses I31Ref so ref.eq /
        /// cross-engine handoff preserve identity semantics.</summary>
        public static object RefI31Object(int value)
        {
            int masked = value & 0x7FFFFFFF;
            return new I31Ref(masked);
        }

        /// <summary>i31.get_s / i31.get_u on an object-typed i31 ref.</summary>
        public static int I31GetObject(object? i31Ref, int signed)
        {
            if (i31Ref == null)
                throw new TrapException("null i31 reference");
            var peeled = Peel(i31Ref);
            int val;
            if (peeled is I31Ref ir)
                val = ir.Value & 0x7FFFFFFF;
            else if (peeled is int boxed)
                val = boxed & 0x7FFFFFFF;
            else
                throw new TrapException("not an i31 reference");
            if (signed != 0 && (val & 0x40000000) != 0)
                val |= unchecked((int)0x80000000);
            return val;
        }

        /// <summary>array.len on an object-typed array ref.</summary>
        public static int ArrayLenObject(object? arrayRef)
        {
            if (arrayRef == null) throw new TrapException("null array reference");
            var target = Peel(arrayRef)!;
            var field = target.GetType().GetField("length");
            if (field == null)
            {
                // Interpreter array: fall back to StoreArray.Length.
                if (target is Wacs.Core.Runtime.StoreArray sa) return sa.Length;
                throw new TrapException("not an array type");
            }
            return (int)field.GetValue(target)!;
        }

        /// <summary>array.fill on an object-typed array ref; value remains a Value
        /// because WASM element types include ref-types plus packed / scalar kinds
        /// and the Value form handles all variants uniformly in one helper.</summary>
        public static void ArrayFillObject(object? arrayRef, int offset, Value value, int length)
        {
            if (arrayRef == null) throw new TrapException("null array reference");
            var target = Peel(arrayRef)!;
            var elementsField = target.GetType().GetField("elements");
            if (elementsField == null) throw new TrapException("not an array type");
            var elements = (System.Array)elementsField.GetValue(target)!;
            if ((long)(uint)offset + (long)(uint)length > elements.Length)
                throw new TrapException("out of bounds array access");
            var elemType = elements.GetType().GetElementType()!;
            var fillVal = ConvertValueToElement(value, elemType);
            for (int i = 0; i < length; i++)
                elements.SetValue(fillVal, offset + i);
        }

        /// <summary>array.copy with object-typed refs.</summary>
        public static void ArrayCopyObject(object? dstRef, int dstOff, object? srcRef, int srcOff, int length)
        {
            if (dstRef == null || srcRef == null) throw new TrapException("null array reference");
            var dst = Peel(dstRef)!;
            var src = Peel(srcRef)!;
            var dstField = dst.GetType().GetField("elements");
            var srcField = src.GetType().GetField("elements");
            if (dstField == null || srcField == null) throw new TrapException("not an array type");
            var dstArr = (System.Array)dstField.GetValue(dst)!;
            var srcArr = (System.Array)srcField.GetValue(src)!;
            if (dstOff + length > dstArr.Length || srcOff + length > srcArr.Length)
                throw new TrapException("out of bounds array access");
            if (length == 0) return;
            System.Array.Copy(srcArr, srcOff, dstArr, dstOff, length);
        }

        /// <summary>array.init_data with object-typed array ref.</summary>
        public static void ArrayInitDataObject(ThinContext ctx, object? arrayRef, int dstOff,
            int dataIdx, int srcOff, int length)
        {
            if (arrayRef == null) throw new TrapException("null array reference");
            var target = Peel(arrayRef)!;
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
            if (segData == null) throw new TrapException("data segment not found");
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

        /// <summary>array.init_elem with object-typed array ref.</summary>
        public static void ArrayInitElemObject(ThinContext ctx, object? arrayRef, int dstOff,
            int elemIdx, int srcOff, int length)
        {
            if (arrayRef == null) throw new TrapException("null array reference");
            var target = Peel(arrayRef)!;
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
            if (values == null) throw new TrapException("element segment not found");
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

        /// <summary>any.convert_extern on object. Re-tagging is implicit at
        /// internal-stack level — object carries no WASM type label. The
        /// boundary wrap on return / storage sets the correct Value.Type.</summary>
        /// <summary>
        /// extern.convert_any: anyref (object on internal stack) → externref
        /// (Value). Wraps with ExternRef type tag regardless of the payload's
        /// category — the spec defines this as a label change, not a content
        /// inspection (doc 1 §11.11).
        ///
        /// If the payload is a <see cref="HostExternRef"/>, unwrap it so the
        /// round-trip externref→anyref→externref preserves the original
        /// Data.Ptr encoding.
        /// </summary>
        public static Value ExternConvertAnyWrap(object? val)
        {
            if (val == null) return new Value(ValType.ExternRef);
            // Note: the three-arg ctor takes (ValType, long address, IGcRef?)
            // — important here because HostExternRef.Address is long. Using
            // `new Value(ValType.ExternRef, h.Address)` would resolve to
            // Value(ValType, object) and unbox as `(int)` → InvalidCastException.
            if (val is HostExternRef h) return new Value(ValType.ExternRef, h.Address, null);
            if (val is IGcRef igc) return new Value(ValType.ExternRef, 0, igc);
            return new Value(ValType.ExternRef, 0, new GcObjectAdapter(val));
        }

        /// <summary>
        /// any.convert_extern: externref (Value) → anyref (object on internal
        /// stack). The runtime payload must be preserved across the boundary
        /// so ref.eq and back-conversion via extern.convert_any work.
        ///
        /// - Null externref → null.
        /// - Externref with a `GcRef` (i31 / struct / array / IGcRef) → peel
        ///   and return the underlying CLR object.
        /// - Externref with only `Data.Ptr` (host-provided via `ref.extern N`
        ///   in tests) → wrap in <see cref="HostExternRef"/> so the Ptr
        ///   survives the Value→object transition.
        /// </summary>
        public static object? AnyConvertExternUnwrap(Value val)
        {
            if (val.IsNullRef) return null;
            var gcRef = val.GcRef;
            if (gcRef != null)
            {
                if (gcRef is GcObjectAdapter a) return a.Target;
                return gcRef;
            }
            // Host externref with Data.Ptr only. Intern so repeated conversions
            // of the same externref yield the same CLR instance — needed for
            // ref.eq identity semantics on the anyref side.
            return HostExternRef.Intern(val.Data.Ptr);
        }

        /// <summary>
        /// Carries a host externref's address across the Value↔object boundary
        /// when the Value has no <see cref="Value.GcRef"/>. Interned per
        /// address so reference equality corresponds to externref equality.
        /// </summary>
        public sealed class HostExternRef : IGcRef
        {
            private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, HostExternRef>
                _cache = new();

            public readonly long Address;
            private HostExternRef(long address) { Address = address; }
            public RefIdx StoreIndex => default(PtrIdx);

            public static HostExternRef Intern(long address)
                => _cache.GetOrAdd(address, a => new HostExternRef(a));
        }

        /// <summary>ref.is_null on an object-typed ref. Emitters can also
        /// inline as `ldnull; ceq`; this helper exists for uniformity with
        /// other ref ops.</summary>
        public static int IsNullRefObject(object? val) => val == null ? 1 : 0;

        /// <summary>ref.eq for two object-typed refs. Null+null → 1;
        /// reference equality on non-null peeled targets otherwise.</summary>
        public static int RefEqObject(object? a, object? b)
        {
            if (a == null && b == null) return 1;
            if (a == null || b == null) return 0;
            var pa = Peel(a);
            var pb = Peel(b);
            if (pa is I31Ref ia && pb is I31Ref ib) return ia.Value == ib.Value ? 1 : 0;
            return ReferenceEquals(pa, pb) ? 1 : 0;
        }
    }
}
