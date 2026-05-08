// Copyright 2026 Kelvin Nishikawa
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
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Transpiler.AOT.Emitters;
using CilOpCode = System.Reflection.Emit.OpCode;
using CilOpCodes = System.Reflection.Emit.OpCodes;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Lookahead peephole pass that runs over every WASM instruction
    /// sequence (function body, block / loop / if / else body) before
    /// the per-instruction emitter dispatches. Each pattern is a strict
    /// IL shrink: when fusion fires, the emitted CIL is at most as large
    /// as the naive emit it replaces, never larger. Misses fall through
    /// to <see cref="FunctionCodegen.EmitInstruction"/> with no behavior
    /// change.
    /// <para>
    /// The pass is intentionally local — single-instruction lookahead in
    /// the common case, occasional 2-3 ahead. No basic-block analysis,
    /// no flow analysis, no SSA. Things that need any of those (loop
    /// bounds-check hoisting, dead-store elimination across joins,
    /// store-to-load forwarding past calls) live in a future Tier 2 pass.
    /// </para>
    /// </summary>
    internal static class PeepholeOptimizer
    {
        /// <summary>
        /// Try to consume <paramref name="i"/> and one or more following
        /// instructions, emitting fused IL. Returns true when fusion
        /// fired and advanced <paramref name="i"/> past the consumed run
        /// (the caller's <c>i++</c> will then land on the next un-emitted
        /// instruction). Returns false when no pattern matched —
        /// caller falls through to the per-instruction dispatcher.
        /// </summary>
        public static bool TryFuse(
            FunctionCodegen codegen,
            ILGenerator il,
            IReadOnlyList<InstructionBase> instructions,
            ref int i)
        {
            if (i + 1 >= instructions.Count) return false;
            var curr = instructions[i];
            var next = instructions[i + 1];

            if (next.Op.x00 != WasmOpCode.BrIf || next is not InstBranchIf brIf)
                return false;

            // Eligibility on the leading op first — Peek (not Get) the
            // br_if's analysis info so an early-return doesn't desync
            // the dequeueing FIFO. Only the fusion-commits path calls
            // Get (and only on the brIf, since the cmp/eqz instruction
            // will be skipped entirely by the i++ below).
            bool eligibleCmp = TryGetCmpBranch(curr.Op.x00, out var branchOp);
            bool eligibleEqz = curr.Op.x00 == WasmOpCode.I32Eqz
                            || curr.Op.x00 == WasmOpCode.I64Eqz;
            if (!eligibleCmp && !eligibleEqz) return false;

            if (!BrIfHasSimpleEmit(codegen, brIf, out var target)) return false;

            // Commit: dequeue the brIf's analysis entry so the per-site
            // FIFO stays aligned with the un-emitted instructions.
            // (The cmp/eqz instruction has no analysis entry of its own
            // in the standard cases — Get is keyed by reference, and
            // these aren't shared singletons; if they were, the
            // unconsumed entry would dangle harmlessly.)
            codegen.ConsumeStackAnalysisInfo(brIf);

            if (eligibleCmp)
                il.Emit(branchOp, target.BranchTarget);
            else
                // i32.eqz / i64.eqz; br_if L → brfalse L
                // (Eqz pushes 1 iff input is 0; br_if branches when
                // stack-top is non-zero — collapsed: branch when zero.)
                il.Emit(CilOpCodes.Brfalse, target.BranchTarget);

            i++;   // consume the br_if; the loop's i++ advances past it
            return true;
        }

        /// <summary>
        /// True when <paramref name="brIf"/>'s emit would have been the
        /// short-form <c>brtrue target</c> path of <see cref="ControlEmitter.EmitBrIf"/>:
        /// no excess values to pop, target carries no result locals.
        /// Matches the same gates that path uses. Reads info via Peek
        /// so a false return doesn't disturb the per-site FIFO.
        /// </summary>
        private static bool BrIfHasSimpleEmit(
            FunctionCodegen codegen, InstBranchIf brIf, out EmitBlock target)
        {
            target = null!;
            var info = codegen.PeekStackAnalysisInfo(brIf);
            // Unreachable br_if would still emit (CLR verifier needs the
            // op present), but fusion across an unreachable boundary is
            // never useful — bail.
            if (info != null && info.Unreachable) return false;
            int excess = info?.Excess ?? 0;
            if (excess > 0) return false;

            target = ControlEmitter.PeekLabel(codegen.BlockStack, brIf.Label);
            if (target.ResultLocals != null) return false;
            return true;
        }

        /// <summary>
        /// Map a WASM compare opcode to the CIL conditional branch that
        /// produces the same effect as <c>cmp; brtrue label</c>. Branches
        /// pop both operands and jump on the typed comparison — saving
        /// the intermediate 0/1 boolean materialization.
        /// </summary>
        private static bool TryGetCmpBranch(WasmOpCode op, out CilOpCode branchOp)
        {
            switch (op)
            {
                // i32 / i64 share CIL branch opcodes — operand width is
                // inferred from the values on the stack.
                // i32 / i64 share CIL branch opcodes — operand width is
                // inferred from the values on the stack.
                case WasmOpCode.I32Eq:
                case WasmOpCode.I64Eq:  branchOp = CilOpCodes.Beq;     return true;
                case WasmOpCode.I32Ne:
                case WasmOpCode.I64Ne:  branchOp = CilOpCodes.Bne_Un;  return true;
                case WasmOpCode.I32LtS:
                case WasmOpCode.I64LtS: branchOp = CilOpCodes.Blt;     return true;
                case WasmOpCode.I32LtU:
                case WasmOpCode.I64LtU: branchOp = CilOpCodes.Blt_Un;  return true;
                case WasmOpCode.I32GtS:
                case WasmOpCode.I64GtS: branchOp = CilOpCodes.Bgt;     return true;
                case WasmOpCode.I32GtU:
                case WasmOpCode.I64GtU: branchOp = CilOpCodes.Bgt_Un;  return true;
                case WasmOpCode.I32LeS:
                case WasmOpCode.I64LeS: branchOp = CilOpCodes.Ble;     return true;
                case WasmOpCode.I32LeU:
                case WasmOpCode.I64LeU: branchOp = CilOpCodes.Ble_Un;  return true;
                case WasmOpCode.I32GeS:
                case WasmOpCode.I64GeS: branchOp = CilOpCodes.Bge;     return true;
                case WasmOpCode.I32GeU:
                case WasmOpCode.I64GeU: branchOp = CilOpCodes.Bge_Un;  return true;
            }
            branchOp = default;
            return false;
        }
    }
}
