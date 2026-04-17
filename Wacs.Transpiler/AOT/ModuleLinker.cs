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
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Describes a module instance managed by the linker.
    /// Holds the ThinContext, transpilation result, and exported entities.
    /// </summary>
    public class LinkedModule
    {
        public string Name { get; }
        public ThinContext Context { get; }
        public TranspilationResult Result { get; }

        /// <summary>
        /// Exported functions as typed delegates, keyed by export name.
        /// </summary>
        public Dictionary<string, Delegate> ExportedFunctions { get; } = new();

        /// <summary>
        /// Exported memories, keyed by export name.
        /// References into Context.Memories.
        /// </summary>
        public Dictionary<string, (int memIdx, MemoryInstance mem)> ExportedMemories { get; } = new();

        /// <summary>
        /// Exported tables, keyed by export name.
        /// References into Context.Tables.
        /// </summary>
        public Dictionary<string, (int tableIdx, TableInstance table)> ExportedTables { get; } = new();

        /// <summary>
        /// Exported globals, keyed by export name.
        /// References into Context.Globals.
        /// </summary>
        public Dictionary<string, (int globalIdx, GlobalInstance global)> ExportedGlobals { get; } = new();

        public LinkedModule(string name, ThinContext context, TranspilationResult result)
        {
            Name = name;
            Context = context;
            Result = result;
        }
    }

    /// <summary>
    /// Factory that manages ThinContext creation and cross-module wiring
    /// for transpiled WASM assemblies.
    ///
    /// Implements the WASM linking/instantiation pattern:
    ///   1. Register host modules (spectest, WASI, etc.)
    ///   2. Transpile and instantiate modules in dependency order
    ///   3. Resolve imports from previously registered modules
    ///   4. Share table/memory/global instances across modules
    ///
    /// Each instantiated module gets a ThinContext with:
    ///   - Imported memories/tables/globals: shared references from provider modules
    ///   - Local memories/tables/globals: newly allocated
    ///   - Import delegates: resolved from provider modules' exports
    ///   - FuncTable: populated with all function delegates
    ///
    /// Usage:
    ///   var linker = new ModuleLinker();
    ///   linker.RegisterHostFunctions("spectest", hostFuncs);
    ///   var moduleA = linker.Instantiate(resultA, "moduleA");
    ///   var moduleB = linker.Instantiate(resultB, "moduleB"); // imports from moduleA
    /// </summary>
    public class ModuleLinker
    {
        private readonly Dictionary<string, LinkedModule> _modules = new();

        /// <summary>
        /// Host function registry: (moduleName, fieldName) → delegate.
        /// Registered before any WASM modules are instantiated.
        /// </summary>
        private readonly Dictionary<(string module, string field), Delegate> _hostFunctions = new();

        /// <summary>
        /// Host global registry: (moduleName, fieldName) → GlobalInstance.
        /// </summary>
        private readonly Dictionary<(string module, string field), GlobalInstance> _hostGlobals = new();

        /// <summary>
        /// Host memory registry: (moduleName, fieldName) → byte[].
        /// </summary>
        private readonly Dictionary<(string module, string field), MemoryInstance> _hostMemories = new();

        /// <summary>
        /// Host table registry: (moduleName, fieldName) → TableInstance.
        /// </summary>
        private readonly Dictionary<(string module, string field), TableInstance> _hostTables = new();


        /// <summary>
        /// Register a host function (e.g., spectest.print_i32).
        /// </summary>
        public void RegisterHostFunction(string moduleName, string fieldName, Delegate func)
        {
            _hostFunctions[(moduleName, fieldName)] = func;
        }

        /// <summary>
        /// Register a host global.
        /// </summary>
        public void RegisterHostGlobal(string moduleName, string fieldName, GlobalInstance global)
        {
            _hostGlobals[(moduleName, fieldName)] = global;
        }

        /// <summary>
        /// Register a host memory.
        /// </summary>
        public void RegisterHostMemory(string moduleName, string fieldName, MemoryInstance memory)
        {
            _hostMemories[(moduleName, fieldName)] = memory;
        }

        /// <summary>
        /// Register a host table.
        /// </summary>
        public void RegisterHostTable(string moduleName, string fieldName, TableInstance table)
        {
            _hostTables[(moduleName, fieldName)] = table;
        }

        /// <summary>
        /// Get a previously instantiated module by name.
        /// </summary>
        public LinkedModule? GetModule(string name)
        {
            return _modules.TryGetValue(name, out var m) ? m : null;
        }

        /// <summary>
        /// Register a module instance after it has been constructed.
        /// The Module constructor handles its own initialization via
        /// InitializationHelper.Initialize — the linker does NOT call
        /// Initialize, avoiding double-init corruption of data segments.
        ///
        /// The ThinContext is extracted from the Module instance and used
        /// to track exported entities for cross-module import resolution.
        /// </summary>
        public LinkedModule Register(
            string name,
            ThinContext ctx,
            TranspilationResult result,
            Wacs.Core.Module wasmModule)
        {
            var linked = new LinkedModule(name, ctx, result);
            PopulateExports(linked, wasmModule);
            _modules[name] = linked;
            return linked;
        }

        /// <summary>
        /// Resolve imported memories/tables/globals from previously registered
        /// modules and host registries. Call AFTER the Module constructor has
        /// created the ThinContext, then patch shared instances into it.
        ///
        /// After patching, re-applies element segments that target imported
        /// tables so they write to the shared table instance (not the placeholder
        /// that was initialized during the constructor).
        /// </summary>
        public void ResolveImports(ThinContext ctx, Wacs.Core.Module wasmModule,
            int initDataId = -1)
        {
            int memImportIdx = 0;
            int tableImportIdx = 0;
            int globalImportIdx = 0;
            var patchedTableIndices = new HashSet<int>();
            var patchedMemoryIndices = new HashSet<int>();

            foreach (var import in wasmModule.Imports)
            {
                switch (import.Desc)
                {
                    case Wacs.Core.Module.ImportDesc.MemDesc md:
                    {
                        if (TryResolveMemory(import.ModuleName, import.Name, out var mem))
                        {
                            // Share the same MemoryInstance — growth propagates automatically
                            ctx.Memories[memImportIdx] = mem;
                            patchedMemoryIndices.Add(memImportIdx);
                        }
                        memImportIdx++;
                        break;
                    }
                    case Wacs.Core.Module.ImportDesc.TableDesc:
                    {
                        if (TryResolveTable(import.ModuleName, import.Name, out var table))
                        {
                            ctx.Tables[tableImportIdx] = table;
                            patchedTableIndices.Add(tableImportIdx);
                        }
                        tableImportIdx++;
                        break;
                    }
                    case Wacs.Core.Module.ImportDesc.GlobalDesc:
                    {
                        if (TryResolveGlobal(import.ModuleName, import.Name, out var global))
                            ctx.Globals[globalImportIdx] = global;
                        globalImportIdx++;
                        break;
                    }
                }
            }

            // Re-evaluate table default expressions that depend on imported globals.
            // During Initialize(), imported globals weren't resolved yet, so table
            // defaults using global.get on imports evaluated to the wrong value.
            // Re-evaluate with resolved globals and replace only slots that still
            // have the old (wrong) default, preserving active element segments.
            if (initDataId >= 0)
            {
                var initData = InitRegistry.Get(initDataId);
                for (int i = 0; i < initData.Tables.Length; i++)
                {
                    var (_, _, _, initExpr) = initData.Tables[i];
                    if (initExpr == null) continue;

                    // Compute the new default with resolved globals
                    var newDefault = InitializationHelper.EvaluateTableDefault(
                        initExpr, ctx.Globals);

                    // Compute what Initialize() produced with unresolved globals
                    var oldDefault = InitializationHelper.EvaluateTableDefault(
                        initExpr, initData.Globals.Length > 0
                            ? BuildLocalGlobals(initData) : System.Array.Empty<GlobalInstance>());

                    // If the defaults differ, an imported global changed the result
                    if (newDefault.Data.Ptr != oldDefault.Data.Ptr || newDefault.Type != oldDefault.Type)
                    {
                        var table = ctx.Tables[i];
                        for (int j = 0; j < table.Elements.Count; j++)
                        {
                            var elem = table.Elements[j];
                            if (elem.Type == oldDefault.Type && elem.Data.Ptr == oldDefault.Data.Ptr)
                                table.Elements[j] = newDefault;
                        }
                    }
                }
            }

            // Re-apply data segments that target imported (now shared) memories.
            // Uses saved data bytes since active segments were dropped after initialization.
            if (patchedMemoryIndices.Count > 0 && initDataId >= 0)
            {
                var initData = InitRegistry.Get(initDataId);
                foreach (var (memIdx, offset, segId) in initData.ActiveDataSegments)
                {
                    if (!patchedMemoryIndices.Contains(memIdx)) continue;
                    if (initData.SavedDataSegments.TryGetValue(segId, out var segData))
                    {
                        var memory = ctx.Memories[memIdx].Data;
                        if (offset + segData.Length <= memory.Length)
                            Buffer.BlockCopy(segData, 0, memory, offset, segData.Length);
                    }
                }
            }

            // Re-apply element segments that target imported (now shared) tables.
            // The Module constructor's Initialize() wrote these to placeholder tables;
            // now that we've patched the real shared tables in, re-apply them.
            // Store bound delegates directly so cross-module call_indirect works.
            if (patchedTableIndices.Count > 0 && initDataId >= 0)
            {
                var initData = InitRegistry.Get(initDataId);
                foreach (var (tableIdx, elemOffset, funcIndices) in initData.ActiveElementSegments)
                {
                    if (!patchedTableIndices.Contains(tableIdx)) continue;
                    var table = ctx.Tables[tableIdx];
                    for (int j = 0; j < funcIndices.Length; j++)
                    {
                        if (elemOffset + j < table.Elements.Count && funcIndices[j] >= 0)
                        {
                            int fi = funcIndices[j];
                            var val = new Value(ValType.FuncRef, fi);
                            // Bind the delegate directly for cross-module dispatch
                            if (fi < ctx.FuncTable.Length && ctx.FuncTable[fi] != null)
                                val.GcRef = new DelegateRef(ctx.FuncTable[fi]);
                            table.Elements[elemOffset + j] = val;
                        }
                    }
                }
            }

            // Deferred element evaluation: re-resolve entries that used global.get
            // on imported globals (which were null at transpile time, now patched).
            if (initDataId >= 0)
            {
                var initData = InitRegistry.Get(initDataId);
                foreach (var (elemSegIdx, slotIdx, globalIdx) in initData.DeferredElemGlobals)
                {
                    if (elemSegIdx >= initData.ActiveElementSegments.Length) continue;
                    if (globalIdx >= ctx.Globals.Length) continue;

                    var globalVal = ctx.Globals[globalIdx].Value;
                    if (globalVal.IsNullRef) continue;

                    // Extract funcref from the global's value
                    int funcIdx = (int)globalVal.Data.Ptr;

                    var (tableIdx, elemOffset, _) = initData.ActiveElementSegments[elemSegIdx];
                    int tableSlot = elemOffset + slotIdx;
                    if (tableIdx >= ctx.Tables.Length) continue;
                    var table = ctx.Tables[tableIdx];
                    if (tableSlot >= table.Elements.Count) continue;

                    // Check if the global carries a delegate (from another module's BindTableDelegates)
                    var val = new Value(ValType.FuncRef, funcIdx);
                    if (globalVal.GcRef is DelegateRef dref)
                    {
                        val.GcRef = dref;
                    }
                    else if (funcIdx >= 0 && funcIdx < ctx.FuncTable.Length && ctx.FuncTable[funcIdx] != null)
                    {
                        val.GcRef = new DelegateRef(ctx.FuncTable[funcIdx]);
                    }
                    table.Elements[tableSlot] = val;
                }
            }

            // Deferred global initialization: re-evaluate globals whose initializers
            // used global.get on imported globals (which had default values at transpile time).
            if (initDataId >= 0)
            {
                var initData2 = InitRegistry.Get(initDataId);
                var globalValueField = typeof(GlobalInstance).GetField(
                    "_value", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var (globalIdx, initializer) in initData2.DeferredGlobalInits)
                {
                    if (globalIdx >= ctx.Globals.Length) continue;
                    // Re-evaluate the initializer expression with access to resolved globals
                    var val = EvaluateInitExpression(initializer, ctx.Globals);
                    globalValueField?.SetValue(ctx.Globals[globalIdx], val);
                }

                // Re-evaluate data segment offsets that depend on globals,
                // then re-copy the data to the correct memory locations.
                foreach (var (dataSegIdx, offsetExpr) in initData2.DeferredDataOffsets)
                {
                    if (dataSegIdx >= initData2.ActiveDataSegments.Length) continue;
                    var (memIdx, _, segId) = initData2.ActiveDataSegments[dataSegIdx];
                    int newOffset = EvaluateInitExpressionI32(offsetExpr, ctx.Globals);
                    // Copy using saved data (original may be dropped)
                    if (initData2.SavedDataSegments.TryGetValue(segId, out var segData))
                    {
                        if (memIdx < ctx.Memories.Length)
                        {
                            var memory = ctx.Memories[memIdx].Data;
                            if (newOffset + segData.Length <= memory.Length)
                                Buffer.BlockCopy(segData, 0, memory, newOffset, segData.Length);
                        }
                    }
                    else
                    {
                        // Try from ModuleInit (segment not yet dropped)
                        var segData2 = ModuleInit.GetDataSegmentData(segId);
                        if (segData2 != null && memIdx < ctx.Memories.Length)
                        {
                            var memory = ctx.Memories[memIdx].Data;
                            if (newOffset + segData2.Length <= memory.Length)
                                Buffer.BlockCopy(segData2, 0, memory, newOffset, segData2.Length);
                        }
                    }
                }

                // Table default re-evaluation is handled earlier in ResolveImports
                // (after global import resolution, before element segment re-apply).
            }
        }

        /// <summary>
        /// Evaluate a constant initializer expression against resolved globals.
        /// Used for deferred global initialization after import patching.
        /// </summary>
        private static Value EvaluateInitExpression(
            Wacs.Core.Types.Expression expr, GlobalInstance[] globals)
        {
            var stack = new Stack<Value>();
            foreach (var inst in expr.Instructions)
            {
                if (inst is Wacs.Core.Instructions.Numeric.InstI32Const i32)
                    { stack.Push(new Value(i32.Value)); continue; }
                if (inst is Wacs.Core.Instructions.Numeric.InstI64Const i64)
                    { stack.Push(new Value(i64.FetchImmediate(null!))); continue; }
                if (inst is Wacs.Core.Instructions.Numeric.InstF32Const f32)
                    { stack.Push(new Value(f32.FetchImmediate(null!))); continue; }
                if (inst is Wacs.Core.Instructions.Numeric.InstF64Const f64)
                    { stack.Push(new Value(f64.FetchImmediate(null!))); continue; }
                if (inst is Wacs.Core.Instructions.InstGlobalGet gg)
                {
                    int idx = gg.GetIndex();
                    if (idx >= 0 && idx < globals.Length)
                        stack.Push(globals[idx].Value);
                    continue;
                }
                if (inst is Wacs.Core.Instructions.Reference.InstRefNull rn)
                    { stack.Push(new Value(rn.RefType)); continue; }
                if (inst is Wacs.Core.Instructions.Reference.InstRefFunc rf)
                    { stack.Push(new Value(Wacs.Core.Types.Defs.ValType.FuncRef, (int)rf.FunctionIndex.Value)); continue; }
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
            return stack.Count > 0 ? stack.Pop() : default;
        }


        private static int EvaluateInitExpressionI32(
            Wacs.Core.Types.Expression expr, GlobalInstance[] globals)
        {
            var val = EvaluateInitExpression(expr, globals);
            return val.Data.Int32;
        }

        private bool TryResolveMemory(string moduleName, string fieldName, out MemoryInstance memory)
        {
            if (_hostMemories.TryGetValue((moduleName, fieldName), out memory!))
                return true;
            if (_modules.TryGetValue(moduleName, out var mod) &&
                mod.ExportedMemories.TryGetValue(fieldName, out var exported))
            {
                memory = exported.mem;
                return true;
            }
            memory = null!;
            return false;
        }

        private bool TryResolveTable(string moduleName, string fieldName, out TableInstance table)
        {
            if (_hostTables.TryGetValue((moduleName, fieldName), out table!))
                return true;
            if (_modules.TryGetValue(moduleName, out var mod) &&
                mod.ExportedTables.TryGetValue(fieldName, out var exported))
            {
                table = exported.table;
                return true;
            }
            table = null!;
            return false;
        }

        private bool TryResolveGlobal(string moduleName, string fieldName, out GlobalInstance global)
        {
            if (_hostGlobals.TryGetValue((moduleName, fieldName), out global!))
                return true;
            if (_modules.TryGetValue(moduleName, out var mod) &&
                mod.ExportedGlobals.TryGetValue(fieldName, out var exported))
            {
                global = exported.global;
                return true;
            }
            global = null!;
            return false;
        }

        /// <summary>
        /// Populate a LinkedModule's export maps from the WASM module's export section.
        /// </summary>
        private static void PopulateExports(LinkedModule linked, Wacs.Core.Module wasmModule)
        {
            foreach (var export in wasmModule.Exports)
            {
                switch (export.Desc)
                {
                    case Wacs.Core.Module.ExportDesc.MemDesc md:
                    {
                        int idx = (int)md.MemoryIndex.Value;
                        if (idx < linked.Context.Memories.Length)
                            linked.ExportedMemories[export.Name] = (idx, linked.Context.Memories[idx]);
                        break;
                    }
                    case Wacs.Core.Module.ExportDesc.TableDesc td:
                    {
                        int idx = (int)td.TableIndex.Value;
                        if (idx < linked.Context.Tables.Length)
                            linked.ExportedTables[export.Name] = (idx, linked.Context.Tables[idx]);
                        break;
                    }
                    case Wacs.Core.Module.ExportDesc.GlobalDesc gd:
                    {
                        int idx = (int)gd.GlobalIndex.Value;
                        if (idx < linked.Context.Globals.Length)
                            linked.ExportedGlobals[export.Name] = (idx, linked.Context.Globals[idx]);
                        break;
                    }
                    case Wacs.Core.Module.ExportDesc.FuncDesc:
                    {
                        // Function exports are accessible via the Module class's IExports
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Build the local-only globals array (without imports) to reconstruct
        /// what Initialize() produced before import resolution.
        /// </summary>
        private static GlobalInstance[] BuildLocalGlobals(ModuleInitData initData)
        {
            var globals = new GlobalInstance[initData.Globals.Length];
            for (int i = 0; i < initData.Globals.Length; i++)
            {
                var (type, mut, init) = initData.Globals[i];
                globals[i] = new GlobalInstance(new GlobalType(type, mut), init);
            }
            return globals;
        }
    }
}
