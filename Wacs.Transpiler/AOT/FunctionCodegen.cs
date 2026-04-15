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
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Transpiler.AOT.Emitters;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Per-function IL code generator. Walks the instruction sequence
    /// and emits CIL via ILGenerator.
    ///
    /// The emitter grows incrementally — each phase adds support for more
    /// instruction categories. Functions containing unsupported instructions
    /// fall back to the interpreter.
    /// </summary>
    public class FunctionCodegen
    {
        private readonly MethodBuilder _method;
        private readonly FunctionInstance _funcInst;
        private readonly FunctionInstance[] _siblingFunctions;
        private readonly MethodBuilder[] _siblingMethods;
        private readonly int _paramCount;
        private readonly int _importCount;
        private readonly ModuleInstance _moduleInst;
        private readonly GcTypeEmitter _gcTypes;
        private readonly FunctionType[] _allFunctionTypes;

        // Control flow state
        private readonly Stack<EmitBlock> _blockStack = new();
        private LocalBuilder[] _locals = null!;
        private ILGenerator _il = null!;

        public FunctionCodegen(
            MethodBuilder method,
            FunctionInstance funcInst,
            FunctionInstance[] siblingFunctions,
            MethodBuilder[] siblingMethods,
            int importCount,
            GcTypeEmitter gcTypes,
            FunctionType[] allFunctionTypes)
        {
            _method = method;
            _funcInst = funcInst;
            _siblingFunctions = siblingFunctions;
            _siblingMethods = siblingMethods;
            _paramCount = funcInst.Type.ParameterTypes.Arity;
            _importCount = importCount;
            _moduleInst = funcInst.Module;
            _gcTypes = gcTypes;
            _allFunctionTypes = allFunctionTypes;
        }

        /// <summary>
        /// Attempt to emit IL for this function.
        /// Returns true if all instructions were successfully emitted.
        /// Returns false if any instruction is unsupported, signaling fallback.
        /// </summary>
        public bool TryEmit()
        {
            // First pass: check if we can handle every instruction (recursive)
            if (!CanEmitAllInstructions(_funcInst.Body.Instructions))
            {
                // Debug: find first failing instruction
                FindFirstUnsupported(_funcInst.Body.Instructions);
                return false;
            }

            // Second pass: emit IL
            _il = _method.GetILGenerator();

            // Declare CIL locals for WASM locals (parameters are CIL args, not locals)
            var wasmLocals = _funcInst.Locals;
            _locals = new LocalBuilder[wasmLocals.Length];
            for (int i = 0; i < wasmLocals.Length; i++)
            {
                _locals[i] = _il.DeclareLocal(ModuleTranspiler.MapValType(wasmLocals[i]));
            }

            // The function body is an implicit block — br 0 at function level
            // targets the function return. Push a function-level block.
            var funcEndLabel = _il.DefineLabel();
            _blockStack.Push(new EmitBlock
            {
                BranchTarget = funcEndLabel,
                Kind = WasmOpCode.Block // block semantics: branch goes to end
            });

            // Emit instructions
            foreach (var inst in _funcInst.Body.Instructions)
            {
                EmitInstruction(_il, inst);
            }

            _blockStack.Pop();
            _il.MarkLabel(funcEndLabel);

            return true;
        }

        /// <summary>
        /// Emit IL for a single instruction. This is the central dispatch that
        /// is also passed as a delegate to ControlEmitter for recursive emission.
        /// </summary>
        private void EmitInstruction(ILGenerator il, InstructionBase inst)
        {
            var op = inst.Op.x00;

            // Exception handling
            if (ExceptionEmitter.CanEmit(op))
            {
                switch (op)
                {
                    case WasmOpCode.Throw:
                        ExceptionEmitter.EmitThrow(il, (InstThrow)inst, _moduleInst);
                        break;
                    case WasmOpCode.ThrowRef:
                        ExceptionEmitter.EmitThrowRef(il);
                        break;
                    case WasmOpCode.TryTable:
                        ExceptionEmitter.EmitTryTable(il, (InstTryTable)inst, _blockStack,
                            EmitInstruction, _moduleInst);
                        break;
                }
                return;
            }

            // 0xFB prefix (GC: struct, array, ref.test/cast, i31, br_on_cast)
            if (op == WasmOpCode.FB)
            {
                // Pass block stack resolver for br_on_cast/fail
                Func<int, System.Reflection.Emit.Label> branchResolver = depth =>
                {
                    int i = 0;
                    foreach (var block in _blockStack)
                    {
                        if (i == depth) return block.BranchTarget;
                        i++;
                    }
                    throw new TranspilerException($"br_on_cast label depth {depth} exceeds block stack");
                };
                GcEmitter.Emit(il, inst, inst.Op.xFB, _gcTypes, _moduleInst, branchResolver);
                return;
            }

            // 0xFC prefix (extensions: sat truncation, bulk memory)
            if (op == WasmOpCode.FC)
            {
                ExtEmitter.Emit(il, inst, inst.Op.xFC);
                return;
            }

            // Other multi-byte prefix opcodes (not yet supported)
            if (op == WasmOpCode.FD || op == WasmOpCode.FE)
            {
                throw new TranspilerException(
                    $"FunctionCodegen: prefix opcode {inst.Op.GetMnemonic()} should have been caught by CanEmit");
            }

            // Numeric instructions (constants, arithmetic, comparisons, conversions)
            if (NumericEmitter.CanEmit(op))
            {
                NumericEmitter.Emit(il, inst, op);
                return;
            }

            // Variable/parametric instructions (locals, drop, select)
            if (VariableEmitter.CanEmit(op))
            {
                VariableEmitter.Emit(il, inst, op, _paramCount, _locals);
                return;
            }

            // Global access
            if (GlobalEmitter.CanEmit(op))
            {
                switch (op)
                {
                    case WasmOpCode.GlobalGet:
                    {
                        var getInst = (InstGlobalGet)inst;
                        var globalType = ResolveGlobalType(getInst.GetIndex());
                        GlobalEmitter.EmitGlobalGet(il, getInst, globalType);
                        return;
                    }
                    case WasmOpCode.GlobalSet:
                    {
                        var setInst = (InstGlobalSet)inst;
                        var globalType = ResolveGlobalType(setInst.GetIndex());
                        GlobalEmitter.EmitGlobalSet(il, setInst, globalType);
                        return;
                    }
                }
            }

            // Memory operations
            if (MemoryEmitter.CanEmit(op))
            {
                MemoryEmitter.Emit(il, inst, op);
                return;
            }

            // Table and reference instructions
            if (TableRefEmitter.CanEmit(op))
            {
                TableRefEmitter.Emit(il, inst, op);
                return;
            }

            // Function calls — resolve strategy then emit
            if (CallEmitter.CanEmit(op))
            {
                var site = CallEmitter.ResolveCallSite(
                    inst, op, _siblingFunctions, _importCount, _moduleInst, _allFunctionTypes);
                CallEmitter.EmitCallSite(il, site, _siblingMethods, _moduleInst);
                return;
            }

            // Control flow
            switch (op)
            {
                case WasmOpCode.Unreachable:
                    ControlEmitter.EmitUnreachable(il);
                    break;

                case WasmOpCode.Nop:
                    il.Emit(OpCodes.Nop);
                    break;

                case WasmOpCode.Block:
                    ControlEmitter.EmitBlock(il, (InstBlock)inst, _blockStack, EmitInstruction);
                    break;

                case WasmOpCode.Loop:
                    ControlEmitter.EmitLoop(il, (InstLoop)inst, _blockStack, EmitInstruction);
                    break;

                case WasmOpCode.If:
                    ControlEmitter.EmitIf(il, (InstIf)inst, _blockStack, EmitInstruction);
                    break;

                case WasmOpCode.Else:
                    // Handled inside EmitIf — should not appear at top level
                    break;

                case WasmOpCode.End:
                    if (inst is InstEnd endInst && endInst.FunctionEnd)
                    {
                        EmitFunctionReturn(il);
                    }
                    // Non-function End is handled by the block/loop/if emitters
                    break;

                case WasmOpCode.Return:
                    EmitFunctionReturn(il);
                    break;

                case WasmOpCode.Br:
                    ControlEmitter.EmitBr(il, (InstBranch)inst, _blockStack);
                    break;

                case WasmOpCode.BrIf:
                    ControlEmitter.EmitBrIf(il, (InstBranchIf)inst, _blockStack);
                    break;

                case WasmOpCode.BrTable:
                    ControlEmitter.EmitBrTable(il, (InstBranchTable)inst, _blockStack);
                    break;

                case WasmOpCode.BrOnNull:
                {
                    // br_on_null L: pop ref, if null branch to L (ref consumed), else push ref back
                    var brInst = (Wacs.Core.Instructions.Reference.InstBrOnNull)inst;
                    int depth = brInst.Label;
                    int i = 0;
                    EmitBlock target = null!;
                    foreach (var block in _blockStack)
                    {
                        if (i == depth) { target = block; break; }
                        i++;
                    }
                    // Stack: [Value ref]
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Call, typeof(Emitters.TableRefHelpers).GetMethod(
                        nameof(Emitters.TableRefHelpers.RefIsNull),
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!);
                    var skipLabel = il.DefineLabel();
                    il.Emit(OpCodes.Brfalse, skipLabel); // not null → skip
                    il.Emit(OpCodes.Pop); // consume ref (null case)
                    il.Emit(OpCodes.Br, target.BranchTarget);
                    il.MarkLabel(skipLabel);
                    break;
                }

                case WasmOpCode.BrOnNonNull:
                {
                    // br_on_non_null L: pop ref, if non-null branch to L (ref on stack), else consume
                    var brInst = (Wacs.Core.Instructions.Reference.InstBrOnNonNull)inst;
                    int depth = brInst.Label;
                    int i = 0;
                    EmitBlock target = null!;
                    foreach (var block in _blockStack)
                    {
                        if (i == depth) { target = block; break; }
                        i++;
                    }
                    // Stack: [Value ref]
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Call, typeof(Emitters.TableRefHelpers).GetMethod(
                        nameof(Emitters.TableRefHelpers.RefIsNull),
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!);
                    var skipLabel = il.DefineLabel();
                    il.Emit(OpCodes.Brtrue, skipLabel); // null → skip
                    il.Emit(OpCodes.Br, target.BranchTarget); // non-null → branch (ref stays)
                    il.MarkLabel(skipLabel);
                    il.Emit(OpCodes.Pop); // consume ref (null case, no branch)
                    break;
                }

                default:
                    throw new TranspilerException(
                        $"FunctionCodegen: no emitter for opcode {inst.Op.GetMnemonic()} (0x{(byte)op:X2})");
            }
        }

        /// <summary>
        /// Recursively check if we have emitters for every instruction.
        /// </summary>
        private bool CanEmitAllInstructions(IEnumerable<InstructionBase> instructions)
        {
            foreach (var inst in instructions)
            {
                if (!HasEmitter(inst))
                    return false;

                // Recursively check nested blocks
                if (inst is IBlockInstruction blockInst)
                {
                    for (int i = 0; i < blockInst.Count; i++)
                    {
                        var block = blockInst.GetBlock(i);
                        if (!CanEmitAllInstructions(block.Instructions))
                            return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Check if we have an IL emitter for the given instruction.
        /// </summary>
        /// <summary>
        /// Emit return sequence for the function.
        /// For 0 results: just ret.
        /// For 1 result: value is on CIL stack, ret.
        /// For N results: CIL stack has [r0, r1, ..., rN-1].
        ///   Store r1..rN-1 to out params (reverse order), leave r0 for ret.
        /// </summary>
        private void EmitFunctionReturn(ILGenerator il)
        {
            var resultTypes = _funcInst.Type.ResultType.Types;
            int resultCount = resultTypes.Length;

            if (resultCount <= 1)
            {
                il.Emit(OpCodes.Ret);
                return;
            }

            // CIL stack has [r0, r1, ..., rN-1] (r0 deepest, rN-1 on top)
            // Need to store r1..rN-1 into out params, leave r0 for ret.
            // Out param for result index i (1-based) is at CIL arg: 1 + _paramCount + (i-1)

            // Spill all results to temps (reverse order — top of stack first)
            var temps = new LocalBuilder[resultCount];
            for (int i = resultCount - 1; i >= 0; i--)
            {
                temps[i] = il.DeclareLocal(ModuleTranspiler.MapValType(resultTypes[i]));
                il.Emit(OpCodes.Stloc, temps[i]);
            }

            // Store results 1..N-1 to out params via stind
            for (int i = 1; i < resultCount; i++)
            {
                int outArgIdx = 1 + _paramCount + (i - 1); // CIL arg index for out param
                il.Emit(OpCodes.Ldarg, outArgIdx);  // load out param address
                il.Emit(OpCodes.Ldloc, temps[i]);    // load result value
                EmitStind(il, resultTypes[i]);        // store indirect
            }

            // Push result 0 back for ret
            il.Emit(OpCodes.Ldloc, temps[0]);
            il.Emit(OpCodes.Ret);
        }

        private static void EmitStind(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32: il.Emit(OpCodes.Stind_I4); break;
                case ValType.I64: il.Emit(OpCodes.Stind_I8); break;
                case ValType.F32: il.Emit(OpCodes.Stind_R4); break;
                case ValType.F64: il.Emit(OpCodes.Stind_R8); break;
                default: il.Emit(OpCodes.Stobj, typeof(Value)); break; // ref types
            }
        }

        /// <summary>
        /// Debug: find and store the first unsupported instruction's mnemonic.
        /// </summary>
        public string? LastRejectionReason { get; private set; }

        private void FindFirstUnsupported(IEnumerable<InstructionBase> instructions)
        {
            foreach (var inst in instructions)
            {
                if (!HasEmitter(inst))
                {
                    LastRejectionReason = $"{inst.Op.GetMnemonic()} (0x{(byte)inst.Op.x00:X2})";
                    return;
                }
                if (inst is IBlockInstruction blockInst)
                {
                    for (int i = 0; i < blockInst.Count; i++)
                    {
                        FindFirstUnsupported(blockInst.GetBlock(i).Instructions);
                        if (LastRejectionReason != null) return;
                    }
                }
            }
        }

        private bool HasEmitter(InstructionBase inst)
        {
            var op = inst.Op.x00;

            // 0xFB prefix (GC)
            if (op == WasmOpCode.FB)
                return GcEmitter.CanEmit(inst.Op.xFB, _gcTypes);

            // 0xFC prefix — check extended opcode
            if (op == WasmOpCode.FC)
                return ExtEmitter.CanEmit(inst.Op.xFC);

            // Other multi-byte opcodes — not yet supported
            if (op == WasmOpCode.FD || op == WasmOpCode.FE)
                return false;

            // Numeric instructions (0x41-0xC4)
            if (NumericEmitter.CanEmit(op))
                return true;

            // Variable/parametric instructions
            if (VariableEmitter.CanEmit(op))
                return true;

            // Control flow (including br_on_null/non_null)
            if (ControlEmitter.CanEmit(op))
                return true;
            if (op == WasmOpCode.BrOnNull || op == WasmOpCode.BrOnNonNull)
                return true;

            // Exception handling
            if (ExceptionEmitter.CanEmit(op))
                return true;

            // Memory operations
            if (MemoryEmitter.CanEmit(op))
                return true;

            // Table and reference instructions
            if (TableRefEmitter.CanEmit(op))
                return true;

            // Global access
            if (GlobalEmitter.CanEmit(op))
            {
                int globalIdx = op == WasmOpCode.GlobalGet
                    ? ((InstGlobalGet)inst).GetIndex()
                    : ((InstGlobalSet)inst).GetIndex();
                try
                {
                    var gtype = ResolveGlobalType(globalIdx);
                    // Support numeric types and ref types (as Value)
                    // V128 globals deferred to SIMD phase
                    return gtype != ValType.V128;
                }
                catch
                {
                    return false;
                }
            }

            // All call variants
            if (CallEmitter.CanEmit(op))
                return true;

            return false;
        }

        /// <summary>
        /// Look up the content type of a global by its index in the module.
        /// Global index space: imported globals first, then locally-defined.
        /// </summary>
        private ValType ResolveGlobalType(int globalIdx)
        {
            var module = _moduleInst.Repr;

            // Count imported globals
            int importedGlobalCount = 0;
            foreach (var import in module.Imports)
            {
                if (import.Desc is Wacs.Core.Module.ImportDesc.GlobalDesc)
                    importedGlobalCount++;
            }

            if (globalIdx < importedGlobalCount)
            {
                // Imported global — get type from import desc
                int gi = 0;
                foreach (var import in module.Imports)
                {
                    if (import.Desc is Wacs.Core.Module.ImportDesc.GlobalDesc gd)
                    {
                        if (gi == globalIdx)
                            return gd.GlobalDef.ContentType;
                        gi++;
                    }
                }
            }

            // Locally-defined global
            int localIdx = globalIdx - importedGlobalCount;
            return module.Globals[localIdx].Type.ContentType;
        }
    }
}
