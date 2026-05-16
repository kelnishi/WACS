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
    ///
    /// <para>The descriptor-decoding work for the create-*
    /// methods lands across follow-up commits — this commit
    /// ships the device-handle skeleton so the request-device
    /// chain returns a real wgpu Device that downstream resource
    /// work can attach to.</para>
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
            // wgpu's uncaptured-error path is a registered C
            // callback on the device; converting it to a polled
            // wasi:io.Pollable requires routing the callback into
            // an event ring. Defer until the wgpu-poll
            // infrastructure lands.
            throw new PlatformNotSupportedException(
                "SilkGpuDevice.OnuncapturederrorSubscribe: not yet "
                + "wired — needs wgpu-error callback → Pollable "
                + "bridge.");
        }

        public void ConnectGraphicsContext(
            global::Wacs.WASI.GFX.GraphicsContext.IContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            EnsureLive();
            // Surface configuration: this is where the wgpu side
            // imports the wasi-gfx context's underlying SDL window
            // as a wgpu Surface and configures it against this
            // device. The actual surface-import path needs the
            // SDL native handle from the wasi-gfx side, which lives
            // on the WasiGfxSilkBindable. Defer.
            throw new PlatformNotSupportedException(
                "SilkGpuDevice.ConnectGraphicsContext: not yet "
                + "wired — needs wgpu surface-import path from "
                + "the wasi-gfx-side SDL window handle.");
        }

        // ===== create-* methods land in follow-up commits =====
        // Each method needs the descriptor flat-form decoding
        // (the WitBindings.cs side passes a default-constructed
        // record today). The methods below throw with a clear
        // "descriptor decoder lands in next commit" signal so the
        // host binding's wire-form decode can still proceed and
        // the throw surfaces as the bridge to wgpu-native.

        private const string DispatchPending
            = "wgpu-native dispatch path landing in follow-up commits. "
              + "The binding's wire-form decoding works; the "
              + "descriptor → wgpu translation is the next step.";

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
                vfWgpu[i] = (TextureFormat)viewFormats[i];
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
                    Format = (TextureFormat)descriptor.Format,
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

        private static TextureFormat MapTextureFormat(
            GenWebgpu.GpuTextureFormat f)
            // wasi:webgpu's gpu-texture-format and wgpu's TextureFormat
            // are spec-aligned. Numeric equivalence holds for the
            // common cases; cast through for now and revisit if a
            // mismatch surfaces.
            => (TextureFormat)f;
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
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateRenderPipeline: " + DispatchPending);
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
        public Result<GenWebgpu.IGpuRenderPipeline, GenWebgpu.CreatePipelineError>
            CreateRenderPipelineAsync(GenWebgpu.GpuRenderPipelineDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateRenderPipelineAsync: " + DispatchPending);
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
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateRenderBundleEncoder: " + DispatchPending);
        public Result<GenWebgpu.IGpuQuerySet, GenWebgpu.CreateQuerySetError>
            CreateQuerySet(GenWebgpu.GpuQuerySetDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateQuerySet: " + DispatchPending);

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
    /// wgpu-backed queue wrapper. Submit / write-buffer / on-
    /// submitted-work-done land in follow-up commits alongside
    /// the command-buffer descriptor decode.
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
