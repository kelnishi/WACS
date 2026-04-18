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
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Transpiler.AOT.Emitters;
using WasmModule = Wacs.Core.Module;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Describes the module's initialization requirements, computed at transpile time.
    /// Consumed by the Module constructor at runtime.
    /// </summary>
    public class ModuleInitData
    {
        /// <summary>Memory declarations: (minPages, maxPages) per memory. maxPages=null if not declared.</summary>
        public (long min, long? max)[] Memories { get; set; } = Array.Empty<(long, long?)>();

        /// <summary>Table declarations: (minSize, maxSize, elementType, initExpr) per table.</summary>
        public (long min, long max, ValType elemType, Expression? initExpr)[] Tables { get; set; }
            = Array.Empty<(long, long, ValType, Expression?)>();

        /// <summary>Global declarations: (type, mutability, initial value) per global.</summary>
        public (ValType type, Mutability mut, Value init)[] Globals { get; set; }
            = Array.Empty<(ValType, Mutability, Value)>();

        /// <summary>Structural hash per function index, for function type identity checks.</summary>
        public int[]? FuncTypeHashes { get; set; }

        /// <summary>
        /// Per-function ordered list of structural hashes: the function's own
        /// declared type hash, then every transitive supertype hash. Populated
        /// only when the module uses declared subtyping (sub &lt;super&gt; ...);
        /// consumers fall back to <see cref="FuncTypeHashes"/> for the direct
        /// match when this is null. Used by <c>ref.test</c> / <c>ref.cast</c>
        /// on funcref to decide subtype relationships without the interpreter
        /// TypesSpace at runtime (doc 1 §11.8).
        /// </summary>
        public int[][]? FuncTypeSuperHashes { get; set; }

        /// <summary>
        /// Structural hash per module-declared type, indexed by type index.
        /// Used by <c>ref.test</c> / <c>ref.cast</c> on funcref to resolve
        /// the target type's hash in standalone mode (no TypesSpace).
        /// </summary>
        public int[]? TypeHashes { get; set; }

        /// <summary>Parallel to <see cref="TypeHashes"/>: whether the declared
        /// type at this index is a function type.</summary>
        public bool[]? TypeIsFunc { get; set; }

        /// <summary>Active data segments: (memIdx, offset, segmentId) per segment.</summary>
        public (int memIdx, int offset, int segId)[] ActiveDataSegments { get; set; }
            = Array.Empty<(int, int, int)>();

        /// <summary>Active element segments: (tableIdx, offset, funcIndices) per segment.</summary>
        public (int tableIdx, int offset, int[] funcIndices)[] ActiveElementSegments { get; set; }
            = Array.Empty<(int, int, int[])>();

        /// <summary>
        /// GC-typed element values (parallel to ActiveElementSegments).
        /// For elements using ref.i31, struct.new, etc. instead of ref.func.
        /// Key: (elemSegIdx, slotIdx). Value: the Value to store in the table.
        /// </summary>
        public Dictionary<(int segIdx, int slotIdx), Value> GcElementValues { get; set; } = new();

        /// <summary>
        /// Element entries that depend on globals (global.get in initializer).
        /// (elemSegIdx, slotIdx within segment, globalIdx).
        /// After import resolution patches globals, these can be re-evaluated.
        /// </summary>
        public List<(int elemSegIdx, int slotIdx, int globalIdx)> DeferredElemGlobals { get; set; } = new();

        /// <summary>Start function index (-1 if none).</summary>
        public int StartFuncIndex { get; set; } = -1;

        /// <summary>Base ID for data segments in the global ModuleInit registry.</summary>
        public int DataSegmentBaseId { get; set; }

        /// <summary>Base ID for element segments in the global ModuleInit registry.</summary>
        public int ElemSegmentBaseId { get; set; }

        /// <summary>Module-local indices of active element segments (implicitly dropped after init).</summary>
        public int[] ActiveElemIndices { get; set; } = Array.Empty<int>();

        /// <summary>Module-local indices of active data segments (implicitly dropped after init).</summary>
        public int[] ActiveDataIndices { get; set; } = Array.Empty<int>();

        /// <summary>
        /// Globals whose initializers use global.get on imported globals.
        /// Stores the full initializer instruction list for re-evaluation after import patching.
        /// </summary>
        public List<(int globalIdx, Wacs.Core.Types.Expression initializer)> DeferredGlobalInits { get; set; } = new();

        /// <summary>
        /// Data segments whose offsets depend on globals (global.get in offset expression).
        /// (dataSegIdx in ActiveDataSegments, offset expression). Re-evaluated after import patching.
        /// </summary>
        public List<(int dataSegIdx, Wacs.Core.Types.Expression offsetExpr)> DeferredDataOffsets { get; set; } = new();

        /// <summary>
        /// Saved data segment bytes (keyed by segId) for linker re-apply.
        /// Active data segments are dropped after initialization, but the linker
        /// may need to re-copy them to shared imported memories.
        /// </summary>
        public Dictionary<int, byte[]> SavedDataSegments { get; set; } = new();

        /// <summary>Number of imported functions.</summary>
        public int ImportFuncCount { get; set; }

        /// <summary>Total function count (imports + locals).</summary>
        public int TotalFuncCount { get; set; }

        /// <summary>
        /// GC global initializers that can't be evaluated at transpile time.
        /// (globalIdx, gcTypeIdx, initKind, params).
        /// InitKind: 0=array.new(fill,len), 1=array.new_default(len), 2=struct.new(fields...)
        /// </summary>
        public List<GcGlobalInit> GcGlobalInits { get; set; } = new();

        /// <summary>
        /// Number of imported tags (doc 1 §13.1, doc 2 §5). Imports come first
        /// in the tag index space; the linker fills these slots with the
        /// exporter's TagInstance so reference equality = tag equality.
        /// </summary>
        public int ImportedTagCount { get; set; }

        /// <summary>
        /// DefType for each local tag, in tag-index order (imports excluded).
        /// Used at Initialize time to construct TagInstance objects with
        /// correct type info.
        /// </summary>
        public DefType[] LocalTagTypes { get; set; } = Array.Empty<DefType>();
    }

    /// <summary>
    /// Describes a GC-typed global that requires runtime object creation.
    /// </summary>
    public class GcGlobalInit
    {
        public int GlobalIndex { get; set; }
        public int TypeIndex { get; set; }
        public int InitKind { get; set; } // 0=array.new, 1=array.new_default
        public long[] Params { get; set; } = Array.Empty<long>(); // const args

        /// <summary>
        /// Raw ValType of the array's element (as int). Non-zero when the
        /// element is a reference/V128 type whose default Value must be
        /// constructed with the correct null-ref encoding (e.g. FuncRef
        /// default is Data.Ptr = long.MinValue, not zero). 0 when the
        /// element is a scalar or we couldn't determine the type.
        /// </summary>
        public int ElementValType { get; set; }
    }

    /// <summary>
    /// Registry of ModuleInitData instances, indexed by registration ID.
    /// The Module constructor references its init data by ID.
    /// </summary>
    public static class InitRegistry
    {
        private static readonly List<ModuleInitData> _initData = new();
        private static readonly object _lock = new();

        public static int Register(ModuleInitData data)
        {
            lock (_lock)
            {
                int id = _initData.Count;
                _initData.Add(data);
                return id;
            }
        }

        public static ModuleInitData Get(int id) => _initData[id];

        /// <summary>
        /// Reset the registry. Call between test runs.
        /// </summary>
        public static void Reset()
        {
            lock (_lock) { _initData.Clear(); }
        }

        /// <summary>
        /// The ID of the most recently registered init data.
        /// Used by ModuleLinker to find the init data for a just-transpiled module.
        /// </summary>
        public static int LastRegisteredId
        {
            get { lock (_lock) { return _initData.Count - 1; } }
        }
    }

    /// <summary>
    /// Registry of emitted GC CLR types, keyed by (initDataId, typeIndex).
    /// Populated at transpile time, consumed at runtime by InitializeGcGlobals.
    /// </summary>
    public static class GcTypeRegistry
    {
        private static readonly Dictionary<(int initId, int typeIdx), Type> _types = new();
        private static readonly object _lock = new();

        public static void Register(int initDataId, int typeIndex, Type clrType)
        {
            lock (_lock) { _types[(initDataId, typeIndex)] = clrType; }
        }

        public static Type? Get(int initDataId, int typeIndex)
        {
            lock (_lock)
            {
                return _types.TryGetValue((initDataId, typeIndex), out var t) ? t : null;
            }
        }

        public static void Reset()
        {
            lock (_lock) { _types.Clear(); }
        }
    }

    /// <summary>
    /// Runtime initialization for the Module constructor.
    /// Performs the WASM 3.0 instantiation sequence (Section 4.7.2):
    ///   1. Allocate memories (sized from memory section)
    ///   2. Allocate tables (sized from table section)
    ///   3. Initialize globals (evaluate constant initializers)
    ///   4. Copy active data segments to memories
    ///   5. Copy active element segments to tables
    ///
    /// Called from the Module constructor's IL.
    /// </summary>
    public static class InitializationHelper
    {
        /// <summary>
        /// Initialize a ThinContext from ModuleInitData.
        /// Returns a fully initialized ThinContext ready for function execution.
        /// </summary>
        public static ThinContext Initialize(int initDataId)
        {
            var data = InitRegistry.Get(initDataId);

            // 1. Allocate memories (as MemoryInstance for shared growth)
            var memories = new MemoryInstance[data.Memories.Length];
            for (int i = 0; i < data.Memories.Length; i++)
            {
                var (min, max) = data.Memories[i];
                var memType = new MemoryType(minimum: (uint)min,
                    maximum: max.HasValue ? (uint?)max.Value : null);
                memories[i] = new MemoryInstance(memType);
            }

            // 2. Allocate tables (default values evaluated after globals in step 3b)
            var tables = new TableInstance[data.Tables.Length];
            for (int i = 0; i < data.Tables.Length; i++)
            {
                var (min, max, elemType, _) = data.Tables[i];
                var tableType = new TableType(elemType, new Limits(AddrType.I32, min, max));
                tables[i] = new TableInstance(tableType, new Value(elemType));
            }

            // 3. Initialize globals
            var globals = new GlobalInstance[data.Globals.Length];
            for (int i = 0; i < data.Globals.Length; i++)
            {
                var (type, mut, init) = data.Globals[i];
                globals[i] = new GlobalInstance(new GlobalType(type, mut), init);
            }

            // 3b. Evaluate table default values (may depend on globals)
            for (int i = 0; i < data.Tables.Length; i++)
            {
                var (_, _, _, initExpr) = data.Tables[i];
                if (initExpr == null) continue;
                var defaultVal = EvaluateTableInit(initExpr, data, globals);
                if (!defaultVal.IsNullRef)
                {
                    var table = tables[i];
                    for (int j = 0; j < table.Elements.Count; j++)
                        table.Elements[j] = defaultVal;
                }
            }

            // 4. Copy active data segments to memories
            foreach (var (memIdx, offset, segId) in data.ActiveDataSegments)
            {
                ModuleInit.CopyDataSegment(memories, memIdx, offset, segId);
            }

            // 5. Copy active element segments to tables
            int elemSegIdx = 0;
            foreach (var (tableIdx, elemOffset, funcIndices) in data.ActiveElementSegments)
            {
                var table = tables[tableIdx];
                for (int j = 0; j < funcIndices.Length; j++)
                {
                    if (elemOffset + j >= table.Elements.Count) continue;
                    if (funcIndices[j] == -2)
                    {
                        // GC-typed element: lookup precomputed Value
                        if (data.GcElementValues.TryGetValue((elemSegIdx, j), out var gcVal))
                            table.Elements[elemOffset + j] = gcVal;
                    }
                    else if (funcIndices[j] >= 0)
                    {
                        table.Elements[elemOffset + j] = new Value(
                            ValType.FuncRef, funcIndices[j]);
                    }
                }
                elemSegIdx++;
            }

            // 6. Save data segment bytes for potential linker re-apply
            // (the linker may need to re-apply data to imported shared memories
            // after dropping — save copies keyed by segId before dropping)
            foreach (var (memIdx, offset, segId) in data.ActiveDataSegments)
            {
                var segData = ModuleInit.GetDataSegmentData(segId);
                if (segData != null && segData.Length > 0)
                    data.SavedDataSegments[segId] = (byte[])segData.Clone();
            }

            // 7. Implicitly drop active segments per spec §4.5.4
            foreach (var idx in data.ActiveElemIndices)
                ModuleInit.DropElemSegment(data.ElemSegmentBaseId + idx);
            foreach (var idx in data.ActiveDataIndices)
                ModuleInit.DropDataSegment(data.DataSegmentBaseId + idx);

            // Create context (MemoryInstance carries its own limits)
            var ctx = new ThinContext(
                memories: memories,
                tables: tables,
                globals: globals);
            ctx.DataSegmentBaseId = data.DataSegmentBaseId;
            ctx.ElemSegmentBaseId = data.ElemSegmentBaseId;
            ctx.InitDataId = initDataId;
            ctx.FuncTypeHashes = data.FuncTypeHashes;
            ctx.FuncTypeSuperHashes = data.FuncTypeSuperHashes;
            ctx.TypeHashes = data.TypeHashes;
            ctx.TypeIsFunc = data.TypeIsFunc;

            // Tags (doc 2 §5). Allocate the full tag index space; imports at
            // the front (linker fills them), locals after with freshly allocated
            // TagInstance objects. DefType may be null in standalone mode —
            // identity is reference-based so the field is unused by throw/catch.
            int totalTags = data.ImportedTagCount + data.LocalTagTypes.Length;
            if (totalTags > 0)
            {
                var tags = new TagInstance[totalTags];
                for (int i = 0; i < data.LocalTagTypes.Length; i++)
                    tags[data.ImportedTagCount + i] = new TagInstance(data.LocalTagTypes[i]!);
                ctx.Tags = tags;
            }

            // 7. Initialize GC-typed globals (array.new, etc.)
            InitializeGcGlobals(ctx, data, initDataId);

            return ctx;
        }

        /// <summary>
        /// Evaluate a table default value expression.
        /// Handles ref.null, ref.func, ref.i31(const), ref.i31(global.get).
        /// </summary>
        /// <summary>
        /// Evaluate a table default expression with resolved globals (including imports).
        /// Called by the linker after import resolution to fix table defaults that
        /// reference imported globals.
        /// </summary>
        public static Value EvaluateTableDefault(
            Wacs.Core.Types.Expression initExpr, GlobalInstance[] globals)
        {
            return EvaluateTableInit(initExpr, null, globals);
        }

        private static Value EvaluateTableInit(
            Wacs.Core.Types.Expression initExpr, ModuleInitData? data, GlobalInstance[] globals)
        {
            var stack = new Stack<Value>();
            foreach (var inst in initExpr.Instructions)
            {
                if (inst is Wacs.Core.Instructions.Numeric.InstI32Const i32c)
                    { stack.Push(new Value(i32c.Value)); continue; }
                if (inst is Wacs.Core.Instructions.Reference.InstRefNull rn)
                    { stack.Push(new Value(rn.RefType)); continue; }
                if (inst is Wacs.Core.Instructions.Reference.InstRefFunc rf)
                    { stack.Push(new Value(ValType.FuncRef, (int)rf.FunctionIndex.Value)); continue; }
                if (inst is Wacs.Core.Instructions.InstGlobalGet gg)
                {
                    int idx = gg.GetIndex();
                    if (idx < globals.Length)
                        stack.Push(globals[idx].Value);
                    else
                        stack.Push(default);
                    continue;
                }
                // GC instructions
                if (inst.GetType().Namespace == "Wacs.Core.Instructions.GC")
                {
                    var gcOp = inst.Op.xFB;
                    if (gcOp == Wacs.Core.OpCodes.GcCode.RefI31 && stack.Count > 0)
                    {
                        int val = stack.Pop().Data.Int32;
                        stack.Push(GcRuntimeHelpers.RefI31Value(val));
                    }
                    continue;
                }
            }
            return stack.Count > 0 ? stack.Pop() : default;
        }

        /// <summary>
        /// Create GC objects for globals whose initializers use GC constructors
        /// (array.new, array.new_default, etc.) and store them in the globals.
        /// </summary>
        private static void InitializeGcGlobals(ThinContext ctx, ModuleInitData data, int initDataId)
        {
            foreach (var gcInit in data.GcGlobalInits)
            {
                var clrType = GcTypeRegistry.Get(initDataId, gcInit.TypeIndex);
                if (clrType == null) continue;

                object? gcObj = null;
                switch (gcInit.InitKind)
                {
                    case 0: // array.new(fill_value, length)
                    {
                        int fillValue = gcInit.Params.Length > 0 ? (int)gcInit.Params[0] : 0;
                        int length = gcInit.Params.Length > 1 ? (int)gcInit.Params[1] : 0;
                        gcObj = Activator.CreateInstance(clrType);
                        var fields = clrType.GetFields();
                        // Fields[0] = elements array, Fields[1] = length
                        var elemType = fields[0].FieldType.GetElementType()!;
                        var arr = System.Array.CreateInstance(elemType, length);
                        // Fill with initial value. Value-typed element slots
                        // require an explicit null-ref encoding when they
                        // represent a ref type (default(Value) is Undefined,
                        // not a null ref — see note in case 1).
                        if (elemType == typeof(Value))
                        {
                            var fill = gcInit.ElementValType != 0
                                ? new Value((ValType)gcInit.ElementValType, fillValue)
                                : default(Value);
                            for (int i = 0; i < length; i++) arr.SetValue(fill, i);
                        }
                        else
                        {
                            for (int i = 0; i < length; i++)
                                arr.SetValue(Convert.ChangeType(fillValue, elemType), i);
                        }
                        fields[0].SetValue(gcObj, arr);
                        fields[1].SetValue(gcObj, length);
                        break;
                    }
                    case 1: // array.new_default(length)
                    {
                        int length = gcInit.Params.Length > 0 ? (int)gcInit.Params[0] : 0;
                        gcObj = Activator.CreateInstance(clrType);
                        var fields = clrType.GetFields();
                        var elemType = fields[0].FieldType.GetElementType()!;
                        var arr = System.Array.CreateInstance(elemType, length);
                        // For Value-typed slots, fill with the reftype's null
                        // encoding (Data.Ptr = long.MinValue, Type = RefType).
                        // Otherwise call_indirect on this element later reads
                        // default(Value) whose IsRefType is false, so the
                        // IsNullRef check skips and the Data.Ptr = 0 is
                        // dispatched as funcIdx 0.
                        if (elemType == typeof(Value) && gcInit.ElementValType != 0)
                        {
                            var nullRef = new Value((ValType)gcInit.ElementValType);
                            for (int i = 0; i < length; i++) arr.SetValue(nullRef, i);
                        }
                        fields[0].SetValue(gcObj, arr);
                        fields[1].SetValue(gcObj, length);
                        break;
                    }
                    case 2: // struct.new(fields...)
                    {
                        gcObj = Activator.CreateInstance(clrType);
                        var fieldInfos = GetStructFields(clrType);
                        for (int f = 0; f < fieldInfos.Length && f < gcInit.Params.Length; f++)
                        {
                            var fi = fieldInfos[f];
                            fi.SetValue(gcObj, CoerceStructFieldValue(fi.FieldType, gcInit.Params[f]));
                        }
                        break;
                    }
                    case 3: // struct.new_default — zero/default all fields
                    {
                        gcObj = Activator.CreateInstance(clrType);
                        // Activator already zero-initialized the fields;
                        // just need to set any Value-typed ref slots to the
                        // proper null-ref encoding (Data.Ptr = long.MinValue).
                        var fieldInfos = GetStructFields(clrType);
                        foreach (var fi in fieldInfos)
                        {
                            if (fi.FieldType == typeof(Value))
                            {
                                // Without explicit per-field reftype metadata,
                                // default(Value) is the safest initial state;
                                // spec-correct null-ref tagging happens when a
                                // struct.get reads the slot anyway.
                            }
                        }
                        break;
                    }
                }

                if (gcObj != null && gcInit.GlobalIndex < ctx.Globals.Length)
                {
                    // Wrap as Value with GcRef. Use reflection to bypass
                    // immutability check on the GlobalInstance.Value setter.
                    var val = Emitters.GcRuntimeHelpers.WrapRef(gcObj);
                    var field = typeof(GlobalInstance).GetField(
                        "_value", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    field?.SetValue(ctx.Globals[gcInit.GlobalIndex], val);
                }
            }
        }

        /// <summary>
        /// Returns just the WASM struct fields (named field_0, field_1, …) from
        /// an emitted WasmStruct_N class, skipping auxiliary fields like
        /// StructuralHash / _storeIndex. Ordered by the numeric suffix so the
        /// result matches WASM declaration order regardless of the CLR's field
        /// enumeration order.
        /// </summary>
        private static System.Reflection.FieldInfo[] GetStructFields(Type clrType)
        {
            var list = new System.Collections.Generic.List<(int idx, System.Reflection.FieldInfo fi)>();
            foreach (var fi in clrType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (fi.Name.StartsWith("field_", StringComparison.Ordinal)
                    && int.TryParse(fi.Name.Substring("field_".Length), out var n))
                    list.Add((n, fi));
            }
            list.Sort((a, b) => a.idx.CompareTo(b.idx));
            var result = new System.Reflection.FieldInfo[list.Count];
            for (int i = 0; i < list.Count; i++) result[i] = list[i].fi;
            return result;
        }

        /// <summary>
        /// Convert a constant param (stored as long) into the CLR type of a
        /// struct field. Packed WASM types (i8 / i16) map to byte / short and
        /// are truncated from the 32-bit literal. Ref types left as default
        /// when params only carry scalar constants.
        /// </summary>
        private static object? CoerceStructFieldValue(Type clrFieldType, long value)
        {
            if (clrFieldType == typeof(byte)) return (byte)(value & 0xFF);
            if (clrFieldType == typeof(short)) return (short)(value & 0xFFFF);
            if (clrFieldType == typeof(int)) return (int)value;
            if (clrFieldType == typeof(long)) return value;
            if (clrFieldType == typeof(float)) return BitConverter.Int32BitsToSingle((int)value);
            if (clrFieldType == typeof(double)) return BitConverter.Int64BitsToDouble(value);
            // Ref / V128 fields: params only carry scalar const initializers,
            // so ref-typed fields in global struct.new constants aren't
            // currently exercised. Leave the Activator-provided default.
            return null;
        }
    }
}
