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
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Silk-backed wrapper around a wgpu <c>Device*</c>. Holds
    /// the native device handle plus the adapter it came from
    /// (some queries forward to the adapter — features /
    /// limits / adapter-info follow the wgpu pattern of "ask the
    /// adapter, not the device").
    ///
    /// <para>The descriptor-decoding work for the create-*
    /// methods lands across follow-up commits — this commit
    /// ships the device-handle skeleton so the request-device
    /// chain returns a real wgpu Device that downstream resource
    /// work can attach to.</para>
    /// </summary>
    internal sealed unsafe class SilkGpuDevice : GenWebgpu.IGpuDevice, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private readonly Adapter* _adapter;
        private Device* _device;
        private Queue* _queue;
        private SilkGpuQueue? _queueWrapper;
        private string _label = string.Empty;
        private bool _disposed;

        public SilkGpuDevice(
            SilkGpuBackend backend, Adapter* adapter, Device* device)
        {
            _backend = backend;
            _adapter = adapter;
            _device = device;
        }

        internal Device* Native => _device;

        public GenWebgpu.IGpuSupportedFeatures Features()
        {
            EnsureLive();
            return new SilkGpuSupportedFeatures(_backend, _adapter);
        }

        public GenWebgpu.IGpuSupportedLimits Limits()
        {
            EnsureLive();
            return new SilkGpuSupportedLimits(_backend, _adapter);
        }

        public GenWebgpu.IGpuAdapterInfo AdapterInfo()
        {
            EnsureLive();
            return new SilkGpuAdapterInfo(_backend, _adapter);
        }

        public GenWebgpu.IGpuQueue Queue()
        {
            EnsureLive();
            if (_queueWrapper != null) return _queueWrapper;
            _queue = _backend.EnsureApi().DeviceGetQueue(_device);
            if (_queue == null)
                throw new Wacs.WASI.GFX.Types.WasiGfxException(
                    "SilkGpuDevice.Queue: wgpuDeviceGetQueue returned "
                    + "null. The device handle is alive but no queue "
                    + "was attached — wgpu-native treats this as a "
                    + "fatal error on the driver side.");
            return _queueWrapper = new SilkGpuQueue(_backend, _queue);
        }

        public void Destroy()
        {
            // wgpu-native distinguishes destroy (driver-side
            // teardown) from release (refcount drop). The WIT's
            // destroy maps to the former; the wrapper's IDisposable
            // does the latter. wgpu-native exposes destroy via
            // wgpuDeviceDestroy, which Silk binds.
            EnsureLive();
            // The Silk method name varies by version; reach for the
            // function via reflection-free direct call. wgpu's
            // device-destroy in 2.23.0 surfaces as DeviceDestroy.
            _backend.EnsureApi().DeviceDestroy(_device);
        }

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            // Silk has a DeviceSetLabel(byte*) overload; the round-
            // trip through wgpu is best-effort (wgpu's label is
            // for debug-marker output, the WIT's get-label is what
            // the guest reads back). We track the label on our side
            // for the WIT round-trip and forward to wgpu for
            // diagnostics.
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().DeviceSetLabel(_device, p);
            }
        }

        public GenWebgpu.IGpuDeviceLostInfo Lost()
        {
            // wgpu-native exposes device-lost via callback; the
            // WIT's lost() is a polled query. Until the callback
            // wiring lands, report "no loss" via a synthetic stub.
            return new SilkGpuDeviceLostInfo();
        }

        public void PushErrorScope(GenWebgpu.GpuErrorFilter filter)
        {
            EnsureLive();
            var f = filter switch
            {
                GenWebgpu.GpuErrorFilter.Validation => ErrorFilter.Validation,
                GenWebgpu.GpuErrorFilter.OutOfMemory => ErrorFilter.OutOfMemory,
                GenWebgpu.GpuErrorFilter.Internal => ErrorFilter.Internal,
                _ => ErrorFilter.Validation,
            };
            _backend.EnsureApi().DevicePushErrorScope(_device, f);
        }

        public Result<Option<GenWebgpu.IGpuError>, GenWebgpu.PopErrorScopeError>
            PopErrorScope()
        {
            // wgpu-native's PopErrorScope is callback-based; the
            // sync-bridge path needs a wgpuDevicePoll loop because
            // the callback isn't synchronous (unlike adapter /
            // device request). Defer until the
            // poll-and-pump infrastructure lands.
            throw new PlatformNotSupportedException(
                "SilkGpuDevice.PopErrorScope: callback-driven; "
                + "needs wgpu-poll infrastructure to bridge to "
                + "sync return. Not yet wired.");
        }

        public IPollable OnuncapturederrorSubscribe()
        {
            // wgpu's uncaptured-error path is a registered C
            // callback on the device; converting it to a polled
            // wasi:io.Pollable requires routing the callback into
            // an event ring. Defer until the wgpu-poll
            // infrastructure lands.
            throw new PlatformNotSupportedException(
                "SilkGpuDevice.OnuncapturederrorSubscribe: not yet "
                + "wired — needs wgpu-error callback → Pollable "
                + "bridge.");
        }

        public void ConnectGraphicsContext(
            global::Wacs.WASI.GFX.GraphicsContext.IContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            EnsureLive();
            // Surface configuration: this is where the wgpu side
            // imports the wasi-gfx context's underlying SDL window
            // as a wgpu Surface and configures it against this
            // device. The actual surface-import path needs the
            // SDL native handle from the wasi-gfx side, which lives
            // on the WasiGfxSilkBindable. Defer.
            throw new PlatformNotSupportedException(
                "SilkGpuDevice.ConnectGraphicsContext: not yet "
                + "wired — needs wgpu surface-import path from "
                + "the wasi-gfx-side SDL window handle.");
        }

        // ===== create-* methods land in follow-up commits =====
        // Each method needs the descriptor flat-form decoding
        // (the WitBindings.cs side passes a default-constructed
        // record today). The methods below throw with a clear
        // "descriptor decoder lands in next commit" signal so the
        // host binding's wire-form decode can still proceed and
        // the throw surfaces as the bridge to wgpu-native.

        private const string DispatchPending
            = "wgpu-native dispatch path landing in follow-up commits. "
              + "The binding's wire-form decoding works; the "
              + "descriptor → wgpu translation is the next step.";

        public GenWebgpu.IGpuBuffer CreateBuffer(
            GenWebgpu.GpuBufferDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateBuffer: " + DispatchPending);
        public GenWebgpu.IGpuTexture CreateTexture(
            GenWebgpu.GpuTextureDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateTexture: " + DispatchPending);
        public GenWebgpu.IGpuSampler CreateSampler(
            Option<GenWebgpu.GpuSamplerDescriptor> descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateSampler: " + DispatchPending);
        public GenWebgpu.IGpuBindGroupLayout CreateBindGroupLayout(
            GenWebgpu.GpuBindGroupLayoutDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateBindGroupLayout: " + DispatchPending);
        public GenWebgpu.IGpuPipelineLayout CreatePipelineLayout(
            GenWebgpu.GpuPipelineLayoutDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreatePipelineLayout: " + DispatchPending);
        public GenWebgpu.IGpuBindGroup CreateBindGroup(
            GenWebgpu.GpuBindGroupDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateBindGroup: " + DispatchPending);
        public GenWebgpu.IGpuShaderModule CreateShaderModule(
            GenWebgpu.GpuShaderModuleDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateShaderModule: " + DispatchPending);
        public GenWebgpu.IGpuComputePipeline CreateComputePipeline(
            GenWebgpu.GpuComputePipelineDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateComputePipeline: " + DispatchPending);
        public GenWebgpu.IGpuRenderPipeline CreateRenderPipeline(
            GenWebgpu.GpuRenderPipelineDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateRenderPipeline: " + DispatchPending);
        public Result<GenWebgpu.IGpuComputePipeline, GenWebgpu.CreatePipelineError>
            CreateComputePipelineAsync(GenWebgpu.GpuComputePipelineDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateComputePipelineAsync: " + DispatchPending);
        public Result<GenWebgpu.IGpuRenderPipeline, GenWebgpu.CreatePipelineError>
            CreateRenderPipelineAsync(GenWebgpu.GpuRenderPipelineDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateRenderPipelineAsync: " + DispatchPending);
        public GenWebgpu.IGpuCommandEncoder CreateCommandEncoder(
            Option<GenWebgpu.GpuCommandEncoderDescriptor> descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateCommandEncoder: " + DispatchPending);
        public GenWebgpu.IGpuRenderBundleEncoder CreateRenderBundleEncoder(
            GenWebgpu.GpuRenderBundleEncoderDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateRenderBundleEncoder: " + DispatchPending);
        public Result<GenWebgpu.IGpuQuerySet, GenWebgpu.CreateQuerySetError>
            CreateQuerySet(GenWebgpu.GpuQuerySetDescriptor descriptor)
            => throw new PlatformNotSupportedException(
                "SilkGpuDevice.CreateQuerySet: " + DispatchPending);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queueWrapper?.Dispose();
            _queueWrapper = null;
            _queue = null;
            if (_device != null)
            {
                _backend.EnsureApi().DeviceRelease(_device);
                _device = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _device == null)
                throw new ObjectDisposedException(nameof(SilkGpuDevice));
        }
    }

    /// <summary>
    /// wgpu-backed queue wrapper. Submit / write-buffer / on-
    /// submitted-work-done land in follow-up commits alongside
    /// the command-buffer descriptor decode.
    /// </summary>
    internal sealed unsafe class SilkGpuQueue : GenWebgpu.IGpuQueue, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private Queue* _queue;
        private string _label = string.Empty;
        private bool _disposed;

        public SilkGpuQueue(SilkGpuBackend backend, Queue* queue)
        {
            _backend = backend;
            _queue = queue;
        }

        internal Queue* Native => _queue;

        public void Submit(GenWebgpu.IGpuCommandBuffer[] commandBuffers)
            => throw new PlatformNotSupportedException(
                "SilkGpuQueue.Submit: wgpu command-buffer lift "
                + "lands alongside the command-encoder wgpu wiring.");

        public void OnSubmittedWorkDone()
            => throw new PlatformNotSupportedException(
                "SilkGpuQueue.OnSubmittedWorkDone: callback-driven; "
                + "needs wgpu-poll infrastructure to bridge to sync.");

        public Result<Unit, GenWebgpu.WriteBufferError> WriteBufferWithCopy(
            GenWebgpu.IGpuBuffer buffer, ulong bufferOffset,
            byte[] data, Option<ulong> dataOffset, Option<ulong> size)
            => throw new PlatformNotSupportedException(
                "SilkGpuQueue.WriteBufferWithCopy: needs the "
                + "SilkGpuBuffer wgpu wrapper to land.");

        public void WriteTextureWithCopy(
            GenWebgpu.GpuTexelCopyTextureInfo destination,
            byte[] data,
            GenWebgpu.GpuTexelCopyBufferLayout dataLayout,
            GenWebgpu.GpuExtent3D size)
            => throw new PlatformNotSupportedException(
                "SilkGpuQueue.WriteTextureWithCopy: needs the "
                + "SilkGpuTexture wgpu wrapper to land.");

        public string Label() => _label;
        public void SetLabel(string label)
        {
            if (_disposed || _queue == null)
                throw new ObjectDisposedException(nameof(SilkGpuQueue));
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().QueueSetLabel(_queue, p);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_queue != null)
            {
                _backend.EnsureApi().QueueRelease(_queue);
                _queue = null;
            }
        }
    }

    /// <summary>Synthetic "device not lost" stub. Real
    /// callback-driven lost detection wires alongside the
    /// wgpu-poll infrastructure.</summary>
    internal sealed class SilkGpuDeviceLostInfo : GenWebgpu.IGpuDeviceLostInfo
    {
        public GenWebgpu.GpuDeviceLostReason Reason()
            => GenWebgpu.GpuDeviceLostReason.Unknown;
        public string Message() => string.Empty;
    }
}
