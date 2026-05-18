// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// Wires the canonical-ABI resource handle-table intrinsics
    /// into a <see cref="WasmRuntime"/> before core-module
    /// instantiation. Used by both the harness's
    /// <c>HarnessLoader.Load</c> and the interpreter's
    /// <c>ComponentInstance.Instantiate</c> — they share this
    /// implementation so the same binding semantics fire on
    /// either path.
    ///
    /// <para>Algorithm: walk the core module's imports for the
    /// <c>("[export]&lt;iface&gt;", "[resource-X]&lt;name&gt;")</c>
    /// pattern. For each resource, allocate a
    /// <see cref="ResourceHandleTable"/> and bind three adapter
    /// functions over it (new / drop / rep). The drop adapter
    /// captures a pending-dtor slot that
    /// <see cref="ResolveDtorTrampolines"/> fills in after the
    /// core module finishes instantiating (the dtor isn't
    /// exported until then).</para>
    /// </summary>
    public static class CanonResourceBinder
    {
        private const string ExportPrefix = "[export]";
        private const string ResourceNewPrefix = "[resource-new]";
        private const string ResourceDropPrefix = "[resource-drop]";
        private const string ResourceRepPrefix = "[resource-rep]";

        /// <summary>
        /// Captured (interface base, resource name) pair for a
        /// pending dtor lookup. The drop adapter calls
        /// <see cref="Dtor"/> after dropping a slot;
        /// <see cref="ResolveDtorTrampolines"/> fills the field
        /// in once the core module's <c>&lt;iface&gt;#[dtor]&lt;name&gt;</c>
        /// export becomes addressable.
        /// </summary>
        public sealed class PendingDtor
        {
            public string IfaceBase = "";
            public string ResourceName = "";
            public ResourceHandleTable Table = null!;
            public Action<int>? Dtor;
        }

        /// <summary>
        /// Bind canon-resource adapters for every exported resource
        /// the core module imports. Call BEFORE
        /// <c>runtime.InstantiateModule(coreModule)</c> so the
        /// inner module's import resolution finds them.
        /// </summary>
        public static (Dictionary<string, ResourceHandleTable> tables,
                       List<PendingDtor> dtorBindings)
            BindImports(WasmRuntime runtime, Module coreModule)
        {
            var tables = new Dictionary<string, ResourceHandleTable>();
            var dtorBindings = new List<PendingDtor>();

            foreach (var import in coreModule.Imports)
            {
                if (!import.ModuleName.StartsWith(ExportPrefix, StringComparison.Ordinal))
                    continue;
                var ifaceBase = import.ModuleName.Substring(ExportPrefix.Length);

                if (import.Name.StartsWith(ResourceNewPrefix, StringComparison.Ordinal))
                {
                    var resourceName = import.Name.Substring(ResourceNewPrefix.Length);
                    var table = GetOrCreate(tables, resourceName);
                    runtime.BindHostFunction(
                        (import.ModuleName, import.Name),
                        (Func<ExecContext, int, int>)((_, rep) => table.New(rep)));
                }
                else if (import.Name.StartsWith(ResourceDropPrefix, StringComparison.Ordinal))
                {
                    var resourceName = import.Name.Substring(ResourceDropPrefix.Length);
                    var table = GetOrCreate(tables, resourceName);
                    var pending = new PendingDtor
                    {
                        IfaceBase = ifaceBase,
                        ResourceName = resourceName,
                        Table = table,
                    };
                    dtorBindings.Add(pending);
                    // v1 caveat: the adapter just removes the
                    // table slot; the dtor invocation is deferred
                    // to a separate top-level call (the harness's
                    // Dispose runs the dtor BEFORE calling
                    // [resource-drop] to avoid re-entrant
                    // wasm-from-host-from-wasm dispatch, which
                    // confuses WACS's frame-stack bookkeeping).
                    // A v2 runtime fix could relax this and let
                    // the adapter invoke the dtor inline.
                    runtime.BindHostFunction(
                        (import.ModuleName, import.Name),
                        (Action<ExecContext, int>)((_, h) => pending.Table.Drop(h)));
                }
                else if (import.Name.StartsWith(ResourceRepPrefix, StringComparison.Ordinal))
                {
                    var resourceName = import.Name.Substring(ResourceRepPrefix.Length);
                    var table = GetOrCreate(tables, resourceName);
                    runtime.BindHostFunction(
                        (import.ModuleName, import.Name),
                        (Func<ExecContext, int, int>)((_, h) => table.Rep(h)));
                }
            }

            return (tables, dtorBindings);
        }

        /// <summary>
        /// After <c>runtime.InstantiateModule</c> finishes, look
        /// up each pending resource's dtor on the freshly-built
        /// core instance and wire it into the drop adapter's
        /// late-bound slot. If the dtor isn't exported, drop
        /// just removes the table entry (the underlying guest
        /// resource leaks until the component instance is
        /// disposed).
        /// </summary>
        public static void ResolveDtorTrampolines(
            WasmRuntime runtime, ModuleInstance coreInstance,
            List<PendingDtor> dtorBindings)
        {
            foreach (var pb in dtorBindings)
            {
                var dtorExportName = pb.IfaceBase + "#[dtor]" + pb.ResourceName;
                FuncAddr? addr = null;
                foreach (var export in coreInstance.Exports)
                {
                    if (export.Name == dtorExportName
                        && export.Value is ExternalValue.Function f)
                    {
                        addr = f.Address;
                        break;
                    }
                }
                if (addr != null)
                    pb.Dtor = runtime.CreateInvokerAction<int>(addr.Value);
            }
        }

        private static ResourceHandleTable GetOrCreate(
            Dictionary<string, ResourceHandleTable> tables, string resourceName)
        {
            if (!tables.TryGetValue(resourceName, out var t))
            {
                t = new ResourceHandleTable();
                tables[resourceName] = t;
            }
            return t;
        }
    }
}
