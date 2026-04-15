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
using Wacs.Core.Instructions.Reference;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for table access and reference type instructions.
    /// These operate on Value (reference types stay as Value on the CIL stack).
    ///
    /// Table ops dispatch through TranspiledContext.Tables[].
    /// Ref ops create/inspect Value structs.
    /// Also handles 0xFC prefix table.size/grow/fill.
    /// </summary>
    internal static class TableRefEmitter
    {
        public static bool CanEmit(WasmOpCode op)
        {
            return op == WasmOpCode.TableGet
                || op == WasmOpCode.TableSet
                || op == WasmOpCode.RefNull
                || op == WasmOpCode.RefIsNull
                || op == WasmOpCode.RefFunc
                || op == WasmOpCode.RefEq
                || op == WasmOpCode.RefAsNonNull
                // br_on_null/non_null handled by FunctionCodegen (need block stack)
                ;
        }

        public static bool CanEmitExt(Wacs.Core.OpCodes.ExtCode op)
        {
            return op == Wacs.Core.OpCodes.ExtCode.TableGrow
                || op == Wacs.Core.OpCodes.ExtCode.TableSize
                || op == Wacs.Core.OpCodes.ExtCode.TableFill;
        }

        public static void Emit(ILGenerator il, InstructionBase inst, WasmOpCode op)
        {
            switch (op)
            {
                case WasmOpCode.TableGet:
                    EmitTableGet(il, (InstTableGet)inst);
                    break;
                case WasmOpCode.TableSet:
                    EmitTableSet(il, (InstTableSet)inst);
                    break;
                case WasmOpCode.RefNull:
                    EmitRefNull(il, (InstRefNull)inst);
                    break;
                case WasmOpCode.RefIsNull:
                    EmitRefIsNull(il);
                    break;
                case WasmOpCode.RefFunc:
                    EmitRefFunc(il, (InstRefFunc)inst);
                    break;
                case WasmOpCode.RefEq:
                    EmitRefEq(il);
                    break;
                case WasmOpCode.RefAsNonNull:
                    EmitRefAsNonNull(il);
                    break;
                default:
                    throw new TranspilerException($"TableRefEmitter: unhandled opcode {op}");
            }
        }

        public static void EmitExt(ILGenerator il, InstructionBase inst, Wacs.Core.OpCodes.ExtCode op)
        {
            switch (op)
            {
                case Wacs.Core.OpCodes.ExtCode.TableSize:
                    EmitTableSize(il, (InstTableSize)inst);
                    break;
                case Wacs.Core.OpCodes.ExtCode.TableGrow:
                    EmitTableGrow(il, (InstTableGrow)inst);
                    break;
                case Wacs.Core.OpCodes.ExtCode.TableFill:
                    EmitTableFill(il, (InstTableFill)inst);
                    break;
                default:
                    throw new TranspilerException($"TableRefEmitter: unhandled ext opcode {op}");
            }
        }

        // ref.eq: [Value, Value] → [i32]
        private static void EmitRefEq(ILGenerator il)
        {
            il.Emit(OpCodes.Call, typeof(TableRefHelpers).GetMethod(
                nameof(TableRefHelpers.RefEq), BindingFlags.Public | BindingFlags.Static)!);
        }

        // ref.as_non_null: [Value] → [Value] (traps if null)
        private static void EmitRefAsNonNull(ILGenerator il)
        {
            il.Emit(OpCodes.Call, typeof(TableRefHelpers).GetMethod(
                nameof(TableRefHelpers.RefAsNonNull), BindingFlags.Public | BindingFlags.Static)!);
        }

        // table.get: [i32 index] → [Value]
        private static void EmitTableGet(ILGenerator il, InstTableGet inst)
        {
            // Stack: [index]
            var idxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, idxLocal);
            il.Emit(OpCodes.Ldarg_0); // ctx
            il.Emit(OpCodes.Ldc_I4, inst.TableIndex);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Call, typeof(TableRefHelpers).GetMethod(
                nameof(TableRefHelpers.TableGet), BindingFlags.Public | BindingFlags.Static)!);
        }

        // table.set: [i32 index, Value ref] → []
        private static void EmitTableSet(ILGenerator il, InstTableSet inst)
        {
            // Stack: [index, value]
            var valLocal = il.DeclareLocal(typeof(Value));
            il.Emit(OpCodes.Stloc, valLocal);
            var idxLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, idxLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, inst.TableIndex);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Call, typeof(TableRefHelpers).GetMethod(
                nameof(TableRefHelpers.TableSet), BindingFlags.Public | BindingFlags.Static)!);
        }

        // ref.null t: [] → [Value (null ref)]
        private static void EmitRefNull(ILGenerator il, InstRefNull inst)
        {
            il.Emit(OpCodes.Ldc_I4, (int)inst.RefType);
            il.Emit(OpCodes.Call, typeof(Value).GetMethod(
                nameof(Value.Null), new[] { typeof(ValType) })!);
        }

        // ref.is_null: [Value] → [i32]
        private static void EmitRefIsNull(ILGenerator il)
        {
            il.Emit(OpCodes.Call, typeof(TableRefHelpers).GetMethod(
                nameof(TableRefHelpers.RefIsNull), BindingFlags.Public | BindingFlags.Static)!);
        }

        // ref.func x: [] → [Value (funcref)]
        private static void EmitRefFunc(ILGenerator il, InstRefFunc inst)
        {
            il.Emit(OpCodes.Ldarg_0); // ctx
            il.Emit(OpCodes.Ldc_I4, (int)inst.FunctionIndex.Value);
            il.Emit(OpCodes.Call, typeof(TableRefHelpers).GetMethod(
                nameof(TableRefHelpers.RefFunc), BindingFlags.Public | BindingFlags.Static)!);
        }

        private static void EmitTableSize(ILGenerator il, InstTableSize inst)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, inst.TableIndex);
            il.Emit(OpCodes.Call, typeof(TableRefHelpers).GetMethod(
                nameof(TableRefHelpers.TableSize), BindingFlags.Public | BindingFlags.Static)!);
        }

        private static void EmitTableGrow(ILGenerator il, InstTableGrow inst)
        {
            // Stack: [Value initVal, i32 delta]
            var deltaLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, deltaLocal);
            var initLocal = il.DeclareLocal(typeof(Value));
            il.Emit(OpCodes.Stloc, initLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, inst.TableIndex);
            il.Emit(OpCodes.Ldloc, initLocal);
            il.Emit(OpCodes.Ldloc, deltaLocal);
            il.Emit(OpCodes.Call, typeof(TableRefHelpers).GetMethod(
                nameof(TableRefHelpers.TableGrow), BindingFlags.Public | BindingFlags.Static)!);
        }

        private static void EmitTableFill(ILGenerator il, InstTableFill inst)
        {
            // Stack: [i32 dst, Value val, i32 len]
            var lenLocal = il.DeclareLocal(typeof(int));
            var valLocal = il.DeclareLocal(typeof(Value));
            var dstLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, lenLocal);
            il.Emit(OpCodes.Stloc, valLocal);
            il.Emit(OpCodes.Stloc, dstLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, inst.TableIndex);
            il.Emit(OpCodes.Ldloc, dstLocal);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Call, typeof(TableRefHelpers).GetMethod(
                nameof(TableRefHelpers.TableFill), BindingFlags.Public | BindingFlags.Static)!);
        }
    }

    public static class TableRefHelpers
    {
        public static Value TableGet(TranspiledContext ctx, int tableIdx, int index)
        {
            var table = ctx.Tables[tableIdx];
            if (index < 0 || index >= table.Elements.Count)
                throw new TrapException("out of bounds table access");
            return table.Elements[index];
        }

        public static void TableSet(TranspiledContext ctx, int tableIdx, int index, Value val)
        {
            var table = ctx.Tables[tableIdx];
            if (index < 0 || index >= table.Elements.Count)
                throw new TrapException("out of bounds table access");
            table.Elements[index] = val;
        }

        public static int RefIsNull(Value val)
        {
            return val.IsNullRef ? 1 : 0;
        }

        public static int RefEq(Value a, Value b)
        {
            // ref.eq: both null → 1, both same ref → 1, else 0
            if (a.IsNullRef && b.IsNullRef) return 1;
            if (a.IsNullRef || b.IsNullRef) return 0;
            return a.Data.Ptr == b.Data.Ptr ? 1 : 0;
        }

        public static Value RefAsNonNull(Value val)
        {
            if (val.IsNullRef)
                throw new TrapException("null reference");
            return val;
        }

        public static Value RefFunc(TranspiledContext ctx, int funcIdx)
        {
            if (ctx.Module == null)
                throw new TrapException("ref.func requires runtime module");
            var funcAddr = ctx.Module.FuncAddrs[(FuncIdx)funcIdx];
            return new Value(funcAddr);
        }

        public static int TableSize(TranspiledContext ctx, int tableIdx)
        {
            return ctx.Tables[tableIdx].Elements.Count;
        }

        public static int TableGrow(TranspiledContext ctx, int tableIdx, Value initVal, int delta)
        {
            var table = ctx.Tables[tableIdx];
            int oldSize = table.Elements.Count;
            if (!table.Grow(delta, initVal))
                return -1;
            return oldSize;
        }

        public static void TableFill(TranspiledContext ctx, int tableIdx, int dst, Value val, int len)
        {
            if (len == 0) return;
            var table = ctx.Tables[tableIdx];
            if (dst < 0 || (long)dst + len > table.Elements.Count)
                throw new TrapException("out of bounds table access");
            for (int i = 0; i < len; i++)
                table.Elements[dst + i] = val;
        }
    }
}
