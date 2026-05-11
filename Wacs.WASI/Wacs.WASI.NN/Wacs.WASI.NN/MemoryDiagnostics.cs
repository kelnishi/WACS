// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN
{
    /// <summary>
    /// Per-call memory + timing instrumentation around each
    /// <see cref="IBackendContext.Compute"/> invocation. Gated on
    /// <c>WACS_DIAG_MEMORY=1</c> in the environment; off by default
    /// (zero overhead in the negative path — the binding layer
    /// reads <see cref="Enabled"/> once and skips the snapshot
    /// hot loop).
    ///
    /// <para>Used to diagnose growth patterns when a long-running
    /// host (e.g., a chat REPL) sees RSS climb across conversation
    /// turns: the per-call counters distinguish
    ///   <list type="bullet">
    ///     <item><b>RSS growth that matches input growth</b> —
    ///       expected: the guest is accumulating history</item>
    ///     <item><b>Managed-heap growth without input growth</b> —
    ///       host-side reference retention (canonical-ABI lift
    ///       copies pinned somewhere)</item>
    ///     <item><b>RSS growth without managed or input growth</b>
    ///       — native fragmentation (libsystem_malloc on macOS
    ///       doesn't decommit) or backend-internal native cache</item>
    ///   </list>
    /// </para>
    ///
    /// <para>Output goes to stderr so a piped stdout still gets
    /// just the model's text. Format is stable for grep-based
    /// post-processing.</para>
    /// </summary>
    internal static class MemoryDiagnostics
    {
        internal static readonly bool Enabled = ParseEnabled(
            Environment.GetEnvironmentVariable("WACS_DIAG_MEMORY"));

        private static int _turn;
        private static long _baselineRss = -1;
        private static long _baselineManaged = -1;

        /// <summary>
        /// Counter incremented every time a wasi-nn
        /// <c>[resource-drop]X</c> interpreter binding fires
        /// (regardless of whether the cross-table hook is wired).
        /// A stuck zero proves the binding itself isn't being
        /// invoked — i.e., the wasm component's resource-drop
        /// import is being satisfied through some other code
        /// path that bypasses the runtime's entity-binding table.
        /// </summary>
        internal static int InterpreterDropInvocations;

        /// <summary>
        /// Counter incremented when the cross-table
        /// <see cref="Wacs.Core.Runtime.WasmRuntime.ExternalResourceDrop"/>
        /// hook actually fires (a subset of the above — only counts
        /// when the binding fires AND ExternalResourceDrop is wired).
        /// </summary>
        internal static int ExternalDropInvocations;

        /// <summary>
        /// Wrap an <see cref="IBackendContext.Compute"/> call with
        /// memory + timing snapshot logging when
        /// <see cref="Enabled"/> is true. Snapshot points: RSS,
        /// managed heap, GC generation counts, input/output byte
        /// totals, wall-clock duration.
        /// </summary>
        public static IReadOnlyList<NamedTensor> RunWithDiagnostics(
            IBackendContext context,
            IReadOnlyList<NamedTensor> inputs)
        {
            if (!Enabled) return context.Compute(inputs);

            var sw = Stopwatch.StartNew();
            IReadOnlyList<NamedTensor>? outputs = null;
            try
            {
                outputs = context.Compute(inputs);
                return outputs;
            }
            finally
            {
                sw.Stop();
                EmitSnapshot(inputs, outputs, sw.Elapsed.TotalSeconds);
            }
        }

        private static void EmitSnapshot(
            IReadOnlyList<NamedTensor> inputs,
            IReadOnlyList<NamedTensor>? outputs,
            double durationSec)
        {
            int turn = Interlocked.Increment(ref _turn);

            long rss = 0;
            try
            {
                using var proc = Process.GetCurrentProcess();
                proc.Refresh();
                rss = proc.WorkingSet64;
            }
            catch { /* counters degrade gracefully if RSS unavailable */ }

            long managed = GC.GetTotalMemory(forceFullCollection: false);

            // Capture the first turn's snapshot as a baseline so
            // subsequent turns can report a delta — much easier to
            // read at a glance than the absolute number across 50
            // conversation turns.
            if (_baselineRss < 0) _baselineRss = rss;
            if (_baselineManaged < 0) _baselineManaged = managed;
            long rssDelta = rss - _baselineRss;
            long managedDelta = managed - _baselineManaged;

            long inBytes = 0, outBytes = 0;
            for (int i = 0; i < inputs.Count; i++)
                inBytes += inputs[i].Tensor.Data.Length;
            if (outputs != null)
                for (int i = 0; i < outputs.Count; i++)
                    outBytes += outputs[i].Tensor.Data.Length;

            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);

            // Single-line format, grep-friendly. All bytes in
            // human-readable form except input/output (small,
            // exact byte counts are more useful for correlating
            // against guest prompt sizes).
            int interpDrops = InterpreterDropInvocations;
            int extDrops = ExternalDropInvocations;
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "[wacs-diag-memory] turn={0} rss={1} ({2}) managed={3} ({4}) "
                + "gc[g0={5} g1={6} g2={7}] in={8}B out={9}B "
                + "drops[interp={10} ext={11}] took={12:F2}s",
                turn,
                FormatBytes(rss),
                FormatBytesDelta(rssDelta),
                FormatBytes(managed),
                FormatBytesDelta(managedDelta),
                gen0, gen1, gen2,
                inBytes, outBytes,
                interpDrops, extDrops,
                durationSec);

            try { Console.Error.WriteLine(line); }
            catch { /* stderr closed; nothing to do */ }
        }

        private static string FormatBytes(long b)
        {
            if (b < 1024L) return b + "B";
            if (b < 1024L * 1024L) return (b / 1024.0).ToString(
                "F1", CultureInfo.InvariantCulture) + "KiB";
            if (b < 1024L * 1024L * 1024L) return (b / (1024.0 * 1024.0))
                .ToString("F1", CultureInfo.InvariantCulture) + "MiB";
            return (b / (1024.0 * 1024.0 * 1024.0)).ToString(
                "F2", CultureInfo.InvariantCulture) + "GiB";
        }

        private static string FormatBytesDelta(long b)
        {
            // Signed form so the reader sees "+0.5MiB" or "-0.1MiB"
            // without parsing.
            if (b < 0) return "-" + FormatBytes(-b);
            return "+" + FormatBytes(b);
        }

        private static bool ParseEnabled(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                default:
                    return false;
            }
        }
    }
}
