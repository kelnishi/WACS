// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Silk.NET.WebGPU;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.GFX.Webgpu;
using SNWebGPU = Silk.NET.WebGPU.WebGPU;
using GenIGpu = Wacs.WASI.GFX.Webgpu.Webgpu.IGpu;
using GenIGpuAdapter = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuAdapter;
using GenIGpuTexture = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuTexture;
using GenIWgslLanguageFeatures = Wacs.WASI.GFX.Webgpu.Webgpu.IWgslLanguageFeatures;
using GenGpuRAOpts = Wacs.WASI.GFX.Webgpu.Webgpu.GpuRequestAdapterOptions;
using GenGpuTextureFormat = Wacs.WASI.GFX.Webgpu.Webgpu.GpuTextureFormat;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Silk.NET.WebGPU-backed implementation of
    /// <see cref="IGpuBackend"/> — the GPU sibling to
    /// <see cref="SilkGfxBackend"/>. v1 phase 3 session 8 ships
    /// the skeleton + the Silk.NET.WebGPU package wiring so
    /// downstream sessions (descriptor decoding + parity
    /// fixtures) have a target to land against.
    ///
    /// <para>The same Silk dependency serves both CPU
    /// (frame-buffer / surface) and GPU (webgpu) host paths;
    /// embedders that wire <c>WasiGfxSilkBindable</c> get GPU
    /// support automatically once the actual
    /// <see cref="SilkGpu"/> implementation lands. Most surface
    /// methods currently throw
    /// <see cref="PlatformNotSupportedException"/> with a
    /// pointer to the session that wires them.</para>
    ///
    /// <para><b>Threading:</b> wgpu-native is internally
    /// thread-safe; method calls on Silk.NET.WebGPU types are
    /// safe from any thread. No main-thread requirement (unlike
    /// SDL). Adapter / device handles are reference-counted at
    /// the wgpu layer; the SPI's IDisposable.Dispose maps to
    /// the matching <c>wgpu*Release</c> call.</para>
    /// </summary>
    public sealed class SilkGpuBackend : IGpuBackend
    {
        private SNWebGPU? _wgpu;
        private GenIGpu? _gpu;
        private bool _disposed;

        /// <summary>The underlying Silk.NET.WebGPU API entry
        /// point. Lazy-initialized on first
        /// <see cref="CreateGpu"/>.</summary>
        internal SNWebGPU EnsureApi()
        {
            if (_wgpu != null) return _wgpu;
            // WebGPU.GetApi() resolves the wgpu-native shared
            // library at the OS-specific install path bundled by
            // Silk.NET.WebGPU.Native.WGPU. On macOS this lands
            // libwgpu_native.dylib in the package's runtimes/
            // tree; the loader probes there first.
            _wgpu = SNWebGPU.GetApi();
            return _wgpu;
        }

        public GenIGpu CreateGpu()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SilkGpuBackend));
            return _gpu ??= new SilkGpu(this);
        }

        public GenIGpuTexture FromAbstractBuffer(IAbstractBuffer source)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SilkGpuBackend));
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source is not IGpuAbstractBuffer)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuBackend.FromAbstractBuffer: source is "
                    + "not an IGpuAbstractBuffer. The CPU-path "
                    + "(SilkAbstractBuffer / ICpuAbstractBuffer) "
                    + "cannot be lifted to a wasi:webgpu texture; "
                    + "use the frame-buffer device's "
                    + "FromGraphicsBuffer instead.");
            // Real wgpu texture creation: import the swap-chain
            // surface as a WGPUTexture handle. Session 11 wires
            // the wgpu API call alongside the surface
            // configuration that produces IGpuAbstractBuffer
            // instances in the first place.
            throw new PlatformNotSupportedException(
                "SilkGpuBackend.FromAbstractBuffer — the wgpu-native "
                + "swap-chain-to-texture lift lands in v1 phase 3 "
                + "session 11 alongside SilkGpuAdapter.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            (_gpu as IDisposable)?.Dispose();
            _gpu = null;
            _wgpu?.Dispose();
            _wgpu = null;
        }
    }

    /// <summary>
    /// Silk-backed <c>wasi:webgpu</c> gpu singleton. Mints
    /// adapters via the wgpu instance. v1 session 8 skeleton:
    /// the wgpu-native instance is acquired and held; downstream
    /// <see cref="RequestAdapter"/> / format / features methods
    /// are stubbed until session 11 wires actual GPU dispatch.
    /// </summary>
    internal sealed class SilkGpu : GenIGpu, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private bool _disposed;

        public SilkGpu(SilkGpuBackend backend)
        {
            _backend = backend;
        }

        public Option<GenIGpuAdapter> RequestAdapter(
            Option<GenGpuRAOpts> options)
        {
            // wgpu.InstanceRequestAdapter is an async-callback
            // API that the wasi:webgpu sync surface must adapt
            // by spinning on a flag the callback flips. Session
            // 11 ships the adapter-resolve dance + the
            // SilkGpuAdapter wrapper around WGPUAdapter*. Until
            // then, headless / no-GPU embedders see this throw
            // and can wire their own backend; CI without GPU
            // skips wasi-webgpu fixtures.
            throw new PlatformNotSupportedException(
                "SilkGpu.RequestAdapter — wgpu-native dispatch "
                + "lands in v1 phase 3 session 11. Until then, "
                + "use a custom IGpuBackend for wasi-webgpu tests.");
        }

        public GenGpuTextureFormat GetPreferredCanvasFormat()
        {
            // wgpu reports this per-surface via
            // wgpu.SurfaceGetCapabilities. Without an active
            // surface the canonical answer matches what every
            // major OS surface reports today: BGRA8 with sRGB
            // encoding. Session 11 will pivot to the surface-
            // queried value once SilkGpuAdapter is in place.
            return GenGpuTextureFormat.Bgra8unormSrgb;
        }

        public GenIWgslLanguageFeatures WgslLanguageFeatures()
        {
            // v1 wgpu-native doesn't expose a language-features
            // query beyond "what it implements," which today is
            // the WGSL spec verbatim. Return an empty features
            // set; guests must check the WGSL spec for any
            // extension they want to use.
            return new SilkWgslLanguageFeatures();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }

    internal sealed class SilkWgslLanguageFeatures : GenIWgslLanguageFeatures
    {
        // wgpu-native implements baseline WGSL; no opt-in
        // features have shipped as of 2026-05.
        public bool Has(string value) => false;
    }
}
