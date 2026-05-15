// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Threading;

namespace Wacs.WASI.GFX
{
    /// <summary>
    /// Cheap stderr trace gated on the <c>WACS_GFX_DEBUG</c>
    /// env var. Used by the contract's WitBindings and by the
    /// Silk backend to log binding entry, SDL ops, and event
    /// dispatch. Off by default — production hosts pay only
    /// the env-var read once at class init.
    ///
    /// <para>Format: <c>[wasi-gfx tid=N] message</c>. The thread
    /// id lets you tell main-thread (event pump) entries apart
    /// from worker-thread (wasm guest) entries when both
    /// interleave in the log.</para>
    /// </summary>
    public static class GfxLog
    {
        private static readonly bool _enabled =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WACS_GFX_DEBUG"));

        public static bool Enabled => _enabled;

        public static void Trace(string msg)
        {
            if (!_enabled) return;
            // Single Write keeps the prefix and message
            // atomic on most consoles (no interleaving with
            // other threads' lines mid-record).
            Console.Error.WriteLine(
                "[wasi-gfx tid=" + Thread.CurrentThread.ManagedThreadId
                + "] " + msg);
        }
    }
}
