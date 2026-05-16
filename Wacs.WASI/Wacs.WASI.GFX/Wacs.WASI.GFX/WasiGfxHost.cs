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
    /// <para>Binds canonical-ABI WIT only — wasi-gfx has no
    /// legacy WITX twin. One <see cref="IBackend"/> per host;
    /// the backend covers all three packages
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

        // Pollables share Preview2's ResourceContext. When
        // SharedResources is set, surface's subscribe-* methods
        // mint pollables into Table<Pollable>() on that context,
        // and Preview2's wasi:io/poll@0.2.0.poll binding (which
        // looks up handles in the same context) finds them.
        //
        // If SharedResources is null (tests / headless usage
        // that never polls), we fall back to a local table —
        // surface.subscribe-* still produces handles but
        // wasi:io/poll.poll won't see them. Real embedders MUST
        // configure SharedResources.
        internal Wacs.WASI.Preview2.HostBinding.ResourceTable Pollables { get; }

        public WasiGfxHost() : this(WasiGfxConfiguration.DefaultConfiguration()) { }

        public WasiGfxHost(WasiGfxConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            // Reuse Preview2's pollable table when shared resources
            // are configured (the wired-with-Preview2 case); fall
            // back to a fresh one otherwise.
            Pollables = _config.SharedResources != null
                ? _config.SharedResources.Table<Wacs.WASI.Preview2.Io.Pollable>()
                : new Wacs.WASI.Preview2.HostBinding.ResourceTable();
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
            // Tables drop in dependency order: buffers are leaves;
            // surfaces/devices/contexts may own each other
            // transitively, but each instance is only disposed
            // once because Drop bails on already-removed entries.
            // Pollables are NOT cleared here — they live in
            // Preview2's shared ResourceContext (or our local
            // fallback) and disposing them is Preview2's job.
            // Backend.Dispose runs last — it owns the OS window
            // + SDL handles.
            FrameBufferBuffers.Clear();
            FrameBufferDevices.Clear();
            AbstractBuffers.Clear();
            Surfaces.Clear();
            Contexts.Clear();
            _config.Backend?.Dispose();
        }
    }
}
