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

                if (TraceEnabled)
                    del = WrapForTrace(del, parsed.Value.CanonOp,
                        parsed.Value.AsyncLower);
                runtime.BindHostFunction(id, del);
                bound++;
            }
            return new BindResult(bound, skipped, unrecognized);
        }

        // Pack a sync-completion status for stream.read/.write
        // per the canonical-ABI return convention. Layout (per
        // component-model 0.3.0-rc): the low 4 bits carry the
        // status code (0 = COMPLETED, 1 = DROPPED, 2 =
        // CANCELLED, 3 = STARTED, etc.) and the upper 28 bits
        // carry the count of items transferred. 0xFFFFFFFF is
        // the BLOCKED sentinel (no transfer; will signal via
        // task/waitable later).
        private static int PackStreamStatus(int count, bool completed)
        {
            // STATUS_COMPLETED = 0; we OR in the count<<4.
            return (count << 4) | 0;
        }

        // ----- Diagnostic call-trace --------------------------
        // Optional per-op call counter, enabled by setting
        // WACS_TRACE_SCAFFOLD=1. Lets the cli-hello diagnostic
        // test surface which scaffolding helper a hung
        // wit-bindgen-rt is spinning in.
        private static readonly bool TraceEnabled =
            Environment.GetEnvironmentVariable("WACS_TRACE_SCAFFOLD") == "1";

        // Exposed for ShimModuleRecognizer to share the same
        // counter map — a single trace covers both the
        // wit-bindgen-rt scaffolding and the canon-async shim
        // bindings.
        internal static readonly bool TraceShims = TraceEnabled;

        private static readonly Dictionary<string, int> _counts =
            new Dictionary<string, int>();
        private static readonly object _countsLock = new object();

        public static IReadOnlyDictionary<string, int> SnapshotTrace()
        {
            lock (_countsLock)
                return new Dictionary<string, int>(_counts);
        }

        public static void ResetTrace()
        {
            lock (_countsLock) _counts.Clear();
        }

        private static void Tick(string key)
        {
            lock (_countsLock)
            {
                _counts.TryGetValue(key, out var n);
                _counts[key] = n + 1;
            }
        }

        // Same wrap as WrapForTrace but for shim-bound
        // canon-async delegates. Single shared counter map.
        internal static Delegate WrapForShimTrace(
            Delegate inner, string opName)
        {
            return WrapDelegate(inner, "shim:" + opName);
        }

        // Wrap an already-built delegate with a counter
        // tick. Preserves the runtime's expected signature
        // type by switching on the concrete Delegate shape.
        private static Delegate WrapForTrace(
            Delegate inner, string op, bool asyncLower)
        {
            var key = asyncLower ? "[async-lower]" + op : op;
            return WrapDelegate(inner, key);
        }

        private static Delegate WrapDelegate(Delegate inner, string key)
        {
            switch (inner)
            {
                case Action<ExecContext> a0:
                    return (Action<ExecContext>)(c =>
                        { Tick(key); a0(c); });
                case Action<ExecContext, int> a1:
                    return (Action<ExecContext, int>)((c, x) =>
                        { Tick(key); a1(c, x); });
                case Action<ExecContext, int, int> a2:
                    return (Action<ExecContext, int, int>)((c, x, y) =>
                        { Tick(key); a2(c, x, y); });
                case Func<ExecContext, int> f0:
                    return (Func<ExecContext, int>)(c =>
                        { Tick(key); return f0(c); });
                case Func<ExecContext, long> fL0:
                    return (Func<ExecContext, long>)(c =>
                        { Tick(key); return fL0(c); });
                case Func<ExecContext, int, int> f1:
                    return (Func<ExecContext, int, int>)((c, x) =>
                        { Tick(key); return f1(c, x); });
                case Func<ExecContext, int, int, int> f2:
                    return (Func<ExecContext, int, int, int>)((c, x, y) =>
                        { Tick(key); return f2(c, x, y); });
                case Func<ExecContext, int, int, int, int> f3:
                    return (Func<ExecContext, int, int, int, int>)(
                        (c, x, y, z) =>
                        { Tick(key); return f3(c, x, y, z); });
                default:
                    return inner;
            }
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
        // supported (the future-side async-lower wrappers
        // need typed memory marshaling that lands in a
        // follow-up slice).
        private static Delegate? TryBuildDelegate(
            ParsedScaffold p, AsyncDispatcher d)
        {
            // [async-lower][stream-write-N]<funcname>(handle,
            //   ptr, len) → (i32 status)
            //
            // wit-bindgen-rt's start_write hook for an
            // outbound stream write. Routes to the
            // dispatcher's StreamWriteFromMemory which copies
            // bytes from the wasm's linear memory into the
            // stream's StreamBuffer<byte>. Return value: the
            // bytes-written count (synchronous-complete in
            // wit-bindgen-rt's protocol; high bit set would
            // signal "blocked, poll").
            if (p.AsyncLower && p.CanonOp == "stream-write"
                && p.TypeIdx != null)
            {
                return (Func<ExecContext, int, int, int, int>)(
                    (ctx, handle, ptr, len) =>
                    {
                        if (d.Memory == null) return 0;
                        int written = d.StreamWriteFromMemory(
                            handle, d.Memory,
                            unchecked((uint)ptr), len);
                        return PackStreamStatus(written, completed: true);
                    });
            }

            // [async-lower][stream-read-N]<funcname>(handle,
            //   ptr, cap) → (i32 status)
            if (p.AsyncLower && p.CanonOp == "stream-read"
                && p.TypeIdx != null)
            {
                return (Func<ExecContext, int, int, int, int>)(
                    (ctx, handle, ptr, cap) =>
                    {
                        if (d.Memory == null) return 0;
                        int read = d.StreamReadToMemory(
                            handle, d.Memory,
                            unchecked((uint)ptr), cap);
                        return PackStreamStatus(read, completed: true);
                    });
            }

            // The future-side async-lower wrappers need typed
            // memory marshaling that we don't have yet — the
            // future's element type drives how many bytes to
            // read/write from the buffer at `ptr`. Land in a
            // follow-up.
            if (p.AsyncLower) return null;

            switch (p.CanonOp)
            {
                // task-return: forwards the wasm-side return
                // value (a single i32 disc for cli-hello's
                // result<_, _> return) into the dispatcher's
                // current-task slot. The lift adapter surfaces
                // it as the host's await result.
                case "task-return":
                    return (Action<ExecContext, int>)((_, disc) =>
                        d.TaskReturn(null!, disc));
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
                // waitable-set-poll: (set, mem-ptr) → (i32
                // event-id). Calls the dispatcher's sync poll
                // — non-blocking check for any deliverable
                // waitable. mem-ptr is where the spec wants
                // the deliverable event written; we ignore it
                // for v0 since the return value carries the
                // member-handle directly.
                case "waitable-set-poll":
                    return (Func<ExecContext, int, int, int>)(
                        (_, setHandle, _) =>
                            d.WaitableSetPoll(null!, setHandle, 0, false));

                // context-get/-set — typeIdx is the slot
                // index. The dispatcher's signatures take a
                // Value (struct); for the integer-slot pattern
                // wit-bindgen-rt uses, we route through i32.
                case "context-get":
                    if (p.TypeIdx == null) return null;
                    return (Func<ExecContext, int>)(_ =>
                        d.ContextGet(p.TypeIdx.Value).Data.Int32);
                case "context-set":
                    if (p.TypeIdx == null) return null;
                    return (Action<ExecContext, int>)((_, v) =>
                        d.ContextSet(p.TypeIdx.Value,
                            new Wacs.Core.Runtime.Value(v)));

                default:
                    return null;
            }
        }
    }
}
