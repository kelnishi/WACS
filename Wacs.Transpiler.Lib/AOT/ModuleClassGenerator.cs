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
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.Instructions.Reference;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Transpiler.AOT.Emitters;
using WasmModule = Wacs.Core.Module;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Generates the Module class for a transpiled WASM assembly.
    ///
    /// The module class is the primary consumer-facing type:
    ///   - Implements IExports (typed methods for each WASM export)
    ///   - Constructor accepts IImports (typed methods the consumer provides)
    ///   - Manages ThinContext internally
    ///   - Exposes Memory, Tables, Globals as properties
    ///   - Provides Start() if the module has a start function
    ///
    /// Usage by consumers:
    ///   var module = new WasmModule(new MyImports());
    ///   int result = module.add(2, 3);
    ///   byte[] memory = module.Memory;
    /// </summary>
    public class ModuleClassGenerator
    {
        private readonly ModuleBuilder _moduleBuilder;
        private readonly string _namespace;
        private readonly WasmModule _wasmModule;
        private readonly InterfaceGenerator _interfaces;
        private readonly TypeBuilder _functionsType;
        private readonly MethodBuilder[] _methodBuilders;
        private readonly int _importCount;
        private readonly int _memoryCount;
        private readonly bool _hasStart;
        private readonly int _startFuncIndex;
        private readonly FunctionType[] _allFunctionTypes;

        public TypeBuilder? ModuleType { get; private set; }

        private readonly DataSegmentEmitter? _dataEmitter;
        private readonly int _dataSegmentBaseId;
        private readonly int _elemSegmentBaseId;
        private readonly TranspilerOptions? _options;
        private int _initDataId = -1;
        public int InitDataId => _initDataId;

        public ModuleClassGenerator(
            ModuleBuilder moduleBuilder,
            string @namespace,
            WasmModule wasmModule,
            InterfaceGenerator interfaces,
            TypeBuilder functionsType,
            MethodBuilder[] methodBuilders,
            int importCount,
            DataSegmentEmitter? dataEmitter = null,
            int dataSegmentBaseId = 0,
            int elemSegmentBaseId = 0,
            FunctionType[]? allFunctionTypes = null,
            TranspilerOptions? options = null)
        {
            _moduleBuilder = moduleBuilder;
            _namespace = @namespace;
            _wasmModule = wasmModule;
            _interfaces = interfaces;
            _functionsType = functionsType;
            _methodBuilders = methodBuilders;
            _importCount = importCount;
            _memoryCount = wasmModule.Memories.Count;
            _hasStart = wasmModule.StartIndex != FuncIdx.Default;
            _startFuncIndex = _hasStart ? (int)wasmModule.StartIndex.Value : -1;
            _dataEmitter = dataEmitter;
            _dataSegmentBaseId = dataSegmentBaseId;
            _elemSegmentBaseId = elemSegmentBaseId;
            _allFunctionTypes = allFunctionTypes ?? Array.Empty<FunctionType>();
            _options = options;
        }

        // True when the resolver matched at least one of this module's
        // imports — the generated ctor needs a trailing `object hostBundle`
        // param so the consumer can supply the typed-interface aggregate
        // the direct-linked IL dispatches through.
        private bool HasResolverBindings =>
            _options?.ResolverImportBindings != null
            && _options.ResolverImportBindings.Count > 0;

        // True when the generated IL might dispatch into the
        // resources lookup machinery — either because some binding
        // is a [method]/[constructor] resource shape, or because
        // the resolver knows about typed resource interfaces that
        // could appear as own<R>/borrow<R> params on free functions.
        // Adds an additional `object resources` ctor param.
        private bool HasResourceBindings
        {
            get
            {
                var b = _options?.ResolverImportBindings;
                if (b == null) return false;
                var resolver = _options?.Resolver;
                // Resolver knows about resource interfaces and has a
                // resources type to dispatch through — even free-fn
                // bindings might need it for own<R> params.
                if (resolver?.PreferredResourcesType != null
                    && resolver.ResourceInterfaceTypes.Count > 0)
                    return true;
                foreach (var kv in b)
                    if (kv.Value.IsResourceMethod) return true;
                return false;
            }
        }

        /// <summary>
        /// Build ModuleInitData from the WASM module and register it.
        /// Must be called before Generate() so the constructor can reference the init data ID.
        /// </summary>
        public void PrepareInitData()
        {
            var data = new ModuleInitData();

            // Memories (imported + module-defined)
            var mems = new List<(long min, long? max)>();
            foreach (var mem in _wasmModule.ImportedMems)
            {
                mems.Add((mem.Limits.Minimum, mem.Limits.Maximum));
            }
            foreach (var mem in _wasmModule.Memories)
            {
                mems.Add((mem.Limits.Minimum, mem.Limits.Maximum));
            }
            data.Memories = mems.ToArray();

            // Tables (imported + module-defined)
            var tables = new List<(long min, long max, ValType elemType, Wacs.Core.Types.Expression? initExpr)>();
            foreach (var tbl in _wasmModule.ImportedTables)
            {
                tables.Add((tbl.Limits.Minimum, tbl.Limits.Maximum ?? uint.MaxValue, tbl.ElementType, null));
            }
            foreach (var tbl in _wasmModule.Tables)
            {
                tables.Add((tbl.Limits.Minimum, tbl.Limits.Maximum ?? uint.MaxValue, tbl.ElementType, tbl.Init));
            }
            data.Tables = tables.ToArray();

            // Globals (imported + module-defined)
            var globals = new List<(ValType type, Mutability mut, Value init)>();
            foreach (var g in _wasmModule.ImportedGlobals)
            {
                // Imported globals get default initial values (the actual value comes from imports)
                globals.Add((g.Type.ContentType, g.Type.Mutability, new Value(g.Type.ContentType)));
            }
            int importedGlobalCount = _wasmModule.ImportedGlobals.Count;
            for (int gi = 0; gi < _wasmModule.Globals.Count; gi++)
            {
                var g = _wasmModule.Globals[gi];
                var gcInit = TryParseGcGlobalInit(g, importedGlobalCount + gi);
                if (gcInit != null)
                {
                    data.GcGlobalInits.Add(gcInit);
                    // Placeholder — will be initialized at runtime
                    globals.Add((g.Type.ContentType, g.Type.Mutability, new Value(g.Type.ContentType)));
                }
                else
                {
                    var initVal = EvaluateGlobalInit(g, globals);
                    globals.Add((g.Type.ContentType, g.Type.Mutability, initVal));
                    // Check if this global depends on an imported global
                    foreach (var inst in g.Initializer.Instructions)
                    {
                        if (inst is InstGlobalGet gg && gg.GetIndex() < importedGlobalCount)
                        {
                            data.DeferredGlobalInits.Add((importedGlobalCount + gi, g.Initializer));
                            break;
                        }
                    }
                }
            }
            data.Globals = globals.ToArray();

            // Function type hashes populated by ModuleTranspiler which has ModuleInstance access

            // Active data segments
            var activeDataIndices = new List<int>();
            if (_dataEmitter != null)
            {
                var activeSegs = new List<(int memIdx, int offset, int segId)>();
                for (int i = 0; i < _dataEmitter.Segments.Length; i++)
                {
                    var seg = _dataEmitter.Segments[i];
                    if (seg.IsPassive || seg.Data.Length == 0) continue;
                    // Re-evaluate offset only if it contains global.get
                    // (DataSegmentEmitter computes simple offsets correctly)
                    int offset = (int)seg.Offset;
                    if (seg.OffsetExpression != null)
                    {
                        bool hasGlobalGet = false;
                        foreach (var inst in seg.OffsetExpression.Instructions)
                            if (inst is InstGlobalGet) { hasGlobalGet = true; break; }
                        if (hasGlobalGet)
                        {
                            offset = EvaluateConstI32(seg.OffsetExpression, globals);
                            data.DeferredDataOffsets.Add((activeDataIndices.Count, seg.OffsetExpression));
                        }
                    }
                    activeSegs.Add((seg.MemoryIndex, offset, _dataSegmentBaseId + i));
                    activeDataIndices.Add(i);
                }
                data.ActiveDataSegments = activeSegs.ToArray();
            }
            data.ActiveDataIndices = activeDataIndices.ToArray();

            // Active element segments
            var droppedElemIndices = new List<int>();
            var activeElems = new List<(int tableIdx, int offset, int[] funcIndices)>();
            int activeElemIdx = 0;
            for (int i = 0; i < _wasmModule.Elements.Length; i++)
            {
                var elem = _wasmModule.Elements[i];
                if (elem.Mode is WasmModule.ElementMode.ActiveMode active)
                {
                    int tableIdx = (int)active.TableIndex.Value;
                    int offset = EvaluateConstI32(active.Offset, globals);
                    var (funcIndices, deferredGlobals) = ExtractFuncIndicesWithDeferred(elem, globals, activeElemIdx, data.GcElementValues);
                    activeElems.Add((tableIdx, offset, funcIndices));
                    data.DeferredElemGlobals.AddRange(deferredGlobals);
                    droppedElemIndices.Add(i); // Active segments implicitly dropped
                    activeElemIdx++;
                }
                else if (elem.Mode is WasmModule.ElementMode.DeclarativeMode)
                {
                    droppedElemIndices.Add(i); // Declarative segments also implicitly dropped
                }
            }
            data.ActiveElementSegments = activeElems.ToArray();
            data.ActiveElemIndices = droppedElemIndices.ToArray();

            // Start function
            data.StartFuncIndex = _startFuncIndex;
            data.ImportFuncCount = _importCount;
            data.TotalFuncCount = _importCount + _methodBuilders.Length;
            data.DataSegmentBaseId = _dataSegmentBaseId;
            data.ElemSegmentBaseId = _elemSegmentBaseId;

            // Tags (doc 1 §13.1, doc 2 §5). Imports are filled by the linker;
            // local tags get fresh TagInstance objects at Initialize time —
            // reference identity is enough for transpiler throw/catch matching.
            // DefType is captured best-effort for interpreter interop; it is
            // not read by the transpiler's own emission or dispatch paths.
            data.ImportedTagCount = _wasmModule.ImportedTags.Count;
            data.LocalTagTypes = new DefType[_wasmModule.Tags.Count];
            // DefType population requires a materialized ModuleInstance
            // (TypesSpace walks the recursive group). We leave entries null
            // here; Initialize constructs TagInstance with null! DefType when
            // needed. The linker replaces imported tag slots with the
            // exporter's fully-typed TagInstance before any throw/catch runs.

            // Snapshot every referenced data-segment's bytes now, so the
            // codec-encoded init-data embedded in the generated .dll carries
            // the full payload. (The pre-phase-4 Initialize path only
            // populated SavedDataSegments *after* PrepareInitData, which was
            // fine for in-process since ModuleInit already had the bytes
            // under the transpile-time IDs — but cross-process load starts
            // with an empty ModuleInit, so the bytes must ride in the
            // embedded resource.)
            foreach (var (_, _, segId) in data.ActiveDataSegments)
            {
                if (data.SavedDataSegments.ContainsKey(segId)) continue;
                var segData = ModuleInit.GetDataSegmentData(segId);
                if (segData != null && segData.Length > 0)
                    data.SavedDataSegments[segId] = (byte[])segData.Clone();
            }

            _initDataId = InitRegistry.Register(data);
        }

        /// <summary>
        /// Evaluate a global's constant initializer expression to a Value.
        /// Handles i32.const, i64.const, f32.const, f64.const, ref.null, ref.func.
        /// </summary>
        /// <summary>
        /// Evaluate a global's constant initializer, with access to previously
        /// initialized globals (for global.get in initializer expressions).
        /// </summary>
        /// <summary>
        /// Check if a global initializer uses GC construction (array.new, etc.)
        /// that can't be evaluated at transpile time.
        /// </summary>
        private static GcGlobalInit? TryParseGcGlobalInit(WasmModule.Global global, int globalIdx)
        {
            // Walk the initializer looking for GC instructions
            var consts = new List<long>();
            int gcTypeIdx = -1;
            int initKind = -1;

            foreach (var inst in global.Initializer.Instructions)
            {
                if (inst is InstI32Const i32c)
                    consts.Add(i32c.Value);
                else if (inst is InstI64Const i64c)
                    consts.Add(i64c.FetchImmediate(null!));
                else if (inst.GetType().Namespace == "Wacs.Core.Instructions.GC")
                {
                    // Detect GC construction instructions by opcode
                    var gcOp = inst.Op.xFB;
                    switch (gcOp)
                    {
                        case Wacs.Core.OpCodes.GcCode.ArrayNew:
                            gcTypeIdx = ((Wacs.Core.Instructions.GC.InstArrayNew)inst).TypeIndex;
                            initKind = 0;
                            break;
                        case Wacs.Core.OpCodes.GcCode.ArrayNewDefault:
                            gcTypeIdx = ((Wacs.Core.Instructions.GC.InstArrayNewDefault)inst).TypeIndex;
                            initKind = 1;
                            break;
                        case Wacs.Core.OpCodes.GcCode.StructNew:
                            gcTypeIdx = ((Wacs.Core.Instructions.GC.InstStructNew)inst).TypeIndex;
                            initKind = 2;
                            break;
                        case Wacs.Core.OpCodes.GcCode.StructNewDefault:
                            gcTypeIdx = ((Wacs.Core.Instructions.GC.InstStructNewDefault)inst).TypeIndex;
                            initKind = 3;
                            break;
                    }
                }
            }

            if (initKind >= 0 && gcTypeIdx >= 0)
            {
                return new GcGlobalInit
                {
                    GlobalIndex = globalIdx,
                    TypeIndex = gcTypeIdx,
                    InitKind = initKind,
                    Params = consts.ToArray()
                };
            }
            return null;
        }

        private static Value EvaluateGlobalInit(
            WasmModule.Global global,
            List<(ValType type, Mutability mut, Value init)> previousGlobals)
        {
            // Stack-based evaluator for constant expressions including
            // extended constant expressions (i32.add/sub/mul, i64.add/sub/mul).
            var stack = new Stack<Value>();
            foreach (var inst in global.Initializer.Instructions)
            {
                if (inst is InstI32Const i32) { stack.Push(new Value(i32.Value)); continue; }
                if (inst is InstI64Const i64) { stack.Push(new Value(i64.FetchImmediate(null!))); continue; }
                if (inst is InstF32Const f32) { stack.Push(new Value(f32.FetchImmediate(null!))); continue; }
                if (inst is InstF64Const f64) { stack.Push(new Value(f64.FetchImmediate(null!))); continue; }
                if (inst is InstRefNull rn) { stack.Push(new Value(rn.RefType)); continue; }
                if (inst is InstRefFunc refFunc)
                { stack.Push(new Value(ValType.FuncRef, (int)refFunc.FunctionIndex.Value)); continue; }
                if (inst is InstGlobalGet gg)
                {
                    int idx = gg.GetIndex();
                    if (idx >= 0 && idx < previousGlobals.Count)
                        stack.Push(previousGlobals[idx].init);
                    else
                        stack.Push(new Value(global.Type.ContentType));
                    continue;
                }
                // Extended constant expressions
                var op = inst.Op.x00;
                if (stack.Count >= 2)
                {
                    if (op == Wacs.Core.OpCodes.OpCode.I32Add) { int b = stack.Pop().Data.Int32, a = stack.Pop().Data.Int32; stack.Push(new Value(a + b)); continue; }
                    if (op == Wacs.Core.OpCodes.OpCode.I32Sub) { int b = stack.Pop().Data.Int32, a = stack.Pop().Data.Int32; stack.Push(new Value(a - b)); continue; }
                    if (op == Wacs.Core.OpCodes.OpCode.I32Mul) { int b = stack.Pop().Data.Int32, a = stack.Pop().Data.Int32; stack.Push(new Value(a * b)); continue; }
                    if (op == Wacs.Core.OpCodes.OpCode.I64Add) { long b = stack.Pop().Data.Int64, a = stack.Pop().Data.Int64; stack.Push(new Value(a + b)); continue; }
                    if (op == Wacs.Core.OpCodes.OpCode.I64Sub) { long b = stack.Pop().Data.Int64, a = stack.Pop().Data.Int64; stack.Push(new Value(a - b)); continue; }
                    if (op == Wacs.Core.OpCodes.OpCode.I64Mul) { long b = stack.Pop().Data.Int64, a = stack.Pop().Data.Int64; stack.Push(new Value(a * b)); continue; }
                }
            }
            return stack.Count > 0 ? stack.Pop() : new Value(global.Type.ContentType);
        }

        /// <summary>
        /// Evaluate a constant i32 expression. Handles simple constants and
        /// extended constant expressions (i32.add, i32.sub, i32.mul).
        /// </summary>
        private int EvaluateConstI32(Wacs.Core.Types.Expression expr,
            List<(ValType type, Mutability mut, Value init)>? globals = null)
        {
            var stack = new Stack<int>();
            foreach (var inst in expr.Instructions)
            {
                if (inst is InstI32Const i32) { stack.Push(i32.Value); continue; }
                if (inst is Wacs.Core.Instructions.Numeric.InstI64Const i64c)
                    { stack.Push((int)i64c.FetchImmediate(null!)); continue; }
                if (inst is InstGlobalGet gg)
                {
                    int idx = gg.GetIndex();
                    if (globals != null && idx >= 0 && idx < globals.Count)
                        stack.Push(globals[idx].init.Data.Int32);
                    else
                        stack.Push(0);
                    continue;
                }
                // Extended constant expressions
                var op = inst.Op.x00;
                if (op == Wacs.Core.OpCodes.OpCode.I32Add && stack.Count >= 2)
                    { int b = stack.Pop(), a = stack.Pop(); stack.Push(a + b); continue; }
                if (op == Wacs.Core.OpCodes.OpCode.I32Sub && stack.Count >= 2)
                    { int b = stack.Pop(), a = stack.Pop(); stack.Push(a - b); continue; }
                if (op == Wacs.Core.OpCodes.OpCode.I32Mul && stack.Count >= 2)
                    { int b = stack.Pop(), a = stack.Pop(); stack.Push(a * b); continue; }
            }
            if (stack.Count > 0) return stack.Pop();
            return 0;
        }

        /// <summary>
        /// Extract function indices from element segment initializers.
        /// Each initializer is typically a single ref.func instruction.
        /// </summary>
        /// <summary>
        /// Extract function indices from element initializers, also recording
        /// entries that use global.get for deferred resolution after import patching.
        /// </summary>
        private static (int[] funcIndices, List<(int elemSegIdx, int slotIdx, int globalIdx)> deferred)
            ExtractFuncIndicesWithDeferred(
            WasmModule.ElementSegment elem,
            List<(ValType type, Mutability mut, Value init)> globals,
            int elemSegIdx,
            Dictionary<(int segIdx, int slotIdx), Value>? gcElemValues = null)
        {
            var deferred = new List<(int, int, int)>();
            var indices = new int[elem.Initializers.Length];
            for (int i = 0; i < elem.Initializers.Length; i++)
            {
                var expr = elem.Initializers[i];
                indices[i] = -1;

                // Check for GC-typed initializers (ref.i31, struct.new, etc.)
                foreach (var inst in expr.Instructions)
                {
                    if (inst.GetType().Namespace == "Wacs.Core.Instructions.GC")
                    {
                        // Evaluate GC element initializer
                        var gcOp = inst.Op.xFB;
                        if (gcOp == Wacs.Core.OpCodes.GcCode.RefI31)
                        {
                            // Find the i32.const that precedes ref.i31
                            foreach (var prev in expr.Instructions)
                            {
                                if (prev is Wacs.Core.Instructions.Numeric.InstI32Const ic)
                                {
                                    gcElemValues?.Add((elemSegIdx, i),
                                        Emitters.GcRuntimeHelpers.RefI31Value(ic.Value));
                                    indices[i] = -2; // sentinel: GC value, not a funcIdx
                                    break;
                                }
                            }
                        }
                        goto nextElement;
                    }
                }

                foreach (var inst in expr.Instructions)
                {
                    if (inst is InstRefFunc rf)
                    {
                        indices[i] = (int)rf.FunctionIndex.Value;
                        break;
                    }
                    if (inst is InstGlobalGet gg)
                    {
                        int gIdx = gg.GetIndex();
                        if (gIdx >= 0 && gIdx < globals.Count)
                        {
                            var gval = globals[gIdx].init;
                            if (gval.Type.IsRefType() && !gval.IsNullRef)
                            {
                                indices[i] = (int)gval.Data.Ptr;
                                break;
                            }
                        }
                        // Record for deferred resolution (imported global not yet available)
                        deferred.Add((elemSegIdx, i, gIdx));
                        break;
                    }
                }
                nextElement:;
            }
            return (indices, deferred);
        }

        private static int[] ExtractFuncIndices(
            WasmModule.ElementSegment elem,
            List<(ValType type, Mutability mut, Value init)> globals)
        {
            var indices = new int[elem.Initializers.Length];
            for (int i = 0; i < elem.Initializers.Length; i++)
            {
                var expr = elem.Initializers[i];
                indices[i] = -1; // default: no function

                foreach (var inst in expr.Instructions)
                {
                    if (inst is InstRefFunc rf)
                    {
                        indices[i] = (int)rf.FunctionIndex.Value;
                        break;
                    }
                    if (inst is InstGlobalGet gg)
                    {
                        // Resolve funcref from global (e.g., imported funcref global)
                        int gIdx = gg.GetIndex();
                        if (gIdx >= 0 && gIdx < globals.Count)
                        {
                            var gval = globals[gIdx].init;
                            if (gval.Type.IsRefType() && !gval.IsNullRef)
                                indices[i] = (int)gval.Data.Ptr;
                        }
                        break;
                    }
                }
            }
            return indices;
        }

        /// <summary>
        /// Generate the Module class. Must be called AFTER function IL bodies are emitted,
        /// but BEFORE CreateType() on the Functions TypeBuilder (since we reference its methods).
        /// </summary>
        public void Generate()
        {
            // Ensure init data is prepared
            if (_initDataId < 0)
                PrepareInitData();

            var parentInterfaces = _interfaces.ExportsInterface != null
                ? new[] { _interfaces.ExportsInterface }
                : Type.EmptyTypes;

            ModuleType = _moduleBuilder.DefineType(
                $"{_namespace}.Module",
                TypeAttributes.Public | TypeAttributes.Class,
                typeof(object),
                parentInterfaces);

            // Private field: ThinContext
            var ctxField = ModuleType.DefineField(
                "_ctx", typeof(ThinContext), FieldAttributes.Private);

            // Constructor
            EmitConstructor(ctxField);

            // Export method implementations
            EmitExportMethods(ctxField);

            // Memory properties
            EmitMemoryProperties(ctxField);

            // Start method
            if (_hasStart)
                EmitStartMethod(ctxField);

            // ModuleName property
            EmitModuleNameProperty();
        }

        /// <summary>
        /// Bake the type. Call after Generate() and after Functions type is created.
        /// </summary>
        public Type? CreateType()
        {
            // Bake the embedded-init-data holder type too. It's separate
            // from ModuleType so it can live alongside the Module class
            // and be referenced from Module ctor IL (Ldsfld on its Data).
            _embeddedInitType?.CreateType();
            // Same for the AotLinked-mode data-segment holder.
            FinalizeAotDataHolder();
            _aotDataType?.CreateType();
            return ModuleType?.CreateType();
        }

        // Holder type + byte[] field holding the codec-encoded ModuleInitData.
        // Populated once during EmitConstructor via EmitEmbeddedInitData.
        private TypeBuilder? _embeddedInitType;
        private FieldBuilder? _embeddedInitField;

        /// <summary>
        /// Validate that the prepared module fits the current AotLinked emission
        /// scope (no memories, tables, globals, or data segments — i.e. compute-
        /// only wasm). Throws <see cref="InvalidOperationException"/> with a
        /// specific reason if not. Coverage will grow incrementally; the
        /// thrower keeps users from silently shipping a Module class with
        /// missing initialization.
        /// </summary>
        // ============================================================
        // AotLinked ctor body emission
        // ============================================================

        // Holder type with one static byte[] field per active data
        // segment. Built lazily on first call to GetAotDataSegmentField.
        private TypeBuilder? _aotDataType;
        private FieldBuilder?[]? _aotDataFields;

        /// <summary>
        /// Emit the IL body for the AotLinked Module ctor. Runs after
        /// <c>base()</c>; leaves a fully-initialized <see cref="ThinContext"/>
        /// in <paramref name="ctxLocal"/> so the standard wiring (imports,
        /// FuncTable, BindTableDelegates) can take over.
        ///
        /// <para>Coverage today: empty modules + memories + active data
        /// segments. Tables, globals, element segments, GC inits, and
        /// deferred globals fall back to <c>Standard</c> via
        /// <see cref="AssertAotLinkedFeasible"/>.</para>
        /// </summary>
        private void EmitAotLinkedCtorBody(ILGenerator il, LocalBuilder ctxLocal)
        {
            if (_initDataId < 0) PrepareInitData();
            var data = InitRegistry.Get(_initDataId);

            // (1) Allocate ThinContext with literal memories, tables, globals.
            //     ctx = new ThinContext(memories, tables, globals, null, null);
            EmitMemoryArray(il, data);
            EmitTablesArray(il, data);
            EmitGlobalsArray(il, data);
            il.Emit(OpCodes.Ldnull);              // importDelegates
            il.Emit(OpCodes.Ldnull);              // funcTable
            var standaloneCtor = typeof(ThinContext).GetConstructor(new[]
            {
                typeof(MemoryInstance[]),
                typeof(TableInstance[]),
                typeof(GlobalInstance[]),
                typeof(Delegate[]),
                typeof(Delegate[]),
            })!;
            il.Emit(OpCodes.Newobj, standaloneCtor);
            il.Emit(OpCodes.Stloc, ctxLocal);

            // (2) Copy each active data segment into its memory at offset.
            EmitDataSegmentCopies(il, ctxLocal, data);

            // (3) Copy each active element segment into its table at offset.
            EmitElementSegmentCopies(il, ctxLocal, data);

            // (4) Populate type-identity arrays consumed by call_indirect
            // subtype checks and ref.test / ref.cast on funcref. Standard's
            // InitializeFromData copies these from the decoded codec data
            // (InitializationHelper.cs:576-579); under AotLinked we inline
            // them as IL constants.
            EmitTypeHashArrays(il, ctxLocal, data);

            // (5) Drop active data + element segments. WASM 3.0 §4.5.4:
            // active segments are implicitly dropped at the end of
            // instantiation, so subsequent `memory.init` / `table.init`
            // referencing them must trap "uninitialized element /
            // segment". Standard's InitializeFromData does this via
            // ModuleInit.DropDataSegment / DropElemSegment; under
            // AotLinked we inline the calls here. Only meaningful for
            // in-process — cross-process AotLinked never repopulates
            // ModuleInit so the drops are no-ops.
            EmitActiveSegmentDrops(il, data);

            // (6) Stash the segment base IDs onto ctx so post-Bake
            // memory.init / table.init helpers can index correctly.
            EmitSegmentBaseIds(il, ctxLocal, data);

            // (7) Stamp ctx.InitDataId with the transpile-time id so
            // MultiReturnMethodRegistry / GcTypeRegistry lookups —
            // both keyed on (initDataId, funcIdx) / (initDataId,
            // typeIdx) — find the entries the transpile-time IL
            // registered. Standard's InitializeFromData does this
            // via the int-overload of InitializeFromData; under
            // AotLinked we set the field directly.
            EmitInitDataIdStamp(il, ctxLocal);

            // (8) Register passive data segments into ModuleInit so
            // `memory.init` instructions resolve them in cross-process
            // loads (the in-process pre-pass already populated the
            // registry under the same ids; RegisterDataSegmentAt is
            // idempotent). Active segments aren't registered — their
            // bytes ride RVA-mapped on __WACSAotData.Segment_N and
            // get copied into linear memory directly via
            // EmitDataSegmentCopies above.
            EmitPassiveDataSegmentRegistrations(il, data);

            // (9) Register passive element segments — same idempotent
            // ModuleInit registration recipe as passive data, but the
            // values are funcref / typed-null mix encoded as int[]
            // (−1 = null; >=0 = funcIdx). The helper materializes the
            // Value[] at runtime; transpile-time we only emit the
            // compact int[] inline.
            EmitPassiveElementSegmentRegistrations(il, data);
        }

        /// <summary>
        /// For each passive element segment, walk its Initializers
        /// at transpile time, encode the entries as <c>int[]</c>
        /// (-1 = null funcref; >=0 = funcIdx), and emit IL that builds
        /// the array inline + calls
        /// <see cref="BulkHelpers.RegisterPassiveElemSegment"/>.
        /// </summary>
        private void EmitPassiveElementSegmentRegistrations(ILGenerator il, ModuleInitData data)
        {
            // Active and declarative segments are in data.ActiveElemIndices
            // (active = also in data.ActiveElementSegments; declarative =
            // dropped immediately, no registration needed). Anything else
            // is passive.
            var droppedSet = new HashSet<int>(data.ActiveElemIndices);

            var registerHelper = typeof(BulkHelpers).GetMethod(
                nameof(BulkHelpers.RegisterPassiveElemSegment),
                BindingFlags.Public | BindingFlags.Static)!;

            for (int localIdx = 0; localIdx < _wasmModule.Elements.Length; localIdx++)
            {
                if (droppedSet.Contains(localIdx)) continue;
                var elem = _wasmModule.Elements[localIdx];
                int n = elem.Initializers.Length;
                if (n == 0) continue;

                var encoded = new int[n];
                bool ok = true;
                for (int j = 0; j < n; j++)
                {
                    if (!TryEncodeFuncRefInitializer(elem.Initializers[j], out encoded[j]))
                    {
                        ok = false;
                        break;
                    }
                }
                if (!ok) continue;     // gate would have rejected such modules

                int absoluteId = data.ElemSegmentBaseId + localIdx;
                il.Emit(OpCodes.Ldc_I4, absoluteId);
                il.Emit(OpCodes.Ldc_I4, n);
                il.Emit(OpCodes.Newarr, typeof(int));
                for (int j = 0; j < n; j++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, j);
                    il.Emit(OpCodes.Ldc_I4, encoded[j]);
                    il.Emit(OpCodes.Stelem_I4);
                }
                il.Emit(OpCodes.Call, registerHelper);
            }
        }

        /// <summary>
        /// Encode a single passive-element-segment initializer as the
        /// compact <c>int</c> the runtime helper expects: <c>-1</c> for
        /// <c>ref.null</c>, the function index for <c>ref.func</c>.
        /// Returns false on GC-typed / global-dependent shapes (gated
        /// out by IsAotLinkedFeasible).
        /// </summary>
        private static bool TryEncodeFuncRefInitializer(
            Wacs.Core.Types.Expression expr, out int encoded)
        {
            encoded = 0;
            foreach (var inst in expr.Instructions)
            {
                if (inst is Wacs.Core.Instructions.Reference.InstRefFunc rf)
                {
                    encoded = (int)rf.FunctionIndex.Value;
                    return true;
                }
                if (inst is Wacs.Core.Instructions.Reference.InstRefNull)
                {
                    encoded = -1;
                    return true;
                }
                // Unsupported instruction kind (global.get etc.) — let
                // the caller fall back to skipping this segment.
                return false;
            }
            return false;
        }

        /// <summary>
        /// For each passive data segment in the module, emit IL that
        /// materializes the RVA-mapped bytes via
        /// <c>RuntimeHelpers.CreateSpan&lt;byte&gt;</c> and calls
        /// <c>BulkHelpers.RegisterPassiveDataSegment(id, span)</c>. The
        /// helper does <c>span.ToArray()</c> + <c>ModuleInit.RegisterDataSegmentAt</c>;
        /// the registry call is no-op when the id is already populated
        /// (in-process pre-pass).
        /// </summary>
        private void EmitPassiveDataSegmentRegistrations(ILGenerator il, ModuleInitData data)
        {
            if (_dataEmitter == null) return;

            // Active indices are the ones in data.ActiveDataIndices;
            // any other segment index is passive.
            var activeSet = new HashSet<int>(data.ActiveDataIndices);

            var createSpanOpen = typeof(System.Runtime.CompilerServices.RuntimeHelpers)
                .GetMethod(
                    nameof(System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan),
                    BindingFlags.Public | BindingFlags.Static)!;
            var createSpanByte = createSpanOpen.MakeGenericMethod(typeof(byte));
            var registerHelper = typeof(BulkHelpers).GetMethod(
                nameof(BulkHelpers.RegisterPassiveDataSegment),
                BindingFlags.Public | BindingFlags.Static)!;

            for (int localIdx = 0; localIdx < _dataEmitter.Segments.Length; localIdx++)
            {
                if (activeSet.Contains(localIdx)) continue;
                var seg = _dataEmitter.Segments[localIdx];
                if (seg.Data.Length == 0) continue;

                int absoluteId = data.DataSegmentBaseId + localIdx;
                // GetAotDataSegmentField is idempotent — the same RVA
                // field is reused across active-copy and passive-register
                // sites if a hypothetical segment is both, though in
                // practice a segment is one or the other.
                var segField = GetAotDataSegmentField(absoluteId, seg.Data);

                il.Emit(OpCodes.Ldc_I4, absoluteId);
                il.Emit(OpCodes.Ldtoken, segField);
                il.Emit(OpCodes.Call, createSpanByte);
                il.Emit(OpCodes.Call, registerHelper);
            }
        }

        private void EmitInitDataIdStamp(ILGenerator il, LocalBuilder ctxLocal)
        {
            var initDataIdField = typeof(ThinContext).GetField(nameof(ThinContext.InitDataId));
            if (initDataIdField == null) return;
            il.Emit(OpCodes.Ldloc, ctxLocal);
            il.Emit(OpCodes.Ldc_I4, _initDataId);
            il.Emit(OpCodes.Stfld, initDataIdField);
        }

        /// <summary>
        /// Inline IL for <c>ModuleInit.DropDataSegment</c> /
        /// <c>DropElemSegment</c> calls covering every active segment
        /// the module declared. Mirrors the loop
        /// <see cref="InitializationHelper.InitializeFromData"/> runs
        /// after the codec round-trip.
        /// </summary>
        private void EmitActiveSegmentDrops(ILGenerator il, ModuleInitData data)
        {
            var dropDataMethod = typeof(ModuleInit).GetMethod(
                nameof(ModuleInit.DropDataSegment),
                BindingFlags.Public | BindingFlags.Static)!;
            var dropElemMethod = typeof(ModuleInit).GetMethod(
                nameof(ModuleInit.DropElemSegment),
                BindingFlags.Public | BindingFlags.Static)!;

            int dataBase = data.DataSegmentBaseId;
            foreach (var localDataIdx in data.ActiveDataIndices)
            {
                il.Emit(OpCodes.Ldc_I4, dataBase + localDataIdx);
                il.Emit(OpCodes.Call, dropDataMethod);
            }

            int elemBase = data.ElemSegmentBaseId;
            foreach (var localElemIdx in data.ActiveElemIndices)
            {
                il.Emit(OpCodes.Ldc_I4, elemBase + localElemIdx);
                il.Emit(OpCodes.Call, dropElemMethod);
            }
        }

        /// <summary>
        /// Set <c>ctx.DataSegmentBaseId</c> and <c>ctx.ElemSegmentBaseId</c>
        /// to the transpile-time base IDs. Standard's <c>InitializeFromData</c>
        /// derives these from the (possibly-remapped) cross-process registry;
        /// AotLinked uses the static transpile-time values since segments
        /// don't get re-registered.
        /// </summary>
        private void EmitSegmentBaseIds(ILGenerator il, LocalBuilder ctxLocal, ModuleInitData data)
        {
            var dataBaseField = typeof(ThinContext).GetField(nameof(ThinContext.DataSegmentBaseId));
            var elemBaseField = typeof(ThinContext).GetField(nameof(ThinContext.ElemSegmentBaseId));
            if (dataBaseField != null)
            {
                il.Emit(OpCodes.Ldloc, ctxLocal);
                il.Emit(OpCodes.Ldc_I4, data.DataSegmentBaseId);
                il.Emit(OpCodes.Stfld, dataBaseField);
            }
            if (elemBaseField != null)
            {
                il.Emit(OpCodes.Ldloc, ctxLocal);
                il.Emit(OpCodes.Ldc_I4, data.ElemSegmentBaseId);
                il.Emit(OpCodes.Stfld, elemBaseField);
            }
        }

        /// <summary>
        /// Inline FuncTypeHashes / FuncTypeSuperHashes / TypeHashes /
        /// TypeIsFunc as IL-constructed constant arrays assigned onto
        /// <c>ctx</c>. Each array is sized by the source-module's type +
        /// function counts and populated with values computed by
        /// <see cref="ModuleTranspiler"/>'s subtype-chain walk
        /// (<c>BuildSuperTypeHashChain</c>) at transpile time.
        /// </summary>
        private void EmitTypeHashArrays(ILGenerator il, LocalBuilder ctxLocal, ModuleInitData data)
        {
            EmitInt32Array(il, ctxLocal, data.FuncTypeHashes,
                typeof(ThinContext).GetField(nameof(ThinContext.FuncTypeHashes))!);
            EmitInt32Array(il, ctxLocal, data.TypeHashes,
                typeof(ThinContext).GetField(nameof(ThinContext.TypeHashes))!);
            EmitBoolArray(il, ctxLocal, data.TypeIsFunc,
                typeof(ThinContext).GetField(nameof(ThinContext.TypeIsFunc))!);
            EmitInt32JaggedArray(il, ctxLocal, data.FuncTypeSuperHashes,
                typeof(ThinContext).GetField(nameof(ThinContext.FuncTypeSuperHashes))!);
        }

        private static void EmitInt32Array(ILGenerator il, LocalBuilder ctxLocal,
            int[]? values, FieldInfo target)
        {
            if (values == null) return;
            il.Emit(OpCodes.Ldloc, ctxLocal);
            il.Emit(OpCodes.Ldc_I4, values.Length);
            il.Emit(OpCodes.Newarr, typeof(int));
            for (int i = 0; i < values.Length; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldc_I4, values[i]);
                il.Emit(OpCodes.Stelem_I4);
            }
            il.Emit(OpCodes.Stfld, target);
        }

        private static void EmitBoolArray(ILGenerator il, LocalBuilder ctxLocal,
            bool[]? values, FieldInfo target)
        {
            if (values == null) return;
            il.Emit(OpCodes.Ldloc, ctxLocal);
            il.Emit(OpCodes.Ldc_I4, values.Length);
            il.Emit(OpCodes.Newarr, typeof(bool));
            for (int i = 0; i < values.Length; i++)
            {
                if (!values[i]) continue;       // newarr zero-inits
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Stelem_I1);
            }
            il.Emit(OpCodes.Stfld, target);
        }

        private static void EmitInt32JaggedArray(ILGenerator il, LocalBuilder ctxLocal,
            int[][]? values, FieldInfo target)
        {
            if (values == null) return;
            il.Emit(OpCodes.Ldloc, ctxLocal);
            il.Emit(OpCodes.Ldc_I4, values.Length);
            il.Emit(OpCodes.Newarr, typeof(int[]));
            for (int i = 0; i < values.Length; i++)
            {
                var inner = values[i];
                if (inner == null) continue;
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldc_I4, inner.Length);
                il.Emit(OpCodes.Newarr, typeof(int));
                for (int j = 0; j < inner.Length; j++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, j);
                    il.Emit(OpCodes.Ldc_I4, inner[j]);
                    il.Emit(OpCodes.Stelem_I4);
                }
                il.Emit(OpCodes.Stelem_Ref);
            }
            il.Emit(OpCodes.Stfld, target);
        }

        private static bool IsPrimitiveValType(Wacs.Core.Types.Defs.ValType t)
            => t == Wacs.Core.Types.Defs.ValType.I32
            || t == Wacs.Core.Types.Defs.ValType.I64
            || t == Wacs.Core.Types.Defs.ValType.F32
            || t == Wacs.Core.Types.Defs.ValType.F64;

        /// <summary>
        /// IL: <c>new TableInstance[N] { new TableInstance(new TableType(elemType, new Limits(I32, min, max)), new Value(elemType)), ... }</c>.
        /// Pushes the tables array onto the stack. Tables with non-null
        /// initExpr are rejected by AssertAotLinkedFeasible — handled here
        /// is the default-null-element shape only.
        /// </summary>
        private void EmitTablesArray(ILGenerator il, ModuleInitData data)
        {
            int n = data.Tables.Length;
            il.Emit(OpCodes.Ldc_I4, n);
            il.Emit(OpCodes.Newarr, typeof(TableInstance));
            if (n == 0) return;

            var limitsCtor = typeof(Wacs.Core.Types.Limits).GetConstructor(new[]
            {
                typeof(Wacs.Core.Types.Defs.AddrType),
                typeof(long),
                typeof(long?),
                typeof(bool),
            })!;
            var nullableLongCtor = typeof(long?).GetConstructor(new[] { typeof(long) })!;
            var tableTypeCtor = typeof(Wacs.Core.Types.TableType).GetConstructor(new[]
            {
                typeof(Wacs.Core.Types.Defs.ValType),
                typeof(Wacs.Core.Types.Limits),
                typeof(Wacs.Core.Types.Expression),
            })!;
            var valueCtor = typeof(Value).GetConstructor(new[]
                { typeof(Wacs.Core.Types.Defs.ValType) })!;
            var tableInstanceCtor = typeof(TableInstance).GetConstructor(new[]
                { typeof(Wacs.Core.Types.TableType), typeof(Value) })!;

            for (int i = 0; i < n; i++)
            {
                var (min, max, elemType, _) = data.Tables[i];
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);

                // new Limits(AddrType.I32, min, max-as-nullable, false)
                il.Emit(OpCodes.Ldc_I4, (int)Wacs.Core.Types.Defs.AddrType.I32);
                il.Emit(OpCodes.Ldc_I8, min);
                // max in (long min, long max, …) tuple — always present
                // but ModuleInitData stuffs uint.MaxValue when unbounded.
                // Pass as Nullable<long>(max).
                il.Emit(OpCodes.Ldc_I8, max);
                il.Emit(OpCodes.Newobj, nullableLongCtor);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Newobj, limitsCtor);

                // new TableType(elemType, limits, null)
                var limitsLoc = il.DeclareLocal(typeof(Wacs.Core.Types.Limits));
                il.Emit(OpCodes.Stloc, limitsLoc);
                il.Emit(OpCodes.Ldc_I4, (int)elemType);
                il.Emit(OpCodes.Ldloc, limitsLoc);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Newobj, tableTypeCtor);

                // new Value(elemType)  — typed null
                il.Emit(OpCodes.Ldc_I4, (int)elemType);
                il.Emit(OpCodes.Newobj, valueCtor);

                il.Emit(OpCodes.Newobj, tableInstanceCtor);
                il.Emit(OpCodes.Stelem_Ref);
            }
        }

        /// <summary>
        /// IL: for each active element segment, write each funcref entry
        /// into the table at <c>elemOffset + j</c>. Null entries (-1) are
        /// skipped (the table starts pre-filled with typed null). GC entries
        /// (-2) are rejected upstream by <see cref="AssertAotLinkedFeasible"/>.
        /// </summary>
        private void EmitElementSegmentCopies(ILGenerator il, LocalBuilder ctxLocal, ModuleInitData data)
        {
            if (data.ActiveElementSegments.Length == 0) return;

            var tablesField = typeof(ThinContext).GetField(nameof(ThinContext.Tables))!;
            // TableInstance.Elements is a public List<Value> field, not a property.
            var elementsField = typeof(TableInstance).GetField(nameof(TableInstance.Elements))!;
            var listSetItem = typeof(System.Collections.Generic.List<Value>).GetMethod("set_Item",
                new[] { typeof(int), typeof(Value) })!;
            var listCount = typeof(System.Collections.Generic.List<Value>).GetProperty("Count")!.GetGetMethod()!;
            var valueFuncRefCtor = typeof(Value).GetConstructor(new[]
                { typeof(Wacs.Core.Types.Defs.ValType), typeof(int) })!;

            foreach (var (tableIdx, elemOffset, funcIndices) in data.ActiveElementSegments)
            {
                for (int j = 0; j < funcIndices.Length; j++)
                {
                    int funcIdx = funcIndices[j];
                    if (funcIdx < 0) continue; // null entry — table starts null
                    int slot = (int)elemOffset + j;

                    // ctx.Tables[tableIdx].Elements[slot] = new Value(FuncRef, funcIdx);
                    il.Emit(OpCodes.Ldloc, ctxLocal);
                    il.Emit(OpCodes.Ldfld, tablesField);
                    il.Emit(OpCodes.Ldc_I4, tableIdx);
                    il.Emit(OpCodes.Ldelem_Ref);
                    il.Emit(OpCodes.Ldfld, elementsField);
                    il.Emit(OpCodes.Ldc_I4, slot);
                    il.Emit(OpCodes.Ldc_I4, (int)Wacs.Core.Types.Defs.ValType.FuncRef);
                    il.Emit(OpCodes.Ldc_I4, funcIdx);
                    il.Emit(OpCodes.Newobj, valueFuncRefCtor);
                    il.Emit(OpCodes.Callvirt, listSetItem);
                }
            }
        }

        /// <summary>
        /// IL: <c>new GlobalInstance[N] { new GlobalInstance(new GlobalType(type, mut), initValue), ... }</c>.
        /// Pushes the globals array onto the stack.
        /// </summary>
        private void EmitGlobalsArray(ILGenerator il, ModuleInitData data)
        {
            int n = data.Globals.Length;
            il.Emit(OpCodes.Ldc_I4, n);
            il.Emit(OpCodes.Newarr, typeof(GlobalInstance));
            if (n == 0) return;

            var globalTypeCtor = typeof(Wacs.Core.Types.GlobalType).GetConstructor(new[]
            {
                typeof(Wacs.Core.Types.Defs.ValType),
                typeof(Wacs.Core.Types.Mutability),
                typeof(bool),
                typeof(bool),
            })!;
            var globalInstanceCtor = typeof(GlobalInstance).GetConstructor(new[]
                { typeof(Wacs.Core.Types.GlobalType), typeof(Value) })!;

            for (int i = 0; i < n; i++)
            {
                var (gtype, mut, init) = data.Globals[i];
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);

                // new GlobalType(gtype, mut, false, false)
                il.Emit(OpCodes.Ldc_I4, (int)gtype);
                il.Emit(OpCodes.Ldc_I4, (int)mut);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Newobj, globalTypeCtor);

                // initValue Value: emit from primitive literal
                EmitPrimitiveValue(il, gtype, init);

                il.Emit(OpCodes.Newobj, globalInstanceCtor);
                il.Emit(OpCodes.Stelem_Ref);
            }
        }

        /// <summary>
        /// IL: <c>new Value(...)</c> for primitive-typed initial values.
        /// Reads the const literal from <paramref name="init"/> and emits
        /// the appropriate typed Value ctor.
        /// <para>
        /// Ref types: when <see cref="Value.IsNullRef"/> is true, emits the
        /// single-arg <c>new Value(refType)</c> which yields the typed
        /// null. For non-null funcref/externref, emits the two-arg
        /// <c>new Value(refType, idx)</c> with the index pulled from
        /// <c>init.Data.Ptr</c>. (i31 and GC-constructed ref inits are
        /// gated out by <see cref="AssertAotLinkedFeasible"/>.)
        /// </para>
        /// </summary>
        private static void EmitPrimitiveValue(ILGenerator il, Wacs.Core.Types.Defs.ValType vt, Value init)
        {
            switch (vt)
            {
                case Wacs.Core.Types.Defs.ValType.I32:
                    il.Emit(OpCodes.Ldc_I4, init.Data.Int32);
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(int) })!);
                    break;
                case Wacs.Core.Types.Defs.ValType.I64:
                    il.Emit(OpCodes.Ldc_I8, init.Data.Int64);
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(long) })!);
                    break;
                case Wacs.Core.Types.Defs.ValType.F32:
                    il.Emit(OpCodes.Ldc_R4, init.Data.Float32);
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(float) })!);
                    break;
                case Wacs.Core.Types.Defs.ValType.F64:
                    il.Emit(OpCodes.Ldc_R8, init.Data.Float64);
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(double) })!);
                    break;
                default:
                    if (init.IsNullRef)
                    {
                        // Typed null ref: `new Value(refType)`.
                        il.Emit(OpCodes.Ldc_I4, (int)vt);
                        il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(Wacs.Core.Types.Defs.ValType) })!);
                    }
                    else
                    {
                        // Non-null funcref / externref: `new Value(refType, idx)`
                        // pulls the func / extern index out of init.Data.Ptr.
                        // (Other non-null ref shapes are gated out earlier.)
                        il.Emit(OpCodes.Ldc_I4, (int)vt);
                        il.Emit(OpCodes.Ldc_I4, (int)init.Data.Ptr);
                        il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(
                            new[] { typeof(Wacs.Core.Types.Defs.ValType), typeof(int) })!);
                    }
                    break;
            }
        }

        /// <summary>
        /// IL: <c>new MemoryInstance[N] { new MemoryInstance(new MemoryType((uint)min[0], (uint?)max[0])), ... }</c>.
        /// Pushes the memories array onto the stack.
        /// </summary>
        private void EmitMemoryArray(ILGenerator il, ModuleInitData data)
        {
            int n = data.Memories.Length;
            il.Emit(OpCodes.Ldc_I4, n);
            il.Emit(OpCodes.Newarr, typeof(MemoryInstance));
            if (n == 0) return;

            var memoryTypeCtor = typeof(MemoryType).GetConstructor(new[]
                { typeof(uint), typeof(uint?), typeof(Wacs.Core.Types.Defs.AddrType) })!;
            var nullableUintCtor = typeof(uint?).GetConstructor(new[] { typeof(uint) })!;
            var memoryInstanceCtor = typeof(MemoryInstance).GetConstructor(new[] { typeof(MemoryType) })!;

            for (int i = 0; i < n; i++)
            {
                var (min, max) = data.Memories[i];
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);

                // new MemoryType((uint)min, max.HasValue ? (uint?)max : default(uint?), AddrType.I32)
                il.Emit(OpCodes.Ldc_I4, (int)(uint)min);
                if (max.HasValue)
                {
                    il.Emit(OpCodes.Ldc_I4, (int)(uint)max.Value);
                    il.Emit(OpCodes.Newobj, nullableUintCtor);
                }
                else
                {
                    var nullableLoc = il.DeclareLocal(typeof(uint?));
                    il.Emit(OpCodes.Ldloca, nullableLoc);
                    il.Emit(OpCodes.Initobj, typeof(uint?));
                    il.Emit(OpCodes.Ldloc, nullableLoc);
                }
                il.Emit(OpCodes.Ldc_I4, (int)Wacs.Core.Types.Defs.AddrType.I32);
                il.Emit(OpCodes.Newobj, memoryTypeCtor);

                il.Emit(OpCodes.Newobj, memoryInstanceCtor);
                il.Emit(OpCodes.Stelem_Ref);
            }
        }

        /// <summary>
        /// IL: for each active data segment, copy the segment's bytes
        /// (held in an RVA-mapped <c>__WACSAotData.Segment_N</c> field,
        /// materialized via <c>RuntimeHelpers.CreateSpan&lt;byte&gt;</c>)
        /// into the right memory's <c>Data</c> array at the right offset
        /// via <see cref="BulkHelpers.CopySegmentToMemory"/>. Zero-copy at
        /// the source: bytes are demand-paged from the PE's initialized-
        /// data section and never materialize a heap <c>byte[]</c>.
        /// </summary>
        private void EmitDataSegmentCopies(ILGenerator il, LocalBuilder ctxLocal, ModuleInitData data)
        {
            if (data.ActiveDataSegments.Length == 0) return;

            var memoriesField = typeof(ThinContext).GetField(nameof(ThinContext.Memories))!;
            var memoryDataField = typeof(MemoryInstance).GetField(nameof(MemoryInstance.Data))!;
            var createSpanOpen = typeof(System.Runtime.CompilerServices.RuntimeHelpers).GetMethod(
                nameof(System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan),
                BindingFlags.Public | BindingFlags.Static)!;
            var createSpanByte = createSpanOpen.MakeGenericMethod(typeof(byte));
            var copyHelper = typeof(BulkHelpers).GetMethod(
                nameof(BulkHelpers.CopySegmentToMemory),
                BindingFlags.Public | BindingFlags.Static)!;

            foreach (var (memIdx, offset, segId) in data.ActiveDataSegments)
            {
                if (!data.SavedDataSegments.TryGetValue(segId, out var bytes) || bytes == null) continue;
                if (bytes.Length == 0) continue;
                var segField = GetAotDataSegmentField(segId, bytes);

                // BulkHelpers.CopySegmentToMemory(
                //     RuntimeHelpers.CreateSpan<byte>(__ldtoken __WACSAotData.Segment_N__),
                //     ctx.Memories[memIdx].Data,
                //     (int)offset, bytes.Length);
                il.Emit(OpCodes.Ldtoken, segField);
                il.Emit(OpCodes.Call, createSpanByte);

                il.Emit(OpCodes.Ldloc, ctxLocal);
                il.Emit(OpCodes.Ldfld, memoriesField);
                il.Emit(OpCodes.Ldc_I4, memIdx);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldfld, memoryDataField);

                il.Emit(OpCodes.Ldc_I4, (int)offset);
                il.Emit(OpCodes.Ldc_I4, bytes.Length);
                il.Emit(OpCodes.Call, copyHelper);
            }
        }

        /// <summary>
        /// Get-or-create a static RVA-mapped data field on the
        /// <c>__WACSAotData</c> holder type, backed by
        /// <paramref name="bytes"/>. The returned <see cref="FieldBuilder"/>'s
        /// runtime type is a synthesized fixed-size value-type with
        /// <c>HasFieldRVA</c> pointing into the PE's initialized-data
        /// section — bytes are demand-paged by the OS loader and consumed
        /// zero-copy via <c>RuntimeHelpers.CreateSpan&lt;byte&gt;</c> at the
        /// call site. No cctor; no decode; no GC heap allocation.
        /// </summary>
        private FieldBuilder GetAotDataSegmentField(int segId, byte[] bytes)
        {
            if (_aotDataType == null)
            {
                _aotDataType = _moduleBuilder.DefineType(
                    $"{_namespace}.__WACSAotData",
                    TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
                // The maximum segId we see at transpile time bounds the array.
                _aotDataFields = new FieldBuilder?[InitRegistry.Get(_initDataId)
                    .SavedDataSegments.Keys.DefaultIfEmpty(0).Max() + 1];
            }
            if (segId < _aotDataFields!.Length && _aotDataFields[segId] != null)
                return _aotDataFields[segId]!;

            // Grow the array if we hit a sparse / out-of-range segId.
            if (segId >= _aotDataFields.Length)
            {
                var grown = new FieldBuilder?[segId + 1];
                Array.Copy(_aotDataFields, grown, _aotDataFields.Length);
                _aotDataFields = grown;
            }

            // DefineInitializedData synthesizes a private nested struct
            // typed __StaticArrayInitTypeSize=N and emits a static field of
            // that type with HasFieldRVA. Field attribute set: Public|Static
            // (InitOnly is implicit for RVA fields and not accepted here).
            var field = _aotDataType.DefineInitializedData(
                $"Segment_{segId}", bytes,
                FieldAttributes.Public | FieldAttributes.Static);
            _aotDataFields[segId] = field;
            return field;
        }

        /// <summary>
        /// No-op now that data-segment fields are RVA-backed: the bytes
        /// live in the PE's initialized-data section and are loaded by the
        /// runtime at type-load time. Kept as a hook in case future
        /// strategies (compressed RVA, lazy decode) need a finalize step.
        /// </summary>
        private void FinalizeAotDataHolder()
        {
            // intentionally empty
        }

        /// <summary>
        /// Collect the list of module-init features that AotLinked emission
        /// can't yet inline as IL. Empty list = the AotLinked ctor body is
        /// safe to emit; non-empty = the codec stack
        /// (<c>Standard</c> emission) is required.
        /// <para>
        /// Features currently inlinable: memories + primitive globals + null
        /// refs + non-null funcref/externref globals + active data segments +
        /// active element segments with funcref / null entries.
        /// Features that still force <c>Standard</c>: GC-typed globals
        /// (array.new / struct.new init), GC element segment entries, deferred
        /// global inits with imported-global dependencies.
        /// </para>
        /// </summary>
        private List<string> CollectAotLinkedUnsupported()
        {
            if (_initDataId < 0) PrepareInitData();
            var data = InitRegistry.Get(_initDataId);

            var unsupported = new List<string>();
            if (data.GcGlobalInits.Count > 0)       unsupported.Add($"{data.GcGlobalInits.Count} GC global inits");
            if (data.DeferredGlobalInits.Count > 0) unsupported.Add($"{data.DeferredGlobalInits.Count} deferred global inits");
            if (data.GcElementValues.Count > 0)     unsupported.Add("GC element values");
            // Globals: primitive types (i32/i64/f32/f64), null refs of any
            // ref type, and non-null FuncRef/ExternRef inits are inlined by
            // EmitGlobalsArray. Other non-null ref shapes (i31, struct.new,
            // array.new, etc.) need GC-construction IL we don't emit yet.
            for (int gi = 0; gi < data.Globals.Length; gi++)
            {
                var (gtype, _, init) = data.Globals[gi];
                if (IsPrimitiveValType(gtype)) continue;
                if (init.IsNullRef) continue;
                if (gtype == Wacs.Core.Types.Defs.ValType.FuncRef
                    || gtype == Wacs.Core.Types.Defs.ValType.ExternRef)
                    continue;
                unsupported.Add($"global {gi} with non-null {gtype} init");
                break;
            }
            // Tables with a non-trivial init expression (one that evaluates
            // to a non-null reference, potentially against globals) need
            // expression evaluation. Default funcref tables carry an init
            // expression that's just `ref.null T; end` — those are the
            // common case and indistinguishable from initExpr == null
            // semantically. Allow all for now; if the init expression
            // evaluates to non-null at runtime, the AotLinked ctor will
            // leave the table holding null instead, which the embedder
            // sees as a wrong-default behavior they can fall back to
            // Standard for.
            // Element segments with funcIndices[j] == -2 (GC-typed) require
            // GcElementValues; primitive funcref entries (>=0) and nulls
            // (-1) are handled by EmitElementSegmentCopies.
            foreach (var (_, _, funcIndices) in data.ActiveElementSegments)
            {
                bool hasGc = false;
                for (int k = 0; k < funcIndices.Length; k++)
                    if (funcIndices[k] == -2) { hasGc = true; break; }
                if (hasGc)
                {
                    unsupported.Add("element segment with GC-typed entries");
                    break;
                }
            }
            return unsupported;
        }

        /// <summary>
        /// True when <see cref="CollectAotLinkedUnsupported"/> returns empty —
        /// i.e. the AotLinked ctor body can be safely emitted for this module.
        /// </summary>
        private bool IsAotLinkedFeasible() => CollectAotLinkedUnsupported().Count == 0;

        /// <summary>
        /// Stricter gate than <see cref="IsAotLinkedFeasible"/>: only true
        /// for module shapes covered by an existing AotLinked emission test.
        /// Used by <see cref="EmissionTarget.Auto"/> to decide whether to
        /// silently promote the user's emission choice.
        /// <para>
        /// Conservative subset: no module imports (function / memory /
        /// table / global / tag), no passive element segments, no
        /// exception tags, single memory at most.
        /// AotLinked emission's runtime correctness is broader than this
        /// conservative subset, but the proof obligations (no
        /// `memory.init` / `table.init` / `data.drop` / `elem.drop`
        /// disagreement, no linker integration) only hold here.
        /// Promote-then-fail-loudly is worse than don't-promote-then-
        /// codec-decode, so this stays narrow.
        /// </para>
        /// </summary>
        private bool IsAotLinkedAutoPromotable()
        {
            if (!IsAotLinkedFeasible()) return false;
            if (_initDataId < 0) PrepareInitData();
            var data = InitRegistry.Get(_initDataId);

            // Imports of any kind disqualify — the AotLinked ctor doesn't
            // wire imported memory / table / global slots, and import
            // delegates depend on the IImports parameter being handed in
            // by a Module-class ctor that AotLinked doesn't currently emit.
            if (_wasmModule.ImportedFunctions.Count > 0) return false;
            foreach (var import in _wasmModule.Imports)
            {
                if (import.Desc is WasmModule.ImportDesc.MemDesc) return false;
                if (import.Desc is WasmModule.ImportDesc.TableDesc) return false;
                if (import.Desc is WasmModule.ImportDesc.GlobalDesc) return false;
                if (import.Desc is WasmModule.ImportDesc.TagDesc) return false;
            }

            // Multi-memory needs additional Newobj sequencing AotLinked
            // doesn't yet emit.
            if (data.Memories.Length > 1) return false;

            // Exception tags need TagInstance ctor IL we don't emit yet.
            if (data.ImportedTagCount > 0 || data.LocalTagTypes.Length > 0) return false;

            // Passive element segments now register via
            // EmitPassiveElementSegmentRegistrations. The encode helper
            // only handles funcref / null entries — verify every passive
            // segment's initializers are encodable, otherwise stay on
            // Standard. (Modules with GC-typed entries are already
            // gated by IsAotLinkedFeasible's GcElementValues check.)
            var droppedSet = new HashSet<int>(data.ActiveElemIndices);
            for (int localIdx = 0; localIdx < _wasmModule.Elements.Length; localIdx++)
            {
                if (droppedSet.Contains(localIdx)) continue;
                var elem = _wasmModule.Elements[localIdx];
                for (int j = 0; j < elem.Initializers.Length; j++)
                {
                    if (!TryEncodeFuncRefInitializer(elem.Initializers[j], out _))
                        return false;
                }
            }

            // Passive data segments are now registered in ModuleInit
            // by EmitPassiveDataSegmentRegistrations, so they're safe
            // to auto-promote.

            return true;
        }

        private void AssertAotLinkedFeasible()
        {
            var unsupported = CollectAotLinkedUnsupported();
            if (unsupported.Count > 0)
                throw new InvalidOperationException(
                    "TranspilerOptions.Emission = AotLinked is not yet supported for this module. " +
                    "Unsupported features: " + string.Join(", ", unsupported) +
                    ". Use Emission = Auto (the default) for automatic fallback to Standard, " +
                    "or Emission = Standard explicitly.");
        }

        /// <summary>
        /// Encode the prepared <see cref="ModuleInitData"/> via
        /// <see cref="InitDataCodec.Encode"/> and persist the bytes into a
        /// static RVA-mapped data field <c>Data</c> on a generated
        /// <c>__WACSInit</c> type in the same namespace as the Module
        /// class.
        ///
        /// <para>Bytes live in the PE's initialized-data section
        /// (<c>.sdata</c>/<c>.rdata</c>) and are surfaced as
        /// <see cref="ReadOnlySpan{Byte}"/> via
        /// <c>RuntimeHelpers.CreateSpan&lt;byte&gt;</c> at the call site.
        /// No cctor; no base64 decode; no per-load heap allocation for
        /// the blob itself. Replaces the legacy base64-string-literal
        /// path that the Lokad.ILPack writer required (PAB supports
        /// <c>DefineInitializedData</c> end-to-end through
        /// <c>Save</c>).</para>
        /// </summary>
        private FieldBuilder EmitEmbeddedInitData()
        {
            if (_embeddedInitField != null) return _embeddedInitField;
            if (_initDataId < 0) PrepareInitData();

            var data = InitRegistry.Get(_initDataId);
            var bytes = InitDataCodec.Encode(data);

            // Holder type + RVA-backed Data field. The synthesized field's
            // runtime type is a private fixed-size value type whose
            // metadata carries HasFieldRVA pointing into the PE's
            // initialized-data section.
            _embeddedInitType = _moduleBuilder.DefineType(
                $"{_namespace}.__WACSInit",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            _embeddedInitField = _embeddedInitType.DefineInitializedData(
                "Data", bytes,
                FieldAttributes.Public | FieldAttributes.Static);

            return _embeddedInitField;
        }

        private void EmitConstructor(FieldBuilder ctxField)
        {
            // ctor signature, in order:
            //   [0] (implicit) this
            //   [1] IImports imports          — when ImportsInterface != null
            //   [2 or 1] object hostBundle    — when HasResolverBindings
            //   [3 / 2 / 1] object resources  — when HasResourceBindings
            // Bundle and resources are appended LAST so the existing
            // (IImports)-only ctor shape stays binary-compatible for
            // consumers that don't use direct linking.
            var ctorParamList = new List<Type>();
            if (_interfaces.ImportsInterface != null)
                ctorParamList.Add(_interfaces.ImportsInterface);
            if (HasResolverBindings)
                ctorParamList.Add(typeof(object));
            if (HasResourceBindings)
                ctorParamList.Add(typeof(object));
            var ctorParams = ctorParamList.ToArray();

            var ctor = ModuleType!.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                ctorParams);

            int paramOrdinal = 1;
            if (_interfaces.ImportsInterface != null)
                ctor.DefineParameter(paramOrdinal++,
                    ParameterAttributes.None, "imports");
            int bundleArgOrdinal = -1;
            if (HasResolverBindings)
            {
                bundleArgOrdinal = paramOrdinal;
                ctor.DefineParameter(paramOrdinal++,
                    ParameterAttributes.None, "hostBundle");
            }
            int resourcesArgOrdinal = -1;
            if (HasResourceBindings)
            {
                resourcesArgOrdinal = paramOrdinal;
                ctor.DefineParameter(paramOrdinal++,
                    ParameterAttributes.None, "resources");
            }

            var il = ctor.GetILGenerator();

            // Call base()
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);

            // === Initialize ThinContext ===
            // Three emission shapes:
            //   Auto:      AotLinked when feasible, else Standard. Default.
            //   Standard:  InitializationHelper.InitializeFromEmbedded(
            //                __WACSInit.Data, _initDataId).
            //   AotLinked: ThinContext ctor body inlined as IL constants;
            //              AssertAotLinkedFeasible throws if the module
            //              shape needs the codec stack.
            var ctxLocal = il.DeclareLocal(typeof(ThinContext));
            var emission = _options?.Emission ?? EmissionTarget.Auto;
            // Auto: pick AotLinked when feasible, fall back to Standard.
            // The user's explicit choice (Standard / AotLinked) bypasses
            // the auto-promotion so consumers that need codec semantics
            // (cross-process registry hint, etc.) can force them.
            bool useAotLinked = emission == EmissionTarget.AotLinked
                || (emission == EmissionTarget.Auto && IsAotLinkedAutoPromotable());
            if (useAotLinked)
            {
                if (emission == EmissionTarget.AotLinked)
                    AssertAotLinkedFeasible();
                EmitAotLinkedCtorBody(il, ctxLocal);
            }
            else
            {
                // The generator embeds a codec-encoded ModuleInitData as
                // RVA-mapped bytes on a sibling type (__WACSInit.Data, built
                // below). The ctor materializes a ReadOnlySpan<byte> over
                // the mapped pages via RuntimeHelpers.CreateSpan and hands
                // that to InitializeFromEmbedded — which handles both
                // in-process (ModuleInit registrations already exist) and
                // cross-process (.dll loaded in a fresh process) via
                // data-segment remapping.
                var initDataField = EmitEmbeddedInitData();

                // Build the closed-generic ReadOnlySpan<byte> CreateSpan
                // MethodInfo and the matching ReadOnlySpan-overload of
                // InitializeFromEmbedded. The span is consumed by the
                // helper, which copies into a byte[] once for the codec
                // call (codec is byte[]-based today; span codec is a
                // follow-up). Net win over base64: no UTF-16 doubling on
                // disk, no Convert.FromBase64String at startup.
                var createSpanOpen = typeof(System.Runtime.CompilerServices.RuntimeHelpers).GetMethod(
                    nameof(System.Runtime.CompilerServices.RuntimeHelpers.CreateSpan),
                    BindingFlags.Public | BindingFlags.Static)!;
                var createSpanByte = createSpanOpen.MakeGenericMethod(typeof(byte));
                var initFromEmbedded = typeof(InitializationHelper).GetMethod(
                    nameof(InitializationHelper.InitializeFromEmbedded),
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(ReadOnlySpan<byte>), typeof(int) },
                    modifiers: null)!;

                il.Emit(OpCodes.Ldtoken, initDataField);
                il.Emit(OpCodes.Call, createSpanByte);
                il.Emit(OpCodes.Ldc_I4, _initDataId);
                il.Emit(OpCodes.Call, initFromEmbedded);
                il.Emit(OpCodes.Stloc, ctxLocal);
            }

            // === Wire import delegates from IImports ===
            if (_interfaces.ImportsInterface != null && _interfaces.ImportMethods.Count > 0)
            {
                EmitImportDelegateWiring(il, ctxLocal);
            }

            // === Stash the host-package bundle onto ctx.HostBundle ===
            // Direct-linked import IL reads this field and dispatches
            // through it instead of ImportDelegates[]. Slots in
            // ImportDelegates for resolver-handled imports stay wired
            // to the IImports stubs but the IL never reads them.
            if (bundleArgOrdinal >= 0)
            {
                il.Emit(OpCodes.Ldloc, ctxLocal);
                il.Emit(OpCodes.Ldarg, bundleArgOrdinal);
                il.Emit(OpCodes.Stfld, typeof(ThinContext).GetField(
                    nameof(ThinContext.HostBundle))!);
            }
            if (resourcesArgOrdinal >= 0)
            {
                il.Emit(OpCodes.Ldloc, ctxLocal);
                il.Emit(OpCodes.Ldarg, resourcesArgOrdinal);
                il.Emit(OpCodes.Stfld, typeof(ThinContext).GetField(
                    nameof(ThinContext.Resources))!);
            }

            // === Populate FuncTable ===
            EmitFuncTablePopulation(il, ctxLocal);

            // === Register precompiled call_indirect dispatch shims ===
            // For every distinct CLR delegate signature any function in
            // this module declares, emit a static Shim_<n> on the
            // Functions class that does the unbox/invoke/box dance the
            // runtime path used to build at call time via
            // Reflection.Emit.DynamicMethod. Registering them here makes
            // the AOT-published binary's call_indirect path codegen-free
            // (no PlatformNotSupportedException under PublishAot, and
            // ~18% faster than the DynamicInvoke fallback PR #103
            // shipped). Multi-result and >16-param funcs aren't covered
            // — those return null from BuildDelegateType and route
            // through MultiReturnMethodRegistry / DynamicInvoke fallback.
            EmitTypedDelegateShimsAndRegister(il);

            // === Cache cabi_realloc delegate, if exported ===
            // Aggregate-RETURN emit (string / list<T>) needs a
            // typed Func<int,int,int,int,int> reference to the
            // component's cabi_realloc export. Cache once at
            // ctor time so each return site is just a field load.
            EmitCabiReallocCacheIfPresent(il, ctxLocal);

            // === Bind delegates into table elements ===
            // After FuncTable is populated, walk tables and replace raw funcref
            // indices with delegate-carrying Values for cross-module call_indirect.
            il.Emit(OpCodes.Ldloc, ctxLocal);
            il.Emit(OpCodes.Call, typeof(ThinContext).GetMethod(
                nameof(ThinContext.BindTableDelegates),
                BindingFlags.Public | BindingFlags.Instance)!);

            // Store ctx field
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, ctxLocal);
            il.Emit(OpCodes.Stfld, ctxField);

            // === Call start function (spec §4.5.4 step 15) ===
            if (_hasStart)
            {
                int localIdx = _startFuncIndex - _importCount;
                if (localIdx >= 0 && localIdx < _methodBuilders.Length)
                {
                    il.Emit(OpCodes.Ldloc, ctxLocal);
                    il.Emit(OpCodes.Call, _methodBuilders[localIdx]);
                }
            }

            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Emit IL to extract typed delegates from the IImports interface methods
        /// and store them in ctx.ImportDelegates[].
        /// </summary>
        private void EmitImportDelegateWiring(ILGenerator il, LocalBuilder ctxLocal)
        {
            int importCount = _interfaces.ImportMethods.Count;

            // ctx.ImportDelegates = new Delegate[importCount];
            il.Emit(OpCodes.Ldloc, ctxLocal);
            il.Emit(OpCodes.Ldc_I4, importCount);
            il.Emit(OpCodes.Newarr, typeof(Delegate));
            il.Emit(OpCodes.Stfld, typeof(ThinContext).GetField(
                nameof(ThinContext.ImportDelegates))!);

            // For each import method, create a delegate wrapping the IImports method
            // and store it in ImportDelegates[i]
            for (int i = 0; i < importCount; i++)
            {
                var importMethod = _interfaces.ImportMethods[i];
                var funcType = importMethod.WasmType;

                // Build the delegate type for this import
                var delegateType = CallEmitter.BuildDelegateType(funcType);
                if (delegateType == null) continue;

                // ctx.ImportDelegates[i] = Delegate.CreateDelegate(delegateType, imports, methodInfo)
                // But since IImports is a TypeBuilder (not yet baked), we use:
                // ctx.ImportDelegates[i] = (cast)imports.MethodName  (via ldftn)

                il.Emit(OpCodes.Ldloc, ctxLocal);
                il.Emit(OpCodes.Ldfld, typeof(ThinContext).GetField(
                    nameof(ThinContext.ImportDelegates))!);
                il.Emit(OpCodes.Ldc_I4, i);

                // Push imports arg (arg 1 for instance ctor)
                il.Emit(OpCodes.Ldarg_1);
                // Push function pointer for the interface method
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldvirtftn, importMethod.Method!);
                // new DelegateType(object, IntPtr)
                il.Emit(OpCodes.Newobj, delegateType.GetConstructor(
                    new[] { typeof(object), typeof(IntPtr) })!);

                il.Emit(OpCodes.Stelem_Ref);
            }
        }

        /// <summary>
        /// If the module exports `cabi_realloc`, cache its typed
        /// delegate on <c>ctx.CabiRealloc</c>. The funcIdx is
        /// known at transpile time; the IL just casts the
        /// matching FuncTable entry to
        /// <c>Func&lt;int,int,int,int,int&gt;</c>.
        /// </summary>
        private void EmitCabiReallocCacheIfPresent(ILGenerator il,
            LocalBuilder ctxLocal)
        {
            int? cabiFuncIdx = null;
            foreach (var ex in _interfaces.ExportMethods)
            {
                if (ex.WasmName == "cabi_realloc")
                {
                    cabiFuncIdx = ex.FuncIndex;
                    break;
                }
            }
            if (cabiFuncIdx == null) return;

            // ctx.CabiRealloc = (Func<int,int,int,int,int>) ctx.FuncTable[cabiFuncIdx]
            il.Emit(OpCodes.Ldloc, ctxLocal);
            il.Emit(OpCodes.Ldloc, ctxLocal);
            il.Emit(OpCodes.Ldfld, typeof(ThinContext).GetField(
                nameof(ThinContext.FuncTable))!);
            il.Emit(OpCodes.Ldc_I4, cabiFuncIdx.Value);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Castclass,
                typeof(Func<int, int, int, int, int>));
            il.Emit(OpCodes.Stfld, typeof(ThinContext).GetField(
                nameof(ThinContext.CabiRealloc))!);
        }

        /// <summary>
        /// Emit IL to populate ctx.FuncTable with all function delegates.
        /// Imports come from ImportDelegates, locals use Delegate.CreateDelegate
        /// to bind ctx as the first argument of the static transpiled method.
        ///
        /// This enables call_indirect/call_ref to dispatch through FuncTable
        /// without needing to know the ThinContext at the call site.
        /// </summary>
        private void EmitFuncTablePopulation(ILGenerator il, LocalBuilder ctxLocal)
        {
            int totalFuncs = _importCount + _methodBuilders.Length;
            if (totalFuncs == 0) return;

            // ctx.FuncTable = new Delegate[totalFuncs];
            il.Emit(OpCodes.Ldloc, ctxLocal);
            il.Emit(OpCodes.Ldc_I4, totalFuncs);
            il.Emit(OpCodes.Newarr, typeof(Delegate));
            il.Emit(OpCodes.Stfld, typeof(ThinContext).GetField(
                nameof(ThinContext.FuncTable))!);

            // Copy import delegates (0..importCount-1)
            if (_importCount > 0 && _interfaces.ImportMethods.Count > 0)
            {
                for (int i = 0; i < Math.Min(_importCount, _interfaces.ImportMethods.Count); i++)
                {
                    il.Emit(OpCodes.Ldloc, ctxLocal);
                    il.Emit(OpCodes.Ldfld, typeof(ThinContext).GetField(
                        nameof(ThinContext.FuncTable))!);
                    il.Emit(OpCodes.Ldc_I4, i);

                    il.Emit(OpCodes.Ldloc, ctxLocal);
                    il.Emit(OpCodes.Ldfld, typeof(ThinContext).GetField(
                        nameof(ThinContext.ImportDelegates))!);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldelem_Ref);

                    il.Emit(OpCodes.Stelem_Ref);
                }
            }

            // Local functions (importCount..totalFuncs-1)
            // For each local function, create a closed delegate via
            // Delegate.CreateDelegate(delegateType, ctx, methodInfo)
            // This binds ctx as the first arg of the static method, so the resulting
            // delegate signature matches what call_indirect expects (no ctx param).
            var createDelegateMethod = typeof(Delegate).GetMethod(
                nameof(Delegate.CreateDelegate),
                new[] { typeof(Type), typeof(object), typeof(MethodInfo) })!;

            for (int i = 0; i < _methodBuilders.Length; i++)
            {
                var mb = _methodBuilders[i];
                var funcType = _allFunctionTypes[_importCount + i];

                // Multi-return funcs use out-params on the static method, which
                // don't fit Action<>/Func<>. Leave their FuncTable slot null
                // and register the MethodInfo with MultiReturnMethodRegistry so
                // InvokeIndirectMulti can dispatch via reflection when a
                // call_indirect / call_ref hits a multi-return target.
                // Direct call IL (CallEmitter) still invokes the static method
                // directly and remains unaffected.
                if (funcType.ResultType.Types.Length > 1)
                {
                    // Emit a MultiShim_<funcIdx> static on _functionsType
                    // and register it directly — the precompiled IL path
                    // doesn't need MethodInfo at runtime, so the AOT
                    // binary's multi-return call_indirect path is
                    // codegen-free. (The legacy
                    // MultiReturnMethodRegistry.Register path stays as
                    // public API for non-transpiler callers.)
                    EmitMultiReturnShimAndRegister(il, mb, funcType, _importCount + i);
                    continue;
                }

                // Build the delegate type without ctx (what call_indirect expects)
                var delegateType = CallEmitter.BuildDelegateType(funcType);
                if (delegateType == null) continue;

                // ctx.FuncTable[importCount + i] = Delegate.CreateDelegate(delegateType, ctx, methodInfo)
                il.Emit(OpCodes.Ldloc, ctxLocal);
                il.Emit(OpCodes.Ldfld, typeof(ThinContext).GetField(
                    nameof(ThinContext.FuncTable))!);
                il.Emit(OpCodes.Ldc_I4, _importCount + i);

                // Push delegateType (typeof)
                il.Emit(OpCodes.Ldtoken, delegateType);
                il.Emit(OpCodes.Call, typeof(Type).GetMethod(
                    nameof(Type.GetTypeFromHandle), new[] { typeof(RuntimeTypeHandle) })!);

                // Push ctx (first argument to bind)
                il.Emit(OpCodes.Ldloc, ctxLocal);

                // Push MethodInfo via ldtoken
                il.Emit(OpCodes.Ldtoken, mb);
                il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod(
                    nameof(MethodBase.GetMethodFromHandle), new[] { typeof(RuntimeMethodHandle) })!);
                il.Emit(OpCodes.Castclass, typeof(MethodInfo));

                // Call Delegate.CreateDelegate(Type, object, MethodInfo)
                il.Emit(OpCodes.Call, createDelegateMethod);

                il.Emit(OpCodes.Stelem_Ref);
            }
        }

        /// <summary>
        /// Emit one static <c>Shim_&lt;n&gt;</c> per distinct CLR delegate
        /// signature this module declares (deduped across imports + locals)
        /// and a corresponding <c>TypedDelegateInvoker.RegisterShim</c>
        /// call into the ctor IL. The shim's body mirrors
        /// <c>TypedDelegateInvoker.Build</c> exactly — cast Delegate to
        /// concrete, unbox each arg, callvirt Invoke, box return — but
        /// emitted at transpile time so the AOT path doesn't need
        /// Reflection.Emit at runtime.
        /// </summary>
        private void EmitTypedDelegateShimsAndRegister(ILGenerator ctorIl)
        {
            // De-duplicate by delegate Type. BuildDelegateType returns the
            // same BCL Func/Action instantiation for any two wasm types
            // that lower to the same CLR signature, so this keys cleanly
            // even when the wasm has many functions sharing a signature.
            var unique = new List<Type>();
            var seen = new HashSet<Type>();
            foreach (var ft in _allFunctionTypes)
            {
                var dt = Emitters.CallEmitter.BuildDelegateType(ft);
                if (dt != null && seen.Add(dt))
                    unique.Add(dt);
            }
            if (unique.Count == 0) return;

            var invokerDelegateType = typeof(TypedDelegateInvoker.Invoker);
            var invokerCtor = invokerDelegateType.GetConstructor(
                new[] { typeof(object), typeof(IntPtr) })
                ?? throw new InvalidOperationException(
                    "Invoker delegate ctor not found");
            var registerShim = typeof(TypedDelegateInvoker).GetMethod(
                nameof(TypedDelegateInvoker.RegisterShim))!;
            var getTypeFromHandle = typeof(Type).GetMethod(
                nameof(Type.GetTypeFromHandle), new[] { typeof(RuntimeTypeHandle) })!;

            for (int idx = 0; idx < unique.Count; idx++)
            {
                var delegateType = unique[idx];
                var shim = EmitTypedDelegateShim(delegateType, idx);

                // TypedDelegateInvoker.RegisterShim(
                //     typeof(<delegateType>),
                //     new Invoker(null, &Functions.Shim_<idx>));
                ctorIl.Emit(OpCodes.Ldtoken, delegateType);
                ctorIl.Emit(OpCodes.Call, getTypeFromHandle);
                ctorIl.Emit(OpCodes.Ldnull);                  // static — no target
                ctorIl.Emit(OpCodes.Ldftn, shim);
                ctorIl.Emit(OpCodes.Newobj, invokerCtor);
                ctorIl.Emit(OpCodes.Call, registerShim);
            }
        }

        private MethodBuilder EmitTypedDelegateShim(Type delegateType, int idx)
        {
            var invokeMethod = delegateType.GetMethod("Invoke")
                ?? throw new InvalidOperationException(
                    $"Delegate type {delegateType} has no Invoke method");
            var parameters = invokeMethod.GetParameters();

            var shim = _functionsType.DefineMethod(
                $"Shim_{idx}",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                new[] { typeof(Delegate), typeof(object[]) });

            var il = shim.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, delegateType);

            for (int i = 0; i < parameters.Length; i++)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);
                var pt = parameters[i].ParameterType;
                if (pt.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, pt);
                else
                    il.Emit(OpCodes.Castclass, pt);
            }

            il.Emit(OpCodes.Callvirt, invokeMethod);

            if (invokeMethod.ReturnType == typeof(void))
                il.Emit(OpCodes.Ldnull);
            else if (invokeMethod.ReturnType.IsValueType)
                il.Emit(OpCodes.Box, invokeMethod.ReturnType);

            il.Emit(OpCodes.Ret);
            return shim;
        }

        /// <summary>
        /// Emit a per-function MultiShim_&lt;funcIdx&gt; static on the
        /// Functions class for a multi-return wasm function and register
        /// it with <see cref="MultiReturnMethodRegistry.RegisterShim"/>
        /// from the Module ctor IL. Mirrors
        /// <c>InitializationHelper.MultiReturnMethodRegistry.BuildInvoker</c>
        /// — declare out-locals, push ctx + unboxed args + ldloca for
        /// each out, call the target, build object[] of [r0, out1, ...
        /// outK-1] all boxed — but at transpile time so the AOT binary
        /// has no Reflection.Emit dependency on the multi-return path.
        /// </summary>
        private void EmitMultiReturnShimAndRegister(
            ILGenerator ctorIl, MethodBuilder target, Wacs.Core.Types.FunctionType funcType, int globalFuncIdx)
        {
            // The shim signature is (ThinContext, object[]) → object[].
            // Body: declare out-locals for each result beyond the first,
            // push ctx, unbox each input arg, ldloca for each out, call
            // target, capture the return + each out into a boxed object[].
            var paramTypes = funcType.ParameterTypes.Types;
            var resultTypes = funcType.ResultType.Types;
            int outCount = resultTypes.Length - 1;

            var shim = _functionsType.DefineMethod(
                $"MultiShim_{globalFuncIdx}",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object[]),
                new[] { typeof(ThinContext), typeof(object[]) });

            var il = shim.GetILGenerator();

            // Map wasm types → CLR types using the same mapper the rest
            // of the transpiler uses, so the unbox/box ops match the
            // target method's actual ABI.
            var clrParamTypes = paramTypes.Select(t => ModuleTranspiler.MapValType(t)).ToArray();
            var clrR0Type = ModuleTranspiler.MapValType(resultTypes[0]);
            var clrOutTypes = new Type[outCount];
            for (int k = 0; k < outCount; k++)
                clrOutTypes[k] = ModuleTranspiler.MapValType(resultTypes[k + 1]);

            // Declare out-locals.
            var outLocals = new LocalBuilder[outCount];
            for (int k = 0; k < outCount; k++)
                outLocals[k] = il.DeclareLocal(clrOutTypes[k]);

            // Push ctx.
            il.Emit(OpCodes.Ldarg_0);

            // Push each unboxed arg (args[i] → CLR param type).
            for (int k = 0; k < paramTypes.Length; k++)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, k);
                il.Emit(OpCodes.Ldelem_Ref);
                if (clrParamTypes[k].IsValueType)
                    il.Emit(OpCodes.Unbox_Any, clrParamTypes[k]);
                else
                    il.Emit(OpCodes.Castclass, clrParamTypes[k]);
            }

            // Push ldloca for each out-param.
            for (int k = 0; k < outCount; k++)
                il.Emit(OpCodes.Ldloca, outLocals[k]);

            // Call the target.
            il.EmitCall(OpCodes.Call, target, null);

            // Build result array: [r0, out1, ..., outK-1].
            var r0Local = il.DeclareLocal(clrR0Type);
            il.Emit(OpCodes.Stloc, r0Local);

            var resultLocal = il.DeclareLocal(typeof(object[]));
            il.Emit(OpCodes.Ldc_I4, 1 + outCount);
            il.Emit(OpCodes.Newarr, typeof(object));
            il.Emit(OpCodes.Stloc, resultLocal);

            // result[0] = box(r0)
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, r0Local);
            if (clrR0Type.IsValueType)
                il.Emit(OpCodes.Box, clrR0Type);
            il.Emit(OpCodes.Stelem_Ref);

            // result[k+1] = box(outLocal[k])
            for (int k = 0; k < outCount; k++)
            {
                il.Emit(OpCodes.Ldloc, resultLocal);
                il.Emit(OpCodes.Ldc_I4, k + 1);
                il.Emit(OpCodes.Ldloc, outLocals[k]);
                if (clrOutTypes[k].IsValueType)
                    il.Emit(OpCodes.Box, clrOutTypes[k]);
                il.Emit(OpCodes.Stelem_Ref);
            }

            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);

            // Now emit the registration IL into the ctor:
            //   MultiReturnMethodRegistry.RegisterShim(
            //       _initDataId, globalFuncIdx,
            //       new MultiReturnInvoker(null, &MultiShim_<idx>));
            var invokerDelType = typeof(MultiReturnInvoker);
            var invokerCtor = invokerDelType.GetConstructor(
                new[] { typeof(object), typeof(IntPtr) })
                ?? throw new InvalidOperationException(
                    "MultiReturnInvoker delegate ctor not found");
            var registerShim = typeof(MultiReturnMethodRegistry).GetMethod(
                nameof(MultiReturnMethodRegistry.RegisterShim))!;

            ctorIl.Emit(OpCodes.Ldc_I4, _initDataId);
            ctorIl.Emit(OpCodes.Ldc_I4, globalFuncIdx);
            ctorIl.Emit(OpCodes.Ldnull);
            ctorIl.Emit(OpCodes.Ldftn, shim);
            ctorIl.Emit(OpCodes.Newobj, invokerCtor);
            ctorIl.Emit(OpCodes.Call, registerShim);
        }

        private void EmitExportMethods(FieldBuilder ctxField)
        {
            foreach (var export in _interfaces.ExportMethods)
            {
                var funcType = export.WasmType;
                int paramCount = funcType.ParameterTypes.Arity;
                var resultTypes = funcType.ResultType.Types;
                int outParamCount = resultTypes.Length > 1 ? resultTypes.Length - 1 : 0;

                // Build the method signature to match the interface
                var clrParamTypes = funcType.ParameterTypes.Types
                    .Select(t => ModuleTranspiler.MapValType(t)).ToArray();
                var allParamTypes = new Type[clrParamTypes.Length + outParamCount];
                Array.Copy(clrParamTypes, allParamTypes, clrParamTypes.Length);
                for (int i = 0; i < outParamCount; i++)
                    allParamTypes[clrParamTypes.Length + i] =
                        ModuleTranspiler.MapValType(resultTypes[i + 1]).MakeByRefType();

                Type returnType = resultTypes.Length >= 1
                    ? ModuleTranspiler.MapValType(resultTypes[0])
                    : typeof(void);

                var mb = ModuleType!.DefineMethod(
                    export.Name,
                    MethodAttributes.Public | MethodAttributes.Virtual,
                    returnType,
                    allParamTypes);

                // Name parameters
                for (int p = 0; p < clrParamTypes.Length; p++)
                    mb.DefineParameter(p + 1, ParameterAttributes.None, $"param{p}");
                for (int r = 0; r < outParamCount; r++)
                    mb.DefineParameter(clrParamTypes.Length + r + 1, ParameterAttributes.Out, $"result{r + 1}");

                int localIdx = export.FuncIndex - _importCount;

                if (localIdx < 0)
                {
                    // Exported import: forward through ctx.ImportDelegates[funcIndex]
                    EmitExportedImportBody(mb, ctxField, export.FuncIndex, funcType);
                    continue;
                }

                if (localIdx >= _methodBuilders.Length) continue;

                var targetMethod = _methodBuilders[localIdx];

                // Emit body: forward to Functions.FunctionN(ctx, params...)
                var il = mb.GetILGenerator();

                // Push ctx
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ctxField);

                // Push all params (arg 1..N on the instance method = params for the static method)
                for (int p = 0; p < clrParamTypes.Length; p++)
                    il.Emit(OpCodes.Ldarg, p + 1);

                // Push out param addresses
                for (int r = 0; r < outParamCount; r++)
                    il.Emit(OpCodes.Ldarg, clrParamTypes.Length + r + 1);

                il.Emit(OpCodes.Call, targetMethod);
                il.Emit(OpCodes.Ret);
            }
        }

        /// <summary>
        /// Emit method body for an exported import: forward through the IImports
        /// delegate stored in ctx.ImportDelegates[importIndex].
        ///
        /// The import delegate's signature matches the WASM function type (no ctx),
        /// so we invoke it directly with the method's parameters.
        /// </summary>
        private static void EmitExportedImportBody(
            MethodBuilder mb, FieldBuilder ctxField, int importIndex, FunctionType funcType)
        {
            var il = mb.GetILGenerator();
            var paramTypes = funcType.ParameterTypes.Types;
            var resultTypes = funcType.ResultType.Types;

            // Load the delegate: ctx.ImportDelegates[importIndex]
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ctxField);
            il.Emit(OpCodes.Ldfld, typeof(ThinContext).GetField(
                nameof(ThinContext.ImportDelegates))!);
            il.Emit(OpCodes.Ldc_I4, importIndex);
            il.Emit(OpCodes.Ldelem_Ref);

            // Build the delegate type and cast
            var delegateType = CallEmitter.BuildDelegateType(funcType);
            if (delegateType != null)
            {
                il.Emit(OpCodes.Castclass, delegateType);

                // Push all params (arg 1..N)
                for (int p = 0; p < paramTypes.Length; p++)
                    il.Emit(OpCodes.Ldarg, p + 1);

                // Invoke typed delegate
                il.Emit(OpCodes.Callvirt, delegateType.GetMethod("Invoke")!);
                il.Emit(OpCodes.Ret);
            }
            else
            {
                // Fallback for unsupported delegate types: DynamicInvoke
                // Pack params into object[]
                il.Emit(OpCodes.Ldc_I4, paramTypes.Length);
                il.Emit(OpCodes.Newarr, typeof(object));
                for (int p = 0; p < paramTypes.Length; p++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, p);
                    il.Emit(OpCodes.Ldarg, p + 1);
                    il.Emit(OpCodes.Box, ModuleTranspiler.MapValType(paramTypes[p]));
                    il.Emit(OpCodes.Stelem_Ref);
                }
                il.Emit(OpCodes.Callvirt, typeof(Delegate).GetMethod(
                    nameof(Delegate.DynamicInvoke))!);

                if (resultTypes.Length == 0)
                {
                    il.Emit(OpCodes.Pop);
                }
                else
                {
                    il.Emit(OpCodes.Unbox_Any, ModuleTranspiler.MapValType(resultTypes[0]));
                }
                il.Emit(OpCodes.Ret);
            }
        }

        private void EmitMemoryProperties(FieldBuilder ctxField)
        {
            if (_memoryCount == 0) return;

            // Memory property: byte[] Memory => _ctx.Memories[0]
            var memProp = ModuleType!.DefineProperty(
                "Memory", PropertyAttributes.None, typeof(byte[]), null);

            var getter = ModuleType.DefineMethod(
                "get_Memory",
                MethodAttributes.Public | MethodAttributes.SpecialName,
                typeof(byte[]), Type.EmptyTypes);

            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ctxField);
            il.Emit(OpCodes.Ldfld, typeof(ThinContext).GetField(nameof(ThinContext.Memories))!);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);              // MemoryInstance
            il.Emit(OpCodes.Ldfld, typeof(MemoryInstance).GetField(nameof(MemoryInstance.Data))!);
            il.Emit(OpCodes.Ret);

            memProp.SetGetMethod(getter);

            // MemoryCount property
            var countProp = ModuleType.DefineProperty(
                "MemoryCount", PropertyAttributes.None, typeof(int), null);
            var countGetter = ModuleType.DefineMethod(
                "get_MemoryCount",
                MethodAttributes.Public | MethodAttributes.SpecialName,
                typeof(int), Type.EmptyTypes);
            var il2 = countGetter.GetILGenerator();
            il2.Emit(OpCodes.Ldc_I4, _memoryCount);
            il2.Emit(OpCodes.Ret);
            countProp.SetGetMethod(countGetter);
        }

        private void EmitStartMethod(FieldBuilder ctxField)
        {
            int localIdx = _startFuncIndex - _importCount;
            if (localIdx < 0 || localIdx >= _methodBuilders.Length) return;

            var targetMethod = _methodBuilders[localIdx];

            var mb = ModuleType!.DefineMethod(
                "Start",
                MethodAttributes.Public,
                typeof(void), Type.EmptyTypes);

            var il = mb.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ctxField);
            il.Emit(OpCodes.Call, targetMethod);
            il.Emit(OpCodes.Ret);
        }

        private void EmitModuleNameProperty()
        {
            string name = _namespace.Split('.').LastOrDefault() ?? "WasmModule";

            var prop = ModuleType!.DefineProperty(
                "ModuleName", PropertyAttributes.None, typeof(string), null);
            var getter = ModuleType.DefineMethod(
                "get_ModuleName",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Static,
                typeof(string), Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ret);
            prop.SetGetMethod(getter);
        }
    }
}
