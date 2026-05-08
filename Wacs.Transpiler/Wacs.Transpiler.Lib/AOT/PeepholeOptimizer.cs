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

            if (TryAlgebraicIdentity(il, curr, next, ref i)) return true;
            if (TryLocalSimplification(codegen, il, curr, next, ref i)) return true;
            if (TrySignExtendBeforeNarrowingStore(curr, next, ref i)) return true;

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

        /// <summary>
        /// Drop pairs of <c>i32.const C; &lt;binop&gt;</c> (and i64 analogues) where
        /// C is the identity element for the binop. The previous-iteration's
        /// <c>&lt;value&gt;</c> emit is already on the CIL stack — skipping the
        /// const + binop leaves it there as the operation's result.
        /// <list type="bullet">
        ///   <item>add / sub / or / xor / shl / shr_s / shr_u / rotl / rotr with 0 → identity</item>
        ///   <item>mul / div_s / div_u with 1 → identity</item>
        ///   <item>and with -1 → identity (all bits set)</item>
        /// </list>
        /// Sound for all values: the binop's result is provably equal to
        /// the left operand; no overflow, NaN, or trap concerns at this
        /// scope (integer ops with these constants don't trap).
        /// </summary>
        private static bool TryAlgebraicIdentity(
            ILGenerator il, InstructionBase curr, InstructionBase next, ref int i)
        {
            // i32.const C ; <i32 binop>
            if (curr is InstI32Const i32c
                && IsIntIdentity(next.Op.x00, i32c.Value, isI64: false))
            {
                i++;   // consume both: const + binop
                return true;
            }

            // i64.const C ; <i64 binop>
            if (curr is InstI64Const i64c
                && IsI64IdentityValue(next.Op.x00, i64c, out var matched)
                && matched)
            {
                i++;
                return true;
            }

            return false;
        }

        private static bool IsIntIdentity(WasmOpCode op, int constValue, bool isI64)
        {
            // identity-on-the-RIGHT only — left operand becomes the result.
            switch (op)
            {
                case WasmOpCode.I32Add or WasmOpCode.I32Sub
                    or WasmOpCode.I32Or  or WasmOpCode.I32Xor
                    or WasmOpCode.I32Shl or WasmOpCode.I32ShrS or WasmOpCode.I32ShrU
                    or WasmOpCode.I32Rotl or WasmOpCode.I32Rotr
                    when !isI64:
                    return constValue == 0;
                case WasmOpCode.I32Mul or WasmOpCode.I32DivS or WasmOpCode.I32DivU
                    when !isI64:
                    return constValue == 1;
                case WasmOpCode.I32And when !isI64:
                    return constValue == -1;
            }
            return false;
        }

        private static bool IsI64IdentityValue(
            WasmOpCode op, InstI64Const i64c, out bool matched)
        {
            // i64.const Value is internal — read via reflection-free
            // accessor on the instruction type. Matches the same
            // identity table as i32 above, with the constant compared
            // as a 64-bit value.
            // The Value field is internal; we can't access it directly
            // here without exposing it. Instead, route via the public
            // API: TryGetConstI64.
            matched = false;
            // Pull the constant from the i64.const instruction. The
            // instruction's `Value` is internal; we use a cheap
            // round-trip via its render text only as a fallback. A
            // direct accessor is added below.
            long v;
            if (!TryReadI64Const(i64c, out v)) return false;
            switch (op)
            {
                case WasmOpCode.I64Add or WasmOpCode.I64Sub
                    or WasmOpCode.I64Or  or WasmOpCode.I64Xor
                    or WasmOpCode.I64Shl or WasmOpCode.I64ShrS or WasmOpCode.I64ShrU
                    or WasmOpCode.I64Rotl or WasmOpCode.I64Rotr:
                    matched = v == 0; return true;
                case WasmOpCode.I64Mul or WasmOpCode.I64DivS or WasmOpCode.I64DivU:
                    matched = v == 1; return true;
                case WasmOpCode.I64And:
                    matched = v == -1L; return true;
            }
            return false;
        }

        // InstI64Const.Value is internal cross-assembly. FetchImmediate
        // is the public reader and ignores its ExecContext argument
        // (just returns the field), so passing null! is safe.
        private static bool TryReadI64Const(InstI64Const c, out long value)
        {
            value = c.FetchImmediate(null!);
            return true;
        }

        /// <summary>
        /// Two local-instruction simplifications:
        /// <list type="bullet">
        ///   <item><c>local.tee x; drop</c> → <c>local.set x</c>.
        ///   The naive <c>tee+drop</c> emits <c>dup; stloc x; pop</c> — the
        ///   dup and pop cancel.</item>
        ///   <item><c>local.get x; local.get x</c> (same x) →
        ///   <c>ldloc x; dup</c>. Saves one memory read.</item>
        /// </list>
        /// </summary>
        private static bool TryLocalSimplification(
            FunctionCodegen codegen, ILGenerator il,
            InstructionBase curr, InstructionBase next, ref int i)
        {
            if (curr is InstLocalTee tee && next.Op.x00 == WasmOpCode.Drop)
            {
                Emitters.VariableEmitter.EmitLocalSetInternal(
                    il, tee.GetIndex(),
                    codegen.ParamCount, codegen.ParamShadowLocals);
                i++;
                return true;
            }

            if (curr is InstLocalGet g1 && next is InstLocalGet g2
                && g1.GetIndex() == g2.GetIndex())
            {
                Emitters.VariableEmitter.EmitLocalGetInternal(
                    il, g1.GetIndex(),
                    codegen.ParamCount, codegen.ParamShadowLocals);
                il.Emit(CilOpCodes.Dup);
                i++;
                return true;
            }

            return false;
        }

        /// <summary>
        /// <c>i32.extend8_s; i32.store8</c> (and the other extend → narrowing
        /// store pairs) drop the extend. The store ignores the upper bits
        /// the extend wrote, so the extend is dead. Five widths covered:
        /// i32.{8,16}_s before i32.store{8,16}, i64.{8,16,32}_s before
        /// i64.store{8,16,32}.
        /// </summary>
        private static bool TrySignExtendBeforeNarrowingStore(
            InstructionBase curr, InstructionBase next, ref int i)
        {
            var c = curr.Op.x00;
            var n = next.Op.x00;
            bool match =
                (c == WasmOpCode.I32Extend8S  && n == WasmOpCode.I32Store8)  ||
                (c == WasmOpCode.I32Extend16S && n == WasmOpCode.I32Store16) ||
                (c == WasmOpCode.I64Extend8S  && n == WasmOpCode.I64Store8)  ||
                (c == WasmOpCode.I64Extend16S && n == WasmOpCode.I64Store16) ||
                (c == WasmOpCode.I64Extend32S && n == WasmOpCode.I64Store32);
            if (!match) return false;

            // The extend is dead; skip it but let the dispatcher emit
            // the store unchanged. Rather than emitting any IL here,
            // bump i by 0 (caller's i++ skips the extend instruction
            // we're consuming). The next loop iteration emits the
            // store via the regular path.
            // Wait — we'd need to NOT skip: the caller's i++ would
            // step past curr, but the store is at i+1; we want the
            // loop to land on i+1. Returning true with no advance
            // makes that work: caller's i++ advances from i (the
            // extend) to i+1 (the store), exactly what we want.
            return true;
        }
    }
}
