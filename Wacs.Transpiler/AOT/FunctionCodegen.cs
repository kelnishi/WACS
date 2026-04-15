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

using System.Collections.Generic;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
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

        // Control flow state
        private readonly Stack<EmitBlock> _blockStack = new();
        private LocalBuilder[] _locals = null!;
        private ILGenerator _il = null!;

        public FunctionCodegen(
            MethodBuilder method,
            FunctionInstance funcInst,
            FunctionInstance[] siblingFunctions,
            MethodBuilder[] siblingMethods,
            int importCount)
        {
            _method = method;
            _funcInst = funcInst;
            _siblingFunctions = siblingFunctions;
            _siblingMethods = siblingMethods;
            _paramCount = funcInst.Type.ParameterTypes.Arity;
            _importCount = importCount;
            _moduleInst = funcInst.Module;
        }

        /// <summary>
        /// Attempt to emit IL for this function.
        /// Returns true if all instructions were successfully emitted.
        /// Returns false if any instruction is unsupported, signaling fallback.
        /// </summary>
        public bool TryEmit()
        {
            // Multi-value returns not yet supported
            if (_funcInst.Type.ResultType.Arity > 1)
                return false;

            // First pass: check if we can handle every instruction (recursive)
            if (!CanEmitAllInstructions(_funcInst.Body.Instructions))
                return false;

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

            // 0xFC prefix (extensions: sat truncation, bulk memory)
            if (op == WasmOpCode.FC)
            {
                ExtEmitter.Emit(il, inst, inst.Op.xFC);
                return;
            }

            // Other multi-byte prefix opcodes (not yet supported)
            if (op == WasmOpCode.FB || op == WasmOpCode.FD || op == WasmOpCode.FE)
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

            // Function calls
            if (CallEmitter.CanEmit(op))
            {
                switch (op)
                {
                    case WasmOpCode.Call:
                        CallEmitter.EmitCall(il, (InstCall)inst, _siblingFunctions, _siblingMethods, _importCount);
                        break;
                    case WasmOpCode.CallIndirect:
                        CallEmitter.EmitCallIndirect(il, (InstCallIndirect)inst, _moduleInst);
                        break;
                    case WasmOpCode.ReturnCall:
                        CallEmitter.EmitReturnCall(il, (InstReturnCall)inst, _siblingFunctions, _siblingMethods, _importCount);
                        break;
                    case WasmOpCode.ReturnCallIndirect:
                        CallEmitter.EmitReturnCallIndirect(il, (InstReturnCallIndirect)inst, _moduleInst);
                        break;
                }
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
                        il.Emit(OpCodes.Ret);
                    }
                    // Non-function End is handled by the block/loop/if emitters
                    break;

                case WasmOpCode.Return:
                    il.Emit(OpCodes.Ret);
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
        private bool HasEmitter(InstructionBase inst)
        {
            var op = inst.Op.x00;

            // 0xFC prefix — check extended opcode
            if (op == WasmOpCode.FC)
                return ExtEmitter.CanEmit(inst.Op.xFC);

            // Other multi-byte opcodes — not yet supported
            if (op == WasmOpCode.FB || op == WasmOpCode.FD || op == WasmOpCode.FE)
                return false;

            // Numeric instructions (0x41-0xC4)
            if (NumericEmitter.CanEmit(op))
                return true;

            // Variable/parametric instructions
            if (VariableEmitter.CanEmit(op))
                return true;

            // Control flow
            if (ControlEmitter.CanEmit(op))
                return true;

            // Memory operations
            if (MemoryEmitter.CanEmit(op))
                return true;

            // Table and reference instructions
            if (TableRefEmitter.CanEmit(op))
                return true;

            // Global access (numeric types only for now)
            if (GlobalEmitter.CanEmit(op))
            {
                int globalIdx = op == WasmOpCode.GlobalGet
                    ? ((InstGlobalGet)inst).GetIndex()
                    : ((InstGlobalSet)inst).GetIndex();
                try
                {
                    var gtype = ResolveGlobalType(globalIdx);
                    return gtype == ValType.I32 || gtype == ValType.I64 ||
                           gtype == ValType.F32 || gtype == ValType.F64;
                }
                catch
                {
                    return false;
                }
            }

            // Calls
            if (op == WasmOpCode.Call)
            {
                int funcIdx = (int)((InstCall)inst).X.Value;
                return funcIdx >= _importCount; // intra-module only for now
            }
            if (op == WasmOpCode.CallIndirect || op == WasmOpCode.ReturnCallIndirect)
                return true;
            if (op == WasmOpCode.ReturnCall)
            {
                int funcIdx = (int)((InstReturnCall)inst).X.Value;
                return funcIdx >= _importCount;
            }

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
