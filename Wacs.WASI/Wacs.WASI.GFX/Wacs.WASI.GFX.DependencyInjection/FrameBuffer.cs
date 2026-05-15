// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using GenFb = Wacs.WASI.GFX.FrameBuffer;
using GenGfxCtx = Wacs.WASI.GFX.GraphicsContext;
using SpiGfx = Wacs.WASI.GFX;

namespace Wacs.WASI.GFX.DependencyInjection
{
    /// <summary>
    /// Direct-link impl for <c>wasi:frame-buffer.device</c>.
    /// Parameterless-ctor + <see cref="Create"/> shape; the
    /// runtime <c>Activator.CreateInstance</c>s + calls Create.
    /// </summary>
    public sealed class Device : GenFb.IDevice, IDisposable
    {
        internal SpiGfx.IFrameBufferDevice Inner { get; private set; } = null!;
        private bool _disposed;

        public void Create()
        {
            var backend = WasiGfxAmbient.RequireBackend();
            Inner = backend.CreateFrameBufferDevice();
        }

        public void ConnectGraphicsContext(GenGfxCtx.IContext context)
        {
            if (context is not Context ctx)
                throw new Types.WasiGfxException(
                    "device.connect-graphics-context: foreign IContext impl");
            if (Inner == null)
                throw new Types.WasiGfxException(
                    "device.connect-graphics-context called before Create()");
            Inner.ConnectGraphicsContext(ctx.Inner);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Inner?.Dispose();
        }
    }

    /// <summary>
    /// Direct-link impl for <c>wasi:frame-buffer.buffer</c>.
    /// The WIT constructor is the static
    /// <c>from-graphics-buffer</c>; the runtime calls
    /// <see cref="FromGraphicsBufferStatic"/> to mint instances.
    /// The instance method <see cref="FromGraphicsBuffer"/>
    /// satisfies the generated interface but is unused at
    /// runtime (mirrors Preview2 <c>Fields.FromList</c> /
    /// <c>FromListStatic</c>'s pattern).
    /// </summary>
    public sealed class Buffer : GenFb.IBuffer, IDisposable
    {
        internal SpiGfx.IFrameBufferBuffer Inner { get; private set; } = null!;
        private bool _disposed;

        /// <summary>Static factory for the WIT
        /// <c>[static]buffer.from-graphics-buffer</c> wire shape.</summary>
        public static Buffer FromGraphicsBufferStatic(
            GenGfxCtx.IAbstractBuffer abstractBuffer)
        {
            if (abstractBuffer is not AbstractBuffer ab)
                throw new Types.WasiGfxException(
                    "buffer.from-graphics-buffer: foreign IAbstractBuffer impl");
            if (ab.Inner == null)
                throw new Types.WasiGfxException(
                    "buffer.from-graphics-buffer: abstract-buffer not initialized");
            // The static call has no surface/device handle; the
            // backend's CPU-path frame-buffer device is the single
            // wasi:frame-buffer.device that was constructed.
            // SilkFrameBufferDevice supports FromGraphicsBuffer
            // without a connect-step.
            var backend = WasiGfxAmbient.RequireBackend();
            var dev = backend.CreateFrameBufferDevice();
            var inner = dev.FromGraphicsBuffer(ab.Inner);
            var buf = new Buffer();
            buf.Inner = inner;
            return buf;
        }

        public GenFb.IBuffer FromGraphicsBuffer(
            GenGfxCtx.IAbstractBuffer abstractBuffer)
            => FromGraphicsBufferStatic(abstractBuffer);

        public byte[] Get()
        {
            if (Inner == null)
                throw new Types.WasiGfxException(
                    "buffer.get called on uninitialized buffer");
            var mem = Inner.Get();
            return mem.ToArray();
        }

        public void Set(byte[] val)
        {
            if (Inner == null)
                throw new Types.WasiGfxException(
                    "buffer.set called on uninitialized buffer");
            Inner.Set(val.AsSpan());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Inner?.Dispose();
        }
    }
}
