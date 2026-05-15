// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
// IGpu / IGpuTexture live in the source-gen-emitted nested
// namespace Wacs.WASI.GFX.Webgpu.Webgpu (override + WIT
// interface name).
using IGpu = Wacs.WASI.GFX.Webgpu.Webgpu.IGpu;
using IGpuTexture = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuTexture;

namespace Wacs.WASI.GFX.Webgpu
{
    /// <summary>
    /// Provider plug-point for a webgpu backend — the GPU sibling
    /// to <see cref="Wacs.WASI.GFX.IBackend"/>. Hosts an
    /// <see cref="IGpu"/> singleton (the wasi:webgpu spec's root
    /// resource) that drives every downstream GPU resource
    /// (adapter / device / queue / buffer / texture / pipeline).
    ///
    /// <para><b>Wiring contract:</b> the v0 GFX backend
    /// (<c>WACS.WASI.GFX.Silk.SilkGfxBackend</c>) is extended in
    /// phase 3c to also implement this interface — one Silk
    /// backend covers both CPU (frame-buffer) and GPU (webgpu)
    /// paths. Future non-Silk backends (Vulkan-direct, Metal-
    /// direct) can implement IGpuBackend independently of the CPU
    /// IBackend.</para>
    ///
    /// <para><b>Threading:</b> the IGpu and its descendants are
    /// invoked on whichever thread the wasm guest's import call
    /// lands on (a worker under <c>--windowed</c>). Backends
    /// implementing this interface MUST handle their own thread
    /// safety — wgpu-native is internally thread-safe, so the
    /// Silk impl forwards calls directly.</para>
    /// </summary>
    public interface IGpuBackend : IDisposable
    {
        /// <summary>
        /// Lazily-constructed singleton. The wasi:webgpu spec's
        /// <c>gpu</c> resource is process-global — every
        /// <c>navigator.gpu</c> lookup in JS-flavored guests
        /// resolves to the same instance. The host's
        /// <see cref="WasiWebgpuHost"/> mints exactly one handle
        /// from this for the lifetime of the host scope.
        /// </summary>
        IGpu CreateGpu();

        /// <summary>
        /// Wrap a graphics-context <see cref="IGpuAbstractBuffer"/>
        /// (a GPU swap-chain surface) as a wasi:webgpu
        /// <c>gpu-texture</c>. Backs the
        /// <c>[static]gpu-texture.from-graphics-buffer</c>
        /// import. Implementations MUST reject CPU-path
        /// <see cref="ICpuAbstractBuffer"/> inputs with a clear
        /// "wrong-kind" error — that lift belongs on the
        /// <see cref="IFrameBufferDevice.FromGraphicsBuffer"/>
        /// path.
        ///
        /// <para>Ownership: the WIT signature consumes
        /// <c>own&lt;abstract-buffer&gt;</c>; the caller (the
        /// wasi-webgpu WitBindings dispatcher) drops the wasm-
        /// side handle after this method returns. Backends may
        /// retain the underlying CLR instance as part of the
        /// returned texture's lifetime, or copy out the wgpu
        /// handle and let the abstract-buffer's Dispose
        /// run.</para>
        /// </summary>
        IGpuTexture FromAbstractBuffer(IAbstractBuffer source);
    }
}
