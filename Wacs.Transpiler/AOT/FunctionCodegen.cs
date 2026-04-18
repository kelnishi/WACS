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
        // Internal CIL types for WASM params as seen on the internal stack after
        // shadow-local routing (doc 2 §15). GC-ref params: typeof(object);
        // others: same as the CIL arg type.
        private Type[] _paramClrTypes = null!;
        // Shadow locals (doc 2 §15) for GC-ref params: object-typed CIL locals
        // populated at function entry from Value args. `local.get`/`local.set`
        // of a ref-typed param reads/writes the shadow local, never the arg.
        // Index i corresponds to WASM param i; null when the param is not a
        // GC ref (scalar, v128, funcref, externref).
        private LocalBuilder?[] _paramShadowLocals = null!;
        private ILGenerator _il = null!;
        private StackAnalysis _stackAnalysis = null!;
        private InstructionInfo? _currentInfo; // precomputed info for current instruction
        private CilValidator _cilValidator = null!;

        // Nesting depth of CLR exception blocks (BeginExceptionBlock). Branches
        // whose target lies outside the current try-region must emit Leave
        // instead of Br (doc 2 §14). Each try_table that wraps the current
        // instruction increments this; EndExceptionBlock decrements.
        private int _tryDepth;

        // True when the function body contains any `try_table`. Forces result
        // shuttle allocation for every label with arity > 0: Leave empties the
        // eval stack, so cross-try branches must rendezvous through locals.
        // Cheap to over-allocate (cold IL) versus prohibitively complex to
        // prove per-label reachability from inside a try region.
        private bool _functionHasTryTable;

        // Module-bound wrappers that disambiguate concrete type indices.
        // Without the module, concrete func types would be misclassified as
        // GC refs (doc 2 §1 invariant 3).
        private Type InternalType(ValType t) => ModuleTranspiler.MapValTypeInternal(t, _moduleInst);
        private bool IsGcRef(ValType t) => ModuleTranspiler.IsGcRefType(t, _moduleInst);

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
            _moduleInst = funcInst.Module;
            _paramCount = funcInst.Type.ParameterTypes.Arity;
            _paramClrTypes = new Type[_paramCount];
            for (int i = 0; i < _paramCount; i++)
                _paramClrTypes[i] = InternalType(funcInst.Type.ParameterTypes.Types[i]);
            _importCount = importCount;
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

            // Scan for try_table anywhere in the body. Its presence requires
            // all labels to use shuttle locals (doc 2 §14, stage 3 WI-4).
            _functionHasTryTable = ContainsTryTable(_funcInst.Body.Instructions);

            // Pre-pass: compute stack heights and reachability (mirrors interpreter Link)
            _stackAnalysis = new StackAnalysis();
            _stackAnalysis.Analyze(
                _funcInst.Type, _funcInst.Body.Instructions,
                _moduleInst, _allFunctionTypes, _importCount);

            // Emit pass: generate CIL using precomputed metadata
            _il = _method.GetILGenerator();

            // CIL stack validator — tracks type stack during emission
            _cilValidator = new CilValidator(_method.Name);

            // Declare CIL locals for WASM locals (parameters are CIL args, not locals).
            // Ref types use the INTERNAL representation (doc 1 §2.1, §4.2):
            // GC refs are typed as object in locals so struct.get / array.get / etc.
            // can consume them directly; funcref / externref / v128 stay as Value.
            var wasmLocals = _funcInst.Locals;
            _locals = new LocalBuilder[wasmLocals.Length];
            for (int i = 0; i < wasmLocals.Length; i++)
            {
                _locals[i] = _il.DeclareLocal(InternalType(wasmLocals[i]));
            }

            // Shadow locals for ref-typed params (doc 2 §15). For each GC-ref
            // param, allocate an object-typed local and initialize it from the
            // incoming Value arg by unwrapping once at entry. Body reads/writes
            // the shadow local; the original arg is not re-read after this.
            _paramShadowLocals = new LocalBuilder?[_paramCount];
            var paramWasmTypes = _funcInst.Type.ParameterTypes.Types;
            for (int i = 0; i < _paramCount; i++)
            {
                if (!IsGcRef(paramWasmTypes[i])) continue;
                var shadow = _il.DeclareLocal(typeof(object));
                _paramShadowLocals[i] = shadow;
                _il.Emit(OpCodes.Ldarg, i + 1); // +1 for ThinContext
                _il.Emit(OpCodes.Call, typeof(Emitters.GcRuntimeHelpers).GetMethod(
                    nameof(Emitters.GcRuntimeHelpers.UnwrapRef),
                    BindingFlags.Public | BindingFlags.Static)!);
                _il.Emit(OpCodes.Stloc, shadow);
            }

            // Stack guard: trap before CLR StackOverflow kills the process.
            // TryEnsureSufficientExecutionStack() is a cheap SP-vs-limit compare
            // that returns false when stack space is running low.
            EmitStackGuard(_il);

            // The function body is an implicit block — br 0 at function level
            // targets the function return. Push a function-level block.
            // Result types use INTERNAL representation (doc 1 §4.2): labels
            // carry GC refs as object; the return boundary wraps to Value.
            var funcEndLabel = _il.DefineLabel();
            var funcResultTypes = _funcInst.Type.ResultType.Types;
            var funcResultClrTypes = new Type[funcResultTypes.Length];
            for (int i = 0; i < funcResultTypes.Length; i++)
                funcResultClrTypes[i] = InternalType(funcResultTypes[i]);

            // If the function contains try_table, br 0 (or br_table 0, etc.)
            // from inside the try emits Leave which empties the stack —
            // allocate shuttle locals so the return operands survive.
            LocalBuilder[]? funcResultLocals = null;
            if (_functionHasTryTable && funcResultTypes.Length > 0)
            {
                funcResultLocals = new LocalBuilder[funcResultTypes.Length];
                for (int i = 0; i < funcResultTypes.Length; i++)
                    funcResultLocals[i] = _il.DeclareLocal(funcResultClrTypes[i]);
            }

            _blockStack.Push(new EmitBlock
            {
                BranchTarget = funcEndLabel,
                Kind = WasmOpCode.Block,
                StackHeight = 0,
                LabelArity = _funcInst.Type.ResultType.Arity,
                ResultClrTypes = funcResultClrTypes,
                ResultLocals = funcResultLocals,
                OpeningTryDepth = 0
            });

            // Emit instructions
            foreach (var inst in _funcInst.Body.Instructions)
            {
                EmitInstruction(_il, inst);
            }

            _blockStack.Pop();
            _il.MarkLabel(funcEndLabel);

            // If shuttle locals were allocated for the function-level block
            // (try_table present), reload results from them. Fall-through from
            // the function body already stored into them via EmitExcessCleanup.
            if (funcResultLocals != null)
            {
                for (int i = 0; i < funcResultLocals.Length; i++)
                    _il.Emit(OpCodes.Ldloc, funcResultLocals[i]);
            }

            // Boundary: at function return, wrap ref-typed results to Value
            // for the signature boundary (doc 1 §3.2, doc 2 §3) and, for
            // multi-result functions, store results r1..r_{N-1} through the
            // byref out-params declared in CreateMethodStub. Single-result
            // ref returns just wrap inline before Ret.
            if (funcResultTypes.Length == 1)
            {
                if (IsGcRef(funcResultTypes[0]))
                {
                    // Wrap with the declared result type so the returned
                    // Value.Type matches (e.g. anyref-returning fn yields
                    // Value with Type=Any, not the derived type of the
                    // actual object).
                    _il.Emit(OpCodes.Ldc_I4, (int)funcResultTypes[0]);
                    _il.Emit(OpCodes.Call, typeof(Emitters.GcRuntimeHelpers).GetMethod(
                        nameof(Emitters.GcRuntimeHelpers.WrapRefAs),
                        BindingFlags.Public | BindingFlags.Static)!);
                }
            }
            else if (funcResultTypes.Length > 1)
            {
                EmitMultiResultReturn(funcResultTypes);
            }

            // Emit a trailing ret so that branches to funcEndLabel have a
            // valid terminator. The function End instruction also emits ret
            // inside the body, but funcEndLabel is placed after the body —
            // code that branches here (br/br_if/br_table targeting label 0)
            // would otherwise land at the end of the method with no ret.
            _il.Emit(OpCodes.Ret);

            return true;
        }

        /// <summary>
        /// Shuttle multi-value results from the CIL stack to the method's
        /// byref out-params and leave result[0] on the stack for the return.
        ///
        /// The method signature (CreateMethodStub) is:
        ///   result[0] Method(ThinContext ctx, param0..paramN-1,
        ///                    result[1]* out, result[2]* out, …)
        ///
        /// At entry, stack is [r0, r1, …, r_{M-1}] with r_{M-1} on top. We
        /// spill r1..r_{M-1} into object/typed locals (internal representation),
        /// then for each r_i (i≥1) load the out-param byref + the spilled
        /// value (wrapping object→Value for ref types) and Stind through the
        /// byref. r0 is left on the stack, wrapped if it is a ref type.
        /// </summary>
        private void EmitMultiResultReturn(ValType[] funcResultTypes)
        {
            int n = funcResultTypes.Length;

            // Spill the top (n-1) results to temps (reverse order — top first).
            var temps = new LocalBuilder[n];
            for (int i = n - 1; i >= 1; i--)
            {
                var internalType = InternalType(funcResultTypes[i]);
                temps[i] = _il.DeclareLocal(internalType);
                _il.Emit(OpCodes.Stloc, temps[i]);
            }
            // r0 remains on the stack. Wrap if ref.
            if (IsGcRef(funcResultTypes[0]))
            {
                _il.Emit(OpCodes.Call, typeof(Emitters.GcRuntimeHelpers).GetMethod(
                    nameof(Emitters.GcRuntimeHelpers.WrapRef),
                    BindingFlags.Public | BindingFlags.Static)!);
            }

            // Store r1..r_{n-1} through out-params. Out-param i sits at CIL
            // arg index (1 + paramCount + (i-1)) = paramCount + i. Paramcount
            // excludes the ThinContext.
            for (int i = 1; i < n; i++)
            {
                _il.Emit(OpCodes.Ldarg, _paramCount + i);
                _il.Emit(OpCodes.Ldloc, temps[i]);
                if (IsGcRef(funcResultTypes[i]))
                {
                    _il.Emit(OpCodes.Call, typeof(Emitters.GcRuntimeHelpers).GetMethod(
                        nameof(Emitters.GcRuntimeHelpers.WrapRef),
                        BindingFlags.Public | BindingFlags.Static)!);
                }
                EmitStindForSignature(_il, funcResultTypes[i]);
            }
        }

        /// <summary>
        /// Emit the Stind.* variant matching the signature representation of
        /// <paramref name="type"/>. Scalars use their size-specific Stind;
        /// everything else (Value-typed ref, v128, exnref at boundary) uses
        /// `Stobj Value` since <see cref="ModuleTranspiler.MapValType"/>
        /// maps those to <c>Value</c>.
        /// </summary>
        private static void EmitStindForSignature(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32: il.Emit(OpCodes.Stind_I4); break;
                case ValType.I64: il.Emit(OpCodes.Stind_I8); break;
                case ValType.F32: il.Emit(OpCodes.Stind_R4); break;
                case ValType.F64: il.Emit(OpCodes.Stind_R8); break;
                default: il.Emit(OpCodes.Stobj, typeof(Value)); break;
            }
        }

        /// <summary>
        /// Emit IL for a single instruction. This is the central dispatch that
        /// is also passed as a delegate to ControlEmitter for recursive emission.
        /// </summary>
        private void EmitInstruction(ILGenerator il, InstructionBase inst)
        {
            // Look up precomputed stack analysis for this instruction
            _currentInfo = _stackAnalysis.Get(inst);

            // Sync validator with authoritative StackAnalysis pre-pass.
            // Always reset height at instruction boundaries — block nesting
            // makes continuous height tracking across compound instructions
            // impractical. Type assertions WITHIN emitters (push/pop with
            // expected types) catch the actual bugs; the height reset ensures
            // each emitter starts from a correct baseline.
            //
            // Order matters: call Reset BEFORE clearing the unreachable flag
            // so Reset can observe that the prior position was unreachable
            // (dead-code types must not leak past an instruction boundary).
            if (_currentInfo != null && !_currentInfo.Unreachable)
            {
                _cilValidator.Reset(_currentInfo.StackHeightBefore);
                _cilValidator.SetReachable();
            }
            else if (_currentInfo?.Unreachable == true)
            {
                _cilValidator.SetUnreachable();
            }

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
                    }, _cilValidator);
                return;
            }

            var op = inst.Op.x00;

            // Exception handling (doc 1 §13, doc 2 §14).
            if (ExceptionEmitter.CanEmit(op))
            {
                switch (op)
                {
                    case WasmOpCode.Throw:
                        ExceptionEmitter.EmitThrow(il, (InstThrow)inst, _moduleInst);
                        // throw is an unconditional terminator.
                        _cilValidator.SetUnreachable();
                        break;
                    case WasmOpCode.ThrowRef:
                        // Pop exnref (WasmException on internal stack, doc 1 §2.1).
                        _cilValidator.Pop(typeof(WasmException), "throw_ref");
                        ExceptionEmitter.EmitThrowRef(il);
                        _cilValidator.SetUnreachable();
                        break;
                    case WasmOpCode.TryTable:
                        ExceptionEmitter.EmitTryTable(il, (InstTryTable)inst, _blockStack,
                            EmitInstruction, _moduleInst,
                            inc => _tryDepth += inc,
                            dec => _tryDepth -= dec);
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
                GcEmitter.Emit(il, inst, inst.Op.xFB, _gcTypes, _moduleInst, branchResolver, _cilValidator);
                return;
            }

            // 0xFC prefix (extensions: sat truncation, bulk memory)
            if (op == WasmOpCode.FC)
            {
                TrackExtStackEffect(inst, before: true);
                ExtEmitter.Emit(il, inst, inst.Op.xFC);
                TrackExtStackEffect(inst, before: false);
                return;
            }

            // 0xFD prefix (SIMD) — pop the exact consumed arity and push the
            // typed result so the validator keeps types for items BELOW the
            // SIMD op intact. TrackGenericStackDiff would Reset the whole
            // stack to Object placeholders, which breaks downstream
            // representation-sensitive dispatch (e.g. `select` then peeks
            // Object and picks SelectObject for an i32 operand).
            if (op == WasmOpCode.FD)
            {
                var simdOp = inst.Op.xFD;
                bool isSimdStore = IsSimdStore(simdOp);
                int outputCount = isSimdStore ? 0 : 1;
                int inputCount = outputCount - inst.StackDiff;
                for (int i = 0; i < inputCount; i++)
                    _cilValidator.Pop(context: "simd input");
                SimdEmitter.Emit(il, inst, simdOp, _options, _diagnostics, _funcInst.Name);
                if (outputCount > 0)
                    _cilValidator.Push(SimdResultType(simdOp));
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
                TrackNumericStackEffect(op);
                return;
            }

            // Variable/parametric instructions (locals, drop, select)
            if (VariableEmitter.CanEmit(op))
            {
                VariableEmitter.Emit(il, inst, op, _paramCount, _locals,
                    _paramClrTypes, _paramShadowLocals, _cilValidator);
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
                        GlobalEmitter.EmitGlobalGet(il, getInst, globalType, _moduleInst);
                        _cilValidator.Push(InternalType(globalType));
                        return;
                    }
                    case WasmOpCode.GlobalSet:
                    {
                        var setInst = (InstGlobalSet)inst;
                        var globalType = ResolveGlobalType(setInst.GetIndex());
                        _cilValidator.Pop(InternalType(globalType), "global.set");
                        GlobalEmitter.EmitGlobalSet(il, setInst, globalType, _moduleInst);
                        return;
                    }
                }
            }

            // Memory operations
            if (MemoryEmitter.CanEmit(op))
            {
                MemoryEmitter.Emit(il, inst, op);
                TrackMemoryStackEffect(op);
                return;
            }

            // ref.is_null / ref.eq / ref.as_non_null: intercept and dispatch on
            // operand representation (object for GC refs, Value for funcref /
            // externref). Doc 2 §1. The dispatch happens here — not in
            // TableRefEmitter — so we can use CilValidator.Peek for typing.
            if (op == WasmOpCode.RefIsNull || op == WasmOpCode.RefEq || op == WasmOpCode.RefAsNonNull)
            {
                EmitRefObjectAware(il, op);
                return;
            }

            // ref.null needs the target heap type to choose representation.
            if (op == WasmOpCode.RefNull)
            {
                EmitRefNullTyped(il, (Wacs.Core.Instructions.Reference.InstRefNull)inst);
                return;
            }

            // table.get / table.set: boundary-wrap for GC-ref tables.
            // Table storage is always Value; the internal stack uses object
            // for GC refs (doc 2 §3).
            if (op == WasmOpCode.TableGet)
            {
                var tg = (InstTableGet)inst;
                var elemType = ResolveTableElementType(tg.TableIndex);
                _cilValidator.Pop(context: "table.get idx"); // i32 or i64
                TableRefEmitter.Emit(il, inst, op); // emits helper returning Value
                if (IsGcRef(elemType))
                {
                    il.Emit(OpCodes.Call, typeof(Emitters.GcRuntimeHelpers).GetMethod(
                        nameof(Emitters.GcRuntimeHelpers.UnwrapRef),
                        BindingFlags.Public | BindingFlags.Static)!);
                    _cilValidator.Push(typeof(object));
                }
                else
                {
                    _cilValidator.Push(typeof(Value));
                }
                return;
            }
            if (op == WasmOpCode.TableSet)
            {
                var ts = (InstTableSet)inst;
                var elemType = ResolveTableElementType(ts.TableIndex);
                bool isGc = IsGcRef(elemType);

                // Stack (bottom→top): [idx, val]. val is object for GC tables,
                // Value otherwise. Spill both to temps of correct types, then
                // reload in the order the helper expects, wrapping val for GC.
                _cilValidator.Pop(isGc ? typeof(object) : typeof(Value), "table.set val");
                _cilValidator.Pop(context: "table.set idx"); // i32 or i64

                var valLocal = il.DeclareLocal(isGc ? typeof(object) : typeof(Value));
                il.Emit(OpCodes.Stloc, valLocal);
                var idxLocal = il.DeclareLocal(typeof(int));
                il.Emit(OpCodes.Stloc, idxLocal);
                il.Emit(OpCodes.Ldarg_0); // ctx
                il.Emit(OpCodes.Ldc_I4, ts.TableIndex);
                il.Emit(OpCodes.Ldloc, idxLocal);
                il.Emit(OpCodes.Ldloc, valLocal);
                if (isGc)
                {
                    il.Emit(OpCodes.Call, typeof(Emitters.GcRuntimeHelpers).GetMethod(
                        nameof(Emitters.GcRuntimeHelpers.WrapRef),
                        BindingFlags.Public | BindingFlags.Static)!);
                }
                il.Emit(OpCodes.Call, typeof(Emitters.TableRefHelpers).GetMethod(
                    nameof(Emitters.TableRefHelpers.TableSet),
                    BindingFlags.Public | BindingFlags.Static)!);
                return;
            }

            // Table and reference instructions
            if (TableRefEmitter.CanEmit(op))
            {
                TrackTableRefStackEffect(op, inst, before: true);
                TableRefEmitter.Emit(il, inst, op);
                TrackTableRefStackEffect(op, inst, before: false);
                return;
            }

            // Function calls — resolve strategy then emit
            if (CallEmitter.CanEmit(op))
            {
                var site = CallEmitter.ResolveCallSite(
                    inst, op, _siblingFunctions, _importCount, _moduleInst, _allFunctionTypes);
                TrackCallStackEffect(site, before: true);
                CallEmitter.EmitCallSite(il, site, _siblingMethods, _moduleInst, _options);
                TrackCallStackEffect(site, before: false);
                return;
            }

            // Control flow
            switch (op)
            {
                case WasmOpCode.Unreachable:
                    ControlEmitter.EmitUnreachable(il);
                    _cilValidator.SetUnreachable();
                    break;

                case WasmOpCode.Nop:
                    il.Emit(OpCodes.Nop);
                    break;

                case WasmOpCode.Block:
                {
                    int sh = _currentInfo?.StackHeightBefore ?? 0;
                    ControlEmitter.EmitBlock(il, (InstBlock)inst, _blockStack, EmitInstruction,
                        sh, _moduleInst, _tryDepth, _functionHasTryTable);
                    break;
                }

                case WasmOpCode.Loop:
                {
                    int sh = _currentInfo?.StackHeightBefore ?? 0;
                    ControlEmitter.EmitLoop(il, (InstLoop)inst, _blockStack, EmitInstruction,
                        sh, _moduleInst, _tryDepth);
                    break;
                }

                case WasmOpCode.If:
                {
                    int sh = (_currentInfo?.StackHeightBefore ?? 1) - 1;
                    ControlEmitter.EmitIf(il, (InstIf)inst, _blockStack, EmitInstruction,
                        sh, _moduleInst, _tryDepth, _functionHasTryTable);
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
                        // If funcBlock owns shuttle locals, store results there
                        // before branching so the label merges with an empty
                        // stack (doc 2 §§2, 14).
                        EmitExcessCleanup(il, 0, funcBlock);
                        BranchBridge.EmitBranch(il, funcBlock, _tryDepth);
                    }
                    // Non-function End is handled by the block/loop/if emitters.
                    break;

                case WasmOpCode.Return:
                {
                    EmitBlock funcBlock = null!;
                    foreach (var block in _blockStack)
                        funcBlock = block;

                    int excess = _currentInfo?.Excess ?? 0;
                    EmitExcessCleanup(il, excess, funcBlock);
                    // Route through funcEndLabel where the multi-result spill
                    // + Ret epilogue lives when the function returns more than
                    // one value — a direct Ret would only consume r0 and leave
                    // r1..r_{N-1} on the stack, producing invalid IL. Shuttle
                    // locals (allocated whenever the function contains a
                    // try_table) also require the epilogue because cross-try
                    // branches emit Leave which empties the stack. Otherwise
                    // (single-result, no shuttle), a direct Ret matches the
                    // CLR's try-frame unwind behavior.
                    if (funcBlock.ResultLocals != null || _funcInst.Type.ResultType.Types.Length > 1)
                    {
                        BranchBridge.EmitBranch(il, funcBlock, _tryDepth);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ret);
                    }
                    _cilValidator.SetUnreachable();
                    break;
                }

                case WasmOpCode.Br:
                {
                    var target = ControlEmitter.PeekLabel(_blockStack, ((InstBranch)inst).Label);
                    int excess = _currentInfo?.Excess ?? 0;
                    EmitExcessCleanup(il, excess, target);
                    BranchBridge.EmitBranch(il, target, _tryDepth);
                    _cilValidator.SetUnreachable();
                    break;
                }

                case WasmOpCode.BrIf:
                {
                    _cilValidator.Pop(typeof(int), "br_if.condition");
                    int excess = _currentInfo?.Excess ?? 0;
                    ControlEmitter.EmitBrIf(il, (InstBranchIf)inst, _blockStack, excess);
                    break;
                }

                case WasmOpCode.BrTable:
                {
                    _cilValidator.Pop(typeof(int), "br_table.index");
                    int excess = _currentInfo?.Excess ?? 0;
                    ControlEmitter.EmitBrTable(il, (InstBranchTable)inst, _blockStack,
                        excess, _currentInfo?.BrTableExcess);
                    _cilValidator.SetUnreachable();
                    break;
                }

                case WasmOpCode.BrOnNull:
                {
                    // br_on_null L: pop ref, if null branch to L (ref consumed),
                    // else push ref back.
                    //
                    // Representation dispatch (doc 2 §1):
                    //   object / WasmException — plain CLR references, can Dup
                    //     at merge points. Inline: Dup; Brfalse skip; (null
                    //     path: Pop; excess+branch); mark skip.
                    //   Value (funcref/externref/v128) — struct containing a
                    //     managed ref. Merge-point restricted (doc 2 §1);
                    //     spill to local, test via RefIsNull helper.
                    var brInst = (Wacs.Core.Instructions.Reference.InstBrOnNull)inst;
                    var target = ControlEmitter.PeekLabel(_blockStack, brInst.Label);
                    int excess = _currentInfo?.Excess ?? 0;
                    var top = _cilValidator.Peek();
                    bool isPlainRef = top == typeof(object) || top == typeof(WasmException);

                    if (isPlainRef)
                    {
                        var skip = il.DefineLabel();
                        il.Emit(OpCodes.Dup);
                        il.Emit(OpCodes.Brtrue, skip);
                        il.Emit(OpCodes.Pop); // discard the null we just duped
                        EmitExcessCleanup(il, excess, target);
                        BranchBridge.EmitBranch(il, target, _tryDepth);
                        il.MarkLabel(skip);
                        // non-null path: original ref still on stack.
                    }
                    else
                    {
                        var refLocal = il.DeclareLocal(typeof(Value));
                        il.Emit(OpCodes.Stloc, refLocal);
                        il.Emit(OpCodes.Ldloc, refLocal);
                        il.Emit(OpCodes.Call, typeof(Emitters.TableRefHelpers).GetMethod(
                            nameof(Emitters.TableRefHelpers.RefIsNull),
                            BindingFlags.Public | BindingFlags.Static)!);
                        var skip = il.DefineLabel();
                        il.Emit(OpCodes.Brfalse, skip); // not null → skip
                        EmitExcessCleanup(il, excess, target);
                        BranchBridge.EmitBranch(il, target, _tryDepth);
                        il.MarkLabel(skip);
                        il.Emit(OpCodes.Ldloc, refLocal);
                    }
                    break;
                }

                case WasmOpCode.BrOnNonNull:
                {
                    // br_on_non_null L: pop ref, if non-null branch to L (ref
                    // on stack), else consume. Same representation dispatch
                    // as BrOnNull.
                    var brInst = (Wacs.Core.Instructions.Reference.InstBrOnNonNull)inst;
                    var target = ControlEmitter.PeekLabel(_blockStack, brInst.Label);
                    int excess = _currentInfo?.Excess ?? 0;
                    var top = _cilValidator.Peek();
                    bool isPlainRef = top == typeof(object) || top == typeof(WasmException);

                    if (isPlainRef)
                    {
                        var skip = il.DefineLabel();
                        il.Emit(OpCodes.Dup);
                        il.Emit(OpCodes.Brfalse, skip);
                        // non-null path: original ref still on stack, branch.
                        EmitExcessCleanup(il, excess, target);
                        BranchBridge.EmitBranch(il, target, _tryDepth);
                        il.MarkLabel(skip);
                        // null path: Pop the duped null.
                        il.Emit(OpCodes.Pop);
                    }
                    else
                    {
                        var refLocal = il.DeclareLocal(typeof(Value));
                        il.Emit(OpCodes.Stloc, refLocal);
                        il.Emit(OpCodes.Ldloc, refLocal);
                        il.Emit(OpCodes.Call, typeof(Emitters.TableRefHelpers).GetMethod(
                            nameof(Emitters.TableRefHelpers.RefIsNull),
                            BindingFlags.Public | BindingFlags.Static)!);
                        var skip = il.DefineLabel();
                        il.Emit(OpCodes.Brtrue, skip); // null → skip
                        il.Emit(OpCodes.Ldloc, refLocal);
                        EmitExcessCleanup(il, excess, target);
                        BranchBridge.EmitBranch(il, target, _tryDepth);
                        il.MarkLabel(skip);
                    }
                    break;
                }

                default:
                    throw new TranspilerException(
                        $"FunctionCodegen: no emitter for opcode {inst.Op.GetMnemonic()} (0x{(byte)op:X2})");
            }
        }

        /// <summary>
        /// Recursively check if the function body contains `try_table`. When
        /// true, all labels must use shuttle locals because cross-try branches
        /// emit `Leave` which empties the CIL eval stack (doc 2 §14).
        /// </summary>
        private static bool ContainsTryTable(IEnumerable<InstructionBase> instructions)
        {
            foreach (var inst in instructions)
            {
                if (inst is InstTryTable) return true;
                if (inst is IBlockInstruction blockInst)
                {
                    for (int i = 0; i < blockInst.Count; i++)
                    {
                        var block = blockInst.GetBlock(i);
                        if (ContainsTryTable(block.Instructions)) return true;
                    }
                }
            }
            return false;
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
        /// <summary>
        /// Prepare the stack for a branch to target:
        /// - If target owns ResultLocals, store carried values there (stack has target.LabelArity
        ///   values above `excess` dead values; store carried, pop excess). Caller then emits Br
        ///   with empty stack.
        /// - Otherwise, shuttle carried values past excess on the stack (leave values on stack
        ///   for the Br to carry).
        /// </summary>
        private static void EmitExcessCleanup(ILGenerator il, int excess, EmitBlock target)
        {
            int arity = target.LabelArity;

            // When target uses ResultLocals, always route through them
            // (even excess=0 — the values must land in locals, not on stack).
            if (target.ResultLocals != null && arity > 0)
            {
                for (int i = arity - 1; i >= 0; i--)
                    il.Emit(OpCodes.Stloc, target.ResultLocals[i]);
                for (int i = 0; i < excess; i++)
                    il.Emit(OpCodes.Pop);
                return;
            }

            if (excess <= 0) return;

            if (arity > 0)
            {
                // Shuttle carried values past excess using temp locals
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
        /// Stack: [ref] — object for GC refs (doc 2 §1); Value for funcref/externref.
        /// br_on_cast: if ref matches targetType, branch (carry ref); else fall through (ref on stack)
        /// br_on_cast_fail: if ref doesn't match, branch (carry ref); else fall through (ref on stack)
        ///
        /// Object operands Dup at the merge point (safe per doc 2 §1);
        /// Value operands spill to a local to avoid the merge-point Dup issue.
        /// </summary>
        private void EmitBrOnCastWithExcess(ILGenerator il, int labelDepth,
            ValType targetType, bool castFail)
        {
            var target = ControlEmitter.PeekLabel(_blockStack, labelDepth);
            int excess = _currentInfo?.Excess ?? 0;
            bool isObjectRef = _cilValidator.Peek() == typeof(object);

            if (isObjectRef)
            {
                // Dup, test (consumes the duped ref), branch on result; the
                // original ref remains on the stack for both paths.
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, (int)targetType);
                il.Emit(OpCodes.Ldc_I4, targetType.IsNullable() ? 1 : 0);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, typeof(Emitters.GcRuntimeHelpers).GetMethod(
                    nameof(Emitters.GcRuntimeHelpers.RefTestObject),
                    BindingFlags.Public | BindingFlags.Static)!);

                var skip = il.DefineLabel();
                il.Emit(castFail ? OpCodes.Brtrue : OpCodes.Brfalse, skip);
                EmitExcessCleanup(il, excess, target);
                BranchBridge.EmitBranch(il, target, _tryDepth);
                il.MarkLabel(skip);
                // Fall-through: original ref still on stack.
                return;
            }

            // Value operand: spill to local to bypass merge-point Dup issue.
            var refLocal = il.DeclareLocal(typeof(Value));
            il.Emit(OpCodes.Stloc, refLocal);
            il.Emit(OpCodes.Ldloc, refLocal);
            il.Emit(OpCodes.Ldc_I4, (int)targetType);
            il.Emit(OpCodes.Ldc_I4, targetType.IsNullable() ? 1 : 0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(Emitters.GcRuntimeHelpers).GetMethod(
                nameof(Emitters.GcRuntimeHelpers.RefTestValue),
                BindingFlags.Public | BindingFlags.Static)!);

            var skipLabel = il.DefineLabel();
            il.Emit(castFail ? OpCodes.Brtrue : OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Ldloc, refLocal);
            EmitExcessCleanup(il, excess, target);
            BranchBridge.EmitBranch(il, target, _tryDepth);
            il.MarkLabel(skipLabel);
            il.Emit(OpCodes.Ldloc, refLocal);
        }

        /// <summary>
        /// Emit ref.is_null / ref.eq / ref.as_non_null with operand-type
        /// dispatch. GC refs (object on stack) use inline IL; funcref /
        /// externref (Value on stack) use the Value-based helpers.
        /// Doc 2 §1 establishes the dual representation.
        /// </summary>
        private void EmitRefObjectAware(ILGenerator il, WasmOpCode op)
        {
            var topType = _cilValidator.Peek();
            bool isObject = topType == typeof(object);

            switch (op)
            {
                case WasmOpCode.RefIsNull:
                    _cilValidator.Pop(isObject ? typeof(object) : typeof(Value), "ref.is_null");
                    if (isObject)
                    {
                        il.Emit(OpCodes.Ldnull);
                        il.Emit(OpCodes.Ceq);
                    }
                    else
                    {
                        il.Emit(OpCodes.Call, typeof(Emitters.TableRefHelpers).GetMethod(
                            nameof(Emitters.TableRefHelpers.RefIsNull),
                            BindingFlags.Public | BindingFlags.Static)!);
                    }
                    _cilValidator.Push(typeof(int));
                    break;

                case WasmOpCode.RefEq:
                    // Both operands share the same stack type by WASM validation
                    // (both anyref / both funcref / etc.).
                    _cilValidator.Pop(isObject ? typeof(object) : typeof(Value), "ref.eq b");
                    _cilValidator.Pop(isObject ? typeof(object) : typeof(Value), "ref.eq a");
                    il.Emit(OpCodes.Call, typeof(Emitters.GcRuntimeHelpers).GetMethod(
                        isObject
                            ? nameof(Emitters.GcRuntimeHelpers.RefEqObject)
                            : nameof(Emitters.TableRefHelpers.RefEq),
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        isObject
                            ? new[] { typeof(object), typeof(object) }
                            : new[] { typeof(Value), typeof(Value) },
                        null)!);
                    _cilValidator.Push(typeof(int));
                    break;

                case WasmOpCode.RefAsNonNull:
                    _cilValidator.Pop(isObject ? typeof(object) : typeof(Value), "ref.as_non_null");
                    if (isObject)
                    {
                        // Inline null check: dup; brtrue skip; trap; mark skip.
                        var okLabel = il.DefineLabel();
                        il.Emit(OpCodes.Dup);
                        il.Emit(OpCodes.Brtrue, okLabel);
                        il.Emit(OpCodes.Ldstr, "null reference");
                        il.Emit(OpCodes.Newobj,
                            typeof(TrapException).GetConstructor(new[] { typeof(string) })!);
                        il.Emit(OpCodes.Throw);
                        il.MarkLabel(okLabel);
                    }
                    else
                    {
                        il.Emit(OpCodes.Call, typeof(Emitters.TableRefHelpers).GetMethod(
                            nameof(Emitters.TableRefHelpers.RefAsNonNull),
                            BindingFlags.Public | BindingFlags.Static)!);
                    }
                    _cilValidator.Push(isObject ? typeof(object) : typeof(Value));
                    break;
            }
        }

        /// <summary>
        /// Emit ref.null with representation chosen by heap type.
        /// GC ref types push null (object); funcref/externref push Value.Null.
        /// Doc 1 §2.5.
        /// </summary>
        private void EmitRefNullTyped(ILGenerator il, Wacs.Core.Instructions.Reference.InstRefNull inst)
        {
            if (IsGcRef(inst.RefType))
            {
                il.Emit(OpCodes.Ldnull);
                _cilValidator.Push(typeof(object));
            }
            else if (ModuleTranspiler.IsExnRefType(inst.RefType))
            {
                il.Emit(OpCodes.Ldnull);
                _cilValidator.Push(typeof(WasmException));
            }
            else
            {
                // Value-typed null ref (funcref / externref / concrete func type).
                // Use the pre-constructed static field when available — avoids
                // emitting `Ldc_I4 (int)ValType; Call Value.Null` which passed
                // a large-int enum value that interacted badly with the JIT
                // for defType variants. For other types, fall through to
                // Value.Null(type).
                var refType = inst.RefType;
                FieldInfo? staticField = null;
                if (refType == ValType.FuncRef)
                    staticField = typeof(Value).GetField(nameof(Value.NullFuncRef));
                else if (refType == ValType.ExternRef)
                    staticField = typeof(Value).GetField(nameof(Value.NullExternRef));

                if (staticField != null)
                {
                    il.Emit(OpCodes.Ldsfld, staticField);
                }
                else
                {
                    // Construct `new Value(type)` via Newobj — the ctor
                    // handles nullable ref types uniformly (Data.Ptr =
                    // long.MinValue, Data.Set = true) and leaves the new
                    // Value on the stack.
                    il.Emit(OpCodes.Ldc_I4, (int)refType);
                    il.Emit(OpCodes.Newobj,
                        typeof(Value).GetConstructor(new[] { typeof(ValType) })!);
                }
                _cilValidator.Push(typeof(Value));
            }
        }


        /// <summary>
        /// Track the CIL stack effect of a numeric instruction in the validator.
        /// Constants push a typed value; unary ops pop+push; binary ops pop 2 push 1.
        /// </summary>
        /// <summary>Track memory instruction stack effects.</summary>
        private void TrackMemoryStackEffect(WasmOpCode op)
        {
            // Address type: i32 for memory32, i64 for memory64.
            // We pop without type assertion since both are valid.
            switch (op)
            {
                // Loads: [addr] → [T]
                case WasmOpCode.I32Load or WasmOpCode.I32Load8S or WasmOpCode.I32Load8U
                    or WasmOpCode.I32Load16S or WasmOpCode.I32Load16U:
                    _cilValidator.Pop(context: "mem.load addr");
                    _cilValidator.Push(typeof(int));
                    break;
                case WasmOpCode.I64Load or WasmOpCode.I64Load8S or WasmOpCode.I64Load8U
                    or WasmOpCode.I64Load16S or WasmOpCode.I64Load16U
                    or WasmOpCode.I64Load32S or WasmOpCode.I64Load32U:
                    _cilValidator.Pop(context: "mem.load addr");
                    _cilValidator.Push(typeof(long));
                    break;
                case WasmOpCode.F32Load:
                    _cilValidator.Pop(context: "f32.load addr");
                    _cilValidator.Push(typeof(float));
                    break;
                case WasmOpCode.F64Load:
                    _cilValidator.Pop(context: "f64.load addr");
                    _cilValidator.Push(typeof(double));
                    break;

                // Stores: [addr, T val] → []
                case WasmOpCode.I32Store or WasmOpCode.I32Store8 or WasmOpCode.I32Store16:
                    _cilValidator.Pop(typeof(int), "i32.store val");
                    _cilValidator.Pop(context: "mem.store addr");
                    break;
                case WasmOpCode.I64Store or WasmOpCode.I64Store8 or WasmOpCode.I64Store16
                    or WasmOpCode.I64Store32:
                    _cilValidator.Pop(typeof(long), "i64.store val");
                    _cilValidator.Pop(context: "mem.store addr");
                    break;
                case WasmOpCode.F32Store:
                    _cilValidator.Pop(typeof(float), "f32.store val");
                    _cilValidator.Pop(context: "mem.store addr");
                    break;
                case WasmOpCode.F64Store:
                    _cilValidator.Pop(typeof(double), "f64.store val");
                    _cilValidator.Pop(context: "mem.store addr");
                    break;

                // memory.size: [] → [i32/i64]
                case WasmOpCode.MemorySize:
                    _cilValidator.Push(typeof(object)); // i32 or i64 depending on memory type
                    break;
                // memory.grow: [delta] → [old_size]
                case WasmOpCode.MemoryGrow:
                    _cilValidator.Pop(context: "memory.grow delta");
                    _cilValidator.Push(typeof(object)); // i32 or i64
                    break;
            }
        }

        /// <summary>Track table/ref instruction stack effects. Ref-type
        /// interactions with representation (GC ref object vs funcref/externref
        /// Value) are handled by intercepting emitters in EmitInstruction —
        /// this only tracks table.get/set (via table type) and ref.func.</summary>
        private void TrackTableRefStackEffect(WasmOpCode op, InstructionBase inst, bool before)
        {
            if (before)
            {
                switch (op)
                {
                    case WasmOpCode.TableGet:
                        _cilValidator.Pop(context: "table.get idx"); break; // i32 or i64
                    case WasmOpCode.TableSet:
                    {
                        var ti = (Wacs.Core.Instructions.InstTableSet)inst;
                        var elemType = ResolveTableElementType(ti.TableIndex);
                        _cilValidator.Pop(InternalType(elemType), "table.set val");
                        _cilValidator.Pop(context: "table.set idx"); break; // i32 or i64
                    }
                }
            }
            else // after
            {
                switch (op)
                {
                    case WasmOpCode.TableGet:
                    {
                        var ti = (Wacs.Core.Instructions.InstTableGet)inst;
                        var elemType = ResolveTableElementType(ti.TableIndex);
                        _cilValidator.Push(InternalType(elemType));
                        break;
                    }
                    case WasmOpCode.RefFunc:
                        // funcref stays as Value (doc 2 §1 invariant 3).
                        _cilValidator.Push(typeof(Value)); break;
                }
            }
        }

        /// <summary>Resolve a table's element ValType from its module index.
        /// Used by track-stack-effect to pick the internal CIL representation
        /// for table.get / table.set values.</summary>
        private ValType ResolveTableElementType(int tableIdx)
        {
            // Tables are indexed with imports first, then local tables.
            int importedTableCount = 0;
            foreach (var import in _moduleInst.Repr.Imports)
            {
                if (import.Desc is Wacs.Core.Module.ImportDesc.TableDesc td)
                {
                    if (importedTableCount == tableIdx)
                        return td.TableDef.ElementType;
                    importedTableCount++;
                }
            }
            int localIdx = tableIdx - importedTableCount;
            if (localIdx >= 0 && localIdx < _moduleInst.Repr.Tables.Count)
                return _moduleInst.Repr.Tables[localIdx].ElementType;
            return ValType.FuncRef; // fallback
        }

        /// <summary>Track call instruction stack effects from resolved call site.
        /// Pops/pushes use INTERNAL types (object for GC refs, Value for
        /// funcref/externref/v128) matching the internal CIL stack. The
        /// wrap-to-Value at the call-site boundary is handled inside
        /// CallEmitter (doc 2 §3).</summary>
        private void TrackCallStackEffect(CallSite site, bool before)
        {
            if (before)
            {
                // Indirect/ref calls pop a table index or funcref. Table
                // index is i32 for table32 or i64 for table64 — untyped
                // pop for height-only tracking (CallEmitter handles the
                // actual Conv_I4 / Conv_I8 per table type).
                if (site.Strategy == CallStrategy.TableIndirect)
                    _cilValidator.Pop(context: "call_indirect.idx");
                else if (site.Strategy == CallStrategy.RefDispatch)
                    _cilValidator.Pop(typeof(Value), "call_ref.funcref");

                // Pop arguments in reverse order
                for (int i = site.FuncType.ParameterTypes.Arity - 1; i >= 0; i--)
                {
                    var ptype = InternalType(site.FuncType.ParameterTypes.Types[i]);
                    _cilValidator.Pop(ptype, $"call.param[{i}]");
                }
            }
            else // after
            {
                // Push results
                for (int i = 0; i < site.FuncType.ResultType.Arity; i++)
                {
                    var rtype = InternalType(site.FuncType.ResultType.Types[i]);
                    _cilValidator.Push(rtype);
                }
            }
        }

        private void TrackNumericStackEffect(WasmOpCode op)
        {
            switch (op)
            {
                // Constants: push typed value
                case WasmOpCode.I32Const: _cilValidator.Push(typeof(int)); break;
                case WasmOpCode.I64Const: _cilValidator.Push(typeof(long)); break;
                case WasmOpCode.F32Const: _cilValidator.Push(typeof(float)); break;
                case WasmOpCode.F64Const: _cilValidator.Push(typeof(double)); break;

                // i32 test (eqz): i32 → i32
                case WasmOpCode.I32Eqz:
                    _cilValidator.Pop(typeof(int), "i32.eqz"); _cilValidator.Push(typeof(int)); break;
                // i64 test (eqz): i64 → i32
                case WasmOpCode.I64Eqz:
                    _cilValidator.Pop(typeof(long), "i64.eqz"); _cilValidator.Push(typeof(int)); break;

                // i32 binary: [i32, i32] → [i32]
                case WasmOpCode.I32Eq or WasmOpCode.I32Ne
                    or WasmOpCode.I32LtS or WasmOpCode.I32LtU
                    or WasmOpCode.I32GtS or WasmOpCode.I32GtU
                    or WasmOpCode.I32LeS or WasmOpCode.I32LeU
                    or WasmOpCode.I32GeS or WasmOpCode.I32GeU
                    or WasmOpCode.I32Add or WasmOpCode.I32Sub
                    or WasmOpCode.I32Mul or WasmOpCode.I32DivS
                    or WasmOpCode.I32DivU or WasmOpCode.I32RemS
                    or WasmOpCode.I32RemU or WasmOpCode.I32And
                    or WasmOpCode.I32Or or WasmOpCode.I32Xor
                    or WasmOpCode.I32Shl or WasmOpCode.I32ShrS
                    or WasmOpCode.I32ShrU or WasmOpCode.I32Rotl
                    or WasmOpCode.I32Rotr:
                    _cilValidator.Pop(typeof(int), "i32.binop");
                    _cilValidator.Pop(typeof(int), "i32.binop");
                    _cilValidator.Push(typeof(int));
                    break;

                // i32 unary: [i32] → [i32]
                case WasmOpCode.I32Clz or WasmOpCode.I32Ctz or WasmOpCode.I32Popcnt:
                    _cilValidator.Pop(typeof(int), "i32.unop");
                    _cilValidator.Push(typeof(int));
                    break;

                // i64 binary: [i64, i64] → [i64]
                case WasmOpCode.I64Add or WasmOpCode.I64Sub
                    or WasmOpCode.I64Mul or WasmOpCode.I64DivS
                    or WasmOpCode.I64DivU or WasmOpCode.I64RemS
                    or WasmOpCode.I64RemU or WasmOpCode.I64And
                    or WasmOpCode.I64Or or WasmOpCode.I64Xor
                    or WasmOpCode.I64Shl or WasmOpCode.I64ShrS
                    or WasmOpCode.I64ShrU or WasmOpCode.I64Rotl
                    or WasmOpCode.I64Rotr:
                    _cilValidator.Pop(typeof(long), "i64.binop");
                    _cilValidator.Pop(typeof(long), "i64.binop");
                    _cilValidator.Push(typeof(long));
                    break;

                // i64 compare: [i64, i64] → [i32]
                case WasmOpCode.I64Eq or WasmOpCode.I64Ne
                    or WasmOpCode.I64LtS or WasmOpCode.I64LtU
                    or WasmOpCode.I64GtS or WasmOpCode.I64GtU
                    or WasmOpCode.I64LeS or WasmOpCode.I64LeU
                    or WasmOpCode.I64GeS or WasmOpCode.I64GeU:
                    _cilValidator.Pop(typeof(long), "i64.relop");
                    _cilValidator.Pop(typeof(long), "i64.relop");
                    _cilValidator.Push(typeof(int));
                    break;

                // i64 unary: [i64] → [i64]
                case WasmOpCode.I64Clz or WasmOpCode.I64Ctz or WasmOpCode.I64Popcnt:
                    _cilValidator.Pop(typeof(long), "i64.unop");
                    _cilValidator.Push(typeof(long));
                    break;

                // f32 binary: [f32, f32] → [f32]
                case WasmOpCode.F32Add or WasmOpCode.F32Sub
                    or WasmOpCode.F32Mul or WasmOpCode.F32Div
                    or WasmOpCode.F32Min or WasmOpCode.F32Max
                    or WasmOpCode.F32Copysign:
                    _cilValidator.Pop(typeof(float), "f32.binop");
                    _cilValidator.Pop(typeof(float), "f32.binop");
                    _cilValidator.Push(typeof(float));
                    break;

                // f32 compare: [f32, f32] → [i32]
                case WasmOpCode.F32Eq or WasmOpCode.F32Ne
                    or WasmOpCode.F32Lt or WasmOpCode.F32Gt
                    or WasmOpCode.F32Le or WasmOpCode.F32Ge:
                    _cilValidator.Pop(typeof(float), "f32.relop");
                    _cilValidator.Pop(typeof(float), "f32.relop");
                    _cilValidator.Push(typeof(int));
                    break;

                // f32 unary: [f32] → [f32]
                case WasmOpCode.F32Abs or WasmOpCode.F32Neg
                    or WasmOpCode.F32Ceil or WasmOpCode.F32Floor
                    or WasmOpCode.F32Trunc or WasmOpCode.F32Nearest
                    or WasmOpCode.F32Sqrt:
                    _cilValidator.Pop(typeof(float), "f32.unop");
                    _cilValidator.Push(typeof(float));
                    break;

                // f64 binary: [f64, f64] → [f64]
                case WasmOpCode.F64Add or WasmOpCode.F64Sub
                    or WasmOpCode.F64Mul or WasmOpCode.F64Div
                    or WasmOpCode.F64Min or WasmOpCode.F64Max
                    or WasmOpCode.F64Copysign:
                    _cilValidator.Pop(typeof(double), "f64.binop");
                    _cilValidator.Pop(typeof(double), "f64.binop");
                    _cilValidator.Push(typeof(double));
                    break;

                // f64 compare: [f64, f64] → [i32]
                case WasmOpCode.F64Eq or WasmOpCode.F64Ne
                    or WasmOpCode.F64Lt or WasmOpCode.F64Gt
                    or WasmOpCode.F64Le or WasmOpCode.F64Ge:
                    _cilValidator.Pop(typeof(double), "f64.relop");
                    _cilValidator.Pop(typeof(double), "f64.relop");
                    _cilValidator.Push(typeof(int));
                    break;

                // f64 unary: [f64] → [f64]
                case WasmOpCode.F64Abs or WasmOpCode.F64Neg
                    or WasmOpCode.F64Ceil or WasmOpCode.F64Floor
                    or WasmOpCode.F64Trunc or WasmOpCode.F64Nearest
                    or WasmOpCode.F64Sqrt:
                    _cilValidator.Pop(typeof(double), "f64.unop");
                    _cilValidator.Push(typeof(double));
                    break;

                // Conversions
                case WasmOpCode.I32WrapI64:
                    _cilValidator.Pop(typeof(long), "i32.wrap"); _cilValidator.Push(typeof(int)); break;
                case WasmOpCode.I64ExtendI32S or WasmOpCode.I64ExtendI32U:
                    _cilValidator.Pop(typeof(int), "i64.extend"); _cilValidator.Push(typeof(long)); break;
                case WasmOpCode.I32TruncF32S or WasmOpCode.I32TruncF32U:
                    _cilValidator.Pop(typeof(float), "i32.trunc_f32"); _cilValidator.Push(typeof(int)); break;
                case WasmOpCode.I32TruncF64S or WasmOpCode.I32TruncF64U:
                    _cilValidator.Pop(typeof(double), "i32.trunc_f64"); _cilValidator.Push(typeof(int)); break;
                case WasmOpCode.I64TruncF32S or WasmOpCode.I64TruncF32U:
                    _cilValidator.Pop(typeof(float), "i64.trunc_f32"); _cilValidator.Push(typeof(long)); break;
                case WasmOpCode.I64TruncF64S or WasmOpCode.I64TruncF64U:
                    _cilValidator.Pop(typeof(double), "i64.trunc_f64"); _cilValidator.Push(typeof(long)); break;
                case WasmOpCode.F32ConvertI32S or WasmOpCode.F32ConvertI32U:
                    _cilValidator.Pop(typeof(int), "f32.convert_i32"); _cilValidator.Push(typeof(float)); break;
                case WasmOpCode.F32ConvertI64S or WasmOpCode.F32ConvertI64U:
                    _cilValidator.Pop(typeof(long), "f32.convert_i64"); _cilValidator.Push(typeof(float)); break;
                case WasmOpCode.F64ConvertI32S or WasmOpCode.F64ConvertI32U:
                    _cilValidator.Pop(typeof(int), "f64.convert_i32"); _cilValidator.Push(typeof(double)); break;
                case WasmOpCode.F64ConvertI64S or WasmOpCode.F64ConvertI64U:
                    _cilValidator.Pop(typeof(long), "f64.convert_i64"); _cilValidator.Push(typeof(double)); break;
                case WasmOpCode.F32DemoteF64:
                    _cilValidator.Pop(typeof(double), "f32.demote"); _cilValidator.Push(typeof(float)); break;
                case WasmOpCode.F64PromoteF32:
                    _cilValidator.Pop(typeof(float), "f64.promote"); _cilValidator.Push(typeof(double)); break;
                case WasmOpCode.I32ReinterpretF32:
                    _cilValidator.Pop(typeof(float), "i32.reinterpret"); _cilValidator.Push(typeof(int)); break;
                case WasmOpCode.I64ReinterpretF64:
                    _cilValidator.Pop(typeof(double), "i64.reinterpret"); _cilValidator.Push(typeof(long)); break;
                case WasmOpCode.F32ReinterpretI32:
                    _cilValidator.Pop(typeof(int), "f32.reinterpret"); _cilValidator.Push(typeof(float)); break;
                case WasmOpCode.F64ReinterpretI64:
                    _cilValidator.Pop(typeof(long), "f64.reinterpret"); _cilValidator.Push(typeof(double)); break;

                // Sign extension
                case WasmOpCode.I32Extend8S or WasmOpCode.I32Extend16S:
                    _cilValidator.Pop(typeof(int), "i32.extend"); _cilValidator.Push(typeof(int)); break;
                case WasmOpCode.I64Extend8S or WasmOpCode.I64Extend16S or WasmOpCode.I64Extend32S:
                    _cilValidator.Pop(typeof(long), "i64.extend"); _cilValidator.Push(typeof(long)); break;
            }
        }

        /// <summary>Resolve block param/result arities for validator tracking.</summary>
        private (int paramArity, int resultArity) ResolveBlockArity(ValType blockType)
        {
            if (blockType == ValType.Empty)
                return (0, 0);
            if (blockType.IsDefType() && _moduleInst != null)
            {
                var funcType = _moduleInst.Types.ResolveBlockType(blockType);
                if (funcType != null)
                    return (funcType.ParameterTypes.Arity, funcType.ResultType.Arity);
            }
            return (0, 1); // simple value type: 0 params, 1 result
        }

        /// <summary>Track 0xFC prefix (extension) instruction stack effects.</summary>
        private void TrackExtStackEffect(InstructionBase inst, bool before)
        {
            var extOp = inst.Op.xFC;
            if (before)
            {
                switch (extOp)
                {
                    // Bulk memory: [dst, src, len] → []. Types can be i32
                    // (memory32) or i64 (memory64) depending on memidx; we
                    // pop untyped for height-only tracking.
                    case Wacs.Core.OpCodes.ExtCode.MemoryCopy:
                    case Wacs.Core.OpCodes.ExtCode.MemoryFill:
                    case Wacs.Core.OpCodes.ExtCode.MemoryInit:
                        _cilValidator.Pop(3, "bulk args");
                        break;
                    // Table bulk: [dst, src, len] → []
                    case Wacs.Core.OpCodes.ExtCode.TableInit:
                    case Wacs.Core.OpCodes.ExtCode.TableCopy:
                        _cilValidator.Pop(3, "table_bulk args");
                        break;
                    // table.fill: [dst, val:Value, len] → []
                    case Wacs.Core.OpCodes.ExtCode.TableFill:
                        _cilValidator.Pop(3, "table.fill args");
                        break;
                    // table.grow: [val:Value, delta] → [i32]
                    case Wacs.Core.OpCodes.ExtCode.TableGrow:
                        _cilValidator.Pop(2, "table.grow args");
                        break;
                    // table.size: [] → [i32]
                    case Wacs.Core.OpCodes.ExtCode.TableSize:
                        break;
                    // data.drop / elem.drop: [] → []
                    case Wacs.Core.OpCodes.ExtCode.DataDrop:
                    case Wacs.Core.OpCodes.ExtCode.ElemDrop:
                        break;
                    // sat truncation: pop one, push one (handled generically)
                    default:
                        TrackGenericStackDiff(inst, before: true);
                        break;
                }
            }
            else // after
            {
                switch (extOp)
                {
                    case Wacs.Core.OpCodes.ExtCode.TableGrow:
                    case Wacs.Core.OpCodes.ExtCode.TableSize:
                        _cilValidator.Push(typeof(int)); break;
                    case Wacs.Core.OpCodes.ExtCode.MemoryCopy:
                    case Wacs.Core.OpCodes.ExtCode.MemoryFill:
                    case Wacs.Core.OpCodes.ExtCode.MemoryInit:
                    case Wacs.Core.OpCodes.ExtCode.TableInit:
                    case Wacs.Core.OpCodes.ExtCode.TableCopy:
                    case Wacs.Core.OpCodes.ExtCode.TableFill:
                    case Wacs.Core.OpCodes.ExtCode.DataDrop:
                    case Wacs.Core.OpCodes.ExtCode.ElemDrop:
                        break; // void result
                    default:
                        TrackGenericStackDiff(inst, before: false);
                        break;
                }
            }
        }

        /// <summary>
        /// Generic stack effect tracking using the instruction's StackDiff.
        /// Used for SIMD and other instructions without detailed type tracking.
        /// Pops/pushes with typeof(object) as a type placeholder.
        /// </summary>
        /// <summary>
        /// True for SIMD opcodes that consume a value and store it without
        /// producing a stack result (V128 stores / store-lanes).
        /// </summary>
        private static bool IsSimdStore(Wacs.Core.OpCodes.SimdCode op) =>
            op == Wacs.Core.OpCodes.SimdCode.V128Store
            || op == Wacs.Core.OpCodes.SimdCode.V128Store8Lane
            || op == Wacs.Core.OpCodes.SimdCode.V128Store16Lane
            || op == Wacs.Core.OpCodes.SimdCode.V128Store32Lane
            || op == Wacs.Core.OpCodes.SimdCode.V128Store64Lane;

        /// <summary>
        /// CIL stack type produced by a SIMD opcode. Most SIMD ops produce a
        /// V128 (carried as Value on the internal stack), but lane-extract and
        /// bitmask / all-true / any-true ops produce a typed scalar.
        /// </summary>
        private static Type SimdResultType(Wacs.Core.OpCodes.SimdCode op)
        {
            switch (op)
            {
                case Wacs.Core.OpCodes.SimdCode.V128AnyTrue:
                case Wacs.Core.OpCodes.SimdCode.I8x16AllTrue:
                case Wacs.Core.OpCodes.SimdCode.I16x8AllTrue:
                case Wacs.Core.OpCodes.SimdCode.I32x4AllTrue:
                case Wacs.Core.OpCodes.SimdCode.I64x2AllTrue:
                case Wacs.Core.OpCodes.SimdCode.I8x16Bitmask:
                case Wacs.Core.OpCodes.SimdCode.I16x8Bitmask:
                case Wacs.Core.OpCodes.SimdCode.I32x4Bitmask:
                case Wacs.Core.OpCodes.SimdCode.I64x2Bitmask:
                case Wacs.Core.OpCodes.SimdCode.I8x16ExtractLaneS:
                case Wacs.Core.OpCodes.SimdCode.I8x16ExtractLaneU:
                case Wacs.Core.OpCodes.SimdCode.I16x8ExtractLaneS:
                case Wacs.Core.OpCodes.SimdCode.I16x8ExtractLaneU:
                case Wacs.Core.OpCodes.SimdCode.I32x4ExtractLane:
                    return typeof(int);
                case Wacs.Core.OpCodes.SimdCode.I64x2ExtractLane:
                    return typeof(long);
                case Wacs.Core.OpCodes.SimdCode.F32x4ExtractLane:
                    return typeof(float);
                case Wacs.Core.OpCodes.SimdCode.F64x2ExtractLane:
                    return typeof(double);
                default:
                    // All other SIMD ops produce a V128 (represented as Value
                    // on the CIL stack).
                    return typeof(Value);
            }
        }

        private void TrackGenericStackDiff(InstructionBase inst, bool before)
        {
            int diff = inst.StackDiff;
            if (before)
            {
                // Strategy: we don't know the per-op signature for SIMD / FC /
                // similar prefix opcodes without an explicit table. Rather than
                // attempt typed pops (which desync and produce bogus validator
                // errors), clear the current type stack to placeholders. The
                // post-emit Push below repopulates the correct height.
                int h = _cilValidator.Height;
                _cilValidator.Reset(h);
                // Reset preserves types when heights match + reachable; force
                // a placeholder rewrite by bumping to a mismatched height then
                // back. Cleaner: directly clear via a new 0→h cycle.
                _cilValidator.Reset(0);
                _cilValidator.Reset(h);
            }
            else
            {
                // After emission, adjust the validator by the net diff.
                // Since Reset at instruction entry normalized to height
                // (all placeholders), we apply diff to match the post-inst
                // height the next instruction's StackAnalysis expects.
                if (diff > 0)
                    _cilValidator.Push(typeof(object), diff);
                else if (diff < 0)
                    _cilValidator.Pop(-diff, "generic");
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
