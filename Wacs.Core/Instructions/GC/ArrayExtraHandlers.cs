// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using Wacs.Core.Compilation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.GC;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.GC
{
    /// <summary>
    /// [OpHandler] entries for the GC array ops that StructArrayHandlers didn't cover:
    /// array.new, array.new_fixed, array.new_data, array.new_elem, array.fill,
    /// array.copy, array.init_data, array.init_elem, and the packed get_s / get_u.
    /// Bodies mirror the polymorphic InstArrayX.Execute implementations — the type /
    /// data / elem indices decoded as stream immediates replace the `this.X` field
    /// reads in the polymorphic path.
    /// </summary>
    internal static class ArrayExtraHandlers
    {
        // 0xFB 0x06 array.new — (bottom) init value, (top) count; allocate array.
        [OpHandler(GcCode.ArrayNew)]
        private static Value ArrayNew(ExecContext ctx, [Imm] uint typeIdx, Value val, int n)
        {
            var defType = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var arrayType = (ArrayType)defType.Expansion;
            var a = ctx.Store.AddArray();
            var ai = new StoreArray(a, arrayType, val, n);
            return new Value(ValType.Ref | (ValType)typeIdx, ai);
        }

        // 0xFB 0x08 array.new_fixed — N stack values + typeIdx + N.
        [OpHandler(GcCode.ArrayNewFixed)]
        private static void ArrayNewFixed(ExecContext ctx, [Imm] uint typeIdx, [Imm] uint n)
        {
            var defType = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var arrayType = (ArrayType)defType.Expansion;
            var values = new Stack<Value>();
            for (int i = 0; i < n; ++i)
                values.Push(ctx.OpStack.PopAny());
            var a = ctx.Store.AddArray();
            var ai = new StoreArray(a, arrayType, ref values);
            ctx.OpStack.PushValue(new Value(ValType.Ref | (ValType)typeIdx, ai));
        }

        // 0xFB 0x09 array.new_data — copy N lanes from a data segment slice.
        [OpHandler(GcCode.ArrayNewData)]
        private static Value ArrayNewData(ExecContext ctx, [Imm] uint typeIdx, [Imm] uint dataIdx,
                                          int s, int n)
        {
            var defType = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var arrayType = (ArrayType)defType.Expansion;
            var da = ctx.Frame.Module.DataAddrs[(DataIdx)dataIdx];
            var datainst = ctx.Store[da];
            int z = arrayType.ElementType.BitWidth().ByteSize();
            int end = s + n * z;
            int span = end;
            if (z != 4) { span -= z; span += 1; }
            if (span > datainst.Data.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("array.new_data: array size exceeds data length");
            // HACK (from polymorphic path): unaligned read shim.
            if (end > datainst.Data.Length)
            {
                end = datainst.Data.Length;
                s = end - n * z;
            }
            if (s < 0)
                throw new Wacs.Core.Runtime.Types.TrapException("array.new_data: out of bounds source start");
            if (end > datainst.Data.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("array.new_data: out of bounds end index");
            var b = datainst.Data.AsSpan()[s..end];
            var a = ctx.Store.AddArray();
            var ai = new StoreArray(a, arrayType, b, n, z);
            return new Value(ValType.Ref | (ValType)typeIdx, ai);
        }

        // 0xFB 0x0A array.new_elem — copy N refs from an element segment slice.
        [OpHandler(GcCode.ArrayNewElem)]
        private static Value ArrayNewElem(ExecContext ctx, [Imm] uint typeIdx, [Imm] uint elemIdx,
                                          int s, int n)
        {
            var defType = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var arrayType = (ArrayType)defType.Expansion;
            var ea = ctx.Frame.Module.ElemAddrs[(ElemIdx)elemIdx];
            var eleminst = ctx.Store[ea];
            if (s < 0 || n < 0)
                throw new Wacs.Core.Runtime.Types.TrapException("array.new_elem: out of bounds source or count");
            if (s + n > eleminst.Elements.Count)
                throw new Wacs.Core.Runtime.Types.TrapException("array.new_elem: array size exceeds elements length");
            var refs = eleminst.Elements.GetRange(s, n);
            var a = ctx.Store.AddArray();
            var ai = new StoreArray(a, arrayType, refs);
            return new Value(ValType.Ref | (ValType)typeIdx, ai);
        }

        // 0xFB 0x10 array.fill — pop ref, d, val, n; write val into arr[d..d+n].
        [OpHandler(GcCode.ArrayFill)]
        private static void ArrayFill(ExecContext ctx, [Imm] uint typeIdx,
                                      Value arrRef, int d, Value val, int n)
        {
            if (arrRef.IsNullRef)
                throw new Wacs.Core.Runtime.Types.TrapException("array.fill: null array reference");
            var a = (StoreArray)arrRef.GcRef;
            if (d + n > a.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("array.fill: array overflow");
            if (n == 0) return;
            a.Fill(val, d, n);
        }

        // 0xFB 0x11 array.copy — copy n entries from src[s..s+n] to dst[d..d+n].
        [OpHandler(GcCode.ArrayCopy)]
        private static void ArrayCopy(ExecContext ctx, [Imm] uint dstTypeIdx, [Imm] uint srcTypeIdx,
                                      Value dstRef, int d, Value srcRef, int s, int n)
        {
            if (dstRef.IsNullRef)
                throw new Wacs.Core.Runtime.Types.TrapException("array.copy: null destination array");
            if (srcRef.IsNullRef)
                throw new Wacs.Core.Runtime.Types.TrapException("array.copy: null source array");
            var dst = (StoreArray)dstRef.GcRef;
            var src = (StoreArray)srcRef.GcRef;
            if (d + n > dst.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("array.copy: destination overflow");
            if (s + n > src.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("array.copy: source overflow");
            if (n == 0) return;
            src.Copy(s, dst, d, n);
        }

        // 0xFB 0x12 array.init_data — copy bytes from data[s..s+n*z] into arr[d..d+n].
        [OpHandler(GcCode.ArrayInitData)]
        private static void ArrayInitData(ExecContext ctx, [Imm] uint typeIdx, [Imm] uint dataIdx,
                                          Value arrRef, int d, int s, int n)
        {
            if (arrRef.IsNullRef)
                throw new Wacs.Core.Runtime.Types.TrapException("array.init_data: null array reference");
            var defType = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var arrayType = (ArrayType)defType.Expansion;
            var a = (StoreArray)arrRef.GcRef;
            int z = arrayType.ElementType.BitWidth().ByteSize();
            var da = ctx.Frame.Module.DataAddrs[(DataIdx)dataIdx];
            var datainst = ctx.Store[da];
            if (d + n > a.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("array.init_data: data length exceeds array bounds");
            int end = s + n * z;
            if (end > datainst.Data.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("array.init_data: array size exceeds data length");
            if (end > datainst.Data.Length)
            {
                end = datainst.Data.Length;
                s = end - n * z;
            }
            if (n == 0) return;
            var b = datainst.Data.AsSpan()[s..end];
            a.Init(d, b, n, z);
        }

        // 0xFB 0x13 array.init_elem — copy refs from elem[s..s+n] into arr[d..d+n].
        [OpHandler(GcCode.ArrayInitElem)]
        private static void ArrayInitElem(ExecContext ctx, [Imm] uint typeIdx, [Imm] uint elemIdx,
                                          Value arrRef, int d, int s, int n)
        {
            if (arrRef.IsNullRef)
                throw new Wacs.Core.Runtime.Types.TrapException("array.init_elem: null array reference");
            var a = (StoreArray)arrRef.GcRef;
            var ea = ctx.Frame.Module.ElemAddrs[(ElemIdx)elemIdx];
            var eleminst = ctx.Store[ea];
            if (d + n > a.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("array.init_elem: element length exceeds array bounds");
            if (s + n > eleminst.Elements.Count)
                throw new Wacs.Core.Runtime.Types.TrapException("array.init_elem: array size exceeds elements length");
            if (n == 0) return;
            var refs = eleminst.Elements.GetRange(s, n);
            a.Init(d, refs, n);
        }

        // 0xFB 0x0C array.get_s — packed signed extension (i8/i16 → i32).
        [OpHandler(GcCode.ArrayGetS)]
        private static int ArrayGetS(ExecContext ctx, [Imm] uint typeIdx, Value arrRef, int idx)
        {
            if (arrRef.IsNullRef)
                throw new Wacs.Core.Runtime.Types.TrapException("array.get_s: null reference");
            var a = (StoreArray)arrRef.GcRef;
            if ((uint)idx >= (uint)a.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("array.get_s: out of bounds");
            var v = a[idx];
            var defType = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var arrayType = (ArrayType)defType.Expansion;
            return arrayType.ElementType.StorageType switch
            {
                ValType.I8 => (int)(sbyte)v.Data.Int32,
                ValType.I16 => (int)(short)v.Data.Int32,
                _ => v.Data.Int32,
            };
        }

        // 0xFB 0x0D array.get_u — packed unsigned (i8/i16 → u32).
        [OpHandler(GcCode.ArrayGetU)]
        private static int ArrayGetU(ExecContext ctx, [Imm] uint typeIdx, Value arrRef, int idx)
        {
            if (arrRef.IsNullRef)
                throw new Wacs.Core.Runtime.Types.TrapException("array.get_u: null reference");
            var a = (StoreArray)arrRef.GcRef;
            if ((uint)idx >= (uint)a.Length)
                throw new Wacs.Core.Runtime.Types.TrapException("array.get_u: out of bounds");
            var v = a[idx];
            var defType = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var arrayType = (ArrayType)defType.Expansion;
            return arrayType.ElementType.StorageType switch
            {
                ValType.I8 => (int)(byte)v.Data.Int32,
                ValType.I16 => (int)(ushort)v.Data.Int32,
                _ => v.Data.Int32,
            };
        }
    }
}
