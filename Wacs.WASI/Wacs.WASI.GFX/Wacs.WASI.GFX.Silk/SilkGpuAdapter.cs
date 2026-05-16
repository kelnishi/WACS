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

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Silk-backed wrapper around a wgpu <c>Adapter*</c>. Each
    /// instance owns one wgpu-native adapter reference; disposal
    /// releases it. Lifetime is parented to the <see cref="SilkGpu"/>
    /// that produced it but the wgpu-native handle remains valid
    /// independently — disposal is reference-counted on the
    /// native side.
    /// </summary>
    internal sealed unsafe class SilkGpuAdapter : GenWebgpu.IGpuAdapter, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private Adapter* _adapter;
        private bool _disposed;

        public SilkGpuAdapter(SilkGpuBackend backend, Adapter* adapter)
        {
            _backend = backend;
            _adapter = adapter;
        }

        internal Adapter* Native => _adapter;

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

        public GenWebgpu.IGpuAdapterInfo Info()
        {
            EnsureLive();
            return new SilkGpuAdapterInfo(_backend, _adapter);
        }

        public bool IsFallbackAdapter()
        {
            EnsureLive();
            // wgpu-native doesn't surface fallback status. We
            // can't infer it from BackendType either (the
            // CPU/llvmpipe adapter still reports Vulkan on
            // Linux). Report false; guests that need a fallback
            // path can request one explicitly via
            // RequestAdapterOptions.ForceFallbackAdapter.
            return false;
        }

        public Result<GenWebgpu.IGpuDevice, GenWebgpu.RequestDeviceError>
            RequestDevice(Option<GenWebgpu.GpuDeviceDescriptor> descriptor)
        {
            EnsureLive();
            // Decode required-features. required-limits stays at
            // defaults (the WIT models limits as a host-minted
            // resource, not in scope here). default-queue.label /
            // label are not consumed by wgpu-native's
            // DeviceDescriptor today, but we accept them on the wire
            // so guests passing Some(...) don't see a behavior
            // regression.
            FeatureName[] features = Array.Empty<FeatureName>();
            if (descriptor.TryGetValue(out var d) && d != null
                && d.RequiredFeatures.TryGetValue(out var featureArr)
                && featureArr != null && featureArr.Length > 0)
            {
                var mapped = new System.Collections.Generic.List<FeatureName>(
                    featureArr.Length);
                foreach (var f in featureArr)
                    if (TryMapWitFeature(f, out var native))
                        mapped.Add(native);
                features = mapped.ToArray();
            }
            Device* dev;
            fixed (FeatureName* featPtr = features)
            {
                var devDesc = new DeviceDescriptor
                {
                    RequiredFeatureCount = (nuint)features.Length,
                    RequiredFeatures = features.Length > 0 ? featPtr : null,
                };
                dev = _backend.RequestDeviceSync(
                    _adapter, &devDesc, out var err1);
                if (dev == null)
                    return Result<GenWebgpu.IGpuDevice, GenWebgpu.RequestDeviceError>
                        .FromErr(new GenWebgpu.RequestDeviceError
                        {
                            Kind = new GenWebgpu.RequestDeviceErrorKind
                                .RequestDeviceErrorKindOperationError(),
                            Message = err1 ?? "wgpu-native returned a null "
                                + "device with no error message.",
                        });
            }
            return Result<GenWebgpu.IGpuDevice, GenWebgpu.RequestDeviceError>
                .FromOk(new SilkGpuDevice(_backend, _adapter, dev));
        }

        // Maps gpu-feature-name (the WIT enum) to wgpu-native's
        // FeatureName. Returns false for entries that don't have a
        // wgpu-native counterpart — the caller drops them rather
        // than failing the request, matching browser behavior where
        // an unsupported `required-feature` rejects the promise but
        // wgpu-native itself just refuses to enable that feature.
        private static bool TryMapWitFeature(
            GenWebgpu.GpuFeatureName name, out FeatureName fn)
        {
            switch (name)
            {
                case GenWebgpu.GpuFeatureName.DepthClipControl:
                    fn = FeatureName.DepthClipControl; return true;
                case GenWebgpu.GpuFeatureName.Depth32floatStencil8:
                    fn = FeatureName.Depth32floatStencil8; return true;
                case GenWebgpu.GpuFeatureName.TextureCompressionBc:
                    fn = FeatureName.TextureCompressionBC; return true;
                case GenWebgpu.GpuFeatureName.TextureCompressionEtc2:
                    fn = FeatureName.TextureCompressionEtc2; return true;
                case GenWebgpu.GpuFeatureName.TextureCompressionAstc:
                    fn = FeatureName.TextureCompressionAstc; return true;
                case GenWebgpu.GpuFeatureName.TimestampQuery:
                    fn = FeatureName.TimestampQuery; return true;
                case GenWebgpu.GpuFeatureName.IndirectFirstInstance:
                    fn = FeatureName.IndirectFirstInstance; return true;
                case GenWebgpu.GpuFeatureName.ShaderF16:
                    fn = FeatureName.ShaderF16; return true;
                case GenWebgpu.GpuFeatureName.Rg11b10ufloatRenderable:
                    fn = FeatureName.RG11B10UfloatRenderable; return true;
                case GenWebgpu.GpuFeatureName.Bgra8unormStorage:
                    fn = FeatureName.Bgra8UnormStorage; return true;
                case GenWebgpu.GpuFeatureName.Float32Filterable:
                    fn = FeatureName.Float32filterable; return true;
                default:
                    fn = default; return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_adapter != null)
            {
                _backend.EnsureApi().AdapterRelease(_adapter);
                _adapter = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _adapter == null)
                throw new ObjectDisposedException(nameof(SilkGpuAdapter));
        }
    }

    /// <summary>
    /// wgpu-backed feature query. Maps WIT feature-name strings
    /// to wgpu's <see cref="FeatureName"/> enum and asks the
    /// adapter directly.
    /// </summary>
    internal sealed unsafe class SilkGpuSupportedFeatures
        : GenWebgpu.IGpuSupportedFeatures
    {
        private readonly SilkGpuBackend _backend;
        private readonly Adapter* _adapter;

        public SilkGpuSupportedFeatures(
            SilkGpuBackend backend, Adapter* adapter)
        {
            _backend = backend;
            _adapter = adapter;
        }

        public bool Has(string value)
        {
            if (!TryMapFeature(value, out var fn)) return false;
            return _backend.EnsureApi()
                .AdapterHasFeature(_adapter, fn);
        }

        // Maps WIT gpu-feature-name strings (or their canonical
        // WebGPU spec names) to wgpu-native FeatureName enum
        // entries. Unknown names map to "none" → Has() returns
        // false. The mapping is conservative: only the entries
        // that exist on both sides. The wasi:webgpu enum has 17
        // entries; wgpu-native enumerates a smaller subset.
        private static bool TryMapFeature(string name, out FeatureName fn)
        {
            fn = default;
            if (string.IsNullOrEmpty(name)) return false;
            switch (name)
            {
                case "depth-clip-control":
                    fn = FeatureName.DepthClipControl; return true;
                case "depth32float-stencil8":
                    fn = FeatureName.Depth32floatStencil8; return true;
                case "texture-compression-bc":
                    fn = FeatureName.TextureCompressionBC; return true;
                case "texture-compression-etc2":
                    fn = FeatureName.TextureCompressionEtc2; return true;
                case "texture-compression-astc":
                    fn = FeatureName.TextureCompressionAstc; return true;
                case "timestamp-query":
                    fn = FeatureName.TimestampQuery; return true;
                case "indirect-first-instance":
                    fn = FeatureName.IndirectFirstInstance; return true;
                case "shader-f16":
                    fn = FeatureName.ShaderF16; return true;
                case "rg11b10ufloat-renderable":
                    fn = FeatureName.RG11B10UfloatRenderable; return true;
                case "bgra8unorm-storage":
                    fn = FeatureName.Bgra8UnormStorage; return true;
                case "float32-filterable":
                    fn = FeatureName.Float32filterable; return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// wgpu-backed limits query. Caches the
    /// <see cref="SupportedLimits"/> struct from
    /// <c>AdapterGetLimits</c> on first use; every per-field
    /// accessor reads from the cache.
    /// </summary>
    internal sealed unsafe class SilkGpuSupportedLimits
        : GenWebgpu.IGpuSupportedLimits
    {
        private readonly SupportedLimits _limits;

        public SilkGpuSupportedLimits(
            SilkGpuBackend backend, Adapter* adapter)
        {
            var sl = default(SupportedLimits);
            backend.EnsureApi().AdapterGetLimits(adapter, &sl);
            _limits = sl;
        }

        private Limits L => _limits.Limits;

        public uint MaxTextureDimension1D() => L.MaxTextureDimension1D;
        public uint MaxTextureDimension2D() => L.MaxTextureDimension2D;
        public uint MaxTextureDimension3D() => L.MaxTextureDimension3D;
        public uint MaxTextureArrayLayers() => L.MaxTextureArrayLayers;
        public uint MaxBindGroups() => L.MaxBindGroups;
        public uint MaxBindGroupsPlusVertexBuffers()
            => L.MaxBindGroupsPlusVertexBuffers;
        public uint MaxBindingsPerBindGroup() => L.MaxBindingsPerBindGroup;
        public uint MaxDynamicUniformBuffersPerPipelineLayout()
            => L.MaxDynamicUniformBuffersPerPipelineLayout;
        public uint MaxDynamicStorageBuffersPerPipelineLayout()
            => L.MaxDynamicStorageBuffersPerPipelineLayout;
        public uint MaxSampledTexturesPerShaderStage()
            => L.MaxSampledTexturesPerShaderStage;
        public uint MaxSamplersPerShaderStage() => L.MaxSamplersPerShaderStage;
        public uint MaxStorageBuffersPerShaderStage()
            => L.MaxStorageBuffersPerShaderStage;
        public uint MaxStorageTexturesPerShaderStage()
            => L.MaxStorageTexturesPerShaderStage;
        public uint MaxUniformBuffersPerShaderStage()
            => L.MaxUniformBuffersPerShaderStage;
        public ulong MaxUniformBufferBindingSize()
            => L.MaxUniformBufferBindingSize;
        public ulong MaxStorageBufferBindingSize()
            => L.MaxStorageBufferBindingSize;
        public uint MinUniformBufferOffsetAlignment()
            => L.MinUniformBufferOffsetAlignment;
        public uint MinStorageBufferOffsetAlignment()
            => L.MinStorageBufferOffsetAlignment;
        public uint MaxVertexBuffers() => L.MaxVertexBuffers;
        public ulong MaxBufferSize() => L.MaxBufferSize;
        public uint MaxVertexAttributes() => L.MaxVertexAttributes;
        public uint MaxVertexBufferArrayStride() => L.MaxVertexBufferArrayStride;
        public uint MaxInterStageShaderVariables()
            => L.MaxInterStageShaderVariables;
        public uint MaxColorAttachments() => L.MaxColorAttachments;
        public uint MaxColorAttachmentBytesPerSample()
            => L.MaxColorAttachmentBytesPerSample;
        public uint MaxComputeWorkgroupStorageSize()
            => L.MaxComputeWorkgroupStorageSize;
        public uint MaxComputeInvocationsPerWorkgroup()
            => L.MaxComputeInvocationsPerWorkgroup;
        public uint MaxComputeWorkgroupSizeX() => L.MaxComputeWorkgroupSizeX;
        public uint MaxComputeWorkgroupSizeY() => L.MaxComputeWorkgroupSizeY;
        public uint MaxComputeWorkgroupSizeZ() => L.MaxComputeWorkgroupSizeZ;
        public uint MaxComputeWorkgroupsPerDimension()
            => L.MaxComputeWorkgroupsPerDimension;
    }

    /// <summary>
    /// wgpu-backed adapter info. Caches the
    /// <see cref="AdapterProperties"/> struct on first use and
    /// decodes the UTF-8 byte* fields once.
    /// </summary>
    internal sealed unsafe class SilkGpuAdapterInfo
        : GenWebgpu.IGpuAdapterInfo
    {
        private readonly string _vendor;
        private readonly string _architecture;
        private readonly string _device;
        private readonly string _description;

        public SilkGpuAdapterInfo(
            SilkGpuBackend backend, Adapter* adapter)
        {
            var props = default(AdapterProperties);
            backend.EnsureApi().AdapterGetProperties(adapter, &props);
            _vendor = SilkGpuBackend.DecodeUtf8(props.VendorName);
            _architecture = SilkGpuBackend.DecodeUtf8(props.Architecture);
            _device = SilkGpuBackend.DecodeUtf8(props.Name);
            _description = SilkGpuBackend.DecodeUtf8(props.DriverDescription);
        }

        public string Vendor() => _vendor;
        public string Architecture() => _architecture;
        public string Device() => _device;
        public string Description() => _description;
        public uint SubgroupMinSize() => 0;
        public uint SubgroupMaxSize() => 0;
    }
}
