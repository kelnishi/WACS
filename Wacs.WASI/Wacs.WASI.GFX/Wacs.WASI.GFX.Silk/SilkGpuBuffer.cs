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
        private readonly global::Silk.NET.WebGPU.Device* _device;
        private global::Silk.NET.WebGPU.Buffer* _buffer;
        private readonly ulong _size;
        private readonly uint _usage;
        private string _label;
        private GenWebgpu.GpuBufferMapState _mapState;
        // The [offset, size) range currently mapped. Set on a
        // successful MapAsync (or at construction time when
        // mappedAtCreation is true) and cleared on Unmap.
        private ulong _mappedOffset;
        private ulong _mappedSize;
        private bool _disposed;

        public SilkGpuBuffer(SilkGpuBackend backend,
            global::Silk.NET.WebGPU.Device* device,
            global::Silk.NET.WebGPU.Buffer* buffer,
            ulong size, uint usage, string label,
            bool mappedAtCreation)
        {
            _backend = backend;
            _device = device;
            _buffer = buffer;
            _size = size;
            _usage = usage;
            _label = label ?? string.Empty;
            if (mappedAtCreation)
            {
                _mapState = GenWebgpu.GpuBufferMapState.Mapped;
                _mappedOffset = 0;
                _mappedSize = size;
            }
            else
            {
                _mapState = GenWebgpu.GpuBufferMapState.Unmapped;
            }
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

        public Result<Unit, GenWebgpu.MapAsyncError> MapAsync(
            uint mode, Option<ulong> offset, Option<ulong> size)
        {
            EnsureLive();
            if (_mapState != GenWebgpu.GpuBufferMapState.Unmapped)
                return MapAsyncOpError(
                    "MapAsync called while buffer is already mapped "
                    + "(or a map is pending). Call Unmap() first.");
            var off = offset.TryGetValue(out var o) ? o : 0UL;
            var sz = size.TryGetValue(out var s) ? s : _size - off;
            if (off + sz > _size)
                return Result<Unit, GenWebgpu.MapAsyncError>.FromErr(
                    new GenWebgpu.MapAsyncError
                    {
                        Kind = new GenWebgpu.MapAsyncErrorKind
                            .MapAsyncErrorKindRangeError(),
                        Message = $"MapAsync: range [{off}, +{sz}) is "
                            + $"out of bounds for buffer size {_size}.",
                    });
            // Tracked across the callback bridge.
            _mapState = GenWebgpu.GpuBufferMapState.Pending;
            var status = BufferMapAsyncStatus.Unknown;
            bool done = false;
            var cb = new PfnBufferMapCallback(
                (BufferMapAsyncStatus s2, void* _) =>
                {
                    status = s2;
                    done = true;
                });
            _backend.EnsureApi().BufferMapAsync(_buffer,
                (MapMode)mode, (nuint)off, (nuint)sz, cb, null);
            _backend.PollUntilDone(_device, () => done);
            if (status != BufferMapAsyncStatus.Success)
            {
                _mapState = GenWebgpu.GpuBufferMapState.Unmapped;
                return Result<Unit, GenWebgpu.MapAsyncError>.FromErr(
                    new GenWebgpu.MapAsyncError
                    {
                        Kind = status switch
                        {
                            BufferMapAsyncStatus.ValidationError
                                => new GenWebgpu.MapAsyncErrorKind
                                    .MapAsyncErrorKindOperationError(),
                            BufferMapAsyncStatus.MappingAlreadyPending
                                => new GenWebgpu.MapAsyncErrorKind
                                    .MapAsyncErrorKindOperationError(),
                            _ => (GenWebgpu.MapAsyncErrorKind)
                                new GenWebgpu.MapAsyncErrorKind
                                    .MapAsyncErrorKindAbortError(),
                        },
                        Message = "BufferMapAsync status=" + status,
                    });
            }
            _mapState = GenWebgpu.GpuBufferMapState.Mapped;
            _mappedOffset = off;
            _mappedSize = sz;
            return Result<Unit, GenWebgpu.MapAsyncError>.FromOk(default);
        }

        public Result<byte[], GenWebgpu.GetMappedRangeError> GetMappedRangeGetWithCopy(
            Option<ulong> offset, Option<ulong> size)
        {
            EnsureLive();
            if (_mapState != GenWebgpu.GpuBufferMapState.Mapped)
                return GetRangeOpError(
                    "GetMappedRangeGetWithCopy: buffer is not currently "
                    + "mapped. Call MapAsync() first and await its "
                    + "completion.");
            var off = offset.TryGetValue(out var o) ? o : _mappedOffset;
            var sz = size.TryGetValue(out var s) ? s : _mappedSize - (off - _mappedOffset);
            if (off < _mappedOffset || off + sz > _mappedOffset + _mappedSize)
                return GetRangeRangeError(
                    "GetMappedRangeGetWithCopy: [" + off + ", +" + sz
                    + ") falls outside the currently-mapped range ["
                    + _mappedOffset + ", +" + _mappedSize + ").");
            var ptr = _backend.EnsureApi().BufferGetMappedRange(
                _buffer, (nuint)off, (nuint)sz);
            if (ptr == null)
                return GetRangeOpError(
                    "GetMappedRangeGetWithCopy: wgpu returned a null "
                    + "mapped-range pointer for a buffer that should be "
                    + "mapped — possible driver issue.");
            var dst = new byte[sz];
            System.Runtime.InteropServices.Marshal.Copy(
                (IntPtr)ptr, dst, 0, (int)sz);
            return Result<byte[], GenWebgpu.GetMappedRangeError>.FromOk(dst);
        }

        public Result<Unit, GenWebgpu.GetMappedRangeError> GetMappedRangeSetWithCopy(
            byte[] data, Option<ulong> offset, Option<ulong> size)
        {
            EnsureLive();
            if (_mapState != GenWebgpu.GpuBufferMapState.Mapped)
                return SetRangeOpError(
                    "GetMappedRangeSetWithCopy: buffer is not currently "
                    + "mapped. Call MapAsync() first and await its "
                    + "completion.");
            data ??= Array.Empty<byte>();
            var off = offset.TryGetValue(out var o) ? o : _mappedOffset;
            var sz = size.TryGetValue(out var s) ? s : (ulong)data.Length;
            if (sz > (ulong)data.Length)
                return SetRangeRangeError(
                    "GetMappedRangeSetWithCopy: size " + sz
                    + " exceeds data.Length " + data.Length + ".");
            if (off < _mappedOffset || off + sz > _mappedOffset + _mappedSize)
                return SetRangeRangeError(
                    "GetMappedRangeSetWithCopy: [" + off + ", +" + sz
                    + ") falls outside the currently-mapped range ["
                    + _mappedOffset + ", +" + _mappedSize + ").");
            var ptr = _backend.EnsureApi().BufferGetMappedRange(
                _buffer, (nuint)off, (nuint)sz);
            if (ptr == null)
                return SetRangeOpError(
                    "GetMappedRangeSetWithCopy: wgpu returned a null "
                    + "mapped-range pointer for a buffer that should be "
                    + "mapped.");
            if (sz > 0)
                System.Runtime.InteropServices.Marshal.Copy(
                    data, 0, (IntPtr)ptr, (int)sz);
            return Result<Unit, GenWebgpu.GetMappedRangeError>.FromOk(default);
        }

        public Result<Unit, GenWebgpu.UnmapError> Unmap()
        {
            EnsureLive();
            if (_mapState != GenWebgpu.GpuBufferMapState.Mapped)
                return Result<Unit, GenWebgpu.UnmapError>.FromErr(
                    new GenWebgpu.UnmapError
                    {
                        Kind = new GenWebgpu.UnmapErrorKind
                            .UnmapErrorKindAbortError(),
                        Message = "Unmap: buffer is not currently mapped.",
                    });
            _backend.EnsureApi().BufferUnmap(_buffer);
            _mapState = GenWebgpu.GpuBufferMapState.Unmapped;
            _mappedOffset = 0;
            _mappedSize = 0;
            return Result<Unit, GenWebgpu.UnmapError>.FromOk(default);
        }

        // ----- Result-construction shorthands -----
        private static Result<Unit, GenWebgpu.MapAsyncError> MapAsyncOpError(
            string message)
            => Result<Unit, GenWebgpu.MapAsyncError>.FromErr(
                new GenWebgpu.MapAsyncError
                {
                    Kind = new GenWebgpu.MapAsyncErrorKind
                        .MapAsyncErrorKindOperationError(),
                    Message = message,
                });

        private static Result<byte[], GenWebgpu.GetMappedRangeError>
            GetRangeOpError(string message)
            => Result<byte[], GenWebgpu.GetMappedRangeError>.FromErr(
                new GenWebgpu.GetMappedRangeError
                {
                    Kind = new GenWebgpu.GetMappedRangeErrorKind
                        .GetMappedRangeErrorKindOperationError(),
                    Message = message,
                });

        private static Result<byte[], GenWebgpu.GetMappedRangeError>
            GetRangeRangeError(string message)
            => Result<byte[], GenWebgpu.GetMappedRangeError>.FromErr(
                new GenWebgpu.GetMappedRangeError
                {
                    Kind = new GenWebgpu.GetMappedRangeErrorKind
                        .GetMappedRangeErrorKindRangeError(),
                    Message = message,
                });

        private static Result<Unit, GenWebgpu.GetMappedRangeError>
            SetRangeOpError(string message)
            => Result<Unit, GenWebgpu.GetMappedRangeError>.FromErr(
                new GenWebgpu.GetMappedRangeError
                {
                    Kind = new GenWebgpu.GetMappedRangeErrorKind
                        .GetMappedRangeErrorKindOperationError(),
                    Message = message,
                });

        private static Result<Unit, GenWebgpu.GetMappedRangeError>
            SetRangeRangeError(string message)
            => Result<Unit, GenWebgpu.GetMappedRangeError>.FromErr(
                new GenWebgpu.GetMappedRangeError
                {
                    Kind = new GenWebgpu.GetMappedRangeErrorKind
                        .GetMappedRangeErrorKindRangeError(),
                    Message = message,
                });

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
