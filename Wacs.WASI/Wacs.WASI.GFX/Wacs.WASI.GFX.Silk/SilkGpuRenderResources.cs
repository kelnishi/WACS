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
    /// <summary>Silk-backed wrapper around a wgpu
    /// <c>RenderPipeline*</c>. Same shape as
    /// <see cref="SilkGpuComputePipeline"/>: lazily wraps the
    /// derived bind-group-layout when the guest queries it.</summary>
    internal sealed unsafe class SilkGpuRenderPipeline
        : GenWebgpu.IGpuRenderPipeline, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private RenderPipeline* _pipeline;
        private string _label;
        private bool _disposed;

        public SilkGpuRenderPipeline(SilkGpuBackend backend,
            RenderPipeline* pipeline, string label)
        {
            _backend = backend;
            _pipeline = pipeline;
            _label = label ?? string.Empty;
        }

        internal RenderPipeline* Native => _pipeline;

        public GenWebgpu.IGpuBindGroupLayout GetBindGroupLayout(uint index)
        {
            EnsureLive();
            var bgl = _backend.EnsureApi()
                .RenderPipelineGetBindGroupLayout(_pipeline, index);
            if (bgl == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuRenderPipeline.GetBindGroupLayout("
                    + index + "): wgpu returned a null layout.");
            return new SilkGpuBindGroupLayout(_backend, bgl, string.Empty);
        }

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().RenderPipelineSetLabel(_pipeline, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_pipeline != null)
            {
                _backend.EnsureApi().RenderPipelineRelease(_pipeline);
                _pipeline = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _pipeline == null)
                throw new ObjectDisposedException(nameof(SilkGpuRenderPipeline));
        }
    }

    /// <summary>Silk-backed wrapper around a wgpu
    /// <c>QuerySet*</c>. Caches type + count at creation (both
    /// are immutable per spec).</summary>
    internal sealed unsafe class SilkGpuQuerySet
        : GenWebgpu.IGpuQuerySet, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private QuerySet* _querySet;
        private readonly GenWebgpu.GpuQueryType _type;
        private readonly uint _count;
        private string _label;
        private bool _disposed;

        public SilkGpuQuerySet(SilkGpuBackend backend, QuerySet* querySet,
            GenWebgpu.GpuQueryType type, uint count, string label)
        {
            _backend = backend;
            _querySet = querySet;
            _type = type;
            _count = count;
            _label = label ?? string.Empty;
        }

        internal QuerySet* Native => _querySet;

        public void Destroy()
        {
            EnsureLive();
            _backend.EnsureApi().QuerySetDestroy(_querySet);
        }
        public GenWebgpu.GpuQueryType Type() => _type;
        public uint Count() => _count;
        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().QuerySetSetLabel(_querySet, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_querySet != null)
            {
                _backend.EnsureApi().QuerySetRelease(_querySet);
                _querySet = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _querySet == null)
                throw new ObjectDisposedException(nameof(SilkGpuQuerySet));
        }
    }

    /// <summary>Silk-backed wrapper around a wgpu
    /// <c>RenderBundle*</c>.</summary>
    internal sealed unsafe class SilkGpuRenderBundle
        : GenWebgpu.IGpuRenderBundle, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private RenderBundle* _bundle;
        private string _label;
        private bool _disposed;

        public SilkGpuRenderBundle(SilkGpuBackend backend,
            RenderBundle* bundle, string label)
        {
            _backend = backend;
            _bundle = bundle;
            _label = label ?? string.Empty;
        }

        internal RenderBundle* Native => _bundle;

        public string Label() => _label;
        public void SetLabel(string label)
        {
            if (_disposed || _bundle == null)
                throw new ObjectDisposedException(nameof(SilkGpuRenderBundle));
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().RenderBundleSetLabel(_bundle, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_bundle != null)
            {
                _backend.EnsureApi().RenderBundleRelease(_bundle);
                _bundle = null;
            }
        }
    }

    /// <summary>Silk-backed wrapper around a wgpu
    /// <c>RenderBundleEncoder*</c>. Records the same draw / set-
    /// * commands as the render-pass encoder;
    /// <see cref="Finish"/> materializes a reusable
    /// <see cref="SilkGpuRenderBundle"/>.</summary>
    internal sealed unsafe class SilkGpuRenderBundleEncoder
        : GenWebgpu.IGpuRenderBundleEncoder, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private RenderBundleEncoder* _encoder;
        private string _label;
        private bool _disposed;

        public SilkGpuRenderBundleEncoder(SilkGpuBackend backend,
            RenderBundleEncoder* encoder, string label)
        {
            _backend = backend;
            _encoder = encoder;
            _label = label ?? string.Empty;
        }

        internal RenderBundleEncoder* Native => _encoder;

        public GenWebgpu.IGpuRenderBundle Finish(
            Option<GenWebgpu.GpuRenderBundleDescriptor> descriptor)
        {
            EnsureLive();
            string label = string.Empty;
            if (descriptor.TryGetValue(out var d) && d != null)
                label = d.Label.TryGetValue(out var l) && l != null ? l : string.Empty;
            var labelBytes = label.Length > 0
                ? System.Text.Encoding.UTF8.GetBytes(label + "\0")
                : null;
            RenderBundle* bundle;
            fixed (byte* labelPtr = labelBytes)
            {
                var desc = new RenderBundleDescriptor { Label = labelPtr };
                bundle = _backend.EnsureApi()
                    .RenderBundleEncoderFinish(_encoder, &desc);
            }
            if (bundle == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuRenderBundleEncoder.Finish: wgpu returned "
                    + "a null bundle.");
            return new SilkGpuRenderBundle(_backend, bundle, label);
        }

        public void SetPipeline(GenWebgpu.IGpuRenderPipeline pipeline)
        {
            EnsureLive();
            if (pipeline is not SilkGpuRenderPipeline sp)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "RenderBundleEncoder.SetPipeline: not Silk-backed.");
            _backend.EnsureApi().RenderBundleEncoderSetPipeline(_encoder, sp.Native);
        }

        public void SetIndexBuffer(GenWebgpu.IGpuBuffer buffer,
            GenWebgpu.GpuIndexFormat indexFormat,
            Option<ulong> offset, Option<ulong> size)
        {
            EnsureLive();
            if (buffer is not SilkGpuBuffer sb)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "RenderBundleEncoder.SetIndexBuffer: not Silk-backed.");
            var off = offset.TryGetValue(out var o) ? o : 0UL;
            var sz = size.TryGetValue(out var s) ? s : sb.NativeSize - off;
            _backend.EnsureApi().RenderBundleEncoderSetIndexBuffer(
                _encoder, sb.Native,
                indexFormat == GenWebgpu.GpuIndexFormat.Uint16
                    ? IndexFormat.Uint16 : IndexFormat.Uint32,
                off, sz);
        }

        public void SetVertexBuffer(uint slot,
            Option<GenWebgpu.IGpuBuffer> buffer,
            Option<ulong> offset, Option<ulong> size)
        {
            EnsureLive();
            global::Silk.NET.WebGPU.Buffer* native = null;
            ulong nativeSize = 0;
            if (buffer.TryGetValue(out var b) && b != null)
            {
                if (b is not SilkGpuBuffer sb)
                    throw new Wacs.WASI.GFX.Types.WasiGfxException(
                        "RenderBundleEncoder.SetVertexBuffer: not Silk-backed.");
                native = sb.Native;
                nativeSize = sb.NativeSize;
            }
            var off = offset.TryGetValue(out var o) ? o : 0UL;
            var sz = size.TryGetValue(out var s) ? s
                : (native != null ? nativeSize - off : 0UL);
            _backend.EnsureApi().RenderBundleEncoderSetVertexBuffer(
                _encoder, slot, native, off, sz);
        }

        public void Draw(uint vertexCount, Option<uint> instanceCount,
            Option<uint> firstVertex, Option<uint> firstInstance)
        {
            EnsureLive();
            _backend.EnsureApi().RenderBundleEncoderDraw(_encoder,
                vertexCount,
                instanceCount.TryGetValue(out var ic) ? ic : 1u,
                firstVertex.TryGetValue(out var fv) ? fv : 0u,
                firstInstance.TryGetValue(out var fi) ? fi : 0u);
        }

        public void DrawIndexed(uint indexCount, Option<uint> instanceCount,
            Option<uint> firstIndex, Option<int> baseVertex,
            Option<uint> firstInstance)
        {
            EnsureLive();
            _backend.EnsureApi().RenderBundleEncoderDrawIndexed(_encoder,
                indexCount,
                instanceCount.TryGetValue(out var ic) ? ic : 1u,
                firstIndex.TryGetValue(out var fi) ? fi : 0u,
                baseVertex.TryGetValue(out var bv) ? bv : 0,
                firstInstance.TryGetValue(out var fin) ? fin : 0u);
        }

        public void DrawIndirect(GenWebgpu.IGpuBuffer indirectBuffer,
            ulong indirectOffset)
        {
            EnsureLive();
            if (indirectBuffer is not SilkGpuBuffer sb)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "RenderBundleEncoder.DrawIndirect: not Silk-backed.");
            _backend.EnsureApi().RenderBundleEncoderDrawIndirect(
                _encoder, sb.Native, indirectOffset);
        }

        public void DrawIndexedIndirect(GenWebgpu.IGpuBuffer indirectBuffer,
            ulong indirectOffset)
        {
            EnsureLive();
            if (indirectBuffer is not SilkGpuBuffer sb)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "RenderBundleEncoder.DrawIndexedIndirect: not Silk-backed.");
            _backend.EnsureApi().RenderBundleEncoderDrawIndexedIndirect(
                _encoder, sb.Native, indirectOffset);
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
                            Message = "SetBindGroup: non-Silk-backed bind group.",
                        });
                native = sbg.Native;
            }
            var offs = dynamicOffsets.TryGetValue(out var arr) && arr != null
                ? arr : Array.Empty<uint>();
            var start = dynamicOffsetsDataStart.TryGetValue(out var s) ? (uint)s : 0u;
            var len = dynamicOffsetsDataLength.TryGetValue(out var ln) ? ln
                : (uint)offs.Length;
            if (start > offs.Length || start + len > offs.Length)
                return Result<Unit, GenWebgpu.SetBindGroupError>.FromErr(
                    new GenWebgpu.SetBindGroupError
                    {
                        Kind = new GenWebgpu.SetBindGroupErrorKind
                            .SetBindGroupErrorKindRangeError(),
                        Message = $"SetBindGroup: dynamic-offsets window "
                            + $"[start={start}, len={len}] out of bounds "
                            + $"for list length {offs.Length}.",
                    });
            if (len == 0)
            {
                _backend.EnsureApi().RenderBundleEncoderSetBindGroup(
                    _encoder, index, native, 0, null);
            }
            else
            {
                fixed (uint* p = &offs[start])
                {
                    _backend.EnsureApi().RenderBundleEncoderSetBindGroup(
                        _encoder, index, native, (nuint)len, p);
                }
            }
            return Result<Unit, GenWebgpu.SetBindGroupError>.FromOk(default);
        }

        public void PushDebugGroup(string groupLabel)
        {
            EnsureLive();
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                (groupLabel ?? string.Empty) + "\0");
            fixed (byte* p = bytes)
                _backend.EnsureApi().RenderBundleEncoderPushDebugGroup(_encoder, p);
        }
        public void PopDebugGroup()
        {
            EnsureLive();
            _backend.EnsureApi().RenderBundleEncoderPopDebugGroup(_encoder);
        }
        public void InsertDebugMarker(string markerLabel)
        {
            EnsureLive();
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                (markerLabel ?? string.Empty) + "\0");
            fixed (byte* p = bytes)
                _backend.EnsureApi().RenderBundleEncoderInsertDebugMarker(_encoder, p);
        }

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
                _backend.EnsureApi().RenderBundleEncoderSetLabel(_encoder, p);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_encoder != null)
            {
                _backend.EnsureApi().RenderBundleEncoderRelease(_encoder);
                _encoder = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _encoder == null)
                throw new ObjectDisposedException(nameof(SilkGpuRenderBundleEncoder));
        }
    }

    /// <summary>Silk-backed wrapper around a wgpu
    /// <c>RenderPassEncoder*</c>. Same recording surface as
    /// <see cref="SilkGpuRenderBundleEncoder"/> plus viewport /
    /// scissor / blend-constant / stencil-reference / occlusion-
    /// query / execute-bundles.</summary>
    internal sealed unsafe class SilkGpuRenderPassEncoder
        : GenWebgpu.IGpuRenderPassEncoder, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private RenderPassEncoder* _pass;
        private string _label = string.Empty;
        private bool _disposed;

        public SilkGpuRenderPassEncoder(SilkGpuBackend backend,
            RenderPassEncoder* pass)
        {
            _backend = backend;
            _pass = pass;
        }

        public void SetViewport(float x, float y, float width, float height,
            float minDepth, float maxDepth)
        {
            EnsureLive();
            _backend.EnsureApi().RenderPassEncoderSetViewport(
                _pass, x, y, width, height, minDepth, maxDepth);
        }

        public void SetScissorRect(uint x, uint y, uint width, uint height)
        {
            EnsureLive();
            _backend.EnsureApi().RenderPassEncoderSetScissorRect(
                _pass, x, y, width, height);
        }

        public void SetBlendConstant(GenWebgpu.GpuColor color)
        {
            EnsureLive();
            var c = new Color
            {
                R = color.R,
                G = color.G,
                B = color.B,
                A = color.A,
            };
            _backend.EnsureApi().RenderPassEncoderSetBlendConstant(_pass, &c);
        }

        public void SetStencilReference(uint reference)
        {
            EnsureLive();
            _backend.EnsureApi().RenderPassEncoderSetStencilReference(
                _pass, reference);
        }

        public void BeginOcclusionQuery(uint queryIndex)
        {
            EnsureLive();
            _backend.EnsureApi().RenderPassEncoderBeginOcclusionQuery(
                _pass, queryIndex);
        }

        public void EndOcclusionQuery()
        {
            EnsureLive();
            _backend.EnsureApi().RenderPassEncoderEndOcclusionQuery(_pass);
        }

        public void ExecuteBundles(GenWebgpu.IGpuRenderBundle[] bundles)
        {
            EnsureLive();
            bundles ??= Array.Empty<GenWebgpu.IGpuRenderBundle>();
            var count = bundles.Length;
            if (count == 0)
            {
                _backend.EnsureApi().RenderPassEncoderExecuteBundles(
                    _pass, 0, null);
                return;
            }
            if (count <= 16)
            {
                RenderBundle** stack = stackalloc RenderBundle*[count];
                for (int i = 0; i < count; i++)
                    stack[i] = ExtractNative(bundles[i], i);
                _backend.EnsureApi().RenderPassEncoderExecuteBundles(
                    _pass, (uint)count, stack);
            }
            else
            {
                var arr = new IntPtr[count];
                for (int i = 0; i < count; i++)
                    arr[i] = (IntPtr)ExtractNative(bundles[i], i);
                fixed (IntPtr* p = arr)
                {
                    _backend.EnsureApi().RenderPassEncoderExecuteBundles(
                        _pass, (uint)count, (RenderBundle**)p);
                }
            }
        }

        private static RenderBundle* ExtractNative(
            GenWebgpu.IGpuRenderBundle rb, int index)
        {
            if (rb is not SilkGpuRenderBundle s)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    $"ExecuteBundles: bundles[{index}] is not Silk-backed.");
            return s.Native;
        }

        public void End()
        {
            EnsureLive();
            _backend.EnsureApi().RenderPassEncoderEnd(_pass);
        }

        public void SetPipeline(GenWebgpu.IGpuRenderPipeline pipeline)
        {
            EnsureLive();
            if (pipeline is not SilkGpuRenderPipeline sp)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "RenderPass.SetPipeline: not Silk-backed.");
            _backend.EnsureApi().RenderPassEncoderSetPipeline(_pass, sp.Native);
        }

        public void SetIndexBuffer(GenWebgpu.IGpuBuffer buffer,
            GenWebgpu.GpuIndexFormat indexFormat,
            Option<ulong> offset, Option<ulong> size)
        {
            EnsureLive();
            if (buffer is not SilkGpuBuffer sb)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "RenderPass.SetIndexBuffer: not Silk-backed.");
            var off = offset.TryGetValue(out var o) ? o : 0UL;
            var sz = size.TryGetValue(out var s) ? s : sb.NativeSize - off;
            _backend.EnsureApi().RenderPassEncoderSetIndexBuffer(
                _pass, sb.Native,
                indexFormat == GenWebgpu.GpuIndexFormat.Uint16
                    ? IndexFormat.Uint16 : IndexFormat.Uint32,
                off, sz);
        }

        public void SetVertexBuffer(uint slot,
            Option<GenWebgpu.IGpuBuffer> buffer,
            Option<ulong> offset, Option<ulong> size)
        {
            EnsureLive();
            global::Silk.NET.WebGPU.Buffer* native = null;
            ulong nativeSize = 0;
            if (buffer.TryGetValue(out var b) && b != null)
            {
                if (b is not SilkGpuBuffer sb)
                    throw new Wacs.WASI.GFX.Types.WasiGfxException(
                        "RenderPass.SetVertexBuffer: not Silk-backed.");
                native = sb.Native;
                nativeSize = sb.NativeSize;
            }
            var off = offset.TryGetValue(out var o) ? o : 0UL;
            var sz = size.TryGetValue(out var s) ? s
                : (native != null ? nativeSize - off : 0UL);
            _backend.EnsureApi().RenderPassEncoderSetVertexBuffer(
                _pass, slot, native, off, sz);
        }

        public void Draw(uint vertexCount, Option<uint> instanceCount,
            Option<uint> firstVertex, Option<uint> firstInstance)
        {
            EnsureLive();
            _backend.EnsureApi().RenderPassEncoderDraw(_pass,
                vertexCount,
                instanceCount.TryGetValue(out var ic) ? ic : 1u,
                firstVertex.TryGetValue(out var fv) ? fv : 0u,
                firstInstance.TryGetValue(out var fi) ? fi : 0u);
        }

        public void DrawIndexed(uint indexCount, Option<uint> instanceCount,
            Option<uint> firstIndex, Option<int> baseVertex,
            Option<uint> firstInstance)
        {
            EnsureLive();
            _backend.EnsureApi().RenderPassEncoderDrawIndexed(_pass,
                indexCount,
                instanceCount.TryGetValue(out var ic) ? ic : 1u,
                firstIndex.TryGetValue(out var fi) ? fi : 0u,
                baseVertex.TryGetValue(out var bv) ? bv : 0,
                firstInstance.TryGetValue(out var fin) ? fin : 0u);
        }

        public void DrawIndirect(GenWebgpu.IGpuBuffer indirectBuffer,
            ulong indirectOffset)
        {
            EnsureLive();
            if (indirectBuffer is not SilkGpuBuffer sb)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "RenderPass.DrawIndirect: not Silk-backed.");
            _backend.EnsureApi().RenderPassEncoderDrawIndirect(
                _pass, sb.Native, indirectOffset);
        }

        public void DrawIndexedIndirect(GenWebgpu.IGpuBuffer indirectBuffer,
            ulong indirectOffset)
        {
            EnsureLive();
            if (indirectBuffer is not SilkGpuBuffer sb)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "RenderPass.DrawIndexedIndirect: not Silk-backed.");
            _backend.EnsureApi().RenderPassEncoderDrawIndexedIndirect(
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
                            Message = "RenderPass.SetBindGroup: non-Silk-backed.",
                        });
                native = sbg.Native;
            }
            var offs = dynamicOffsets.TryGetValue(out var arr) && arr != null
                ? arr : Array.Empty<uint>();
            var start = dynamicOffsetsDataStart.TryGetValue(out var s) ? (uint)s : 0u;
            var len = dynamicOffsetsDataLength.TryGetValue(out var ln) ? ln
                : (uint)offs.Length;
            if (start > offs.Length || start + len > offs.Length)
                return Result<Unit, GenWebgpu.SetBindGroupError>.FromErr(
                    new GenWebgpu.SetBindGroupError
                    {
                        Kind = new GenWebgpu.SetBindGroupErrorKind
                            .SetBindGroupErrorKindRangeError(),
                        Message = $"RenderPass.SetBindGroup: window "
                            + $"[start={start}, len={len}] out of bounds.",
                    });
            if (len == 0)
            {
                _backend.EnsureApi().RenderPassEncoderSetBindGroup(
                    _pass, index, native, 0, null);
            }
            else
            {
                fixed (uint* p = &offs[start])
                {
                    _backend.EnsureApi().RenderPassEncoderSetBindGroup(
                        _pass, index, native, (nuint)len, p);
                }
            }
            return Result<Unit, GenWebgpu.SetBindGroupError>.FromOk(default);
        }

        public void PushDebugGroup(string groupLabel)
        {
            EnsureLive();
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                (groupLabel ?? string.Empty) + "\0");
            fixed (byte* p = bytes)
                _backend.EnsureApi().RenderPassEncoderPushDebugGroup(_pass, p);
        }
        public void PopDebugGroup()
        {
            EnsureLive();
            _backend.EnsureApi().RenderPassEncoderPopDebugGroup(_pass);
        }
        public void InsertDebugMarker(string markerLabel)
        {
            EnsureLive();
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                (markerLabel ?? string.Empty) + "\0");
            fixed (byte* p = bytes)
                _backend.EnsureApi().RenderPassEncoderInsertDebugMarker(_pass, p);
        }

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
                _backend.EnsureApi().RenderPassEncoderSetLabel(_pass, p);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_pass != null)
            {
                _backend.EnsureApi().RenderPassEncoderRelease(_pass);
                _pass = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _pass == null)
                throw new ObjectDisposedException(nameof(SilkGpuRenderPassEncoder));
        }
    }
}
