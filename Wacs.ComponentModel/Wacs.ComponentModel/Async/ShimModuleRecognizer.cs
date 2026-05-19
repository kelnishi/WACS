// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.Linq;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;

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

        /// <summary>
        /// Result of binding a wit-component shim module — counts
        /// of bound, mismatched, and skipped (deferred) entries
        /// so callers can surface diagnostics.
        /// </summary>
        public readonly struct BindResult
        {
            /// <summary>Delegates successfully bound under
            /// <c>("", "&lt;i&gt;")</c>.</summary>
            public int Bound { get; }
            /// <summary>Shim functions whose debug-name didn't
            /// match the canon-op of the positional canon entry
            /// — surfaces wit-component emit-order surprises.</summary>
            public int Mismatched { get; }
            /// <summary>Canon entries whose typed delegate
            /// construction returned null
            /// (aggregate task.return, non-primitive context, etc.).
            /// Awaits the canon-ABI lift adapter.</summary>
            public int Skipped { get; }

            public BindResult(int bound, int mismatched, int skipped)
            {
                Bound = bound;
                Mismatched = mismatched;
                Skipped = skipped;
            }
        }

        /// <summary>
        /// Walk the shim's function-name custom section, pair each
        /// shim function with the positionally-matching canon-async
        /// entry from <paramref name="canonEntries"/>, build the
        /// typed delegate via
        /// <see cref="CanonAsyncBinder.TryBuildDelegateForEntry"/>,
        /// and register it on <paramref name="runtime"/> under the
        /// shim's <c>("", "&lt;funcIdx&gt;")</c> import name.
        ///
        /// <para><b>Pairing convention:</b> wit-component emits
        /// shim functions in the order it processes the
        /// component's canon-async entries during encoding. The
        /// shim function at index <c>i</c> corresponds to the
        /// <c>i</c>-th canon-async entry in <paramref name="canonEntries"/>
        /// (skipping <see cref="CanonLift"/>, which produces a
        /// component-level func not a core one). The shim's
        /// debug-name custom section serves as a validation
        /// signal: if the kebab-normalized debug-name doesn't
        /// match the positional canon entry's op, the recognizer
        /// reports it as <see cref="BindResult.Mismatched"/> and
        /// skips that binding rather than guessing.</para>
        ///
        /// <para>Returns <see cref="BindResult"/> with
        /// per-category counts so the caller can surface "bound 8
        /// of 10 — 1 mismatched, 1 deferred" diagnostics.</para>
        ///
        /// <para>No-op (returns zero-counts) when
        /// <paramref name="shim"/> is not a shim module, or its
        /// function-name subsection is stripped (the
        /// hard-limit case documented above).</para>
        /// </summary>
        public static BindResult BindShimImports(
            WasmRuntime runtime,
            Module shim,
            IReadOnlyList<CanonEntry> canonEntries,
            AsyncDispatcher dispatcher)
        {
            if (!IsShimModule(shim)) return default;
            var names = ExtractCanonOpNames(shim);
            if (names.Count == 0) return default;

            // Filter the canon list to async entries only. Order
            // preserved — that's the pairing key.
            var asyncEntries = canonEntries
                .Where(IsCanonAsync)
                .ToList();

            int bound = 0, mismatched = 0, skipped = 0;
            foreach (var (shimFuncIdx, opName) in
                     names.OrderBy(kv => kv.Key))
            {
                if (shimFuncIdx >= asyncEntries.Count) { mismatched++; continue; }
                var entry = asyncEntries[(int)shimFuncIdx];
                var entryOpName = CanonOpNameFor(entry);
                if (entryOpName != opName) { mismatched++; continue; }

                var del = CanonAsyncBinder.TryBuildDelegateForEntry(entry, dispatcher);
                if (del == null) { skipped++; continue; }

                runtime.BindHostFunction(
                    (string.Empty, shimFuncIdx.ToString()), del);
                bound++;
            }
            return new BindResult(bound, mismatched, skipped);
        }

        // Distinct from CanonLift — the latter produces a
        // component-level func and doesn't get a shim entry.
        private static bool IsCanonAsync(CanonEntry e) => e switch
        {
            CanonLift _ => false,
            CanonLower _ => false,
            CanonResourceOp _ => false,
            // Async-family entries match. Everything else does too
            // by elimination, but listing them explicitly makes
            // the contract auditable.
            CanonTaskReturn _ or CanonTaskCancel _
                or CanonSubtaskCancel _ or CanonSubtaskDrop _
                or CanonBackpressureOp _ or CanonContextOp _
                or CanonThreadYield _ or CanonStreamOp _
                or CanonFutureOp _ or CanonErrorContextOp _
                or CanonWaitableSetOp _ or CanonWaitableJoin _ => true,
            _ => false,
        };

        // Map a canon entry to its wasmtime symbol_name() spelling.
        // Mirrors the [CanonAsync] attribute decorations on
        // AsyncDispatcher — keep them in sync if either side
        // adds a canon op.
        private static string CanonOpNameFor(CanonEntry e) => e switch
        {
            CanonTaskReturn _ => "task-return",
            CanonTaskCancel _ => "task-cancel",
            CanonSubtaskCancel _ => "subtask-cancel",
            CanonSubtaskDrop _ => "subtask-drop",
            CanonBackpressureOp bp => bp.Op switch
            {
                CanonBackpressureOp.Kind.Set => "backpressure-set",
                CanonBackpressureOp.Kind.Inc => "backpressure-inc",
                CanonBackpressureOp.Kind.Dec => "backpressure-dec",
                _ => "",
            },
            CanonContextOp cx => cx.Op == CanonContextOp.Kind.Get
                ? "context-get" : "context-set",
            CanonThreadYield _ => "thread-yield",
            CanonStreamOp s => s.Op switch
            {
                CanonStreamOp.Kind.New => "stream-new",
                CanonStreamOp.Kind.Read => "stream-read",
                CanonStreamOp.Kind.Write => "stream-write",
                CanonStreamOp.Kind.CancelRead => "stream-cancel-read",
                CanonStreamOp.Kind.CancelWrite => "stream-cancel-write",
                CanonStreamOp.Kind.DropReadable => "stream-drop-readable",
                CanonStreamOp.Kind.DropWritable => "stream-drop-writable",
                _ => "",
            },
            CanonFutureOp f => f.Op switch
            {
                CanonFutureOp.Kind.New => "future-new",
                CanonFutureOp.Kind.Read => "future-read",
                CanonFutureOp.Kind.Write => "future-write",
                CanonFutureOp.Kind.CancelRead => "future-cancel-read",
                CanonFutureOp.Kind.CancelWrite => "future-cancel-write",
                CanonFutureOp.Kind.DropReadable => "future-drop-readable",
                CanonFutureOp.Kind.DropWritable => "future-drop-writable",
                _ => "",
            },
            CanonErrorContextOp ec => ec.Op switch
            {
                CanonErrorContextOp.Kind.New => "error-context-new",
                CanonErrorContextOp.Kind.DebugMessage => "error-context-debug-message",
                CanonErrorContextOp.Kind.Drop => "error-context-drop",
                _ => "",
            },
            CanonWaitableSetOp ws => ws.Op switch
            {
                CanonWaitableSetOp.Kind.New => "waitable-set-new",
                CanonWaitableSetOp.Kind.Wait => "waitable-set-wait",
                CanonWaitableSetOp.Kind.Poll => "waitable-set-poll",
                CanonWaitableSetOp.Kind.Drop => "waitable-set-drop",
                _ => "",
            },
            CanonWaitableJoin _ => "waitable-join",
            _ => "",
        };
    }
}
