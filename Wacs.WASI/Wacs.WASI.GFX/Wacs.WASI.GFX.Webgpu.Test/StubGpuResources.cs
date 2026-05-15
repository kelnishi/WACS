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
        public void Submit(IGpuCommandBuffer[] commandBuffers)
            => throw new NotImplementedException();
        public void OnSubmittedWorkDone()
            => throw new NotImplementedException();
        public Result<Unit, WriteBufferError> WriteBufferWithCopy(
            IGpuBuffer buffer, ulong bufferOffset, byte[] data,
            Option<ulong> dataOffset, Option<ulong> size)
            => throw new NotImplementedException();
        public void WriteTextureWithCopy(
            GpuTexelCopyTextureInfo destination, byte[] data,
            GpuTexelCopyBufferLayout dataLayout, GpuExtent3D size)
            => throw new NotImplementedException();
        public string Label() => "stub-queue";
        public void SetLabel(string label) { }
    }

    internal sealed class StubGpuDeviceLostInfo : IGpuDeviceLostInfo
    {
        public GpuDeviceLostReason Reason() => default;
        public string Message() => string.Empty;
    }
}
