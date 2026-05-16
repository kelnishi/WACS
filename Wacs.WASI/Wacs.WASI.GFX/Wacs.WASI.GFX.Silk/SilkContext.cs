// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.WASI.GFX.Types;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// CPU-path graphics-context for the Silk backend. Owns the
    /// back-buffer (RGBA8 packed) sized to the connected
    /// surface's current dimensions. <see cref="GetCurrentBuffer"/>
    /// returns a fresh <see cref="SilkAbstractBuffer"/> wrapping
    /// this context; <see cref="Present"/> blits the back-buffer
    /// bytes to the connected surface and renders.
    ///
    /// <para>Contract: one surface per context. Multi-surface
    /// composition is out of scope until the WIT clarifies it.</para>
    /// </summary>
    internal sealed class SilkContext : IGraphicsContext
    {
        internal SilkSurface? Surface { get; private set; }
        internal byte[] BackBuffer { get; private set; } = Array.Empty<byte>();
        internal int BackBufferWidth { get; private set; }
        internal int BackBufferHeight { get; private set; }
        /// <summary>Set by <c>SilkGpuDevice.ConnectGraphicsContext</c>
        /// when a webgpu device takes over the frame loop. While
        /// non-null, <see cref="GetCurrentBuffer"/> returns a GPU-
        /// flavored abstract-buffer and <see cref="Present"/>
        /// routes through wgpu's surface-present.</summary>
        internal SilkGpuConnection? GpuConnection { get; set; }

        internal void ConnectSurface(SilkSurface surface)
        {
            Surface = surface ?? throw new ArgumentNullException(nameof(surface));
            EnsureBackBuffer();
        }

        // Re-allocate the back-buffer to match the connected
        // surface's current size. Called on connect + each
        // GetCurrentBuffer (which catches resize-between-frames).
        private void EnsureBackBuffer()
        {
            if (Surface == null) return;
            int w = (int)Surface.Width;
            int h = (int)Surface.Height;
            if (w == BackBufferWidth && h == BackBufferHeight) return;
            BackBufferWidth = w;
            BackBufferHeight = h;
            BackBuffer = new byte[Math.Max(0, w * h * 4)];
        }

        public IAbstractBuffer GetCurrentBuffer()
        {
            if (Surface == null)
                throw new WasiGfxException(
                    "context.get-current-buffer called before any "
                    + "surface was connected to this context.");
            // GPU mode: hand back a GPU-flavored buffer that
            // resolves to the surface's current swap-chain texture
            // when the webgpu side lifts it via from-graphics-buffer.
            if (GpuConnection != null)
                return new SilkGpuAbstractBuffer(GpuConnection);
            EnsureBackBuffer();
            return new SilkAbstractBuffer(this);
        }

        public void Present()
        {
            if (Surface == null)
                throw new WasiGfxException(
                    "context.present called before any surface "
                    + "was connected to this context.");
            // GPU mode: present routes through wgpu's swap-chain.
            // The SDL window's surface is already attached to wgpu
            // via the Metal layer, so we don't also do SDL's blit.
            if (GpuConnection != null)
            {
                GpuConnection.Present();
                return;
            }
            Surface.Blit(BackBuffer, BackBufferWidth, BackBufferHeight);
        }

        public void Dispose()
        {
            // Back-buffer is plain managed memory — let GC handle.
            // Surface lifetime is owned by its own resource handle;
            // don't dispose here.
        }
    }

    /// <summary>
    /// Thin wrapper for the WIT <c>abstract-buffer</c> resource.
    /// Just holds a reference to the context whose back-buffer
    /// it represents. No state — the static
    /// <c>buffer.from-graphics-buffer</c> consumes it and the
    /// returned frame-buffer-buffer talks to the context
    /// directly.
    /// </summary>
    internal sealed class SilkAbstractBuffer : ICpuAbstractBuffer
    {
        internal SilkContext Context { get; }

        public SilkAbstractBuffer(SilkContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public void Dispose() { /* no native resources */ }
    }

    /// <summary>
    /// CPU-path frame-buffer device for the Silk backend.
    /// Stores the context it's connected to as a hook for
    /// future multi-device flows; today the static
    /// <c>buffer.from-graphics-buffer</c> path reads the
    /// context off the abstract-buffer directly so the device
    /// reference is informational.
    /// </summary>
    internal sealed class SilkFrameBufferDevice : IFrameBufferDevice
    {
        internal SilkContext? Context { get; private set; }

        public void ConnectGraphicsContext(IGraphicsContext context)
        {
            if (context is not SilkContext sctx)
                throw new WasiGfxException(
                    "SilkFrameBufferDevice.ConnectGraphicsContext: "
                    + "context is not a SilkContext "
                    + "(cross-backend mixing is not supported).");
            Context = sctx;
        }

        public IFrameBufferBuffer FromGraphicsBuffer(IAbstractBuffer src)
        {
            // Downcast goes through ICpuAbstractBuffer so a
            // guest accidentally handing a GPU-surface buffer to
            // the CPU frame-buffer path gets a clear "wrong kind"
            // error instead of a backend-specific cast failure.
            if (src is not ICpuAbstractBuffer)
                throw new WasiGfxException(
                    "device.FromGraphicsBuffer: source is not a "
                    + "CPU-path abstract-buffer (got GPU-path or "
                    + "third-party type). The frame-buffer device "
                    + "only consumes ICpuAbstractBuffer; webgpu "
                    + "guests should use "
                    + "[static]gpu-texture.from-graphics-buffer.");
            if (src is not SilkAbstractBuffer sab)
                throw new WasiGfxException(
                    "device.FromGraphicsBuffer: source is not a "
                    + "SilkAbstractBuffer (cross-backend mixing is "
                    + "not supported).");
            return new SilkFrameBufferBuffer(sab.Context);
        }

        public void Dispose() { /* no native resources */ }
    }

    /// <summary>
    /// CPU-path frame-buffer buffer for the Silk backend.
    /// <see cref="Get"/> / <see cref="Set"/> read and write
    /// the connected context's back-buffer bytes; the next
    /// <c>context.present</c> commits them to the surface.
    /// </summary>
    internal sealed class SilkFrameBufferBuffer : IFrameBufferBuffer
    {
        private readonly SilkContext _context;

        public SilkFrameBufferBuffer(SilkContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public ReadOnlyMemory<byte> Get()
        {
            return _context.BackBuffer;
        }

        public void Set(ReadOnlySpan<byte> data)
        {
            var dst = _context.BackBuffer;
            if (data.Length != dst.Length)
                throw new WasiGfxException(
                    "buffer.set: input length "
                    + data.Length + " does not match back-buffer length "
                    + dst.Length + " (width="
                    + _context.BackBufferWidth + " * height="
                    + _context.BackBufferHeight + " * 4 RGBA8).");
            data.CopyTo(dst);
        }

        public void Dispose() { /* no native resources */ }
    }
}
