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
using System.Linq;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Wraps a Delegate as IGcRef so it can be stored in Value.GcRef.
    /// Used to store bound delegates in table elements for cross-module
    /// call_indirect dispatch.
    /// </summary>
    public class DelegateRef : IGcRef
    {
        public readonly Delegate Target;
        public DelegateRef(Delegate target) => Target = target;
        public RefIdx StoreIndex => default(PtrIdx);
    }

    /// <summary>
    /// Flags indicating which transpiler features are active in this context.
    /// Set at transpile time from TranspilerOptions, baked into the Module constructor.
    /// Runtime helpers branch on these to enable layered behavior.
    /// </summary>
    [Flags]
    public enum TranspilerCapabilities
    {
        None = 0,
        /// <summary>Layer 0: CLR inheritance for intra-module subtyping (always on, included for interrogation).</summary>
        ClrInheritance = 1 << 0,
        /// <summary>Layer 1: cross-module structural hash comparison for ref.test/cast.</summary>
        StructuralHash = 1 << 1,
        /// <summary>Layer 2: full type descriptor registry for complete subtype queries.</summary>
        FullRegistry = 1 << 2,
    }

    /// <summary>
    /// Lean runtime context passed as the first parameter to every transpiled function.
    /// Holds pre-resolved module-level state for fast access without carrying
    /// interpreter-specific overhead (OpStack, InstructionPointer, Frame, etc.).
    ///
    /// When running standalone (without WasmRuntime), consumers construct this directly.
    /// When running inside the WACS framework, LoadTranspiledModule populates it from the Store.
    /// </summary>
    public class ThinContext
    {
        // === Transpiler Capabilities ===
        // Flags from TranspilerOptions, baked into the Module constructor.
        public TranspilerCapabilities Capabilities;

        // Init data ID for GcTypeRegistry lookups (concrete type ref.test/cast).
        public int InitDataId = -1;

        // === Linear Memory ===
        public MemoryInstance[] Memories;

        // Base offsets into the global ModuleInit registries.
        public int DataSegmentBaseId;
        public int ElemSegmentBaseId;

        // === Tables ===
        // Indexed by tableidx. Elements are function references for call_indirect.
        public TableInstance[] Tables;

        // === Globals ===
        // Indexed by globalidx. Mutable globals are written through GlobalInstance.Value.
        public GlobalInstance[] Globals;

        // === Function dispatch ===
        // ImportDelegates: typed delegates for imported functions.
        // Filled by the implementor at load time. Each delegate's signature matches
        // the WASM import's function type (no ThinContext parameter).
        public Delegate[] ImportDelegates;

        // FuncTable: delegates for ALL functions in the module index space.
        // Imports (0..importCount-1): same objects as ImportDelegates.
        // Locals (importCount..N): wrapping the transpiled static methods.
        // Used by call_indirect/call_ref for dynamic dispatch.
        public Delegate[] FuncTable;

        // === Interpreter interop (nullable — not needed for standalone) ===
        // When running inside the WACS framework, these enable mixed-mode
        // execution with interpreted modules. When standalone, they are null.
        public Store? Store;
        public ExecContext? ExecContext;
        public ModuleInstance? Module;

        // === Type information for runtime checks ===
        // Needed by ref.test, ref.cast, br_on_cast, call_indirect type checks.
        public TypesSpace? Types;

        /// <summary>
        /// Construct a ThinContext for standalone use (no WasmRuntime).
        /// </summary>
        public ThinContext(
            MemoryInstance[]? memories = null,
            TableInstance[]? tables = null,
            GlobalInstance[]? globals = null,
            Delegate[]? importDelegates = null,
            Delegate[]? funcTable = null)
        {
            Memories = memories ?? Array.Empty<MemoryInstance>();
            Tables = tables ?? Array.Empty<TableInstance>();
            Globals = globals ?? Array.Empty<GlobalInstance>();
            ImportDelegates = importDelegates ?? Array.Empty<Delegate>();
            FuncTable = funcTable ?? Array.Empty<Delegate>();
        }

        /// <summary>
        /// Bind delegates into funcref table elements.
        /// After FuncTable is populated, walks all tables and replaces raw funcref
        /// index Values with Values that carry the bound delegate as GcRef.
        /// This enables cross-module call_indirect: the delegate is self-contained
        /// and doesn't need the originating module's FuncTable to dispatch.
        /// </summary>
        public void BindTableDelegates()
        {
            if (FuncTable == null || FuncTable.Length == 0) return;

            foreach (var table in Tables)
            {
                // Only bind delegates in tables that hold funcrefs
                var ht = table.Type.ElementType.GetHeapType();
                if (ht != Wacs.Core.Types.Defs.HeapType.Func
                    && ht != Wacs.Core.Types.Defs.HeapType.NoFunc
                    && table.Type.ElementType != ValType.FuncRef)
                    continue;

                for (int i = 0; i < table.Elements.Count; i++)
                {
                    var elem = table.Elements[i];
                    if (elem.IsNullRef) continue;
                    // Skip elements that already have a delegate bound (from another module)
                    if (elem.GcRef is Delegate) continue;
                    // Skip non-funcref elements (GC refs, i31, etc.)
                    if (elem.Type != ValType.FuncRef) continue;

                    // Extract funcIdx from the raw funcref Value
                    int funcIdx = (int)elem.Data.Ptr;
                    if (funcIdx >= 0 && funcIdx < FuncTable.Length && FuncTable[funcIdx] != null)
                    {
                        // Store the delegate as GcRef, keeping the funcIdx for type checking
                        var bound = new Value(ValType.FuncRef, funcIdx);
                        bound.GcRef = new DelegateRef(FuncTable[funcIdx]);
                        table.Elements[i] = bound;
                    }
                }
            }

            // Also bind delegates into funcref globals so cross-module
            // global.get carries the delegate for element initializers.
            // Use reflection to bypass immutability check — we're enriching
            // the Value with a delegate, not changing the logical value.
            var globalValueField = typeof(GlobalInstance).GetField(
                "_value", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var global in Globals)
            {
                var gval = global.Value;
                if (gval.Type != ValType.FuncRef || gval.IsNullRef) continue;
                if (gval.GcRef is DelegateRef) continue;

                int funcIdx = (int)gval.Data.Ptr;
                if (funcIdx >= 0 && funcIdx < FuncTable.Length && FuncTable[funcIdx] != null)
                {
                    var bound = new Value(ValType.FuncRef, funcIdx);
                    bound.GcRef = new DelegateRef(FuncTable[funcIdx]);
                    globalValueField?.SetValue(global, bound);
                }
            }
        }

        /// <summary>
        /// Construct a ThinContext wired to the WACS framework.
        /// </summary>
        public ThinContext(
            Store store,
            ExecContext execContext,
            ModuleInstance moduleInstance)
        {
            Store = store;
            ExecContext = execContext;
            Module = moduleInstance;
            Types = moduleInstance.Types;

            // Pre-resolve module-level state from the Store
            Memories = ResolveMemories(store, moduleInstance);
            Tables = ResolveTables(store, moduleInstance);
            Globals = ResolveGlobals(store, moduleInstance);
            ImportDelegates = Array.Empty<Delegate>(); // Filled by consumer
            FuncTable = Array.Empty<Delegate>(); // Filled after transpilation
        }

        private static MemoryInstance[] ResolveMemories(Store store, ModuleInstance module)
        {
            var addrs = module.MemAddrs;
            int count = 0;
            while (addrs.Contains((MemIdx)count)) count++;
            var memories = new MemoryInstance[count];
            for (int i = 0; i < count; i++)
            {
                memories[i] = store[addrs[(MemIdx)i]];
            }
            return memories;
        }

        private static TableInstance[] ResolveTables(Store store, ModuleInstance module)
        {
            var addrs = module.TableAddrs;
            int count = 0;
            while (addrs.Contains((TableIdx)count)) count++;
            var tables = new TableInstance[count];
            for (int i = 0; i < count; i++)
            {
                tables[i] = store[addrs[(TableIdx)i]];
            }
            return tables;
        }

        private static GlobalInstance[] ResolveGlobals(Store store, ModuleInstance module)
        {
            var addrs = module.GlobalAddrs;
            int count = 0;
            while (addrs.Contains((GlobalIdx)count)) count++;
            var globals = new GlobalInstance[count];
            for (int i = 0; i < count; i++)
            {
                globals[i] = store[addrs[(GlobalIdx)i]];
            }
            return globals;
        }

        /// <summary>
        /// Populate the FuncTable with delegates wrapping transpiled methods
        /// and import delegates. Called after transpilation.
        /// </summary>
        public void PopulateFuncTable(Delegate[] localDelegates, int importCount)
        {
            FuncTable = new Delegate[importCount + localDelegates.Length];
            for (int i = 0; i < importCount; i++)
            {
                FuncTable[i] = i < ImportDelegates.Length ? ImportDelegates[i] : null!;
            }
            for (int i = 0; i < localDelegates.Length; i++)
            {
                FuncTable[importCount + i] = localDelegates[i];
            }
        }

        /// <summary>
        /// Build FuncTable from transpiled methods in mixed-mode (equivalence testing).
        /// For each transpiled function, creates a closed delegate binding this context
        /// as the first argument. Non-transpiled slots are left null.
        ///
        /// The FuncTable is indexed by Store FuncAddr (not module-local index),
        /// so that ResolveIndirect can look up delegates using the FuncAddr values
        /// stored in table elements by the interpreter.
        /// </summary>
        public void BuildFuncTable(
            System.Reflection.MethodInfo[] transpiledMethods,
            FunctionType[] functionTypes,
            int importCount)
        {
            if (Module == null || Store == null) return;

            // Find the maximum FuncAddr to size the table
            int maxAddr = 0;
            foreach (var addr in Module.FuncAddrs)
            {
                int a = (int)addr.Value;
                if (a > maxAddr) maxAddr = a;
            }
            FuncTable = new Delegate[maxAddr + 1];

            // Map each local function's Store FuncAddr to its delegate.
            // After TranspileAndSwap, functions may be TranspiledFunction or FunctionInstance.
            // We use index position relative to importCount to identify local functions.
            int funcIdx = 0;
            foreach (var addr in Module.FuncAddrs)
            {
                if (funcIdx >= importCount)
                {
                    int localIdx = funcIdx - importCount;
                    if (localIdx < transpiledMethods.Length)
                    {
                        var method = transpiledMethods[localIdx];
                        if (method != null && (importCount + localIdx) < functionTypes.Length)
                        {
                            var funcType = functionTypes[importCount + localIdx];
                            var delegateType = BuildDelegateTypeForFunc(funcType);
                            if (delegateType != null)
                            {
                                try
                                {
                                    FuncTable[(int)addr.Value] = Delegate.CreateDelegate(
                                        delegateType, this, method);
                                }
                                catch
                                {
                                    // Signature mismatch (e.g., multi-value out params) — leave null
                                }
                            }
                        }
                    }
                }
                funcIdx++;
            }
        }

        /// <summary>
        /// Build CLR delegate type for a WASM function type (without ThinContext param).
        /// </summary>
        private static Type? BuildDelegateTypeForFunc(FunctionType funcType)
        {
            var paramClrTypes = funcType.ParameterTypes.Types
                .Select(t => MapValType(t)).ToArray();
            var resultTypes = funcType.ResultType.Types;

            if (resultTypes.Length == 0)
            {
                return paramClrTypes.Length switch
                {
                    0 => typeof(Action),
                    1 => typeof(Action<>).MakeGenericType(paramClrTypes),
                    _ when paramClrTypes.Length <= 16 =>
                        Type.GetType($"System.Action`{paramClrTypes.Length}")?.MakeGenericType(paramClrTypes),
                    _ => null
                };
            }

            var returnType = MapValType(resultTypes[0]);
            var allTypes = paramClrTypes.Append(returnType).ToArray();
            return allTypes.Length switch
            {
                1 => typeof(Func<>).MakeGenericType(allTypes),
                _ when allTypes.Length <= 17 =>
                    Type.GetType($"System.Func`{allTypes.Length}")?.MakeGenericType(allTypes),
                _ => null
            };
        }

        private static Type MapValType(ValType vt) => vt switch
        {
            ValType.I32 => typeof(int),
            ValType.I64 => typeof(long),
            ValType.F32 => typeof(float),
            ValType.F64 => typeof(double),
            _ => typeof(Value)
        };
    }
}
