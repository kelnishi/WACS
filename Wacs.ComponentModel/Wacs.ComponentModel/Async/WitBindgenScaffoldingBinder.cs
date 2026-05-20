// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Binds the canon-async helper imports that wit-bindgen-rt
    /// emits per-imported-async-call-site into a guest core
    /// module. These are distinct from the canon-async builtins
    /// that the component declares in its canon section (those
    /// route through the wit-component:shim and are handled by
    /// <see cref="ShimModuleRecognizer"/> + <see cref="CanonAsyncBinder"/>).
    ///
    /// <para>The naming convention is wit-bindgen-rt's, not
    /// spec-defined. wit-bindgen-rt emits per-method scaffolding
    /// of the form <c>("&lt;iface-name&gt;", "[&lt;canon-op&gt;-N]&lt;funcname&gt;")</c>
    /// where:
    /// <list type="bullet">
    ///   <item><c>&lt;iface-name&gt;</c> is the imported interface's
    ///     namespaced WIT name (e.g.,
    ///     <c>wasi:cli/stdin@0.3.0-rc-2026-03-15</c>).</item>
    ///   <item><c>&lt;canon-op&gt;</c> is the canonical op name
    ///     (e.g., <c>stream-new</c>, <c>future-cancel-write</c>,
    ///     <c>stream-drop-readable</c>).</item>
    ///   <item><c>-N</c> is the canon op's typeidx
    ///     disambiguator (stream type index for stream ops,
    ///     future type index for future ops, slot index for
    ///     context ops; absent for non-disambiguated ops like
    ///     <c>task-cancel</c>).</item>
    ///   <item><c>&lt;funcname&gt;</c> is the imported method
    ///     (e.g., <c>read-via-stream</c>) — wit-bindgen uses it
    ///     to disambiguate helpers across multiple async-imported
    ///     methods of the same interface.</item>
    /// </list>
    /// </para>
    ///
    /// <para><b>Special form:</b>
    /// <c>[async-lower][&lt;canon-op&gt;-N]&lt;funcname&gt;</c>
    /// (double-bracketed) wraps the canon op as a host-call
    /// async lower — wit-bindgen-rt uses it to start a call to
    /// the imported async method and get back a poll-able
    /// future-handle. The bracketed inner op identifies which
    /// canon op the lower-side is layering on. v0 binds these
    /// as permissive stubs returning a fresh future-handle that
    /// completes synchronously; the spec-correct implementation
    /// requires the canon-async lift adapter for outbound async
    /// calls.</para>
    ///
    /// <para><b>Top-level [task-return]&lt;funcname&gt;</b> under
    /// <c>[export]&lt;iface&gt;</c> is the export-side
    /// task-return shim — wit-component injects it for every
    /// async-lifted export. Bound the same way as the canon
    /// section's <c>CanonTaskReturn</c> entry would be.</para>
    ///
    /// <para><b>Per component-model#654</b>: wit-bindgen-rt's
    /// naming convention isn't spec-defined. Future versions of
    /// wit-bindgen (or alternative linkers) may emit different
    /// names. This binder is best-effort against the
    /// current-version wit-bindgen output; embedders whose
    /// toolchain emits a different convention need a sibling
    /// binder.</para>
    /// </summary>
    public static class WitBindgenScaffoldingBinder
    {
        /// <summary>
        /// Counts of bindings made and entries skipped, by
        /// category, for diagnostics.
        /// </summary>
        public readonly struct BindResult
        {
            /// <summary>Imports successfully bound to dispatcher
            /// delegates.</summary>
            public int Bound { get; }
            /// <summary>Imports recognized as wit-bindgen-rt
            /// scaffolding but with a shape this binder doesn't
            /// yet handle (e.g., <c>[async-lower][...]</c>
            /// without dispatcher support).</summary>
            public int Skipped { get; }
            /// <summary>Imports that didn't match any
            /// wit-bindgen-rt pattern. Caller can stub or trap
            /// these depending on what they want to surface.</summary>
            public int Unrecognized { get; }

            public BindResult(int bound, int skipped, int unrecognized)
            {
                Bound = bound; Skipped = skipped; Unrecognized = unrecognized;
            }
        }

        /// <summary>
        /// Walk <paramref name="coreModule"/>'s function imports,
        /// pattern-match the wit-bindgen-rt-emitted bracketed
        /// names, and bind delegates routing to
        /// <paramref name="dispatcher"/>. Imports already bound
        /// on <paramref name="runtime"/> (e.g., from a previous
        /// pass or from a host's <c>BindToRuntime</c>) are
        /// skipped.
        /// </summary>
        public static BindResult BindImports(
            WasmRuntime runtime, Module coreModule,
            AsyncDispatcher dispatcher)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (coreModule == null)
                throw new ArgumentNullException(nameof(coreModule));
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));

            int bound = 0, skipped = 0, unrecognized = 0;
            foreach (var import in coreModule.Imports)
            {
                if (!(import.Desc is Module.ImportDesc.FuncDesc))
                    continue;
                var id = (import.ModuleName, import.Name);
                if (runtime.TryGetExportedFunction(id, out _))
                    continue;

                var parsed = TryParseScaffoldingName(import.Name);
                if (parsed == null) { unrecognized++; continue; }

                var del = TryBuildDelegate(parsed.Value, dispatcher);
                if (del == null) { skipped++; continue; }

                runtime.BindHostFunction(id, del);
                bound++;
            }
            return new BindResult(bound, skipped, unrecognized);
        }

        /// <summary>
        /// Parsed shape of a wit-bindgen-rt scaffolding import
        /// name. The canonical op name is one of the wasmtime
        /// <c>symbol_name()</c> spellings
        /// (<c>"stream-new"</c>, <c>"future-cancel-write"</c>,
        /// <c>"task-return"</c>, etc.); <see cref="TypeIdx"/> is
        /// the trailing <c>-N</c> disambiguator or null when the
        /// op doesn't carry one.
        /// </summary>
        public readonly struct ParsedScaffold
        {
            public string CanonOp { get; }
            public int? TypeIdx { get; }
            public bool AsyncLower { get; }
            public string FuncName { get; }

            public ParsedScaffold(string canonOp, int? typeIdx,
                bool asyncLower, string funcName)
            {
                CanonOp = canonOp;
                TypeIdx = typeIdx;
                AsyncLower = asyncLower;
                FuncName = funcName;
            }
        }

        /// <summary>
        /// Parse a wit-bindgen-rt-emitted scaffolding import
        /// name. Returns null when the name doesn't match the
        /// expected bracketed shape.
        ///
        /// <para>Recognized shapes:</para>
        /// <list type="bullet">
        ///   <item><c>[stream-new-0]read-via-stream</c></item>
        ///   <item><c>[future-cancel-write-1]read-via-stream</c></item>
        ///   <item><c>[async-lower][stream-write-0]read-via-stream</c></item>
        ///   <item><c>[task-return]run</c> (no typeIdx)</item>
        ///   <item><c>[task-cancel]</c> (no funcname suffix)</item>
        /// </list>
        /// </summary>
        public static ParsedScaffold? TryParseScaffoldingName(string name)
        {
            if (string.IsNullOrEmpty(name) || name[0] != '[')
                return null;

            int i = 0;
            bool asyncLower = false;

            // Strip [async-lower] prefix if present.
            if (StartsWithBracket(name, i, "async-lower", out int afterAsync))
            {
                asyncLower = true;
                i = afterAsync;
                if (i >= name.Length || name[i] != '[') return null;
            }

            // Read the next bracketed segment: [<op-or-op-N>]
            if (name[i] != '[') return null;
            int closeIdx = name.IndexOf(']', i);
            if (closeIdx < 0) return null;
            string opBody = name.Substring(i + 1, closeIdx - (i + 1));
            string funcName = name.Substring(closeIdx + 1);

            // Split the typeidx suffix: "stream-new-5" → ("stream-new", 5).
            string canonOp;
            int? typeIdx = null;
            int dashIdx = opBody.LastIndexOf('-');
            if (dashIdx > 0
                && int.TryParse(
                    opBody.Substring(dashIdx + 1), out int n))
            {
                canonOp = opBody.Substring(0, dashIdx);
                typeIdx = n;
            }
            else
            {
                canonOp = opBody;
            }

            return new ParsedScaffold(canonOp, typeIdx,
                asyncLower, funcName);
        }

        // Helper: returns true if `name` at offset `i` starts
        // with a `[<expected>]` bracketed segment; reports the
        // index AFTER the closing bracket via `afterEnd`.
        private static bool StartsWithBracket(
            string name, int i, string expected, out int afterEnd)
        {
            afterEnd = -1;
            int len = expected.Length;
            if (i + len + 2 > name.Length) return false;
            if (name[i] != '[' || name[i + len + 1] != ']')
                return false;
            for (int k = 0; k < len; k++)
                if (name[i + 1 + k] != expected[k]) return false;
            afterEnd = i + len + 2;
            return true;
        }

        // Build the host-side delegate for a parsed scaffold
        // entry. Returns null when the shape isn't currently
        // supported (the async-lower wrappers + typed-payload
        // task-return entries land in follow-up slices).
        private static Delegate? TryBuildDelegate(
            ParsedScaffold p, AsyncDispatcher d)
        {
            // The [async-lower][<op>] wrappers start a host
            // async call and return a future-handle the
            // guest polls. v0 doesn't have the canon-async
            // lift adapter for outbound calls — skip and
            // surface as Skipped.
            if (p.AsyncLower) return null;

            switch (p.CanonOp)
            {
                // task-return: bind the no-payload form
                // (return-disc-only). Typed-payload variants
                // come from the canon section, not the
                // wit-bindgen scaffolding.
                case "task-return":
                    return (Action<ExecContext, int>)((_, _) =>
                        d.TaskReturn(null!, null));
                case "task-cancel":
                    return (Action<ExecContext>)(_ =>
                        d.TaskCancel(null!));

                // stream ops. wit-bindgen-rt's scaffolding
                // imports don't pass the async-flag arg the
                // dispatcher methods take — that's a wasmtime
                // canon-binder convention. wit-bindgen-rt
                // emits cancel/drop ops as (handle) → (i32) or
                // (handle) → ().
                case "stream-new":
                    if (p.TypeIdx == null) return null;
                    // wit-bindgen-rt's stream-new returns a
                    // packed u64 (low 32 = writer-handle,
                    // high 32 = reader-handle). The
                    // dispatcher's StreamNew returns a single
                    // unified handle; emit it in both halves
                    // until we model wit-bindgen-rt's
                    // dual-handle convention end-to-end.
                    return (Func<ExecContext, long>)(_ =>
                    {
                        int h = d.StreamNew(p.TypeIdx.Value);
                        return ((long)h << 32) | (uint)h;
                    });
                case "stream-drop-readable":
                    return (Action<ExecContext, int>)((_, h) =>
                        d.StreamDropReadable(h));
                case "stream-drop-writable":
                    return (Action<ExecContext, int>)((_, h) =>
                        d.StreamDropWritable(h));
                case "stream-cancel-read":
                    return (Func<ExecContext, int, int>)((_, h) =>
                        d.StreamCancelRead(h, false) ? 1 : 0);
                case "stream-cancel-write":
                    return (Func<ExecContext, int, int>)((_, h) =>
                        d.StreamCancelWrite(h, false) ? 1 : 0);

                // future ops — same shape as stream ops above.
                case "future-new":
                    if (p.TypeIdx == null) return null;
                    return (Func<ExecContext, long>)(_ =>
                    {
                        int h = d.FutureNew(p.TypeIdx.Value);
                        return ((long)h << 32) | (uint)h;
                    });
                case "future-drop-readable":
                    return (Action<ExecContext, int>)((_, h) =>
                        d.FutureDropReadable(h));
                case "future-drop-writable":
                    return (Action<ExecContext, int>)((_, h) =>
                        d.FutureDropWritable(h));
                case "future-cancel-read":
                    return (Func<ExecContext, int, int>)((_, h) =>
                        d.FutureCancelRead(h, false) ? 1 : 0);
                case "future-cancel-write":
                    return (Func<ExecContext, int, int>)((_, h) =>
                        d.FutureCancelWrite(h, false) ? 1 : 0);

                // waitable-set / waitable-join — not
                // typeidx-disambiguated.
                case "waitable-set-new":
                    return (Func<ExecContext, int>)(_ =>
                        d.WaitableSetNew());
                case "waitable-set-drop":
                    return (Action<ExecContext, int>)((_, h) =>
                        d.WaitableSetDrop(h));
                case "waitable-join":
                    return (Action<ExecContext, int, int>)((_, w, t) =>
                        d.WaitableJoin(w, t));

                default:
                    return null;
            }
        }
    }
}
