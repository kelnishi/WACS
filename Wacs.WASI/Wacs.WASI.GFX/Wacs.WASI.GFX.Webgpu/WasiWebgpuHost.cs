// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.GFX.HostBinding;
using Wacs.WASI.GFX.Types;
using IGpu = Wacs.WASI.GFX.Webgpu.Webgpu.IGpu;

namespace Wacs.WASI.GFX.Webgpu
{
    /// <summary>
    /// Top-level wasi-webgpu host. Mirrors
    /// <see cref="WasiGfxHost"/> — owns the per-instance state
    /// (resource handle tables + configuration) and exposes
    /// <see cref="BindToRuntime"/> to wire imports onto a
    /// <see cref="WasmRuntime"/>.
    ///
    /// <para>v1 phase 3 session 2: scaffolding only. The
    /// <see cref="BindToRuntime"/> delegation to
    /// <c>WitBindings.Bind</c> is in place but the per-resource
    /// canonical-ABI host functions land across sessions 3-6.
    /// A host constructed here is harmless — its
    /// <see cref="BindToRuntime"/> wires zero imports until then.</para>
    ///
    /// <para>Resource tables are one-per-wasi:webgpu resource.
    /// Webgpu has 38 resources; tables are created lazily on
    /// first use to keep no-webgpu embedders' constructor cost
    /// flat.</para>
    /// </summary>
    public sealed class WasiWebgpuHost : IBindable, IDisposable
    {
        private readonly WasiWebgpuConfiguration _config;
        private IGpu? _gpu;

        // Per-resource tables. ResourceTable is the same WACS.WASI.GFX
        // implementation — same handle-allocator + drop semantics.
        // Each is constructed eagerly so direct-link emit's
        // TryFindResourceImpl can resolve impl classes against the
        // expected interface types at transpile time.
        internal ResourceTable Gpus { get; } = new();
        internal ResourceTable Adapters { get; } = new();
        internal ResourceTable Devices { get; } = new();
        internal ResourceTable Queues { get; } = new();
        internal ResourceTable Buffers { get; } = new();
        internal ResourceTable Textures { get; } = new();
        internal ResourceTable TextureViews { get; } = new();
        internal ResourceTable Samplers { get; } = new();
        internal ResourceTable BindGroupLayouts { get; } = new();
        internal ResourceTable BindGroups { get; } = new();
        internal ResourceTable PipelineLayouts { get; } = new();
        internal ResourceTable ShaderModules { get; } = new();
        internal ResourceTable ComputePipelines { get; } = new();
        internal ResourceTable RenderPipelines { get; } = new();
        internal ResourceTable CommandBuffers { get; } = new();
        internal ResourceTable CommandEncoders { get; } = new();
        internal ResourceTable ComputePassEncoders { get; } = new();
        internal ResourceTable RenderPassEncoders { get; } = new();
        internal ResourceTable RenderBundles { get; } = new();
        internal ResourceTable RenderBundleEncoders { get; } = new();
        internal ResourceTable QuerySets { get; } = new();
        internal ResourceTable CanvasContexts { get; } = new();
        internal ResourceTable AdapterInfos { get; } = new();
        internal ResourceTable SupportedLimits { get; } = new();
        internal ResourceTable SupportedFeatures { get; } = new();
        internal ResourceTable WgslLanguageFeatures { get; } = new();
        internal ResourceTable CompilationMessages { get; } = new();
        internal ResourceTable CompilationInfos { get; } = new();
        internal ResourceTable DeviceLostInfos { get; } = new();
        internal ResourceTable Errors { get; } = new();
        internal ResourceTable UncapturedErrorEvents { get; } = new();

        public WasiWebgpuHost()
            : this(WasiWebgpuConfiguration.DefaultConfiguration()) { }

        public WasiWebgpuHost(WasiWebgpuConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>The backend driving this host. Null until
        /// configured.</summary>
        public IGpuBackend? Backend => _config.Backend;

        /// <summary>The cross-host abstract-buffer resolver
        /// configured at construction. Used by the
        /// <c>[static]gpu-texture.from-graphics-buffer</c>
        /// binding to look up the wasi-gfx-side handle.
        /// Null when no graphics-context bridge is wired (pure-
        /// webgpu embedders).</summary>
        internal Func<int, IAbstractBuffer?>? AbstractBufferResolver
            => _config.AbstractBufferResolver;

        internal Func<int, Wacs.WASI.GFX.GraphicsContext.IContext?>?
            GraphicsContextResolver
            => _config.GraphicsContextResolver;

        /// <summary>v1: shared Preview2 resource context the
        /// host plumbs through. Needed by binding sites that mint
        /// wasi:io.Pollable resources into Preview2's table — the
        /// only path their handles can flow through wasi:io/poll
        /// .poll on the host side.</summary>
        internal Wacs.WASI.Preview2.HostBinding.ResourceContext?
            SharedResources => _config.SharedResources;

        /// <summary>The lazily-constructed singleton <c>gpu</c>
        /// resource — the wasi:webgpu spec's
        /// <c>navigator.gpu</c> equivalent. Null when no backend
        /// is configured; constructed on first
        /// <see cref="GetOrCreateGpu"/> call.</summary>
        internal IGpu? Gpu => _gpu;

        internal IGpu GetOrCreateGpu()
        {
            if (_gpu != null) return _gpu;
            if (_config.Backend == null)
                throw new WasiGfxException(
                    "Cannot construct wasi:webgpu gpu resource: no "
                    + "IGpuBackend configured. Set "
                    + "WasiWebgpuConfiguration.Backend before "
                    + "constructing the host.");
            _gpu = _config.Backend.CreateGpu();
            return _gpu;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (_config.Backend == null)
                throw new WasiGfxException(
                    "Cannot bind wasi-webgpu: no backend configured. "
                    + "Set WasiWebgpuConfiguration.Backend before "
                    + "constructing the host.");
            WitBindings.Bind(runtime, this);
        }

        public void Dispose()
        {
            // Drop in reverse-dependency order: per-frame ephemera
            // first (encoders / passes / bundles), then long-lived
            // resources (pipelines / buffers / textures), then
            // adapter / device, finally the gpu singleton + the
            // backend itself.
            UncapturedErrorEvents.Clear();
            Errors.Clear();
            DeviceLostInfos.Clear();
            CompilationInfos.Clear();
            CompilationMessages.Clear();
            QuerySets.Clear();
            RenderBundleEncoders.Clear();
            RenderBundles.Clear();
            RenderPassEncoders.Clear();
            ComputePassEncoders.Clear();
            CommandEncoders.Clear();
            CommandBuffers.Clear();
            RenderPipelines.Clear();
            ComputePipelines.Clear();
            ShaderModules.Clear();
            PipelineLayouts.Clear();
            BindGroups.Clear();
            BindGroupLayouts.Clear();
            Samplers.Clear();
            TextureViews.Clear();
            Textures.Clear();
            Buffers.Clear();
            Queues.Clear();
            CanvasContexts.Clear();
            Devices.Clear();
            Adapters.Clear();
            AdapterInfos.Clear();
            SupportedLimits.Clear();
            SupportedFeatures.Clear();
            WgslLanguageFeatures.Clear();
            Gpus.Clear();
            (_gpu as IDisposable)?.Dispose();
            (_config.Backend as IDisposable)?.Dispose();
        }
    }
}
