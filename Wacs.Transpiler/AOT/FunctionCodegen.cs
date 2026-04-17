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
using System.Runtime.CompilerServices;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
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
        private readonly TranspilerOptions _options;
        private readonly DiagnosticCollector _diagnostics;

        // Control flow state
        private readonly Stack<EmitBlock> _blockStack = new();
        private LocalBuilder[] _locals = null!;
        private ILGenerator _il = null!;
        private StackAnalysis _stackAnalysis = null!;
        private InstructionInfo? _currentInfo; // precomputed info for current instruction

        public FunctionCodegen(
            MethodBuilder method,
            FunctionInstance funcInst,
            FunctionInstance[] siblingFunctions,
            MethodBuilder[] siblingMethods,
            int importCount,
            GcTypeEmitter gcTypes,
            FunctionType[] allFunctionTypes,
            TranspilerOptions options,
            DiagnosticCollector diagnostics)
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
            _options = options;
            _diagnostics = diagnostics;
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

            // Pre-pass: compute stack heights and reachability (mirrors interpreter Link)
            _stackAnalysis = new StackAnalysis();
            _stackAnalysis.Analyze(
                _funcInst.Type, _funcInst.Body.Instructions,
                _moduleInst, _allFunctionTypes, _importCount);

            // Emit pass: generate CIL using precomputed metadata
            _il = _method.GetILGenerator();

            // Declare CIL locals for WASM locals (parameters are CIL args, not locals)
            var wasmLocals = _funcInst.Locals;
            _locals = new LocalBuilder[wasmLocals.Length];
            for (int i = 0; i < wasmLocals.Length; i++)
            {
                _locals[i] = _il.DeclareLocal(ModuleTranspiler.MapValType(wasmLocals[i]));
            }

            // Stack guard: trap before CLR StackOverflow kills the process.
            // TryEnsureSufficientExecutionStack() is a cheap SP-vs-limit compare
            // that returns false when stack space is running low.
            EmitStackGuard(_il);

            // The function body is an implicit block — br 0 at function level
            // targets the function return. Push a function-level block.
            var funcEndLabel = _il.DefineLabel();
            var funcResultTypes = _funcInst.Type.ResultType.Types;
            var funcResultClrTypes = new Type[funcResultTypes.Length];
            for (int i = 0; i < funcResultTypes.Length; i++)
                funcResultClrTypes[i] = ModuleTranspiler.MapValType(funcResultTypes[i]);

            _blockStack.Push(new EmitBlock
            {
                BranchTarget = funcEndLabel,
                Kind = WasmOpCode.Block,
                StackHeight = 0,
                LabelArity = _funcInst.Type.ResultType.Arity,
                ResultClrTypes = funcResultClrTypes
            });

            // Emit instructions
            foreach (var inst in _funcInst.Body.Instructions)
            {
                EmitInstruction(_il, inst);
            }

            _blockStack.Pop();
            _il.MarkLabel(funcEndLabel);

            // Emit a trailing ret so that branches to funcEndLabel have a
            // valid terminator. The function End instruction also emits ret
            // inside the body, but funcEndLabel is placed after the body —
            // code that branches here (br/br_if/br_table targeting label 0)
            // would otherwise land at the end of the method with no ret.
            _il.Emit(OpCodes.Ret);

            return true;
        }

        /// <summary>
        /// Emit IL for a single instruction. This is the central dispatch that
        /// is also passed as a delegate to ControlEmitter for recursive emission.
        /// </summary>
        private void EmitInstruction(ILGenerator il, InstructionBase inst)
        {
            // Look up precomputed stack analysis for this instruction
            _currentInfo = _stackAnalysis.Get(inst);

            // Detect GC instructions by class type — their opcodes may be aliased
            // after the interpreter's linking step rewrites the ByteCode.
            if (inst.GetType().Namespace == "Wacs.Core.Instructions.GC")
            {
                // Handle br_on_cast/fail here (not in GcEmitter) because we need
                // block stack + excess info for the shuttle mechanism.
                if (inst is Wacs.Core.Instructions.GC.InstBrOnCast boc)
                {
                    EmitBrOnCastWithExcess(il, boc.Label, boc.TargetType, castFail: false);
                    return;
                }
                if (inst is Wacs.Core.Instructions.GC.InstBrOnCastFail bocf)
                {
                    EmitBrOnCastWithExcess(il, bocf.Label, bocf.TargetType, castFail: true);
                    return;
                }

                GcEmitter.Emit(il, inst, inst.Op.xFB, _gcTypes, _moduleInst,
                    depth =>
                    {
                        int i = 0;
                        foreach (var block in _blockStack)
                        {
                            if (i == depth) return block.BranchTarget;
                            i++;
                        }
                        throw new TranspilerException($"br_on_cast label depth {depth} exceeds block stack");
                    });
                return;
            }

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

            // 0xFD prefix (SIMD)
            if (op == WasmOpCode.FD)
            {
                SimdEmitter.Emit(il, inst, inst.Op.xFD, _options, _diagnostics, _funcInst.Name);
                return;
            }

            // Other multi-byte prefix opcodes (not yet supported)
            if (op == WasmOpCode.FE)
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
                CallEmitter.EmitCallSite(il, site, _siblingMethods, _moduleInst, _options);
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
                {
                    int sh = _currentInfo?.StackHeightBefore ?? 0;
                    ControlEmitter.EmitBlock(il, (InstBlock)inst, _blockStack, EmitInstruction,
                        sh, _moduleInst);
                    break;
                }

                case WasmOpCode.Loop:
                {
                    int sh = _currentInfo?.StackHeightBefore ?? 0;
                    ControlEmitter.EmitLoop(il, (InstLoop)inst, _blockStack, EmitInstruction,
                        sh, _moduleInst);
                    break;
                }

                case WasmOpCode.If:
                {
                    int sh = (_currentInfo?.StackHeightBefore ?? 1) - 1;
                    ControlEmitter.EmitIf(il, (InstIf)inst, _blockStack, EmitInstruction,
                        sh, _moduleInst);
                    break;
                }

                case WasmOpCode.Else:
                    // Handled inside EmitIf — should not appear at top level
                    break;

                case WasmOpCode.End:
                    if (inst is InstEnd endInst && endInst.FunctionEnd)
                    {
                        // Branch to funcEndLabel where ret is emitted.
                        // This gives the JIT an explicit branch edge so it can
                        // merge the stack state from here with any other branches
                        // to funcEndLabel (br/br_if/br_table targeting label 0).
                        EmitBlock funcBlock = null!;
                        foreach (var block in _blockStack)
                            funcBlock = block;
                        il.Emit(OpCodes.Br, funcBlock.BranchTarget);
                    }
                    // Non-function End is handled by the block/loop/if emitters.
                    break;

                case WasmOpCode.Return:
                {
                    // return ≡ br to the function-level block.
                    // Use precomputed excess from StackAnalysis.
                    EmitBlock funcBlock = null!;
                    foreach (var block in _blockStack)
                        funcBlock = block;

                    int excess = _currentInfo?.Excess ?? 0;
                    EmitExcessCleanup(il, excess, funcBlock);
                    il.Emit(OpCodes.Ret);
                    break;
                }

                case WasmOpCode.Br:
                {
                    var target = ControlEmitter.PeekLabel(_blockStack, ((InstBranch)inst).Label);
                    int excess = _currentInfo?.Excess ?? 0;
                    EmitExcessCleanup(il, excess, target);
                    il.Emit(OpCodes.Br, target.BranchTarget);
                    break;
                }

                case WasmOpCode.BrIf:
                {
                    int excess = _currentInfo?.Excess ?? 0;
                    ControlEmitter.EmitBrIf(il, (InstBranchIf)inst, _blockStack, excess);
                    break;
                }

                case WasmOpCode.BrTable:
                {
                    int excess = _currentInfo?.Excess ?? 0;
                    ControlEmitter.EmitBrTable(il, (InstBranchTable)inst, _blockStack,
                        excess, _currentInfo?.BrTableExcess);
                    break;
                }

                case WasmOpCode.BrOnNull:
                {
                    // br_on_null L: pop ref, if null branch to L (ref consumed), else push ref back
                    var brInst = (Wacs.Core.Instructions.Reference.InstBrOnNull)inst;
                    var target = ControlEmitter.PeekLabel(_blockStack, brInst.Label);
                    int excess = _currentInfo?.Excess ?? 0;

                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Call, typeof(Emitters.TableRefHelpers).GetMethod(
                        nameof(Emitters.TableRefHelpers.RefIsNull),
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!);
                    var skipLabel = il.DefineLabel();
                    il.Emit(OpCodes.Brfalse, skipLabel); // not null → skip
                    il.Emit(OpCodes.Pop); // consume ref (null case)

                    EmitExcessCleanup(il, excess, target);
                    il.Emit(OpCodes.Br, target.BranchTarget);

                    il.MarkLabel(skipLabel);
                    break;
                }

                case WasmOpCode.BrOnNonNull:
                {
                    // br_on_non_null L: pop ref, if non-null branch to L (ref on stack), else consume
                    var brInst = (Wacs.Core.Instructions.Reference.InstBrOnNonNull)inst;
                    var target = ControlEmitter.PeekLabel(_blockStack, brInst.Label);
                    int excess = _currentInfo?.Excess ?? 0;

                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Call, typeof(Emitters.TableRefHelpers).GetMethod(
                        nameof(Emitters.TableRefHelpers.RefIsNull),
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!);
                    var skipLabel = il.DefineLabel();
                    il.Emit(OpCodes.Brtrue, skipLabel); // null → skip

                    // non-null path: ref is on stack, clean excess then branch
                    // (excess was computed for the branch path which includes the ref)
                    EmitExcessCleanup(il, excess, target);
                    il.Emit(OpCodes.Br, target.BranchTarget);

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
        /// Check if a block type is simple (void or single result, no params).
        /// Multi-value block types (type indices resolving to multi-param or multi-result
        /// function types) require shuttle locals and are not yet supported.
        /// </summary>

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
        /// <summary>
        /// Emit return sequence for the function.
        /// <summary>
        /// Emit CIL to clean up excess stack values before a branch.
        /// Saves the label's carried values (arity), pops excess, restores carried.
        /// Uses the precomputed excess count from StackAnalysis.
        /// </summary>
        private static void EmitExcessCleanup(ILGenerator il, int excess, EmitBlock target)
        {
            if (excess <= 0) return;

            int arity = target.LabelArity;
            if (arity > 0)
            {
                // Shuttle carried values past excess
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
            else
            {
                for (int i = 0; i < excess; i++)
                    il.Emit(OpCodes.Pop);
            }
        }


        /// <summary>
        /// Emit br_on_cast / br_on_cast_fail with proper excess cleanup.
        /// Stack: [ref (Value)]
        /// br_on_cast: if ref matches targetType, branch (carry ref); else fall through (ref on stack)
        /// br_on_cast_fail: if ref doesn't match, branch (carry ref); else fall through (ref on stack)
        /// </summary>
        private void EmitBrOnCastWithExcess(ILGenerator il, int labelDepth,
            ValType targetType, bool castFail)
        {
            var target = ControlEmitter.PeekLabel(_blockStack, labelDepth);
            int excess = _currentInfo?.Excess ?? 0;

            // Dup the ref, test the type
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, (int)targetType);
            il.Emit(OpCodes.Ldc_I4, targetType.IsNullable() ? 1 : 0);
            il.Emit(OpCodes.Ldarg_0); // ThinContext
            il.Emit(OpCodes.Call, typeof(Emitters.GcRuntimeHelpers).GetMethod(
                nameof(Emitters.GcRuntimeHelpers.RefTestValue),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!);

            if (excess > 0)
            {
                // Need shuttle: save test result, conditionally do excess cleanup + branch
                var testLocal = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Stloc, testLocal);

                var skipLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, testLocal);
                if (castFail)
                    il.Emit(OpCodes.Brtrue, skipLabel); // test passed → don't branch (skip)
                else
                    il.Emit(OpCodes.Brfalse, skipLabel); // test failed → don't branch (skip)

                // Branch path: ref is on stack as carried value, clean excess, branch
                EmitExcessCleanup(il, excess, target);
                il.Emit(OpCodes.Br, target.BranchTarget);

                il.MarkLabel(skipLabel);
                // Fall-through: ref still on stack from the Dup
            }
            else
            {
                // Simple case: no excess, just branch directly
                if (castFail)
                    il.Emit(OpCodes.Brfalse, target.BranchTarget);
                else
                    il.Emit(OpCodes.Brtrue, target.BranchTarget);
            }
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
            // Detect GC instructions by class type — opcodes may be aliased
            if (inst.GetType().Namespace == "Wacs.Core.Instructions.GC")
                return GcEmitter.CanEmit(inst.Op.xFB, _gcTypes);

            var op = inst.Op.x00;

            // 0xFB prefix (GC) — fallback for non-aliased opcodes
            if (op == WasmOpCode.FB)
                return GcEmitter.CanEmit(inst.Op.xFB, _gcTypes);

            // 0xFC prefix — check extended opcode
            if (op == WasmOpCode.FC)
                return ExtEmitter.CanEmit(inst.Op.xFC);

            // 0xFD prefix (SIMD)
            if (op == WasmOpCode.FD)
                return SimdEmitter.CanEmit(inst.Op.xFD);

            // Other multi-byte opcodes — not yet supported
            if (op == WasmOpCode.FE)
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
                    // All global types supported (numeric, ref, v128 — all via Value)
                    return true;
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

        private static readonly MethodInfo TryEnsureStackMethod =
            typeof(RuntimeHelpers).GetMethod(
                nameof(RuntimeHelpers.TryEnsureSufficientExecutionStack),
                BindingFlags.Public | BindingFlags.Static)!;

        /// <summary>
        /// Emit a stack depth guard at function entry.
        /// Calls RuntimeHelpers.TryEnsureSufficientExecutionStack() which is a cheap
        /// SP-vs-limit compare. If it returns false, we throw a TrapException before
        /// the CLR's unrecoverable StackOverflowException can fire.
        /// </summary>
        private static void EmitStackGuard(ILGenerator il)
        {
            var okLabel = il.DefineLabel();

            // if (RuntimeHelpers.TryEnsureSufficientExecutionStack()) goto ok;
            il.Emit(OpCodes.Call, TryEnsureStackMethod);
            il.Emit(OpCodes.Brtrue_S, okLabel);

            // throw new WasmRuntimeException("call stack exhausted");
            il.Emit(OpCodes.Ldstr, "call stack exhausted");
            il.Emit(OpCodes.Newobj, typeof(WasmRuntimeException).GetConstructor(
                new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(okLabel);
        }
    }
}
