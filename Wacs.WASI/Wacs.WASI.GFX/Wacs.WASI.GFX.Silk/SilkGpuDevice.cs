// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Silk.NET.WebGPU;
using Wacs.ComponentModel.Runtime;
using GenWebgpu = Wacs.WASI.GFX.Webgpu.Webgpu;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Silk-backed wrapper around a wgpu <c>Device*</c>. Holds
    /// the native device handle plus the adapter it came from
    /// (some queries forward to the adapter — features /
    /// limits / adapter-info follow the wgpu pattern of "ask the
    /// adapter, not the device").
    /// </summary>
    internal sealed unsafe class SilkGpuDevice : GenWebgpu.IGpuDevice, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private readonly Adapter* _adapter;
        private Device* _device;
        private Queue* _queue;
        private SilkGpuQueue? _queueWrapper;
        private string _label = string.Empty;
        private bool _disposed;
        // Pollables subscribed via OnuncapturederrorSubscribe. The
        // wgpu device-level uncaptured-error callback fires on a
        // wgpu-internal thread and signals every entry — guests
        // that share one device but want independent error
        // monitors can each subscribe and get their own pollable.
        // The PfnErrorCallback delegate has to outlive the device,
        // so the field also pins it indirectly via _uncapturedCb.
        private readonly System.Collections.Generic.List<
            Wacs.WASI.Preview2.Io.ManualResetPollable> _uncapturedSubs
            = new();
        private PfnErrorCallback? _uncapturedCb;
        private bool _uncapturedCallbackRegistered;

        public SilkGpuDevice(
            SilkGpuBackend backend, Adapter* adapter, Device* device)
        {
            _backend = backend;
            _adapter = adapter;
            _device = device;
        }

        internal Device* Native => _device;

        public GenWebgpu.IGpuSupportedFeatures Features()
        {
            EnsureLive();
            return new SilkGpuSupportedFeatures(_backend, _adapter);
        }

        public GenWebgpu.IGpuSupportedLimits Limits()
        {
            EnsureLive();
            return new SilkGpuSupportedLimits(_backend, _adapter);
        }

        public GenWebgpu.IGpuAdapterInfo AdapterInfo()
        {
            EnsureLive();
            return new SilkGpuAdapterInfo(_backend, _adapter);
        }

        public GenWebgpu.IGpuQueue Queue()
        {
            EnsureLive();
            if (_queueWrapper != null) return _queueWrapper;
            _queue = _backend.EnsureApi().DeviceGetQueue(_device);
            if (_queue == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.Queue: wgpuDeviceGetQueue returned "
                    + "null. The device handle is alive but no queue "
                    + "was attached — wgpu-native treats this as a "
                    + "fatal error on the driver side.");
            return _queueWrapper = new SilkGpuQueue(_backend, _device, _queue);
        }

        public void Destroy()
        {
            // wgpu-native distinguishes destroy (driver-side
            // teardown) from release (refcount drop). The WIT's
            // destroy maps to the former; the wrapper's IDisposable
            // does the latter. wgpu-native exposes destroy via
            // wgpuDeviceDestroy, which Silk binds.
            EnsureLive();
            // The Silk method name varies by version; reach for the
            // function via reflection-free direct call. wgpu's
            // device-destroy in 2.23.0 surfaces as DeviceDestroy.
            _backend.EnsureApi().DeviceDestroy(_device);
        }

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            // Silk has a DeviceSetLabel(byte*) overload; the round-
            // trip through wgpu is best-effort (wgpu's label is
            // for debug-marker output, the WIT's get-label is what
            // the guest reads back). We track the label on our side
            // for the WIT round-trip and forward to wgpu for
            // diagnostics.
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().DeviceSetLabel(_device, p);
            }
        }

        public GenWebgpu.IGpuDeviceLostInfo Lost()
        {
            // wgpu-native exposes device-lost via callback; the
            // WIT's lost() is a polled query. Until the callback
            // wiring lands, report "no loss" via a synthetic stub.
            return new SilkGpuDeviceLostInfo();
        }

        public void PushErrorScope(GenWebgpu.GpuErrorFilter filter)
        {
            EnsureLive();
            var f = filter switch
            {
                GenWebgpu.GpuErrorFilter.Validation => ErrorFilter.Validation,
                GenWebgpu.GpuErrorFilter.OutOfMemory => ErrorFilter.OutOfMemory,
                GenWebgpu.GpuErrorFilter.Internal => ErrorFilter.Internal,
                _ => ErrorFilter.Validation,
            };
            _backend.EnsureApi().DevicePushErrorScope(_device, f);
        }

        public Result<Option<GenWebgpu.IGpuError>, GenWebgpu.PopErrorScopeError>
            PopErrorScope()
        {
            EnsureLive();
            bool done = false;
            var errType = ErrorType.NoError;
            string message = string.Empty;
            var cb = new PfnErrorCallback(
                (ErrorType t, byte* msg, void* _) =>
                {
                    errType = t;
                    if (msg != null) message = SilkGpuBackend.DecodeUtf8(msg);
                    done = true;
                });
            _backend.EnsureApi().DevicePopErrorScope(_device, cb, null);
            _backend.PollUntilDone(_device, () => done);
            if (errType == ErrorType.NoError)
                return Result<Option<GenWebgpu.IGpuError>, GenWebgpu.PopErrorScopeError>
                    .FromOk(Option<GenWebgpu.IGpuError>.None);
            GenWebgpu.GpuErrorKind kind = errType switch
            {
                ErrorType.Validation =>
                    new GenWebgpu.GpuErrorKind.GpuErrorKindValidationError(),
                ErrorType.OutOfMemory =>
                    new GenWebgpu.GpuErrorKind.GpuErrorKindOutOfMemoryError(),
                ErrorType.Internal =>
                    new GenWebgpu.GpuErrorKind.GpuErrorKindInternalError(),
                _ =>
                    new GenWebgpu.GpuErrorKind.GpuErrorKindInternalError(),
            };
            return Result<Option<GenWebgpu.IGpuError>, GenWebgpu.PopErrorScopeError>
                .FromOk(Option<GenWebgpu.IGpuError>.Some(
                    new SilkGpuError(kind, message)));
        }

        public IPollable OnuncapturederrorSubscribe()
        {
            EnsureLive();
            EnsureUncapturedCallbackRegistered();
            var pollable = new Wacs.WASI.Preview2.Io.ManualResetPollable();
            lock (_uncapturedSubs) _uncapturedSubs.Add(pollable);
            return pollable;
        }

        private void EnsureUncapturedCallbackRegistered()
        {
            if (_uncapturedCallbackRegistered) return;
            _uncapturedCallbackRegistered = true;
            // wgpu's uncaptured-error callback fires on a wgpu-
            // owned thread (any submission processing). Capture
            // the device + sub-list via the field; no userdata
            // needed.
            _uncapturedCb = new PfnErrorCallback(
                (ErrorType type, byte* msg, void* _) =>
                {
                    if (type == ErrorType.NoError) return;
                    Wacs.WASI.Preview2.Io.ManualResetPollable[] snapshot;
                    lock (_uncapturedSubs)
                        snapshot = _uncapturedSubs.ToArray();
                    foreach (var p in snapshot)
                    {
                        try { p.Signal(); }
                        catch { /* swallow per-subscriber errors */ }
                    }
                });
            _backend.EnsureApi().DeviceSetUncapturedErrorCallback(
                _device, _uncapturedCb.Value, null);
        }

        public void ConnectGraphicsContext(
            global::Wacs.WASI.GFX.GraphicsContext.IContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            EnsureLive();
            // Drill from the wit-generated IContext down to the
            // SilkContext (SPI). Two wrapper shapes carry an SPI
            // IGraphicsContext today:
            //   - WasiGfxSilkBindable.SpiContextAdapter (interpreter)
            //   - Wacs.WASI.GFX.DependencyInjection.Context (DI /
            //     transpiler-direct-link)
            // Both expose an Inner field internally.
            Wacs.WASI.GFX.IGraphicsContext? spi = context switch
            {
                WasiGfxSilkBindable.SpiContextAdapter a => a.Inner,
                global::Wacs.WASI.GFX.DependencyInjection.Context dic => dic.Inner,
                _ => null,
            };
            if (spi is not SilkContext silkCtx)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.ConnectGraphicsContext: context "
                    + "is not a Silk-backed wasi:graphics-context. "
                    + "Cross-backend connections (e.g. RayLib graphics-"
                    + "context + Silk webgpu) are not supported.");
            if (silkCtx.Surface == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.ConnectGraphicsContext: the context "
                    + "has no surface connected yet. Call "
                    + "surface.connect-graphics-context first.");
            var sdlSurface = silkCtx.Surface;
            var window = sdlSurface.NativeWindow;
            if (window == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.ConnectGraphicsContext: the surface "
                    + "has no live SDL window (already disposed?).");

            // Build the macOS Metal surface descriptor. The
            // wgpu-native spec lists Metal, Windows-HWND, Xlib,
            // Wayland, X11/XCB, Android — only macOS is wired
            // here; other platforms throw with a clear pointer.
            if (!System.Runtime.InteropServices.RuntimeInformation
                    .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                throw new PlatformNotSupportedException(
                    "SilkGpuDevice.ConnectGraphicsContext: only macOS "
                    + "(Metal-backed wgpu surface) is currently wired. "
                    + "Windows/Linux paths would mirror this with "
                    + "SurfaceDescriptorFromWindowsHwnd / "
                    + "SurfaceDescriptorFromXlibWindow / "
                    + "SurfaceDescriptorFromWaylandSurface.");

            // The SDL renderer (created up-front for the CPU-path
            // SilkSurface ctor) claims the window's content view's
            // CAMetalLayer. Drop it before SDL_Metal_CreateView so
            // the Metal layer the wgpu surface attaches to is the
            // one actually visible.
            sdlSurface.DropSdlRenderer();

            // SDL_Metal_CreateView / GetLayer must run on the
            // main thread on macOS — AppKit enforces NSView
            // creation + window attachment on the main thread.
            // The CLI's --windowed mode runs the wasm guest on a
            // worker thread, so we route through the dispatcher.
            var sdl = sdlSurface.NativeSdl;
            void* viewOut = null;
            void* layerOut = null;
            sdlSurface.Dispatcher.Invoke(() =>
            {
                viewOut = sdl.MetalCreateView(window);
                if (viewOut != null)
                    layerOut = sdl.MetalGetLayer(viewOut);
            });
            if (viewOut == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.ConnectGraphicsContext: "
                    + "SDL_Metal_CreateView returned null. The SDL "
                    + "window may have been created without metal-"
                    + "compatible flags.");
            var view = viewOut;
            var layer = layerOut;

            var metalDesc = new SurfaceDescriptorFromMetalLayer
            {
                Chain = new ChainedStruct
                {
                    SType = SType.SurfaceDescriptorFromMetalLayer,
                },
                Layer = layer,
            };
            global::Silk.NET.WebGPU.Surface* surface;
            var sd = new SurfaceDescriptor
            {
                NextInChain = (ChainedStruct*)&metalDesc,
            };
            var instance = _backend.EnsureInstance();
            surface = _backend.EnsureApi().InstanceCreateSurface(instance, &sd);
            if (surface == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.ConnectGraphicsContext: wgpu "
                    + "returned a null surface from the Metal-layer "
                    + "descriptor.");

            // Configure: preferred format, RenderAttachment usage,
            // FIFO present mode (vsync), Auto alpha mode.
            var format = _backend.EnsureApi()
                .SurfaceGetPreferredFormat(surface, _adapter);
            uint width = sdlSurface.Width;
            uint height = sdlSurface.Height;
            var cfg = new SurfaceConfiguration
            {
                Device = _device,
                Format = format,
                Usage = TextureUsage.RenderAttachment,
                ViewFormatCount = 0,
                ViewFormats = null,
                AlphaMode = CompositeAlphaMode.Auto,
                Width = width,
                Height = height,
                PresentMode = PresentMode.Fifo,
            };
            _backend.EnsureApi().SurfaceConfigure(surface, &cfg);

            var connection = new SilkGpuConnection(_backend, this,
                surface, view,
                (GenWebgpu.GpuTextureFormat)format, width, height);
            // Replace any prior connection (re-connect on the same
            // context drops the previous swap-chain cleanly).
            silkCtx.GpuConnection?.Dispose();
            silkCtx.GpuConnection = connection;
        }

        // ===== create-* methods =====
        // create-* paths that aren't yet bridged to wgpu-native
        // throw DispatchPending so the binding's wire-form decode
        // still succeeds and the missing translation surfaces as a
        // single message at dispatch time.

        private const string DispatchPending
            = "wgpu-native dispatch path is not implemented for this "
              + "resource. The binding's wire-form decoding works; "
              + "the descriptor → wgpu translation is not wired.";

        public GenWebgpu.IGpuBuffer CreateBuffer(
            GenWebgpu.GpuBufferDescriptor descriptor)
        {
            EnsureLive();
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            var label = descriptor.Label.TryGetValue(out var l) && l != null
                ? l : string.Empty;
            var mappedAtCreation = descriptor.MappedAtCreation
                .TryGetValue(out var m) && m;
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            global::Silk.NET.WebGPU.Buffer* buf;
            fixed (byte* labelPtr = labelBytes)
            {
                var desc = new BufferDescriptor
                {
                    Label = labelPtr,
                    Size = descriptor.Size,
                    Usage = (BufferUsage)descriptor.Usage,
                    MappedAtCreation = mappedAtCreation,
                };
                buf = _backend.EnsureApi().DeviceCreateBuffer(_device, &desc);
            }
            if (buf == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.CreateBuffer: wgpu returned a null "
                    + "buffer for size=" + descriptor.Size + ", usage=0x"
                    + descriptor.Usage.ToString("X")
                    + ". Check the wgpu validation log.");
            return new SilkGpuBuffer(_backend, _device, buf,
                descriptor.Size, descriptor.Usage, label,
                mappedAtCreation);
        }
        public GenWebgpu.IGpuTexture CreateTexture(
            GenWebgpu.GpuTextureDescriptor descriptor)
        {
            EnsureLive();
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            var size = descriptor.Size
                ?? throw new ArgumentNullException(
                    nameof(descriptor) + ".Size");
            uint width = size.Width;
            uint height = size.Height.TryGetValue(out var h) ? h : 1u;
            uint depth = size.DepthOrArrayLayers.TryGetValue(out var d) ? d : 1u;
            uint mipLevels = descriptor.MipLevelCount.TryGetValue(out var ml) ? ml : 1u;
            uint sampleCount = descriptor.SampleCount.TryGetValue(out var sc) ? sc : 1u;
            var dimensionWit = descriptor.Dimension.TryGetValue(out var dim)
                ? dim : GenWebgpu.GpuTextureDimension.D2;
            var label = descriptor.Label.TryGetValue(out var l) && l != null
                ? l : string.Empty;
            var viewFormats = descriptor.ViewFormats
                .TryGetValue(out var vf) && vf != null
                ? vf : System.Array.Empty<GenWebgpu.GpuTextureFormat>();
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            var vfWgpu = new TextureFormat[viewFormats.Length];
            for (int i = 0; i < viewFormats.Length; i++)
                vfWgpu[i] = MapTextureFormat(viewFormats[i]);
            Texture* tex;
            fixed (byte* labelPtr = labelBytes)
            fixed (TextureFormat* vfPtr = vfWgpu)
            {
                var desc = new TextureDescriptor
                {
                    Label = labelPtr,
                    Usage = (TextureUsage)descriptor.Usage,
                    Dimension = (TextureDimension)dimensionWit,
                    Size = new Extent3D
                    {
                        Width = width,
                        Height = height,
                        DepthOrArrayLayers = depth,
                    },
                    Format = MapTextureFormat(descriptor.Format),
                    MipLevelCount = mipLevels,
                    SampleCount = sampleCount,
                    ViewFormatCount = (nuint)viewFormats.Length,
                    ViewFormats = viewFormats.Length > 0 ? vfPtr : null,
                };
                tex = _backend.EnsureApi().DeviceCreateTexture(_device, &desc);
            }
            if (tex == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.CreateTexture: wgpu returned a null "
                    + "texture for " + width + "x" + height + "x" + depth
                    + " format=" + descriptor.Format + ".");
            return new SilkGpuTexture(_backend, tex,
                width, height, depth, mipLevels, sampleCount,
                descriptor.Usage, dimensionWit, descriptor.Format, label);
        }

        public GenWebgpu.IGpuSampler CreateSampler(
            Option<GenWebgpu.GpuSamplerDescriptor> descriptor)
        {
            EnsureLive();
            // wgpu defaults for an all-default sampler: clamp-to-
            // edge addressing, nearest filtering, lod 0..32, no
            // compare, anisotropy 1. Matches WebGPU spec defaults.
            var addrU = AddressMode.ClampToEdge;
            var addrV = AddressMode.ClampToEdge;
            var addrW = AddressMode.ClampToEdge;
            var mag = FilterMode.Nearest;
            var min = FilterMode.Nearest;
            var mip = MipmapFilterMode.Nearest;
            float lodMin = 0f;
            float lodMax = 32f;
            var cmp = CompareFunction.Undefined;
            ushort maxAniso = 1;
            string label = string.Empty;
            if (descriptor.TryGetValue(out var d) && d != null)
            {
                if (d.AddressModeU.TryGetValue(out var au)) addrU = MapAddressMode(au);
                if (d.AddressModeV.TryGetValue(out var av)) addrV = MapAddressMode(av);
                if (d.AddressModeW.TryGetValue(out var aw)) addrW = MapAddressMode(aw);
                if (d.MagFilter.TryGetValue(out var mf)) mag = MapFilterMode(mf);
                if (d.MinFilter.TryGetValue(out var mn)) min = MapFilterMode(mn);
                if (d.MipmapFilter.TryGetValue(out var mip2)) mip = MapMipmapMode(mip2);
                if (d.LodMinClamp.TryGetValue(out var lmin)) lodMin = lmin;
                if (d.LodMaxClamp.TryGetValue(out var lmax)) lodMax = lmax;
                if (d.Compare.TryGetValue(out var c)) cmp = MapCompareFunction(c);
                if (d.MaxAnisotropy.TryGetValue(out var ma)) maxAniso = ma;
                if (d.Label.TryGetValue(out var l) && l != null) label = l;
            }
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            Sampler* s;
            fixed (byte* labelPtr = labelBytes)
            {
                var desc = new SamplerDescriptor
                {
                    Label = labelPtr,
                    AddressModeU = addrU,
                    AddressModeV = addrV,
                    AddressModeW = addrW,
                    MagFilter = mag,
                    MinFilter = min,
                    MipmapFilter = mip,
                    LodMinClamp = lodMin,
                    LodMaxClamp = lodMax,
                    Compare = cmp,
                    MaxAnisotropy = maxAniso,
                };
                s = _backend.EnsureApi().DeviceCreateSampler(_device, &desc);
            }
            if (s == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.CreateSampler: wgpu returned a null "
                    + "sampler.");
            return new SilkGpuSampler(_backend, s, label);
        }

        private static AddressMode MapAddressMode(GenWebgpu.GpuAddressMode m)
            => m switch
            {
                GenWebgpu.GpuAddressMode.ClampToEdge => AddressMode.ClampToEdge,
                GenWebgpu.GpuAddressMode.Repeat => AddressMode.Repeat,
                GenWebgpu.GpuAddressMode.MirrorRepeat => AddressMode.MirrorRepeat,
                _ => AddressMode.ClampToEdge,
            };

        private static FilterMode MapFilterMode(GenWebgpu.GpuFilterMode m)
            => m switch
            {
                GenWebgpu.GpuFilterMode.Nearest => FilterMode.Nearest,
                GenWebgpu.GpuFilterMode.Linear => FilterMode.Linear,
                _ => FilterMode.Nearest,
            };

        private static MipmapFilterMode MapMipmapMode(GenWebgpu.GpuMipmapFilterMode m)
            => m switch
            {
                GenWebgpu.GpuMipmapFilterMode.Nearest => MipmapFilterMode.Nearest,
                GenWebgpu.GpuMipmapFilterMode.Linear => MipmapFilterMode.Linear,
                _ => MipmapFilterMode.Nearest,
            };

        private static CompareFunction MapCompareFunction(GenWebgpu.GpuCompareFunction c)
            => c switch
            {
                GenWebgpu.GpuCompareFunction.Never => CompareFunction.Never,
                GenWebgpu.GpuCompareFunction.Less => CompareFunction.Less,
                GenWebgpu.GpuCompareFunction.Equal => CompareFunction.Equal,
                GenWebgpu.GpuCompareFunction.LessEqual => CompareFunction.LessEqual,
                GenWebgpu.GpuCompareFunction.Greater => CompareFunction.Greater,
                GenWebgpu.GpuCompareFunction.NotEqual => CompareFunction.NotEqual,
                GenWebgpu.GpuCompareFunction.GreaterEqual => CompareFunction.GreaterEqual,
                GenWebgpu.GpuCompareFunction.Always => CompareFunction.Always,
                _ => CompareFunction.Undefined,
            };
        public GenWebgpu.IGpuBindGroupLayout CreateBindGroupLayout(
            GenWebgpu.GpuBindGroupLayoutDescriptor descriptor)
        {
            EnsureLive();
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            var entries = descriptor.Entries ?? Array.Empty<GenWebgpu.GpuBindGroupLayoutEntry>();
            var label = descriptor.Label.TryGetValue(out var l) && l != null
                ? l : string.Empty;
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            var nativeEntries = new BindGroupLayoutEntry[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                var src = entries[i];
                // Build each sub-struct as a local so the writes
                // commit through cleanly. C# ref locals through
                // nested struct fields ARE supposed to write
                // through, but the Silk struct layout (or
                // possibly a struct-blittability quirk) loses
                // the nested writes — verified empirically.
                var buffer = default(BufferBindingLayout);
                if (src.Buffer.TryGetValue(out var bbl) && bbl != null)
                {
                    buffer.Type = bbl.Type.TryGetValue(out var bt)
                        ? bt switch
                        {
                            GenWebgpu.GpuBufferBindingType.Uniform => BufferBindingType.Uniform,
                            GenWebgpu.GpuBufferBindingType.Storage => BufferBindingType.Storage,
                            GenWebgpu.GpuBufferBindingType.ReadOnlyStorage => BufferBindingType.ReadOnlyStorage,
                            _ => BufferBindingType.Undefined,
                        }
                        : BufferBindingType.Uniform;
                    buffer.HasDynamicOffset = bbl.HasDynamicOffset.TryGetValue(out var hdo) && hdo;
                    buffer.MinBindingSize = bbl.MinBindingSize.TryGetValue(out var mbs) ? mbs : 0;
                }
                var sampler = default(SamplerBindingLayout);
                if (src.Sampler.TryGetValue(out var sbl) && sbl != null)
                {
                    sampler.Type = sbl.Type.TryGetValue(out var st)
                        ? st switch
                        {
                            GenWebgpu.GpuSamplerBindingType.Filtering => SamplerBindingType.Filtering,
                            GenWebgpu.GpuSamplerBindingType.NonFiltering => SamplerBindingType.NonFiltering,
                            GenWebgpu.GpuSamplerBindingType.Comparison => SamplerBindingType.Comparison,
                            _ => SamplerBindingType.Undefined,
                        }
                        : SamplerBindingType.Filtering;
                }
                var texture = default(TextureBindingLayout);
                if (src.Texture.TryGetValue(out var tbl) && tbl != null)
                {
                    texture.SampleType = tbl.SampleType.TryGetValue(out var tst)
                        ? tst switch
                        {
                            GenWebgpu.GpuTextureSampleType.Float => TextureSampleType.Float,
                            GenWebgpu.GpuTextureSampleType.UnfilterableFloat => TextureSampleType.UnfilterableFloat,
                            GenWebgpu.GpuTextureSampleType.Depth => TextureSampleType.Depth,
                            GenWebgpu.GpuTextureSampleType.Sint => TextureSampleType.Sint,
                            GenWebgpu.GpuTextureSampleType.Uint => TextureSampleType.Uint,
                            _ => TextureSampleType.Undefined,
                        }
                        : TextureSampleType.Float;
                    texture.ViewDimension = MapViewDimension(
                        tbl.ViewDimension.TryGetValue(out var tvd)
                            ? (GenWebgpu.GpuTextureViewDimension?)tvd : null);
                    texture.Multisampled = tbl.Multisampled.TryGetValue(out var ms) && ms;
                }
                var storageTexture = default(StorageTextureBindingLayout);
                if (src.StorageTexture.TryGetValue(out var stbl) && stbl != null)
                {
                    storageTexture.Access = stbl.Access.TryGetValue(out var sta)
                        ? sta switch
                        {
                            GenWebgpu.GpuStorageTextureAccess.WriteOnly => StorageTextureAccess.WriteOnly,
                            GenWebgpu.GpuStorageTextureAccess.ReadOnly => StorageTextureAccess.ReadOnly,
                            GenWebgpu.GpuStorageTextureAccess.ReadWrite => StorageTextureAccess.ReadWrite,
                            _ => StorageTextureAccess.Undefined,
                        }
                        : StorageTextureAccess.WriteOnly;
                    storageTexture.Format = MapTextureFormat(stbl.Format);
                    storageTexture.ViewDimension = MapViewDimension(
                        stbl.ViewDimension.TryGetValue(out var svd)
                            ? (GenWebgpu.GpuTextureViewDimension?)svd : null);
                }
                nativeEntries[i] = new BindGroupLayoutEntry
                {
                    Binding = src.Binding,
                    Visibility = (ShaderStage)src.Visibility,
                    Buffer = buffer,
                    Sampler = sampler,
                    Texture = texture,
                    StorageTexture = storageTexture,
                };
            }
            BindGroupLayout* bgl;
            fixed (byte* labelPtr = labelBytes)
            fixed (BindGroupLayoutEntry* entriesPtr = nativeEntries)
            {
                var desc = new BindGroupLayoutDescriptor
                {
                    Label = labelPtr,
                    EntryCount = (nuint)entries.Length,
                    Entries = entriesPtr,
                };
                bgl = _backend.EnsureApi().DeviceCreateBindGroupLayout(_device, &desc);
            }
            if (bgl == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.CreateBindGroupLayout: wgpu returned "
                    + "a null layout.");
            return new SilkGpuBindGroupLayout(_backend, bgl, label);
        }

        public GenWebgpu.IGpuPipelineLayout CreatePipelineLayout(
            GenWebgpu.GpuPipelineLayoutDescriptor descriptor)
        {
            EnsureLive();
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            var bgls = descriptor.BindGroupLayouts
                ?? Array.Empty<Option<GenWebgpu.IGpuBindGroupLayout>>();
            var label = descriptor.Label.TryGetValue(out var l) && l != null
                ? l : string.Empty;
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            var nativeArr = new IntPtr[bgls.Length];
            for (int i = 0; i < bgls.Length; i++)
            {
                if (!bgls[i].TryGetValue(out var bgl) || bgl == null)
                {
                    nativeArr[i] = IntPtr.Zero;
                    continue;
                }
                if (bgl is not SilkGpuBindGroupLayout sbgl)
                    throw new Wacs.WASI.GFX.Types.WasiGfxException(
                        "SilkGpuDevice.CreatePipelineLayout: "
                        + $"bindGroupLayouts[{i}] is not a Silk-backed "
                        + "gpu-bind-group-layout.");
                nativeArr[i] = (IntPtr)sbgl.Native;
            }
            PipelineLayout* pl;
            fixed (byte* labelPtr = labelBytes)
            fixed (IntPtr* arrPtr = nativeArr)
            {
                var desc = new PipelineLayoutDescriptor
                {
                    Label = labelPtr,
                    BindGroupLayoutCount = (nuint)bgls.Length,
                    BindGroupLayouts = (BindGroupLayout**)arrPtr,
                };
                pl = _backend.EnsureApi().DeviceCreatePipelineLayout(_device, &desc);
            }
            if (pl == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.CreatePipelineLayout: wgpu returned "
                    + "a null layout.");
            return new SilkGpuPipelineLayout(_backend, pl, label);
        }

        public GenWebgpu.IGpuBindGroup CreateBindGroup(
            GenWebgpu.GpuBindGroupDescriptor descriptor)
        {
            EnsureLive();
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (descriptor.Layout is not SilkGpuBindGroupLayout slayout)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.CreateBindGroup: layout is not a "
                    + "Silk-backed gpu-bind-group-layout.");
            var entries = descriptor.Entries
                ?? Array.Empty<GenWebgpu.GpuBindGroupEntry>();
            var label = descriptor.Label.TryGetValue(out var l) && l != null
                ? l : string.Empty;
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            var nativeEntries = new BindGroupEntry[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                var src = entries[i];
                ref var dst = ref nativeEntries[i];
                dst = default;
                dst.Binding = src.Binding;
                switch (src.Resource)
                {
                    case GenWebgpu.GpuBindingResource.GpuBindingResourceGpuBufferBinding bb:
                        if (bb.Value.Buffer is not SilkGpuBuffer sb)
                            throw new Wacs.WASI.GFX.Types.WasiGfxException(
                                $"SilkGpuDevice.CreateBindGroup: entries[{i}]"
                                + ".buffer is not a Silk-backed buffer.");
                        dst.Buffer = sb.Native;
                        dst.Offset = bb.Value.Offset.TryGetValue(out var off) ? off : 0;
                        dst.Size = bb.Value.Size.TryGetValue(out var sz)
                            ? sz : sb.NativeSize - dst.Offset;
                        break;
                    case GenWebgpu.GpuBindingResource.GpuBindingResourceGpuSampler ss:
                        if (ss.Value is not SilkGpuSampler ssamp)
                            throw new Wacs.WASI.GFX.Types.WasiGfxException(
                                $"SilkGpuDevice.CreateBindGroup: entries[{i}]"
                                + ".sampler is not a Silk-backed sampler.");
                        dst.Sampler = ssamp.Native;
                        break;
                    case GenWebgpu.GpuBindingResource.GpuBindingResourceGpuTextureView tv:
                        if (tv.Value is not SilkGpuTextureView sview)
                            throw new Wacs.WASI.GFX.Types.WasiGfxException(
                                $"SilkGpuDevice.CreateBindGroup: entries[{i}]"
                                + ".texture-view is not a Silk-backed view.");
                        dst.TextureView = sview.Native;
                        break;
                    default:
                        throw new Wacs.WASI.GFX.Types.WasiGfxException(
                            $"SilkGpuDevice.CreateBindGroup: entries[{i}]"
                            + " has an unrecognized binding-resource "
                            + "variant case.");
                }
            }
            BindGroup* bg;
            fixed (byte* labelPtr = labelBytes)
            fixed (BindGroupEntry* entriesPtr = nativeEntries)
            {
                var desc = new BindGroupDescriptor
                {
                    Label = labelPtr,
                    Layout = slayout.Native,
                    EntryCount = (nuint)entries.Length,
                    Entries = entriesPtr,
                };
                bg = _backend.EnsureApi().DeviceCreateBindGroup(_device, &desc);
            }
            if (bg == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.CreateBindGroup: wgpu returned a null "
                    + "bind group.");
            return new SilkGpuBindGroup(_backend, bg, label);
        }

        private static TextureViewDimension MapViewDimension(
            GenWebgpu.GpuTextureViewDimension? d)
            => d switch
            {
                GenWebgpu.GpuTextureViewDimension.D1 => TextureViewDimension.Dimension1D,
                GenWebgpu.GpuTextureViewDimension.D2 => TextureViewDimension.Dimension2D,
                GenWebgpu.GpuTextureViewDimension.D2Array => TextureViewDimension.Dimension2DArray,
                GenWebgpu.GpuTextureViewDimension.Cube => TextureViewDimension.DimensionCube,
                GenWebgpu.GpuTextureViewDimension.CubeArray => TextureViewDimension.DimensionCubeArray,
                GenWebgpu.GpuTextureViewDimension.D3 => TextureViewDimension.Dimension3D,
                _ => TextureViewDimension.DimensionUndefined,
            };

        internal static TextureFormat MapTextureFormat(
            GenWebgpu.GpuTextureFormat f)
            // WIT and wgpu both follow the WebGPU spec format
            // ordering, but wgpu prepends an Undefined=0 case
            // that the WIT enum doesn't have. Shift by +1 so
            // WIT Rgba8unorm (17) lands on wgpu Rgba8Unorm (18).
            => (TextureFormat)((int)f + 1);
        public GenWebgpu.IGpuShaderModule CreateShaderModule(
            GenWebgpu.GpuShaderModuleDescriptor descriptor)
        {
            EnsureLive();
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            // wgpu's ShaderModuleDescriptor is the outer descriptor;
            // the WGSL source is provided through a chained
            // ShaderModuleWGSLDescriptor.
            var label = descriptor.Label.TryGetValue(out var l) && l != null
                ? l : string.Empty;
            var code = descriptor.Code ?? string.Empty;
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            var codeBytes = System.Text.Encoding.UTF8.GetBytes(code + "\0");
            ShaderModule* sm;
            fixed (byte* labelPtr = labelBytes)
            fixed (byte* codePtr = codeBytes)
            {
                var wgsl = new ShaderModuleWGSLDescriptor
                {
                    Chain = new ChainedStruct
                    {
                        SType = SType.ShaderModuleWgslDescriptor,
                    },
                    Code = codePtr,
                };
                var desc = new ShaderModuleDescriptor
                {
                    Label = labelPtr,
                    NextInChain = (ChainedStruct*)&wgsl,
                };
                sm = _backend.EnsureApi().DeviceCreateShaderModule(_device, &desc);
            }
            if (sm == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.CreateShaderModule: wgpu returned "
                    + "a null shader module. Check wgpu's WGSL "
                    + "compilation log.");
            return new SilkGpuShaderModule(_backend, _device, sm, label);
        }
        public GenWebgpu.IGpuComputePipeline CreateComputePipeline(
            GenWebgpu.GpuComputePipelineDescriptor descriptor)
        {
            EnsureLive();
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            var stage = descriptor.Compute
                ?? throw new ArgumentNullException(
                    nameof(descriptor) + ".Compute");
            if (stage.Module is not SilkGpuShaderModule smod)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.CreateComputePipeline: programmable-"
                    + "stage.module is not a Silk-backed shader-module.");
            var entryPoint = stage.EntryPoint.TryGetValue(out var ep) && ep != null
                ? ep : "main";
            var label = descriptor.Label.TryGetValue(out var l) && l != null
                ? l : string.Empty;
            PipelineLayout* nativeLayout = null;
            if (descriptor.Layout
                is GenWebgpu.GpuLayoutMode.GpuLayoutModeSpecific spec)
            {
                if (spec.Value is not SilkGpuPipelineLayout spl)
                    throw new Wacs.WASI.GFX.Types.WasiGfxException(
                        "SilkGpuDevice.CreateComputePipeline: layout."
                        + "specific is not a Silk-backed pipeline-layout.");
                nativeLayout = spl.Native;
            }
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            var epBytes = System.Text.Encoding.UTF8.GetBytes(entryPoint + "\0");
            ComputePipeline* cp;
            fixed (byte* labelPtr = labelBytes)
            fixed (byte* epPtr = epBytes)
            {
                var desc = new ComputePipelineDescriptor
                {
                    Label = labelPtr,
                    Layout = nativeLayout,
                    Compute = new ProgrammableStageDescriptor
                    {
                        Module = smod.Native,
                        EntryPoint = epPtr,
                        ConstantCount = 0,
                        Constants = null,
                    },
                };
                cp = _backend.EnsureApi().DeviceCreateComputePipeline(_device, &desc);
            }
            if (cp == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.CreateComputePipeline: wgpu returned "
                    + "a null pipeline.");
            return new SilkGpuComputePipeline(_backend, cp, label);
        }
        public GenWebgpu.IGpuRenderPipeline CreateRenderPipeline(
            GenWebgpu.GpuRenderPipelineDescriptor descriptor)
        {
            EnsureLive();
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            return CreateRenderPipelineInner(descriptor);
        }

        // Owns the lifetime of all the byte[]/array buffers that
        // need to be pinned across DeviceCreateRenderPipeline.
        // The wgpu call is synchronous so a stack of fixed-blocks
        // is fine; the helper consolidates them in one place so
        // CreateRenderPipeline + CreateRenderPipelineAsync share
        // the dispatch body.
        private GenWebgpu.IGpuRenderPipeline CreateRenderPipelineInner(
            GenWebgpu.GpuRenderPipelineDescriptor descriptor)
        {
            var label = descriptor.Label.TryGetValue(out var l) && l != null
                ? l : string.Empty;
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;

            // ----- vertex state -----
            var vertex = descriptor.Vertex
                ?? throw new ArgumentNullException(
                    nameof(descriptor) + ".Vertex");
            if (vertex.Module is not SilkGpuShaderModule vmod)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "CreateRenderPipeline: vertex.module is not Silk-backed.");
            var vertexEntry = vertex.EntryPoint.TryGetValue(out var ve) && ve != null
                ? ve : "main";
            var vertexEntryBytes = System.Text.Encoding.UTF8.GetBytes(vertexEntry + "\0");
            // Flatten vertex buffers list — None entries become
            // wgpu's "buffer slot unused" via stepMode =
            // VertexBufferNotUsed. wgpu's wgpu-native distinguishes
            // None slots that way; spec WebGPU uses null entries
            // in the array.
            var vertexBuffers = vertex.Buffers.TryGetValue(out var vbs) && vbs != null
                ? vbs : Array.Empty<Option<GenWebgpu.GpuVertexBufferLayout>>();
            var nativeBuffers = new VertexBufferLayout[vertexBuffers.Length];
            // We need to keep the per-buffer attributes arrays
            // pinned for the duration of the create call. Hold
            // them in a list of byte[] + pinned attribute arrays.
            var attributeArrays = new VertexAttribute[vertexBuffers.Length][];
            for (int i = 0; i < vertexBuffers.Length; i++)
            {
                if (!vertexBuffers[i].TryGetValue(out var vb) || vb == null)
                {
                    nativeBuffers[i] = new VertexBufferLayout
                    {
                        StepMode = VertexStepMode.VertexBufferNotUsed,
                    };
                    attributeArrays[i] = Array.Empty<VertexAttribute>();
                    continue;
                }
                var srcAttrs = vb.Attributes ?? Array.Empty<GenWebgpu.GpuVertexAttribute>();
                var natAttrs = new VertexAttribute[srcAttrs.Length];
                for (int j = 0; j < srcAttrs.Length; j++)
                {
                    natAttrs[j] = new VertexAttribute
                    {
                        Format = MapVertexFormat(srcAttrs[j].Format),
                        Offset = srcAttrs[j].Offset,
                        ShaderLocation = srcAttrs[j].ShaderLocation,
                    };
                }
                attributeArrays[i] = natAttrs;
                nativeBuffers[i] = new VertexBufferLayout
                {
                    ArrayStride = vb.ArrayStride,
                    StepMode = vb.StepMode.TryGetValue(out var sm)
                        ? (sm == GenWebgpu.GpuVertexStepMode.Vertex
                            ? VertexStepMode.Vertex
                            : VertexStepMode.Instance)
                        : VertexStepMode.Vertex,
                    AttributeCount = (nuint)natAttrs.Length,
                    // Attributes pointer is patched in inside the
                    // fixed scope below — we can't set it here
                    // because the array address isn't stable yet.
                };
            }

            // ----- primitive state -----
            var prim = new PrimitiveState
            {
                Topology = PrimitiveTopology.TriangleList,
                StripIndexFormat = IndexFormat.Undefined,
                FrontFace = FrontFace.Ccw,
                CullMode = CullMode.None,
            };
            if (descriptor.Primitive.TryGetValue(out var pp) && pp != null)
            {
                if (pp.Topology.TryGetValue(out var topo))
                    prim.Topology = MapTopology(topo);
                if (pp.StripIndexFormat.TryGetValue(out var sif))
                    prim.StripIndexFormat = MapIndexFormat(sif);
                if (pp.FrontFace.TryGetValue(out var ff))
                    prim.FrontFace = ff == GenWebgpu.GpuFrontFace.Ccw
                        ? FrontFace.Ccw : FrontFace.CW;
                if (pp.CullMode.TryGetValue(out var cm))
                    prim.CullMode = cm switch
                    {
                        GenWebgpu.GpuCullMode.None => CullMode.None,
                        GenWebgpu.GpuCullMode.Front => CullMode.Front,
                        GenWebgpu.GpuCullMode.Back => CullMode.Back,
                        _ => CullMode.None,
                    };
            }

            // ----- depth-stencil state -----
            DepthStencilState dss = default;
            bool hasDss = false;
            if (descriptor.DepthStencil.TryGetValue(out var ds) && ds != null)
            {
                dss.Format = MapTextureFormat(ds.Format);
                dss.DepthWriteEnabled = ds.DepthWriteEnabled
                    .TryGetValue(out var dwe) && dwe;
                dss.DepthCompare = ds.DepthCompare.TryGetValue(out var dc)
                    ? MapCompareFunction(dc) : CompareFunction.Undefined;
                dss.StencilFront = BuildStencilFace(ds.StencilFront);
                dss.StencilBack = BuildStencilFace(ds.StencilBack);
                dss.StencilReadMask = ds.StencilReadMask.TryGetValue(out var srm)
                    ? srm : 0xFFFFFFFFu;
                dss.StencilWriteMask = ds.StencilWriteMask.TryGetValue(out var swm)
                    ? swm : 0xFFFFFFFFu;
                dss.DepthBias = ds.DepthBias.TryGetValue(out var db) ? db : 0;
                dss.DepthBiasSlopeScale = ds.DepthBiasSlopeScale
                    .TryGetValue(out var dbss) ? dbss : 0f;
                dss.DepthBiasClamp = ds.DepthBiasClamp
                    .TryGetValue(out var dbc) ? dbc : 0f;
                hasDss = true;
            }

            // ----- multisample state -----
            var ms = new MultisampleState
            {
                Count = 1,
                Mask = 0xFFFFFFFFu,
                AlphaToCoverageEnabled = false,
            };
            if (descriptor.Multisample.TryGetValue(out var msv) && msv != null)
            {
                if (msv.Count.TryGetValue(out var mc)) ms.Count = mc;
                if (msv.Mask.TryGetValue(out var mm)) ms.Mask = mm;
                if (msv.AlphaToCoverageEnabled.TryGetValue(out var atc))
                    ms.AlphaToCoverageEnabled = atc;
            }

            // ----- fragment state -----
            FragmentState fs = default;
            bool hasFs = false;
            byte[]? fragEntryBytes = null;
            ColorTargetState[]? nativeTargets = null;
            BlendState[]? blendStates = null;
            SilkGpuShaderModule? fragMod = null;
            if (descriptor.Fragment.TryGetValue(out var f2) && f2 != null)
            {
                if (f2.Module is not SilkGpuShaderModule fmod)
                    throw new Wacs.WASI.GFX.Types.WasiGfxException(
                        "CreateRenderPipeline: fragment.module is not Silk-backed.");
                fragMod = fmod;
                var fragEntry = f2.EntryPoint.TryGetValue(out var fe) && fe != null
                    ? fe : "main";
                fragEntryBytes = System.Text.Encoding.UTF8.GetBytes(fragEntry + "\0");
                var targets = f2.Targets ?? Array.Empty<Option<GenWebgpu.GpuColorTargetState>>();
                nativeTargets = new ColorTargetState[targets.Length];
                blendStates = new BlendState[targets.Length];
                for (int i = 0; i < targets.Length; i++)
                {
                    if (!targets[i].TryGetValue(out var t) || t == null)
                    {
                        nativeTargets[i] = default;
                        continue;
                    }
                    nativeTargets[i] = new ColorTargetState
                    {
                        Format = MapTextureFormat(t.Format),
                        WriteMask = t.WriteMask.TryGetValue(out var wm)
                            ? (ColorWriteMask)wm : ColorWriteMask.All,
                    };
                    if (t.Blend.TryGetValue(out var bs) && bs != null)
                    {
                        blendStates[i] = new BlendState
                        {
                            Color = MapBlendComponent(bs.Color),
                            Alpha = MapBlendComponent(bs.Alpha),
                        };
                    }
                }
                hasFs = true;
            }

            // ----- layout -----
            PipelineLayout* nativeLayout = null;
            if (descriptor.Layout
                is GenWebgpu.GpuLayoutMode.GpuLayoutModeSpecific spec)
            {
                if (spec.Value is not SilkGpuPipelineLayout spl)
                    throw new Wacs.WASI.GFX.Types.WasiGfxException(
                        "CreateRenderPipeline: layout.specific is not "
                        + "a Silk-backed pipeline-layout.");
                nativeLayout = spl.Native;
            }

            RenderPipeline* pipeline;
            // Big fixed-stack: pin every backing array used by the
            // descriptor + sub-records before the wgpu call.
            fixed (byte* labelPtr = labelBytes)
            fixed (byte* vEntryPtr = vertexEntryBytes)
            fixed (byte* fEntryPtr = fragEntryBytes)
            fixed (VertexBufferLayout* vbufPtr = nativeBuffers)
            fixed (ColorTargetState* tgtsPtr = nativeTargets)
            fixed (BlendState* blendPtr = blendStates)
            {
                // Patch in the per-buffer attribute pointers now
                // that the destination buffer array is pinned.
                var attrPins = new System.Runtime.InteropServices.GCHandle[
                    attributeArrays.Length];
                try
                {
                    for (int i = 0; i < attributeArrays.Length; i++)
                    {
                        attrPins[i] = System.Runtime.InteropServices.GCHandle
                            .Alloc(attributeArrays[i],
                                System.Runtime.InteropServices.GCHandleType.Pinned);
                        vbufPtr[i].Attributes = (VertexAttribute*)
                            attrPins[i].AddrOfPinnedObject();
                    }
                    // Patch in per-target blend pointers similarly —
                    // wgpu wants Blend pointers, not embedded structs.
                    if (nativeTargets != null && blendStates != null)
                    {
                        for (int i = 0; i < nativeTargets.Length; i++)
                        {
                            if (blendStates[i].Color.SrcFactor == BlendFactor.Zero
                                && blendStates[i].Color.DstFactor == BlendFactor.Zero
                                && blendStates[i].Alpha.SrcFactor == BlendFactor.Zero
                                && blendStates[i].Alpha.DstFactor == BlendFactor.Zero)
                            {
                                // No blend was set on this target; leave
                                // Blend pointer null (wgpu's "no blending").
                                nativeTargets[i].Blend = null;
                            }
                            else
                            {
                                nativeTargets[i].Blend = &blendPtr[i];
                            }
                        }
                    }

                    var vState = new VertexState
                    {
                        Module = vmod.Native,
                        EntryPoint = vEntryPtr,
                        ConstantCount = 0,
                        Constants = null,
                        BufferCount = (nuint)nativeBuffers.Length,
                        Buffers = nativeBuffers.Length > 0 ? vbufPtr : null,
                    };
                    if (hasFs && fragMod != null)
                    {
                        fs = new FragmentState
                        {
                            Module = fragMod.Native,
                            EntryPoint = fEntryPtr,
                            ConstantCount = 0,
                            Constants = null,
                            TargetCount = (nuint)(nativeTargets?.Length ?? 0),
                            Targets = (nativeTargets?.Length ?? 0) > 0 ? tgtsPtr : null,
                        };
                    }
                    var desc = new RenderPipelineDescriptor
                    {
                        Label = labelPtr,
                        Layout = nativeLayout,
                        Vertex = vState,
                        Primitive = prim,
                        DepthStencil = hasDss ? &dss : null,
                        Multisample = ms,
                        Fragment = hasFs ? &fs : null,
                    };
                    pipeline = _backend.EnsureApi()
                        .DeviceCreateRenderPipeline(_device, &desc);
                }
                finally
                {
                    for (int i = 0; i < attrPins.Length; i++)
                        if (attrPins[i].IsAllocated)
                            attrPins[i].Free();
                }
            }
            if (pipeline == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "CreateRenderPipeline: wgpu returned a null pipeline.");
            return new SilkGpuRenderPipeline(_backend, pipeline, label);
        }

        public Result<GenWebgpu.IGpuRenderPipeline, GenWebgpu.CreatePipelineError>
            CreateRenderPipelineAsync(GenWebgpu.GpuRenderPipelineDescriptor descriptor)
        {
            // wgpu-native's async variant exists but is callback-
            // driven; the sync path completes synchronously and
            // surfaces compile errors via error-scope. Wrap.
            try
            {
                var pipeline = CreateRenderPipeline(descriptor);
                return Result<GenWebgpu.IGpuRenderPipeline, GenWebgpu.CreatePipelineError>
                    .FromOk(pipeline);
            }
            catch (System.Exception ex)
            {
                return Result<GenWebgpu.IGpuRenderPipeline, GenWebgpu.CreatePipelineError>
                    .FromErr(new GenWebgpu.CreatePipelineError
                    {
                        Kind = new GenWebgpu.CreatePipelineErrorKind
                            .CreatePipelineErrorKindGpuPipelineError(
                                GenWebgpu.GpuPipelineErrorReason.Internal),
                        Message = ex.Message ?? "create-render-pipeline failed",
                    });
            }
        }

        private static StencilFaceState BuildStencilFace(
            Option<GenWebgpu.GpuStencilFaceState> opt)
        {
            var face = new StencilFaceState
            {
                Compare = CompareFunction.Always,
                FailOp = StencilOperation.Keep,
                DepthFailOp = StencilOperation.Keep,
                PassOp = StencilOperation.Keep,
            };
            if (!opt.TryGetValue(out var v) || v == null) return face;
            if (v.Compare.TryGetValue(out var c))
                face.Compare = MapCompareFunction(c);
            if (v.FailOp.TryGetValue(out var fo))
                face.FailOp = MapStencilOp(fo);
            if (v.DepthFailOp.TryGetValue(out var dfo))
                face.DepthFailOp = MapStencilOp(dfo);
            if (v.PassOp.TryGetValue(out var po))
                face.PassOp = MapStencilOp(po);
            return face;
        }

        private static BlendComponent MapBlendComponent(GenWebgpu.GpuBlendComponent c)
        {
            var bc = new BlendComponent
            {
                Operation = BlendOperation.Add,
                SrcFactor = BlendFactor.One,
                DstFactor = BlendFactor.Zero,
            };
            if (c == null) return bc;
            if (c.Operation.TryGetValue(out var op))
                bc.Operation = MapBlendOperation(op);
            if (c.SrcFactor.TryGetValue(out var sf))
                bc.SrcFactor = MapBlendFactor(sf);
            if (c.DstFactor.TryGetValue(out var df))
                bc.DstFactor = MapBlendFactor(df);
            return bc;
        }

        private static PrimitiveTopology MapTopology(GenWebgpu.GpuPrimitiveTopology t)
            => t switch
            {
                GenWebgpu.GpuPrimitiveTopology.PointList => PrimitiveTopology.PointList,
                GenWebgpu.GpuPrimitiveTopology.LineList => PrimitiveTopology.LineList,
                GenWebgpu.GpuPrimitiveTopology.LineStrip => PrimitiveTopology.LineStrip,
                GenWebgpu.GpuPrimitiveTopology.TriangleList => PrimitiveTopology.TriangleList,
                GenWebgpu.GpuPrimitiveTopology.TriangleStrip => PrimitiveTopology.TriangleStrip,
                _ => PrimitiveTopology.TriangleList,
            };

        private static IndexFormat MapIndexFormat(GenWebgpu.GpuIndexFormat f)
            => f == GenWebgpu.GpuIndexFormat.Uint16
                ? IndexFormat.Uint16 : IndexFormat.Uint32;

        private static BlendOperation MapBlendOperation(GenWebgpu.GpuBlendOperation o)
            => o switch
            {
                GenWebgpu.GpuBlendOperation.Add => BlendOperation.Add,
                GenWebgpu.GpuBlendOperation.Subtract => BlendOperation.Subtract,
                GenWebgpu.GpuBlendOperation.ReverseSubtract => BlendOperation.ReverseSubtract,
                GenWebgpu.GpuBlendOperation.Min => BlendOperation.Min,
                GenWebgpu.GpuBlendOperation.Max => BlendOperation.Max,
                _ => BlendOperation.Add,
            };

        private static BlendFactor MapBlendFactor(GenWebgpu.GpuBlendFactor f)
            => f switch
            {
                GenWebgpu.GpuBlendFactor.Zero => BlendFactor.Zero,
                GenWebgpu.GpuBlendFactor.One => BlendFactor.One,
                GenWebgpu.GpuBlendFactor.Src => BlendFactor.Src,
                GenWebgpu.GpuBlendFactor.OneMinusSrc => BlendFactor.OneMinusSrc,
                GenWebgpu.GpuBlendFactor.SrcAlpha => BlendFactor.SrcAlpha,
                GenWebgpu.GpuBlendFactor.OneMinusSrcAlpha => BlendFactor.OneMinusSrcAlpha,
                GenWebgpu.GpuBlendFactor.Dst => BlendFactor.Dst,
                GenWebgpu.GpuBlendFactor.OneMinusDst => BlendFactor.OneMinusDst,
                GenWebgpu.GpuBlendFactor.DstAlpha => BlendFactor.DstAlpha,
                GenWebgpu.GpuBlendFactor.OneMinusDstAlpha => BlendFactor.OneMinusDstAlpha,
                GenWebgpu.GpuBlendFactor.SrcAlphaSaturated => BlendFactor.SrcAlphaSaturated,
                GenWebgpu.GpuBlendFactor.Constant => BlendFactor.Constant,
                GenWebgpu.GpuBlendFactor.OneMinusConstant => BlendFactor.OneMinusConstant,
                _ => BlendFactor.Zero,
            };

        private static StencilOperation MapStencilOp(GenWebgpu.GpuStencilOperation o)
            => o switch
            {
                GenWebgpu.GpuStencilOperation.Keep => StencilOperation.Keep,
                GenWebgpu.GpuStencilOperation.Zero => StencilOperation.Zero,
                GenWebgpu.GpuStencilOperation.Replace => StencilOperation.Replace,
                GenWebgpu.GpuStencilOperation.Invert => StencilOperation.Invert,
                GenWebgpu.GpuStencilOperation.IncrementClamp => StencilOperation.IncrementClamp,
                GenWebgpu.GpuStencilOperation.DecrementClamp => StencilOperation.DecrementClamp,
                GenWebgpu.GpuStencilOperation.IncrementWrap => StencilOperation.IncrementWrap,
                GenWebgpu.GpuStencilOperation.DecrementWrap => StencilOperation.DecrementWrap,
                _ => StencilOperation.Keep,
            };

        private static VertexFormat MapVertexFormat(GenWebgpu.GpuVertexFormat f)
            => f switch
            {
                // Map by name. WIT has scalar 8-bit / 16-bit single-
                // channel formats that wgpu-native doesn't support;
                // those fall through to Undefined and wgpu will
                // reject at validation with a clear error.
                GenWebgpu.GpuVertexFormat.Uint8x2 => VertexFormat.Uint8x2,
                GenWebgpu.GpuVertexFormat.Uint8x4 => VertexFormat.Uint8x4,
                GenWebgpu.GpuVertexFormat.Sint8x2 => VertexFormat.Sint8x2,
                GenWebgpu.GpuVertexFormat.Sint8x4 => VertexFormat.Sint8x4,
                GenWebgpu.GpuVertexFormat.Unorm8x2 => VertexFormat.Unorm8x2,
                GenWebgpu.GpuVertexFormat.Unorm8x4 => VertexFormat.Unorm8x4,
                GenWebgpu.GpuVertexFormat.Snorm8x2 => VertexFormat.Snorm8x2,
                GenWebgpu.GpuVertexFormat.Snorm8x4 => VertexFormat.Snorm8x4,
                GenWebgpu.GpuVertexFormat.Uint16x2 => VertexFormat.Uint16x2,
                GenWebgpu.GpuVertexFormat.Uint16x4 => VertexFormat.Uint16x4,
                GenWebgpu.GpuVertexFormat.Sint16x2 => VertexFormat.Sint16x2,
                GenWebgpu.GpuVertexFormat.Sint16x4 => VertexFormat.Sint16x4,
                GenWebgpu.GpuVertexFormat.Unorm16x2 => VertexFormat.Unorm16x2,
                GenWebgpu.GpuVertexFormat.Unorm16x4 => VertexFormat.Unorm16x4,
                GenWebgpu.GpuVertexFormat.Snorm16x2 => VertexFormat.Snorm16x2,
                GenWebgpu.GpuVertexFormat.Snorm16x4 => VertexFormat.Snorm16x4,
                GenWebgpu.GpuVertexFormat.Float16x2 => VertexFormat.Float16x2,
                GenWebgpu.GpuVertexFormat.Float16x4 => VertexFormat.Float16x4,
                GenWebgpu.GpuVertexFormat.Float32 => VertexFormat.Float32,
                GenWebgpu.GpuVertexFormat.Float32x2 => VertexFormat.Float32x2,
                GenWebgpu.GpuVertexFormat.Float32x3 => VertexFormat.Float32x3,
                GenWebgpu.GpuVertexFormat.Float32x4 => VertexFormat.Float32x4,
                GenWebgpu.GpuVertexFormat.Uint32 => VertexFormat.Uint32,
                GenWebgpu.GpuVertexFormat.Uint32x2 => VertexFormat.Uint32x2,
                GenWebgpu.GpuVertexFormat.Uint32x3 => VertexFormat.Uint32x3,
                GenWebgpu.GpuVertexFormat.Uint32x4 => VertexFormat.Uint32x4,
                GenWebgpu.GpuVertexFormat.Sint32 => VertexFormat.Sint32,
                GenWebgpu.GpuVertexFormat.Sint32x2 => VertexFormat.Sint32x2,
                GenWebgpu.GpuVertexFormat.Sint32x3 => VertexFormat.Sint32x3,
                GenWebgpu.GpuVertexFormat.Sint32x4 => VertexFormat.Sint32x4,
                _ => VertexFormat.Undefined,
            };
        public Result<GenWebgpu.IGpuComputePipeline, GenWebgpu.CreatePipelineError>
            CreateComputePipelineAsync(GenWebgpu.GpuComputePipelineDescriptor descriptor)
        {
            // wgpu-native's DeviceCreateComputePipelineAsync exists
            // but the sync DeviceCreateComputePipeline completes
            // synchronously and surfaces compilation errors via
            // PopErrorScope. Wrap the sync path; if it throws
            // (e.g. shader compile failure surfaced as a host
            // exception), surface as a pipeline-error Err.
            try
            {
                var pipeline = CreateComputePipeline(descriptor);
                return Result<GenWebgpu.IGpuComputePipeline, GenWebgpu.CreatePipelineError>
                    .FromOk(pipeline);
            }
            catch (System.Exception ex)
            {
                return Result<GenWebgpu.IGpuComputePipeline, GenWebgpu.CreatePipelineError>
                    .FromErr(new GenWebgpu.CreatePipelineError
                    {
                        Kind = new GenWebgpu.CreatePipelineErrorKind
                            .CreatePipelineErrorKindGpuPipelineError(
                                GenWebgpu.GpuPipelineErrorReason.Internal),
                        Message = ex.Message ?? "create-compute-pipeline failed",
                    });
            }
        }
        public GenWebgpu.IGpuCommandEncoder CreateCommandEncoder(
            Option<GenWebgpu.GpuCommandEncoderDescriptor> descriptor)
        {
            EnsureLive();
            string label = string.Empty;
            if (descriptor.TryGetValue(out var d) && d != null)
                label = d.Label.TryGetValue(out var l) && l != null
                    ? l : string.Empty;
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            CommandEncoder* enc;
            fixed (byte* labelPtr = labelBytes)
            {
                var desc = new CommandEncoderDescriptor
                {
                    Label = labelPtr,
                };
                enc = _backend.EnsureApi().DeviceCreateCommandEncoder(_device, &desc);
            }
            if (enc == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.CreateCommandEncoder: wgpu returned "
                    + "a null command encoder.");
            return new SilkGpuCommandEncoder(_backend, enc, label);
        }
        public GenWebgpu.IGpuRenderBundleEncoder CreateRenderBundleEncoder(
            GenWebgpu.GpuRenderBundleEncoderDescriptor descriptor)
        {
            EnsureLive();
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            var label = descriptor.Label.TryGetValue(out var l) && l != null
                ? l : string.Empty;
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            // color-formats: list<option<format>>. None entries map
            // to TextureFormat.Undefined (wgpu's "no color
            // attachment at this slot" marker).
            var colorFormats = descriptor.ColorFormats
                ?? Array.Empty<Option<GenWebgpu.GpuTextureFormat>>();
            var nativeFormats = new TextureFormat[colorFormats.Length];
            for (int i = 0; i < colorFormats.Length; i++)
                nativeFormats[i] = colorFormats[i].TryGetValue(out var f)
                    ? MapTextureFormat(f) : TextureFormat.Undefined;
            RenderBundleEncoder* enc;
            fixed (byte* labelPtr = labelBytes)
            fixed (TextureFormat* formatsPtr = nativeFormats)
            {
                var desc = new RenderBundleEncoderDescriptor
                {
                    Label = labelPtr,
                    ColorFormatCount = (nuint)nativeFormats.Length,
                    ColorFormats = nativeFormats.Length > 0 ? formatsPtr : null,
                    DepthStencilFormat = descriptor.DepthStencilFormat
                        .TryGetValue(out var dsf)
                        ? MapTextureFormat(dsf) : TextureFormat.Undefined,
                    SampleCount = descriptor.SampleCount.TryGetValue(out var sc)
                        ? sc : 1u,
                    DepthReadOnly = descriptor.DepthReadOnly.TryGetValue(out var dro)
                        && dro,
                    StencilReadOnly = descriptor.StencilReadOnly.TryGetValue(out var sro)
                        && sro,
                };
                enc = _backend.EnsureApi()
                    .DeviceCreateRenderBundleEncoder(_device, &desc);
            }
            if (enc == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.CreateRenderBundleEncoder: wgpu "
                    + "returned a null encoder.");
            return new SilkGpuRenderBundleEncoder(_backend, enc, label);
        }
        public Result<GenWebgpu.IGpuQuerySet, GenWebgpu.CreateQuerySetError>
            CreateQuerySet(GenWebgpu.GpuQuerySetDescriptor descriptor)
        {
            EnsureLive();
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            var label = descriptor.Label.TryGetValue(out var l) && l != null
                ? l : string.Empty;
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            QuerySet* qs;
            fixed (byte* labelPtr = labelBytes)
            {
                var desc = new QuerySetDescriptor
                {
                    Label = labelPtr,
                    Type = descriptor.Type == GenWebgpu.GpuQueryType.Timestamp
                        ? QueryType.Timestamp : QueryType.Occlusion,
                    Count = descriptor.Count,
                };
                qs = _backend.EnsureApi().DeviceCreateQuerySet(_device, &desc);
            }
            if (qs == null)
                return Result<GenWebgpu.IGpuQuerySet, GenWebgpu.CreateQuerySetError>
                    .FromErr(new GenWebgpu.CreateQuerySetError
                    {
                        Kind = new GenWebgpu.CreateQuerySetErrorKind
                            .CreateQuerySetErrorKindTypeError(),
                        Message = "DeviceCreateQuerySet returned null.",
                    });
            return Result<GenWebgpu.IGpuQuerySet, GenWebgpu.CreateQuerySetError>
                .FromOk(new SilkGpuQuerySet(_backend, qs,
                    descriptor.Type, descriptor.Count, label));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queueWrapper?.Dispose();
            _queueWrapper = null;
            _queue = null;
            if (_device != null)
            {
                _backend.EnsureApi().DeviceRelease(_device);
                _device = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _device == null)
                throw new ObjectDisposedException(nameof(SilkGpuDevice));
        }
    }

    /// <summary>
    /// wgpu-backed queue wrapper. Hosts submit / write-buffer /
    /// on-submitted-work-done over a wgpu <c>Queue*</c>.
    /// </summary>
    internal sealed unsafe class SilkGpuQueue : GenWebgpu.IGpuQueue, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private readonly Device* _device;
        private Queue* _queue;
        private string _label = string.Empty;
        private bool _disposed;

        public SilkGpuQueue(SilkGpuBackend backend, Device* device, Queue* queue)
        {
            _backend = backend;
            _device = device;
            _queue = queue;
        }

        internal Queue* Native => _queue;

        public void Submit(GenWebgpu.IGpuCommandBuffer[] commandBuffers)
        {
            if (_disposed || _queue == null)
                throw new ObjectDisposedException(nameof(SilkGpuQueue));
            commandBuffers ??= Array.Empty<GenWebgpu.IGpuCommandBuffer>();
            var count = commandBuffers.Length;
            // wgpu wants a contiguous CommandBuffer* array. Stackalloc
            // covers reasonable submit batch sizes (a few CBs);
            // the WIT spec allows arbitrary list length, so degrade
            // gracefully to a heap array for large batches.
            if (count <= 16)
            {
                CommandBuffer** stack = stackalloc CommandBuffer*[count];
                for (int i = 0; i < count; i++)
                    stack[i] = ExtractNative(commandBuffers[i], i);
                _backend.EnsureApi().QueueSubmit(_queue, (uint)count, stack);
            }
            else
            {
                var arr = new IntPtr[count];
                for (int i = 0; i < count; i++)
                    arr[i] = (IntPtr)ExtractNative(commandBuffers[i], i);
                fixed (IntPtr* p = arr)
                {
                    _backend.EnsureApi().QueueSubmit(
                        _queue, (uint)count, (CommandBuffer**)p);
                }
            }
        }

        private static CommandBuffer* ExtractNative(
            GenWebgpu.IGpuCommandBuffer cb, int index)
        {
            if (cb is not SilkGpuCommandBuffer s)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    $"SilkGpuQueue.Submit: commandBuffers[{index}] is "
                    + "not a Silk-backed gpu-command-buffer.");
            return s.Native;
        }

        public void OnSubmittedWorkDone()
        {
            if (_disposed || _queue == null)
                throw new ObjectDisposedException(nameof(SilkGpuQueue));
            if (_device == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuQueue.OnSubmittedWorkDone: queue is not "
                    + "associated with a device (constructed via a "
                    + "non-standard path).");
            bool done = false;
            var cb = new PfnQueueWorkDoneCallback(
                (QueueWorkDoneStatus _, void* _) => done = true);
            _backend.EnsureApi().QueueOnSubmittedWorkDone(_queue, cb, null);
            _backend.PollUntilDone(_device, () => done);
        }

        public Result<Unit, GenWebgpu.WriteBufferError> WriteBufferWithCopy(
            GenWebgpu.IGpuBuffer buffer, ulong bufferOffset,
            byte[] data, Option<ulong> dataOffset, Option<ulong> size)
        {
            if (_disposed || _queue == null)
                throw new ObjectDisposedException(nameof(SilkGpuQueue));
            if (buffer is not SilkGpuBuffer sb)
                return Result<Unit, GenWebgpu.WriteBufferError>.FromErr(
                    new GenWebgpu.WriteBufferError
                    {
                        Kind = new GenWebgpu.WriteBufferErrorKind
                            .WriteBufferErrorKindOperationError(),
                        Message = "WriteBufferWithCopy: buffer is not a "
                            + "Silk-backed gpu-buffer; cross-backend "
                            + "writes are not supported.",
                    });
            data ??= Array.Empty<byte>();
            var srcOff = dataOffset.TryGetValue(out var dso)
                ? (long)dso : 0L;
            var byteCount = size.TryGetValue(out var sz)
                ? (long)sz : (data.Length - srcOff);
            if (byteCount < 0 || srcOff < 0 || srcOff + byteCount > data.Length)
                return Result<Unit, GenWebgpu.WriteBufferError>.FromErr(
                    new GenWebgpu.WriteBufferError
                    {
                        Kind = new GenWebgpu.WriteBufferErrorKind
                            .WriteBufferErrorKindOperationError(),
                        Message = $"WriteBufferWithCopy: data range "
                            + $"[off={srcOff}, count={byteCount}] is "
                            + $"out of bounds for data.Length={data.Length}.",
                    });
            if (byteCount == 0)
                return Result<Unit, GenWebgpu.WriteBufferError>.FromOk(default);
            fixed (byte* p = &data[srcOff])
            {
                _backend.EnsureApi().QueueWriteBuffer(
                    _queue, sb.Native, bufferOffset, p, (nuint)byteCount);
            }
            return Result<Unit, GenWebgpu.WriteBufferError>.FromOk(default);
        }

        public void WriteTextureWithCopy(
            GenWebgpu.GpuTexelCopyTextureInfo destination,
            byte[] data,
            GenWebgpu.GpuTexelCopyBufferLayout dataLayout,
            GenWebgpu.GpuExtent3D size)
        {
            if (_disposed || _queue == null)
                throw new ObjectDisposedException(nameof(SilkGpuQueue));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (destination.Texture is not SilkGpuTexture stex)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "WriteTextureWithCopy: destination.texture is not "
                    + "a Silk-backed texture.");
            data ??= System.Array.Empty<byte>();
            var origin = new Origin3D();
            if (destination.Origin.TryGetValue(out var o) && o != null)
            {
                if (o.X.TryGetValue(out var x)) origin.X = x;
                if (o.Y.TryGetValue(out var y)) origin.Y = y;
                if (o.Z.TryGetValue(out var z)) origin.Z = z;
            }
            var aspect = destination.Aspect.TryGetValue(out var asp)
                ? SilkGpuTexture.MapAspect(asp)
                : TextureAspect.All;
            uint mip = destination.MipLevel.TryGetValue(out var m) ? m : 0u;
            var ict = new ImageCopyTexture
            {
                Texture = stex.Native,
                MipLevel = mip,
                Origin = origin,
                Aspect = aspect,
            };
            var layout = new TextureDataLayout
            {
                Offset = dataLayout.Offset.TryGetValue(out var loff) ? loff : 0UL,
                BytesPerRow = dataLayout.BytesPerRow.TryGetValue(out var bpr) ? bpr : 0u,
                RowsPerImage = dataLayout.RowsPerImage.TryGetValue(out var rpi) ? rpi : 0u,
            };
            var ext = new Extent3D
            {
                Width = size.Width,
                Height = size.Height.TryGetValue(out var sh) ? sh : 1u,
                DepthOrArrayLayers = size.DepthOrArrayLayers.TryGetValue(out var sd) ? sd : 1u,
            };
            if (data.Length == 0)
            {
                _backend.EnsureApi().QueueWriteTexture(
                    _queue, &ict, null, 0, &layout, &ext);
                return;
            }
            fixed (byte* p = data)
            {
                _backend.EnsureApi().QueueWriteTexture(
                    _queue, &ict, p, (nuint)data.Length, &layout, &ext);
            }
        }

        public string Label() => _label;
        public void SetLabel(string label)
        {
            if (_disposed || _queue == null)
                throw new ObjectDisposedException(nameof(SilkGpuQueue));
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().QueueSetLabel(_queue, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_queue != null)
            {
                _backend.EnsureApi().QueueRelease(_queue);
                _queue = null;
            }
        }
    }

    /// <summary>Synthetic "device not lost" stub. Real
    /// callback-driven lost detection wires alongside the
    /// wgpu-poll infrastructure.</summary>
    internal sealed class SilkGpuDeviceLostInfo : GenWebgpu.IGpuDeviceLostInfo
    {
        public GenWebgpu.GpuDeviceLostReason Reason()
            => GenWebgpu.GpuDeviceLostReason.Unknown;
        public string Message() => string.Empty;
    }

    /// <summary>Captured wgpu error from <c>PopErrorScope</c>.
    /// Plain value carrier — no underlying wgpu handle to track,
    /// since wgpu's callback-passed pointer is owned by the
    /// driver and only valid inside the callback.</summary>
    internal sealed class SilkGpuError : GenWebgpu.IGpuError
    {
        private readonly GenWebgpu.GpuErrorKind _kind;
        private readonly string _message;
        public SilkGpuError(GenWebgpu.GpuErrorKind kind, string message)
        {
            _kind = kind;
            _message = message ?? string.Empty;
        }
        public GenWebgpu.GpuErrorKind Kind() => _kind;
        public string Message() => _message;
    }
}
