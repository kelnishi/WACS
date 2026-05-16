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
    /// Silk-backed wrapper around a wgpu <c>Texture*</c>. Caches
    /// the descriptor fields needed by the WIT query methods so
    /// the hot path doesn't have to round-trip wgpu queries that
    /// only return cached creation params anyway.
    /// </summary>
    internal sealed unsafe class SilkGpuTexture
        : GenWebgpu.IGpuTexture, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private Texture* _texture;
        private readonly uint _width, _height, _depthOrArrayLayers;
        private readonly uint _mipLevelCount, _sampleCount, _usage;
        private readonly GenWebgpu.GpuTextureDimension _dimension;
        private readonly GenWebgpu.GpuTextureFormat _format;
        private string _label;
        private bool _disposed;

        public SilkGpuTexture(SilkGpuBackend backend, Texture* texture,
            uint width, uint height, uint depthOrArrayLayers,
            uint mipLevelCount, uint sampleCount, uint usage,
            GenWebgpu.GpuTextureDimension dimension,
            GenWebgpu.GpuTextureFormat format,
            string label)
        {
            _backend = backend;
            _texture = texture;
            _width = width;
            _height = height;
            _depthOrArrayLayers = depthOrArrayLayers;
            _mipLevelCount = mipLevelCount;
            _sampleCount = sampleCount;
            _usage = usage;
            _dimension = dimension;
            _format = format;
            _label = label ?? string.Empty;
        }

        internal Texture* Native => _texture;

        public GenWebgpu.IGpuTextureView CreateView(
            Option<GenWebgpu.GpuTextureViewDescriptor> descriptor)
        {
            EnsureLive();
            string label = string.Empty;
            // wgpu defaults: MipLevelCount + ArrayLayerCount =
            // 0xFFFFFFFF means "use all remaining" — passing 0
            // is rejected by wgpu validation as "invalid count".
            // The other enum-typed fields treat zero = Undefined
            // which wgpu maps to "inherit from texture".
            var desc = default(TextureViewDescriptor);
            desc.MipLevelCount = uint.MaxValue;
            desc.ArrayLayerCount = uint.MaxValue;
            if (descriptor.TryGetValue(out var d) && d != null)
            {
                label = d.Label.TryGetValue(out var l) && l != null
                    ? l : string.Empty;
                if (d.Format.TryGetValue(out var fmt))
                    desc.Format = SilkGpuDevice.MapTextureFormat(fmt);
                if (d.Dimension.TryGetValue(out var dim))
                    desc.Dimension = MapViewDim(dim);
                // d.Usage is a webgpu-spec hint not exposed by
                // wgpu-native's TextureViewDescriptor; ignore.
                if (d.Aspect.TryGetValue(out var asp))
                    desc.Aspect = MapAspect(asp);
                if (d.BaseMipLevel.TryGetValue(out var bml))
                    desc.BaseMipLevel = bml;
                if (d.MipLevelCount.TryGetValue(out var mlc))
                    desc.MipLevelCount = mlc;
                if (d.BaseArrayLayer.TryGetValue(out var bal))
                    desc.BaseArrayLayer = bal;
                if (d.ArrayLayerCount.TryGetValue(out var alc))
                    desc.ArrayLayerCount = alc;
            }
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            TextureView* view;
            fixed (byte* labelPtr = labelBytes)
            {
                desc.Label = labelPtr;
                view = _backend.EnsureApi().TextureCreateView(_texture, &desc);
            }
            if (view == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuTexture.CreateView: wgpu returned a null "
                    + "texture view.");
            return new SilkGpuTextureView(_backend, view, label);
        }

        public void Destroy()
        {
            EnsureLive();
            _backend.EnsureApi().TextureDestroy(_texture);
        }

        public uint Width() => _width;
        public uint Height() => _height;
        public uint DepthOrArrayLayers() => _depthOrArrayLayers;
        public uint MipLevelCount() => _mipLevelCount;
        public uint SampleCount() => _sampleCount;
        public GenWebgpu.GpuTextureDimension Dimension() => _dimension;
        public GenWebgpu.GpuTextureFormat Format() => _format;
        public uint Usage() => _usage;

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().TextureSetLabel(_texture, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_texture != null)
            {
                _backend.EnsureApi().TextureRelease(_texture);
                _texture = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _texture == null)
                throw new ObjectDisposedException(nameof(SilkGpuTexture));
        }

        internal static TextureViewDimension MapViewDim(
            GenWebgpu.GpuTextureViewDimension d)
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

        internal static TextureAspect MapAspect(
            GenWebgpu.GpuTextureAspect a)
            => a switch
            {
                GenWebgpu.GpuTextureAspect.All => TextureAspect.All,
                GenWebgpu.GpuTextureAspect.StencilOnly => TextureAspect.StencilOnly,
                GenWebgpu.GpuTextureAspect.DepthOnly => TextureAspect.DepthOnly,
                _ => TextureAspect.All,
            };
    }

    /// <summary>Silk-backed wrapper around a wgpu
    /// <c>TextureView*</c>.</summary>
    internal sealed unsafe class SilkGpuTextureView
        : GenWebgpu.IGpuTextureView, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private TextureView* _view;
        private string _label;
        private bool _disposed;

        public SilkGpuTextureView(SilkGpuBackend backend, TextureView* view,
            string label)
        {
            _backend = backend;
            _view = view;
            _label = label ?? string.Empty;
        }

        internal TextureView* Native => _view;

        public string Label() => _label;
        public void SetLabel(string label)
        {
            if (_disposed || _view == null)
                throw new ObjectDisposedException(nameof(SilkGpuTextureView));
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().TextureViewSetLabel(_view, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_view != null)
            {
                _backend.EnsureApi().TextureViewRelease(_view);
                _view = null;
            }
        }
    }

    /// <summary>Silk-backed wrapper around a wgpu
    /// <c>Sampler*</c>.</summary>
    internal sealed unsafe class SilkGpuSampler
        : GenWebgpu.IGpuSampler, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private Sampler* _sampler;
        private string _label;
        private bool _disposed;

        public SilkGpuSampler(SilkGpuBackend backend, Sampler* sampler,
            string label)
        {
            _backend = backend;
            _sampler = sampler;
            _label = label ?? string.Empty;
        }

        internal Sampler* Native => _sampler;

        public string Label() => _label;
        public void SetLabel(string label)
        {
            if (_disposed || _sampler == null)
                throw new ObjectDisposedException(nameof(SilkGpuSampler));
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().SamplerSetLabel(_sampler, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_sampler != null)
            {
                _backend.EnsureApi().SamplerRelease(_sampler);
                _sampler = null;
            }
        }
    }
}
