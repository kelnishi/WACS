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
    /// Silk-backed wrapper around a wgpu <c>ShaderModule*</c>.
    /// Created from a WGSL source string via the wgpu
    /// ShaderModuleWGSLDescriptor chained struct.
    /// </summary>
    internal sealed unsafe class SilkGpuShaderModule
        : GenWebgpu.IGpuShaderModule, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private ShaderModule* _module;
        private string _label;
        private bool _disposed;

        public SilkGpuShaderModule(
            SilkGpuBackend backend, ShaderModule* module, string label)
        {
            _backend = backend;
            _module = module;
            _label = label ?? string.Empty;
        }

        internal ShaderModule* Native => _module;

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().ShaderModuleSetLabel(_module, p);
            }
        }

        public GenWebgpu.IGpuCompilationInfo GetCompilationInfo()
            => throw new PlatformNotSupportedException(
                "SilkGpuShaderModule.GetCompilationInfo: callback-"
                + "driven; needs wgpu-poll infrastructure.");

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_module != null)
            {
                _backend.EnsureApi().ShaderModuleRelease(_module);
                _module = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _module == null)
                throw new ObjectDisposedException(nameof(SilkGpuShaderModule));
        }
    }
}
