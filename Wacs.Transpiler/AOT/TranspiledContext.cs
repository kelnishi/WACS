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

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Lean runtime context passed as the first parameter to every transpiled function.
    /// Holds pre-resolved module-level state for fast access without carrying
    /// interpreter-specific overhead (OpStack, InstructionPointer, Frame, etc.).
    ///
    /// When running standalone (without WasmRuntime), consumers construct this directly.
    /// When running inside the WACS framework, LoadTranspiledModule populates it from the Store.
    /// </summary>
    public class TranspiledContext
    {
        // === Linear Memory ===
        // Indexed by memidx. Most modules use only Memories[0].
        // memory.grow changes the byte[] reference, so transpiled code must
        // reload after any call that might trigger growth.
        public byte[][] Memories;

        // === Tables ===
        // Indexed by tableidx. Elements are function references for call_indirect.
        public TableInstance[] Tables;

        // === Globals ===
        // Indexed by globalidx. Mutable globals are written through GlobalInstance.Value.
        public GlobalInstance[] Globals;

        // === Function dispatch ===
        // ImportDelegates: typed delegates for imported functions.
        // Filled by the implementor at load time. Each delegate's signature matches
        // the WASM import's function type (no TranspiledContext parameter).
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
        /// Construct a TranspiledContext for standalone use (no WasmRuntime).
        /// </summary>
        public TranspiledContext(
            byte[][]? memories = null,
            TableInstance[]? tables = null,
            GlobalInstance[]? globals = null,
            Delegate[]? importDelegates = null,
            Delegate[]? funcTable = null)
        {
            Memories = memories ?? Array.Empty<byte[]>();
            Tables = tables ?? Array.Empty<TableInstance>();
            Globals = globals ?? Array.Empty<GlobalInstance>();
            ImportDelegates = importDelegates ?? Array.Empty<Delegate>();
            FuncTable = funcTable ?? Array.Empty<Delegate>();
        }

        /// <summary>
        /// Construct a TranspiledContext wired to the WACS framework.
        /// </summary>
        public TranspiledContext(
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

        /// <summary>
        /// Reload memory references after a potential memory.grow.
        /// </summary>
        public void RefreshMemories()
        {
            if (Store != null && Module != null)
            {
                Memories = ResolveMemories(Store, Module);
            }
        }

        private static byte[][] ResolveMemories(Store store, ModuleInstance module)
        {
            var addrs = module.MemAddrs;
            int count = 0;
            // MemAddrs uses array-backed storage, count by probing Contains
            while (addrs.Contains((MemIdx)count)) count++;
            var memories = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                memories[i] = store[addrs[(MemIdx)i]].Data;
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
    }
}
