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
        {
            EnsureLive();
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            var label = descriptor.Label.TryGetValue(out var l) && l != null
                ? l : string.Empty;
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            var colorAttachments = descriptor.ColorAttachments
                ?? Array.Empty<Option<GenWebgpu.GpuRenderPassColorAttachment>>();
            var nativeAtts = new RenderPassColorAttachment[colorAttachments.Length];
            for (int i = 0; i < colorAttachments.Length; i++)
            {
                if (!colorAttachments[i].TryGetValue(out var ca) || ca == null)
                {
                    // wgpu treats null View as "no attachment at this
                    // slot"; the WIT's None on the outer option matches.
                    nativeAtts[i] = default;
                    continue;
                }
                if (ca.View is not SilkGpuTextureView sv)
                    throw new Wacs.WASI.GFX.Types.WasiGfxException(
                        $"BeginRenderPass: color attachment {i} view is "
                        + "not Silk-backed.");
                TextureView* resolve = null;
                if (ca.ResolveTarget.TryGetValue(out var rt) && rt != null)
                {
                    if (rt is not SilkGpuTextureView srt)
                        throw new Wacs.WASI.GFX.Types.WasiGfxException(
                            $"BeginRenderPass: color attachment {i} "
                            + "resolve-target is not Silk-backed.");
                    resolve = srt.Native;
                }
                var clear = new Color { R = 0, G = 0, B = 0, A = 0 };
                if (ca.ClearValue.TryGetValue(out var cv) && cv != null)
                {
                    clear.R = cv.R; clear.G = cv.G; clear.B = cv.B; clear.A = cv.A;
                }
                nativeAtts[i] = new RenderPassColorAttachment
                {
                    View = sv.Native,
                    DepthSlice = ca.DepthSlice.TryGetValue(out var ds) ? ds : 0u,
                    ResolveTarget = resolve,
                    LoadOp = ca.LoadOp == GenWebgpu.GpuLoadOp.Load
                        ? LoadOp.Load : LoadOp.Clear,
                    StoreOp = ca.StoreOp == GenWebgpu.GpuStoreOp.Store
                        ? StoreOp.Store : StoreOp.Discard,
                    ClearValue = clear,
                };
            }

            RenderPassDepthStencilAttachment dsa = default;
            bool hasDsa = false;
            if (descriptor.DepthStencilAttachment.TryGetValue(out var dsaOpt)
                && dsaOpt != null)
            {
                if (dsaOpt.View is not SilkGpuTextureView dsv)
                    throw new Wacs.WASI.GFX.Types.WasiGfxException(
                        "BeginRenderPass: depth-stencil view is not Silk-backed.");
                dsa.View = dsv.Native;
                dsa.DepthClearValue = dsaOpt.DepthClearValue.TryGetValue(out var dcv)
                    ? dcv : 0f;
                dsa.DepthLoadOp = dsaOpt.DepthLoadOp.TryGetValue(out var dlo)
                    ? (dlo == GenWebgpu.GpuLoadOp.Load ? LoadOp.Load : LoadOp.Clear)
                    : LoadOp.Undefined;
                dsa.DepthStoreOp = dsaOpt.DepthStoreOp.TryGetValue(out var dso)
                    ? (dso == GenWebgpu.GpuStoreOp.Store ? StoreOp.Store : StoreOp.Discard)
                    : StoreOp.Undefined;
                dsa.DepthReadOnly = dsaOpt.DepthReadOnly.TryGetValue(out var dro) && dro;
                dsa.StencilClearValue = dsaOpt.StencilClearValue
                    .TryGetValue(out var scv) ? scv : 0u;
                dsa.StencilLoadOp = dsaOpt.StencilLoadOp.TryGetValue(out var slo)
                    ? (slo == GenWebgpu.GpuLoadOp.Load ? LoadOp.Load : LoadOp.Clear)
                    : LoadOp.Undefined;
                dsa.StencilStoreOp = dsaOpt.StencilStoreOp.TryGetValue(out var sso)
                    ? (sso == GenWebgpu.GpuStoreOp.Store ? StoreOp.Store : StoreOp.Discard)
                    : StoreOp.Undefined;
                dsa.StencilReadOnly = dsaOpt.StencilReadOnly.TryGetValue(out var sro) && sro;
                hasDsa = true;
            }

            QuerySet* occlusionNative = null;
            if (descriptor.OcclusionQuerySet.TryGetValue(out var oqs)
                && oqs != null)
            {
                if (oqs is not SilkGpuQuerySet sqs)
                    throw new Wacs.WASI.GFX.Types.WasiGfxException(
                        "BeginRenderPass: occlusion-query-set is not Silk-backed.");
                occlusionNative = sqs.Native;
            }

            RenderPassEncoder* pass;
            fixed (byte* labelPtr = labelBytes)
            fixed (RenderPassColorAttachment* attsPtr = nativeAtts)
            {
                var desc = new RenderPassDescriptor
                {
                    Label = labelPtr,
                    ColorAttachmentCount = (nuint)nativeAtts.Length,
                    ColorAttachments = nativeAtts.Length > 0 ? attsPtr : null,
                    DepthStencilAttachment = hasDsa ? &dsa : null,
                    OcclusionQuerySet = occlusionNative,
                    TimestampWrites = null,
                };
                pass = _backend.EnsureApi()
                    .CommandEncoderBeginRenderPass(_encoder, &desc);
            }
            if (pass == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "BeginRenderPass: wgpu returned a null pass encoder.");
            return new SilkGpuRenderPassEncoder(_backend, pass);
        }

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
        {
            EnsureLive();
            var src = BuildImageCopyBuffer(source);
            var dst = BuildImageCopyTexture(destination);
            var size = BuildExtent(copySize);
            _backend.EnsureApi().CommandEncoderCopyBufferToTexture(
                _encoder, &src, &dst, &size);
        }

        public void CopyTextureToBuffer(
            GenWebgpu.GpuTexelCopyTextureInfo source,
            GenWebgpu.GpuTexelCopyBufferInfo destination,
            GenWebgpu.GpuExtent3D copySize)
        {
            EnsureLive();
            var src = BuildImageCopyTexture(source);
            var dst = BuildImageCopyBuffer(destination);
            var size = BuildExtent(copySize);
            _backend.EnsureApi().CommandEncoderCopyTextureToBuffer(
                _encoder, &src, &dst, &size);
        }

        public void CopyTextureToTexture(
            GenWebgpu.GpuTexelCopyTextureInfo source,
            GenWebgpu.GpuTexelCopyTextureInfo destination,
            GenWebgpu.GpuExtent3D copySize)
        {
            EnsureLive();
            var src = BuildImageCopyTexture(source);
            var dst = BuildImageCopyTexture(destination);
            var size = BuildExtent(copySize);
            _backend.EnsureApi().CommandEncoderCopyTextureToTexture(
                _encoder, &src, &dst, &size);
        }

        private static ImageCopyTexture BuildImageCopyTexture(
            GenWebgpu.GpuTexelCopyTextureInfo info)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));
            if (info.Texture is not SilkGpuTexture stex)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "TexelCopyTextureInfo.texture is not Silk-backed.");
            var origin = new Origin3D();
            if (info.Origin.TryGetValue(out var o) && o != null)
            {
                if (o.X.TryGetValue(out var x)) origin.X = x;
                if (o.Y.TryGetValue(out var y)) origin.Y = y;
                if (o.Z.TryGetValue(out var z)) origin.Z = z;
            }
            return new ImageCopyTexture
            {
                Texture = stex.Native,
                MipLevel = info.MipLevel.TryGetValue(out var m) ? m : 0u,
                Origin = origin,
                Aspect = info.Aspect.TryGetValue(out var asp)
                    ? SilkGpuTexture.MapAspect(asp) : TextureAspect.All,
            };
        }

        private static ImageCopyBuffer BuildImageCopyBuffer(
            GenWebgpu.GpuTexelCopyBufferInfo info)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));
            if (info.Buffer is not SilkGpuBuffer sb)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "TexelCopyBufferInfo.buffer is not Silk-backed.");
            return new ImageCopyBuffer
            {
                Buffer = sb.Native,
                Layout = new TextureDataLayout
                {
                    Offset = info.Offset.TryGetValue(out var off) ? off : 0UL,
                    BytesPerRow = info.BytesPerRow.TryGetValue(out var bpr) ? bpr : 0u,
                    RowsPerImage = info.RowsPerImage.TryGetValue(out var rpi) ? rpi : 0u,
                },
            };
        }

        private static Extent3D BuildExtent(GenWebgpu.GpuExtent3D size)
        {
            if (size == null)
                throw new ArgumentNullException(nameof(size));
            return new Extent3D
            {
                Width = size.Width,
                Height = size.Height.TryGetValue(out var h) ? h : 1u,
                DepthOrArrayLayers = size.DepthOrArrayLayers
                    .TryGetValue(out var d) ? d : 1u,
            };
        }

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
        {
            EnsureLive();
            if (querySet is not SilkGpuQuerySet sqs)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "ResolveQuerySet: querySet is not Silk-backed.");
            if (destination is not SilkGpuBuffer dst)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "ResolveQuerySet: destination is not Silk-backed.");
            _backend.EnsureApi().CommandEncoderResolveQuerySet(
                _encoder, sqs.Native, firstQuery, queryCount,
                dst.Native, destinationOffset);
        }

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
        {
            EnsureLive();
            BindGroup* native = null;
            if (bindGroup.TryGetValue(out var bg) && bg != null)
            {
                if (bg is not SilkGpuBindGroup sbg)
                    return Result<Unit, GenWebgpu.SetBindGroupError>.FromErr(
                        new GenWebgpu.SetBindGroupError
                        {
                            Kind = new GenWebgpu.SetBindGroupErrorKind
                                .SetBindGroupErrorKindRangeError(),
                            Message = "SetBindGroup: non-Silk-backed bind "
                                + "group.",
                        });
                native = sbg.Native;
            }
            // dynamic-offsets: spec splits into the full list +
            // start/length window. The window narrows the slice
            // passed to wgpu. Default (no options) → no offsets.
            uint[] offs = dynamicOffsets.TryGetValue(out var arr) && arr != null
                ? arr : Array.Empty<uint>();
            uint start = dynamicOffsetsDataStart.TryGetValue(out var s)
                ? (uint)s : 0u;
            uint len = dynamicOffsetsDataLength.TryGetValue(out var len2)
                ? len2 : (uint)offs.Length;
            if (start > offs.Length || start + len > offs.Length)
                return Result<Unit, GenWebgpu.SetBindGroupError>.FromErr(
                    new GenWebgpu.SetBindGroupError
                    {
                        Kind = new GenWebgpu.SetBindGroupErrorKind
                            .SetBindGroupErrorKindRangeError(),
                        Message = "SetBindGroup: dynamic-offsets window "
                            + $"[start={start}, len={len}] is out of "
                            + $"bounds for list length {offs.Length}.",
                    });
            if (len == 0)
            {
                _backend.EnsureApi().ComputePassEncoderSetBindGroup(
                    _pass, index, native, 0, null);
            }
            else
            {
                fixed (uint* p = &offs[start])
                {
                    _backend.EnsureApi().ComputePassEncoderSetBindGroup(
                        _pass, index, native, (nuint)len, p);
                }
            }
            return Result<Unit, GenWebgpu.SetBindGroupError>.FromOk(default);
        }

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
            var bgl = _backend.EnsureApi()
                .ComputePipelineGetBindGroupLayout(_pipeline, index);
            if (bgl == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuComputePipeline.GetBindGroupLayout("
                    + index + "): wgpu returned a null layout — "
                    + "check the WGSL @group/@binding decorations.");
            return new SilkGpuBindGroupLayout(_backend, bgl, string.Empty);
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
