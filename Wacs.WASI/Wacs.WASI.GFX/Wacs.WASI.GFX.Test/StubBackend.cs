// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Threading;
using Wacs.WASI.GFX;
using Wacs.WASI.GFX.Types;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.GFX.Test
{
    /// <summary>
    /// Headless test backend for the SPI tests. Doesn't touch
    /// SDL or windowing — each resource is a simple POCO that
    /// records calls for assertions.
    /// </summary>
    internal sealed class StubBackend : IBackend
    {
        public int ContextsCreated { get; private set; }
        public int SurfacesCreated { get; private set; }
        public int FbDevicesCreated { get; private set; }
        public bool RunMainLoopCalled { get; private set; }
        public bool Disposed { get; private set; }

        public IGraphicsContext CreateContext()
        {
            ContextsCreated++;
            return new StubContext();
        }

        public ISurface CreateSurface(uint? width, uint? height)
        {
            SurfacesCreated++;
            return new StubSurface(width ?? 640, height ?? 480);
        }

        public IFrameBufferDevice CreateFrameBufferDevice()
        {
            FbDevicesCreated++;
            return new StubFbDevice();
        }

        public void RunMainLoop(CancellationToken ct)
        {
            RunMainLoopCalled = true;
            // Block briefly so tests can verify "the loop is running"
            // without taking an indefinite wait.
            try { ct.WaitHandle.WaitOne(50); } catch { /* ignore */ }
        }

        public void Dispose() { Disposed = true; }
    }

    internal sealed class StubContext : IGraphicsContext
    {
        public int PresentCalls { get; private set; }
        private readonly byte[] _buf = new byte[256];
        public IAbstractBuffer GetCurrentBuffer() => new StubAbstractBuffer(_buf);
        public void Present() => PresentCalls++;
        public void Dispose() { }
    }

    internal sealed class StubAbstractBuffer : IAbstractBuffer
    {
        public byte[] Backing { get; }
        public StubAbstractBuffer(byte[] backing) { Backing = backing; }
        public void Dispose() { }
    }

    internal sealed class StubSurface : ISurface
    {
        private readonly ManualResetPollable _resizeP = new ManualResetPollable();
        private readonly ManualResetPollable _frameP = new ManualResetPollable();

        public StubSurface(uint w, uint h) { Width = w; Height = h; }

        public uint Width { get; private set; }
        public uint Height { get; private set; }

        public void ConnectGraphicsContext(IGraphicsContext context) { }
        public void RequestSetSize(uint? width, uint? height)
        {
            if (width.HasValue) Width = width.Value;
            if (height.HasValue) Height = height.Value;
        }

        public Pollable SubscribeResize() => _resizeP;
        public ResizeEvent? GetResize() => new ResizeEvent(Width, Height);
        public Pollable SubscribeFrame() => _frameP;
        public FrameEvent? GetFrame() => new FrameEvent(true);
        public Pollable SubscribePointerUp() => new ManualResetPollable();
        public PointerEvent? GetPointerUp() => null;
        public Pollable SubscribePointerDown() => new ManualResetPollable();
        public PointerEvent? GetPointerDown() => null;
        public Pollable SubscribePointerMove() => new ManualResetPollable();
        public PointerEvent? GetPointerMove() => null;
        public Pollable SubscribeKeyUp() => new ManualResetPollable();
        public KeyEvent? GetKeyUp() => null;
        public Pollable SubscribeKeyDown() => new ManualResetPollable();
        public KeyEvent? GetKeyDown() => null;

        public void Dispose() { }
    }

    internal sealed class StubFbDevice : IFrameBufferDevice
    {
        public IGraphicsContext? ConnectedContext { get; private set; }
        public void ConnectGraphicsContext(IGraphicsContext context)
            => ConnectedContext = context;
        public IFrameBufferBuffer FromGraphicsBuffer(IAbstractBuffer src)
            => new StubFbBuffer(((StubAbstractBuffer)src).Backing);
        public void Dispose() { }
    }

    internal sealed class StubFbBuffer : IFrameBufferBuffer
    {
        private readonly byte[] _backing;
        public StubFbBuffer(byte[] backing) { _backing = backing; }
        public ReadOnlyMemory<byte> Get() => _backing;
        public void Set(ReadOnlySpan<byte> data)
        {
            if (data.Length != _backing.Length)
                throw new WasiGfxException("size mismatch");
            data.CopyTo(_backing);
        }
        public void Dispose() { }
    }
}
