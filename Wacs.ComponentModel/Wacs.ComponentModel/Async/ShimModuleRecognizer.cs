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
    ///
    /// <para><b>Stripped-name-section hard limit:</b> wit-component
    /// always emits both the module-name and function-name
    /// subsections for the shim, but downstream tooling
    /// (<c>wasm-opt --strip-debug</c>, <c>wasm-tools strip</c>,
    /// some release pipelines) can remove them. This recognizer
    /// degrades gracefully on stripped module-name (via
    /// <see cref="LooksLikeShimByStructure"/>) — but if the
    /// function-name subsection is stripped, the per-shim
    /// canon-op identity is unrecoverable. The integer indices
    /// the main module imports are positional, and the
    /// position-to-op mapping is wit-component's internal emit
    /// order — not derivable from structure alone. Embedders who
    /// strip names from canon-async-using components break their
    /// own ability to be hosted by WACS.</para>
    /// </summary>
    public static class ShimModuleRecognizer
    {
        /// <summary>The exact module-name string wit-component
        /// stamps on the shim's name section.</summary>
        public const string ShimModuleName = "wit-component:shim";

        /// <summary>
        /// True iff <paramref name="core"/> is recognizable as
        /// wit-component's canon-async shim. Combines two signals:
        ///
        /// <list type="number">
        ///   <item>Primary: name section's module-name equals
        ///     <see cref="ShimModuleName"/>. Cheapest +
        ///     definitive, but requires
        ///     <see cref="BinaryModuleParser.ParseCustomNames"/>
        ///     and an unstripped name section.</item>
        ///   <item>Fallback: structural pattern via
        ///     <see cref="LooksLikeShimByStructure"/> — checks
        ///     for imports from module <c>""</c> with all-digit
        ///     names. Normal core modules don't import from the
        ///     empty namespace, so false-positives are negligible
        ///     in practice.</item>
        /// </list>
        ///
        /// <para>Returns false when both signals miss — including
        /// the case where the name section IS present but the
        /// module name was stripped or set to something else by
        /// an aggressive optimizer.</para>
        /// </summary>
        public static bool IsShimModule(Module core) =>
            core != null
            && (NameSectionSaysShim(core)
                || LooksLikeShimByStructure(core));

        private static bool NameSectionSaysShim(Module core) =>
            string.Equals(core.Name, ShimModuleName,
                System.StringComparison.Ordinal);

        /// <summary>
        /// Structural fallback for <see cref="IsShimModule"/>.
        /// Returns true iff <paramref name="core"/> has at least
        /// one import whose module name is the empty string and
        /// whose function name parses as a non-negative integer
        /// (<c>"0"</c>, <c>"1"</c>, …). This matches wit-component's
        /// shim-import convention
        /// (<c>imports_section.import("", &amp;shim.name, …)</c>
        /// where <c>shim.name</c> is <c>shims.len().to_string()</c>)
        /// and survives name-section stripping.
        ///
        /// <para>The all-digit constraint is important: normal
        /// core modules occasionally import from an empty module
        /// name with descriptive function names (rare but legal),
        /// and we don't want to confuse those for shims. The
        /// integer-name convention is wit-component-specific.</para>
        /// </summary>
        public static bool LooksLikeShimByStructure(Module core)
        {
            if (core?.Imports == null) return false;
            foreach (var imp in core.Imports)
            {
                if (imp.ModuleName != string.Empty) continue;
                if (IsAllDigits(imp.Name)) return true;
            }
            return false;
        }

        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9') return false;
            }
            return true;
        }

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
        /// function-name custom section. This is the
        /// <b>hard-limit case</b>: when downstream tooling strips
        /// the function-name subsection (`wasm-opt
        /// --strip-debug`, `wasm-tools strip`, etc.), the per-
        /// shim canon-op identity is unrecoverable from anything
        /// else in the binary. The main module's calls become
        /// opaque integer-indexed indirect jumps. WACS surfaces
        /// the empty map; the caller should treat it as a
        /// "stripped names — cannot bind this component"
        /// diagnostic.</para>
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
