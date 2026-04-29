// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.WASI.Preview2.Cli;
using Wacs.WASI.Preview2.Clocks;
using Wacs.WASI.Preview2.Filesystem;
using Wacs.WASI.Preview2.Http;
using Wacs.WASI.Preview2.Io;
using Wacs.WASI.Preview2.Random;
using Wacs.WASI.Preview2.Sockets;

namespace Wacs.WASI.Preview2.DependencyInjection
{
    /// <summary>
    /// Aggregate over every stateless WASI Preview 2 host interface
    /// shipped by <c>Wacs.WASI.Preview2</c>. The transpiler's direct-
    /// linked import path lowers each guest <c>call $import</c> into
    /// inline IL that loads a generated component class's
    /// <c>_bundle</c> field, dereferences the typed interface, and
    /// invokes it directly — no <see cref="System.Delegate"/> hop, no
    /// <c>WasmRuntime.BindHostFunction</c> table lookup.
    ///
    /// <para>Only stateless impls live here. Per-instance state
    /// (<see cref="HostBinding.ResourceContext"/>, the
    /// <c>*Bindings</c> orchestrators, the
    /// <see cref="Wacs.Core.Runtime.Linker"/>) is constructed under
    /// <see cref="WasiPreview2Options.InstanceLifetime"/> and reaches
    /// the generated component class through separate ctor params.</para>
    ///
    /// <para>The bundle is registered as a
    /// <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton"/>
    /// alongside the underlying impls — see
    /// <see cref="WasiPreview2ServiceCollectionExtensions.AddWasiPreview2"/>.</para>
    /// </summary>
    public sealed class WasiPreview2Bundle
    {
        public IEnvironment Environment { get; }
        public IExit Exit { get; }
        public IStdin Stdin { get; }
        public IStdout Stdout { get; }
        public IStderr Stderr { get; }
        public ITerminalStdin TerminalStdin { get; }
        public ITerminalStdout TerminalStdout { get; }
        public ITerminalStderr TerminalStderr { get; }
        public IMonotonicClock MonotonicClock { get; }
        public IWallClock WallClock { get; }
        public ITimezone Timezone { get; }
        public IRandom Random { get; }
        public IInsecure Insecure { get; }
        public IInsecureSeed InsecureSeed { get; }
        public IPoll Poll { get; }
        public IPreopens Preopens { get; }
        public IFilesystemErrorCode FilesystemErrorCode { get; }
        public IInstanceNetwork InstanceNetwork { get; }
        public ITcpCreateSocket TcpCreateSocket { get; }
        public IUdpCreateSocket UdpCreateSocket { get; }
        public IIpNameLookup IpNameLookup { get; }
        public IOutgoingHandler OutgoingHandler { get; }

        public WasiPreview2Bundle(
            IEnvironment environment,
            IExit exit,
            IStdin stdin,
            IStdout stdout,
            IStderr stderr,
            ITerminalStdin terminalStdin,
            ITerminalStdout terminalStdout,
            ITerminalStderr terminalStderr,
            IMonotonicClock monotonicClock,
            IWallClock wallClock,
            ITimezone timezone,
            IRandom random,
            IInsecure insecure,
            IInsecureSeed insecureSeed,
            IPoll poll,
            IPreopens preopens,
            IFilesystemErrorCode filesystemErrorCode,
            IInstanceNetwork instanceNetwork,
            ITcpCreateSocket tcpCreateSocket,
            IUdpCreateSocket udpCreateSocket,
            IIpNameLookup ipNameLookup,
            IOutgoingHandler outgoingHandler)
        {
            Environment = environment;
            Exit = exit;
            Stdin = stdin;
            Stdout = stdout;
            Stderr = stderr;
            TerminalStdin = terminalStdin;
            TerminalStdout = terminalStdout;
            TerminalStderr = terminalStderr;
            MonotonicClock = monotonicClock;
            WallClock = wallClock;
            Timezone = timezone;
            Random = random;
            Insecure = insecure;
            InsecureSeed = insecureSeed;
            Poll = poll;
            Preopens = preopens;
            FilesystemErrorCode = filesystemErrorCode;
            InstanceNetwork = instanceNetwork;
            TcpCreateSocket = tcpCreateSocket;
            UdpCreateSocket = udpCreateSocket;
            IpNameLookup = ipNameLookup;
            OutgoingHandler = outgoingHandler;
        }
    }
}
