// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wacs.WASI.NN.Types;
using Nn = Wacs.WASI.NN.Nn;

namespace Wacs.WASI.NN.DependencyInjection
{
    /// <summary>
    /// One-line registration of every WASI-NN host implementation
    /// the transpiler-direct-link path needs. Mirrors the shape
    /// of <c>WasiPreview2ServiceCollectionExtensions.AddWasiPreview2</c>.
    ///
    /// <para>The bundled <see cref="WasiNNConfiguration"/>
    /// defaults to no backends; embedders chain
    /// <c>services.AddWasiNN(b => b.AddBackend(GraphEncoding.ONNX,
    /// new OnnxBackend()))</c> to register the backends their
    /// guests need. <c>WasiNNBundle</c> resolves the configured
    /// <c>IGraphFuncs</c> against the registered backends.</para>
    /// </summary>
    public sealed class WasiNNDependencyInjectionOptions
    {
        public WasiNNConfiguration Configuration { get; set; }
            = WasiNNConfiguration.DefaultConfiguration();

        /// <summary>
        /// Register a backend for <paramref name="encoding"/>.
        /// Repeat-callable for multiple encodings; later
        /// registrations overwrite earlier ones for the same
        /// encoding.
        /// </summary>
        public WasiNNDependencyInjectionOptions AddBackend(
            GraphEncoding encoding, IBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            Configuration.Backends[encoding] = backend;
            return this;
        }
    }

    public static class WasiNNServiceCollectionExtensions
    {
        public static IServiceCollection AddWasiNN(
            this IServiceCollection services,
            Action<WasiNNDependencyInjectionOptions>? configure = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            var opts = new WasiNNDependencyInjectionOptions();
            configure?.Invoke(opts);
            services.TryAddSingleton(opts.Configuration);
            services.TryAddSingleton<Nn.IGraphFuncs>(sp =>
                new GraphFuncsImpl(sp.GetRequiredService<WasiNNConfiguration>()));
            services.TryAddSingleton<WasiNNBundle>();
            return services;
        }
    }
}
