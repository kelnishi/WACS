// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Cli;
using Wacs.WASI.Preview2.Clocks;
using Wacs.WASI.Preview2.DependencyInjection;
using Wacs.WASI.Preview2.Filesystem;
using Wacs.WASI.Preview2.Http;
using Wacs.WASI.Preview2.Io;
using Wacs.WASI.Preview2.Random;
using Wacs.WASI.Preview2.Sockets;
using Nn = Wacs.WASI.NN.Nn;

namespace Wacs.WASI.NN.DependencyInjection
{
    /// <summary>
    /// Composite bundle that exposes both <c>WasiPreview2Bundle</c>
    /// and <c>WasiNNBundle</c> through a single CLR object.
    /// Property names match each underlying bundle so the
    /// transpiler's <c>HostPackageResolver.ResolveBundleProperty</c>
    /// finds them by the existing name-or-type lookup — no emit
    /// changes required.
    ///
    /// <para>Used when a component imports both <c>wasi:cli/*</c>
    /// (Preview 2) AND <c>wasi:nn/*</c>. The module ctor still
    /// takes a single <c>object hostBundle</c> slot; this composite
    /// satisfies every binding both packages contribute through
    /// one slot.</para>
    ///
    /// <para>Stateless. Per-instance state (resource handle tables,
    /// the wasi-nn host) is held by the underlying bundles' own
    /// scoped services — the composite is a thin façade.</para>
    /// </summary>
    [WacsCompositeBundle("wasi-nn", Priority = 10)]
    public sealed class WasiPreview2NNBundle
    {
        private readonly WasiPreview2Bundle _p2;
        private readonly WasiNNBundle _nn;

        public WasiPreview2NNBundle(
            WasiPreview2Bundle preview2Bundle,
            WasiNNBundle nnBundle)
        {
            _p2 = preview2Bundle
                ?? throw new ArgumentNullException(nameof(preview2Bundle));
            _nn = nnBundle
                ?? throw new ArgumentNullException(nameof(nnBundle));
        }

        // ---- Preview 2 properties (forward to _p2) ----------------
        public IEnvironment Environment => _p2.Environment;
        public IExit Exit => _p2.Exit;
        public IStdin Stdin => _p2.Stdin;
        public IStdout Stdout => _p2.Stdout;
        public IStderr Stderr => _p2.Stderr;
        public ITerminalStdin TerminalStdin => _p2.TerminalStdin;
        public ITerminalStdout TerminalStdout => _p2.TerminalStdout;
        public ITerminalStderr TerminalStderr => _p2.TerminalStderr;
        public IMonotonicClock MonotonicClock => _p2.MonotonicClock;
        public IWallClock WallClock => _p2.WallClock;
        public ITimezone Timezone => _p2.Timezone;
        public IRandom Random => _p2.Random;
        public IInsecure Insecure => _p2.Insecure;
        public IInsecureSeed InsecureSeed => _p2.InsecureSeed;
        public IPoll Poll => _p2.Poll;
        public IPreopens Preopens => _p2.Preopens;
        public IFilesystemErrorCode FilesystemErrorCode => _p2.FilesystemErrorCode;
        public IInstanceNetwork InstanceNetwork => _p2.InstanceNetwork;
        public ITcpCreateSocket TcpCreateSocket => _p2.TcpCreateSocket;
        public IUdpCreateSocket UdpCreateSocket => _p2.UdpCreateSocket;
        public IIpNameLookup IpNameLookup => _p2.IpNameLookup;
        public IOutgoingHandler OutgoingHandler => _p2.OutgoingHandler;

        // ---- WASI NN properties (forward to _nn) ------------------
        public Nn.IGraphFuncs GraphFuncs => _nn.GraphFuncs;

        // ---- Direct accessors for ComponentMainHost ---------------
        // ComponentMainHost detects this composite type and may
        // need each underlying bundle for resource construction.
        // Public so external embedders can also unwrap.
        public WasiPreview2Bundle Preview2 => _p2;
        public WasiNNBundle Nn => _nn;
    }
}
