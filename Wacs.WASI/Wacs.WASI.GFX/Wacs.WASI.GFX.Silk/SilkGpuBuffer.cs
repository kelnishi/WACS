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
    /// Silk-backed wrapper around a wgpu <c>global::Silk.NET.WebGPU.Buffer*</c>. Caches
    /// the size + usage at creation time so the size/usage WIT
    /// queries don't round-trip through wgpu — wgpu reports
    /// these via separate FFI calls, but the values are
    /// immutable after creation.
    /// </summary>
    internal sealed unsafe class SilkGpuBuffer : GenWebgpu.IGpuBuffer, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private global::Silk.NET.WebGPU.Buffer* _buffer;
        private readonly ulong _size;
        private readonly uint _usage;
        private string _label;
        private GenWebgpu.GpuBufferMapState _mapState;
        private bool _disposed;

        public SilkGpuBuffer(SilkGpuBackend backend, global::Silk.NET.WebGPU.Buffer* buffer,
            ulong size, uint usage, string label,
            bool mappedAtCreation)
        {
            _backend = backend;
            _buffer = buffer;
            _size = size;
            _usage = usage;
            _label = label ?? string.Empty;
            _mapState = mappedAtCreation
                ? GenWebgpu.GpuBufferMapState.Mapped
                : GenWebgpu.GpuBufferMapState.Unmapped;
        }

        internal global::Silk.NET.WebGPU.Buffer* Native => _buffer;
        internal ulong NativeSize => _size;

        public ulong Size() => _size;
        public uint Usage() => _usage;
        public GenWebgpu.GpuBufferMapState MapState() => _mapState;

        public void Destroy()
        {
            EnsureLive();
            _backend.EnsureApi().BufferDestroy(_buffer);
            _mapState = GenWebgpu.GpuBufferMapState.Unmapped;
        }

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().BufferSetLabel(_buffer, p);
            }
        }

        // Mapping methods land alongside the wgpu-poll
        // infrastructure — BufferMapAsync is callback-driven and
        // needs the poll loop to drive it to completion.
        public Result<Unit, GenWebgpu.MapAsyncError> MapAsync(
            uint mode, Option<ulong> offset, Option<ulong> size)
            => throw new PlatformNotSupportedException(
                "SilkGpuBuffer.MapAsync: callback-driven; needs "
                + "wgpu-poll infrastructure to bridge sync.");

        public Result<byte[], GenWebgpu.GetMappedRangeError> GetMappedRangeGetWithCopy(
            Option<ulong> offset, Option<ulong> size)
            => throw new PlatformNotSupportedException(
                "SilkGpuBuffer.GetMappedRangeGetWithCopy: needs "
                + "wgpu-poll for the MapAsync prerequisite.");

        public Result<Unit, GenWebgpu.GetMappedRangeError> GetMappedRangeSetWithCopy(
            byte[] data, Option<ulong> offset, Option<ulong> size)
            => throw new PlatformNotSupportedException(
                "SilkGpuBuffer.GetMappedRangeSetWithCopy: needs "
                + "wgpu-poll for the MapAsync prerequisite.");

        public Result<Unit, GenWebgpu.UnmapError> Unmap()
            => throw new PlatformNotSupportedException(
                "SilkGpuBuffer.Unmap: paired with MapAsync; lands "
                + "alongside the wgpu-poll infrastructure.");

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_buffer != null)
            {
                _backend.EnsureApi().BufferRelease(_buffer);
                _buffer = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _buffer == null)
                throw new ObjectDisposedException(nameof(SilkGpuBuffer));
        }
    }
}
