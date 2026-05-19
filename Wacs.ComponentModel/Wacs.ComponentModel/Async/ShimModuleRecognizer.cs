// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using Wacs.Core;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Recognizes and consumes the canon-async shim module that
    /// <c>wit-component</c> emits inside a
    /// <c>.component.wasm</c> when the component uses any
    /// canon-async builtin.
    ///
    /// <para><b>Shape</b> (per <c>wasm-tools</c>
    /// <c>crates/wit-component/src/encoding.rs</c>):</para>
    /// <list type="bullet">
    ///   <item>The core module's name section sets module-name
    ///     <c>"wit-component:shim"</c>.</item>
    ///   <item>The module defines a <c>funcref</c> table.</item>
    ///   <item>For each canon-async builtin used by the component,
    ///     the shim has:
    ///     <list type="bullet">
    ///       <item>A function (let's call its index <c>i</c>)
    ///         that calls indirectly through the table.</item>
    ///       <item>An import <c>("", "&lt;i&gt;")</c> the
    ///         embedder fills.</item>
    ///       <item>A function-name custom-section entry mapping
    ///         <c>i</c> → debug name like <c>"task.return"</c>
    ///         or <c>"stream.read"</c>.</item>
    ///     </list>
    ///   </item>
    /// </list>
    ///
    /// <para>WACS consumes this by reading the debug name per
    /// shim function, normalizing dot→dash to match the wasmtime
    /// <c>symbol_name()</c> spelling
    /// (<c>"task.return"</c> → <c>"task-return"</c>),
    /// validating against
    /// <see cref="CanonOpRegistry.IsKnown"/>, and binding the
    /// dispatcher delegate via
    /// <see cref="CanonAsyncBinder"/>'s per-shape switch under
    /// the <c>("", "&lt;i&gt;")</c> import name.</para>
    ///
    /// <para><b>Slice G3 status:</b> the recognizer + name
    /// extraction land here. End-to-end integration with
    /// <see cref="Wacs.ComponentModel.Runtime.ComponentInstance"/>
    /// (multi-core-module shim handling) is the natural next
    /// step once a real wit-component fixture is available to
    /// validate against.</para>
    /// </summary>
    public static class ShimModuleRecognizer
    {
        /// <summary>The exact module-name string wit-component
        /// stamps on the shim's name section.</summary>
        public const string ShimModuleName = "wit-component:shim";

        /// <summary>
        /// True iff <paramref name="core"/>'s name section
        /// declares it as the wit-component canon-async shim.
        /// Requires the consumer to have set
        /// <see cref="BinaryModuleParser.ParseCustomNames"/> to
        /// <c>true</c> before parsing — without that, the name
        /// section is skipped and this returns <c>false</c> even
        /// for genuine shims.
        /// </summary>
        public static bool IsShimModule(Module core) =>
            core != null
            && string.Equals(core.Name, ShimModuleName, System.StringComparison.Ordinal);

        /// <summary>
        /// Read the shim's function-name custom section and
        /// return a (funcIdx → canon-op-name) map. Names are
        /// kebab-normalized (<c>"task.return"</c> →
        /// <c>"task-return"</c>) so they match the spelling
        /// <see cref="CanonOpRegistry.IsKnown"/> accepts.
        ///
        /// <para>Functions whose debug name doesn't map to a
        /// known canon op are still included — the caller can
        /// distinguish via <see cref="CanonOpRegistry.IsKnown"/>
        /// and surface a diagnostic for unrecognized entries.</para>
        ///
        /// <para>Returns an empty dictionary when the shim has no
        /// function-name custom section (which would indicate a
        /// degenerate wit-component output — every real shim
        /// includes the section).</para>
        /// </summary>
        public static Dictionary<uint, string> ExtractCanonOpNames(Module core)
        {
            var result = new Dictionary<uint, string>();
            if (core?.Names?.FunctionNames?.Names?.NameAssocMap is not { } assoc)
                return result;
            foreach (var kv in assoc)
            {
                result[kv.Key] = NormalizeDebugName(kv.Value);
            }
            return result;
        }

        /// <summary>
        /// Convert a wit-component debug name (dotted spelling
        /// like <c>"task.return"</c>) to the canonical wasmtime
        /// kebab spelling (<c>"task-return"</c>) used by
        /// <see cref="CanonOpRegistry"/>.
        /// </summary>
        public static string NormalizeDebugName(string debugName)
        {
            if (string.IsNullOrEmpty(debugName)) return debugName;
            return debugName.Replace('.', '-');
        }
    }
}
