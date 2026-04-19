// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using Wacs.Core.Compilation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.GC;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.GC
{
    /// <summary>
    /// [OpHandler] entries for the main GC struct and array ops. Targets the non-packed
    /// variants (no sign/zero extension); the .s / .u suffix variants (struct.get_s,
    /// struct.get_u, array.get_s, array.get_u) and the bulk array ops (array.new_fixed /
    /// new_data / new_elem / fill / copy / init_*) are wireable on the same pattern but
    /// deferred to keep this commit focused.
    ///
    /// Bodies mirror the polymorphic InstStructX / InstArrayX classes but elide the
    /// `context.Assert` checks that reference `this.Op.GetMnemonic()` — those can't be
    /// inlined into GeneratedDispatcher. Validation has already guaranteed the shapes,
    /// and trap-paths use literal mnemonics instead.
    /// </summary>
    internal static class StructArrayHandlers
    {
        // 0xFB 00 struct.new — pop N fields (N = struct's field count), alloc, push ref.
        [OpHandler(GcCode.StructNew)]
        private static void StructNew(ExecContext ctx, [Imm] uint typeIdx)
        {
            var defType = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var structType = (StructType)defType.Expansion;
            int n = structType.FieldTypes.Length;

            Stack<Value> vals = new();
            ctx.OpStack.PopResults(n, ref vals);
            var a = ctx.Store.AddStruct();
            var si = new StoreStruct(a, structType, vals);
            ctx.OpStack.PushValue(new Value(ValType.Ref | (ValType)typeIdx, si));
        }

        // 0xFB 01 struct.new_default — no stack pops; alloc default-initialized struct.
        [OpHandler(GcCode.StructNewDefault)]
        private static void StructNewDefault(ExecContext ctx, [Imm] uint typeIdx)
        {
            var defType = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var structType = (StructType)defType.Expansion;
            var a = ctx.Store.AddStruct();
            var si = new StoreStruct(a, structType);
            ctx.OpStack.PushValue(new Value(ValType.Ref | (ValType)typeIdx, si));
        }

        // 0xFB 02 struct.get — pop ref, push field. Non-packed only (no extension).
        [OpHandler(GcCode.StructGet)]
        private static Value StructGet(ExecContext ctx, [Imm] uint typeIdx, [Imm] uint fieldIdx, Value refVal)
        {
            if (refVal.IsNullRef)
                throw new TrapException("struct.get: null reference");
            var refStruct = (StoreStruct)refVal.GcRef;
            return refStruct[(FieldIdx)fieldIdx];
        }

        // 0xFB 05 struct.set — pop ref, pop val, write. Stack: [ref, val] with val on top.
        [OpHandler(GcCode.StructSet)]
        private static void StructSet(ExecContext ctx, [Imm] uint typeIdx, [Imm] uint fieldIdx, Value refVal, Value val)
        {
            if (refVal.IsNullRef)
                throw new TrapException("struct.set: null reference");
            var refStruct = (StoreStruct)refVal.GcRef;
            refStruct[(FieldIdx)fieldIdx] = val;
        }

        // 0xFB 03 struct.get_s — packed signed extension on i8 / i16 fields.
        [OpHandler(GcCode.StructGetS)]
        private static int StructGetS(ExecContext ctx, [Imm] uint typeIdx, [Imm] uint fieldIdx, Value refVal)
        {
            if (refVal.IsNullRef)
                throw new TrapException("struct.get_s: null reference");
            var refStruct = (StoreStruct)refVal.GcRef;
            var v = refStruct[(FieldIdx)fieldIdx];
            var defType = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var structType = (StructType)defType.Expansion;
            var fieldType = structType.FieldTypes[(int)fieldIdx];
            return fieldType.StorageType switch
            {
                ValType.I8 => (int)(sbyte)v.Data.Int32,
                ValType.I16 => (int)(short)v.Data.Int32,
                _ => v.Data.Int32,
            };
        }

        // 0xFB 04 struct.get_u — packed unsigned extension.
        [OpHandler(GcCode.StructGetU)]
        private static int StructGetU(ExecContext ctx, [Imm] uint typeIdx, [Imm] uint fieldIdx, Value refVal)
        {
            if (refVal.IsNullRef)
                throw new TrapException("struct.get_u: null reference");
            var refStruct = (StoreStruct)refVal.GcRef;
            var v = refStruct[(FieldIdx)fieldIdx];
            var defType = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var structType = (StructType)defType.Expansion;
            var fieldType = structType.FieldTypes[(int)fieldIdx];
            return fieldType.StorageType switch
            {
                ValType.I8 => (int)(byte)v.Data.Int32,
                ValType.I16 => (int)(ushort)v.Data.Int32,
                _ => v.Data.Int32,
            };
        }

        // 0xFB 07 array.new_default — pop count n, alloc zero-init array of length n.
        [OpHandler(GcCode.ArrayNewDefault)]
        private static void ArrayNewDefault(ExecContext ctx, [Imm] uint typeIdx, int n)
        {
            var defType = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var arrayType = (ArrayType)defType.Expansion;
            var a = ctx.Store.AddArray();
            var ai = new StoreArray(a, arrayType, n);
            ctx.OpStack.PushValue(new Value(ValType.Ref | (ValType)typeIdx, ai));
        }

        // 0xFB 0F array.len — pop ref, push length. No typeIdx immediate.
        [OpHandler(GcCode.ArrayLen)]
        private static int ArrayLen(Value refVal)
        {
            if (refVal.IsNullRef)
                throw new TrapException("array.len: null reference");
            return ((StoreArray)refVal.GcRef).Length;
        }

        // 0xFB 0B array.get — pop ref, pop idx, push elem. Non-packed only.
        [OpHandler(GcCode.ArrayGet)]
        private static Value ArrayGet(ExecContext ctx, [Imm] uint typeIdx, Value refVal, int idx)
        {
            if (refVal.IsNullRef)
                throw new TrapException("array.get: null reference");
            var arr = (StoreArray)refVal.GcRef;
            if ((uint)idx >= (uint)arr.Length)
                throw new TrapException("array.get: out of bounds");
            return arr[idx];
        }

        // 0xFB 0E array.set — pop ref, pop idx, pop val, write. Stack: [ref, idx, val].
        [OpHandler(GcCode.ArraySet)]
        private static void ArraySet(ExecContext ctx, [Imm] uint typeIdx, Value refVal, int idx, Value val)
        {
            if (refVal.IsNullRef)
                throw new TrapException("array.set: null reference");
            var arr = (StoreArray)refVal.GcRef;
            if ((uint)idx >= (uint)arr.Length)
                throw new TrapException("array.set: out of bounds");
            arr[idx] = val;
        }
    }
}
