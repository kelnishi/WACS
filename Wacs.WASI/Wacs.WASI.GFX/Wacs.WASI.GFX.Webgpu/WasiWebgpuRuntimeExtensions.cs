// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;

namespace Wacs.WASI.GFX.Webgpu
{
    /// <summary>
    /// Fluent <c>runtime.UseWasiWebgpu(...)</c> entry point.
    /// Mirrors <c>WasiGfxRuntimeExtensions.UseWasiGfx</c>: the
    /// embedder configures the backend + shared resources inside
    /// a builder callback, the helper constructs the host, calls
    /// <see cref="WasiWebgpuHost.BindToRuntime"/>, and returns
    /// the host for the caller to dispose at end of scope.
    /// </summary>
    public static class WasiWebgpuRuntimeExtensions
    {
        public static WasiWebgpuHost UseWasiWebgpu(
            this WasmRuntime runtime,
            Action<WasiWebgpuConfigurationBuilder>? configure = null)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            var builder = new WasiWebgpuConfigurationBuilder();
            configure?.Invoke(builder);
            var host = new WasiWebgpuHost(builder.Build());
            host.BindToRuntime(runtime);
            return host;
        }
    }

    /// <summary>
    /// Builder for <see cref="WasiWebgpuConfiguration"/> exposed
    /// from <see cref="WasiWebgpuRuntimeExtensions.UseWasiWebgpu"/>.
    /// Method chaining mirrors <c>WasiGfxConfigurationBuilder</c>.
    /// </summary>
    public sealed class WasiWebgpuConfigurationBuilder
    {
        private readonly WasiWebgpuConfiguration _cfg
            = WasiWebgpuConfiguration.DefaultConfiguration();

        public WasiWebgpuConfigurationBuilder WithBackend(IGpuBackend backend)
        {
            _cfg.Backend = backend
                ?? throw new ArgumentNullException(nameof(backend));
            return this;
        }

        public WasiWebgpuConfigurationBuilder WithSharedResources(
            Wacs.WASI.Preview2.HostBinding.ResourceContext? resources)
        {
            _cfg.SharedResources = resources;
            return this;
        }

        public WasiWebgpuConfigurationBuilder WithAbstractBufferResolver(
            Func<int, IAbstractBuffer?>? resolver)
        {
            _cfg.AbstractBufferResolver = resolver;
            return this;
        }

        public WasiWebgpuConfigurationBuilder WithGraphicsContextResolver(
            Func<int, Wacs.WASI.GFX.GraphicsContext.IContext?>? resolver)
        {
            _cfg.GraphicsContextResolver = resolver;
            return this;
        }

        internal WasiWebgpuConfiguration Build() => _cfg;
    }
}
