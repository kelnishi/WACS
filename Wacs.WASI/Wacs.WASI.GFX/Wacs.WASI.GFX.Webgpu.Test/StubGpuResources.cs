// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.GFX.Webgpu;
using Wacs.WASI.GFX.Webgpu.Webgpu;
using Wacs.WASI.Preview2.Io;
using GenIContext = Wacs.WASI.GFX.GraphicsContext.IContext;

namespace Wacs.WASI.GFX.Webgpu.Test
{
    /// <summary>
    /// Stub impls of every wasi:webgpu resource interface a v1
    /// phase 3 session 4 test exercises. Methods the bindings
    /// touch return deterministic stub values; methods bound by
    /// later sessions throw <see cref="NotImplementedException"/>
    /// so a session-N test calling a session-N+k method gets a
    /// clear "wire this next" signal instead of a silent bad
    /// result. The volume reflects the wasi:webgpu surface area —
    /// 38 resources / ~220 methods total; this file stubs only
    /// the resources sessions 3-4 reach.
    /// </summary>
    internal sealed class StubGpuAdapter : IGpuAdapter
    {
        public IGpuSupportedFeatures Features()
            => new StubGpuSupportedFeatures();
        public IGpuSupportedLimits Limits()
            => new StubGpuSupportedLimits();
        public IGpuAdapterInfo Info()
            => new StubGpuAdapterInfo();
        public bool IsFallbackAdapter() => false;
        public Result<IGpuDevice, RequestDeviceError> RequestDevice(
            Option<GpuDeviceDescriptor> descriptor)
            => Result<IGpuDevice, RequestDeviceError>.FromOk(
                new StubGpuDevice());
    }

    internal sealed class StubGpuDevice : IGpuDevice
    {
        private string _label = "stub-device";

        public IGpuSupportedFeatures Features()
            => new StubGpuSupportedFeatures();
        public IGpuSupportedLimits Limits()
            => new StubGpuSupportedLimits();
        public IGpuAdapterInfo AdapterInfo()
            => new StubGpuAdapterInfo();
        public IGpuQueue Queue() => new StubGpuQueue();
        public void Destroy() { /* no-op */ }
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? string.Empty; }
        public IGpuDeviceLostInfo Lost()
            => new StubGpuDeviceLostInfo();

        // Sessions 5-7: create-* / async / error-scope land
        // alongside the resource sessions that bind them at the
        // wire layer. Until then these stubs are unreachable
        // through any bound host function.
        public IGpuBuffer CreateBuffer(GpuBufferDescriptor descriptor)
            => throw new NotImplementedException();
        public IGpuTexture CreateTexture(GpuTextureDescriptor descriptor)
            => throw new NotImplementedException();
        public IGpuSampler CreateSampler(
            Option<GpuSamplerDescriptor> descriptor)
            => throw new NotImplementedException();
        public IGpuBindGroupLayout CreateBindGroupLayout(
            GpuBindGroupLayoutDescriptor descriptor)
            => throw new NotImplementedException();
        public IGpuPipelineLayout CreatePipelineLayout(
            GpuPipelineLayoutDescriptor descriptor)
            => throw new NotImplementedException();
        public IGpuBindGroup CreateBindGroup(
            GpuBindGroupDescriptor descriptor)
            => throw new NotImplementedException();
        public IGpuShaderModule CreateShaderModule(
            GpuShaderModuleDescriptor descriptor)
            => throw new NotImplementedException();
        public IGpuComputePipeline CreateComputePipeline(
            GpuComputePipelineDescriptor descriptor)
            => throw new NotImplementedException();
        public IGpuRenderPipeline CreateRenderPipeline(
            GpuRenderPipelineDescriptor descriptor)
            => throw new NotImplementedException();
        public Result<IGpuComputePipeline, CreatePipelineError>
            CreateComputePipelineAsync(GpuComputePipelineDescriptor descriptor)
            => throw new NotImplementedException();
        public Result<IGpuRenderPipeline, CreatePipelineError>
            CreateRenderPipelineAsync(GpuRenderPipelineDescriptor descriptor)
            => throw new NotImplementedException();
        public IGpuCommandEncoder CreateCommandEncoder(
            Option<GpuCommandEncoderDescriptor> descriptor)
            => throw new NotImplementedException();
        public IGpuRenderBundleEncoder CreateRenderBundleEncoder(
            GpuRenderBundleEncoderDescriptor descriptor)
            => throw new NotImplementedException();
        public Result<IGpuQuerySet, CreateQuerySetError> CreateQuerySet(
            GpuQuerySetDescriptor descriptor)
            => throw new NotImplementedException();
        public void PushErrorScope(GpuErrorFilter filter)
            => throw new NotImplementedException();
        public Result<Option<IGpuError>, PopErrorScopeError>
            PopErrorScope()
            => throw new NotImplementedException();
        public IPollable OnuncapturederrorSubscribe()
            => throw new NotImplementedException();
        public void ConnectGraphicsContext(GenIContext context)
            => throw new NotImplementedException();
    }

    internal sealed class StubGpuSupportedFeatures : IGpuSupportedFeatures
    {
        public bool Has(string value) => false;
    }

    internal sealed class StubGpuSupportedLimits : IGpuSupportedLimits
    {
        // wasm-spec defaults; real backends report driver-reported
        // values. Zero is a safe stub — guests checking the limits
        // see "nothing supported."
        public uint MaxTextureDimension1D() => 0;
        public uint MaxTextureDimension2D() => 0;
        public uint MaxTextureDimension3D() => 0;
        public uint MaxTextureArrayLayers() => 0;
        public uint MaxBindGroups() => 0;
        public uint MaxBindGroupsPlusVertexBuffers() => 0;
        public uint MaxBindingsPerBindGroup() => 0;
        public uint MaxDynamicUniformBuffersPerPipelineLayout() => 0;
        public uint MaxDynamicStorageBuffersPerPipelineLayout() => 0;
        public uint MaxSampledTexturesPerShaderStage() => 0;
        public uint MaxSamplersPerShaderStage() => 0;
        public uint MaxStorageBuffersPerShaderStage() => 0;
        public uint MaxStorageTexturesPerShaderStage() => 0;
        public uint MaxUniformBuffersPerShaderStage() => 0;
        public ulong MaxUniformBufferBindingSize() => 0;
        public ulong MaxStorageBufferBindingSize() => 0;
        public uint MinUniformBufferOffsetAlignment() => 0;
        public uint MinStorageBufferOffsetAlignment() => 0;
        public uint MaxVertexBuffers() => 0;
        public ulong MaxBufferSize() => 0;
        public uint MaxVertexAttributes() => 0;
        public uint MaxVertexBufferArrayStride() => 0;
        public uint MaxInterStageShaderVariables() => 0;
        public uint MaxColorAttachments() => 0;
        public uint MaxColorAttachmentBytesPerSample() => 0;
        public uint MaxComputeWorkgroupStorageSize() => 0;
        public uint MaxComputeInvocationsPerWorkgroup() => 0;
        public uint MaxComputeWorkgroupSizeX() => 0;
        public uint MaxComputeWorkgroupSizeY() => 0;
        public uint MaxComputeWorkgroupSizeZ() => 0;
        public uint MaxComputeWorkgroupsPerDimension() => 0;
    }

    internal sealed class StubGpuAdapterInfo : IGpuAdapterInfo
    {
        public string Vendor() => "wacs-stub";
        public string Architecture() => "test";
        public string Device() => "stub-device";
        public string Description() => "headless test stub";
        public uint SubgroupMinSize() => 0;
        public uint SubgroupMaxSize() => 0;
        public bool IsFallbackAdapter() => false;
    }

    internal sealed class StubGpuQueue : IGpuQueue
    {
        private string _label = "stub-queue";
        public System.Collections.Generic.List<IGpuCommandBuffer[]> Submissions { get; } = new();
        public int OnSubmittedCalls { get; private set; }
        public void Submit(IGpuCommandBuffer[] commandBuffers)
        {
            Submissions.Add(commandBuffers);
        }
        public void OnSubmittedWorkDone() { OnSubmittedCalls++; }
        /// <summary>Most recent (buffer, offset, data) passed to
        /// <see cref="WriteBufferWithCopy"/>. Tests verify the
        /// canonical-ABI list<u8> decode pinned the right bytes.</summary>
        public (IGpuBuffer buffer, ulong offset, byte[] data,
            Option<ulong> dataOffset, Option<ulong> size)? LastWriteBuffer
        { get; private set; }
        public Result<Unit, WriteBufferError> WriteBufferWithCopy(
            IGpuBuffer buffer, ulong bufferOffset, byte[] data,
            Option<ulong> dataOffset, Option<ulong> size)
        {
            LastWriteBuffer = (buffer, bufferOffset, data,
                dataOffset, size);
            return Result<Unit, WriteBufferError>.FromOk(Unit.Value);
        }
        public void WriteTextureWithCopy(
            GpuTexelCopyTextureInfo destination, byte[] data,
            GpuTexelCopyBufferLayout dataLayout, GpuExtent3D size)
            => throw new NotImplementedException();
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
    }

    internal sealed class StubGpuDeviceLostInfo : IGpuDeviceLostInfo
    {
        public GpuDeviceLostReason Reason() => default;
        public string Message() => string.Empty;
    }

    // ---- Session 5 resources --------------------------------

    internal sealed class StubGpuBuffer : IGpuBuffer
    {
        private string _label = "stub-buffer";
        private bool _destroyed;
        public ulong Size() => 0;
        public uint Usage() => 0;
        public GpuBufferMapState MapState()
            => GpuBufferMapState.Unmapped;
        public void Destroy() { _destroyed = true; }
        public bool Destroyed => _destroyed;
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }

        // result<_, error> shapes — drive Ok/Err via the public
        // toggles so tests can verify both retArea paths.
        public bool FailWithUnmapError { get; set; }
        public bool FailWithMapAsyncError { get; set; }

        public Result<Unit, MapAsyncError> MapAsync(
            uint mode, Option<ulong> offset, Option<ulong> size)
        {
            if (FailWithMapAsyncError)
                return Result<Unit, MapAsyncError>.FromErr(
                    new MapAsyncError
                    {
                        Kind = new MapAsyncErrorKind
                            .MapAsyncErrorKindRangeError(),
                        Message = "stub: out of range",
                    });
            return Result<Unit, MapAsyncError>.FromOk(Unit.Value);
        }
        public Result<byte[], GetMappedRangeError>
            GetMappedRangeGetWithCopy(Option<ulong> offset,
                Option<ulong> size)
            => Result<byte[], GetMappedRangeError>.FromOk(
                Array.Empty<byte>());
        public Result<Unit, UnmapError> Unmap()
        {
            if (FailWithUnmapError)
                return Result<Unit, UnmapError>.FromErr(
                    new UnmapError
                    {
                        Kind = new UnmapErrorKind
                            .UnmapErrorKindAbortError(),
                        Message = "stub: aborted",
                    });
            return Result<Unit, UnmapError>.FromOk(Unit.Value);
        }
        /// <summary>Captures the last data buffer handed to
        /// <see cref="GetMappedRangeSetWithCopy"/> so tests can
        /// verify the canonical-ABI list<u8> decode landed
        /// correctly.</summary>
        public byte[]? LastSetData { get; private set; }
        public Result<Unit, GetMappedRangeError>
            GetMappedRangeSetWithCopy(byte[] data,
                Option<ulong> offset, Option<ulong> size)
        {
            LastSetData = data;
            return Result<Unit, GetMappedRangeError>.FromOk(Unit.Value);
        }
    }

    internal sealed class StubGpuShaderModule : IGpuShaderModule
    {
        private string _label = "stub-shader";
        public IGpuCompilationInfo GetCompilationInfo()
            => throw new NotImplementedException();
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
    }

    internal sealed class StubGpuPipelineLayout : IGpuPipelineLayout
    {
        private string _label = "stub-pipeline-layout";
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
    }

    internal sealed class StubGpuBindGroupLayout : IGpuBindGroupLayout
    {
        private string _label = "stub-bind-group-layout";
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
    }

    internal sealed class StubGpuBindGroup : IGpuBindGroup
    {
        private string _label = "stub-bind-group";
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
    }

    // ---- Session 6: compute path ----------------------------

    internal sealed class StubGpuComputePipeline : IGpuComputePipeline
    {
        private string _label = "stub-compute-pipeline";
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
        public IGpuBindGroupLayout GetBindGroupLayout(uint index)
            => new StubGpuBindGroupLayout();
    }

    internal sealed class StubGpuCommandEncoder : IGpuCommandEncoder
    {
        private string _label = "stub-command-encoder";
        public IGpuRenderPassEncoder BeginRenderPass(
            GpuRenderPassDescriptor descriptor)
            => throw new NotImplementedException();
        public IGpuComputePassEncoder BeginComputePass(
            Option<GpuComputePassDescriptor> descriptor)
            => new StubGpuComputePassEncoder();
        public void CopyBufferToBuffer(IGpuBuffer source,
            ulong sourceOffset, IGpuBuffer destination,
            ulong destinationOffset, ulong size) { }
        public void CopyBufferToTexture(GpuTexelCopyBufferInfo source,
            GpuTexelCopyTextureInfo destination, GpuExtent3D copySize)
            => throw new NotImplementedException();
        public void CopyTextureToBuffer(GpuTexelCopyTextureInfo source,
            GpuTexelCopyBufferInfo destination, GpuExtent3D copySize)
            => throw new NotImplementedException();
        public void CopyTextureToTexture(GpuTexelCopyTextureInfo source,
            GpuTexelCopyTextureInfo destination, GpuExtent3D copySize)
            => throw new NotImplementedException();
        public void ClearBuffer(IGpuBuffer buffer,
            Option<ulong> offset, Option<ulong> size) { }
        public void ResolveQuerySet(IGpuQuerySet querySet,
            uint firstQuery, uint queryCount, IGpuBuffer destination,
            ulong destinationOffset)
            => throw new NotImplementedException();
        public IGpuCommandBuffer Finish(
            Option<GpuCommandBufferDescriptor> descriptor)
            => new StubGpuCommandBuffer();
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
        public void PushDebugGroup(string groupLabel) { }
        public void PopDebugGroup() { }
        public void InsertDebugMarker(string markerLabel) { }
    }

    internal sealed class StubGpuComputePassEncoder : IGpuComputePassEncoder
    {
        private string _label = "stub-compute-pass";
        public IGpuComputePipeline? LastPipeline { get; private set; }
        public (uint x, Option<uint> y, Option<uint> z)? LastDispatch
        { get; private set; }
        public bool Ended { get; private set; }

        public void SetPipeline(IGpuComputePipeline pipeline)
        {
            LastPipeline = pipeline;
        }
        public void DispatchWorkgroups(uint workgroupCountX,
            Option<uint> workgroupCountY, Option<uint> workgroupCountZ)
        {
            LastDispatch = (workgroupCountX, workgroupCountY, workgroupCountZ);
        }
        public void DispatchWorkgroupsIndirect(IGpuBuffer indirectBuffer,
            ulong indirectOffset) { }
        public void End() { Ended = true; }
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
        public void PushDebugGroup(string groupLabel) { }
        public void PopDebugGroup() { }
        public void InsertDebugMarker(string markerLabel) { }
        public Result<Unit, SetBindGroupError> SetBindGroup(uint index,
            Option<IGpuBindGroup> bindGroup,
            Option<uint[]> dynamicOffsetsData,
            Option<ulong> dynamicOffsetsDataStart,
            Option<uint> dynamicOffsetsDataLength)
            => Result<Unit, SetBindGroupError>.FromOk(Unit.Value);
    }

    internal sealed class StubGpuCommandBuffer : IGpuCommandBuffer
    {
        private string _label = "stub-command-buffer";
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
    }

    // ---- Session 7: render path resources -------------------

    internal sealed class StubGpuTexture : IGpuTexture
    {
        private string _label = "stub-texture";
        public bool Destroyed { get; private set; }

        public IGpuTextureView CreateView(
            Option<GpuTextureViewDescriptor> descriptor)
            => new StubGpuTextureView();
        public void Destroy() { Destroyed = true; }
        public uint Width() => 0;
        public uint Height() => 0;
        public uint DepthOrArrayLayers() => 1;
        public uint MipLevelCount() => 1;
        public uint SampleCount() => 1;
        public GpuTextureDimension Dimension()
            => GpuTextureDimension.D2;
        public GpuTextureFormat Format()
            => GpuTextureFormat.Rgba8unorm;
        public uint Usage() => 0;
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
    }

    internal sealed class StubGpuTextureView : IGpuTextureView
    {
        private string _label = "stub-texture-view";
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
    }

    internal sealed class StubGpuSampler : IGpuSampler
    {
        private string _label = "stub-sampler";
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
    }

    internal sealed class StubGpuRenderPipeline : IGpuRenderPipeline
    {
        private string _label = "stub-render-pipeline";
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
        public IGpuBindGroupLayout GetBindGroupLayout(uint index)
            => new StubGpuBindGroupLayout();
    }

    internal sealed class StubGpuRenderBundle : IGpuRenderBundle
    {
        private string _label = "stub-render-bundle";
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
    }

    // ---- Render-pass-encoder + render-bundle-encoder --------

    internal sealed class StubGpuRenderPassEncoder
        : IGpuRenderPassEncoder
    {
        private string _label = "stub-render-pass";
        public (float x, float y, float w, float h,
            float minD, float maxD)? LastViewport { get; private set; }
        public (uint x, uint y, uint w, uint h)? LastScissor
        { get; private set; }
        public GpuColor? LastBlend { get; private set; } // nullable ref
        public uint? LastStencil { get; private set; }
        public uint? LastOcclusionQuery { get; private set; }
        public bool OcclusionEnded { get; private set; }
        public IGpuRenderBundle[]? LastBundles { get; private set; }
        public bool Ended { get; private set; }
        public IGpuRenderPipeline? LastPipeline { get; private set; }
        public (uint vc, Option<uint> ic,
            Option<uint> firstV, Option<uint> firstI)? LastDraw
        { get; private set; }
        public (uint ic, Option<uint> instc, Option<uint> firstI,
            Option<int> baseV, Option<uint> firstInst)? LastDrawIndexed
        { get; private set; }

        public void SetViewport(float x, float y, float width,
            float height, float minDepth, float maxDepth)
            => LastViewport = (x, y, width, height, minDepth, maxDepth);
        public void SetScissorRect(uint x, uint y, uint width, uint height)
            => LastScissor = (x, y, width, height);
        public void SetBlendConstant(GpuColor color)
            => LastBlend = color;
        public void SetStencilReference(uint reference)
            => LastStencil = reference;
        public void BeginOcclusionQuery(uint queryIndex)
            => LastOcclusionQuery = queryIndex;
        public void EndOcclusionQuery() { OcclusionEnded = true; }
        public void ExecuteBundles(IGpuRenderBundle[] bundles)
            => LastBundles = bundles;
        public void End() { Ended = true; }
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
        public void PushDebugGroup(string groupLabel) { }
        public void PopDebugGroup() { }
        public void InsertDebugMarker(string markerLabel) { }
        public Result<Unit, SetBindGroupError> SetBindGroup(uint index,
            Option<IGpuBindGroup> bindGroup,
            Option<uint[]> dynamicOffsetsData,
            Option<ulong> dynamicOffsetsDataStart,
            Option<uint> dynamicOffsetsDataLength)
            => Result<Unit, SetBindGroupError>.FromOk(Unit.Value);
        public void SetPipeline(IGpuRenderPipeline pipeline)
            => LastPipeline = pipeline;
        public void SetIndexBuffer(IGpuBuffer buffer,
            GpuIndexFormat indexFormat, Option<ulong> offset,
            Option<ulong> size) { }
        public void SetVertexBuffer(uint slot,
            Option<IGpuBuffer> buffer, Option<ulong> offset,
            Option<ulong> size) { }
        public void Draw(uint vertexCount, Option<uint> instanceCount,
            Option<uint> firstVertex, Option<uint> firstInstance)
            => LastDraw = (vertexCount, instanceCount,
                firstVertex, firstInstance);
        public void DrawIndexed(uint indexCount,
            Option<uint> instanceCount, Option<uint> firstIndex,
            Option<int> baseVertex, Option<uint> firstInstance)
            => LastDrawIndexed = (indexCount, instanceCount,
                firstIndex, baseVertex, firstInstance);
        public void DrawIndirect(IGpuBuffer indirectBuffer,
            ulong indirectOffset) { }
        public void DrawIndexedIndirect(IGpuBuffer indirectBuffer,
            ulong indirectOffset) { }
    }

    internal sealed class StubGpuRenderBundleEncoder
        : IGpuRenderBundleEncoder
    {
        private string _label = "stub-render-bundle-encoder";
        public IGpuRenderBundle Finish(
            Option<GpuRenderBundleDescriptor> descriptor)
            => new StubGpuRenderBundle();
        public string Label() => _label;
        public void SetLabel(string label) { _label = label ?? ""; }
        public void PushDebugGroup(string groupLabel) { }
        public void PopDebugGroup() { }
        public void InsertDebugMarker(string markerLabel) { }
        public Result<Unit, SetBindGroupError> SetBindGroup(uint index,
            Option<IGpuBindGroup> bindGroup,
            Option<uint[]> dynamicOffsetsData,
            Option<ulong> dynamicOffsetsDataStart,
            Option<uint> dynamicOffsetsDataLength)
            => Result<Unit, SetBindGroupError>.FromOk(Unit.Value);
        public void SetPipeline(IGpuRenderPipeline pipeline) { }
        public void SetIndexBuffer(IGpuBuffer buffer,
            GpuIndexFormat indexFormat, Option<ulong> offset,
            Option<ulong> size) { }
        public void SetVertexBuffer(uint slot,
            Option<IGpuBuffer> buffer, Option<ulong> offset,
            Option<ulong> size) { }
        public void Draw(uint vertexCount, Option<uint> instanceCount,
            Option<uint> firstVertex, Option<uint> firstInstance) { }
        public void DrawIndexed(uint indexCount,
            Option<uint> instanceCount, Option<uint> firstIndex,
            Option<int> baseVertex, Option<uint> firstInstance) { }
        public void DrawIndirect(IGpuBuffer indirectBuffer,
            ulong indirectOffset) { }
        public void DrawIndexedIndirect(IGpuBuffer indirectBuffer,
            ulong indirectOffset) { }
    }
}
