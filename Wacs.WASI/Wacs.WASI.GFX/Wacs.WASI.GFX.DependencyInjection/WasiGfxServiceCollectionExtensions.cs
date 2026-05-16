// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Wacs.WASI.GFX.DependencyInjection
{
    /// <summary>
    /// One-line registration for the wasi-gfx host bundle in a
    /// <see cref="IServiceCollection"/>. Mirrors the shape of
    /// <c>AddWasiNN</c> and <c>AddWasiPreview2</c>.
    /// </summary>
    public sealed class WasiGfxDependencyInjectionOptions
    {
        public WasiGfxConfiguration Configuration { get; set; }
            = WasiGfxConfiguration.DefaultConfiguration();

        /// <summary>
        /// Set the backend on the configuration. One backend
        /// per host; later calls replace earlier ones
        /// (last-write-wins).
        /// </summary>
        public WasiGfxDependencyInjectionOptions WithBackend(IBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            Configuration.Backend = backend;
            return this;
        }
    }

    public static class WasiGfxServiceCollectionExtensions
    {
        public static IServiceCollection AddWasiGfx(
            this IServiceCollection services,
            Action<WasiGfxDependencyInjectionOptions>? configure = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            var opts = new WasiGfxDependencyInjectionOptions();
            configure?.Invoke(opts);
            services.TryAddSingleton(opts.Configuration);
            services.TryAddSingleton<WasiGfxBundle>();
            return services;
        }

        /// <summary>
        /// Register the <see cref="WasiPreview2GfxBundle"/>
        /// composite that ComponentMainHost uses for components
        /// importing both <c>wasi:cli/*</c> and wasi-gfx.
        /// Assumes <c>AddWasiPreview2()</c> and
        /// <c>AddWasiGfx()</c> have already been called on the
        /// same service collection.
        /// </summary>
        public static IServiceCollection AddWasiPreview2GfxBundle(
            this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            services.TryAddSingleton<WasiPreview2GfxBundle>();
            return services;
        }
    }
}
