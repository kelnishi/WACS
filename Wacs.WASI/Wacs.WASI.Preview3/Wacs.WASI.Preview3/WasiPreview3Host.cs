// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview3.Cli;

namespace Wacs.WASI.Preview3
{
    /// <summary>
    /// Composite host configuration for WASI Preview 3 imports.
    /// Sibling of <c>WACS.WASI.Preview2.WasiPreview2Host</c>, but
    /// the wire surface is the Component Model async ABI —
    /// stream/future handles routed through
    /// <see cref="ComponentModel.Async.AsyncDispatcher"/>.
    ///
    /// <para><b>v0 scope</b> (vertical slice): <c>wasi:cli/run</c>
    /// + <c>wasi:cli/{stdin,stdout,stderr}</c>. Sockets / http /
    /// filesystem / clocks / random remain to be ported in
    /// Phase 5 once the v0 slice validates against a real
    /// wit-component fixture.</para>
    ///
    /// <para><b>Wiring shape</b>: this class holds the builder's
    /// configured impls. The actual binding to runtime
    /// host-functions happens through the canon-async binder
    /// (Phase 3 G3+H) once a component-instance is loaded —
    /// the binder identifies the wit-component shim module,
    /// extracts canon-op identities, and registers delegates
    /// that route to the configured impls. Today this class
    /// pins the public surface; the wire-level connection
    /// lands when fixture availability lets us validate the
    /// convention.</para>
    /// </summary>
    public sealed class WasiPreview3Host : IBindable
    {
        private readonly WasiPreview3HostBuilder _config;

        // Lazy-cached defaults so repeated property access
        // returns the same instance — required for DI
        // singleton semantics and consistent with the
        // Preview2 host's lifetime model.
        private IStdin? _stdin;
        private IStdout? _stdout;
        private IStderr? _stderr;

        public WasiPreview3Host() : this(new WasiPreview3HostBuilder()) { }

        public WasiPreview3Host(WasiPreview3HostBuilder builder)
        {
            _config = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public IStdin Stdin => _stdin ??=
            _config.Stdin ?? new StreamBackedStdin(Console.OpenStandardInput());

        public IStdout Stdout => _stdout ??=
            _config.Stdout ?? new StreamBackedSink(Console.OpenStandardOutput());

        public IStderr Stderr => _stderr ??=
            (_config.Stderr as IStderr)
                ?? new StreamBackedSink(Console.OpenStandardError());

        /// <summary>
        /// Registers the host-side delegates the wit-component
        /// shim module will route canon-async calls to.
        ///
        /// <para>v0: no-op — the binding mechanism (delegate
        /// registration under <c>("", "&lt;funcIdx&gt;")</c> imports
        /// produced by wit-component's shim emit) lands in
        /// Slice J once a real fixture is available. The
        /// <see cref="WasiPreview3Host"/> public surface is
        /// stable; the wire connection is the deferred piece.</para>
        /// </summary>
        public void BindToRuntime(WasmRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            // Slice J: register host delegates under the
            // canon-async-shim-resolved import names. The
            // ShimModuleRecognizer + CanonAsyncBinder pipeline
            // does the actual routing; this layer just plugs
            // the IStdio impls into the delegate bodies.
            //
            // No-op today — see class doc comment.
        }
    }

    /// <summary>Fluent builder for <see cref="WasiPreview3Host"/>.</summary>
    public sealed class WasiPreview3HostBuilder
    {
        public IStdin? Stdin { get; set; }
        public IStdout? Stdout { get; set; }
        public IStderr? Stderr { get; set; }
    }

    /// <summary>Ergonomic one-liner mirroring
    /// <c>UseWasiPreview2</c>.</summary>
    public static class WasiPreview3RuntimeExtensions
    {
        public static WasiPreview3Host UseWasiPreview3(
            this WasmRuntime runtime,
            Action<WasiPreview3HostBuilder>? configure = null)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            var builder = new WasiPreview3HostBuilder();
            configure?.Invoke(builder);
            var host = new WasiPreview3Host(builder);
            host.BindToRuntime(runtime);
            return host;
        }
    }
}
