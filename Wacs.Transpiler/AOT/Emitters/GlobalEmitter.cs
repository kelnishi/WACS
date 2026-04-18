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
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types.Defs;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for WebAssembly global variable access.
    ///
    /// global.get: Load from ThinContext.Globals[idx].Value, extract typed field
    /// global.set: Store to ThinContext.Globals[idx].Value with typed construction
    /// </summary>
    internal static class GlobalEmitter
    {
        private static readonly FieldInfo GlobalsField =
            typeof(ThinContext).GetField(nameof(ThinContext.Globals))!;

        private static readonly PropertyInfo ValueProperty =
            typeof(GlobalInstance).GetProperty(nameof(GlobalInstance.Value))!;

        private static readonly MethodInfo ValueGetter = ValueProperty.GetGetMethod()!;
        private static readonly MethodInfo ValueSetter = ValueProperty.GetSetMethod()!;

        private static readonly FieldInfo DataField =
            typeof(Value).GetField(nameof(Value.Data))!;

        private static readonly FieldInfo Int32Field =
            typeof(DUnion).GetField(nameof(DUnion.Int32))!;
        private static readonly FieldInfo Int64Field =
            typeof(DUnion).GetField(nameof(DUnion.Int64))!;
        private static readonly FieldInfo Float32Field =
            typeof(DUnion).GetField(nameof(DUnion.Float32))!;
        private static readonly FieldInfo Float64Field =
            typeof(DUnion).GetField(nameof(DUnion.Float64))!;

        public static bool CanEmit(WasmOpCode op)
        {
            return op == WasmOpCode.GlobalGet
                || op == WasmOpCode.GlobalSet;
        }

        /// <summary>
        /// Emit global.get — push the global's value onto the CIL stack.
        /// GC-ref globals unwrap Value → object at the boundary (doc 2 §3);
        /// scalar globals extract the typed primitive; funcref / externref / v128
        /// stay as Value.
        /// </summary>
        public static void EmitGlobalGet(ILGenerator il, InstGlobalGet inst, ValType globalType, ModuleInstance? moduleInst = null)
        {
            int idx = inst.GetIndex();

            // ctx.Globals[idx].Value.Data.{TypedField}
            il.Emit(OpCodes.Ldarg_0);                    // ThinContext
            il.Emit(OpCodes.Ldfld, GlobalsField);        // GlobalInstance[]
            il.Emit(OpCodes.Ldc_I4, idx);                // index
            il.Emit(OpCodes.Ldelem_Ref);                 // GlobalInstance
            il.Emit(OpCodes.Callvirt, ValueGetter);       // Value (struct returned)

            if (globalType == ValType.I32 || globalType == ValType.I64 ||
                globalType == ValType.F32 || globalType == ValType.F64)
            {
                EmitExtractTypedValue(il, globalType);
            }
            else if (ModuleTranspiler.IsGcRefType(globalType, moduleInst))
            {
                // GC ref: unwrap to object (doc 2 §3 boundary).
                il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                    nameof(GcRuntimeHelpers.UnwrapRef), BindingFlags.Public | BindingFlags.Static)!);
            }
            // else: Value stays on CIL stack for funcref / externref / v128.
        }

        /// <summary>
        /// Emit global.set — pop a typed value from CIL stack and store to global.
        /// GC-ref globals wrap object → Value before storing (doc 2 §3).
        /// </summary>
        public static void EmitGlobalSet(ILGenerator il, InstGlobalSet inst, ValType globalType, ModuleInstance? moduleInst = null)
        {
            int idx = inst.GetIndex();

            if (globalType == ValType.I32 || globalType == ValType.I64 ||
                globalType == ValType.F32 || globalType == ValType.F64)
            {
                // Numeric: spill typed value, construct Value, store.
                var temp = il.DeclareLocal(ModuleTranspiler.MapValType(globalType));
                il.Emit(OpCodes.Stloc, temp);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, GlobalsField);
                il.Emit(OpCodes.Ldc_I4, idx);
                il.Emit(OpCodes.Ldelem_Ref);

                il.Emit(OpCodes.Ldloc, temp);
                EmitConstructValue(il, globalType);
                il.Emit(OpCodes.Callvirt, ValueSetter);
            }
            else if (ModuleTranspiler.IsGcRefType(globalType, moduleInst))
            {
                // GC ref: object on stack → wrap to Value → store.
                var temp = il.DeclareLocal(typeof(object));
                il.Emit(OpCodes.Stloc, temp);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, GlobalsField);
                il.Emit(OpCodes.Ldc_I4, idx);
                il.Emit(OpCodes.Ldelem_Ref);

                il.Emit(OpCodes.Ldloc, temp);
                il.Emit(OpCodes.Call, typeof(GcRuntimeHelpers).GetMethod(
                    nameof(GcRuntimeHelpers.WrapRef), BindingFlags.Public | BindingFlags.Static)!);
                il.Emit(OpCodes.Callvirt, ValueSetter);
            }
            else
            {
                // Funcref / externref / v128: Value already on stack.
                var temp = il.DeclareLocal(typeof(Value));
                il.Emit(OpCodes.Stloc, temp);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, GlobalsField);
                il.Emit(OpCodes.Ldc_I4, idx);
                il.Emit(OpCodes.Ldelem_Ref);

                il.Emit(OpCodes.Ldloc, temp);
                il.Emit(OpCodes.Callvirt, ValueSetter);
            }
        }

        /// <summary>
        /// Given a Value struct on the CIL stack, extract the typed primitive.
        /// Replaces the Value with int/long/float/double.
        /// </summary>
        private static void EmitExtractTypedValue(ILGenerator il, ValType type)
        {
            // Value is a struct — we need to use ldflda for nested field access
            var valueLocal = il.DeclareLocal(typeof(Value));
            il.Emit(OpCodes.Stloc, valueLocal);
            il.Emit(OpCodes.Ldloca, valueLocal);
            il.Emit(OpCodes.Ldflda, DataField);

            switch (type)
            {
                case ValType.I32:
                    il.Emit(OpCodes.Ldfld, Int32Field);
                    break;
                case ValType.I64:
                    il.Emit(OpCodes.Ldfld, Int64Field);
                    break;
                case ValType.F32:
                    il.Emit(OpCodes.Ldfld, Float32Field);
                    break;
                case ValType.F64:
                    il.Emit(OpCodes.Ldfld, Float64Field);
                    break;
                default:
                    throw new TranspilerException($"GlobalEmitter: unsupported global type {type}");
            }
        }

        /// <summary>
        /// Given a typed primitive on the CIL stack, construct a Value struct.
        /// Leaves Value on the stack.
        /// </summary>
        private static void EmitConstructValue(ILGenerator il, ValType type)
        {
            // Use the Value constructors: new Value(int), new Value(long), etc.
            switch (type)
            {
                case ValType.I32:
                    il.Emit(OpCodes.Newobj,
                        typeof(Value).GetConstructor(new[] { typeof(int) })!);
                    break;
                case ValType.I64:
                    il.Emit(OpCodes.Newobj,
                        typeof(Value).GetConstructor(new[] { typeof(long) })!);
                    break;
                case ValType.F32:
                    il.Emit(OpCodes.Newobj,
                        typeof(Value).GetConstructor(new[] { typeof(float) })!);
                    break;
                case ValType.F64:
                    il.Emit(OpCodes.Newobj,
                        typeof(Value).GetConstructor(new[] { typeof(double) })!);
                    break;
                default:
                    throw new TranspilerException($"GlobalEmitter: unsupported global type {type}");
            }
        }
    }
}
