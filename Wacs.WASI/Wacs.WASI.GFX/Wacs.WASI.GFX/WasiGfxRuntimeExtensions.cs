// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;

namespace Wacs.WASI.GFX
{
    /// <summary>
    /// Ergonomic one-liner to wire wasi-gfx onto a
    /// <see cref="WasmRuntime"/>. Replaces the multi-step
    /// "construct config → set backend → build host →
    /// BindToRuntime" sequence with a single call that matches
    /// the shape embedders use for the rest of the host stack
    /// (e.g. <c>runtime.UseWasiNN(...)</c>,
    /// <c>runtime.UseWasiPreview2(...)</c>).
    ///
    /// <para>Usage:
    /// <code>
    /// using var host = runtime.UseWasiGfx(b =&gt;
    ///     b.WithBackend(new SilkGfxBackend()));
    /// </code>
    /// </para>
    /// </summary>
    public static class WasiGfxRuntimeExtensions
    {
        /// <summary>
        /// Construct and bind a <c>WasiGfxHost</c> using the
        /// configuration produced by
        /// <paramref name="configure"/>. Returns the host so
        /// the caller can hold its lifetime — it owns resource
        /// tables that must outlive the runtime's invocations
        /// of wasi-gfx imports.
        /// </summary>
        public static WasiGfxHost UseWasiGfx(this WasmRuntime runtime,
            Action<WasiGfxBuilder>? configure = null)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            var builder = new WasiGfxBuilder();
            configure?.Invoke(builder);
            var host = new WasiGfxHost(builder.Build());
            host.BindToRuntime(runtime);
            return host;
        }
    }

    /// <summary>
    /// Fluent builder for <see cref="WasiGfxConfiguration"/>.
    /// Lets
    /// <c>runtime.UseWasiGfx(b =&gt; b.WithBackend(...))</c>
    /// read like the rest of the host wiring.
    /// </summary>
    public sealed class WasiGfxBuilder
    {
        private IBackend? _backend;

        /// <summary>
        /// Set the backend for this host. v0 supports one
        /// backend per host; subsequent calls replace the
        /// previous value (last-write-wins).
        /// </summary>
        public WasiGfxBuilder WithBackend(IBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            return this;
        }

        internal WasiGfxConfiguration Build()
        {
            var cfg = WasiGfxConfiguration.DefaultConfiguration();
            cfg.Backend = _backend;
            return cfg;
        }
    }
}
