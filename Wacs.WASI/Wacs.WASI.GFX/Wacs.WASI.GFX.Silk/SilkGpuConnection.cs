// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Silk.NET.SDL;
using Silk.NET.WebGPU;
using GenWebgpu = Wacs.WASI.GFX.Webgpu.Webgpu;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// State for the wgpu side of a wasi:graphics-context that's
    /// been connected to a webgpu device. Owns the wgpu Surface
    /// and the SDL Metal view (on macOS), exposes
    /// <see cref="GetCurrentTexture"/> for the per-frame
    /// abstract-buffer, and <see cref="Present"/> for the
    /// matching context.present call.
    ///
    /// <para>One connection per context — re-connecting drops
    /// the previous surface. The surface lifetime is tied to
    /// the connection: <see cref="Dispose"/> releases it via
    /// SurfaceRelease + MetalDestroyView.</para>
    ///
    /// <para>macOS-only: only the Metal surface-descriptor
    /// path is implemented. Other platforms throw on connect
    /// with a clear pointer at the missing surface-from-* helper.</para>
    /// </summary>
    internal sealed unsafe class SilkGpuConnection : IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private readonly SilkGpuDevice _device;
        private global::Silk.NET.WebGPU.Surface* _surface;
        private void* _metalView;
        private readonly GenWebgpu.GpuTextureFormat _format;
        private uint _width, _height;
        private bool _disposed;

        public SilkGpuConnection(SilkGpuBackend backend,
            SilkGpuDevice device, global::Silk.NET.WebGPU.Surface* surface, void* metalView,
            GenWebgpu.GpuTextureFormat format,
            uint width, uint height)
        {
            _backend = backend;
            _device = device;
            _surface = surface;
            _metalView = metalView;
            _format = format;
            _width = width;
            _height = height;
        }

        internal global::Silk.NET.WebGPU.Surface* NativeSurface => _surface;
        internal GenWebgpu.GpuTextureFormat Format => _format;
        internal uint Width => _width;
        internal uint Height => _height;

        /// <summary>Get the wgpu texture that corresponds to
        /// the current frame's swap-chain image. Wraps the
        /// surface's owned texture in a SilkGpuTexture that
        /// doesn't release on Dispose — the surface controls
        /// the texture's lifetime, and a release on the
        /// SurfaceTexture's wgpu Texture would crash the
        /// driver.</summary>
        internal SilkGpuTexture GetCurrentTexture()
        {
            EnsureLive();
            SurfaceTexture st = default;
            _backend.EnsureApi().SurfaceGetCurrentTexture(_surface, &st);
            if (st.Status != SurfaceGetCurrentTextureStatus.Success
                || st.Texture == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuConnection.GetCurrentTexture: wgpu surface "
                    + "returned status=" + st.Status
                    + ". Surface needs reconfigure (resize/lost).");
            return new SilkGpuTexture(_backend, st.Texture,
                _width, _height, 1u, 1u, 1u,
                (uint)TextureUsage.RenderAttachment,
                GenWebgpu.GpuTextureDimension.D2,
                _format, label: "swap-chain");
        }

        internal void Present()
        {
            EnsureLive();
            _backend.EnsureApi().SurfacePresent(_surface);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_surface != null)
            {
                _backend.EnsureApi().SurfaceRelease(_surface);
                _surface = null;
            }
            // The Metal view was created via SDL_Metal_CreateView
            // and must be released through the SDL helper. The SDL
            // handle lives on the wasi-gfx side; we can't reach it
            // from here without coupling, so the Metal view leaks
            // until process exit. wgpu surface release already drops
            // the underlying CAMetalLayer ref, so the residual leak
            // is the SDL_MetalView wrapper, not the layer itself.
            _metalView = null;
        }

        private void EnsureLive()
        {
            if (_disposed || _surface == null)
                throw new ObjectDisposedException(nameof(SilkGpuConnection));
        }
    }

    /// <summary>
    /// GPU-flavored <c>abstract-buffer</c> the wasi:graphics-
    /// context produces when its underlying context has been
    /// connected to a webgpu device. The webgpu side's
    /// <c>[static]gpu-texture.from-graphics-buffer</c> binding
    /// resolves the buffer through this type's
    /// <see cref="Connection"/> + <see cref="ToCurrentTexture"/>
    /// to get a wgpu texture for the current frame.
    /// </summary>
    internal sealed class SilkGpuAbstractBuffer
        : IGpuAbstractBuffer
    {
        internal SilkGpuConnection Connection { get; }

        public SilkGpuAbstractBuffer(SilkGpuConnection connection)
        {
            Connection = connection
                ?? throw new ArgumentNullException(nameof(connection));
        }

        internal SilkGpuTexture ToCurrentTexture()
            => Connection.GetCurrentTexture();

        public void Dispose() { /* no native handle — surface owns */ }
    }
}
