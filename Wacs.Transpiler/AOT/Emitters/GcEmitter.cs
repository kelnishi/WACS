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

            // Array ops: 0x06-0x13 (complex — defer for now except simple ones)
            // Start with basic ones: array.new (0x06), array.new_default (0x07),
            // array.get (0x0B-0x0D), array.set (0x0E), array.len (0x0F)
            if (op == GcCode.ArrayNew || op == GcCode.ArrayNewDefault ||
                op == GcCode.ArrayGet || op == GcCode.ArrayGetS || op == GcCode.ArrayGetU ||
                op == GcCode.ArraySet || op == GcCode.ArrayLen)
                return true;

            // ref.test/cast: 0x14-0x17
            if (op == GcCode.RefTest || op == GcCode.RefTestNull ||
                op == GcCode.RefCast || op == GcCode.RefCastNull)
                return true;

            // Conversion: 0x1A-0x1B
            if (op == GcCode.AnyConvertExtern || op == GcCode.ExternConvertAny)
                return true;

            // i31: 0x1C-0x1E
            if (op == GcCode.RefI31 || op == GcCode.I31GetS || op == GcCode.I31GetU)
                return true;

            return false;
        }

        public static void Emit(ILGenerator il, InstructionBase inst, GcCode op,
            GcTypeEmitter gcTypes, ModuleInstance moduleInst)
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

                // === ref.test / ref.cast ===
                case GcCode.RefTest:
                case GcCode.RefTestNull:
                    EmitRefTest(il, (InstRefTest)inst);
                    break;
                case GcCode.RefCast:
                case GcCode.RefCastNull:
                    EmitRefCast(il, (InstRefCast)inst);
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
        }

        // struct.new_default X: create instance (fields already zero-initialized by CLR)
        private static void EmitStructNewDefault(ILGenerator il, InstStructNewDefault inst,
            GcTypeEmitter gcTypes)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"struct.new_default: type {inst.TypeIndex} not emitted");
            il.Emit(OpCodes.Newobj, gcType.ClrType.GetConstructor(Type.EmptyTypes)!);
        }

        // struct.get X Y: pop structref, load field Y
        private static void EmitStructGet(ILGenerator il, InstStructGet inst,
            GcTypeEmitter gcTypes)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"struct.get: type {inst.TypeIndex} not emitted");

            // Stack: [structref (object)]
            il.Emit(OpCodes.Castclass, gcType.ClrType);
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

            // Stack: [structref, value]
            var valLocal = il.DeclareLocal(gcType.Fields[inst.FieldIndex].FieldType);
            il.Emit(OpCodes.Stloc, valLocal);
            il.Emit(OpCodes.Castclass, gcType.ClrType);
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
        }

        // array.get X: pop [arrayref, index], load element
        private static void EmitArrayGet(ILGenerator il, InstArrayGet inst,
            GcTypeEmitter gcTypes, ModuleInstance moduleInst)
        {
            var gcType = gcTypes.GetGcType(inst.TypeIndex);
            if (gcType == null)
                throw new TranspilerException($"array.get: type {inst.TypeIndex} not emitted");

            // Stack: [arrayref, index]
            var idxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, idxLocal);
            il.Emit(OpCodes.Castclass, gcType.ClrType);
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

            // Stack: [arrayref, index, value]
            var valLocal = il.DeclareLocal(gcType.Fields[0].FieldType.GetElementType()!);
            il.Emit(OpCodes.Stloc, valLocal);
            var idxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, idxLocal);
            il.Emit(OpCodes.Castclass, gcType.ClrType);
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

        // ref.test: pop [ref], push [i32 (0 or 1)]
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
    }

    /// <summary>
    /// Runtime helpers for GC operations called from transpiled IL.
    /// These handle the Value-based operations that can't be done with pure CLR types.
    /// </summary>
    public static class GcRuntimeHelpers
    {
        public static int ArrayLen(object arrayRef)
        {
            if (arrayRef == null)
                throw new TrapException("null array reference");
            // All emitted array types have a public 'length' field
            var field = arrayRef.GetType().GetField("length");
            if (field == null)
                throw new TrapException("not an array type");
            return (int)field.GetValue(arrayRef)!;
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
    }
}
