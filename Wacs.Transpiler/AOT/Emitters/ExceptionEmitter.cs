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
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    internal static class ExceptionEmitter
    {
        public static bool CanEmit(WasmOpCode op)
        {
            return op == WasmOpCode.Throw
                || op == WasmOpCode.ThrowRef
                || op == WasmOpCode.TryTable;
        }

        /// <summary>
        /// Emit throw: pop N field values, create WasmException, throw.
        /// Spec: throw x → create exn{tag, fields}, throw_ref
        /// </summary>
        public static void EmitThrow(ILGenerator il, InstThrow inst, ModuleInstance moduleInst)
        {
            int tagIdx = inst.TagIndex;

            // Resolve the tag's type to know how many fields to pop
            int fieldCount = ResolveTagFieldCount(moduleInst, (TagIdx)tagIdx);

            // Spill fields from CIL stack into Value[]
            var fieldTemps = new LocalBuilder[fieldCount];
            for (int i = fieldCount - 1; i >= 0; i--)
            {
                fieldTemps[i] = il.DeclareLocal(typeof(Value));
                // Fields are on CIL stack as typed values — box to Value
                // Actually they could be int/long/float/double or Value depending on type
                // For simplicity, assume all are already Value or can be boxed
                il.Emit(OpCodes.Stloc, fieldTemps[i]);
            }

            // Build Value[] fields
            il.Emit(OpCodes.Ldc_I4, fieldCount);
            il.Emit(OpCodes.Newarr, typeof(Value));
            for (int i = 0; i < fieldCount; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, fieldTemps[i]);
                il.Emit(OpCodes.Stelem, typeof(Value));
            }
            var fieldsLocal = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Stloc, fieldsLocal);

            // Create and throw WasmException
            il.Emit(OpCodes.Ldarg_0); // ctx
            il.Emit(OpCodes.Ldc_I4, tagIdx);
            il.Emit(OpCodes.Ldloc, fieldsLocal);
            il.Emit(OpCodes.Call, typeof(ExceptionHelpers).GetMethod(
                nameof(ExceptionHelpers.CreateAndThrow), BindingFlags.Public | BindingFlags.Static)!);
        }

        /// <summary>
        /// Emit throw_ref: pop exnref (Value), extract ExnInstance, throw WasmException.
        /// Spec: throw_ref → pop exnref, trap if null, unwind to handler.
        /// </summary>
        public static void EmitThrowRef(ILGenerator il)
        {
            // Stack: [Value (exnref)]
            il.Emit(OpCodes.Call, typeof(ExceptionHelpers).GetMethod(
                nameof(ExceptionHelpers.ThrowRef), BindingFlags.Public | BindingFlags.Static)!);
        }

        /// <summary>
        /// Emit try_table: CIL try/catch wrapping the body, with tag-based dispatch.
        ///
        /// CIL structure:
        ///   .try { body; leave END }
        ///   catch WasmException {
        ///     stloc exn;
        ///     // Check each catch clause, store fields, leave to DISPATCH_N
        ///     rethrow  // no clause matched
        ///   }
        ///   DISPATCH_0: load fields, br catchLabel0
        ///   DISPATCH_1: load fields, br catchLabel1
        ///   ...
        ///   END:
        /// </summary>
        public static void EmitTryTable(
            ILGenerator il,
            InstTryTable inst,
            Stack<EmitBlock> blockStack,
            EmitInstructionDelegate emitInstruction,
            ModuleInstance moduleInst)
        {
            var catches = inst.Catches;
            var endLabel = il.DefineLabel();

            // The try_table introduces a block label (like block) — branch to end
            blockStack.Push(new EmitBlock
            {
                BranchTarget = endLabel,
                Kind = WasmOpCode.Block
            });

            // Define dispatch labels (one per catch clause, outside the try/catch)
            var dispatchLabels = new System.Reflection.Emit.Label[catches.Length];
            for (int i = 0; i < catches.Length; i++)
                dispatchLabels[i] = il.DefineLabel();

            // Local for caught exception
            var exnLocal = il.DeclareLocal(typeof(WasmException));
            // Local for matched clause index
            var clauseLocal = il.DeclareLocal(typeof(int));

            // === .try ===
            il.BeginExceptionBlock();

            // Emit body
            var block = inst.GetBlock(0);
            foreach (var child in block.Instructions)
                emitInstruction(il, child);

            // Normal exit: leave to END (implicit from BeginExceptionBlock/EndExceptionBlock)
            il.Emit(OpCodes.Leave, endLabel);

            // === catch (WasmException) ===
            il.BeginCatchBlock(typeof(WasmException));
            il.Emit(OpCodes.Stloc, exnLocal);

            // Check each catch clause in order
            for (int i = 0; i < catches.Length; i++)
            {
                var handler = catches[i];
                switch (handler.Mode)
                {
                    case CatchFlags.None: // catch tagidx labelidx
                    case CatchFlags.CatchRef: // catch_ref tagidx labelidx
                    {
                        // Check if exn.Tag matches module.TagAddrs[handler.X]
                        il.Emit(OpCodes.Ldarg_0); // ctx
                        il.Emit(OpCodes.Ldloc, exnLocal);
                        il.Emit(OpCodes.Ldc_I4, (int)handler.X.Value);
                        il.Emit(OpCodes.Call, typeof(ExceptionHelpers).GetMethod(
                            nameof(ExceptionHelpers.TagMatches), BindingFlags.Public | BindingFlags.Static)!);
                        var nextClause = il.DefineLabel();
                        il.Emit(OpCodes.Brfalse, nextClause);

                        // Match! Store clause index, leave to dispatch
                        il.Emit(OpCodes.Ldc_I4, i);
                        il.Emit(OpCodes.Stloc, clauseLocal);
                        il.Emit(OpCodes.Leave, dispatchLabels[i]);

                        il.MarkLabel(nextClause);
                        break;
                    }
                    case CatchFlags.CatchAll: // catch_all labelidx
                    case CatchFlags.CatchAllRef: // catch_all_ref labelidx
                    {
                        // Always matches
                        il.Emit(OpCodes.Ldc_I4, i);
                        il.Emit(OpCodes.Stloc, clauseLocal);
                        il.Emit(OpCodes.Leave, dispatchLabels[i]);
                        break;
                    }
                }
            }

            // No clause matched — rethrow
            il.Emit(OpCodes.Rethrow);

            // === end try/catch ===
            il.EndExceptionBlock();

            // === Dispatch labels (outside try/catch) ===
            // Each dispatch: push fields (and optionally exnref), branch to catch label
            for (int i = 0; i < catches.Length; i++)
            {
                il.MarkLabel(dispatchLabels[i]);
                var handler = catches[i];
                int labelDepth = (int)handler.L.Value;

                switch (handler.Mode)
                {
                    case CatchFlags.None:
                    {
                        // Push exception fields to CIL stack
                        int fieldCount = ResolveTagFieldCount(moduleInst, handler.X);
                        EmitPushFieldsTyped(il, exnLocal, fieldCount);
                        break;
                    }
                    case CatchFlags.CatchRef:
                    {
                        int fieldCount = ResolveTagFieldCount(moduleInst, handler.X);
                        EmitPushFieldsTyped(il, exnLocal, fieldCount);
                        EmitPushExnRef(il, exnLocal);
                        break;
                    }
                    case CatchFlags.CatchAll:
                        break;
                    case CatchFlags.CatchAllRef:
                        EmitPushExnRef(il, exnLocal);
                        break;
                }

                // Branch to the catch label (resolve from block stack)
                // The label depth is relative to the try_table's enclosing context
                // Since we pushed the try_table block, depth 0 = try_table end,
                // depth 1 = enclosing block, etc.
                int idx = 0;
                foreach (var block2 in blockStack)
                {
                    if (idx == labelDepth)
                    {
                        il.Emit(OpCodes.Br, block2.BranchTarget);
                        break;
                    }
                    idx++;
                }
            }

            blockStack.Pop();
            il.MarkLabel(endLabel);
        }

        private static int ResolveTagFieldCount(ModuleInstance moduleInst, TagIdx tagIdx)
        {
            // Imported tags come before local tags in the index space.
            // Check imports first, then local tags.
            int importedTagCount = 0;
            foreach (var import in moduleInst.Repr.Imports)
            {
                if (import.Desc is Wacs.Core.Module.ImportDesc.TagDesc)
                    importedTagCount++;
            }

            TypeIdx typeIdx;
            if ((int)tagIdx.Value < importedTagCount)
            {
                // Imported tag — get type from import desc
                int ti = 0;
                foreach (var import in moduleInst.Repr.Imports)
                {
                    if (import.Desc is Wacs.Core.Module.ImportDesc.TagDesc td)
                    {
                        if (ti == (int)tagIdx.Value)
                        {
                            var ft = moduleInst.Types[td.TagDef.TypeIndex].Expansion as FunctionType;
                            return ft?.ParameterTypes.Arity ?? 0;
                        }
                        ti++;
                    }
                }
                return 0;
            }

            // Local tag
            int localIdx = (int)tagIdx.Value - importedTagCount;
            var tag = moduleInst.Repr.Tags[localIdx];
            var tagType = moduleInst.Types[tag.TypeIndex].Expansion as FunctionType;
            return tagType?.ParameterTypes.Arity ?? 0;
        }

        /// <summary>
        /// Push N exception fields to the CIL stack as Value.
        /// The field count is known at emit time from the tag definition.
        /// </summary>
        private static void EmitPushFieldsTyped(ILGenerator il, LocalBuilder exnLocal, int fieldCount)
        {
            if (fieldCount == 0) return;

            // Get the fields array
            il.Emit(OpCodes.Ldloc, exnLocal);
            il.Emit(OpCodes.Callvirt, typeof(WasmException).GetProperty(nameof(WasmException.Fields))!.GetGetMethod()!);
            var fieldsLocal = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Stloc, fieldsLocal);

            // Push each field individually
            for (int f = 0; f < fieldCount; f++)
            {
                il.Emit(OpCodes.Ldloc, fieldsLocal);
                il.Emit(OpCodes.Ldc_I4, f);
                il.Emit(OpCodes.Ldelem, typeof(Value));
            }
        }

        private static void EmitPushExnRef(ILGenerator il, LocalBuilder exnLocal)
        {
            il.Emit(OpCodes.Ldloc, exnLocal);
            il.Emit(OpCodes.Callvirt, typeof(WasmException).GetProperty(nameof(WasmException.ExnRef))!.GetGetMethod()!);
        }
    }

    public static class ExceptionHelpers
    {
        public static void CreateAndThrow(ThinContext ctx, int tagIdx, Value[] fields)
        {
            if (ctx.Module == null || ctx.Store == null)
                throw new TrapException("throw requires runtime context");
            var tagAddr = ctx.Module.TagAddrs[(TagIdx)tagIdx];
            // AllocateExn expects Stack<Value> with top = first field
            var fieldStack = new Stack<Value>();
            for (int i = fields.Length - 1; i >= 0; i--)
                fieldStack.Push(fields[i]);
            var ea = ctx.Store.AllocateExn(tagAddr, fieldStack);
            var exn = ctx.Store[ea];
            var exnRef = new Value(ValType.Exn, exn);
            throw new WasmException(tagAddr, fields, exnRef);
        }

        public static void ThrowRef(Value exnRefVal)
        {
            if (exnRefVal.IsNullRef)
                throw new TrapException("null exception reference");
            var exn = exnRefVal.GcRef as ExnInstance;
            if (exn == null)
                throw new TrapException("invalid exception reference");
            throw new WasmException(exn.Tag, exn.Fields.ToArray(), exnRefVal);
        }

        public static bool TagMatches(ThinContext ctx, WasmException wex, int tagIdx)
        {
            if (ctx.Module == null) return false;
            var expected = ctx.Module.TagAddrs[(TagIdx)tagIdx];
            return wex.Tag.Equals(expected);
        }

        public static Value[] GetFields(WasmException wex)
        {
            return wex.Fields;
        }
    }
}
