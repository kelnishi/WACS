// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Microsoft.Extensions.DependencyInjection;
using Wacs.WASI.Preview3.Cli;

namespace Wacs.WASI.Preview3.DependencyInjection
{
    /// <summary>
    /// <c>Microsoft.Extensions.DependencyInjection</c> wiring for
    /// the WASI Preview 3 host suite. Mirrors
    /// <c>WACS.WASI.Preview2.DependencyInjection</c>'s shape.
    ///
    /// <para>v0 scope: registers the default
    /// <see cref="IStdin"/> / <see cref="IStdout"/> /
    /// <see cref="IStderr"/> implementations + a
    /// <see cref="WasiPreview3Host"/> singleton.</para>
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Register the WASI Preview 3 host suite. Pass
        /// <paramref name="configure"/> to override defaults
        /// (e.g. substitute a memory-backed stdout for tests).
        /// </summary>
        public static IServiceCollection AddWacsWasiPreview3(
            this IServiceCollection services,
            Action<WasiPreview3HostBuilder>? configure = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            services.AddSingleton(sp =>
            {
                var builder = new WasiPreview3HostBuilder();
                configure?.Invoke(builder);
                return builder;
            });
            services.AddSingleton<WasiPreview3Host>();
            services.AddSingleton<IStdin>(sp =>
                sp.GetRequiredService<WasiPreview3Host>().Stdin);
            services.AddSingleton<IStdout>(sp =>
                sp.GetRequiredService<WasiPreview3Host>().Stdout);
            services.AddSingleton<IStderr>(sp =>
                sp.GetRequiredService<WasiPreview3Host>().Stderr);

            return services;
        }
    }
}
