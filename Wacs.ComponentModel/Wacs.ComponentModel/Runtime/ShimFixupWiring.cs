// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.IO;
using Wacs.ComponentModel.Async;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>
    /// Minimal extension to the legacy multi-core path:
    /// instantiate the wit-component shim + fixup modules and
    /// fill the shim's funcref table with the host's bindings
    /// so the primary's call_indirect-through-shim reaches the
    /// right implementations.
    ///
    /// <para>The legacy path is left intact for everything
    /// else (canon-async binding via CanonAsyncBinder /
    /// WitBindgenScaffoldingBinder / configureImports). This
    /// helper only adds what's strictly needed for the
    /// funcref-table dance to work end-to-end, sidestepping
    /// the larger spec-following rewrite explored under
    /// MultiCoreInstantiator.</para>
    /// </summary>
    internal static class ShimFixupWiring
    {
        /// <summary>
        /// Wire shim + fixup. Call AFTER configureImports has
        /// run (so host bindings are in place) and BEFORE the
        /// primary core module is instantiated (so the shim's
        /// table + fixup's element segment land before
        /// primary's call_indirect can fire).
        ///
        /// <para>No-op when the component doesn't have the
        /// wit-component shim+fixup pattern — leaves the
        /// existing path's behavior alone.</para>
        /// </summary>
        private static readonly bool TraceEnabled =
            System.Environment.GetEnvironmentVariable(
                "WACS_TRACE_SHIM_FIXUP") == "1";

        private static void Trace(string msg)
        {
            if (TraceEnabled)
                System.Console.Error.WriteLine($"[shim-fixup] {msg}");
        }

        public static void Wire(
            WasmRuntime runtime,
            ComponentModule component,
            IReadOnlyList<byte[]> coreBinaries,
            int primaryIdx)
        {
            Trace($"Wire start: {coreBinaries.Count} binaries, primary={primaryIdx}");
            // 1. Identify shim + fixup core-module indices via
            //    structural shape. The shim defines a funcref
            //    table named "$imports" and exports it; the
            //    fixup imports that same table by name plus a
            //    slate of integer-named function imports from
            //    the empty module.
            int? shimModuleIdx = null;
            int? fixupModuleIdx = null;
            for (int i = 0; i < coreBinaries.Count; i++)
            {
                if (i == primaryIdx) continue;
                using var ms = new MemoryStream(coreBinaries[i]);
                var module = BinaryModuleParser.ParseWasm(ms);
                bool isFixup = IsFixupModule(module);
                bool isShim = IsShimByStructure(module);
                Trace($"  binary {i}: Name={module.Name ?? "(null)"}, " +
                    $"IsFixup={isFixup}, IsShim={isShim}, " +
                    $"imports={module.Imports.Length}, " +
                    $"exports={module.Exports.Length}");
                if (isFixup)
                    fixupModuleIdx = i;
                else if (isShim)
                    shimModuleIdx = i;
            }
            Trace($"shim={shimModuleIdx}, fixup={fixupModuleIdx}");
            if (shimModuleIdx == null || fixupModuleIdx == null)
                return; // not the wit-component pattern

            // 2. Find the core-instance index that instantiates
            //    the shim — the shim's exports live there in
            //    the core-instance space, and component aliases
            //    target it by (instanceIdx, "<slot>").
            int? shimInstanceIdx = null;
            for (int i = 0; i < component.CoreInstances.Count; i++)
            {
                if (component.CoreInstances[i] is InstantiateCoreModule ic
                    && ic.ModuleIdx == shimModuleIdx.Value)
                {
                    shimInstanceIdx = i;
                    break;
                }
            }
            Trace($"shimInstanceIdx={shimInstanceIdx}");
            if (shimInstanceIdx == null) return;

            // 3. Walk component sections in file order to build
            //    the parallel index spaces we need:
            //    coreFuncIdx → slot (from shim aliases) and
            //    coreInstance index → list of (slot, methodName)
            //    pairs (from inline-export bundles using slot-
            //    sourced core-funcs).
            var slotByCoreFuncIdx = new Dictionary<uint, uint>();
            var slotMethodsByInstance =
                new Dictionary<int, List<(uint Slot, string Method)>>();
            // Track slot → (MOD, METHOD) — built when the
            // primary's InstantiateCoreModule shows up.
            var slotToImport = new Dictionary<uint, (string Mod, string Method)>();

            uint coreFuncIdx = 0;
            int coreInstIdx = 0;

            foreach (var raw in component.RawSections)
            {
                switch (raw.Id)
                {
                    case ComponentSectionId.Alias:
                        foreach (var entry in AliasSectionReader.Decode(raw.Payload))
                            ProcessAlias(entry,
                                shimInstanceIdx.Value,
                                ref coreFuncIdx,
                                slotByCoreFuncIdx);
                        break;
                    case ComponentSectionId.Canon:
                        foreach (var entry in CanonSectionReader.Decode(raw.Payload))
                            ProcessCanon(entry, ref coreFuncIdx);
                        break;
                    case ComponentSectionId.CoreInstance:
                        foreach (var entry in CoreInstanceSectionReader.Decode(raw.Payload))
                        {
                            ProcessCoreInstance(entry,
                                primaryIdx,
                                coreInstIdx,
                                slotByCoreFuncIdx,
                                slotMethodsByInstance,
                                slotToImport);
                            coreInstIdx++;
                        }
                        break;
                }
            }

            Trace($"slotToImport.Count={slotToImport.Count}");
            // 4. For each slot we resolved to a host import,
            //    bind ("", "<slot>") to the same host binding
            //    so the fixup's element segment puts the right
            //    function in the table.
            int bound = 0, missed = 0;
            foreach (var kv in slotToImport)
            {
                var (mod, method) = kv.Value;
                if (!runtime.TryGetExportedFunction((mod, method), out var addr))
                {
                    Trace($"  slot {kv.Key}: NO host binding " +
                        $"({mod}, {method})");
                    missed++;
                    continue;
                }
                var fi = runtime.GetFunction(addr);
                runtime.BindHostFunction(
                    (string.Empty, kv.Key.ToString()), fi);
                bound++;
                Trace($"  slot {kv.Key}: bound ({mod}, {method})");
            }
            Trace($"Slot binding: bound={bound}, missed={missed}");

            // 5. Instantiate the shim — defines the funcref
            //    table + the indirect-XXX functions that
            //    call_indirect through it.
            Trace("Instantiating shim...");
            using var shimMs = new MemoryStream(coreBinaries[shimModuleIdx.Value]);
            var shimModule = BinaryModuleParser.ParseWasm(shimMs);
            var shimInstance = runtime.InstantiateModule(shimModule,
                new RuntimeOptions
                {
                    MemoryStorage = AmbientRuntime.MemoryStorage,
                });
            Trace($"  shim exports: {shimInstance.Exports.Count}");

            // 6. Expose shim's funcref table at ("", "$imports")
            //    so fixup's table import resolves to it.
            foreach (var export in shimInstance.Exports)
            {
                if (export.Name == "$imports"
                    && export.Value is ExternalValue.Table)
                {
                    runtime.BindExternal(
                        (string.Empty, "$imports"), export.Value);
                    break;
                }
            }

            // 7. Instantiate the fixup — its element segment
            //    populates the funcref table from the
            //    ("", "<slot>") host functions we bound in
            //    step 4. After this returns the shim's
            //    call_indirect goes through to the right host
            //    implementations.
            Trace("Instantiating fixup...");
            using var fixupMs = new MemoryStream(coreBinaries[fixupModuleIdx.Value]);
            var fixupModule = BinaryModuleParser.ParseWasm(fixupMs);
            runtime.InstantiateModule(fixupModule,
                new RuntimeOptions
                {
                    MemoryStorage = AmbientRuntime.MemoryStorage,
                });
            Trace("Wire complete");
        }

        private static void ProcessAlias(
            ComponentAliasEntry entry,
            int shimInstanceIdx,
            ref uint coreFuncIdx,
            Dictionary<uint, uint> slotByCoreFuncIdx)
        {
            if (entry.Sort != AliasSort.CoreSort
                || entry.CoreKind != CoreAliasKind.Func)
                return;
            // Only core-func aliases bump the core-func space.
            if (entry.TargetKind == AliasTargetKind.CoreInstanceExport
                && entry.InstanceIdx == (uint)shimInstanceIdx
                && entry.ExportName != null
                && uint.TryParse(entry.ExportName, out var slot))
            {
                slotByCoreFuncIdx[coreFuncIdx] = slot;
            }
            coreFuncIdx++;
        }

        private static void ProcessCanon(
            CanonEntry entry, ref uint coreFuncIdx)
        {
            // Lift produces a component-level func — doesn't
            // bump the core-func counter. Every other canon
            // form does.
            if (entry is not CanonLift)
                coreFuncIdx++;
        }

        private static void ProcessCoreInstance(
            CoreInstanceEntry entry,
            int primaryIdx,
            int coreInstIdx,
            Dictionary<uint, uint> slotByCoreFuncIdx,
            Dictionary<int, List<(uint Slot, string Method)>>
                slotMethodsByInstance,
            Dictionary<uint, (string Mod, string Method)> slotToImport)
        {
            switch (entry)
            {
                case InstantiateCoreInline inline:
                {
                    var slotMethods = new List<(uint, string)>();
                    foreach (var exp in inline.Exports)
                    {
                        if (exp.Sort == CoreSort.Func
                            && slotByCoreFuncIdx.TryGetValue(exp.Index, out var slot))
                        {
                            slotMethods.Add((slot, exp.Name));
                        }
                    }
                    if (slotMethods.Count > 0)
                        slotMethodsByInstance[coreInstIdx] = slotMethods;
                    return;
                }
                case InstantiateCoreModule ic
                    when (int)ic.ModuleIdx == primaryIdx:
                {
                    foreach (var arg in ic.Args)
                    {
                        if (slotMethodsByInstance.TryGetValue(
                                (int)arg.InstanceIdx, out var methods))
                        {
                            foreach (var (slot, method) in methods)
                                slotToImport[slot] = (arg.Name, method);
                        }
                    }
                    return;
                }
            }
        }

        // Structural identification — wit-component 0.246+
        // doesn't emit the shim's internal module-name section
        // anymore (only the COMPONENT-level core-module-name
        // section names it). Both shim + fixup are uniquely
        // identifiable by their "$imports" funcref table:
        // shim EXPORTS it (defines), fixup IMPORTS it. The
        // shape is wit-component-specific and false-positives
        // are negligible.

        private static bool IsShimByStructure(Module module)
        {
            if (module?.Exports == null) return false;
            foreach (var exp in module.Exports)
            {
                if (exp.Name != "$imports") continue;
                if (exp.Desc is Module.ExportDesc.TableDesc) return true;
            }
            return false;
        }

        private static bool IsFixupModule(Module module)
        {
            if (module?.Imports == null) return false;
            foreach (var imp in module.Imports)
            {
                if (imp.ModuleName != string.Empty) continue;
                if (imp.Name != "$imports") continue;
                if (imp.Desc is Module.ImportDesc.TableDesc) return true;
            }
            return false;
        }
    }
}
