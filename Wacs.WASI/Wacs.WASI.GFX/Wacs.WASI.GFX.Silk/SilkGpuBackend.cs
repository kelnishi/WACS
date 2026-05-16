// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Loader;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.GFX.Webgpu;
using SNWebGPU = Silk.NET.WebGPU.WebGPU;
using GenIGpu = Wacs.WASI.GFX.Webgpu.Webgpu.IGpu;
using GenIGpuAdapter = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuAdapter;
using GenIGpuTexture = Wacs.WASI.GFX.Webgpu.Webgpu.IGpuTexture;
using GenIWgslLanguageFeatures = Wacs.WASI.GFX.Webgpu.Webgpu.IWgslLanguageFeatures;
using GenGpuRAOpts = Wacs.WASI.GFX.Webgpu.Webgpu.GpuRequestAdapterOptions;
using GenGpuTextureFormat = Wacs.WASI.GFX.Webgpu.Webgpu.GpuTextureFormat;
using GenGpuPowerPreference = Wacs.WASI.GFX.Webgpu.Webgpu.GpuPowerPreference;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Silk.NET.WebGPU-backed implementation of
    /// <see cref="IGpuBackend"/> — the GPU sibling to
    /// <see cref="SilkGfxBackend"/>. Holds the wgpu-native API
    /// entry point + a lazily-created Instance shared by every
    /// adapter request through the lifetime of this backend.
    ///
    /// <para><b>Threading:</b> wgpu-native is internally
    /// thread-safe; method calls on Silk.NET.WebGPU types are
    /// safe from any thread. No main-thread requirement (unlike
    /// SDL). Adapter / device handles are reference-counted at
    /// the wgpu layer; <see cref="IDisposable.Dispose"/> on every
    /// wrapper maps to the matching <c>wgpu*Release</c> call.</para>
    ///
    /// <para><b>Lifetime:</b> backend → instance → adapter →
    /// device → buffers / pipelines / encoders. Disposing the
    /// backend cascades the release in LIFO order via each
    /// wrapper's own <see cref="IDisposable.Dispose"/>.</para>
    /// </summary>
    public sealed unsafe class SilkGpuBackend : IGpuBackend
    {
        private SNWebGPU? _wgpu;
        private Wgpu? _wgpuExt;
        private Instance* _instance;
        private GenIGpu? _gpu;
        private bool _disposed;

        /// <summary>The underlying Silk.NET.WebGPU API entry
        /// point. Lazy-initialized on first use; survives across
        /// every adapter / device request from this backend.</summary>
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

        /// <summary>The wgpu-native extensions API — adds
        /// <c>DevicePoll</c> on top of the core
        /// <see cref="SNWebGPU"/> surface. Used by the
        /// callback→sync bridge for buffer.map-async,
        /// queue.on-submitted-work-done, and other callback-
        /// driven flows.</summary>
        internal Wgpu EnsureWgpuExt()
        {
            if (_wgpuExt != null) return _wgpuExt;
            EnsureApi();
            _wgpuExt = new Wgpu(_wgpu!.Context);
            return _wgpuExt;
        }

        /// <summary>Drives wgpu-native's event loop until the
        /// supplied <paramref name="isDone"/> flag flips. Used
        /// by every callback→sync bridge that isn't covered by
        /// the synchronous adapter/device path. Loops with
        /// <c>wgpuDevicePoll(device, true, NULL)</c> which blocks
        /// until at least one queue submission completes; safe
        /// to call before a submission has been kicked off (it
        /// returns immediately when the queue is idle).
        ///
        /// <para>Bounded by <paramref name="maxIterations"/> to
        /// avoid livelock on a callback that never fires; the
        /// default is generous enough for any healthy GPU op.</para>
        /// </summary>
        internal void PollUntilDone(Device* device, Func<bool> isDone,
            int maxIterations = 1000)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));
            if (isDone == null)
                throw new ArgumentNullException(nameof(isDone));
            var ext = EnsureWgpuExt();
            for (int i = 0; i < maxIterations; i++)
            {
                if (isDone()) return;
                ext.DevicePoll(device, true, null);
            }
            if (!isDone())
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuBackend.PollUntilDone: callback did not fire "
                    + "after " + maxIterations + " wgpuDevicePoll calls. "
                    + "The wgpu-native driver may have stalled.");
        }

        /// <summary>The single wgpu Instance shared by every
        /// adapter request on this backend. wgpu-native is
        /// happy with one instance per process; reusing it
        /// avoids re-initializing the driver discovery path on
        /// each <c>gpu.request-adapter</c> call.</summary>
        internal Instance* EnsureInstance()
        {
            if (_instance != null) return _instance;
            EnsureApi();
            var desc = new InstanceDescriptor();
            _instance = _wgpu!.CreateInstance(&desc);
            if (_instance == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuBackend: wgpuCreateInstance returned "
                    + "null. wgpu-native is installed but the driver "
                    + "discovery failed; check the wgpu-native log "
                    + "for the underlying error.");
            return _instance;
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
            // The buffer's connection holds the wgpu surface
            // configured by ConnectGraphicsContext. Lifting to a
            // texture is just SurfaceGetCurrentTexture wrapped in
            // a SilkGpuTexture — the texture's lifetime is owned
            // by the surface (not refcounted via our wrapper).
            if (source is not SilkGpuAbstractBuffer sgab)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuBackend.FromAbstractBuffer: source is an "
                    + "IGpuAbstractBuffer but not Silk-backed. Cross-"
                    + "backend mixing is not supported.");
            return sgab.ToCurrentTexture();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            (_gpu as IDisposable)?.Dispose();
            _gpu = null;
            if (_instance != null && _wgpu != null)
            {
                _wgpu.InstanceRelease(_instance);
                _instance = null;
            }
            _wgpu?.Dispose();
            _wgpu = null;
        }

        // -----------------------------------------------------
        //  Callback-bridge helpers
        //
        //  wgpu-native completes adapter / device requests
        //  synchronously — the user-supplied callback fires
        //  before the request method returns. We exploit that to
        //  turn the C-style callback API into a sync return via
        //  closure capture. The Pfn* delegate stays alive for the
        //  duration of the call (rooted by the local `cb` var),
        //  so no GCHandle pinning is needed.
        // -----------------------------------------------------

        internal Adapter* RequestAdapterSync(
            Instance* instance,
            RequestAdapterOptions* options,
            out string? errorMessage)
        {
            EnsureApi();
            Adapter* result = null;
            string? err = null;
            bool fired = false;
            var cb = new PfnRequestAdapterCallback(
                (RequestAdapterStatus status, Adapter* a,
                    byte* msg, void* _) =>
                {
                    fired = true;
                    if (status == RequestAdapterStatus.Success)
                        result = a;
                    else
                        err = "wgpu adapter request status="
                            + status + (msg != null
                                ? ": " + DecodeUtf8(msg) : "");
                });
            _wgpu!.InstanceRequestAdapter(instance, options, cb, null);
            if (!fired)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuBackend.RequestAdapterSync: wgpu-native "
                    + "did not invoke the adapter callback "
                    + "synchronously — the sync-bridge assumption "
                    + "no longer holds.");
            errorMessage = err;
            return result;
        }

        internal Device* RequestDeviceSync(
            Adapter* adapter,
            DeviceDescriptor* descriptor,
            out string? errorMessage)
        {
            EnsureApi();
            Device* result = null;
            string? err = null;
            bool fired = false;
            var cb = new PfnRequestDeviceCallback(
                (RequestDeviceStatus status, Device* d,
                    byte* msg, void* _) =>
                {
                    fired = true;
                    if (status == RequestDeviceStatus.Success)
                        result = d;
                    else
                        err = "wgpu device request status="
                            + status + (msg != null
                                ? ": " + DecodeUtf8(msg) : "");
                });
            _wgpu!.AdapterRequestDevice(adapter, descriptor, cb, null);
            if (!fired)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuBackend.RequestDeviceSync: wgpu-native "
                    + "did not invoke the device callback "
                    + "synchronously.");
            errorMessage = err;
            return result;
        }

        internal static string DecodeUtf8(byte* p)
        {
            if (p == null) return string.Empty;
            int len = 0;
            while (p[len] != 0) len++;
            if (len == 0) return string.Empty;
            return System.Text.Encoding.UTF8.GetString(p, len);
        }
    }

    /// <summary>
    /// Silk-backed <c>wasi:webgpu</c> gpu singleton. Translates
    /// <see cref="RequestAdapter"/> to a real wgpu-native
    /// <c>wgpuInstanceRequestAdapter</c> via the backend's
    /// synchronous callback bridge.
    /// </summary>
    internal sealed unsafe class SilkGpu : GenIGpu, IDisposable
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
            if (_disposed)
                throw new ObjectDisposedException(nameof(SilkGpu));
            var instance = _backend.EnsureInstance();
            var raOpts = default(RequestAdapterOptions);
            if (options.TryGetValue(out var o) && o != null)
            {
                if (o.PowerPreference.TryGetValue(out var pp))
                {
                    raOpts.PowerPreference = pp switch
                    {
                        GenGpuPowerPreference.LowPower
                            => PowerPreference.LowPower,
                        GenGpuPowerPreference.HighPerformance
                            => PowerPreference.HighPerformance,
                        _ => PowerPreference.Undefined,
                    };
                }
                if (o.ForceFallbackAdapter.TryGetValue(out var ffa))
                    raOpts.ForceFallbackAdapter = ffa;
                // FeatureLevel + XrCompatible aren't surfaced by
                // wgpu-native's RequestAdapterOptions; the WIT
                // accepts them for forward-compat but they're
                // currently advisory.
            }
            var adapter = _backend.RequestAdapterSync(
                instance, &raOpts, out var err);
            if (adapter == null)
            {
                // Spec: option<gpu-adapter> — None signals "no
                // matching adapter found." err is logged for
                // diagnostics; the wasm guest just sees None.
                if (err != null)
                    Wacs.WASI.GFX.GfxLog.Trace(
                        "SilkGpu.RequestAdapter: " + err);
                return Option<GenIGpuAdapter>.None;
            }
            return Option<GenIGpuAdapter>.Some(
                new SilkGpuAdapter(_backend, adapter));
        }

        public GenGpuTextureFormat GetPreferredCanvasFormat()
        {
            // wgpu reports this per-surface via
            // SurfaceGetCapabilities. Without an active surface
            // the canonical answer matches what every major OS
            // surface reports today: BGRA8 with sRGB encoding.
            return GenGpuTextureFormat.Bgra8unormSrgb;
        }

        public GenIWgslLanguageFeatures WgslLanguageFeatures()
        {
            // wgpu-native implements baseline WGSL; no opt-in
            // features have shipped as of 2026-05.
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
