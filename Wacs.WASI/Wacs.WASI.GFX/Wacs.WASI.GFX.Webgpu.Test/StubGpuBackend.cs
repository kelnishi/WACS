// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.GFX.Webgpu;
using GenIGpu = Wacs.WASI.GFX.Webgpu.Webgpu.IGpu;
using GenIGpuAdapter = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuAdapter;
using GenIWgsl = Wacs.WASI.GFX.Webgpu.Webgpu.IWgslLanguageFeatures;
using GenGpuRAOpts = Wacs.WASI.GFX.Webgpu.Webgpu.GpuRequestAdapterOptions;
using GenGpuTextureFormat = Wacs.WASI.GFX.Webgpu.Webgpu.GpuTextureFormat;

namespace Wacs.WASI.GFX.Webgpu.Test
{
    /// <summary>
    /// Headless test backend for the v1 phase 3 SPI tests. Records
    /// call counts so the tests can assert the WitBindings reached
    /// the backend correctly; doesn't load any GPU drivers.
    ///
    /// <para>v1 phase 3 session 3 covers <c>get-gpu</c> +
    /// <c>get-preferred-canvas-format</c> +
    /// <c>wgsl-language-features</c> + <c>[resource-drop]gpu</c>.
    /// Adapter / device / buffer / pipeline stubs land alongside
    /// later sessions.</para>
    /// </summary>
    internal sealed class StubGpuBackend : IGpuBackend
    {
        public int CreateGpuCalls { get; private set; }
        public int FromAbstractBufferCalls { get; private set; }
        public bool Disposed { get; private set; }

        public GenIGpu CreateGpu()
        {
            CreateGpuCalls++;
            return new StubGpu();
        }

        public Wacs.WASI.GFX.Webgpu.Webgpu.IGpuTexture
            FromAbstractBuffer(Wacs.WASI.GFX.IAbstractBuffer source)
        {
            FromAbstractBufferCalls++;
            if (source is not Wacs.WASI.GFX.IGpuAbstractBuffer)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "StubGpuBackend.FromAbstractBuffer: source must "
                    + "be IGpuAbstractBuffer.");
            return new StubGpuTexture();
        }

        public void Dispose() { Disposed = true; }
    }

    /// <summary>Test stand-in for a GPU-path abstract-buffer —
    /// implements the marker interface so the bridge can downcast
    /// without exploding on the test side.</summary>
    internal sealed class StubGpuAbstractBuffer
        : Wacs.WASI.GFX.IGpuAbstractBuffer
    {
        public void Dispose() { }
    }

    /// <summary>Test stand-in for a CPU-path abstract-buffer —
    /// used to verify the bridge rejects it on the GPU side.</summary>
    internal sealed class TestCpuAbstractBuffer
        : Wacs.WASI.GFX.ICpuAbstractBuffer
    {
        public void Dispose() { }
    }

    internal sealed class StubGpu : GenIGpu
    {
        public int RequestAdapterCalls { get; private set; }
        public int GetPreferredCanvasFormatCalls { get; private set; }
        public int WgslLanguageFeaturesCalls { get; private set; }

        /// <summary>When true, <see cref="RequestAdapter"/>
        /// returns a fresh <see cref="StubGpuAdapter"/>. Default
        /// false → returns None, matching the original session 3
        /// stub. Session 4 tests flip this to exercise the
        /// option-handle retArea write path.</summary>
        public bool AlwaysReturnAdapter { get; set; }

        /// <summary>Captures the most recent
        /// <c>request-adapter</c> arguments so session 4 tests
        /// can assert the canonical-ABI option<record> decoding
        /// landed the right CLR values.</summary>
        public Option<GenGpuRAOpts> LastRequestAdapterOptions { get; private set; }

        public Option<GenIGpuAdapter> RequestAdapter(
            Option<GenGpuRAOpts> options)
        {
            RequestAdapterCalls++;
            LastRequestAdapterOptions = options;
            return AlwaysReturnAdapter
                ? Option<GenIGpuAdapter>.Some(new StubGpuAdapter())
                : Option<GenIGpuAdapter>.None;
        }

        public GenGpuTextureFormat GetPreferredCanvasFormat()
        {
            GetPreferredCanvasFormatCalls++;
            // BGRA8 unorm — the de-facto preferred swap-chain
            // format on macOS / Windows, and what wgpu-native
            // reports for most surfaces.
            return GenGpuTextureFormat.Bgra8unorm;
        }

        public GenIWgsl WgslLanguageFeatures()
        {
            WgslLanguageFeaturesCalls++;
            return new StubWgslLanguageFeatures();
        }
    }

    internal sealed class StubWgslLanguageFeatures : GenIWgsl
    {
        public bool Has(string value) => false;
    }
}
