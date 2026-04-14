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
        private readonly MethodBuilder[] _siblingMethods;
        private readonly int _paramCount;

        // Control flow state
        private readonly Stack<EmitBlock> _blockStack = new();
        private LocalBuilder[] _locals = null!;
        private ILGenerator _il = null!;

        public FunctionCodegen(
            MethodBuilder method,
            FunctionInstance funcInst,
            MethodBuilder[] siblingMethods)
        {
            _method = method;
            _funcInst = funcInst;
            _siblingMethods = siblingMethods;
            _paramCount = funcInst.Type.ParameterTypes.Arity;
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

            // Check for multi-byte prefix opcodes (not yet supported)
            if (op == WasmOpCode.FB || op == WasmOpCode.FC ||
                op == WasmOpCode.FD || op == WasmOpCode.FE)
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
                if (!HasEmitter(inst.Op))
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
        /// Check if we have an IL emitter for the given bytecode.
        /// </summary>
        private bool HasEmitter(ByteCode bc)
        {
            var op = bc.x00;

            // Multi-byte opcodes (FB/FC/FD/FE prefix) — not yet supported
            if (op == WasmOpCode.FB || op == WasmOpCode.FC ||
                op == WasmOpCode.FD || op == WasmOpCode.FE)
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

            return false;
        }
    }
}
