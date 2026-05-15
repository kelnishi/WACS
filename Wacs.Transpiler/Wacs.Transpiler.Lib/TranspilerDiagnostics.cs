// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Wacs.Transpiler
{
    /// <summary>
    /// Diagnostic trace surface for the transpiler's direct-link
    /// machinery. Off by default; opt in via the
    /// <c>WACS_TRANSPILER_DEBUG</c> environment variable or by
    /// setting <see cref="Enabled"/> from code (the CLI's
    /// <c>--trace-imports</c> flag flips the in-process toggle).
    ///
    /// <para>What you get when enabled:</para>
    /// <list type="bullet">
    /// <item><see cref="TraceLenientServed"/> — fires the first
    /// time the <c>ImportDispatcher</c>'s lenient mode serves a
    /// default for an (interface, method) pair the user expected
    /// to be direct-linked. This is the diagnostic the v0
    /// wasi-gfx debugging needed but didn't have — a hung wasm
    /// at 100% CPU was the only signal that a binding had
    /// silently fallen off the direct-link gate.</item>
    /// <item><see cref="TraceEmitGateReject"/> — fires inside
    /// <c>DirectLinkedImportEmit</c> when a gate returns false,
    /// naming the gate + the reason (which CLR / wasm shape
    /// mismatched). Pairs with the lenient-served log so the
    /// reader can connect "missing handler" to "rejected by
    /// emit gate."</item>
    /// </list>
    ///
    /// <para>Both traces dedupe by (interface, method) so a hot
    /// loop calling the same unresolved import doesn't flood
    /// stderr.</para>
    /// </summary>
    public static class TranspilerDiagnostics
    {
        private static int _enabled = string.IsNullOrEmpty(
                Environment.GetEnvironmentVariable("WACS_TRANSPILER_DEBUG"))
            ? 0
            : 1;

        // (kind, ident) → seen, where kind is "lenient" or "gate"
        // and ident is the (interface, method) pair joined.
        private static readonly ConcurrentDictionary<string, byte>
            _seen = new ConcurrentDictionary<string, byte>();

        /// <summary>
        /// Whether diagnostic traces are emitted to stderr.
        /// Read on every <c>TraceXxx</c> call; safe to flip
        /// concurrently. Volatile-backed.
        /// </summary>
        public static bool Enabled
        {
            get => Volatile.Read(ref _enabled) != 0;
            set => Volatile.Write(ref _enabled, value ? 1 : 0);
        }

        /// <summary>
        /// Log a one-line message that the lenient
        /// <c>ImportDispatcher</c> served a default for
        /// <paramref name="ifaceName"/>.<paramref name="methodName"/>.
        /// Deduped — repeat calls for the same pair don't re-log.
        /// </summary>
        public static void TraceLenientServed(
            string ifaceName, string methodName, Type returnType)
        {
            if (!Enabled) return;
            var key = "lenient:" + ifaceName + "." + methodName;
            if (!_seen.TryAdd(key, 0)) return;
            Console.Error.WriteLine(
                "[wacs-transpiler] lenient default served for "
                + ifaceName + "." + methodName + " : returns "
                + returnType.FullName + " (no handler registered "
                + "and no direct-link binding emitted — guest will "
                + "see a default-valued result, which usually "
                + "manifests as the guest spinning or trapping "
                + "downstream)");
        }

        /// <summary>
        /// Log a one-line message that the direct-link IL emit
        /// rejected a binding for a wasm import. Called from gate
        /// failure sites in <c>DirectLinkedImportEmit</c>.
        /// Deduped per (module, entity, gate, reason) so each
        /// distinct rejection logs exactly once.
        /// </summary>
        public static void TraceEmitGateReject(
            string module, string entity, string gate, string reason)
        {
            if (!Enabled) return;
            var key = "gate:" + module + "/" + entity + "#" + gate;
            if (!_seen.TryAdd(key, 0)) return;
            Console.Error.WriteLine(
                "[wacs-transpiler] direct-link emit rejected "
                + module + "." + entity + " (gate '" + gate
                + "': " + reason + ") — binding falls back to "
                + "delegate dispatch via IImports stub");
        }

        /// <summary>
        /// Reset the dedupe cache. Used by tests that want to
        /// observe a fresh trace stream.
        /// </summary>
        public static void ResetSeen() => _seen.Clear();
    }
}
