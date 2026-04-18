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
            FunctionType[]? allFunctionTypes = null)
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
        public Type? CreateType() => ModuleType?.CreateType();

        private void EmitConstructor(FieldBuilder ctxField)
        {
            var ctorParams = _interfaces.ImportsInterface != null
                ? new[] { _interfaces.ImportsInterface }
                : Type.EmptyTypes;

            var ctor = ModuleType!.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                ctorParams);

            if (_interfaces.ImportsInterface != null)
                ctor.DefineParameter(1, ParameterAttributes.None, "imports");

            var il = ctor.GetILGenerator();

            // Call base()
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);

            // === Initialize via InitializationHelper ===
            // ThinContext ctx = InitializationHelper.Initialize(initDataId);
            il.Emit(OpCodes.Ldc_I4, _initDataId);
            il.Emit(OpCodes.Call, typeof(InitializationHelper).GetMethod(
                nameof(InitializationHelper.Initialize),
                BindingFlags.Public | BindingFlags.Static)!);
            var ctxLocal = il.DeclareLocal(typeof(ThinContext));
            il.Emit(OpCodes.Stloc, ctxLocal);

            // === Wire import delegates from IImports ===
            if (_interfaces.ImportsInterface != null && _interfaces.ImportMethods.Count > 0)
            {
                EmitImportDelegateWiring(il, ctxLocal);
            }

            // === Populate FuncTable ===
            EmitFuncTablePopulation(il, ctxLocal);

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
                    // MultiReturnMethodRegistry.Register(initDataId, funcIdx, mb)
                    il.Emit(OpCodes.Ldc_I4, _initDataId);
                    il.Emit(OpCodes.Ldc_I4, _importCount + i);
                    il.Emit(OpCodes.Ldtoken, mb);
                    il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod(
                        nameof(MethodBase.GetMethodFromHandle), new[] { typeof(RuntimeMethodHandle) })!);
                    il.Emit(OpCodes.Castclass, typeof(MethodInfo));
                    il.Emit(OpCodes.Call, typeof(MultiReturnMethodRegistry).GetMethod(
                        nameof(MultiReturnMethodRegistry.Register))!);
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
