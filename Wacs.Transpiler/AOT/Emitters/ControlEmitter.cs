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

        /// <summary>
        /// CIL stack height at block entry. Used to calculate how many excess
        /// values need to be popped when br/br_if targets this block.
        /// For blocks: label arity is the result count (0 for void blocks).
        /// For loops: label arity is the param count (0 for simple loops).
        /// </summary>
        public int StackHeight;

        /// <summary>Number of values the label expects (result arity for blocks, param arity for loops).</summary>
        public int LabelArity;

        /// <summary>CLR types of the label's result values. Used to create correctly-typed
        /// shuttle locals when br_if needs to rearrange stack values.</summary>
        public Type[] ResultClrTypes = System.Array.Empty<Type>();
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
            EmitInstructionDelegate emitInstruction,
            int stackHeight = 0)
        {
            var endLabel = il.DefineLabel();
            blockStack.Push(new EmitBlock
            {
                BranchTarget = endLabel,
                Kind = WasmOpCode.Block,
                StackHeight = stackHeight,
                LabelArity = BlockResultArity(inst.BlockType),
                ResultClrTypes = BlockResultClrTypes(inst.BlockType)
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
            EmitInstructionDelegate emitInstruction,
            int stackHeight = 0)
        {
            var startLabel = il.DefineLabel();
            il.MarkLabel(startLabel);

            blockStack.Push(new EmitBlock
            {
                BranchTarget = startLabel,
                Kind = WasmOpCode.Loop,
                StackHeight = stackHeight,
                LabelArity = 0 // Loop labels have param arity (0 for simple loops)
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
            EmitInstructionDelegate emitInstruction,
            int stackHeight = 0)
        {
            var endLabel = il.DefineLabel();

            blockStack.Push(new EmitBlock
            {
                BranchTarget = endLabel,
                Kind = WasmOpCode.If,
                StackHeight = stackHeight,
                LabelArity = BlockResultArity(inst.BlockType),
                ResultClrTypes = BlockResultClrTypes(inst.BlockType)
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
        /// <summary>
        /// Emit br_if — conditional branch. Pops i32 condition from stack.
        /// Uses precomputed excess from StackAnalysis.
        ///
        /// When excess > 0, the branch path must: save condition, check,
        /// shuttle carried values past excess, branch. Fall-through is unchanged.
        /// </summary>
        public static void EmitBrIf(ILGenerator il, InstBranchIf inst,
            Stack<EmitBlock> blockStack, int excess)
        {
            var target = PeekLabel(blockStack, inst.Label);

            if (excess > 0)
            {
                // Save condition, conditionally rearrange stack on branch path.
                // CIL stack (top→bottom): [condition, carried..., excess..., label_stack...]
                var condLocal = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Stloc, condLocal);

                var fallThrough = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, condLocal);
                il.Emit(OpCodes.Brfalse, fallThrough);

                // Branch path: shuttle carried values past excess
                int arity = target.LabelArity;
                var carriedLocals = new LocalBuilder[arity];
                for (int i = 0; i < arity; i++)
                {
                    var clrType = i < target.ResultClrTypes.Length
                        ? target.ResultClrTypes[i] : typeof(int);
                    carriedLocals[i] = il.DeclareLocal(clrType);
                    il.Emit(OpCodes.Stloc, carriedLocals[i]);
                }
                for (int i = 0; i < excess; i++)
                    il.Emit(OpCodes.Pop);
                for (int i = arity - 1; i >= 0; i--)
                    il.Emit(OpCodes.Ldloc, carriedLocals[i]);

                il.Emit(OpCodes.Br, target.BranchTarget);
                il.MarkLabel(fallThrough);
                return;
            }

            il.Emit(OpCodes.Brtrue, target.BranchTarget);
        }

        /// <summary>
        /// Emit br_table — switch on an index. Pops i32 from stack.
        /// CIL switch jumps to labels[index], falls through if out of range.
        /// Uses precomputed excess from StackAnalysis.
        /// </summary>
        public static void EmitBrTable(ILGenerator il, InstBranchTable inst,
            Stack<EmitBlock> blockStack, int excess = 0)
        {
            if (excess > 0)
            {
                // Need to clean up excess values before branching.
                // Save index, save carried values, pop excess, restore carried,
                // then switch. All targets have the same arity (spec requirement).
                var target = PeekLabel(blockStack, inst.DefaultLabel);
                var indexLocal = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Stloc, indexLocal);

                int arity = target.LabelArity;
                var carriedLocals = new LocalBuilder[arity];
                for (int i = 0; i < arity; i++)
                {
                    var clrType = i < target.ResultClrTypes.Length
                        ? target.ResultClrTypes[i] : typeof(int);
                    carriedLocals[i] = il.DeclareLocal(clrType);
                    il.Emit(OpCodes.Stloc, carriedLocals[i]);
                }
                for (int i = 0; i < excess; i++)
                    il.Emit(OpCodes.Pop);
                for (int i = arity - 1; i >= 0; i--)
                    il.Emit(OpCodes.Ldloc, carriedLocals[i]);

                il.Emit(OpCodes.Ldloc, indexLocal);
            }

            var labels = new Label[inst.LabelCount];
            for (int i = 0; i < inst.LabelCount; i++)
                labels[i] = PeekLabel(blockStack, inst.GetLabel(i)).BranchTarget;

            var defaultTarget = PeekLabel(blockStack, inst.DefaultLabel).BranchTarget;

            il.Emit(OpCodes.Switch, labels);
            il.Emit(OpCodes.Br, defaultTarget);
        }

        /// <summary>
        /// Get the result arity for a block type.
        /// Empty = 0, simple value type = 1.
        /// </summary>
        private static int BlockResultArity(Wacs.Core.Types.Defs.ValType blockType)
        {
            if (blockType == Wacs.Core.Types.Defs.ValType.Empty) return 0;
            // Any non-empty single value type = 1 result
            return 1;
        }

        /// <summary>
        /// Get the CLR types for the block's result values.
        /// Used by EmitBrIf to create correctly-typed shuttle locals.
        /// </summary>
        private static Type[] BlockResultClrTypes(Wacs.Core.Types.Defs.ValType blockType)
        {
            if (blockType == Wacs.Core.Types.Defs.ValType.Empty) return System.Array.Empty<Type>();
            return new[] { ModuleTranspiler.MapValType(blockType) };
        }

        /// <summary>
        /// Look up a label by depth in the block stack.
        /// Depth 0 = innermost (top of stack), depth N = Nth enclosing.
        /// </summary>
        internal static EmitBlock PeekLabel(Stack<EmitBlock> blockStack, int depth)
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
