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
    /// Silk-backed wrapper around a wgpu <c>CommandEncoder*</c>.
    /// Records the basic compute-path commands (clear-buffer /
    /// copy-buffer-to-buffer / begin-compute-pass / finish).
    /// Texture-copy + render-pass methods land alongside their
    /// respective wgpu wiring.
    /// </summary>
    internal sealed unsafe class SilkGpuCommandEncoder
        : GenWebgpu.IGpuCommandEncoder, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private CommandEncoder* _encoder;
        private string _label;
        private bool _disposed;

        public SilkGpuCommandEncoder(
            SilkGpuBackend backend, CommandEncoder* encoder, string label)
        {
            _backend = backend;
            _encoder = encoder;
            _label = label ?? string.Empty;
        }

        internal CommandEncoder* Native => _encoder;

        public GenWebgpu.IGpuComputePassEncoder BeginComputePass(
            Option<GenWebgpu.GpuComputePassDescriptor> descriptor)
        {
            EnsureLive();
            // hello_compute uses the no-descriptor form; the
            // timestamp-writes path lands when a guest needs it.
            var desc = default(ComputePassDescriptor);
            var pass = _backend.EnsureApi().CommandEncoderBeginComputePass(
                _encoder, &desc);
            if (pass == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuCommandEncoder.BeginComputePass: wgpu "
                    + "returned a null pass encoder.");
            return new SilkGpuComputePassEncoder(_backend, pass);
        }

        public GenWebgpu.IGpuRenderPassEncoder BeginRenderPass(
            GenWebgpu.GpuRenderPassDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuCommandEncoder.BeginRenderPass: render-pass "
                + "descriptor decode + wgpu wiring not yet landed.");

        public GenWebgpu.IGpuCommandBuffer Finish(
            Option<GenWebgpu.GpuCommandBufferDescriptor> descriptor)
        {
            EnsureLive();
            string label = string.Empty;
            if (descriptor.TryGetValue(out var d) && d != null)
                label = d.Label.TryGetValue(out var l) && l != null
                    ? l : string.Empty;
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            CommandBuffer* cb;
            fixed (byte* labelPtr = labelBytes)
            {
                var desc = new CommandBufferDescriptor
                {
                    Label = labelPtr,
                };
                cb = _backend.EnsureApi().CommandEncoderFinish(_encoder, &desc);
            }
            if (cb == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuCommandEncoder.Finish: wgpu returned a "
                    + "null command buffer.");
            return new SilkGpuCommandBuffer(_backend, cb, label);
        }

        public void CopyBufferToBuffer(
            GenWebgpu.IGpuBuffer source, ulong sourceOffset,
            GenWebgpu.IGpuBuffer destination, ulong destinationOffset,
            ulong size)
        {
            EnsureLive();
            if (source is not SilkGpuBuffer src ||
                destination is not SilkGpuBuffer dst)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuCommandEncoder.CopyBufferToBuffer: "
                    + "non-Silk-backed buffer; cross-backend copy not "
                    + "supported.");
            _backend.EnsureApi().CommandEncoderCopyBufferToBuffer(
                _encoder, src.Native, sourceOffset,
                dst.Native, destinationOffset, size);
        }

        public void CopyBufferToTexture(
            GenWebgpu.GpuTexelCopyBufferInfo source,
            GenWebgpu.GpuTexelCopyTextureInfo destination,
            GenWebgpu.GpuExtent3D copySize)
            => throw new PlatformNotSupportedException(
                "SilkGpuCommandEncoder.CopyBufferToTexture: texture "
                + "wgpu wrapper + texel-copy descriptor decode not yet "
                + "landed.");

        public void CopyTextureToBuffer(
            GenWebgpu.GpuTexelCopyTextureInfo source,
            GenWebgpu.GpuTexelCopyBufferInfo destination,
            GenWebgpu.GpuExtent3D copySize)
            => throw new PlatformNotSupportedException(
                "SilkGpuCommandEncoder.CopyTextureToBuffer: not yet "
                + "landed.");

        public void CopyTextureToTexture(
            GenWebgpu.GpuTexelCopyTextureInfo source,
            GenWebgpu.GpuTexelCopyTextureInfo destination,
            GenWebgpu.GpuExtent3D copySize)
            => throw new PlatformNotSupportedException(
                "SilkGpuCommandEncoder.CopyTextureToTexture: not yet "
                + "landed.");

        public void ClearBuffer(GenWebgpu.IGpuBuffer buffer,
            Option<ulong> offset, Option<ulong> size)
        {
            EnsureLive();
            if (buffer is not SilkGpuBuffer sb)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuCommandEncoder.ClearBuffer: non-Silk-backed "
                    + "buffer.");
            var off = offset.TryGetValue(out var o) ? o : 0UL;
            var sz = size.TryGetValue(out var s) ? s : sb.NativeSize - off;
            _backend.EnsureApi().CommandEncoderClearBuffer(
                _encoder, sb.Native, off, sz);
        }

        public void ResolveQuerySet(GenWebgpu.IGpuQuerySet querySet,
            uint firstQuery, uint queryCount,
            GenWebgpu.IGpuBuffer destination, ulong destinationOffset)
            => throw new PlatformNotSupportedException(
                "SilkGpuCommandEncoder.ResolveQuerySet: query-set "
                + "wgpu wrapper not yet landed.");

        public void PushDebugGroup(string groupLabel)
        {
            EnsureLive();
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                (groupLabel ?? string.Empty) + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().CommandEncoderPushDebugGroup(_encoder, p);
            }
        }
        public void PopDebugGroup()
        {
            EnsureLive();
            _backend.EnsureApi().CommandEncoderPopDebugGroup(_encoder);
        }
        public void InsertDebugMarker(string markerLabel)
        {
            EnsureLive();
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                (markerLabel ?? string.Empty) + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().CommandEncoderInsertDebugMarker(_encoder, p);
            }
        }

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().CommandEncoderSetLabel(_encoder, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_encoder != null)
            {
                _backend.EnsureApi().CommandEncoderRelease(_encoder);
                _encoder = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _encoder == null)
                throw new ObjectDisposedException(nameof(SilkGpuCommandEncoder));
        }
    }

    /// <summary>
    /// Silk-backed wrapper around a wgpu
    /// <c>ComputePassEncoder*</c>. Records set-pipeline /
    /// dispatch-workgroups / end. set-bind-group + indirect
    /// dispatch follow the bind-group-layout wiring.
    /// </summary>
    internal sealed unsafe class SilkGpuComputePassEncoder
        : GenWebgpu.IGpuComputePassEncoder, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private ComputePassEncoder* _pass;
        private string _label = string.Empty;
        private bool _disposed;

        public SilkGpuComputePassEncoder(
            SilkGpuBackend backend, ComputePassEncoder* pass)
        {
            _backend = backend;
            _pass = pass;
        }

        public void SetPipeline(GenWebgpu.IGpuComputePipeline pipeline)
        {
            EnsureLive();
            if (pipeline is not SilkGpuComputePipeline sp)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuComputePassEncoder.SetPipeline: non-Silk-"
                    + "backed compute pipeline.");
            _backend.EnsureApi().ComputePassEncoderSetPipeline(
                _pass, sp.Native);
        }

        public void DispatchWorkgroups(uint workgroupCountX,
            Option<uint> workgroupCountY, Option<uint> workgroupCountZ)
        {
            EnsureLive();
            var y = workgroupCountY.TryGetValue(out var yy) ? yy : 1u;
            var z = workgroupCountZ.TryGetValue(out var zz) ? zz : 1u;
            _backend.EnsureApi().ComputePassEncoderDispatchWorkgroups(
                _pass, workgroupCountX, y, z);
        }

        public void DispatchWorkgroupsIndirect(
            GenWebgpu.IGpuBuffer indirectBuffer, ulong indirectOffset)
        {
            EnsureLive();
            if (indirectBuffer is not SilkGpuBuffer sb)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuComputePassEncoder.DispatchWorkgroupsIndirect: "
                    + "non-Silk-backed indirect buffer.");
            _backend.EnsureApi().ComputePassEncoderDispatchWorkgroupsIndirect(
                _pass, sb.Native, indirectOffset);
        }

        public Result<Unit, GenWebgpu.SetBindGroupError> SetBindGroup(
            uint index, Option<GenWebgpu.IGpuBindGroup> bindGroup,
            Option<uint[]> dynamicOffsets,
            Option<ulong> dynamicOffsetsDataStart,
            Option<uint> dynamicOffsetsDataLength)
            => throw new PlatformNotSupportedException(
                "SilkGpuComputePassEncoder.SetBindGroup: bind-group "
                + "wgpu wrapper not yet landed.");

        public void End()
        {
            EnsureLive();
            _backend.EnsureApi().ComputePassEncoderEnd(_pass);
        }

        public void PushDebugGroup(string groupLabel)
        {
            EnsureLive();
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                (groupLabel ?? string.Empty) + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().ComputePassEncoderPushDebugGroup(_pass, p);
            }
        }
        public void PopDebugGroup()
        {
            EnsureLive();
            _backend.EnsureApi().ComputePassEncoderPopDebugGroup(_pass);
        }
        public void InsertDebugMarker(string markerLabel)
        {
            EnsureLive();
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                (markerLabel ?? string.Empty) + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().ComputePassEncoderInsertDebugMarker(_pass, p);
            }
        }

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().ComputePassEncoderSetLabel(_pass, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_pass != null)
            {
                _backend.EnsureApi().ComputePassEncoderRelease(_pass);
                _pass = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _pass == null)
                throw new ObjectDisposedException(nameof(SilkGpuComputePassEncoder));
        }
    }

    /// <summary>
    /// Silk-backed wrapper around a wgpu <c>CommandBuffer*</c>.
    /// A passive carrier — submit consumes it on the queue side.
    /// </summary>
    internal sealed unsafe class SilkGpuCommandBuffer
        : GenWebgpu.IGpuCommandBuffer, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private CommandBuffer* _cb;
        private string _label;
        private bool _disposed;

        public SilkGpuCommandBuffer(
            SilkGpuBackend backend, CommandBuffer* cb, string label)
        {
            _backend = backend;
            _cb = cb;
            _label = label ?? string.Empty;
        }

        internal CommandBuffer* Native => _cb;

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().CommandBufferSetLabel(_cb, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_cb != null)
            {
                _backend.EnsureApi().CommandBufferRelease(_cb);
                _cb = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _cb == null)
                throw new ObjectDisposedException(nameof(SilkGpuCommandBuffer));
        }
    }

    /// <summary>
    /// Placeholder Silk compute-pipeline wrapper so the
    /// SetPipeline pattern compiles. Real wgpu wiring lands
    /// with create-compute-pipeline.
    /// </summary>
    internal sealed unsafe class SilkGpuComputePipeline
        : GenWebgpu.IGpuComputePipeline, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private ComputePipeline* _pipeline;
        private string _label;
        private bool _disposed;

        public SilkGpuComputePipeline(SilkGpuBackend backend,
            ComputePipeline* pipeline, string label)
        {
            _backend = backend;
            _pipeline = pipeline;
            _label = label ?? string.Empty;
        }

        internal ComputePipeline* Native => _pipeline;

        public GenWebgpu.IGpuBindGroupLayout GetBindGroupLayout(uint index)
        {
            if (_disposed || _pipeline == null)
                throw new ObjectDisposedException(nameof(SilkGpuComputePipeline));
            // wgpu returns a refcounted handle; the wrapper class
            // for bind-group-layout exists separately and lands
            // alongside create-bind-group-layout. Defer.
            throw new PlatformNotSupportedException(
                "SilkGpuComputePipeline.GetBindGroupLayout: bind-group-"
                + "layout wgpu wrapper not yet landed.");
        }

        public string Label() => _label;
        public void SetLabel(string label)
        {
            if (_disposed || _pipeline == null)
                throw new ObjectDisposedException(nameof(SilkGpuComputePipeline));
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().ComputePipelineSetLabel(_pipeline, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_pipeline != null)
            {
                _backend.EnsureApi().ComputePipelineRelease(_pipeline);
                _pipeline = null;
            }
        }
    }
}
