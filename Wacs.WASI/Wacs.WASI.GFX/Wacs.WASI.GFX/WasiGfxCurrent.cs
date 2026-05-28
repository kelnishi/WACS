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
    /// AsyncLocal-scoped handle to the active wasi-gfx backend.
    /// Replaces the v0 process-global <c>WasiGfxAmbient</c>:
    /// per-<see cref="ExecutionContext"/> scope means multi-
    /// runtime async embedders get isolation automatically while
    /// single-runtime/single-thread embedders observe identical
    /// behavior.
    ///
    /// <para>The <c>WIT [static]buffer.from-graphics-buffer</c>
    /// factory is the canonical reason this exists: a static method
    /// can't accept the <see cref="WasiGfxBundle"/> through ctor
    /// injection (the way the resource <c>Context</c> /
    /// <c>Surface</c> / <c>Device</c> impls do, post-v1-phase-1g),
    /// so it reads the backend from this AsyncLocal. The runtime
    /// instance methods read from here too, dropping the v0
    /// fallback to a process-global static field.</para>
    ///
    /// <para>Set by <c>AddWasiGfx</c> when the bundle is
    /// constructed and by <c>WasiGfxSilkBindable.BindToRuntime</c>
    /// on the CLI's <c>--wasi-gfx</c> path; the value flows to the
    /// wasm guest's invocation context through the standard
    /// .NET async-flow rules.</para>
    /// </summary>
    public static class WasiGfxCurrent
    {
        private static readonly AsyncLocal<IBackend?> _backend = new();

        /// <summary>The active backend in this ExecutionContext,
        /// or null when no binding has run yet.</summary>
        public static IBackend? Backend => _backend.Value;

        /// <summary>Bind <paramref name="backend"/> as the active
        /// one for the current ExecutionContext (and any contexts
        /// flowing from it). The previous value is silently
        /// overwritten — call <see cref="PushBackend"/> when you
        /// need to nest.</summary>
        public static void SetBackend(IBackend? backend)
            => _backend.Value = backend;

        /// <summary>Scoped variant of <see cref="SetBackend"/>:
        /// installs <paramref name="backend"/> for the current
        /// scope and restores the previous value on
        /// <see cref="IDisposable.Dispose"/>. Use this when an
        /// embedder is juggling multiple backends in flight at
        /// once.</summary>
        public static IDisposable PushBackend(IBackend backend)
        {
            var prev = _backend.Value;
            _backend.Value = backend
                ?? throw new ArgumentNullException(nameof(backend));
            return new Restore(prev);
        }

        internal static IBackend RequireBackend()
        {
            var b = _backend.Value;
            if (b == null)
                throw new Types.WasiGfxException(
                    "WasiGfxCurrent.Backend is null — call "
                    + "AddWasiGfx(...) (or runtime.UseWasiGfx(...)) "
                    + "before the wasm guest constructs any wasi-gfx "
                    + "resource.");
            return b;
        }

        private sealed class Restore : IDisposable
        {
            private readonly IBackend? _prev;
            public Restore(IBackend? prev) { _prev = prev; }
            public void Dispose() => _backend.Value = _prev;
        }
    }
}
