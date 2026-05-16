// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Silk.NET.WebGPU;
using GenWebgpu = Wacs.WASI.GFX.Webgpu.Webgpu;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Silk-backed wrapper around a wgpu
    /// <c>BindGroupLayout*</c>.
    /// </summary>
    internal sealed unsafe class SilkGpuBindGroupLayout
        : GenWebgpu.IGpuBindGroupLayout, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private BindGroupLayout* _layout;
        private string _label;
        private bool _disposed;

        public SilkGpuBindGroupLayout(SilkGpuBackend backend,
            BindGroupLayout* layout, string label)
        {
            _backend = backend;
            _layout = layout;
            _label = label ?? string.Empty;
        }

        internal BindGroupLayout* Native => _layout;

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().BindGroupLayoutSetLabel(_layout, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_layout != null)
            {
                _backend.EnsureApi().BindGroupLayoutRelease(_layout);
                _layout = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _layout == null)
                throw new ObjectDisposedException(nameof(SilkGpuBindGroupLayout));
        }
    }

    /// <summary>
    /// Silk-backed wrapper around a wgpu
    /// <c>PipelineLayout*</c>.
    /// </summary>
    internal sealed unsafe class SilkGpuPipelineLayout
        : GenWebgpu.IGpuPipelineLayout, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private PipelineLayout* _layout;
        private string _label;
        private bool _disposed;

        public SilkGpuPipelineLayout(SilkGpuBackend backend,
            PipelineLayout* layout, string label)
        {
            _backend = backend;
            _layout = layout;
            _label = label ?? string.Empty;
        }

        internal PipelineLayout* Native => _layout;

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().PipelineLayoutSetLabel(_layout, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_layout != null)
            {
                _backend.EnsureApi().PipelineLayoutRelease(_layout);
                _layout = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _layout == null)
                throw new ObjectDisposedException(nameof(SilkGpuPipelineLayout));
        }
    }

    /// <summary>
    /// Silk-backed wrapper around a wgpu <c>BindGroup*</c>.
    /// </summary>
    internal sealed unsafe class SilkGpuBindGroup
        : GenWebgpu.IGpuBindGroup, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private BindGroup* _group;
        private string _label;
        private bool _disposed;

        public SilkGpuBindGroup(SilkGpuBackend backend,
            BindGroup* group, string label)
        {
            _backend = backend;
            _group = group;
            _label = label ?? string.Empty;
        }

        internal BindGroup* Native => _group;

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().BindGroupSetLabel(_group, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_group != null)
            {
                _backend.EnsureApi().BindGroupRelease(_group);
                _group = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _group == null)
                throw new ObjectDisposedException(nameof(SilkGpuBindGroup));
        }
    }
}
