// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using Wacs.ComponentModel.Async;
using Wacs.ComponentModel.CanonicalABI;
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
        /// The component-instance dispatcher the host bindings
        /// route stream/future handles through. Late-bound — set
        /// by the embedder after <c>ComponentInstance.Instantiate</c>
        /// has allocated the dispatcher (it's available on
        /// <c>ComponentInstance.AsyncDispatcher</c>). The host
        /// function delegates registered by <see cref="BindToRuntime"/>
        /// resolve this lazily so a null setting at bind time
        /// doesn't break registration.
        /// </summary>
        public AsyncDispatcher? Dispatcher { get; set; }

        /// <summary>
        /// Registers the WASI Preview 3 host-import surface on
        /// <paramref name="runtime"/>:
        ///
        /// <list type="bullet">
        ///   <item><c>wasi:cli/stdout@0.3.0-rc-2026-03-15
        ///     .write-via-stream(data: stream&lt;u8&gt;) -&gt;
        ///     future&lt;result&lt;_, error-code&gt;&gt;</c> —
        ///     drains the supplied stream handle into
        ///     <see cref="Stdout"/>, returns a future handle the
        ///     guest awaits.</item>
        ///   <item><c>wasi:cli/stderr.write-via-stream</c> —
        ///     same shape, routes to <see cref="Stderr"/>.</item>
        /// </list>
        ///
        /// <para>The delegate bodies resolve
        /// <see cref="Dispatcher"/> at call time, so the
        /// embedder can set it AFTER instantiating the
        /// component (where the dispatcher is created). At call
        /// time the dispatcher must be set or the delegate
        /// throws a clear "Dispatcher not set" diagnostic.</para>
        ///
        /// <para><b>Wire convention:</b> uses
        /// <c>(wasi:cli/stdout@0.3.0-rc-2026-03-15,
        /// write-via-stream)</c> as the import name — wit-
        /// component's lowering of the component's import of
        /// this interface method. The exact spelling awaits
        /// fixture-level verification (same caveat as the
        /// canon-async binder's placeholder convention); if
        /// it differs in real output, the binding-name resolver
        /// hook is the override point. The Slice J commitment
        /// is the binding shape + delegate body, not the wire-
        /// name lockdown.</para>
        ///
        /// <para><c>read-via-stream</c> (stdin) returns
        /// <c>tuple&lt;stream&lt;u8&gt;, future&lt;result&lt;_,
        /// error-code&gt;&gt;&gt;</c>. Per canon-ABI flat-lowering
        /// rules (<c>MAX_FLAT_RESULTS = 1</c>; this tuple flattens
        /// to 2 i32s) the lowered host signature is
        /// <c>(retptr: i32) -&gt; ()</c> — the host writes a
        /// <c>(stream-handle, future-handle)</c> i32 pair into
        /// the component's linear memory at <c>retptr</c>
        /// (8 bytes, 4-aligned).</para>
        /// </summary>
        public void BindToRuntime(WasmRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));

            runtime.BindHostFunction(
                (StdoutModuleName, "write-via-stream"),
                (Func<ExecContext, int, int>)((_, streamHandle) =>
                    InvokeWriteViaStream(Stdout, streamHandle)));

            runtime.BindHostFunction(
                (StderrModuleName, "write-via-stream"),
                (Func<ExecContext, int, int>)((_, streamHandle) =>
                    InvokeWriteViaStream(Stderr, streamHandle)));

            runtime.BindHostFunction(
                (StdinModuleName, "read-via-stream"),
                (Action<ExecContext, int>)((_, retptr) =>
                    InvokeReadViaStream(Stdin, retptr)));
        }

        /// <summary>
        /// Invoke <c>wasi:cli/stdout.write-via-stream</c>'s
        /// host-side delegate logic directly — same body the
        /// canon-async-shim-routed import calls. Public so tests
        /// can exercise the binding without going through a full
        /// wasm invoke; embedders typically reach this through
        /// the runtime-bound host function instead.
        /// </summary>
        public int InvokeWriteViaStream(IStdout sink, int streamHandle)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            var dispatcher = RequireDispatcher();
            var (futureHandle, _) = sink.WriteViaStream(dispatcher, streamHandle);
            return futureHandle;
        }

        /// <summary>Same as <see cref="InvokeWriteViaStream(IStdout, int)"/>
        /// but for stderr.</summary>
        public int InvokeWriteViaStream(IStderr sink, int streamHandle)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            var dispatcher = RequireDispatcher();
            var (futureHandle, _) = sink.WriteViaStream(dispatcher, streamHandle);
            return futureHandle;
        }

        /// <summary>
        /// Invoke <c>wasi:cli/stdin.read-via-stream</c>'s host-side
        /// delegate logic. Calls
        /// <see cref="IStdin.ReadViaStream(AsyncDispatcher)"/> to
        /// allocate the stream + future pair and start the
        /// host-side read loop, then writes both handles to the
        /// component's linear memory at
        /// <paramref name="retptr"/>:
        ///
        /// <list type="bullet">
        ///   <item><c>memory[retptr + 0..4]</c> = stream handle (i32 LE)</item>
        ///   <item><c>memory[retptr + 4..8]</c> = future handle (i32 LE)</item>
        /// </list>
        ///
        /// <para>Per canon-ABI: <c>retptr</c> must be 4-byte
        /// aligned and the 8-byte struct must fit within the
        /// memory bounds. Misaligned or out-of-bounds pointers
        /// throw <see cref="InvalidOperationException"/> with a
        /// diagnostic — the wasm caller's contract requires it
        /// to allocate the slot properly.</para>
        ///
        /// <para>Returns nothing — the future the
        /// <see cref="IStdin"/> impl produced fires when reading
        /// finishes; embedders observe completion through that
        /// future's await-able task.</para>
        /// </summary>
        public void InvokeReadViaStream(IStdin source, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:cli/stdin.read-via-stream: dispatcher.Memory " +
                    "must be set before this import is invoked. Wire it " +
                    "from ComponentInstance after core-module " +
                    "instantiation.");

            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:cli/stdin.read-via-stream: retptr " +
                    $"0x{retptr:X8} is misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). The caller " +
                    "must allocate an 8-byte 4-aligned return area.");

            var (streamHandle, futureHandle, _) =
                source.ReadViaStream(dispatcher);

            var dest = memory.AsSpan(retptr, 8);
            StreamMarshal.WriteHandle(dest, 0, streamHandle);
            FutureMarshal.WriteHandle(dest, 4, futureHandle);
        }

        private AsyncDispatcher RequireDispatcher()
        {
            return Dispatcher
                ?? throw new InvalidOperationException(
                    "WasiPreview3Host.Dispatcher must be set before " +
                    "the host's stdio imports are invoked. Set it from " +
                    "ComponentInstance.AsyncDispatcher after instantiation.");
        }

        /// <summary>Wire-level WASI module name for stdout.</summary>
        public const string StdoutModuleName =
            "wasi:cli/stdout@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for stderr.</summary>
        public const string StderrModuleName =
            "wasi:cli/stderr@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for stdin.</summary>
        public const string StdinModuleName =
            "wasi:cli/stdin@0.3.0-rc-2026-03-15";
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
