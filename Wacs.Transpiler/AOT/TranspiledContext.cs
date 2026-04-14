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
        // Indexed by funcidx (module-local index space, imports first).
        // For intra-module transpiled calls, the IL uses direct call instructions.
        // For cross-module and host calls, dispatch goes through this table.
        public IFunctionInstance[] Functions;

        // === Runtime services (nullable for standalone mode) ===
        // When running inside the WACS framework, these enable host function
        // interop, GC operations, and type checks. When standalone, they are null.
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
            IFunctionInstance[]? functions = null)
        {
            Memories = memories ?? Array.Empty<byte[]>();
            Tables = tables ?? Array.Empty<TableInstance>();
            Globals = globals ?? Array.Empty<GlobalInstance>();
            Functions = functions ?? Array.Empty<IFunctionInstance>();
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
            Functions = ResolveFunctions(store, moduleInstance);
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

        private static IFunctionInstance[] ResolveFunctions(Store store, ModuleInstance module)
        {
            return module.FuncAddrs.Select(addr => store[addr]).ToArray();
        }
    }
}
