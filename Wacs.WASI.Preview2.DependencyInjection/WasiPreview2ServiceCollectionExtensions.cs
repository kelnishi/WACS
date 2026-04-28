// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wacs.ComponentModel.Validation;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.Cli;
using Wacs.WASI.Preview2.Clocks;
using Wacs.WASI.Preview2.Filesystem;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Http;
using Wacs.WASI.Preview2.Io;
using Wacs.WASI.Preview2.Random;
using Wacs.WASI.Preview2.Sockets;

namespace Wacs.WASI.Preview2.DependencyInjection
{
    /// <summary>
    /// Configuration knobs for
    /// <see cref="WasiPreview2ServiceCollectionExtensions.AddWasiPreview2"/>.
    /// </summary>
    public sealed class WasiPreview2Options
    {
        /// <summary>Validation severity applied to the
        /// <see cref="Linker"/> resolved from DI. Default
        /// <see cref="ValidationLevel.Strict"/> — surfaces
        /// contract drift as a deterministic exception at link
        /// time. Lower to <see cref="ValidationLevel.Warnings"/>
        /// or <see cref="ValidationLevel.Off"/> for relaxed
        /// hosts.</summary>
        public ValidationLevel ValidationLevel { get; set; }
            = ValidationLevel.Strict;

        /// <summary>Lifetime for the per-component-instance
        /// state — <see cref="ResourceContext"/>,
        /// <see cref="WasmRuntime"/>, the
        /// <c>*Bindings</c> orchestrators, and the
        /// <see cref="Linker"/>. Stateless host impls
        /// (<see cref="IRandom"/>, <see cref="IExit"/>, etc.)
        /// always register as singletons.
        ///
        /// <para>Default <see cref="ServiceLifetime.Scoped"/> —
        /// suitable for ASP.NET request-scoped wasm execution.
        /// Use <see cref="ServiceLifetime.Transient"/> for
        /// console apps or worker services where the lifetime
        /// is naturally per-call.</para></summary>
        public ServiceLifetime InstanceLifetime { get; set; }
            = ServiceLifetime.Scoped;
    }

    /// <summary>
    /// One-line registration of every WASI Preview 2 default
    /// implementation, every <c>*Bindings</c> orchestrator, and
    /// a <see cref="Linker"/> pre-wired with all of them.
    /// Override individual host impls by registering a different
    /// implementation against the same interface — the
    /// <c>TryAdd</c> calls here only register defaults if
    /// nothing else is bound.
    /// </summary>
    public static class WasiPreview2ServiceCollectionExtensions
    {
        public static IServiceCollection AddWasiPreview2(
            this IServiceCollection services,
            Action<WasiPreview2Options>? configure = null)
        {
            if (services == null) throw new ArgumentNullException(
                nameof(services));

            var opts = new WasiPreview2Options();
            configure?.Invoke(opts);

            // ---- Stateless host impls — Singleton --------------
            // These don't reference ResourceContext; safe to share.
            services.TryAddSingleton<IEnvironment,
                Wacs.WASI.Preview2.Cli.Environment>();
            services.TryAddSingleton<IExit, ExitHandler>();
            services.TryAddSingleton<IStdin, Stdin>();
            services.TryAddSingleton<IStdout, Stdout>();
            services.TryAddSingleton<IStderr, Stderr>();
            services.TryAddSingleton<ITerminalStdin, TerminalStdin>();
            services.TryAddSingleton<ITerminalStdout, TerminalStdout>();
            services.TryAddSingleton<ITerminalStderr, TerminalStderr>();

            services.TryAddSingleton<IMonotonicClock, MonotonicClock>();
            services.TryAddSingleton<IWallClock, WallClock>();
            services.TryAddSingleton<ITimezone, Timezone>();

            services.TryAddSingleton<IRandom, Random.Random>();
            services.TryAddSingleton<IInsecure, InsecureRandom>();
            services.TryAddSingleton<IInsecureSeed, InsecureSeedSource>();

            services.TryAddSingleton<IPoll, PollSource>();

            services.TryAddSingleton<IPreopens>(
                _ => new DefaultPreopens());
            services.TryAddSingleton<IFilesystemErrorCode,
                FilesystemErrorCodeSource>();

            services.TryAddSingleton<IInstanceNetwork,
                InstanceNetworkSource>();
            services.TryAddSingleton<ITcpCreateSocket,
                SystemTcpCreateSocket>();
            services.TryAddSingleton<IUdpCreateSocket,
                SystemUdpCreateSocket>();
            services.TryAddSingleton<IIpNameLookup, IpNameLookup>();

            services.TryAddSingleton<IOutgoingHandler,
                HttpClientOutgoingHandler>();

            // ---- Per-component-instance — InstanceLifetime ----
            // ResourceContext, WasmRuntime, all bindings classes,
            // and the Linker share the configured lifetime so
            // resource tables stay consistent across a single
            // wasm instance's lifetime.
            services.TryAdd(new ServiceDescriptor(
                typeof(ResourceContext), typeof(ResourceContext),
                opts.InstanceLifetime));
            services.TryAdd(new ServiceDescriptor(
                typeof(WasmRuntime), typeof(WasmRuntime),
                opts.InstanceLifetime));

            // Bindings orchestrators — capture ResourceContext +
            // optional impl deps from DI.
            AddBindings(services, opts.InstanceLifetime);

            // Linker — pre-wired with every IBindable so
            // consumers resolve a fully-bound runtime.
            services.TryAdd(new ServiceDescriptor(
                typeof(Linker),
                sp => BuildLinker(sp, opts.ValidationLevel),
                opts.InstanceLifetime));

            return services;
        }

        private static void AddBindings(IServiceCollection services,
            ServiceLifetime lifetime)
        {
            services.TryAdd(new ServiceDescriptor(
                typeof(CliBindings),
                sp => new CliBindings(
                    sp.GetRequiredService<ResourceContext>(),
                    sp.GetService<IEnvironment>(),
                    sp.GetService<IExit>(),
                    sp.GetService<IStdin>(),
                    sp.GetService<IStdout>(),
                    sp.GetService<IStderr>(),
                    sp.GetService<ITerminalStdin>(),
                    sp.GetService<ITerminalStdout>(),
                    sp.GetService<ITerminalStderr>()),
                lifetime));

            services.TryAdd(new ServiceDescriptor(
                typeof(ClockBindings),
                sp => new ClockBindings(
                    sp.GetRequiredService<ResourceContext>(),
                    sp.GetService<IMonotonicClock>(),
                    sp.GetService<IWallClock>(),
                    sp.GetService<ITimezone>()),
                lifetime));

            services.TryAdd(new ServiceDescriptor(
                typeof(RandomBindings),
                sp => new RandomBindings(
                    sp.GetService<IRandom>(),
                    sp.GetService<IInsecure>(),
                    sp.GetService<IInsecureSeed>()),
                lifetime));

            services.TryAdd(new ServiceDescriptor(
                typeof(IoBindings),
                sp => new IoBindings(
                    sp.GetRequiredService<ResourceContext>(),
                    sp.GetService<IPoll>()),
                lifetime));

            services.TryAdd(new ServiceDescriptor(
                typeof(StreamBindings),
                sp => new StreamBindings(
                    sp.GetRequiredService<ResourceContext>()),
                lifetime));

            services.TryAdd(new ServiceDescriptor(
                typeof(FilesystemBindings),
                sp => new FilesystemBindings(
                    sp.GetRequiredService<ResourceContext>(),
                    sp.GetService<IPreopens>(),
                    sp.GetService<IFilesystemErrorCode>()),
                lifetime));

            services.TryAdd(new ServiceDescriptor(
                typeof(SocketsBindings),
                sp => new SocketsBindings(
                    sp.GetRequiredService<ResourceContext>(),
                    sp.GetService<IInstanceNetwork>(),
                    sp.GetService<ITcpCreateSocket>(),
                    sp.GetService<IUdpCreateSocket>(),
                    sp.GetService<IIpNameLookup>()),
                lifetime));

            services.TryAdd(new ServiceDescriptor(
                typeof(HttpTypes),
                sp => new HttpTypes(
                    sp.GetRequiredService<ResourceContext>()),
                lifetime));

            services.TryAdd(new ServiceDescriptor(
                typeof(OutgoingHandlerBindings),
                sp => new OutgoingHandlerBindings(
                    sp.GetRequiredService<ResourceContext>(),
                    sp.GetRequiredService<IOutgoingHandler>()),
                lifetime));
        }

        private static Linker BuildLinker(IServiceProvider sp,
            ValidationLevel level)
        {
            var runtime = sp.GetRequiredService<WasmRuntime>();
            var linker = new Linker(runtime, level);
            linker.Bind(sp.GetRequiredService<CliBindings>());
            linker.Bind(sp.GetRequiredService<ClockBindings>());
            linker.Bind(sp.GetRequiredService<RandomBindings>());
            linker.Bind(sp.GetRequiredService<IoBindings>());
            linker.Bind(sp.GetRequiredService<StreamBindings>());
            linker.Bind(sp.GetRequiredService<FilesystemBindings>());
            linker.Bind(sp.GetRequiredService<SocketsBindings>());
            linker.Bind(sp.GetRequiredService<HttpTypes>());
            linker.Bind(sp.GetRequiredService<OutgoingHandlerBindings>());
            return linker;
        }
    }

    /// <summary>Default <see cref="IPreopens"/> — empty
    /// directory list. Hosts that want preopened directories
    /// register a custom <see cref="IPreopens"/> before
    /// calling <c>AddWasiPreview2</c>.</summary>
    internal sealed class DefaultPreopens : IPreopens
    {
        public (IDescriptor, string)[] GetDirectories()
            => System.Array.Empty<(IDescriptor, string)>();
    }
}
