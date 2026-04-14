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
using Wacs.Core.Runtime.Types;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Tracks a WASM control flow block during IL emission.
    /// Maps WASM label depths to CIL labels.
    /// </summary>
    internal class EmitBlock
    {
        /// <summary>For blocks: branch here to exit. For loops: branch here to continue.</summary>
        public Label BranchTarget;

        /// <summary>The WASM opcode that opened this block (Block, Loop, If).</summary>
        public WasmOpCode Kind;

        /// <summary>Whether this block is a loop (branch goes backward).</summary>
        public bool IsLoop => Kind == WasmOpCode.Loop;
    }

    /// <summary>
    /// Emits CIL for WebAssembly control flow instructions.
    ///
    /// WASM control flow is structured — blocks, loops, and ifs are nested and
    /// branches can only target enclosing structures. This maps cleanly to CIL:
    ///
    /// - block: forward-branching. br N targets the end label of the Nth enclosing block.
    /// - loop: backward-branching. br N to a loop targets its start label.
    /// - if/else: conditional branch with optional else clause.
    /// - br_table: switch on an index, branching to one of several labels.
    ///
    /// Block parameters/results are not yet handled (Phase 2 focuses on simple
    /// block types — void and single-value). Multi-value block types will be
    /// added when shuttle locals are implemented.
    /// </summary>
    internal static class ControlEmitter
    {
        public static bool CanEmit(WasmOpCode op)
        {
            return op == WasmOpCode.Unreachable
                || op == WasmOpCode.Nop
                || op == WasmOpCode.Block
                || op == WasmOpCode.Loop
                || op == WasmOpCode.If
                || op == WasmOpCode.Else
                || op == WasmOpCode.End
                || op == WasmOpCode.Br
                || op == WasmOpCode.BrIf
                || op == WasmOpCode.BrTable
                || op == WasmOpCode.Return;
        }

        public static void EmitUnreachable(ILGenerator il)
        {
            il.Emit(OpCodes.Ldstr, "wasm trap: unreachable");
            il.Emit(OpCodes.Newobj,
                typeof(TrapException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);
        }

        /// <summary>
        /// Emit a block instruction. Defines an end label that br 0 targets.
        /// Recursively emits the block body.
        /// </summary>
        public static void EmitBlock(
            ILGenerator il,
            InstBlock inst,
            Stack<EmitBlock> blockStack,
            EmitInstructionDelegate emitInstruction)
        {
            var endLabel = il.DefineLabel();
            blockStack.Push(new EmitBlock
            {
                BranchTarget = endLabel,
                Kind = WasmOpCode.Block
            });

            // Emit block body
            var block = inst.GetBlock(0);
            foreach (var child in block.Instructions)
            {
                emitInstruction(il, child);
            }

            blockStack.Pop();
            il.MarkLabel(endLabel);
        }

        /// <summary>
        /// Emit a loop instruction. Defines a start label that br 0 targets (backward jump).
        /// </summary>
        public static void EmitLoop(
            ILGenerator il,
            InstLoop inst,
            Stack<EmitBlock> blockStack,
            EmitInstructionDelegate emitInstruction)
        {
            var startLabel = il.DefineLabel();
            il.MarkLabel(startLabel);

            blockStack.Push(new EmitBlock
            {
                BranchTarget = startLabel,
                Kind = WasmOpCode.Loop
            });

            var block = inst.GetBlock(0);
            foreach (var child in block.Instructions)
            {
                emitInstruction(il, child);
            }

            blockStack.Pop();
        }

        /// <summary>
        /// Emit an if instruction. Pops condition from stack, branches.
        /// </summary>
        public static void EmitIf(
            ILGenerator il,
            InstIf inst,
            Stack<EmitBlock> blockStack,
            EmitInstructionDelegate emitInstruction)
        {
            var endLabel = il.DefineLabel();

            blockStack.Push(new EmitBlock
            {
                BranchTarget = endLabel,
                Kind = WasmOpCode.If
            });

            bool hasElse = inst.Count == 2;

            if (hasElse)
            {
                var elseLabel = il.DefineLabel();

                // condition is on stack
                il.Emit(OpCodes.Brfalse, elseLabel);

                // if-true body
                var ifBlock = inst.GetBlock(0);
                foreach (var child in ifBlock.Instructions)
                {
                    emitInstruction(il, child);
                }
                il.Emit(OpCodes.Br, endLabel);

                // else body
                il.MarkLabel(elseLabel);
                var elseBlock = inst.GetBlock(1);
                foreach (var child in elseBlock.Instructions)
                {
                    emitInstruction(il, child);
                }
            }
            else
            {
                // condition is on stack
                il.Emit(OpCodes.Brfalse, endLabel);

                // if-true body only
                var ifBlock = inst.GetBlock(0);
                foreach (var child in ifBlock.Instructions)
                {
                    emitInstruction(il, child);
                }
            }

            blockStack.Pop();
            il.MarkLabel(endLabel);
        }

        /// <summary>
        /// Emit br — unconditional branch to the Nth enclosing block's target.
        /// </summary>
        public static void EmitBr(ILGenerator il, InstBranch inst, Stack<EmitBlock> blockStack)
        {
            var target = PeekLabel(blockStack, inst.Label);
            il.Emit(OpCodes.Br, target.BranchTarget);
        }

        /// <summary>
        /// Emit br_if — conditional branch. Pops i32 condition from stack.
        /// </summary>
        public static void EmitBrIf(ILGenerator il, InstBranchIf inst, Stack<EmitBlock> blockStack)
        {
            var target = PeekLabel(blockStack, inst.Label);
            il.Emit(OpCodes.Brtrue, target.BranchTarget);
        }

        /// <summary>
        /// Emit br_table — switch on an index. Pops i32 from stack.
        /// CIL switch jumps to labels[index], falls through if out of range.
        /// </summary>
        public static void EmitBrTable(ILGenerator il, InstBranchTable inst, Stack<EmitBlock> blockStack)
        {
            // Build CIL label array from WASM label depths
            var labels = new Label[inst.LabelCount];
            for (int i = 0; i < inst.LabelCount; i++)
            {
                labels[i] = PeekLabel(blockStack, inst.GetLabel(i)).BranchTarget;
            }

            var defaultTarget = PeekLabel(blockStack, inst.DefaultLabel).BranchTarget;

            // CIL switch: jumps to labels[index] if 0 <= index < labels.Length,
            // falls through otherwise — then we branch to default.
            il.Emit(OpCodes.Switch, labels);
            il.Emit(OpCodes.Br, defaultTarget);
        }

        /// <summary>
        /// Look up a label by depth in the block stack.
        /// Depth 0 = innermost (top of stack), depth N = Nth enclosing.
        /// </summary>
        private static EmitBlock PeekLabel(Stack<EmitBlock> blockStack, int depth)
        {
            // Stack enumeration goes top-first, which matches WASM label indexing
            int i = 0;
            foreach (var block in blockStack)
            {
                if (i == depth)
                    return block;
                i++;
            }
            throw new TranspilerException($"Branch label depth {depth} exceeds block stack size {blockStack.Count}");
        }
    }

    /// <summary>
    /// Delegate for recursive instruction emission from within control flow emitters.
    /// </summary>
    internal delegate void EmitInstructionDelegate(ILGenerator il, InstructionBase inst);
}
