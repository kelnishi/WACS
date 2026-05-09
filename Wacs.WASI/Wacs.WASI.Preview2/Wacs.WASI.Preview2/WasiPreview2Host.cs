// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.Cli;
using Wacs.WASI.Preview2.Clocks;
using Wacs.WASI.Preview2.Filesystem;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Http;
using Wacs.WASI.Preview2.Io;
using Wacs.WASI.Preview2.Random;
using Wacs.WASI.Preview2.Sockets;

namespace Wacs.WASI.Preview2
{
    /// <summary>
    /// Composite <see cref="IBindable"/> that wires every WASI
    /// Preview 2 sub-binding (random, clocks, io, streams, cli,
    /// filesystem, optionally sockets + http) onto a
    /// <see cref="WasmRuntime"/> in one call. Symmetric with
    /// <c>Wacs.WASI.NN.WasiNNHost</c> — interpreter consumers
    /// who don't want to thread a <see cref="ResourceContext"/>
    /// through eight separate <c>BindToRuntime</c> calls now have
    /// a single entry point.
    ///
    /// <para>The transpiler-direct-link path (<c>--wasip2</c> +
    /// <see cref="DependencyInjection.WasiPreview2Bundle"/>)
    /// remains the perf-optimized path. This host is the
    /// interpreter-side baseline that consumers can also use as
    /// a fallback for any subsystem the transpiler bundle
    /// doesn't cover yet.</para>
    ///
    /// <para>Default posture matches Wasmtime's safe defaults:
    /// host clocks / random / FS preopens / cli stdio are wired;
    /// sockets and http require explicit opt-in via
    /// <see cref="WasiPreview2HostBuilder.EnableSockets"/> /
    /// <see cref="WasiPreview2HostBuilder.EnableHttp"/>. No
    /// surprise network access from a no-args
    /// <c>runtime.UseWasiPreview2()</c>.</para>
    /// </summary>
    public sealed class WasiPreview2Host : IBindable
    {
        private readonly ResourceContext _resources;
        private readonly WasiPreview2HostBuilder _config;

        public WasiPreview2Host()
            : this(new WasiPreview2HostBuilder()) { }

        public WasiPreview2Host(WasiPreview2HostBuilder builder)
        {
            _config = builder ?? throw new ArgumentNullException(nameof(builder));
            _resources = builder.SharedResources ?? new ResourceContext();
        }

        /// <summary>
        /// Bind every configured WASI Preview 2 sub-binding onto
        /// <paramref name="runtime"/>. Order matters only insofar
        /// as later bindings can refer to resource types the
        /// earlier ones already minted; in practice the per-type
        /// handle tables are lazy-created so the order here is
        /// chosen for readability rather than correctness.
        /// </summary>
        public void BindToRuntime(WasmRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));

            new RandomBindings(_config.Random, _config.InsecureRandom,
                _config.InsecureSeed).BindToRuntime(runtime);
            new ClockBindings(_resources, _config.MonotonicClock,
                _config.WallClock, _config.Timezone).BindToRuntime(runtime);
            new IoBindings(_resources, _config.Poll).BindToRuntime(runtime);
            new StreamBindings(_resources).BindToRuntime(runtime);
            new CliBindings(_resources, _config.Environment, _config.Exit,
                _config.Stdin, _config.Stdout, _config.Stderr,
                _config.TerminalStdin, _config.TerminalStdout,
                _config.TerminalStderr).BindToRuntime(runtime);
            new FilesystemBindings(_resources, _config.Preopens,
                _config.FilesystemErrorCode).BindToRuntime(runtime);

            if (_config.SocketsEnabled)
                new SocketsBindings(_resources, _config.InstanceNetwork,
                    _config.TcpCreate, _config.UdpCreate,
                    _config.IpNameLookup).BindToRuntime(runtime);
            if (_config.HttpEnabled)
            {
                new HttpTypes(_resources, _config.HttpErrorCodeMapper)
                    .BindToRuntime(runtime);
                if (_config.OutgoingHandler != null)
                    new OutgoingHandlerBindings(_resources,
                        _config.OutgoingHandler,
                        _config.HttpUseSpecErrorCode).BindToRuntime(runtime);
            }
        }
    }

    /// <summary>
    /// Fluent builder for <see cref="WasiPreview2Host"/>. Every
    /// field is optional; pass <c>null</c> (the default) to use
    /// the host stock impl for that subsystem (real wall clock,
    /// real random, sandboxed-no-fs, etc.).
    ///
    /// <para>Sockets + http are off-by-default — toggle on with
    /// <see cref="EnableSockets"/> / <see cref="EnableHttp"/>
    /// before passing the builder to the host ctor.</para>
    /// </summary>
    public sealed class WasiPreview2HostBuilder
    {
        // Shared resources
        public ResourceContext? SharedResources { get; set; }

        // Cli
        public IEnvironment? Environment { get; set; }
        public IExit? Exit { get; set; }
        public IStdin? Stdin { get; set; }
        public IStdout? Stdout { get; set; }
        public IStderr? Stderr { get; set; }
        public ITerminalStdin? TerminalStdin { get; set; }
        public ITerminalStdout? TerminalStdout { get; set; }
        public ITerminalStderr? TerminalStderr { get; set; }

        // Clocks
        public IMonotonicClock? MonotonicClock { get; set; }
        public IWallClock? WallClock { get; set; }
        public ITimezone? Timezone { get; set; }

        // Io
        public IPoll? Poll { get; set; }

        // Random
        public IRandom? Random { get; set; }
        public IInsecure? InsecureRandom { get; set; }
        public IInsecureSeed? InsecureSeed { get; set; }

        // Filesystem
        public IPreopens? Preopens { get; set; }
        public IFilesystemErrorCode? FilesystemErrorCode { get; set; }

        // Sockets (off by default — opt in to grant network access)
        public bool SocketsEnabled { get; private set; }
        public IInstanceNetwork? InstanceNetwork { get; set; }
        public ITcpCreateSocket? TcpCreate { get; set; }
        public IUdpCreateSocket? UdpCreate { get; set; }
        public IIpNameLookup? IpNameLookup { get; set; }

        // Http (off by default)
        public bool HttpEnabled { get; private set; }
        public IHttpErrorCodeMapper? HttpErrorCodeMapper { get; set; }
        public IOutgoingHandler? OutgoingHandler { get; set; }
        public bool HttpUseSpecErrorCode { get; set; }

        public WasiPreview2HostBuilder EnableSockets()
        {
            SocketsEnabled = true;
            return this;
        }

        public WasiPreview2HostBuilder EnableHttp()
        {
            HttpEnabled = true;
            return this;
        }
    }

    /// <summary>
    /// Ergonomic one-liner to wire the WASI Preview 2 host
    /// suite onto a <see cref="WasmRuntime"/>. Mirrors the shape
    /// of <c>runtime.UseWasiNN(b => …)</c>.
    /// </summary>
    public static class WasiPreview2RuntimeExtensions
    {
        /// <summary>
        /// Construct and bind a <see cref="WasiPreview2Host"/>
        /// using the configuration produced by
        /// <paramref name="configure"/>. Returns the host so the
        /// caller can hold its lifetime (the shared
        /// <see cref="ResourceContext"/> must outlive any
        /// invocation of imports it backs).
        /// </summary>
        public static WasiPreview2Host UseWasiPreview2(
            this WasmRuntime runtime,
            Action<WasiPreview2HostBuilder>? configure = null)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            var builder = new WasiPreview2HostBuilder();
            configure?.Invoke(builder);
            var host = new WasiPreview2Host(builder);
            host.BindToRuntime(runtime);
            return host;
        }
    }
}
