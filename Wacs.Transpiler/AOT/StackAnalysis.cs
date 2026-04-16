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
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Reference;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Per-instruction metadata computed by the pre-pass.
    /// Keyed by InstructionBase reference identity.
    /// </summary>
    public class InstructionInfo
    {
        /// <summary>CIL stack height BEFORE this instruction executes.</summary>
        public int StackHeightBefore;

        /// <summary>Whether this instruction is unreachable (after unconditional branch).</summary>
        public bool Unreachable;

        /// <summary>
        /// For branch instructions: excess values that must be discarded.
        /// excess = actualStackAfterPops - targetStackHeight - labelArity
        /// Negative or zero means no cleanup needed.
        /// </summary>
        public int Excess;
    }

    /// <summary>
    /// Block label tracked during the pre-pass.
    /// Mirrors the interpreter's BlockTarget/Label.
    /// </summary>
    internal class AnalysisBlock
    {
        public WasmOpCode Kind;
        public int StackHeight;   // stack height at block entry
        public int LabelArity;    // results for block/if, params for loop
        public int Parameters;    // parameter count consumed from outer stack
        public int Results;       // result count produced

        public int TargetStack => StackHeight + LabelArity;
    }

    /// <summary>
    /// Pre-pass that walks the WASM instruction tree and computes per-instruction
    /// metadata (stack heights, reachability, excess counts).
    ///
    /// Mirrors the interpreter's Link phase:
    ///   - Tracks stackHeight forward through instructions
    ///   - Sets unreachable=true after br/br_table/return/unreachable
    ///   - Resets unreachable=false at End (block boundary)
    ///   - Computes excess for each branch instruction
    ///
    /// The instruction tree is already parsed — block instructions contain their
    /// children. We recurse into blocks using the same IBlockInstruction.GetBlock()
    /// interface that the emitters use.
    /// </summary>
    public class StackAnalysis
    {
        private readonly Dictionary<InstructionBase, InstructionInfo> _info = new();
        private readonly Stack<AnalysisBlock> _blockStack = new();
        private int _stackHeight;
        private bool _unreachable;

        /// <summary>
        /// Look up precomputed info for an instruction.
        /// Returns null if the instruction wasn't analyzed (shouldn't happen).
        /// </summary>
        public InstructionInfo? Get(InstructionBase inst)
        {
            return _info.TryGetValue(inst, out var info) ? info : null;
        }

        /// <summary>
        /// Run the analysis on a function body.
        /// Call once before IL emission begins.
        /// </summary>
        public void Analyze(FunctionType funcType, IEnumerable<InstructionBase> bodyInstructions)
        {
            _stackHeight = 0;
            _unreachable = false;
            _blockStack.Clear();
            _info.Clear();

            // Push the implicit function-level block
            _blockStack.Push(new AnalysisBlock
            {
                Kind = WasmOpCode.Block,
                StackHeight = 0,
                LabelArity = funcType.ResultType.Arity,
                Parameters = 0,
                Results = funcType.ResultType.Arity
            });

            AnalyzeSequence(bodyInstructions);

            _blockStack.Pop();
        }

        private void AnalyzeSequence(IEnumerable<InstructionBase> instructions)
        {
            foreach (var inst in instructions)
            {
                AnalyzeInstruction(inst);
            }
        }

        private void AnalyzeInstruction(InstructionBase inst)
        {
            // Record pre-instruction state
            var info = new InstructionInfo
            {
                StackHeightBefore = _stackHeight,
                Unreachable = _unreachable,
                Excess = 0
            };
            _info[inst] = info;

            // GC instructions detected by namespace
            if (inst.GetType().Namespace == "Wacs.Core.Instructions.GC")
            {
                if (!_unreachable)
                    ApplyStackDiff(inst);
                // GC branch instructions (br_on_cast) would need handling here
                // but for now we just track the stack diff
                return;
            }

            var op = inst.Op.x00;

            // Block-structured instructions: recurse into children
            if (inst is IBlockInstruction blockInst)
            {
                switch (op)
                {
                    case WasmOpCode.Block:
                        AnalyzeBlock((InstBlock)inst);
                        return;
                    case WasmOpCode.Loop:
                        AnalyzeLoop((InstLoop)inst);
                        return;
                    case WasmOpCode.If:
                        AnalyzeIf((InstIf)inst);
                        return;
                }
                // TryTable and other block instructions — just recurse
                if (blockInst.Count > 0)
                {
                    for (int b = 0; b < blockInst.Count; b++)
                    {
                        var block = blockInst.GetBlock(b);
                        AnalyzeSequence(block.Instructions);
                    }
                }
                return;
            }

            // Non-block instructions: apply StackDiff and check control flow
            if (!_unreachable)
                ApplyStackDiff(inst);

            switch (op)
            {
                case WasmOpCode.Unreachable:
                    _unreachable = true;
                    break;

                case WasmOpCode.Br:
                {
                    if (!info.Unreachable)
                    {
                        var target = PeekLabel(((InstBranch)inst).Label);
                        // br.StackDiff = 0, so _stackHeight still includes carried + excess
                        info.Excess = _stackHeight - target.TargetStack;
                    }
                    _unreachable = true;
                    break;
                }

                case WasmOpCode.BrIf:
                {
                    if (!info.Unreachable)
                    {
                        var target = PeekLabel(((InstBranchIf)inst).Label);
                        // br_if.StackDiff = -1 (condition popped), already applied.
                        // _stackHeight is now the height AFTER popping condition.
                        // The carried values + excess are what remains.
                        info.Excess = _stackHeight - target.TargetStack;
                    }
                    // br_if does NOT set unreachable (condition may be false)
                    break;
                }

                case WasmOpCode.BrTable:
                {
                    if (!info.Unreachable)
                    {
                        var btInst = (InstBranchTable)inst;
                        var target = PeekLabel(btInst.DefaultLabel);
                        // br_table.StackDiff = 0 but it pops the index.
                        // The CIL switch instruction also pops the index.
                        // Excess = stack - index - target_stack
                        info.Excess = _stackHeight - 1 - target.TargetStack;
                    }
                    _unreachable = true;
                    break;
                }

                case WasmOpCode.Return:
                {
                    if (!info.Unreachable)
                    {
                        var funcBlock = PeekBottom();
                        // return.StackDiff = 0, _stackHeight includes return values + excess
                        info.Excess = _stackHeight - funcBlock.TargetStack;
                    }
                    _unreachable = true;
                    break;
                }

                case WasmOpCode.BrOnNull:
                {
                    if (!info.Unreachable)
                    {
                        var brInst = (InstBrOnNull)inst;
                        var target = PeekLabel(brInst.Label);
                        // br_on_null.StackDiff = 0, ref is still on the stack.
                        // On the null path: ref is consumed (-1), then branch.
                        int nullPathHeight = _stackHeight - 1;
                        info.Excess = nullPathHeight - target.TargetStack;
                    }
                    // br_on_null does NOT set unreachable (non-null path continues)
                    break;
                }

                case WasmOpCode.BrOnNonNull:
                {
                    if (!info.Unreachable)
                    {
                        var brInst = (InstBrOnNonNull)inst;
                        var target = PeekLabel(brInst.Label);
                        // br_on_non_null.StackDiff = -1, already applied.
                        // On the non-null path: ref stays (label arity includes it).
                        // The excess is relative to what's on the stack after StackDiff.
                        // But on the branch path, the ref is kept (+1 vs the StackDiff).
                        // Actual branch height = _stackHeight + 1 (ref not consumed on branch path)
                        int branchPathHeight = _stackHeight + 1;
                        info.Excess = branchPathHeight - target.TargetStack;
                    }
                    break;
                }

                case WasmOpCode.End:
                {
                    // Function End: reset unreachable, compute final stack
                    if (inst is InstEnd endInst && endInst.FunctionEnd)
                    {
                        _unreachable = false;
                        var funcBlock = PeekBottom();
                        _stackHeight = funcBlock.StackHeight
                            - funcBlock.Parameters
                            + funcBlock.Results;
                    }
                    break;
                }
            }
        }

        private void AnalyzeBlock(InstBlock inst)
        {
            var blockType = inst.BlockType;
            int paramArity = 0; // simple blocks have no params
            int resultArity = blockType == ValType.Empty ? 0 : 1;

            _blockStack.Push(new AnalysisBlock
            {
                Kind = WasmOpCode.Block,
                StackHeight = _stackHeight,
                LabelArity = resultArity, // branch targets block end with results
                Parameters = paramArity,
                Results = resultArity
            });

            bool savedUnreachable = _unreachable;
            // Body starts reachable (entering the block is reachable)

            var block = inst.GetBlock(0);
            AnalyzeSequence(block.Instructions);

            _blockStack.Pop();

            // Restore reachability: End of block is reachable
            // (even if body ended unreachable, the branch target is reachable)
            _unreachable = false;

            // Compute stack after block: entry - params + results
            var ab = new AnalysisBlock
            {
                StackHeight = _stackHeight, // will be overwritten
                Parameters = paramArity,
                Results = resultArity
            };
            _stackHeight = (_info[inst]?.StackHeightBefore ?? _stackHeight)
                - paramArity + resultArity;
        }

        private void AnalyzeLoop(InstLoop inst)
        {
            var blockType = inst.BlockType;
            int paramArity = 0; // simple loops have no params
            int resultArity = blockType == ValType.Empty ? 0 : 1;

            _blockStack.Push(new AnalysisBlock
            {
                Kind = WasmOpCode.Loop,
                StackHeight = _stackHeight,
                LabelArity = paramArity, // branch to loop targets start with params
                Parameters = paramArity,
                Results = resultArity
            });

            var block = inst.GetBlock(0);
            AnalyzeSequence(block.Instructions);

            _blockStack.Pop();
            _unreachable = false;

            _stackHeight = (_info[inst]?.StackHeightBefore ?? _stackHeight)
                - paramArity + resultArity;
        }

        private void AnalyzeIf(InstIf inst)
        {
            var blockType = inst.BlockType;
            int paramArity = 0;
            int resultArity = blockType == ValType.Empty ? 0 : 1;

            // If consumes condition (-1)
            _stackHeight--;
            int entryHeight = _stackHeight;

            _blockStack.Push(new AnalysisBlock
            {
                Kind = WasmOpCode.If,
                StackHeight = entryHeight,
                LabelArity = resultArity,
                Parameters = paramArity,
                Results = resultArity
            });

            bool hasElse = inst.Count == 2;

            // Analyze then branch
            var ifBlock = inst.GetBlock(0);
            AnalyzeSequence(ifBlock.Instructions);

            if (hasElse)
            {
                // Reset stack for else branch (same entry point as then)
                _stackHeight = entryHeight;
                _unreachable = false;

                var elseBlock = inst.GetBlock(1);
                AnalyzeSequence(elseBlock.Instructions);
            }

            _blockStack.Pop();
            _unreachable = false;

            _stackHeight = entryHeight - paramArity + resultArity;
        }

        private void ApplyStackDiff(InstructionBase inst)
        {
            _stackHeight += inst.StackDiff;
            if (_stackHeight < 0) _stackHeight = 0;
        }

        private AnalysisBlock PeekLabel(int depth)
        {
            int i = 0;
            foreach (var block in _blockStack)
            {
                if (i == depth) return block;
                i++;
            }
            throw new TranspilerException(
                $"StackAnalysis: label depth {depth} exceeds block stack size {_blockStack.Count}");
        }

        private AnalysisBlock PeekBottom()
        {
            AnalysisBlock bottom = null!;
            foreach (var block in _blockStack)
                bottom = block;
            return bottom;
        }
    }
}
