// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Marks a partial class as an AOT-safe component harness.
    /// The <see cref="AsyncComponentHarnessGenerator"/> emits
    /// the missing constructor + the implementation of each
    /// <see cref="AsyncExportAttribute"/>-marked partial method,
    /// wiring them through
    /// <see cref="Wacs.ComponentModel.Runtime.ComponentInstance.InstantiateAot"/>
    /// + <see cref="Wacs.ComponentModel.Runtime.ComponentInstance.InvokeCoreAsyncLift"/>
    /// so the consumer code never reaches the reflective
    /// typed-bridge surface.
    ///
    /// <para>Usage:
    /// <code>
    /// [AsyncComponentHarness]
    /// public partial class RunHarness
    /// {
    ///     [AsyncExport("[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run")]
    ///     public partial void Run();
    /// }
    /// </code>
    /// The generator adds:
    /// <list type="bullet">
    ///   <item>A constructor
    ///     <c>(byte[] componentBytes, Action&lt;WasmRuntime&gt;? configureImports)</c>
    ///     that calls
    ///     <c>ComponentInstance.InstantiateAot</c>.</item>
    ///   <item>An <c>Instance</c> property exposing the
    ///     underlying <see cref="Wacs.ComponentModel.Runtime.ComponentInstance"/>
    ///     for direct dispatcher access.</item>
    ///   <item>Each <c>[AsyncExport]</c>-marked partial method's
    ///     body, invoking the named core export through
    ///     <c>InvokeCoreAsyncLift</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class AsyncComponentHarnessAttribute : Attribute
    {
    }

    /// <summary>
    /// Per-method marker inside a
    /// <see cref="AsyncComponentHarnessAttribute"/>-decorated
    /// class. Identifies the core export name the generated
    /// method body should invoke through
    /// <see cref="Wacs.ComponentModel.Runtime.ComponentInstance.InvokeCoreAsyncLift"/>.
    ///
    /// <para>The export name follows wit-component's lift
    /// naming convention — typically
    /// <c>[async-lift]&lt;qualified-export&gt;</c> for async-
    /// lifted exports, plain export name for sync ones.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class AsyncExportAttribute : Attribute
    {
        /// <summary>Name of the wasm core export to invoke.</summary>
        public string ExportName { get; }

        public AsyncExportAttribute(string exportName)
        {
            ExportName = exportName;
        }
    }

    /// <summary>
    /// Per-method marker for SYNC exports — wit-component's
    /// plain canonical lifts that don't go through the
    /// canon-async dispatcher. The generated method body
    /// resolves the export's <c>FuncAddr</c> lazily on first
    /// call, then invokes it via
    /// <c>WasmRuntime.CreateInvokerFunc&lt;...&gt;</c> /
    /// <c>CreateInvokerAction&lt;...&gt;</c> with statically-
    /// known type args — fully AOT-safe, no
    /// <c>InvokeCoreAsyncLift</c> involvement.
    ///
    /// <para>Use this when the component export is sync
    /// (no <c>async func</c> in the WIT) and the export
    /// signature is primitives only. The hello-spike's
    /// <c>greet(name: string) -&gt; string</c> would be a
    /// <c>[SyncExport]</c> once string lift/lower codegen
    /// ships; for now sync primitives are supported.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SyncExportAttribute : Attribute
    {
        /// <summary>Name of the wasm core export to invoke.</summary>
        public string ExportName { get; }

        public SyncExportAttribute(string exportName)
        {
            ExportName = exportName;
        }
    }
}
