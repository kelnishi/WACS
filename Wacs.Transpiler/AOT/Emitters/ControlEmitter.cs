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
using System.Linq;
using System.Reflection.Emit;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
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

        /// <summary>
        /// Result locals for labels carrying Value types. When non-null, all paths
        /// reaching BranchTarget must store their carried values here BEFORE Br,
        /// and the code after MarkLabel(BranchTarget) loads from these locals.
        ///
        /// The CLR verifier rejects programs where Value structs (containing managed
        /// reference fields like IGcRef?) sit on the CIL evaluation stack at a label
        /// merge point. Shuttling through locals guarantees the stack is empty at
        /// the label merge, sidestepping the restriction.
        ///
        /// Length matches LabelArity. Null for labels that don't carry Value results
        /// (scalar-only blocks, void blocks).
        /// </summary>
        public LocalBuilder[]? ResultLocals;

        /// <summary>
        /// The CLR try-region nesting depth at the point this block was pushed.
        /// Branches from a deeper try-depth to this block's BranchTarget must
        /// emit `Leave` instead of `Br` (doc 2 §14). When the current emit
        /// try-depth equals this value, `Br` is sufficient.
        /// </summary>
        public int OpeningTryDepth;
    }

    /// <summary>
    /// Helpers for emitting branches that respect CLR try-region rules.
    /// Doc 2 §14: when a branch's source lies inside a try block (or catch
    /// handler) but the target lies outside, the CLR requires `Leave` rather
    /// than `Br`. <see cref="EmitBlock.OpeningTryDepth"/> records the depth
    /// at which the target was introduced; <paramref name="currentTryDepth"/>
    /// is the depth where the branch is emitted.
    ///
    /// Known limitation: `Leave` empties the eval stack. Branches that carry
    /// values directly on the stack (labels without <see cref="EmitBlock.ResultLocals"/>)
    /// are not correct through `Leave`. Such a case requires shuttle locals
    /// — a follow-up pass will force shuttle allocation for cross-try targets.
    /// </summary>
    internal static class BranchBridge
    {
        public static OpCode BranchOpFor(EmitBlock target, int currentTryDepth)
            => currentTryDepth > target.OpeningTryDepth ? OpCodes.Leave : OpCodes.Br;

        public static void EmitBranch(ILGenerator il, EmitBlock target, int currentTryDepth)
            => il.Emit(BranchOpFor(target, currentTryDepth), target.BranchTarget);
    }

    internal static class LabelShuttle
    {
        /// <summary>
        /// Should this label carry its values through locals instead of the eval stack?
        /// True when any carried type is Value (ref types, V128) — the CLR verifier
        /// rejects such structs at label merge points.
        /// </summary>
        public static bool NeedsLocals(Type[] carriedTypes)
        {
            foreach (var t in carriedTypes)
                if (t == typeof(Wacs.Core.Runtime.Value)) return true;
            return false;
        }

        /// <summary>
        /// Emit the branch-to-label sequence: store stack values to target's
        /// ResultLocals (if any), then Br to the target label.
        /// Handles the case where ResultLocals is null (Br with values on stack).
        /// </summary>
        public static void EmitBranchToLabel(ILGenerator il, EmitBlock target)
        {
            if (target.ResultLocals != null)
            {
                // Store carried values to result locals (reverse — top of stack first)
                for (int i = target.LabelArity - 1; i >= 0; i--)
                    il.Emit(OpCodes.Stloc, target.ResultLocals[i]);
            }
            il.Emit(OpCodes.Br, target.BranchTarget);
        }
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
            int stackHeight = 0,
            ModuleInstance? moduleInst = null,
            int tryDepth = 0,
            bool forceShuttle = false)
        {
            var (paramArity, resultArity, _, resultClrTypes) =
                moduleInst != null
                    ? ResolveBlockArities(inst.BlockType, moduleInst)
                    : (0, inst.BlockType == ValType.Empty ? 0 : 1,
                       Array.Empty<Type>(),
                       inst.BlockType == ValType.Empty ? Array.Empty<Type>() : new[] { ModuleTranspiler.MapValTypeInternal(inst.BlockType) });

            var endLabel = il.DefineLabel();

            // Allocate result locals for labels carrying Value types OR when
            // the caller forces shuttle (a try_table elsewhere in the function;
            // cross-try branches emit Leave which empties the eval stack —
            // locals guarantee rendezvous regardless, per doc 2 §14).
            LocalBuilder[]? resultLocals = null;
            if (resultArity > 0 && (LabelShuttle.NeedsLocals(resultClrTypes) || forceShuttle))
            {
                resultLocals = new LocalBuilder[resultArity];
                for (int i = 0; i < resultArity; i++)
                    resultLocals[i] = il.DeclareLocal(resultClrTypes[i]);
            }

            blockStack.Push(new EmitBlock
            {
                BranchTarget = endLabel,
                Kind = WasmOpCode.Block,
                StackHeight = stackHeight - paramArity,
                LabelArity = resultArity,
                ResultClrTypes = resultClrTypes,
                ResultLocals = resultLocals,
                OpeningTryDepth = tryDepth
            });

            // Emit block body
            var block = inst.GetBlock(0);
            foreach (var child in block.Instructions)
            {
                emitInstruction(il, child);
            }
            bool bodyEndReachable = BodyEndIsReachable(block.Instructions);

            blockStack.Pop();

            // When resultLocals is used, the label merge must only see EMPTY eval stack.
            // If the body's natural end is reachable with Value results on stack,
            // shuttle them into locals and Br to the label. Otherwise skip (dead code).
            if (resultLocals != null && bodyEndReachable)
            {
                for (int i = resultArity - 1; i >= 0; i--)
                    il.Emit(OpCodes.Stloc, resultLocals[i]);
                il.Emit(OpCodes.Br, endLabel);
            }

            il.MarkLabel(endLabel);

            // After the label: load result locals onto the stack.
            if (resultLocals != null)
            {
                for (int i = 0; i < resultArity; i++)
                    il.Emit(OpCodes.Ldloc, resultLocals[i]);
            }
        }

        /// <summary>
        /// Is this an unconditional terminator that ends the current path?
        /// Determines whether emission should continue after this instruction.
        /// Skips over structural markers like End which don't terminate execution.
        /// </summary>
        private static bool IsUnconditionalTerminator(InstructionBase inst)
        {
            var op = inst.Op.x00;
            if (op == WasmOpCode.Return || op == WasmOpCode.Br
                || op == WasmOpCode.Unreachable)
                return true;
            // return_call* opcodes are in the tail call family — they don't return
            if (op == WasmOpCode.ReturnCall || op == WasmOpCode.ReturnCallIndirect
                || op == WasmOpCode.ReturnCallRef)
                return true;
            return false;
        }

        /// <summary>
        /// Does the block body's natural fall-through reach its end? A body where
        /// the last non-end instruction is an unconditional terminator has no
        /// reachable fall-through at the closing brace.
        /// </summary>
        private static bool BodyEndIsReachable(Wacs.Core.InstructionSequence seq)
        {
            for (int i = seq.Count - 1; i >= 0; i--)
            {
                var inst = seq[i]!;
                var op = inst.Op.x00;
                // Skip structural markers that don't execute
                if (op == WasmOpCode.End || op == WasmOpCode.Else)
                    continue;
                return !IsUnconditionalTerminator(inst);
            }
            return true; // empty body — trivially reachable
        }

        /// <summary>
        /// Emit a loop instruction. Defines a start label that br 0 targets (backward jump).
        /// </summary>
        public static void EmitLoop(
            ILGenerator il,
            InstLoop inst,
            Stack<EmitBlock> blockStack,
            EmitInstructionDelegate emitInstruction,
            int stackHeight = 0,
            ModuleInstance? moduleInst = null,
            int tryDepth = 0)
        {
            var (paramArity, _, paramClrTypes, _) =
                moduleInst != null
                    ? ResolveBlockArities(inst.BlockType, moduleInst)
                    : (0, 0, Array.Empty<Type>(), Array.Empty<Type>());

            var startLabel = il.DefineLabel();
            il.MarkLabel(startLabel);

            blockStack.Push(new EmitBlock
            {
                BranchTarget = startLabel,
                Kind = WasmOpCode.Loop,
                StackHeight = stackHeight - paramArity,
                LabelArity = paramArity, // Loop labels carry params on backward branch
                ResultClrTypes = paramClrTypes,
                OpeningTryDepth = tryDepth
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
            int stackHeight = 0,
            ModuleInstance? moduleInst = null,
            int tryDepth = 0,
            bool forceShuttle = false)
        {
            var (paramArity, resultArity, _, resultClrTypes) =
                moduleInst != null
                    ? ResolveBlockArities(inst.BlockType, moduleInst)
                    : (0, inst.BlockType == ValType.Empty ? 0 : 1,
                       Array.Empty<Type>(),
                       inst.BlockType == ValType.Empty ? Array.Empty<Type>() : new[] { ModuleTranspiler.MapValTypeInternal(inst.BlockType) });

            var endLabel = il.DefineLabel();

            // Allocate result locals for Value-carrying labels OR when the
            // caller forces shuttle (see EmitBlock for rationale).
            LocalBuilder[]? resultLocals = null;
            if (resultArity > 0 && (LabelShuttle.NeedsLocals(resultClrTypes) || forceShuttle))
            {
                resultLocals = new LocalBuilder[resultArity];
                for (int i = 0; i < resultArity; i++)
                    resultLocals[i] = il.DeclareLocal(resultClrTypes[i]);
            }

            blockStack.Push(new EmitBlock
            {
                BranchTarget = endLabel,
                Kind = WasmOpCode.If,
                StackHeight = stackHeight - paramArity,
                LabelArity = resultArity,
                ResultClrTypes = resultClrTypes,
                ResultLocals = resultLocals,
                OpeningTryDepth = tryDepth
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
                    emitInstruction(il, child);
                bool ifEndReachable = BodyEndIsReachable(ifBlock.Instructions);

                // End of if-true body: shuttle to locals if reachable, then Br
                if (ifEndReachable)
                {
                    if (resultLocals != null)
                    {
                        for (int i = resultArity - 1; i >= 0; i--)
                            il.Emit(OpCodes.Stloc, resultLocals[i]);
                    }
                    il.Emit(OpCodes.Br, endLabel);
                }

                // else body
                il.MarkLabel(elseLabel);
                var elseBlock = inst.GetBlock(1);
                foreach (var child in elseBlock.Instructions)
                    emitInstruction(il, child);
                bool elseEndReachable = BodyEndIsReachable(elseBlock.Instructions);

                // End of else body: shuttle to locals if reachable (fall-through to endLabel)
                if (resultLocals != null && elseEndReachable)
                {
                    for (int i = resultArity - 1; i >= 0; i--)
                        il.Emit(OpCodes.Stloc, resultLocals[i]);
                    il.Emit(OpCodes.Br, endLabel);
                }
            }
            else
            {
                // condition is on stack — no results possible on false path (void if)
                il.Emit(OpCodes.Brfalse, endLabel);

                // if-true body only
                var ifBlock = inst.GetBlock(0);
                foreach (var child in ifBlock.Instructions)
                    emitInstruction(il, child);
                bool ifEndReachable = BodyEndIsReachable(ifBlock.Instructions);

                // End of if-true body: shuttle to locals if reachable
                if (resultLocals != null && ifEndReachable)
                {
                    for (int i = resultArity - 1; i >= 0; i--)
                        il.Emit(OpCodes.Stloc, resultLocals[i]);
                    il.Emit(OpCodes.Br, endLabel);
                }
            }

            blockStack.Pop();
            il.MarkLabel(endLabel);

            // After the label: load result locals onto stack
            if (resultLocals != null)
            {
                for (int i = 0; i < resultArity; i++)
                    il.Emit(OpCodes.Ldloc, resultLocals[i]);
            }
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

            // When target has ResultLocals, the label merge expects an EMPTY eval stack
            // (values loaded by the label owner). We must store carried values to the
            // target's ResultLocals before Br, not leave them on the stack.
            bool useTargetLocals = target.ResultLocals != null;
            int arity = target.LabelArity;

            if (excess > 0 || useTargetLocals)
            {
                // CIL stack (top→bottom): [condition, carried..., excess..., label_stack...]
                var condLocal = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Stloc, condLocal);

                var fallThrough = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, condLocal);
                il.Emit(OpCodes.Brfalse, fallThrough);

                // Branch path: shuttle carried values to target locals (if any), pop excess, branch.
                if (useTargetLocals)
                {
                    for (int i = arity - 1; i >= 0; i--)
                        il.Emit(OpCodes.Stloc, target.ResultLocals![i]);
                }
                else
                {
                    // Use temp locals to preserve carried values past the excess pop
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
                }

                // If useTargetLocals and excess > 0, pop the excess after storing carried.
                if (useTargetLocals)
                {
                    for (int i = 0; i < excess; i++)
                        il.Emit(OpCodes.Pop);
                }

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
        ///
        /// When all targets have the same excess (common case), a single cleanup
        /// before the switch suffices. When targets have different depths, we emit
        /// per-target trampolines: the switch jumps to cleanup stubs, each of
        /// which does its own excess cleanup then branches to the real target.
        /// </summary>
        public static void EmitBrTable(ILGenerator il, InstBranchTable inst,
            Stack<EmitBlock> blockStack, int excess = 0, int[]? perTargetExcess = null)
        {
            // Determine if any target uses ResultLocals. All targets of br_table share
            // the same label arity, but their ResultLocals may differ (different blocks).
            // If any target needs locals, emit per-target trampolines that store to
            // each target's specific locals before Br.
            bool anyTargetUsesLocals = false;
            for (int i = 0; i < inst.LabelCount; i++)
            {
                if (PeekLabel(blockStack, inst.GetLabel(i)).ResultLocals != null)
                { anyTargetUsesLocals = true; break; }
            }
            if (!anyTargetUsesLocals)
                anyTargetUsesLocals = PeekLabel(blockStack, inst.DefaultLabel).ResultLocals != null;

            if (anyTargetUsesLocals)
            {
                EmitBrTableThroughLocals(il, inst, blockStack, excess, perTargetExcess);
                return;
            }

            if (perTargetExcess != null)
            {
                // Multi-depth targets: emit per-target trampolines.
                // Save index and carried values to locals. Each trampoline does
                // its own excess cleanup and branches to the real target.
                var defTarget = PeekLabel(blockStack, inst.DefaultLabel);
                int arity = defTarget.LabelArity; // all targets have same arity per spec

                var indexLocal = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Stloc, indexLocal);

                // Save carried values (same for all targets)
                var carriedLocals = new LocalBuilder[arity];
                for (int i = 0; i < arity; i++)
                {
                    var clrType = i < defTarget.ResultClrTypes.Length
                        ? defTarget.ResultClrTypes[i] : typeof(int);
                    carriedLocals[i] = il.DeclareLocal(clrType);
                    il.Emit(OpCodes.Stloc, carriedLocals[i]);
                }

                // Define trampoline labels
                var trampolines = new Label[inst.LabelCount];
                for (int i = 0; i < inst.LabelCount; i++)
                    trampolines[i] = il.DefineLabel();
                var defTrampoline = il.DefineLabel();

                // Emit the switch on the saved index
                il.Emit(OpCodes.Ldloc, indexLocal);
                il.Emit(OpCodes.Switch, trampolines);
                il.Emit(OpCodes.Br, defTrampoline);

                // Emit each trampoline: pop excess, restore carried, br real target
                for (int i = 0; i < inst.LabelCount; i++)
                {
                    il.MarkLabel(trampolines[i]);
                    int ex = perTargetExcess[i];
                    for (int j = 0; j < ex; j++)
                        il.Emit(OpCodes.Pop);
                    for (int j = arity - 1; j >= 0; j--)
                        il.Emit(OpCodes.Ldloc, carriedLocals[j]);
                    il.Emit(OpCodes.Br, PeekLabel(blockStack, inst.GetLabel(i)).BranchTarget);
                }

                // Default trampoline
                il.MarkLabel(defTrampoline);
                int defEx = perTargetExcess[inst.LabelCount];
                for (int j = 0; j < defEx; j++)
                    il.Emit(OpCodes.Pop);
                for (int j = arity - 1; j >= 0; j--)
                    il.Emit(OpCodes.Ldloc, carriedLocals[j]);
                il.Emit(OpCodes.Br, PeekLabel(blockStack, inst.DefaultLabel).BranchTarget);
                return;
            }

            if (excess > 0)
            {
                // All targets at same depth: single cleanup before switch.
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
        /// br_table variant when targets carry Value results through locals.
        /// Emits per-target trampolines that store carried values to each
        /// target's specific ResultLocals before Br.
        /// </summary>
        private static void EmitBrTableThroughLocals(ILGenerator il, InstBranchTable inst,
            Stack<EmitBlock> blockStack, int excess, int[]? perTargetExcess)
        {
            var defTarget = PeekLabel(blockStack, inst.DefaultLabel);
            int arity = defTarget.LabelArity;

            // Save index and carried values to temp locals
            var indexLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, indexLocal);

            var carriedLocals = new LocalBuilder[arity];
            for (int i = 0; i < arity; i++)
            {
                var clrType = i < defTarget.ResultClrTypes.Length
                    ? defTarget.ResultClrTypes[i] : typeof(int);
                carriedLocals[i] = il.DeclareLocal(clrType);
                il.Emit(OpCodes.Stloc, carriedLocals[i]);
            }

            // Per-target trampolines
            var trampolines = new Label[inst.LabelCount];
            for (int i = 0; i < inst.LabelCount; i++)
                trampolines[i] = il.DefineLabel();
            var defTrampoline = il.DefineLabel();

            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Switch, trampolines);
            il.Emit(OpCodes.Br, defTrampoline);

            // Each trampoline: pop excess, route carried values to target's locals or stack, br
            for (int i = 0; i < inst.LabelCount; i++)
            {
                il.MarkLabel(trampolines[i]);
                var t = PeekLabel(blockStack, inst.GetLabel(i));
                int ex = perTargetExcess != null ? perTargetExcess[i] : excess;
                for (int j = 0; j < ex; j++)
                    il.Emit(OpCodes.Pop);

                // Deliver carried values: to target's ResultLocals if present, else onto stack
                if (t.ResultLocals != null)
                {
                    // carriedLocals were stored top-first (i=0 is top). Target locals
                    // are indexed the same way. Load from carriedLocals, store to target locals.
                    for (int j = 0; j < arity; j++)
                    {
                        il.Emit(OpCodes.Ldloc, carriedLocals[j]);
                        il.Emit(OpCodes.Stloc, t.ResultLocals[j]);
                    }
                }
                else
                {
                    for (int j = arity - 1; j >= 0; j--)
                        il.Emit(OpCodes.Ldloc, carriedLocals[j]);
                }
                il.Emit(OpCodes.Br, t.BranchTarget);
            }

            // Default trampoline
            il.MarkLabel(defTrampoline);
            int defEx = perTargetExcess != null ? perTargetExcess[inst.LabelCount] : excess;
            for (int j = 0; j < defEx; j++)
                il.Emit(OpCodes.Pop);
            if (defTarget.ResultLocals != null)
            {
                for (int j = 0; j < arity; j++)
                {
                    il.Emit(OpCodes.Ldloc, carriedLocals[j]);
                    il.Emit(OpCodes.Stloc, defTarget.ResultLocals[j]);
                }
            }
            else
            {
                for (int j = arity - 1; j >= 0; j--)
                    il.Emit(OpCodes.Ldloc, carriedLocals[j]);
            }
            il.Emit(OpCodes.Br, defTarget.BranchTarget);
        }

        /// <summary>
        /// Resolve a block's type to param/result arities and CLR types.
        /// Handles simple types (void, single value) and multi-value type indices.
        /// </summary>
        internal static (int paramArity, int resultArity, Type[] paramClrTypes, Type[] resultClrTypes)
            ResolveBlockArities(ValType blockType, ModuleInstance moduleInst)
        {
            if (blockType == ValType.Empty)
                return (0, 0, Array.Empty<Type>(), Array.Empty<Type>());

            if (blockType.IsDefType())
            {
                var funcType = moduleInst.Types.ResolveBlockType(blockType);
                if (funcType != null)
                {
                    // Internal representation: GC refs flow as object on the
                    // CIL stack (doc 1 §2.1, doc 2 §1). Block-result shuttle
                    // locals therefore use typeof(object) for GC refs, which
                    // obviates the merge-point restriction entirely — most
                    // GC-ref labels no longer need the shuttle mechanism.
                    var paramClr = funcType.ParameterTypes.Types
                        .Select(t => ModuleTranspiler.MapValTypeInternal(t, moduleInst)).ToArray();
                    var resultClr = funcType.ResultType.Types
                        .Select(t => ModuleTranspiler.MapValTypeInternal(t, moduleInst)).ToArray();
                    return (funcType.ParameterTypes.Arity, funcType.ResultType.Arity,
                        paramClr, resultClr);
                }
            }

            // Simple value type: 0 params, 1 result
            return (0, 1, Array.Empty<Type>(), new[] { ModuleTranspiler.MapValTypeInternal(blockType, moduleInst) });
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
