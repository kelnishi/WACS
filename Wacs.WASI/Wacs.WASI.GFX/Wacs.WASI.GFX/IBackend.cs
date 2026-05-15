// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Threading;
using Wacs.WASI.GFX.Types;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.GFX
{
    /// <summary>
    /// Provider plug-point for a wasi-gfx backend. The SPI is the
    /// surface both ABIs depend on (canonical-ABI WIT only — no
    /// WITX twin; wasi-gfx has no legacy ABI). Backends never see
    /// wire types or guest memory directly: the bindings handle
    /// canonical-ABI lift/lower and call into the SPI in CLR
    /// types.
    ///
    /// <para>Backends are registered on
    /// <see cref="WasiGfxConfiguration.Backend"/> — one backend
    /// per host, not encoding-keyed (unlike wasi-nn). Missing
    /// package coverage is rejected at
    /// <c>WasiGfxHost.BindToRuntime</c> time.</para>
    ///
    /// <para><b>Threading:</b> backends that drive OS windowing
    /// or input MUST pump their event loop on the embedder's
    /// main thread — see <see cref="RunMainLoop"/>. v0 SPI does
    /// not support background-thread event pumps; that's a macOS
    /// AppKit hard requirement we don't paper over.</para>
    /// </summary>
    public interface IBackend : IDisposable
    {
        /// <summary>
        /// Mint a fresh <c>wasi:graphics-context.context</c>
        /// resource. The context is the abstract connector
        /// between a rendering API (frame-buffer or webgpu) and
        /// a surface; it has no platform meaning until a
        /// surface attaches via
        /// <see cref="ISurface.ConnectGraphicsContext"/>.
        /// </summary>
        IGraphicsContext CreateContext();

        /// <summary>
        /// Mint a <c>wasi:surface.surface</c> resource. The
        /// surface is the drawing target — a host-owned window
        /// in v0 — and the source of pointer/key/resize/frame
        /// events. Width/height are advisory; backends may
        /// substitute platform defaults (e.g. SDL falls back to
        /// 640x480 when both are null).
        /// </summary>
        ISurface CreateSurface(uint? width, uint? height);

        /// <summary>
        /// Mint a <c>wasi:frame-buffer.device</c> resource. The
        /// frame-buffer device is the CPU-side raster path:
        /// guests build pixel data on the host heap and push it
        /// through <see cref="IFrameBufferBuffer.Set"/>, then
        /// the connected graphics context blits to the surface.
        /// </summary>
        IFrameBufferDevice CreateFrameBufferDevice();

        /// <summary>
        /// Run the backend's OS event loop, blocking the calling
        /// thread until <paramref name="ct"/> fires. This is the
        /// "embedder owns the main thread" entry point: the host
        /// runs wasm on a worker, then calls this on the main
        /// thread to pump SDL / winit / equivalent events into
        /// the per-surface pollables.
        ///
        /// <para>Backends that don't drive OS windowing (test
        /// stubs, headless) may return immediately.</para>
        /// </summary>
        void RunMainLoop(CancellationToken ct);
    }

    /// <summary>
    /// Host-side <c>wasi:graphics-context.context</c>. Bridges a
    /// surface to a rendering API. The <c>abstract-buffer</c>
    /// returned by <see cref="GetCurrentBuffer"/> is opaque to
    /// guests; specific rendering APIs (frame-buffer, webgpu)
    /// know how to interpret it.
    /// </summary>
    public interface IGraphicsContext : IDisposable
    {
        /// <summary>
        /// Acquire the next frame's backing buffer. The returned
        /// <see cref="IAbstractBuffer"/> is the "draw here"
        /// target; <see cref="Present"/> commits its contents
        /// to the connected surface.
        /// </summary>
        IAbstractBuffer GetCurrentBuffer();

        /// <summary>
        /// Commit the most recently acquired buffer to the
        /// connected surface. Equivalent to swap-buffers in
        /// a traditional graphics API.
        /// </summary>
        void Present();
    }

    /// <summary>
    /// Host-side <c>wasi:graphics-context.abstract-buffer</c>.
    /// Opaque connector type. Frame-buffer guests turn it into
    /// a CPU pixel buffer via
    /// <see cref="IFrameBufferDevice"/>; webgpu guests turn it
    /// into a GPUTexture via <c>[static]gpu-texture.from-graphics-buffer</c>.
    ///
    /// <para>v1 phase 3 session 9: backends produce instances
    /// that implement <em>one</em> of the two sub-interfaces
    /// (<see cref="ICpuAbstractBuffer"/> or
    /// <see cref="IGpuAbstractBuffer"/>) so consumers can
    /// downcast cleanly without backend-specific knowledge. The
    /// wasi-gfx WIT itself doesn't distinguish — a single
    /// <c>abstract-buffer</c> handle moves through both worlds
    /// at the wire level. Mixing kinds (e.g. handing a CPU
    /// buffer to <c>[static]gpu-texture.from-graphics-buffer</c>)
    /// is a backend-level error, surfaced at the consume site
    /// rather than at the produce site.</para>
    /// </summary>
    public interface IAbstractBuffer : IDisposable
    {
    }

    /// <summary>
    /// Marker for an <see cref="IAbstractBuffer"/> the
    /// <see cref="IFrameBufferDevice.FromGraphicsBuffer"/> path
    /// can consume — a CPU-side raster surface where the bytes
    /// are addressable by the host. Backends with a CPU
    /// rendering target produce these from
    /// <see cref="IGraphicsContext.GetCurrentBuffer"/>.
    /// </summary>
    public interface ICpuAbstractBuffer : IAbstractBuffer
    {
    }

    /// <summary>
    /// Marker for an <see cref="IAbstractBuffer"/> the
    /// <c>[static]gpu-texture.from-graphics-buffer</c> path can
    /// consume — a GPU swap-chain surface that wraps a wgpu /
    /// vulkan / metal texture handle. Backends with an active
    /// GPU rendering target produce these.
    /// </summary>
    public interface IGpuAbstractBuffer : IAbstractBuffer
    {
    }

    /// <summary>
    /// Host-side <c>wasi:surface.surface</c>. Each subscribe-*
    /// returns a fresh <see cref="Pollable"/> that the backend
    /// signals when the matching OS event fires; the
    /// corresponding get-* method drains the latest event (or
    /// returns null if no event is pending since the last drain).
    ///
    /// <para>v0 backends typically return
    /// <c>Wacs.WASI.Preview2.Io.ManualResetPollable</c>
    /// instances — the existing wasi-io plumbing covers the
    /// guest-side <c>pollable.ready()</c> / <c>pollable.block()</c>
    /// surface verbatim.</para>
    /// </summary>
    public interface ISurface : IDisposable
    {
        uint Width { get; }
        uint Height { get; }

        /// <summary>
        /// Attach the surface to a graphics context. The
        /// rendering API (frame-buffer or webgpu) then knows
        /// which surface to blit to on
        /// <see cref="IGraphicsContext.Present"/>.
        /// </summary>
        void ConnectGraphicsContext(IGraphicsContext context);

        /// <summary>
        /// Advisory size request. Backends MAY ignore or clamp.
        /// Successful changes will fire a resize event on the
        /// next event-pump tick.
        /// </summary>
        void RequestSetSize(uint? width, uint? height);

        Pollable SubscribeResize();
        ResizeEvent? GetResize();

        Pollable SubscribeFrame();
        FrameEvent? GetFrame();

        Pollable SubscribePointerUp();
        PointerEvent? GetPointerUp();

        Pollable SubscribePointerDown();
        PointerEvent? GetPointerDown();

        Pollable SubscribePointerMove();
        PointerEvent? GetPointerMove();

        Pollable SubscribeKeyUp();
        KeyEvent? GetKeyUp();

        Pollable SubscribeKeyDown();
        KeyEvent? GetKeyDown();
    }

    /// <summary>
    /// Host-side <c>wasi:frame-buffer.device</c>. v0 backends
    /// expose a single device per host; the WIT supports
    /// multi-device but no current guest needs it.
    /// </summary>
    public interface IFrameBufferDevice : IDisposable
    {
        /// <summary>
        /// Attach the frame-buffer device to a graphics context.
        /// After this, the context's abstract-buffers can be
        /// turned into frame-buffer buffers via
        /// <see cref="FromGraphicsBuffer"/>.
        /// </summary>
        void ConnectGraphicsContext(IGraphicsContext context);

        /// <summary>
        /// Wrap the abstract-buffer in a frame-buffer view that
        /// exposes raw pixel bytes via
        /// <see cref="IFrameBufferBuffer.Get"/> /
        /// <see cref="IFrameBufferBuffer.Set"/>. The returned
        /// buffer's lifetime is tied to the
        /// <paramref name="src"/>'s — disposing
        /// <paramref name="src"/> invalidates the wrapper.
        /// </summary>
        IFrameBufferBuffer FromGraphicsBuffer(IAbstractBuffer src);
    }

    /// <summary>
    /// Host-side <c>wasi:frame-buffer.buffer</c>. Raw pixel
    /// bytes — v0 hard-codes the layout as RGBA8 packed,
    /// width*height*4 bytes. The WIT signature uses
    /// <c>list&lt;u8&gt;</c> which is intentionally untyped on
    /// the format; format negotiation is deferred to a future
    /// proposal cut.
    /// </summary>
    public interface IFrameBufferBuffer : IDisposable
    {
        /// <summary>
        /// Read pixels out of the backing buffer. v0 returns a
        /// view — callers MUST consume before the next surface
        /// frame tick.
        /// </summary>
        ReadOnlyMemory<byte> Get();

        /// <summary>
        /// Write pixels into the backing buffer. Length must
        /// equal <c>width*height*4</c>; mismatches throw a
        /// <see cref="WasiGfxException"/>.
        /// </summary>
        void Set(ReadOnlySpan<byte> data);
    }
}
