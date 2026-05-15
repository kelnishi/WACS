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
    /// <para>v0 contract: one surface per context. Multi-surface
    /// composition is out of scope until the WIT clarifies it.</para>
    /// </summary>
    internal sealed class SilkContext : IGraphicsContext
    {
        internal SilkSurface? Surface { get; private set; }
        internal byte[] BackBuffer { get; private set; } = Array.Empty<byte>();
        internal int BackBufferWidth { get; private set; }
        internal int BackBufferHeight { get; private set; }

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
            EnsureBackBuffer();
            return new SilkAbstractBuffer(this);
        }

        public void Present()
        {
            if (Surface == null)
                throw new WasiGfxException(
                    "context.present called before any surface "
                    + "was connected to this context.");
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
    internal sealed class SilkAbstractBuffer : IAbstractBuffer
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
    /// Stores the context it's connected to so future
    /// multi-device flows have a hook, but the static
    /// <c>buffer.from-graphics-buffer</c> path reads the
    /// context off the abstract-buffer directly — the device
    /// reference is informational in v0.
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
