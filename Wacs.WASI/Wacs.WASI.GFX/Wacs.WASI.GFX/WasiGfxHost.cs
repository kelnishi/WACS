// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.GFX.HostBinding;
using Wacs.WASI.GFX.Types;

namespace Wacs.WASI.GFX
{
    /// <summary>
    /// Top-level wasi-gfx host. Mirrors
    /// <c>Wacs.WASI.NN.WasiNNHost</c> — owns the per-instance
    /// state (resource handle tables + configuration), exposes
    /// <see cref="BindToRuntime"/> to wire imports onto a
    /// <see cref="WasmRuntime"/>.
    ///
    /// <para>v0 binds canonical-ABI WIT only — wasi-gfx has no
    /// legacy WITX twin. One <see cref="IBackend"/> per host;
    /// the backend covers all three v0 packages
    /// (graphics-context + surface + frame-buffer).</para>
    /// </summary>
    public sealed class WasiGfxHost : IBindable, IDisposable
    {
        private readonly WasiGfxConfiguration _config;

        internal ResourceTable Contexts { get; } = new();
        internal ResourceTable Surfaces { get; } = new();
        internal ResourceTable FrameBufferDevices { get; } = new();
        internal ResourceTable FrameBufferBuffers { get; } = new();
        internal ResourceTable AbstractBuffers { get; } = new();

        // Pollables are minted by surface's subscribe-* methods.
        // The handle space is shared across all subscribe-*
        // sources so guests can mix-and-match in a single poll
        // call. The Pollable type comes from WACS.WASI.Preview2;
        // we reuse it directly rather than re-implementing.
        internal ResourceTable Pollables { get; } = new();

        public WasiGfxHost() : this(WasiGfxConfiguration.DefaultConfiguration()) { }

        public WasiGfxHost(WasiGfxConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// The backend driving this host. Null until configured;
        /// <see cref="BindToRuntime"/> rejects bindings when no
        /// backend is set.
        /// </summary>
        public IBackend? Backend => _config.Backend;

        /// <summary>
        /// Wire the wasi-gfx imports onto
        /// <paramref name="runtime"/>. Requires a backend on the
        /// configuration; throws otherwise.
        /// </summary>
        public void BindToRuntime(WasmRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (_config.Backend == null)
                throw new WasiGfxException(
                    "Cannot bind wasi-gfx: no backend configured. "
                    + "Set WasiGfxConfiguration.Backend before constructing the host.");

            WitBindings.Bind(runtime, this);
        }

        public void Dispose()
        {
            // Tables drop in dependency order: pollables and
            // buffers are leaves; surfaces/devices/contexts may
            // own each other transitively, but each instance is
            // only disposed once because Drop bails on
            // already-removed entries. Backend.Dispose runs last
            // — it owns the OS window + SDL handles.
            Pollables.Clear();
            FrameBufferBuffers.Clear();
            FrameBufferDevices.Clear();
            AbstractBuffers.Clear();
            Surfaces.Clear();
            Contexts.Clear();
            _config.Backend?.Dispose();
        }
    }
}
