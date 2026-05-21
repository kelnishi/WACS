// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using Wacs.ComponentModel.Async;
using Wacs.ComponentModel.CanonicalABI;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview3.CanonicalAbi;
using Wacs.WASI.Preview3.Cli;
using Wacs.WASI.Preview3.Clocks;
using Wacs.WASI.Preview3.Filesystem;
using Wacs.WASI.Preview3.Http;
using Wacs.WASI.Preview3.Random;
using Wacs.WASI.Preview3.Resources;
using Wacs.WASI.Preview3.Sockets;

namespace Wacs.WASI.Preview3
{
    /// <summary>
    /// 17-wire-param custom delegate for udp-socket.send. The
    /// canon-ABI flat lowering totals 17 wire i32 slots: self +
    /// list&lt;u8&gt; ptr/len + option&lt;ip-socket-address&gt;
    /// (1 opt-disc + 12 variant slots) + retArea. System.Action
    /// only goes up to <c>Action&lt;T1..T16&gt;</c>, so we
    /// declare this delegate to reach 18 type args (including
    /// the leading ExecContext).
    /// </summary>
    public delegate void UdpSendDelegate(
        ExecContext ctx,
        int self,
        int dataPtr, int dataLen,
        int optDisc,
        int addrDisc,
        int s1, int s2, int s3, int s4, int s5,
        int s6, int s7, int s8, int s9, int s10, int s11,
        int retptr);

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
        private IMonotonicClock? _monotonic;
        private ISystemClock? _system;
        private IRandom? _random;
        private IInsecure? _insecure;
        private IInsecureSeed? _insecureSeed;
        private IExit? _exit;
        private IEnvironment? _environment;
        private IPreopens? _preopens;
        private IIpNameLookup? _nameLookup;
        private IClient? _httpClient;
        private IHandler? _httpHandler;

        /// <summary>
        /// Host-side handle table for <c>wasi:http/types.fields</c>
        /// resources. The guest sees i32 handles; we look up the
        /// CLR <see cref="IFields"/> instance through this table
        /// at every method dispatch.
        ///
        /// <para>One table per host instance — fresh component
        /// instances get fresh tables. The same handle integer
        /// across two host instances refers to two unrelated
        /// fields objects, which matches the per-component
        /// resource-lifetime model.</para>
        /// </summary>
        public HostResourceTable<IFields> FieldsHandles { get; } =
            new HostResourceTable<IFields>();

        /// <summary>Host-side handle table for
        /// <c>wasi:http/types.request-options</c> resources.</summary>
        public HostResourceTable<IRequestOptions> RequestOptionsHandles { get; } =
            new HostResourceTable<IRequestOptions>();

        /// <summary>Host-side handle table for
        /// <c>wasi:http/types.response</c> resources.</summary>
        public HostResourceTable<IResponse> ResponseHandles { get; } =
            new HostResourceTable<IResponse>();

        /// <summary>Host-side handle table for
        /// <c>wasi:http/types.request</c> resources.</summary>
        public HostResourceTable<IRequest> RequestHandles { get; } =
            new HostResourceTable<IRequest>();

        // Tracks the completion-future-handle that
        // [static]request.new / response.new returned alongside
        // each request / response. The future signals "request
        // was successfully sent" (resolved by client.send /
        // handler.handle after the host SendAsync /
        // HandleAsync settles). Identity-keyed: a given
        // IRequest / IResponse impl maps to its future-handle.
        // Cleared on resource-drop to avoid orphaned entries.
        //
        // (ReferenceEqualityComparer isn't on netstandard2.1,
        //  so use a local identity comparer.)
        private sealed class IdentityComparer<T> :
            System.Collections.Generic.IEqualityComparer<T>
            where T : class
        {
            public static readonly IdentityComparer<T> Instance =
                new IdentityComparer<T>();
            public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers
                    .GetHashCode(obj);
        }

        private readonly System.Collections.Generic.Dictionary<IRequest, int>
            _requestCompletionFutures =
                new System.Collections.Generic.Dictionary<IRequest, int>(
                    IdentityComparer<IRequest>.Instance);
        private readonly System.Collections.Generic.Dictionary<IResponse, int>
            _responseCompletionFutures =
                new System.Collections.Generic.Dictionary<IResponse, int>(
                    IdentityComparer<IResponse>.Instance);

        /// <summary>Host-side handle table for
        /// <c>wasi:filesystem/types.descriptor</c> resources.</summary>
        public HostResourceTable<IDescriptor> DescriptorHandles { get; } =
            new HostResourceTable<IDescriptor>();

        /// <summary>Host-side handle table for
        /// <c>wasi:sockets/types.tcp-socket</c> resources.</summary>
        public HostResourceTable<ITcpSocket> TcpSocketHandles { get; } =
            new HostResourceTable<ITcpSocket>();

        /// <summary>Host-side handle table for
        /// <c>wasi:sockets/types.udp-socket</c> resources.</summary>
        public HostResourceTable<IUdpSocket> UdpSocketHandles { get; } =
            new HostResourceTable<IUdpSocket>();

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

        public IMonotonicClock MonotonicClock =>
            _monotonic ??= _config.MonotonicClock ?? new MonotonicClock();

        public ISystemClock SystemClock =>
            _system ??= _config.SystemClock ?? new SystemClock();

        public IRandom Random =>
            _random ??= _config.Random ?? new Random.Random();

        public IInsecure InsecureRandom =>
            _insecure ??= _config.InsecureRandom ?? new InsecureRandom();

        public IInsecureSeed InsecureSeed =>
            _insecureSeed ??= _config.InsecureSeed ?? new InsecureSeedSource();

        /// <summary>
        /// CLI exit handler. The default <see cref="ExitHandler"/>
        /// throws <see cref="ExitException"/> carrying the exit
        /// code; embedders that want native process termination
        /// supply their own (e.g. one that calls
        /// <c>System.Environment.Exit</c>).
        /// </summary>
        public IExit Exit =>
            _exit ??= _config.Exit ?? new ExitHandler();

        /// <summary>
        /// CLI environment / arguments / cwd provider. Default
        /// <see cref="EnvironmentHandler"/> mirrors the host's
        /// <c>System.Environment</c>; embedders that want
        /// sandboxing supply a fixed-set implementation via the
        /// builder.
        /// </summary>
        public IEnvironment Environment =>
            _environment ??= _config.Environment ?? new EnvironmentHandler();

        /// <summary>
        /// Filesystem preopen set. Defaults to an empty list —
        /// guests with no configured preopens see no
        /// filesystem. Embedders typically populate this via
        /// <see cref="DirectoryPreopens.FromHostPaths"/>.
        /// </summary>
        public IPreopens Preopens =>
            _preopens ??= _config.Preopens
                ?? DirectoryPreopens.FromHostPaths();

        /// <summary>
        /// Hostname → IP address resolver. Defaults to
        /// <see cref="NoNameLookup"/> — guests requesting
        /// resolution see <see cref="ErrorCode.PermanentResolverFailure"/>
        /// until the embedder explicitly opts in (typically by
        /// configuring <see cref="DnsBackedNameLookup"/> or a
        /// custom impl). This default fail-closed posture
        /// prevents accidental leakage of the host's DNS
        /// resolver to untrusted components.
        /// </summary>
        public IIpNameLookup IpNameLookup =>
            _nameLookup ??= _config.IpNameLookup ?? new NoNameLookup();

        /// <summary>
        /// Outbound HTTP client. Default is an
        /// <see cref="HttpBackedClient"/> with a fresh
        /// <see cref="System.Net.Http.HttpClient"/>; embedders
        /// that want pinned cert validation, custom proxies, or
        /// shared client pooling configure their own via the
        /// builder.
        /// </summary>
        public IClient HttpClient =>
            _httpClient ??= _config.HttpClient ?? new HttpBackedClient();

        /// <summary>
        /// Inbound HTTP handler. No default — guests that
        /// import <c>wasi:http/handler</c> for inbound serving
        /// must have an embedder-configured handler. The
        /// canon-async binding throws
        /// <see cref="HttpErrorCode.ConfigurationError"/> at
        /// invocation time when this is null.
        /// </summary>
        public IHandler? HttpHandler =>
            _httpHandler ??= _config.HttpHandler;

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

            // wasi:clocks/monotonic-clock@0.3.0-rc-2026-03-15
            //   now: () -> mark (u64)
            //   get-resolution: () -> duration (u64)
            //   wait-until: async (when: mark) -> ()
            //   wait-for: async (how-long: duration) -> ()
            //
            // The async `wait-*` functions bind here as
            // synchronous-blocking shapes: the host-side
            // implementation completes the Task.Delay synchronously
            // from the wasm caller's perspective. The lowered wire
            // signature for an async-func import depends on the
            // caller's canon-lower opts (`async`/`callback`); the
            // sync-blocking form below is the conservative starting
            // point until a real wit-component fixture pins the
            // exact convention.
            runtime.BindHostFunction(
                (MonotonicClockModuleName, "now"),
                (Func<ExecContext, long>)(_ =>
                    unchecked((long)MonotonicClock.Now())));

            runtime.BindHostFunction(
                (MonotonicClockModuleName, "get-resolution"),
                (Func<ExecContext, long>)(_ =>
                    unchecked((long)MonotonicClock.GetResolution())));

            runtime.BindHostFunction(
                (MonotonicClockModuleName, "wait-until"),
                (Action<ExecContext, long>)((_, when) =>
                    MonotonicClock.WaitUntilAsync(
                        unchecked((ulong)when)).GetAwaiter().GetResult()));

            runtime.BindHostFunction(
                (MonotonicClockModuleName, "wait-for"),
                (Action<ExecContext, long>)((_, howLong) =>
                    MonotonicClock.WaitForAsync(
                        unchecked((ulong)howLong)).GetAwaiter().GetResult()));

            // Canon-lower-async variants. wit-component lowers
            // `async func(p: u64)` imports as `(i64) -> i32`
            // when the params fit in flat-args and the return
            // is void: u64 passed directly, packed status
            // returned. Per the canonical-ABI ("await_result"
            // in wit-bindgen-rt::async_support): status is
            // upper 2 bits (>>30), call/subtask handle is
            // lower 30 bits. STATUS_RETURNED = 2 signals the
            // call completed synchronously with no pending
            // subtask — the only state the guest accepts
            // without further polling.
            runtime.BindHostFunction(
                (MonotonicClockModuleName, "[async-lower]wait-until"),
                (Func<ExecContext, long, int>)((_, when) =>
                {
                    MonotonicClock.WaitUntilAsync(
                        unchecked((ulong)when)).GetAwaiter().GetResult();
                    return CanonAsyncStatusReturnedPacked;
                }));

            runtime.BindHostFunction(
                (MonotonicClockModuleName, "[async-lower]wait-for"),
                (Func<ExecContext, long, int>)((_, howLong) =>
                {
                    MonotonicClock.WaitForAsync(
                        unchecked((ulong)howLong)).GetAwaiter().GetResult();
                    return CanonAsyncStatusReturnedPacked;
                }));

            // wasi:clocks/system-clock@0.3.0-rc-2026-03-15
            //   record instant { seconds: s64, nanoseconds: u32 }
            //   now: () -> instant
            //   get-resolution: () -> duration (u64)
            //
            // instant flat-count = 2 (s64 + u32). Exceeds
            // MAX_FLAT_RESULTS = 1 → lowered shape is
            // (retptr: i32) -> () with the record laid out at
            // retptr (16 bytes, 8-aligned). Wire layout:
            //   +0..8: seconds (s64 LE)
            //   +8..12: nanoseconds (u32 LE)
            //   +12..16: 4-byte tail pad (record's align is 8)
            runtime.BindHostFunction(
                (SystemClockModuleName, "now"),
                (Action<ExecContext, int>)((_, retptr) =>
                    InvokeSystemClockNow(SystemClock, retptr)));

            runtime.BindHostFunction(
                (SystemClockModuleName, "get-resolution"),
                (Func<ExecContext, long>)(_ =>
                    unchecked((long)SystemClock.GetResolution())));

            // wasi:random — three interfaces. Memory-writing
            // bindings (get-*-bytes, get-insecure-seed) need
            // cabi_realloc to reserve guest space; the resolver
            // captures the runtime here and resolves the export
            // lazily at first call (after instantiation).
            var realloc = new Realloc(runtime);

            // wasi:random/random@0.3.0-rc-2026-03-15
            //   get-random-bytes: func(max-len: u64) -> list<u8>
            //   get-random-u64: func() -> u64
            //
            // list<u8> flat-count = 2 (ptr + len) > MAX_FLAT_RESULTS
            // → retptr layout: ptr (i32) + len (i32). The bytes
            // themselves are allocated via cabi_realloc and the
            // host writes them at the returned ptr.
            runtime.BindHostFunction(
                (RandomModuleName, "get-random-u64"),
                (Func<ExecContext, long>)(_ =>
                    unchecked((long)Random.GetRandomU64())));

            runtime.BindHostFunction(
                (RandomModuleName, "get-random-bytes"),
                (Action<ExecContext, long, int>)((_, maxLen, retptr) =>
                    InvokeGetRandomBytes(Random, realloc,
                        unchecked((ulong)maxLen), retptr)));

            // wasi:random/insecure@0.3.0-rc-2026-03-15
            //   get-insecure-random-bytes: func(max-len: u64) -> list<u8>
            //   get-insecure-random-u64: func() -> u64
            runtime.BindHostFunction(
                (InsecureRandomModuleName, "get-insecure-random-u64"),
                (Func<ExecContext, long>)(_ =>
                    unchecked((long)InsecureRandom.GetInsecureRandomU64())));

            runtime.BindHostFunction(
                (InsecureRandomModuleName, "get-insecure-random-bytes"),
                (Action<ExecContext, long, int>)((_, maxLen, retptr) =>
                    InvokeGetInsecureRandomBytes(InsecureRandom, realloc,
                        unchecked((ulong)maxLen), retptr)));

            // wasi:random/insecure-seed@0.3.0-rc-2026-03-15
            //   get-insecure-seed: func() -> tuple<u64, u64>
            //
            // tuple<u64, u64> flat-count = 2 > MAX_FLAT_RESULTS
            // → retptr layout: u64 (8 bytes) + u64 (8 bytes) =
            // 16 bytes, 8-aligned.
            runtime.BindHostFunction(
                (InsecureSeedModuleName, "get-insecure-seed"),
                (Action<ExecContext, int>)((_, retptr) =>
                    InvokeGetInsecureSeed(InsecureSeed, retptr)));

            // wasi:cli/exit@0.3.0-rc-2026-03-15
            //   exit: func(status: result);
            //
            // The result<_, _> discriminant lowers to a single
            // i32: 0 = Ok, 1 = Err. The default
            // <see cref="ExitHandler"/> throws ExitException so
            // the dispatcher unwinds the wasm stack cleanly.
            runtime.BindHostFunction(
                (CliExitModuleName, "exit"),
                (Action<ExecContext, int>)((_, status) =>
                    Exit.Exit(status == 0)));

            // wasi:cli/environment@0.3.0-rc-2026-03-15
            //   get-environment: () -> list<tuple<string, string>>
            //   get-arguments:   () -> list<string>
            //   get-initial-cwd: () -> option<string>
            //
            // All three returns exceed MAX_FLAT_RESULTS, so the
            // lowered signatures take a retptr the host writes
            // the canonical-ABI lowering into. Strings are
            // allocated via cabi_realloc.
            runtime.BindHostFunction(
                (CliEnvironmentModuleName, "get-environment"),
                (Action<ExecContext, int>)((_, retptr) =>
                    InvokeGetEnvironment(Environment, realloc, retptr)));

            runtime.BindHostFunction(
                (CliEnvironmentModuleName, "get-arguments"),
                (Action<ExecContext, int>)((_, retptr) =>
                    InvokeGetArguments(Environment, realloc, retptr)));

            runtime.BindHostFunction(
                (CliEnvironmentModuleName, "get-initial-cwd"),
                (Action<ExecContext, int>)((_, retptr) =>
                    InvokeGetInitialCwd(Environment, realloc, retptr)));

            // wasi:cli/terminal-{stdin,stdout,stderr}@0.3.0-rc-2026-03-15
            //   get-terminal-*: func() -> option<terminal-{input,output}>
            //
            // Default: always-None — the host doesn't expose a
            // terminal handle to the guest. retArea = 8 bytes
            // (disc:u8 + 3 pad + handle:u32). Embedders that
            // want to expose a real TTY override these later.
            runtime.BindHostFunction(
                (CliTerminalStdinModuleName, "get-terminal-stdin"),
                (Action<ExecContext, int>)((_, retptr) =>
                    WriteOptionResourceNone(retptr)));

            runtime.BindHostFunction(
                (CliTerminalStdoutModuleName, "get-terminal-stdout"),
                (Action<ExecContext, int>)((_, retptr) =>
                    WriteOptionResourceNone(retptr)));

            runtime.BindHostFunction(
                (CliTerminalStderrModuleName, "get-terminal-stderr"),
                (Action<ExecContext, int>)((_, retptr) =>
                    WriteOptionResourceNone(retptr)));

            // wasi:http/types.fields — host-resource lifecycle +
            // representative method dispatch. Constructor allocates
            // a fresh empty Fields and returns its handle; methods
            // take the self handle as first arg and look up the
            // CLR instance through FieldsHandles. Destructor drops.
            //
            // Names use the wit-component convention
            // ([constructor]NAME / [method]NAME.OP /
            // [resource-drop]NAME); the exact spelling awaits
            // real wit-component fixture validation, same caveat
            // as Slice J's stdout binding.
            runtime.BindHostFunction(
                (HttpTypesModuleName, "[constructor]fields"),
                (Func<ExecContext, int>)(_ =>
                    FieldsHandles.Allocate(new Fields())));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[resource-drop]fields"),
                (Action<ExecContext, int>)((_, handle) =>
                    InvokeFieldsDrop(handle)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]fields.has"),
                (Func<ExecContext, int, int, int, int>)((_, self, namePtr, nameLen) =>
                    InvokeFieldsHas(self, namePtr, nameLen) ? 1 : 0));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]fields.append"),
                (Action<ExecContext, int, int, int, int, int>)(
                    (_, self, namePtr, nameLen, valuePtr, valueLen) =>
                        InvokeFieldsAppend(self, namePtr, nameLen,
                            valuePtr, valueLen)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]fields.set"),
                (Action<ExecContext, int, int, int, int, int, int>)(
                    (_, self, namePtr, nameLen, listPtr, listCount, retptr) =>
                        InvokeFieldsSet(self, namePtr, nameLen,
                            listPtr, listCount, retptr, realloc)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]fields.delete"),
                (Action<ExecContext, int, int, int, int>)(
                    (_, self, namePtr, nameLen, retptr) =>
                        InvokeFieldsDelete(self, namePtr, nameLen,
                            retptr, realloc)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]fields.clone"),
                (Func<ExecContext, int, int>)((_, self) =>
                    InvokeFieldsClone(self)));

            // ---- fields.get / copy-all / get-and-delete / from-list (Slice AA) ----

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]fields.get"),
                (Action<ExecContext, int, int, int, int>)(
                    (_, self, namePtr, nameLen, retptr) =>
                        InvokeFieldsGet(self, namePtr, nameLen,
                            retptr, realloc)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]fields.copy-all"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeFieldsCopyAll(self, retptr, realloc)));

            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    "[method]fields.get-and-delete"),
                (Action<ExecContext, int, int, int, int>)(
                    (_, self, namePtr, nameLen, retptr) =>
                        InvokeFieldsGetAndDelete(self, namePtr, nameLen,
                            retptr, realloc)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[static]fields.from-list"),
                (Action<ExecContext, int, int, int>)(
                    (_, listPtr, listLen, retptr) =>
                        InvokeFieldsFromList(listPtr, listLen,
                            retptr, realloc)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[resource-drop]request"),
                (Action<ExecContext, int>)((_, handle) =>
                {
                    // Clean up the request → completion-future
                    // entry (allocated by request.new) so the
                    // dictionary doesn't accumulate dropped
                    // requests. Slice QQ.
                    var req = RequestHandles.Get(handle);
                    if (req != null)
                        _requestCompletionFutures.Remove(req);
                    RequestHandles.Drop(handle);
                }));

            // wasi:http/types.request-options — fully wired.
            // All methods are simple primitive round-trips
            // (the option<duration> wire shape lowers to
            // (i32 is-some, i64 value)).
            runtime.BindHostFunction(
                (HttpTypesModuleName, "[constructor]request-options"),
                (Func<ExecContext, int>)(_ =>
                    RequestOptionsHandles.Allocate(new RequestOptions())));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[resource-drop]request-options"),
                (Action<ExecContext, int>)((_, handle) =>
                    RequestOptionsHandles.Drop(handle)));

            BindOptionalDurationTimeout(runtime, "connect-timeout",
                opt => opt.GetConnectTimeout(),
                (opt, v) => opt.SetConnectTimeout(v));
            BindOptionalDurationTimeout(runtime, "first-byte-timeout",
                opt => opt.GetFirstByteTimeout(),
                (opt, v) => opt.SetFirstByteTimeout(v));
            BindOptionalDurationTimeout(runtime, "between-bytes-timeout",
                opt => opt.GetBetweenBytesTimeout(),
                (opt, v) => opt.SetBetweenBytesTimeout(v));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]request-options.clone"),
                (Func<ExecContext, int, int>)((_, self) =>
                {
                    var opt = RequireRequestOptions(self);
                    return RequestOptionsHandles.Allocate(opt.Clone());
                }));

            // ---- request method + scheme (Slice CC) ----------------
            //
            // method variant: 9 simple cases + other(string).
            // scheme variant: HTTP, HTTPS, other(string).
            // Both flat-lower to 3 slots (disc + str-ptr + str-len)
            // and retptr-encode to 12 bytes 4-aligned. Wrapping
            // scheme in option<...> adds 1 outer slot / 4 outer
            // bytes for the option-disc.

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]request.get-method"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeRequestGetMethod(self, retptr, realloc)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]request.set-method"),
                (Func<ExecContext, int, int, int, int, int>)(
                    (_, self, disc, strPtr, strLen) =>
                        InvokeRequestSetMethod(
                            self, disc, strPtr, strLen)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]request.get-scheme"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeRequestGetScheme(self, retptr, realloc)));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]request.set-scheme"),
                (Func<ExecContext, int, int, int, int, int, int>)(
                    (_, self, optDisc, schemeDisc, strPtr, strLen) =>
                        InvokeRequestSetScheme(
                            self, optDisc, schemeDisc,
                            strPtr, strLen)));

            // ---- request option<string> getters/setters (Slice BB) ----
            //
            // path-with-query, authority: option<string> shape.
            // Setters take (self, optDisc, strPtr, strLen) and
            // return result (1 flat i32 slot, always 0 since the
            // impl setters can't fail). Getters write a 12-byte
            // 4-aligned option<string> retptr.

            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    "[method]request.get-path-with-query"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeRequestGetOptionString(
                        self, retptr, realloc,
                        r => r.GetPathWithQuery())));

            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    "[method]request.set-path-with-query"),
                (Func<ExecContext, int, int, int, int, int>)(
                    (_, self, optDisc, strPtr, strLen) =>
                        InvokeRequestSetOptionString(
                            self, optDisc, strPtr, strLen,
                            (r, v) => r.SetPathWithQuery(v))));

            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    "[method]request.get-authority"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeRequestGetOptionString(
                        self, retptr, realloc,
                        r => r.GetAuthority())));

            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    "[method]request.set-authority"),
                (Func<ExecContext, int, int, int, int, int>)(
                    (_, self, optDisc, strPtr, strLen) =>
                        InvokeRequestSetOptionString(
                            self, optDisc, strPtr, strLen,
                            (r, v) => r.SetAuthority(v))));

            // wasi:http/types.response — drop + status getter/setter.
            // The constructor (static `new`) returns a
            // tuple<response, future<...>> which needs the
            // multi-return shape from Slice K plus future
            // allocation; ships in a later slice. Other simple
            // methods (get-headers) follow once the headers
            // shared-handle resolution model is in place.
            runtime.BindHostFunction(
                (HttpTypesModuleName, "[resource-drop]response"),
                (Action<ExecContext, int>)((_, handle) =>
                {
                    var resp = ResponseHandles.Get(handle);
                    if (resp != null)
                        _responseCompletionFutures.Remove(resp);
                    ResponseHandles.Drop(handle);
                }));

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]response.get-status-code"),
                (Func<ExecContext, int, int>)((_, self) =>
                    RequireResponse(self).GetStatusCode()));

            // ---- request.new / response.new (Slice II) ------------
            //
            // [static]request.new(
            //   headers: headers,
            //   contents: option<stream<u8>>,
            //   trailers: future<result<option<trailers>, error-code>>,
            //   options: option<request-options>
            // ) -> tuple<request, future<result<_, error-code>>>
            //
            // Flat-lowered params: headers(1) + option<stream<u8>>(2)
            //   + trailers-future(1) + option<options>(2) = 6, plus
            //   retArea(1) = 7 wire params. Return tuple is 2 i32
            //   slots > MAX_FLAT_RESULTS → retArea (8 bytes 4-aligned).
            //
            // v0 caveat: the impl ignores the body-stream and
            // trailers-future args (passes null Body / Trailers
            // to the Request constructor). The wire-side handles
            // are accepted and the completion future is allocated;
            // full stream-bridge integration (wiring the
            // StreamBuffer<byte> to System.IO.Stream and resolving
            // the completion future when the body finishes) lands
            // in a follow-up slice.

            runtime.BindHostFunction(
                (HttpTypesModuleName, "[static]request.new"),
                (Action<ExecContext,
                    int, int, int, int, int, int, int>)(
                    (_, headersH,
                     contentsOptDisc, contentsStreamH,
                     trailersFutureH,
                     optsOptDisc, optsH,
                     retptr) =>
                        InvokeRequestNew(
                            headersH,
                            contentsOptDisc, contentsStreamH,
                            trailersFutureH,
                            optsOptDisc, optsH,
                            retptr)));

            // [static]response.new(
            //   headers: headers,
            //   contents: option<stream<u8>>,
            //   trailers: future<result<option<trailers>, error-code>>
            // ) -> tuple<response, future<result<_, error-code>>>
            //
            // 5 wire params: headers + option<stream>(2) +
            //   trailers-future + retArea.
            runtime.BindHostFunction(
                (HttpTypesModuleName, "[static]response.new"),
                (Action<ExecContext,
                    int, int, int, int, int>)(
                    (_, headersH,
                     contentsOptDisc, contentsStreamH,
                     trailersFutureH,
                     retptr) =>
                        InvokeResponseNew(
                            headersH,
                            contentsOptDisc, contentsStreamH,
                            trailersFutureH,
                            retptr)));

            // ---- consume-body (Slice JJ) ---------------------------
            //
            // [static]request.consume-body(this: request,
            //   res: future<result<_, error-code>>)
            //   -> tuple<stream<u8>,
            //            future<result<option<trailers>, error-code>>>
            //
            // 4 wire params: this + res-future + retArea. Return
            // tuple is 2 i32 slots > 1 → 8-byte 4-aligned retArea.
            //
            // [static]response.consume-body has the identical
            // wire signature with response in place of request.
            //
            // v0 caveat: the body data doesn't flow into the
            // returned stream yet — the stream handle is allocated
            // (empty / closed-by-spec), the trailers future is
            // allocated and pending. Full stream-bridge integration
            // ships in a follow-up alongside the cooperative-yield
            // refactor.

            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    "[static]request.consume-body"),
                (Action<ExecContext, int, int, int>)(
                    (_, thisHandle, resFutureHandle, retptr) =>
                        InvokeRequestConsumeBody(
                            thisHandle, resFutureHandle, retptr)));

            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    "[static]response.consume-body"),
                (Action<ExecContext, int, int, int>)(
                    (_, thisHandle, resFutureHandle, retptr) =>
                        InvokeResponseConsumeBody(
                            thisHandle, resFutureHandle, retptr)));

            // ---- get-headers: own<fields> (Slice DD) ---------------
            //
            // Allocates a fresh fields handle pointing at the
            // request/response impl's existing IFields instance.
            // Mutations through the returned handle are visible
            // through the parent request/response's own reads
            // (and vice versa) since both share the same impl
            // object. Resource drop on the returned handle
            // removes only that handle's entry — the parent
            // resource keeps its independent reference.
            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    "[method]request.get-headers"),
                (Func<ExecContext, int, int>)((_, self) =>
                    FieldsHandles.Allocate(
                        RequireRequest(self).GetHeaders())));

            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    "[method]response.get-headers"),
                (Func<ExecContext, int, int>)((_, self) =>
                    FieldsHandles.Allocate(
                        RequireResponse(self).GetHeaders())));

            // Per the WIT signature, set-status-code returns
            // `result` (1 flat i32 slot, no payload). The impl
            // setter can't fail, so the disc is always 0 — but
            // the wire arity needs to match the canon-ABI shape.
            runtime.BindHostFunction(
                (HttpTypesModuleName, "[method]response.set-status-code"),
                (Func<ExecContext, int, int, int>)((_, self, code) =>
                {
                    RequireResponse(self).SetStatusCode(
                        unchecked((ushort)code));
                    return 0; // result disc = ok
                }));

            // ---- request.get-options (Slice EE) ---------------------
            //
            // option<request-options> = option<own<request-options>>:
            //   flat lowering 2 slots (opt-disc + handle) > 1 →
            //   retptr 8 bytes 4-aligned.
            //   +0: option-disc (u8) + 3 pad
            //   +4: request-options handle (i32; 0 when none)
            //
            // Allocates a fresh request-options handle pointing
            // at the parent request's existing IRequestOptions
            // impl (same shared-impl pattern as get-headers).
            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    "[method]request.get-options"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeRequestGetOptions(self, retptr)));

            // wasi:http/client.send and wasi:http/handler.handle.
            // Both are async func in the WIT and lower as a
            // canon-async call. Phase 5 binds them sync-blocking
            // (.GetAwaiter().GetResult() on the Task<IResponse>);
            // when the canon-async-func wire shape stabilizes
            // (see Phase 3 Slice L), the binding moves to the
            // cooperative-yield path via the lift adapter.
            //
            // Wire convention: takes a request handle, returns
            // a response handle. The err path throws
            // HttpException; the canon-async binding lowers to
            // result<response, error-code>::err.
            // result<response, error-code> retptr layout (32 bytes
            // 4-aligned):
            //   +0:   result-disc (u8) + 3 pad
            //   +4..32: payload section (28 bytes — sized to the
            //           error-code variant which dominates the
            //           4-byte response handle)
            //     ok:  response handle at +4 (i32)
            //     err: error-code variant at +4..32 (28 bytes)
            runtime.BindHostFunction(
                (HttpClientModuleName, "send"),
                (Action<ExecContext, int, int>)(
                    (_, requestHandle, retptr) =>
                        InvokeClientSend(
                            requestHandle, retptr, realloc)));

            runtime.BindHostFunction(
                (HttpHandlerModuleName, "handle"),
                (Action<ExecContext, int, int>)(
                    (_, requestHandle, retptr) =>
                        InvokeHandlerHandle(
                            requestHandle, retptr, realloc)));

            // wasi:filesystem/types.descriptor — drop + the
            // simplest sync method as a representative wire-up.
            // The remaining ~23 methods (stat / open-at /
            // read-via-stream / etc.) follow the same pattern
            // and ship in follow-up slices alongside the
            // canon-async-func wire-shape settling.
            runtime.BindHostFunction(
                (FilesystemTypesModuleName, "[resource-drop]descriptor"),
                (Action<ExecContext, int>)((_, handle) =>
                    DescriptorHandles.Drop(handle)));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName, "[method]descriptor.get-flags"),
                (Func<ExecContext, int, int>)((_, self) =>
                    (int)RequireDescriptor(self).GetFlags()));

            // wasi:filesystem/preopens.get-directories — the
            // landmark binding that lets guests discover the
            // host-configured preopen set. Returns
            // list<tuple<descriptor, string>>:
            //
            //   retptr+0: list-ptr (i32)
            //   retptr+4: list-count (i32)
            //
            // List body at list-ptr: count tuples × 12 bytes:
            //
            //   +0: descriptor-handle (i32)
            //   +4: path-string-ptr   (i32)
            //   +8: path-string-len   (i32)
            //
            // Each tuple's path string lives in its own
            // cabi_realloc-allocated guest memory region. We
            // reuse the `realloc` allocator already constructed
            // above for the wasi:random list-returning paths —
            // a single instance per BindToRuntime call caches the
            // cabi_realloc resolution.
            runtime.BindHostFunction(
                (FilesystemPreopensModuleName, "get-directories"),
                (Action<ExecContext, int>)((_, retptr) =>
                    InvokePreopensGetDirectories(retptr, realloc)));

            // ---- wasi:filesystem/types.descriptor methods --------
            //
            // The async result<_, error-code> methods all flow
            // through WriteErrorCodeResult — the 20-byte
            // 4-aligned retptr layout mirroring header-error's.
            // The descriptor-stat-returning paths use
            // WriteDescriptorStatResult (112-byte record).
            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.create-directory-at"),
                (Action<ExecContext, int, int, int, int>)(
                    (_, self, pathPtr, pathLen, retptr) =>
                        InvokeDescriptorCreateDirectoryAt(
                            self, pathPtr, pathLen, retptr, realloc)));

            // Canon-lowered async variant: wit-bindgen emits
            // `[async-lower][method]<op>` for async-func calls.
            // Same param shape as the sync slot — flat-params
            // fit in the inline calling convention. Body runs
            // synchronously and returns STATUS_RETURNED so the
            // guest's WaitableOperation completes immediately.
            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.create-directory-at"),
                (Func<ExecContext, int, int, int, int, int>)(
                    (_, self, pathPtr, pathLen, retptr) =>
                    {
                        InvokeDescriptorCreateDirectoryAt(
                            self, pathPtr, pathLen, retptr, realloc);
                        return CanonAsyncStatusReturnedPacked;
                    }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.unlink-file-at"),
                (Action<ExecContext, int, int, int, int>)(
                    (_, self, pathPtr, pathLen, retptr) =>
                        InvokeDescriptorUnlinkFileAt(
                            self, pathPtr, pathLen, retptr, realloc)));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.unlink-file-at"),
                (Func<ExecContext, int, int, int, int, int>)(
                    (_, self, pathPtr, pathLen, retptr) =>
                    {
                        InvokeDescriptorUnlinkFileAt(
                            self, pathPtr, pathLen, retptr, realloc);
                        return CanonAsyncStatusReturnedPacked;
                    }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.remove-directory-at"),
                (Action<ExecContext, int, int, int, int>)(
                    (_, self, pathPtr, pathLen, retptr) =>
                        InvokeDescriptorRemoveDirectoryAt(
                            self, pathPtr, pathLen, retptr, realloc)));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.remove-directory-at"),
                (Func<ExecContext, int, int, int, int, int>)(
                    (_, self, pathPtr, pathLen, retptr) =>
                    {
                        InvokeDescriptorRemoveDirectoryAt(
                            self, pathPtr, pathLen, retptr, realloc);
                        return CanonAsyncStatusReturnedPacked;
                    }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.is-same-object"),
                (Func<ExecContext, int, int, int>)((_, self, other) =>
                    InvokeDescriptorIsSameObject(self, other) ? 1 : 0));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.get-type"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeDescriptorGetType(self, retptr, realloc)));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.get-type"),
                (Func<ExecContext, int, int, int>)((_, self, retptr) =>
                {
                    InvokeDescriptorGetType(self, retptr, realloc);
                    return CanonAsyncStatusReturnedPacked;
                }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.open-at"),
                (Action<ExecContext, int, int, int, int, int, int, int>)(
                    (_, self, pathFlags, pathPtr, pathLen,
                     openFlags, flags, retptr) =>
                        InvokeDescriptorOpenAt(self,
                            unchecked((uint)pathFlags), pathPtr, pathLen,
                            unchecked((uint)openFlags),
                            unchecked((uint)flags),
                            retptr, realloc)));

            // open-at canon-lowered async variant. The lowered
            // signature is `(params_ptr, results_ptr) -> i32`:
            // the 6-arg flat-params shape exceeds the inline
            // calling convention, so wit-bindgen packs params
            // into a guest-allocated struct at params_ptr and
            // passes the result-area pointer separately. Struct
            // layout (canonical-ABI, fields placed at next-
            // aligned offset):
            //   +0  self        i32  (own<descriptor>)
            //   +4  path-flags  u8   (1-bit flags)
            //   +8  path-ptr    i32  (4-aligned, 3-byte pad)
            //   +12 path-len    i32
            //   +16 open-flags  u8
            //   +17 desc-flags  u8
            //   total 20 bytes (4-aligned tail pad)
            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.open-at"),
                (Func<ExecContext, int, int, int>)(
                    (_, paramsPtr, resultsPtr) =>
                    {
                        var mem = RequireDispatcherMemory();
                        var pSpan = mem.AsSpan(paramsPtr, 20);
                        int self = System.Buffers.Binary.BinaryPrimitives
                            .ReadInt32LittleEndian(pSpan.Slice(0));
                        uint pathFlags = pSpan[4];
                        int pathPtr = System.Buffers.Binary.BinaryPrimitives
                            .ReadInt32LittleEndian(pSpan.Slice(8));
                        int pathLen = System.Buffers.Binary.BinaryPrimitives
                            .ReadInt32LittleEndian(pSpan.Slice(12));
                        uint openFlags = pSpan[16];
                        uint descFlags = pSpan[17];
                        InvokeDescriptorOpenAt(self,
                            pathFlags, pathPtr, pathLen,
                            openFlags, descFlags,
                            resultsPtr, realloc);
                        return CanonAsyncStatusReturnedPacked;
                    }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.stat"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeDescriptorStat(self, retptr, realloc)));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.stat"),
                (Func<ExecContext, int, int, int>)((_, self, retptr) =>
                {
                    InvokeDescriptorStat(self, retptr, realloc);
                    return CanonAsyncStatusReturnedPacked;
                }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.stat-at"),
                (Action<ExecContext, int, int, int, int, int>)(
                    (_, self, pathFlags, pathPtr, pathLen, retptr) =>
                        InvokeDescriptorStatAt(
                            self,
                            unchecked((uint)pathFlags),
                            pathPtr, pathLen,
                            retptr, realloc)));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.stat-at"),
                (Func<ExecContext, int, int, int, int, int, int>)(
                    (_, self, pathFlags, pathPtr, pathLen, retptr) =>
                    {
                        InvokeDescriptorStatAt(
                            self,
                            unchecked((uint)pathFlags),
                            pathPtr, pathLen,
                            retptr, realloc);
                        return CanonAsyncStatusReturnedPacked;
                    }));

            // ---- Stream-shape descriptor methods --------------------
            // read-via-stream(offset) -> tuple<stream, future>:
            //   8-byte 4-aligned retptr layout (stream-handle, future-handle)
            // write-via-stream(data, offset) -> future:
            //   single i32 return (future-handle)
            // append-via-stream(data) -> future:
            //   single i32 return
            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.read-via-stream"),
                (Action<ExecContext, int, long, int>)(
                    (_, self, offset, retptr) =>
                        InvokeDescriptorReadViaStream(
                            self, unchecked((ulong)offset), retptr)));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.write-via-stream"),
                (Func<ExecContext, int, int, long, int>)(
                    (_, self, streamHandle, offset) =>
                        InvokeDescriptorWriteViaStream(
                            self, streamHandle, unchecked((ulong)offset))));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.append-via-stream"),
                (Func<ExecContext, int, int, int>)(
                    (_, self, streamHandle) =>
                        InvokeDescriptorAppendViaStream(
                            self, streamHandle)));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.read-directory"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeDescriptorReadDirectory(
                        self, retptr, realloc)));

            // ---- Remaining result<_, error-code> methods ----------
            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.set-size"),
                (Action<ExecContext, int, long, int>)(
                    (_, self, size, retptr) =>
                        InvokeDescriptorResultErrorCodeNoArgs(
                            self, retptr, realloc,
                            desc => desc.SetSizeAsync(
                                new FileSize(unchecked((ulong)size)))
                                .GetAwaiter().GetResult())));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.set-size"),
                (Func<ExecContext, int, long, int, int>)(
                    (_, self, size, retptr) =>
                    {
                        InvokeDescriptorResultErrorCodeNoArgs(
                            self, retptr, realloc,
                            desc => desc.SetSizeAsync(
                                new FileSize(unchecked((ulong)size)))
                                .GetAwaiter().GetResult());
                        return CanonAsyncStatusReturnedPacked;
                    }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.sync"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeDescriptorResultErrorCodeNoArgs(
                        self, retptr, realloc,
                        desc => desc.SyncAsync()
                            .GetAwaiter().GetResult())));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.sync"),
                (Func<ExecContext, int, int, int>)((_, self, retptr) =>
                {
                    InvokeDescriptorResultErrorCodeNoArgs(
                        self, retptr, realloc,
                        desc => desc.SyncAsync()
                            .GetAwaiter().GetResult());
                    return CanonAsyncStatusReturnedPacked;
                }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.sync-data"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeDescriptorResultErrorCodeNoArgs(
                        self, retptr, realloc,
                        desc => desc.SyncDataAsync()
                            .GetAwaiter().GetResult())));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.sync-data"),
                (Func<ExecContext, int, int, int>)((_, self, retptr) =>
                {
                    InvokeDescriptorResultErrorCodeNoArgs(
                        self, retptr, realloc,
                        desc => desc.SyncDataAsync()
                            .GetAwaiter().GetResult());
                    return CanonAsyncStatusReturnedPacked;
                }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.advise"),
                (Action<ExecContext, int, long, long, int, int>)(
                    (_, self, offset, length, advice, retptr) =>
                        InvokeDescriptorResultErrorCodeNoArgs(
                            self, retptr, realloc,
                            desc => desc.AdviseAsync(
                                new FileSize(unchecked((ulong)offset)),
                                new FileSize(unchecked((ulong)length)),
                                (Advice)advice)
                                .GetAwaiter().GetResult())));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.advise"),
                (Func<ExecContext, int, long, long, int, int, int>)(
                    (_, self, offset, length, advice, retptr) =>
                    {
                        InvokeDescriptorResultErrorCodeNoArgs(
                            self, retptr, realloc,
                            desc => desc.AdviseAsync(
                                new FileSize(unchecked((ulong)offset)),
                                new FileSize(unchecked((ulong)length)),
                                (Advice)advice)
                                .GetAwaiter().GetResult());
                        return CanonAsyncStatusReturnedPacked;
                    }));

            // ---- metadata-hash / metadata-hash-at ------------------
            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.metadata-hash"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeDescriptorMetadataHash(
                        self, retptr, realloc, atPath: null,
                        pathFlags: 0)));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.metadata-hash"),
                (Func<ExecContext, int, int, int>)((_, self, retptr) =>
                {
                    InvokeDescriptorMetadataHash(
                        self, retptr, realloc, atPath: null,
                        pathFlags: 0);
                    return CanonAsyncStatusReturnedPacked;
                }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.metadata-hash-at"),
                (Action<ExecContext, int, int, int, int, int>)(
                    (_, self, pathFlags, pathPtr, pathLen, retptr) =>
                        InvokeDescriptorMetadataHash(
                            self, retptr, realloc,
                            atPath: ReadGuestUtf8(pathPtr, pathLen),
                            pathFlags: unchecked((uint)pathFlags))));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.metadata-hash-at"),
                (Func<ExecContext, int, int, int, int, int, int>)(
                    (_, self, pathFlags, pathPtr, pathLen, retptr) =>
                    {
                        InvokeDescriptorMetadataHash(
                            self, retptr, realloc,
                            atPath: ReadGuestUtf8(pathPtr, pathLen),
                            pathFlags: unchecked((uint)pathFlags));
                        return CanonAsyncStatusReturnedPacked;
                    }));

            // ---- readlink-at ---------------------------------------
            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.readlink-at"),
                (Action<ExecContext, int, int, int, int>)(
                    (_, self, pathPtr, pathLen, retptr) =>
                        InvokeDescriptorReadlinkAt(
                            self, pathPtr, pathLen, retptr, realloc)));

            // ---- variant-bearing descriptor methods ----------------
            //
            // new-timestamp variant flat-lowering: 3 slots
            // (disc i32, seconds i64, nanoseconds i32). The
            // joined payload is sized to the timestamp(instant)
            // arm — instant flat-lowers to (i64, i32). Arms
            // no-change/now leave seconds/nanoseconds slots
            // unused per the disc.
            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.set-times"),
                (Action<ExecContext,
                    int,
                    int, long, int,
                    int, long, int,
                    int>)(
                    (_, self,
                     aDisc, aSecs, aNanos,
                     mDisc, mSecs, mNanos,
                     retptr) =>
                        InvokeDescriptorResultErrorCodeNoArgs(
                            self, retptr, realloc,
                            desc => desc.SetTimesAsync(
                                ReadNewTimestampFromSlots(
                                    aDisc, aSecs, aNanos),
                                ReadNewTimestampFromSlots(
                                    mDisc, mSecs, mNanos))
                                .GetAwaiter().GetResult())));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.set-times-at"),
                (Action<ExecContext,
                    int,
                    int, int, int,
                    int, long, int,
                    int, long, int,
                    int>)(
                    (_, self,
                     pathFlags, pathPtr, pathLen,
                     aDisc, aSecs, aNanos,
                     mDisc, mSecs, mNanos,
                     retptr) =>
                        InvokeDescriptorSetTimesAt(
                            self,
                            unchecked((uint)pathFlags),
                            pathPtr, pathLen,
                            aDisc, aSecs, aNanos,
                            mDisc, mSecs, mNanos,
                            retptr, realloc)));

            // link-at / rename-at: borrow<descriptor> arg shows
            // up as a plain i32 handle in the flat lowering, the
            // same shape as any other resource handle.
            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.link-at"),
                (Action<ExecContext,
                    int, int, int, int, int, int, int, int>)(
                    (_, self, oldPathFlags,
                     oldPathPtr, oldPathLen,
                     newDesc, newPathPtr, newPathLen, retptr) =>
                        InvokeDescriptorLinkAt(
                            self,
                            unchecked((uint)oldPathFlags),
                            oldPathPtr, oldPathLen,
                            newDesc, newPathPtr, newPathLen,
                            retptr, realloc)));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.rename-at"),
                (Action<ExecContext,
                    int, int, int, int, int, int, int>)(
                    (_, self,
                     oldPathPtr, oldPathLen,
                     newDesc, newPathPtr, newPathLen, retptr) =>
                        InvokeDescriptorRenameAt(
                            self, oldPathPtr, oldPathLen,
                            newDesc, newPathPtr, newPathLen,
                            retptr, realloc)));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[method]descriptor.symlink-at"),
                (Action<ExecContext,
                    int, int, int, int, int, int>)(
                    (_, self,
                     oldPathPtr, oldPathLen,
                     newPathPtr, newPathLen, retptr) =>
                        InvokeDescriptorSymlinkAt(
                            self, oldPathPtr, oldPathLen,
                            newPathPtr, newPathLen,
                            retptr, realloc)));

            // ---- [async-lower] (params_ptr, results_ptr) bindings ----
            //
            // For methods whose inline-flattened params exceed
            // the canonical-ABI calling-convention limit (set-
            // times, set-times-at, link-at, rename-at, symlink-
            // at), the async-lower lowering packs all params
            // into a struct at params_ptr and passes a separate
            // results_ptr for the result-area. Each unpack
            // helper reads the struct fields per the canonical-
            // ABI layout (per-field alignment + padding) and
            // delegates to the existing sync host body.
            //
            // new-timestamp variant layout (8-aligned, 24
            // bytes): disc:u8 at +0, 7-byte pad, seconds:s64
            // at +8, nanoseconds:u32 at +16, 4-byte tail pad.

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.set-times"),
                (Func<ExecContext, int, int, int>)(
                    (_, paramsPtr, resultsPtr) =>
                    {
                        var mem = RequireDispatcherMemory();
                        var p = mem.AsSpan(paramsPtr, 56);
                        int self = ReadI32(p, 0);
                        var (aDisc, aSecs, aNanos) =
                            ReadNewTimestamp(p, 8);
                        var (mDisc, mSecs, mNanos) =
                            ReadNewTimestamp(p, 32);
                        InvokeDescriptorResultErrorCodeNoArgs(
                            self, resultsPtr, realloc,
                            desc => desc.SetTimesAsync(
                                ReadNewTimestampFromSlots(
                                    aDisc, aSecs, aNanos),
                                ReadNewTimestampFromSlots(
                                    mDisc, mSecs, mNanos))
                                .GetAwaiter().GetResult());
                        return CanonAsyncStatusReturnedPacked;
                    }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.set-times-at"),
                (Func<ExecContext, int, int, int>)(
                    (_, paramsPtr, resultsPtr) =>
                    {
                        var mem = RequireDispatcherMemory();
                        var p = mem.AsSpan(paramsPtr, 64);
                        int self = ReadI32(p, 0);
                        uint pathFlags = p[4];
                        int pathPtr = ReadI32(p, 8);
                        int pathLen = ReadI32(p, 12);
                        var (aDisc, aSecs, aNanos) =
                            ReadNewTimestamp(p, 16);
                        var (mDisc, mSecs, mNanos) =
                            ReadNewTimestamp(p, 40);
                        InvokeDescriptorSetTimesAt(
                            self, pathFlags, pathPtr, pathLen,
                            aDisc, aSecs, aNanos,
                            mDisc, mSecs, mNanos,
                            resultsPtr, realloc);
                        return CanonAsyncStatusReturnedPacked;
                    }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.link-at"),
                (Func<ExecContext, int, int, int>)(
                    (_, paramsPtr, resultsPtr) =>
                    {
                        var mem = RequireDispatcherMemory();
                        var p = mem.AsSpan(paramsPtr, 28);
                        int self = ReadI32(p, 0);
                        uint oldPathFlags = p[4];
                        int oldPathPtr = ReadI32(p, 8);
                        int oldPathLen = ReadI32(p, 12);
                        int newDesc = ReadI32(p, 16);
                        int newPathPtr = ReadI32(p, 20);
                        int newPathLen = ReadI32(p, 24);
                        InvokeDescriptorLinkAt(
                            self, oldPathFlags,
                            oldPathPtr, oldPathLen,
                            newDesc, newPathPtr, newPathLen,
                            resultsPtr, realloc);
                        return CanonAsyncStatusReturnedPacked;
                    }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.rename-at"),
                (Func<ExecContext, int, int, int>)(
                    (_, paramsPtr, resultsPtr) =>
                    {
                        var mem = RequireDispatcherMemory();
                        var p = mem.AsSpan(paramsPtr, 24);
                        int self = ReadI32(p, 0);
                        int oldPathPtr = ReadI32(p, 4);
                        int oldPathLen = ReadI32(p, 8);
                        int newDesc = ReadI32(p, 12);
                        int newPathPtr = ReadI32(p, 16);
                        int newPathLen = ReadI32(p, 20);
                        InvokeDescriptorRenameAt(
                            self, oldPathPtr, oldPathLen,
                            newDesc, newPathPtr, newPathLen,
                            resultsPtr, realloc);
                        return CanonAsyncStatusReturnedPacked;
                    }));

            runtime.BindHostFunction(
                (FilesystemTypesModuleName,
                    "[async-lower][method]descriptor.symlink-at"),
                (Func<ExecContext, int, int, int>)(
                    (_, paramsPtr, resultsPtr) =>
                    {
                        var mem = RequireDispatcherMemory();
                        var p = mem.AsSpan(paramsPtr, 20);
                        int self = ReadI32(p, 0);
                        int oldPathPtr = ReadI32(p, 4);
                        int oldPathLen = ReadI32(p, 8);
                        int newPathPtr = ReadI32(p, 12);
                        int newPathLen = ReadI32(p, 16);
                        InvokeDescriptorSymlinkAt(
                            self, oldPathPtr, oldPathLen,
                            newPathPtr, newPathLen,
                            resultsPtr, realloc);
                        return CanonAsyncStatusReturnedPacked;
                    }));

            // wasi:sockets — resource drops + ip-name-lookup.
            // Full ITcpSocket/IUdpSocket method wire-ups + a
            // System.Net.Sockets-backed default impl ship in
            // a follow-up slice; the resource drops + the DNS
            // lookup are the immediately useful pieces.
            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[resource-drop]tcp-socket"),
                (Action<ExecContext, int>)((_, handle) =>
                    TcpSocketHandles.Drop(handle)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[resource-drop]udp-socket"),
                (Action<ExecContext, int>)((_, handle) =>
                    UdpSocketHandles.Drop(handle)));

            // wasi:sockets/ip-name-lookup.resolve-addresses
            //   async func(name: string) -> result<list<ip-address>, error-code>
            //
            // The (name) arg lowers to (i32 ptr, i32 len). The
            // result list flat-lowers to 2 i32s > MAX_FLAT_RESULTS
            // → retptr layout (8 bytes 4-aligned: list-ptr +
            // list-count). Each ip-address variant entry is 18
            // bytes 2-aligned: disc byte (case 0=ipv4, 1=ipv6) +
            // 1-byte align-pad + 16 bytes payload (max(4, 16)).
            // Async-blocking on the Task<IReadOnlyList<IpAddress>>
            // until the canon-async-func wire shape settles.
            runtime.BindHostFunction(
                (IpNameLookupModuleName, "resolve-addresses"),
                (Action<ExecContext, int, int, int>)((_, namePtr, nameLen, retptr) =>
                    InvokeResolveAddresses(namePtr, nameLen, retptr, realloc)));

            // ---- wasi:sockets/types.tcp-socket simple methods ----
            //
            // Static factory + simple primitive getters/setters.
            // Bind / Connect / Listen / Send / Receive land in a
            // follow-up slice alongside the variant-arg flat-
            // lowering and stream-shape work.
            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[static]tcp-socket.create"),
                (Action<ExecContext, int, int>)((_, family, retptr) =>
                    InvokeTcpSocketCreate(
                        (IpAddressFamily)family, retptr, realloc)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.get-address-family"),
                (Func<ExecContext, int, int>)((_, self) =>
                    (int)RequireTcpSocket(self).GetAddressFamily()));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.get-is-listening"),
                (Func<ExecContext, int, int>)((_, self) =>
                    RequireTcpSocket(self).GetIsListening() ? 1 : 0));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.get-receive-buffer-size"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeTcpSocketGetBufferSize(
                        self, retptr, realloc, sendBuffer: false)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.set-receive-buffer-size"),
                (Action<ExecContext, int, long, int>)(
                    (_, self, value, retptr) =>
                        InvokeTcpSocketSetBufferSize(
                            self, unchecked((ulong)value),
                            retptr, realloc, sendBuffer: false)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.get-send-buffer-size"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeTcpSocketGetBufferSize(
                        self, retptr, realloc, sendBuffer: true)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.set-send-buffer-size"),
                (Action<ExecContext, int, long, int>)(
                    (_, self, value, retptr) =>
                        InvokeTcpSocketSetBufferSize(
                            self, unchecked((ulong)value),
                            retptr, realloc, sendBuffer: true)));

            // ---- Stream-shape tcp-socket methods --------------------
            // send(data: stream<u8>) -> future<...>:
            //   single i32 future-handle return
            // receive() -> tuple<stream<u8>, future<...>>:
            //   8-byte 4-aligned retptr (stream-handle, future-handle)
            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[method]tcp-socket.send"),
                (Func<ExecContext, int, int, int>)(
                    (_, self, streamHandle) =>
                        InvokeTcpSocketSend(self, streamHandle)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[method]tcp-socket.receive"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeTcpSocketReceive(self, retptr)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[method]tcp-socket.listen"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeTcpSocketListen(self, retptr, realloc)));

            // ---- bind / connect: ip-socket-address variant-arg ----
            //
            // ip-socket-address variant flat-lowers to 12 i32
            // slots: 1 disc + 11 joined payload. The joined
            // payload is sized to the larger case (ipv6 = 11
            // slots: port + flow-info + 8×u16 address + scope-id).
            // ipv4 (port + 4×u8) lives in the first 5 slots; the
            // remaining 6 are unused per disc=ipv4.
            //
            // Total wire signature: (self, 12 variant slots,
            // retptr) = 14 i32 params.
            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[method]tcp-socket.bind"),
                (Action<ExecContext, int, int, int, int, int, int, int,
                    int, int, int, int, int, int, int>)(
                    (_, self, disc, s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11,
                     retptr) =>
                        InvokeTcpSocketBind(self,
                            ReadIpSocketAddressFromSlots(
                                disc, s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11),
                            retptr, realloc)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[method]tcp-socket.connect"),
                (Action<ExecContext, int, int, int, int, int, int, int,
                    int, int, int, int, int, int, int>)(
                    (_, self, disc, s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11,
                     retptr) =>
                        InvokeTcpSocketConnect(self,
                            ReadIpSocketAddressFromSlots(
                                disc, s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11),
                            retptr, realloc)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[method]udp-socket.bind"),
                (Action<ExecContext, int, int, int, int, int, int, int,
                    int, int, int, int, int, int, int>)(
                    (_, self, disc, s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11,
                     retptr) =>
                        InvokeUdpSocketBind(self,
                            ReadIpSocketAddressFromSlots(
                                disc, s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11),
                            retptr, realloc)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[method]udp-socket.connect"),
                (Action<ExecContext, int, int, int, int, int, int, int,
                    int, int, int, int, int, int, int>)(
                    (_, self, disc, s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11,
                     retptr) =>
                        InvokeUdpSocketConnect(self,
                            ReadIpSocketAddressFromSlots(
                                disc, s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11),
                            retptr, realloc)));

            // ---- get-local-address / get-remote-address ----------
            //
            // result<ip-socket-address, error-code>: 36-byte
            // 4-aligned retptr. Per-side variants share the
            // wire encoder; the per-getter only differs in
            // which impl method it calls.
            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.get-local-address"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeTcpSocketGetLocalAddress(self, retptr, realloc)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.get-remote-address"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeTcpSocketGetRemoteAddress(self, retptr, realloc)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]udp-socket.get-local-address"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeUdpSocketGetLocalAddress(self, retptr, realloc)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]udp-socket.get-remote-address"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeUdpSocketGetRemoteAddress(self, retptr, realloc)));

            // ---- wasi:sockets/types.udp-socket simple methods ----
            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[static]udp-socket.create"),
                (Action<ExecContext, int, int>)((_, family, retptr) =>
                    InvokeUdpSocketCreate(
                        (IpAddressFamily)family, retptr, realloc)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]udp-socket.get-address-family"),
                (Func<ExecContext, int, int>)((_, self) =>
                    (int)RequireUdpSocket(self).GetAddressFamily()));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]udp-socket.get-unicast-hop-limit"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeUdpSocketGetHopLimit(self, retptr, realloc)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]udp-socket.set-unicast-hop-limit"),
                (Action<ExecContext, int, int, int>)(
                    (_, self, value, retptr) =>
                        InvokeUdpSocketSetHopLimit(
                            self, (byte)value, retptr, realloc)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]udp-socket.get-receive-buffer-size"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeUdpSocketGetBufferSize(
                        self, retptr, realloc, sendBuffer: false)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]udp-socket.set-receive-buffer-size"),
                (Action<ExecContext, int, long, int>)(
                    (_, self, value, retptr) =>
                        InvokeUdpSocketSetBufferSize(
                            self, unchecked((ulong)value),
                            retptr, realloc, sendBuffer: false)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]udp-socket.get-send-buffer-size"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeUdpSocketGetBufferSize(
                        self, retptr, realloc, sendBuffer: true)));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]udp-socket.set-send-buffer-size"),
                (Action<ExecContext, int, long, int>)(
                    (_, self, value, retptr) =>
                        InvokeUdpSocketSetBufferSize(
                            self, unchecked((ulong)value),
                            retptr, realloc, sendBuffer: true)));

            // ---- UDP send (Slice FF) ------------------------------
            //
            // udp-socket.send(data: list<u8>,
            //                 remote-address: option<ip-socket-address>)
            //   -> result<_, error-code>
            //
            // Flat-lowered wire signature:
            //   self                        (1)
            //   list<u8>: ptr, len          (2)
            //   option<ip-socket-address>:
            //     opt-disc                  (1)
            //     ip-sock-addr variant:
            //       disc                    (1)
            //       joined payload (11 slots, sized to ipv6's
            //       port+flow-info+8×u16-addr+scope-id)
            //                               (12 total)
            //   retArea                     (1)
            //
            // Total = 17 wire params. System.Action only goes up
            // to 16 generic args (= 17 type args including
            // ExecContext), so we need a custom delegate type
            // (UdpSendDelegate, declared below) to reach 18.
            runtime.BindHostFunction(
                (SocketsTypesModuleName, "[method]udp-socket.send"),
                (UdpSendDelegate)((_, self,
                    dataPtr, dataLen,
                    optDisc, addrDisc,
                    s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11,
                    retptr) =>
                        InvokeUdpSocketSend(
                            self,
                            dataPtr, dataLen,
                            optDisc, addrDisc,
                            s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11,
                            retptr, realloc)));

            // ---- UDP receive (Slice Y) -----------------------------
            //
            // udp-socket.receive: () -> result<tuple<list<u8>,
            // ip-socket-address>, error-code>. 44-byte 4-aligned
            // retptr. Sync-blocks the async ReceiveAsync via
            // .GetAwaiter().GetResult() — cooperative-yield
            // migration is a separate large slice.
            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]udp-socket.receive"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeUdpSocketReceive(self, retptr, realloc)));

            // ---- TCP simple getter/setter cluster (Slice X) -------
            //
            // The remaining flat tcp-socket methods:
            // set-listen-backlog-size, keep-alive family
            // (enabled/idle-time/interval/count), hop-limit.
            // (get-is-listening was wired in Slice O.) All getters
            // return result<X, error-code> via the matching retptr
            // shape; setters return result<_, error-code> via the
            // standard 20-byte sockets-error-code retptr.

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.set-listen-backlog-size"),
                (Action<ExecContext, int, long, int>)(
                    (_, self, value, retptr) =>
                        InvokeTcpSocketSetterErrorCode(
                            self, retptr, realloc,
                            s => s.SetListenBacklogSize(
                                unchecked((ulong)value)))));

            // ---- keep-alive family --------------------------------
            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.get-keep-alive-enabled"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeTcpSocketGetterResultBool(
                        self, retptr, realloc,
                        s => s.GetKeepAliveEnabled())));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.set-keep-alive-enabled"),
                (Action<ExecContext, int, int, int>)(
                    (_, self, value, retptr) =>
                        InvokeTcpSocketSetterErrorCode(
                            self, retptr, realloc,
                            s => s.SetKeepAliveEnabled(value != 0))));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.get-keep-alive-idle-time"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeTcpSocketGetterResultU64(
                        self, retptr, realloc,
                        s => s.GetKeepAliveIdleTime())));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.set-keep-alive-idle-time"),
                (Action<ExecContext, int, long, int>)(
                    (_, self, value, retptr) =>
                        InvokeTcpSocketSetterErrorCode(
                            self, retptr, realloc,
                            s => s.SetKeepAliveIdleTime(
                                unchecked((ulong)value)))));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.get-keep-alive-interval"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeTcpSocketGetterResultU64(
                        self, retptr, realloc,
                        s => s.GetKeepAliveInterval())));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.set-keep-alive-interval"),
                (Action<ExecContext, int, long, int>)(
                    (_, self, value, retptr) =>
                        InvokeTcpSocketSetterErrorCode(
                            self, retptr, realloc,
                            s => s.SetKeepAliveInterval(
                                unchecked((ulong)value)))));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.get-keep-alive-count"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeTcpSocketGetterResultU32(
                        self, retptr, realloc,
                        s => s.GetKeepAliveCount())));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.set-keep-alive-count"),
                (Action<ExecContext, int, int, int>)(
                    (_, self, value, retptr) =>
                        InvokeTcpSocketSetterErrorCode(
                            self, retptr, realloc,
                            s => s.SetKeepAliveCount(
                                unchecked((uint)value)))));

            // ---- hop-limit -----------------------------------------
            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.get-hop-limit"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeTcpSocketGetterResultU8(
                        self, retptr, realloc,
                        s => s.GetHopLimit())));

            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]tcp-socket.set-hop-limit"),
                (Action<ExecContext, int, int, int>)(
                    (_, self, value, retptr) =>
                        InvokeTcpSocketSetterErrorCode(
                            self, retptr, realloc,
                            s => s.SetHopLimit(unchecked((byte)value)))));

            // ---- UDP disconnect ------------------------------------
            runtime.BindHostFunction(
                (SocketsTypesModuleName,
                    "[method]udp-socket.disconnect"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeUdpSocketSetterErrorCode(
                        self, retptr, realloc,
                        s => s.Disconnect())));
        }

        // ---- wasi:sockets udp-socket binding bodies (Slice P) -------

        /// <summary>Invoke <c>[static]udp-socket.create(family)</c>.</summary>
        public void InvokeUdpSocketCreate(
            IpAddressFamily family, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            int handle = 0;
            try
            {
                var socket = new UdpSocket(family);
                handle = UdpSocketHandles.Allocate(socket);
            }
            catch (SocketsException ex) { caught = ex; }

            var dest = memory.AsSpan(retptr, ResultSocketsErrorCodeSize);
            if (caught == null)
            {
                dest.Clear();
                dest[0] = 0;
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(4), handle);
            }
            else
            {
                WriteSocketsErrorCodeResult(dest, caught, realloc, memory);
            }
        }

        /// <summary>Invoke
        /// <c>[method]udp-socket.get-unicast-hop-limit()</c>. Result
        /// is <c>result&lt;u8, error-code&gt;</c> — 20-byte 4-aligned
        /// retptr: disc at +0, u8 value at +4 (3-byte tail pad), err
        /// payload at +4 on err.</summary>
        public void InvokeUdpSocketGetHopLimit(
            int self, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            byte value = 0;
            try { value = RequireUdpSocket(self).GetUnicastHopLimit(); }
            catch (SocketsException ex) { caught = ex; }

            var dest = memory.AsSpan(retptr, 20);
            if (caught == null)
            {
                dest.Clear();
                dest[0] = 0;
                dest[4] = value;
            }
            else
            {
                WriteSocketsErrorCodeResult(dest, caught, realloc, memory);
            }
        }

        /// <summary>Invoke
        /// <c>[method]udp-socket.set-unicast-hop-limit(value)</c>.</summary>
        public void InvokeUdpSocketSetHopLimit(
            int self, byte value, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            try { RequireUdpSocket(self).SetUnicastHopLimit(value); }
            catch (SocketsException ex) { caught = ex; }
            WriteSocketsErrorCodeResult(
                memory.AsSpan(retptr, ResultSocketsErrorCodeSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke
        /// <c>[method]udp-socket.get-{send,receive}-buffer-size()</c>.
        /// Same 24-byte 8-aligned result&lt;u64, error-code&gt;
        /// layout as <see cref="InvokeTcpSocketGetBufferSize"/>.</summary>
        public void InvokeUdpSocketGetBufferSize(
            int self, int retptr, ICabiRealloc realloc, bool sendBuffer)
        {
            var memory = RequireMemoryForHttp();
            if (retptr < 0 || (retptr & 0x7) != 0
                || retptr + 24 > memory.Data.Length)
                throw new InvalidOperationException(
                    "udp-socket.get-{buffer}-size: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}).");
            SocketsException? caught = null;
            ulong value = 0;
            try
            {
                var sock = RequireUdpSocket(self);
                value = sendBuffer
                    ? sock.GetSendBufferSize()
                    : sock.GetReceiveBufferSize();
            }
            catch (SocketsException ex) { caught = ex; }

            var dest = memory.AsSpan(retptr, 24);
            dest.Clear();
            if (caught == null)
            {
                dest[0] = 0;
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt64LittleEndian(dest.Slice(8), value);
            }
            else
            {
                dest[0] = 1;
                WriteSocketsErrorCodeBytes(
                    dest.Slice(8, 16), caught, realloc, memory);
            }
        }

        /// <summary>Invoke
        /// <c>[method]udp-socket.set-{send,receive}-buffer-size(value)</c>.</summary>
        public void InvokeUdpSocketSetBufferSize(
            int self, ulong value, int retptr,
            ICabiRealloc realloc, bool sendBuffer)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            try
            {
                var sock = RequireUdpSocket(self);
                if (sendBuffer) sock.SetSendBufferSize(value);
                else sock.SetReceiveBufferSize(value);
            }
            catch (SocketsException ex) { caught = ex; }
            WriteSocketsErrorCodeResult(
                memory.AsSpan(retptr, ResultSocketsErrorCodeSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke
        /// <c>[method]udp-socket.send(data, remote-address)</c>.
        /// Reads the list&lt;u8&gt; payload from guest memory,
        /// decodes the option&lt;ip-socket-address&gt; arg from
        /// its 13 flat slots, sync-blocks SendAsync, writes the
        /// standard 20-byte sockets-error-code retptr.</summary>
        public void InvokeUdpSocketSend(
            int self,
            int dataPtr, int dataLen,
            int optDisc, int addrDisc,
            int s1, int s2, int s3, int s4, int s5,
            int s6, int s7, int s8, int s9, int s10, int s11,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);

            SocketsException? caught = null;
            try
            {
                byte[] data = dataLen > 0
                    ? memory.AsSpan(dataPtr, dataLen).ToArray()
                    : Array.Empty<byte>();
                IpSocketAddress? remote = null;
                if (optDisc != 0)
                {
                    // some(ip-socket-address) — decode via the
                    // shared Slice-T reader.
                    remote = ReadIpSocketAddressFromSlots(
                        addrDisc,
                        s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11);
                }
                RequireUdpSocket(self).SendAsync(data, remote)
                    .GetAwaiter().GetResult();
            }
            catch (SocketsException ex) { caught = ex; }
            WriteSocketsErrorCodeResult(
                memory.AsSpan(retptr, ResultSocketsErrorCodeSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke
        /// <c>[method]udp-socket.receive()</c>. Writes
        /// <c>result&lt;tuple&lt;list&lt;u8&gt;,
        /// ip-socket-address&gt;, error-code&gt;</c> through a
        /// 44-byte 4-aligned retptr. Sync-blocks the async impl
        /// (same pattern as the rest of the Slice O sockets).
        ///
        /// Retptr layout (44 bytes 4-aligned):
        /// <code>
        /// +0:    result-disc (u8) + 3 pad
        /// +4..44: payload section (40 bytes, sized to the ok tuple)
        ///
        ///   ok (tuple):
        ///     +4..8:   list-ptr (i32, ICabiRealloc-allocated)
        ///     +8..12:  list-len (i32)
        ///     +12..44: ip-socket-address (32 bytes)
        ///       +12:    ip-sock-addr disc (u8) + 3 pad
        ///       +16..20: port (u16, ipv6 pads u16 to align-4)
        ///       +20..24: flow-info (u32, ipv6 only)
        ///       +24..40: 8×u16 BE addr groups (ipv6) /
        ///                ipv4 fills +16..22 only (port + octets)
        ///       +40..44: scope-id (u32, ipv6 only)
        ///
        ///   err (error-code variant):
        ///     +4..16: error-code variant + payload (per
        ///             WriteSocketsErrorCodeBytes)
        ///     +16..44: unused, zero-filled
        /// </code>
        /// </summary>
        public void InvokeUdpSocketReceive(
            int self, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 44 > memory.Data.Length)
                throw new InvalidOperationException(
                    "udp-socket.receive: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 44-byte 4-aligned " +
                    "return area.");

            SocketsException? caught = null;
            byte[] data = Array.Empty<byte>();
            IpSocketAddress source = default;
            try
            {
                var sock = RequireUdpSocket(self);
                (data, source) = sock.ReceiveAsync()
                    .GetAwaiter().GetResult();
            }
            catch (SocketsException ex) { caught = ex; }

            var dest = memory.AsSpan(retptr, 44);
            dest.Clear();
            if (caught != null)
            {
                dest[0] = 1;
                WriteSocketsErrorCodeBytes(
                    dest.Slice(4, 12), caught, realloc, memory);
                return;
            }

            dest[0] = 0; // ok disc
            // list<u8>: allocate via cabi_realloc and copy.
            int listPtr = data.Length > 0
                ? realloc.Allocate(1, data.Length) : 0;
            if (data.Length > 0)
                new ReadOnlySpan<byte>(data)
                    .CopyTo(memory.AsSpan(listPtr, data.Length));
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), listPtr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(8), data.Length);

            // ip-socket-address variant at +12..44.
            //   +12: variant disc (u8) + 3 pad
            //   +16..: payload (28 bytes max for ipv6; ipv4 fits
            //          in +16..22 only)
            dest[12] = (byte)(source.Family == IpAddressFamily.Ipv4
                ? 0 : 1);
            if (source.Family == IpAddressFamily.Ipv4)
            {
                dest[16] = (byte)(source.V4.Port & 0xFF);
                dest[17] = (byte)((source.V4.Port >> 8) & 0xFF);
                dest[18] = source.V4.Address.A;
                dest[19] = source.V4.Address.B;
                dest[20] = source.V4.Address.C;
                dest[21] = source.V4.Address.D;
            }
            else
            {
                dest[16] = (byte)(source.V6.Port & 0xFF);
                dest[17] = (byte)((source.V6.Port >> 8) & 0xFF);
                // +18..20 pad (to align-4 for flow-info at +20)
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt32LittleEndian(
                        dest.Slice(20, 4), source.V6.FlowInfo);
                var v6 = source.V6.Address;
                ushort[] groups = new ushort[]
                {
                    v6.G0, v6.G1, v6.G2, v6.G3,
                    v6.G4, v6.G5, v6.G6, v6.G7,
                };
                for (int slot = 0; slot < 8; slot++)
                {
                    dest[24 + slot * 2 + 0] =
                        (byte)((groups[slot] >> 8) & 0xFF);
                    dest[24 + slot * 2 + 1] =
                        (byte)(groups[slot] & 0xFF);
                }
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt32LittleEndian(
                        dest.Slice(40, 4), source.V6.ScopeId);
            }
        }

        private IUdpSocket RequireUdpSocket(int handle)
        {
            var s = UdpSocketHandles.Get(handle);
            if (s == null)
                throw new SocketsException(
                    Sockets.ErrorCode.InvalidState,
                    $"wasi:sockets/types.udp-socket: handle " +
                    $"{handle} is not allocated.");
            return s;
        }

        // ---- wasi:sockets tcp-socket binding bodies (Slice O) -------

        /// <summary>Invoke <c>[static]tcp-socket.create(family)</c>.
        /// Allocates a fresh <see cref="TcpSocket"/> on success
        /// and writes its handle at retptr+4; on err encodes the
        /// sockets error-code at retptr.</summary>
        public void InvokeTcpSocketCreate(
            IpAddressFamily family, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            int handle = 0;
            try
            {
                var socket = new TcpSocket(family);
                handle = TcpSocketHandles.Allocate(socket);
            }
            catch (SocketsException ex) { caught = ex; }

            var dest = memory.AsSpan(retptr, ResultSocketsErrorCodeSize);
            if (caught == null)
            {
                dest.Clear();
                dest[0] = 0; // ok
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(4), handle);
            }
            else
            {
                WriteSocketsErrorCodeResult(dest, caught, realloc, memory);
            }
        }

        /// <summary>Invoke
        /// <c>[method]tcp-socket.get-{send,receive}-buffer-size()</c>.
        /// Writes <c>result&lt;u64, error-code&gt;</c> at retptr —
        /// 16 bytes 8-aligned (disc i32 + i64 value), or err
        /// payload on failure.</summary>
        public void InvokeTcpSocketGetBufferSize(
            int self, int retptr, ICabiRealloc realloc, bool sendBuffer)
        {
            var memory = RequireMemoryForHttp();
            // result<u64, error-code>: align = max(8, 4) = 8,
            // payload-offset = round_up(1, 8) = 8.
            // size = round_up(8 + max(8, 16), 8) = round_up(24, 8) = 24.
            if (retptr < 0 || (retptr & 0x7) != 0
                || retptr + 24 > memory.Data.Length)
                throw new InvalidOperationException(
                    "tcp-socket.get-{buffer}-size: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 24-byte 8-aligned " +
                    "return area.");

            SocketsException? caught = null;
            ulong value = 0;
            try
            {
                var socket = RequireTcpSocket(self);
                value = sendBuffer
                    ? socket.GetSendBufferSize()
                    : socket.GetReceiveBufferSize();
            }
            catch (SocketsException ex) { caught = ex; }

            var dest = memory.AsSpan(retptr, 24);
            dest.Clear();
            if (caught == null)
            {
                dest[0] = 0; // result-disc = ok
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt64LittleEndian(dest.Slice(8), value);
            }
            else
            {
                dest[0] = 1; // err
                // error-code variant at +8 (16 bytes).
                WriteSocketsErrorCodeBytes(
                    dest.Slice(8, 16), caught, realloc, memory);
            }
        }

        /// <summary>Invoke
        /// <c>[method]tcp-socket.set-{send,receive}-buffer-size(value)</c>.
        /// </summary>
        public void InvokeTcpSocketSetBufferSize(
            int self, ulong value, int retptr,
            ICabiRealloc realloc, bool sendBuffer)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            try
            {
                var socket = RequireTcpSocket(self);
                if (sendBuffer) socket.SetSendBufferSize(value);
                else socket.SetReceiveBufferSize(value);
            }
            catch (SocketsException ex) { caught = ex; }
            WriteSocketsErrorCodeResult(
                memory.AsSpan(retptr, ResultSocketsErrorCodeSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke
        /// <c>[method]tcp-socket.send(data: stream&lt;u8&gt;)</c>.
        /// Returns the i32 future-handle directly.</summary>
        public int InvokeTcpSocketSend(int self, int streamHandle)
        {
            var dispatcher = RequireDispatcher();
            var socket = RequireTcpSocket(self);
            var (futureHandle, _) = socket.Send(dispatcher, streamHandle);
            return futureHandle;
        }

        /// <summary>Invoke
        /// <c>[method]tcp-socket.listen()</c>. Returns
        /// <c>result&lt;stream&lt;tcp-socket&gt;,
        /// error-code&gt;</c> via the 20-byte 4-aligned retptr —
        /// on ok writes disc=0 + i32 stream-handle at +4; on
        /// err writes the standard sockets error-code variant.
        ///
        /// <para>The accept loop's bookkeeping is wired through
        /// <see cref="TcpSocket.ListenInternal"/>: the host
        /// passes itself in so accepted sockets land in
        /// <see cref="TcpSocketHandles"/>, and the i32 handles
        /// get serialized as 4 LE bytes per accepted socket
        /// into the stream's byte buffer. Guests reading the
        /// stream read 4 bytes per accepted socket and
        /// interpret as <c>own&lt;tcp-socket&gt;</c>.</para>
        /// </summary>
        public void InvokeTcpSocketListen(
            int self, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            int streamHandle = 0;
            try
            {
                var socket = RequireTcpSocket(self) as TcpSocket
                    ?? throw new SocketsException(
                        Sockets.ErrorCode.Other,
                        "tcp-socket.listen: socket impl is not a " +
                        "Wacs.WASI.Preview3.Sockets.TcpSocket; " +
                        "custom impls don't currently participate " +
                        "in the listen accept-loop pathway.");
                streamHandle = socket.ListenInternal(
                    RequireDispatcher(), host: this);
            }
            catch (SocketsException ex) { caught = ex; }

            var dest = memory.AsSpan(retptr, ResultSocketsErrorCodeSize);
            if (caught == null)
            {
                dest.Clear();
                dest[0] = 0;
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(4), streamHandle);
            }
            else
            {
                WriteSocketsErrorCodeResult(dest, caught, realloc, memory);
            }
        }

        /// <summary>Invoke
        /// <c>[method]tcp-socket.receive()</c>. Returns
        /// <c>tuple&lt;stream&lt;u8&gt;,
        /// future&lt;result&lt;_, error-code&gt;&gt;&gt;</c>
        /// via an 8-byte 4-aligned retptr (stream-handle at +0,
        /// future-handle at +4).</summary>
        public void InvokeTcpSocketReceive(int self, int retptr)
        {
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "tcp-socket.receive: dispatcher.Memory " +
                    "must be set before this import is invoked.");
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "tcp-socket.receive: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate an 8-byte 4-aligned " +
                    "return area.");

            var socket = RequireTcpSocket(self);
            var (streamHandle, futureHandle, _) =
                socket.Receive(dispatcher);
            var dest = memory.AsSpan(retptr, 8);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(0), streamHandle);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), futureHandle);
        }

        /// <summary>
        /// Decode an <see cref="IpSocketAddress"/> from the 12
        /// canon-ABI flat slots (1 disc + 11 joined payload):
        ///
        /// <list type="bullet">
        ///   <item>disc=0 (ipv4): s1=port, s2..s5=address octets;
        ///     s6..s11 are unused per the variant disc.</item>
        ///   <item>disc=1 (ipv6): s1=port, s2=flow-info,
        ///     s3..s10=address u16 groups, s11=scope-id.</item>
        /// </list>
        /// </summary>
        public static IpSocketAddress ReadIpSocketAddressFromSlots(
            int disc,
            int s1, int s2, int s3, int s4, int s5, int s6,
            int s7, int s8, int s9, int s10, int s11)
        {
            if (disc == 0)
            {
                return IpSocketAddress.Ipv4(new Ipv4SocketAddress(
                    port: (ushort)s1,
                    address: new Ipv4Address(
                        (byte)s2, (byte)s3, (byte)s4, (byte)s5)));
            }
            if (disc == 1)
            {
                return IpSocketAddress.Ipv6(new Ipv6SocketAddress(
                    port: (ushort)s1,
                    flowInfo: unchecked((uint)s2),
                    address: new Ipv6Address(
                        (ushort)s3, (ushort)s4, (ushort)s5, (ushort)s6,
                        (ushort)s7, (ushort)s8, (ushort)s9, (ushort)s10),
                    scopeId: unchecked((uint)s11)));
            }
            throw new SocketsException(
                Sockets.ErrorCode.InvalidArgument,
                $"ip-socket-address: invalid discriminant {disc} " +
                "(expected 0 for ipv4 or 1 for ipv6).");
        }

        // ---- bind / connect wire bodies (Slice T) -------------------

        /// <summary>Invoke
        /// <c>[method]tcp-socket.bind(local-address)</c>.</summary>
        public void InvokeTcpSocketBind(
            int self, IpSocketAddress localAddress,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            try { RequireTcpSocket(self).Bind(localAddress); }
            catch (SocketsException ex) { caught = ex; }
            WriteSocketsErrorCodeResult(
                memory.AsSpan(retptr, ResultSocketsErrorCodeSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke
        /// <c>[method]tcp-socket.connect(remote-address)</c>.
        /// Sync-blocking on the async impl (same pattern as
        /// client.send / handler.handle from Slice H).</summary>
        public void InvokeTcpSocketConnect(
            int self, IpSocketAddress remoteAddress,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            try
            {
                RequireTcpSocket(self).ConnectAsync(remoteAddress)
                    .GetAwaiter().GetResult();
            }
            catch (SocketsException ex) { caught = ex; }
            WriteSocketsErrorCodeResult(
                memory.AsSpan(retptr, ResultSocketsErrorCodeSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke
        /// <c>[method]udp-socket.bind(local-address)</c>.</summary>
        public void InvokeUdpSocketBind(
            int self, IpSocketAddress localAddress,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            try { RequireUdpSocket(self).Bind(localAddress); }
            catch (SocketsException ex) { caught = ex; }
            WriteSocketsErrorCodeResult(
                memory.AsSpan(retptr, ResultSocketsErrorCodeSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke
        /// <c>[method]udp-socket.connect(remote-address)</c>.
        /// UDP connect is synchronous (no handshake — sets the
        /// default peer for subsequent send/receive).</summary>
        public void InvokeUdpSocketConnect(
            int self, IpSocketAddress remoteAddress,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            try { RequireUdpSocket(self).Connect(remoteAddress); }
            catch (SocketsException ex) { caught = ex; }
            WriteSocketsErrorCodeResult(
                memory.AsSpan(retptr, ResultSocketsErrorCodeSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke
        /// <c>[method]tcp-socket.get-local-address()</c>.</summary>
        public void InvokeTcpSocketGetLocalAddress(
            int self, int retptr, ICabiRealloc realloc)
            => InvokeSocketGetAddress(self, retptr, realloc,
                handle => RequireTcpSocket(handle).GetLocalAddress());

        /// <summary>Invoke
        /// <c>[method]tcp-socket.get-remote-address()</c>.</summary>
        public void InvokeTcpSocketGetRemoteAddress(
            int self, int retptr, ICabiRealloc realloc)
            => InvokeSocketGetAddress(self, retptr, realloc,
                handle => RequireTcpSocket(handle).GetRemoteAddress());

        /// <summary>Invoke
        /// <c>[method]udp-socket.get-local-address()</c>.</summary>
        public void InvokeUdpSocketGetLocalAddress(
            int self, int retptr, ICabiRealloc realloc)
            => InvokeSocketGetAddress(self, retptr, realloc,
                handle => RequireUdpSocket(handle).GetLocalAddress());

        /// <summary>Invoke
        /// <c>[method]udp-socket.get-remote-address()</c>.</summary>
        public void InvokeUdpSocketGetRemoteAddress(
            int self, int retptr, ICabiRealloc realloc)
            => InvokeSocketGetAddress(self, retptr, realloc,
                handle => RequireUdpSocket(handle).GetRemoteAddress());

        // Shared dispatch for the four get-{local,remote}-address
        // host functions. The only per-method variation is the
        // impl call; the wire encoding is identical.
        private void InvokeSocketGetAddress(
            int self, int retptr, ICabiRealloc realloc,
            Func<int, IpSocketAddress> getter)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultIpSocketAddressRetptr(retptr, memory);
            SocketsException? caught = null;
            IpSocketAddress addr = default;
            try { addr = getter(self); }
            catch (SocketsException ex) { caught = ex; }
            WriteResultIpSocketAddress(
                memory.AsSpan(retptr, ResultIpSocketAddressSize),
                caught, addr, realloc, memory);
        }

        // ---- Slice X dispatch helpers ------------------------------
        //
        // Shared bodies for the TCP simple-getter cluster. Each
        // method-shape gets its own retptr-sized helper:
        //
        //   result<bool, error-code>: 20 bytes 4-aligned
        //     align = max(disc=1, max(bool=1, error-code=4)) = 4
        //     max_case = max(bool=1, error-code=16) = 16
        //     size = align_to(align_to(1, 4) + 16, 4) = 20
        //   result<u8,  error-code>: same 20-byte 4-aligned shape
        //   result<u32, error-code>: same 20-byte 4-aligned shape
        //     (u32 payload sits at +4)
        //   result<u64, error-code>: 24-byte 8-aligned (u64 forces
        //     align 8; implemented separately for buffer-size +
        //     keep-alive idle-time / interval)
        //
        // (Slice MM fix: the prior 12-byte size truncated the
        //  error-code variant's option<string> payload at the
        //  Other arm — simple err arms still worked since their
        //  payload is just the disc byte.)

        private const int ResultSmallErrorCodeSize = 20;

        private static void ValidateResultSmallErrorCodeRetptr(
            int retptr,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + ResultSmallErrorCodeSize > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:sockets/types: result<bool|u8|u32, " +
                    $"error-code> retptr 0x{retptr:X8} misaligned " +
                    $"or out of range (memory size = " +
                    $"{memory.Data.Length}). Caller must allocate " +
                    "a 20-byte 4-aligned return area.");
        }

        internal void InvokeTcpSocketGetterResultBool(
            int self, int retptr, ICabiRealloc realloc,
            Func<ITcpSocket, bool> getter)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSmallErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            bool value = false;
            try { value = getter(RequireTcpSocket(self)); }
            catch (SocketsException ex) { caught = ex; }
            var dest = memory.AsSpan(retptr, ResultSmallErrorCodeSize);
            dest.Clear();
            if (caught == null)
            {
                dest[0] = 0; // ok disc
                dest[4] = (byte)(value ? 1 : 0);
            }
            else
            {
                dest[0] = 1; // err disc
                WriteSocketsErrorCodeBytes(
                    dest.Slice(4, 16), caught, realloc, memory);
            }
        }

        internal void InvokeTcpSocketGetterResultU8(
            int self, int retptr, ICabiRealloc realloc,
            Func<ITcpSocket, byte> getter)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSmallErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            byte value = 0;
            try { value = getter(RequireTcpSocket(self)); }
            catch (SocketsException ex) { caught = ex; }
            var dest = memory.AsSpan(retptr, ResultSmallErrorCodeSize);
            dest.Clear();
            if (caught == null)
            {
                dest[0] = 0;
                dest[4] = value;
            }
            else
            {
                dest[0] = 1;
                WriteSocketsErrorCodeBytes(
                    dest.Slice(4, 16), caught, realloc, memory);
            }
        }

        internal void InvokeTcpSocketGetterResultU32(
            int self, int retptr, ICabiRealloc realloc,
            Func<ITcpSocket, uint> getter)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSmallErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            uint value = 0;
            try { value = getter(RequireTcpSocket(self)); }
            catch (SocketsException ex) { caught = ex; }
            var dest = memory.AsSpan(retptr, ResultSmallErrorCodeSize);
            dest.Clear();
            if (caught == null)
            {
                dest[0] = 0;
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt32LittleEndian(dest.Slice(4), value);
            }
            else
            {
                dest[0] = 1;
                WriteSocketsErrorCodeBytes(
                    dest.Slice(4, 16), caught, realloc, memory);
            }
        }

        // Shared u64-shape getter for keep-alive idle-time /
        // interval. Mirrors InvokeTcpSocketGetBufferSize's
        // 24-byte 8-aligned retptr layout exactly — only the
        // impl-call lambda varies per method.
        internal void InvokeTcpSocketGetterResultU64(
            int self, int retptr, ICabiRealloc realloc,
            Func<ITcpSocket, ulong> getter)
        {
            var memory = RequireMemoryForHttp();
            if (retptr < 0 || (retptr & 0x7) != 0
                || retptr + 24 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:sockets/types: result<u64, " +
                    $"error-code> retptr 0x{retptr:X8} " +
                    $"misaligned or out of range (memory " +
                    $"size = {memory.Data.Length}). Caller " +
                    "must allocate a 24-byte 8-aligned " +
                    "return area.");
            SocketsException? caught = null;
            ulong value = 0;
            try { value = getter(RequireTcpSocket(self)); }
            catch (SocketsException ex) { caught = ex; }
            var dest = memory.AsSpan(retptr, 24);
            dest.Clear();
            if (caught == null)
            {
                dest[0] = 0;
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt64LittleEndian(dest.Slice(8), value);
            }
            else
            {
                dest[0] = 1;
                WriteSocketsErrorCodeBytes(
                    dest.Slice(8, 16), caught, realloc, memory);
            }
        }

        // Shared setter dispatch for the methods that return only
        // result<_, error-code>. The lambda runs the actual impl
        // call; the wire layer captures SocketsException and writes
        // the 20-byte sockets-error-code retptr.
        internal void InvokeTcpSocketSetterErrorCode(
            int self, int retptr, ICabiRealloc realloc,
            Action<ITcpSocket> body)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            try { body(RequireTcpSocket(self)); }
            catch (SocketsException ex) { caught = ex; }
            WriteSocketsErrorCodeResult(
                memory.AsSpan(retptr, ResultSocketsErrorCodeSize),
                caught, realloc, memory);
        }

        internal void InvokeUdpSocketSetterErrorCode(
            int self, int retptr, ICabiRealloc realloc,
            Action<IUdpSocket> body)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultSocketsErrorCodeRetptr(retptr, memory);
            SocketsException? caught = null;
            try { body(RequireUdpSocket(self)); }
            catch (SocketsException ex) { caught = ex; }
            WriteSocketsErrorCodeResult(
                memory.AsSpan(retptr, ResultSocketsErrorCodeSize),
                caught, realloc, memory);
        }

        // ---- canon-ABI result<ip-socket-address, error-code> ---
        //
        // 36-byte 4-aligned retptr layout:
        //
        //   +0:   result-disc (u8) + 3-byte pad
        //   +4..36: payload section (32 bytes — sized to the
        //           ip-socket-address variant case, which
        //           dominates error-code's 16-byte variant)
        //
        // On ok the inner ip-socket-address variant lays out:
        //   +4:   ip-sock-addr disc (u8) + 3-byte pad
        //   +8..36: payload (28 bytes — ipv6 case; ipv4 uses
        //           only the first 6 bytes per its variant
        //           disc, with the trailing 22 bytes unused).
        //
        // ipv4-socket-address inner layout (6 bytes, align 2):
        //   +8..10: port (u16)
        //   +10..14: 4 address octets (u8 each)
        //
        // ipv6-socket-address inner layout (28 bytes, align 4):
        //   +8..10:  port (u16)
        //   +10..12: pad (to align-4 for flow-info)
        //   +12..16: flow-info (u32)
        //   +16..32: address (8×u16, big-endian groups)
        //   +32..36: scope-id (u32)

        private const int ResultIpSocketAddressSize = 36;

        private static void ValidateResultIpSocketAddressRetptr(
            int retptr, Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + ResultIpSocketAddressSize > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:sockets/types: result<ip-socket-address, " +
                    $"error-code> retptr 0x{retptr:X8} misaligned or " +
                    $"out of range (memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 36-byte 4-aligned " +
                    "return area.");
        }

        private static void WriteResultIpSocketAddress(
            Span<byte> dest, SocketsException? ex, IpSocketAddress addr,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest.Clear();
            if (ex != null)
            {
                dest[0] = 1; // result-disc = err
                WriteSocketsErrorCodeBytes(
                    dest.Slice(4, 16), ex, realloc, memory);
                return;
            }
            dest[0] = 0; // result-disc = ok
            // ip-sock-addr variant disc at +4.
            dest[4] = (byte)(addr.Family == IpAddressFamily.Ipv4 ? 0 : 1);
            if (addr.Family == IpAddressFamily.Ipv4)
            {
                // +8..10: port (u16 LE), +10..14: 4 octets.
                dest[8] = (byte)(addr.V4.Port & 0xFF);
                dest[9] = (byte)((addr.V4.Port >> 8) & 0xFF);
                dest[10] = addr.V4.Address.A;
                dest[11] = addr.V4.Address.B;
                dest[12] = addr.V4.Address.C;
                dest[13] = addr.V4.Address.D;
            }
            else
            {
                // +8..10: port, +12..16: flow-info,
                // +16..32: 8×u16 BE address, +32..36: scope-id.
                dest[8] = (byte)(addr.V6.Port & 0xFF);
                dest[9] = (byte)((addr.V6.Port >> 8) & 0xFF);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt32LittleEndian(
                        dest.Slice(12, 4), addr.V6.FlowInfo);
                var v6 = addr.V6.Address;
                ushort[] groups = new ushort[]
                {
                    v6.G0, v6.G1, v6.G2, v6.G3,
                    v6.G4, v6.G5, v6.G6, v6.G7,
                };
                for (int slot = 0; slot < 8; slot++)
                {
                    dest[16 + slot * 2 + 0] =
                        (byte)((groups[slot] >> 8) & 0xFF);
                    dest[16 + slot * 2 + 1] =
                        (byte)(groups[slot] & 0xFF);
                }
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt32LittleEndian(
                        dest.Slice(32, 4), addr.V6.ScopeId);
            }
        }

        internal ITcpSocket RequireTcpSocket(int handle)
        {
            var s = TcpSocketHandles.Get(handle);
            if (s == null)
                throw new SocketsException(
                    Sockets.ErrorCode.InvalidState,
                    $"wasi:sockets/types.tcp-socket: handle " +
                    $"{handle} is not allocated.");
            return s;
        }

        // ---- canon-ABI result<_, sockets.error-code> encoder --------

        private const int ResultSocketsErrorCodeSize = 20;

        private static void ValidateResultSocketsErrorCodeRetptr(
            int retptr, Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + ResultSocketsErrorCodeSize > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:sockets/types: result<_, error-code> " +
                    $"retptr 0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 20-byte 4-aligned " +
                    "return area.");
        }

        private static void WriteSocketsErrorCodeResult(
            Span<byte> dest, SocketsException? ex,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest.Clear();
            if (ex == null) { dest[0] = 0; return; }
            dest[0] = 1;
            WriteSocketsErrorCodeBytes(
                dest.Slice(4, 16), ex, realloc, memory);
        }

        // Encoder for `wasi:sockets/types.error-code` — the
        // 15-arm variant used by tcp-socket / udp-socket method
        // failures. The shared C# Sockets.ErrorCode enum
        // includes ip-name-lookup-only arms
        // (NameUnresolvable/etc.) — those throw at wire-time
        // since they're not representable in the types
        // variant.
        private static void WriteSocketsErrorCodeBytes(
            Span<byte> dest, SocketsException ex,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest.Clear();
            dest[0] = MapSocketsTypesErrorCodeDisc(ex.Code);
            if (ex.Code == Sockets.ErrorCode.Other
                && ex.OtherPayload != null)
            {
                WriteOtherPayloadString(
                    dest, ex.OtherPayload, realloc, memory);
            }
        }

        // Encoder for `wasi:sockets/ip-name-lookup.error-code` —
        // the 6-arm variant used by resolve-addresses failures.
        // Wire disc map: AccessDenied=0, InvalidArgument=1,
        // NameUnresolvable=2, TemporaryResolverFailure=3,
        // PermanentResolverFailure=4, Other(option<string>)=5.
        private static void WriteIpNameLookupErrorCodeBytes(
            Span<byte> dest, SocketsException ex,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest.Clear();
            dest[0] = MapIpNameLookupErrorCodeDisc(ex.Code);
            if (ex.Code == Sockets.ErrorCode.Other
                && ex.OtherPayload != null)
            {
                WriteOtherPayloadString(
                    dest, ex.OtherPayload, realloc, memory);
            }
        }

        // option<string> at +4..16: disc=1 at +4, ptr at +8, len
        // at +12. Shared between the two error-code writers.
        private static void WriteOtherPayloadString(
            Span<byte> dest, string payload,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest[4] = 1;
            var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
            int strPtr = bytes.Length > 0
                ? realloc.Allocate(1, bytes.Length) : 0;
            if (bytes.Length > 0)
                new ReadOnlySpan<byte>(bytes)
                    .CopyTo(memory.AsSpan(strPtr, bytes.Length));
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(8), strPtr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(12), bytes.Length);
        }

        /// <summary>Map the C# Sockets.ErrorCode enum to the
        /// wire discriminant for
        /// <c>wasi:sockets/types.error-code</c> (15 arms).
        /// Throws when the value is one of the ip-name-lookup-
        /// only arms (NameUnresolvable / Temporary /
        /// PermanentResolverFailure) — those aren't
        /// representable in this variant.</summary>
        public static byte MapSocketsTypesErrorCodeDisc(
            Sockets.ErrorCode code) => code switch
        {
            Sockets.ErrorCode.AccessDenied        => 0,
            Sockets.ErrorCode.NotSupported        => 1,
            Sockets.ErrorCode.InvalidArgument     => 2,
            Sockets.ErrorCode.OutOfMemory         => 3,
            Sockets.ErrorCode.Timeout             => 4,
            Sockets.ErrorCode.InvalidState        => 5,
            Sockets.ErrorCode.AddressNotBindable  => 6,
            Sockets.ErrorCode.AddressInUse        => 7,
            Sockets.ErrorCode.RemoteUnreachable   => 8,
            Sockets.ErrorCode.ConnectionRefused   => 9,
            Sockets.ErrorCode.ConnectionBroken    => 10,
            Sockets.ErrorCode.ConnectionReset     => 11,
            Sockets.ErrorCode.ConnectionAborted   => 12,
            Sockets.ErrorCode.DatagramTooLarge    => 13,
            Sockets.ErrorCode.Other               => 14,
            _ => throw new ArgumentException(
                $"wasi:sockets/types.error-code: " +
                $"{code} has no wire discriminant " +
                "(only valid in ip-name-lookup.error-code)."),
        };

        /// <summary>Map the C# Sockets.ErrorCode enum to the
        /// wire discriminant for
        /// <c>wasi:sockets/ip-name-lookup.error-code</c>
        /// (6 arms).</summary>
        public static byte MapIpNameLookupErrorCodeDisc(
            Sockets.ErrorCode code) => code switch
        {
            Sockets.ErrorCode.AccessDenied             => 0,
            Sockets.ErrorCode.InvalidArgument          => 1,
            Sockets.ErrorCode.NameUnresolvable         => 2,
            Sockets.ErrorCode.TemporaryResolverFailure => 3,
            Sockets.ErrorCode.PermanentResolverFailure => 4,
            Sockets.ErrorCode.Other                    => 5,
            _ => throw new ArgumentException(
                $"wasi:sockets/ip-name-lookup.error-code: " +
                $"{code} has no wire discriminant " +
                "(only valid in sockets/types.error-code)."),
        };

        // ---- wasi:sockets binding bodies (Slice J) -------------------

        /// <summary>Invoke
        /// <c>wasi:sockets/ip-name-lookup.resolve-addresses</c>'s
        /// body. Reads the hostname from guest memory, blocks
        /// on the Task&lt;IReadOnlyList&lt;IpAddress&gt;&gt;,
        /// allocates the canon-ABI-lowered variant list through
        /// cabi_realloc, writes
        /// <c>result&lt;list&lt;ip-address&gt;, error-code&gt;</c>
        /// through a 16-byte 4-aligned retptr.
        ///
        /// <para>Retptr layout:</para>
        /// <code>
        /// +0:    result-disc (u8) + 3 pad
        /// +4..16: payload section (12 bytes, sized to the
        ///         error-code variant arm which dominates the
        ///         8-byte list arm)
        ///   ok (list&lt;ip-address&gt;):
        ///     +4..8:  list-ptr (i32)
        ///     +8..12: list-len (i32)
        ///     +12..16: unused, zero
        ///   err (error-code variant):
        ///     +4..16: variant disc + payload (per
        ///             WriteSocketsErrorCodeBytes)
        /// </code>
        /// </summary>
        public void InvokeResolveAddresses(
            int namePtr, int nameLen, int retptr, ICabiRealloc realloc)
        {
            if (realloc == null) throw new ArgumentNullException(nameof(realloc));
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:sockets/ip-name-lookup.resolve-addresses: " +
                    "dispatcher.Memory must be set before this " +
                    "import is invoked.");
            // result<list<ip-address>, error-code>:
            //   align = max(disc=1, max(list=4, error-code=4)) = 4
            //   max_case = max(list=8, error-code=16) = 16
            //   size = align_to(align_to(1, 4) + 16, 4) = 20
            //
            // (Slice MM fix: prior 16-byte size truncated the
            //  error-code variant's option<string> payload at
            //  the Other arm.)
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 20 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:sockets/ip-name-lookup.resolve-addresses: " +
                    $"retptr 0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 20-byte 4-aligned " +
                    "return area.");

            // Read the hostname string.
            string name;
            if (nameLen == 0)
                name = string.Empty;
            else
                name = System.Text.Encoding.UTF8.GetString(
                    memory.AsSpan(namePtr, nameLen));

            // Resolve (sync-blocking). Catch SocketsException
            // and lower to the err variant; other exceptions
            // propagate as host-side bugs.
            SocketsException? caught = null;
            IReadOnlyList<IpAddress>? addresses = null;
            try
            {
                addresses = IpNameLookup
                    .ResolveAddressesAsync(name)
                    .GetAwaiter().GetResult();
            }
            catch (SocketsException ex) { caught = ex; }

            var dest = memory.AsSpan(retptr, 20);
            dest.Clear();
            if (caught != null)
            {
                dest[0] = 1; // result-disc = err
                // Slice KK: ip-name-lookup.error-code variant.
                // Slice MM: pass the full 16-byte variant span,
                // not a truncated 12 (the Other arm needs all
                // 16 bytes for its option<string> payload).
                WriteIpNameLookupErrorCodeBytes(
                    dest.Slice(4, 16), caught, realloc, memory);
                return;
            }

            dest[0] = 0; // result-disc = ok
            int count = addresses!.Count;

            int listPtr;
            if (count == 0)
            {
                listPtr = 0;
            }
            else
            {
                // ip-address wire entry: 18 bytes, 2-aligned.
                //   +0: disc (u8: 0=ipv4, 1=ipv6)
                //   +1: align pad
                //   +2..18: payload (max(ipv4 = 4, ipv6 = 16))
                const int entrySize = 18;
                listPtr = realloc.Allocate(
                    align: 2, size: count * entrySize);
                var listSpan = memory.AsSpan(
                    listPtr, count * entrySize);
                for (int i = 0; i < count; i++)
                {
                    int off = i * entrySize;
                    var addr = addresses[i];
                    // Zero the entire entry first to keep the
                    // pad bytes deterministic.
                    for (int j = 0; j < entrySize; j++)
                        listSpan[off + j] = 0;

                    if (addr.Family == IpAddressFamily.Ipv4)
                    {
                        listSpan[off + 0] = 0; // disc
                        listSpan[off + 2] = addr.V4.A;
                        listSpan[off + 3] = addr.V4.B;
                        listSpan[off + 4] = addr.V4.C;
                        listSpan[off + 5] = addr.V4.D;
                    }
                    else
                    {
                        listSpan[off + 0] = 1; // disc = ipv6
                        // payload at +2: 8 u16s, 16 bytes, BE
                        // (network byte order).
                        ushort[] groups = new ushort[]
                        {
                            addr.V6.G0, addr.V6.G1, addr.V6.G2, addr.V6.G3,
                            addr.V6.G4, addr.V6.G5, addr.V6.G6, addr.V6.G7,
                        };
                        for (int slot = 0; slot < 8; slot++)
                        {
                            listSpan[off + 2 + slot * 2 + 0] =
                                (byte)((groups[slot] >> 8) & 0xFF);
                            listSpan[off + 2 + slot * 2 + 1] =
                                (byte)(groups[slot] & 0xFF);
                        }
                    }
                }
            }

            // Outer (list-ptr, list-count) at +4..12.
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), listPtr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(8), count);
        }

        // ---- wasi:filesystem binding bodies (Slice I) ---------------

        /// <summary>Invoke
        /// <c>wasi:filesystem/preopens.get-directories</c>'s
        /// body. Allocates a descriptor handle per configured
        /// preopen, writes each path string via cabi_realloc,
        /// then allocates the outer tuple-list and writes its
        /// (ptr, count) at the retptr.</summary>
        public void InvokePreopensGetDirectories(int retptr, ICabiRealloc realloc)
        {
            if (realloc == null) throw new ArgumentNullException(nameof(realloc));
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:filesystem/preopens.get-directories: " +
                    "dispatcher.Memory must be set before this " +
                    "import is invoked.");
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:filesystem/preopens.get-directories: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate an 8-byte 4-aligned " +
                    "return area.");

            var preopens = Preopens.GetDirectories();
            int count = preopens.Count;
            if (count == 0)
            {
                // Empty list: (ptr=0, count=0) at retptr.
                var emptyDest = memory.AsSpan(retptr, 8);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(emptyDest.Slice(0), 0);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(emptyDest.Slice(4), 0);
                return;
            }

            // For each preopen, allocate a descriptor handle +
            // a guest-side path-string region. We accumulate the
            // (handle, str-ptr, str-len) triples in a host-side
            // staging array; the final array goes through one
            // cabi_realloc allocation.
            var triples = new (int Handle, int StrPtr, int StrLen)[count];
            for (int i = 0; i < count; i++)
            {
                var pre = preopens[i];
                int handle = DescriptorHandles.Allocate(pre.Descriptor);
                var pathBytes = System.Text.Encoding.UTF8.GetBytes(pre.Path);
                int strPtr = pathBytes.Length == 0
                    ? 0
                    : realloc.Allocate(align: 1, size: pathBytes.Length);
                if (pathBytes.Length > 0)
                    new ReadOnlySpan<byte>(pathBytes)
                        .CopyTo(memory.AsSpan(strPtr, pathBytes.Length));
                triples[i] = (handle, strPtr, pathBytes.Length);
            }

            // Tuple<descriptor, string> wire size: 12 bytes
            // (i32 handle + i32 ptr + i32 len). Tuple-list
            // allocation must be 4-aligned.
            int tupleArrayBytes = count * 12;
            int arrayPtr = realloc.Allocate(align: 4, size: tupleArrayBytes);
            var arraySpan = memory.AsSpan(arrayPtr, tupleArrayBytes);
            for (int i = 0; i < count; i++)
            {
                int off = i * 12;
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(
                        arraySpan.Slice(off + 0), triples[i].Handle);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(
                        arraySpan.Slice(off + 4), triples[i].StrPtr);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(
                        arraySpan.Slice(off + 8), triples[i].StrLen);
            }

            // Write the outer (list-ptr, list-count) at retptr.
            var outerDest = memory.AsSpan(retptr, 8);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(outerDest.Slice(0), arrayPtr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(outerDest.Slice(4), count);
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.read-via-stream(offset)</c>.
        /// Returns <c>tuple&lt;stream&lt;u8&gt;,
        /// future&lt;result&lt;_, error-code&gt;&gt;&gt;</c> via
        /// a 8-byte 4-aligned retptr (stream-handle at +0,
        /// future-handle at +4).</summary>
        public void InvokeDescriptorReadViaStream(
            int self, ulong offset, int retptr)
        {
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "descriptor.read-via-stream: dispatcher.Memory " +
                    "must be set before this import is invoked.");
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "descriptor.read-via-stream: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate an 8-byte 4-aligned " +
                    "return area.");

            var desc = RequireDescriptor(self);
            var (streamHandle, futureHandle, _) =
                desc.ReadViaStream(dispatcher, new FileSize(offset));

            var dest = memory.AsSpan(retptr, 8);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(0), streamHandle);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), futureHandle);
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.write-via-stream(data, offset)</c>.
        /// Returns the i32 future-handle directly (no retptr —
        /// single i32 return fits within MAX_FLAT_RESULTS).</summary>
        public int InvokeDescriptorWriteViaStream(
            int self, int streamHandle, ulong offset)
        {
            var dispatcher = RequireDispatcher();
            var desc = RequireDescriptor(self);
            var (futureHandle, _) = desc.WriteViaStream(
                dispatcher, streamHandle, new FileSize(offset));
            return futureHandle;
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.append-via-stream(data)</c>.</summary>
        public int InvokeDescriptorAppendViaStream(
            int self, int streamHandle)
        {
            var dispatcher = RequireDispatcher();
            var desc = RequireDescriptor(self);
            var (futureHandle, _) = desc.AppendViaStream(
                dispatcher, streamHandle);
            return futureHandle;
        }

        // Shared invocation for descriptor methods that take no
        // string-path arg (set-size, sync, sync-data, advise)
        // and return result<_, error-code>.
        private void InvokeDescriptorResultErrorCodeNoArgs(
            int self, int retptr, ICabiRealloc realloc,
            Action<IDescriptor> body)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultErrorCodeRetptr(retptr, memory);
            FilesystemException? caught = null;
            try { body(RequireDescriptor(self)); }
            catch (FilesystemException ex) { caught = ex; }
            WriteErrorCodeResult(
                memory.AsSpan(retptr, ResultErrorCodeSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.metadata-hash()</c> or
        /// <c>metadata-hash-at(path-flags, path)</c>. Returns
        /// <c>result&lt;metadata-hash-value, error-code&gt;</c>
        /// via a 24-byte 8-aligned retptr: disc at +0,
        /// metadata-hash-value (16 bytes: lower u64 + upper u64)
        /// at +8 on success, error-code at +8 on err.</summary>
        public void InvokeDescriptorMetadataHash(
            int self, int retptr, ICabiRealloc realloc,
            string? atPath, uint pathFlags)
        {
            var memory = RequireMemoryForHttp();
            // result<metadata-hash-value, error-code>:
            //   align = max(8 (u64), 4) = 8
            //   max_case = max(16, 16) = 16
            //   size = round_up(1 + 16, 8) = 24
            //   payload offset = 8
            if (retptr < 0 || (retptr & 0x7) != 0
                || retptr + 24 > memory.Data.Length)
                throw new InvalidOperationException(
                    "descriptor.metadata-hash: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 24-byte 8-aligned " +
                    "return area.");

            FilesystemException? caught = null;
            MetadataHashValue? hash = null;
            try
            {
                var desc = RequireDescriptor(self);
                hash = atPath != null
                    ? desc.MetadataHashAtAsync(
                        (PathFlags)pathFlags, atPath)
                        .GetAwaiter().GetResult()
                    : desc.MetadataHashAsync()
                        .GetAwaiter().GetResult();
            }
            catch (FilesystemException ex) { caught = ex; }

            var dest = memory.AsSpan(retptr, 24);
            dest.Clear();
            if (caught == null)
            {
                dest[0] = 0;
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt64LittleEndian(
                        dest.Slice(8, 8), hash!.Lower);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt64LittleEndian(
                        dest.Slice(16, 8), hash.Upper);
            }
            else
            {
                dest[0] = 1;
                WriteErrorCodeBytes(
                    dest.Slice(8, 16), caught, realloc, memory);
            }
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.readlink-at(path)</c>. Returns
        /// <c>result&lt;string, error-code&gt;</c> via a 20-byte
        /// 4-aligned retptr: disc at +0, string (ptr+len at +4)
        /// or error-code at +4.</summary>
        public void InvokeDescriptorReadlinkAt(
            int self, int pathPtr, int pathLen, int retptr,
            ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            // result<string, error-code>:
            //   align = max(4, 4) = 4
            //   max_case = max(8, 16) = 16
            //   size = round_up(1 + 16, 4) = 20
            //   payload offset = 4
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 20 > memory.Data.Length)
                throw new InvalidOperationException(
                    "descriptor.readlink-at: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}).");

            FilesystemException? caught = null;
            string? target = null;
            try
            {
                var desc = RequireDescriptor(self);
                var path = ReadGuestUtf8(pathPtr, pathLen);
                target = desc.ReadlinkAtAsync(path)
                    .GetAwaiter().GetResult();
            }
            catch (FilesystemException ex) { caught = ex; }

            var dest = memory.AsSpan(retptr, 20);
            dest.Clear();
            if (caught == null)
            {
                dest[0] = 0;
                var bytes = System.Text.Encoding.UTF8
                    .GetBytes(target ?? string.Empty);
                int strPtr = bytes.Length > 0
                    ? realloc.Allocate(1, bytes.Length) : 0;
                if (bytes.Length > 0)
                    new ReadOnlySpan<byte>(bytes)
                        .CopyTo(memory.AsSpan(strPtr, bytes.Length));
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(4), strPtr);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(8), bytes.Length);
            }
            else
            {
                dest[0] = 1;
                WriteErrorCodeBytes(
                    dest.Slice(4, 16), caught, realloc, memory);
            }
        }

        // ---- Variant-bearing descriptor methods (Slice W) ------------

        /// <summary>Decode a flat-lowered <c>new-timestamp</c>
        /// variant from 3 wire slots
        /// <c>(disc, seconds, nanoseconds)</c>. Variant
        /// flat-lowering rule: disc i32 + joined payload sized to
        /// the largest arm. <c>timestamp(instant)</c> is the only
        /// payload-bearing arm and instant flat-lowers to
        /// <c>(i64, i32)</c>; the <c>no-change</c> / <c>now</c>
        /// arms leave the payload slots unused.</summary>
        public static NewTimestamp ReadNewTimestampFromSlots(
            int disc, long seconds, int nanoseconds)
        {
            switch (disc)
            {
                case 0: return NewTimestamp.NoChange;
                case 1: return NewTimestamp.Now;
                case 2:
                    return NewTimestamp.Timestamp(
                        new Instant(seconds,
                            unchecked((uint)nanoseconds)));
                default:
                    throw new FilesystemException(
                        Filesystem.ErrorCode.Invalid,
                        $"new-timestamp: invalid discriminant " +
                        $"{disc} (expected 0/1/2 for " +
                        "no-change/now/timestamp).");
            }
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.set-times-at(path-flags, path,
        /// access, mod)</c>.</summary>
        public void InvokeDescriptorSetTimesAt(
            int self,
            uint pathFlags, int pathPtr, int pathLen,
            int aDisc, long aSecs, int aNanos,
            int mDisc, long mSecs, int mNanos,
            int retptr, ICabiRealloc realloc)
        {
            InvokeDescriptorResultErrorCodeNoArgs(
                self, retptr, realloc,
                desc => desc.SetTimesAtAsync(
                    (PathFlags)pathFlags,
                    ReadGuestUtf8(pathPtr, pathLen),
                    ReadNewTimestampFromSlots(aDisc, aSecs, aNanos),
                    ReadNewTimestampFromSlots(mDisc, mSecs, mNanos))
                    .GetAwaiter().GetResult());
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.link-at(old-path-flags, old-path,
        /// new-descriptor, new-path)</c>.</summary>
        public void InvokeDescriptorLinkAt(
            int self,
            uint oldPathFlags, int oldPathPtr, int oldPathLen,
            int newDescHandle, int newPathPtr, int newPathLen,
            int retptr, ICabiRealloc realloc)
        {
            InvokeDescriptorResultErrorCodeNoArgs(
                self, retptr, realloc,
                desc => desc.LinkAtAsync(
                    (PathFlags)oldPathFlags,
                    ReadGuestUtf8(oldPathPtr, oldPathLen),
                    RequireDescriptor(newDescHandle),
                    ReadGuestUtf8(newPathPtr, newPathLen))
                    .GetAwaiter().GetResult());
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.rename-at(old-path,
        /// new-descriptor, new-path)</c>.</summary>
        public void InvokeDescriptorRenameAt(
            int self,
            int oldPathPtr, int oldPathLen,
            int newDescHandle, int newPathPtr, int newPathLen,
            int retptr, ICabiRealloc realloc)
        {
            InvokeDescriptorResultErrorCodeNoArgs(
                self, retptr, realloc,
                desc => desc.RenameAtAsync(
                    ReadGuestUtf8(oldPathPtr, oldPathLen),
                    RequireDescriptor(newDescHandle),
                    ReadGuestUtf8(newPathPtr, newPathLen))
                    .GetAwaiter().GetResult());
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.symlink-at(old-path,
        /// new-path)</c>.</summary>
        public void InvokeDescriptorSymlinkAt(
            int self,
            int oldPathPtr, int oldPathLen,
            int newPathPtr, int newPathLen,
            int retptr, ICabiRealloc realloc)
        {
            InvokeDescriptorResultErrorCodeNoArgs(
                self, retptr, realloc,
                desc => desc.SymlinkAtAsync(
                    ReadGuestUtf8(oldPathPtr, oldPathLen),
                    ReadGuestUtf8(newPathPtr, newPathLen))
                    .GetAwaiter().GetResult());
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.read-directory()</c>. Returns
        /// <c>tuple&lt;stream&lt;directory-entry&gt;,
        /// future&lt;result&lt;_, error-code&gt;&gt;&gt;</c>
        /// via an 8-byte 4-aligned retptr (stream-handle at +0,
        /// future-handle at +4). The stream's bytes carry
        /// 24-byte directory-entry records; per-entry name
        /// strings are cabi_realloc-allocated at the addresses
        /// embedded in each record's name-ptr slot.</summary>
        public void InvokeDescriptorReadDirectory(
            int self, int retptr, ICabiRealloc realloc)
        {
            if (realloc == null) throw new ArgumentNullException(nameof(realloc));
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "descriptor.read-directory: dispatcher.Memory " +
                    "must be set before this import is invoked.");
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "descriptor.read-directory: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}).");

            var desc = RequireDescriptor(self);
            var (streamHandle, futureHandle, _) =
                desc.ReadDirectory(dispatcher, realloc);
            var dest = memory.AsSpan(retptr, 8);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(0), streamHandle);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), futureHandle);
        }

        private IDescriptor RequireDescriptor(int handle)
        {
            var desc = DescriptorHandles.Get(handle);
            if (desc == null)
                throw new FilesystemException(
                    Filesystem.ErrorCode.BadDescriptor,
                    $"wasi:filesystem/types.descriptor: handle " +
                    $"{handle} is not allocated.");
            return desc;
        }

        // ---- wasi:filesystem/types.descriptor method bodies ----------

        /// <summary>Invoke
        /// <c>[method]descriptor.create-directory-at(path)</c>.</summary>
        public void InvokeDescriptorCreateDirectoryAt(
            int self, int pathPtr, int pathLen, int retptr,
            ICabiRealloc realloc)
        {
            InvokeDescriptorResultErrorCode(self, retptr, realloc,
                (desc, path) =>
                    desc.CreateDirectoryAtAsync(path)
                        .GetAwaiter().GetResult(),
                pathPtr, pathLen);
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.unlink-file-at(path)</c>.</summary>
        public void InvokeDescriptorUnlinkFileAt(
            int self, int pathPtr, int pathLen, int retptr,
            ICabiRealloc realloc)
        {
            InvokeDescriptorResultErrorCode(self, retptr, realloc,
                (desc, path) =>
                    desc.UnlinkFileAtAsync(path)
                        .GetAwaiter().GetResult(),
                pathPtr, pathLen);
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.remove-directory-at(path)</c>.</summary>
        public void InvokeDescriptorRemoveDirectoryAt(
            int self, int pathPtr, int pathLen, int retptr,
            ICabiRealloc realloc)
        {
            InvokeDescriptorResultErrorCode(self, retptr, realloc,
                (desc, path) =>
                    desc.RemoveDirectoryAtAsync(path)
                        .GetAwaiter().GetResult(),
                pathPtr, pathLen);
        }

        // Shared invocation pattern for descriptor methods that
        // take a path arg and return result<_, error-code>.
        private void InvokeDescriptorResultErrorCode(
            int self, int retptr, ICabiRealloc realloc,
            Action<IDescriptor, string> body,
            int pathPtr, int pathLen)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultErrorCodeRetptr(retptr, memory);
            FilesystemException? caught = null;
            try
            {
                var desc = RequireDescriptor(self);
                var path = ReadGuestUtf8(pathPtr, pathLen);
                body(desc, path);
            }
            catch (FilesystemException ex) { caught = ex; }
            WriteErrorCodeResult(
                memory.AsSpan(retptr, ResultErrorCodeSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.is-same-object(other)</c>. Both
        /// args are descriptor handles; returns bool.</summary>
        public bool InvokeDescriptorIsSameObject(int self, int other)
        {
            var selfDesc = RequireDescriptor(self);
            var otherDesc = DescriptorHandles.Get(other);
            if (otherDesc == null) return false;
            return selfDesc.IsSameObjectAsync(otherDesc)
                .GetAwaiter().GetResult();
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.get-type() -&gt; descriptor-type</c>.
        /// Writes the variant at retptr (16 bytes 4-aligned).
        /// Unit cases: just write the tag at +0; the
        /// <see cref="DescriptorType.Other"/> case may carry an
        /// option&lt;string&gt; payload at +8.</summary>
        public void InvokeDescriptorGetType(
            int self, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateDescriptorTypeRetptr(retptr, memory);
            var desc = RequireDescriptor(self);
            var dtype = desc.GetType_();
            var dest = memory.AsSpan(retptr, 16);
            dest.Clear();
            dest[0] = (byte)dtype.Kind;
            if (dtype.Kind == DescriptorType.Tag.Other
                && dtype.OtherPayload != null)
            {
                dest[4] = 1; // option-disc = some
                var bytes = System.Text.Encoding.UTF8
                    .GetBytes(dtype.OtherPayload);
                int strPtr = bytes.Length > 0
                    ? realloc.Allocate(1, bytes.Length) : 0;
                if (bytes.Length > 0)
                    new ReadOnlySpan<byte>(bytes)
                        .CopyTo(memory.AsSpan(strPtr, bytes.Length));
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(8), strPtr);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(12), bytes.Length);
            }
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.open-at</c>. On success
        /// allocates a fresh descriptor handle bound to the
        /// returned IDescriptor; on err encodes the error-code
        /// at the retptr.</summary>
        public void InvokeDescriptorOpenAt(
            int self, uint pathFlagsBits, int pathPtr, int pathLen,
            uint openFlagsBits, uint flagsBits,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultErrorCodeRetptr(retptr, memory);
            FilesystemException? caught = null;
            int childHandle = 0;
            try
            {
                var parent = RequireDescriptor(self);
                var path = ReadGuestUtf8(pathPtr, pathLen);
                var child = parent.OpenAtAsync(
                    (PathFlags)pathFlagsBits, path,
                    (OpenFlags)openFlagsBits,
                    (DescriptorFlags)flagsBits)
                    .GetAwaiter().GetResult();
                childHandle = DescriptorHandles.Allocate(child);
            }
            catch (FilesystemException ex) { caught = ex; }

            var dest = memory.AsSpan(retptr, ResultErrorCodeSize);
            if (caught == null)
            {
                // ok: disc=0 + own<descriptor> at +4
                dest.Clear();
                dest[0] = 0;
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(4), childHandle);
            }
            else
            {
                WriteErrorCodeResult(dest, caught, realloc, memory);
            }
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.stat() -&gt;
        /// result&lt;descriptor-stat, error-code&gt;</c>. Writes
        /// the 104-byte descriptor-stat at retptr+8 on success
        /// or the error-code variant at retptr+8..24 on
        /// failure.</summary>
        public void InvokeDescriptorStat(
            int self, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateDescriptorStatRetptr(retptr, memory);

            FilesystemException? caught = null;
            DescriptorStat? stat = null;
            try
            {
                stat = RequireDescriptor(self).StatAsync()
                    .GetAwaiter().GetResult();
            }
            catch (FilesystemException ex) { caught = ex; }

            WriteResultDescriptorStat(
                memory.AsSpan(retptr, ResultDescriptorStatSize),
                caught, stat, realloc, memory);
        }

        /// <summary>Invoke
        /// <c>[method]descriptor.stat-at(path-flags, path) -&gt;
        /// result&lt;descriptor-stat, error-code&gt;</c>. Same
        /// retptr layout as stat — 112-byte 8-aligned. Routes
        /// the string path through ReadGuestUtf8.</summary>
        public void InvokeDescriptorStatAt(
            int self, uint pathFlags, int pathPtr, int pathLen,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateDescriptorStatRetptr(retptr, memory);

            FilesystemException? caught = null;
            DescriptorStat? stat = null;
            try
            {
                var path = ReadGuestUtf8(pathPtr, pathLen);
                stat = RequireDescriptor(self).StatAtAsync(
                    (PathFlags)pathFlags, path)
                    .GetAwaiter().GetResult();
            }
            catch (FilesystemException ex) { caught = ex; }

            WriteResultDescriptorStat(
                memory.AsSpan(retptr, ResultDescriptorStatSize),
                caught, stat, realloc, memory);
        }

        // result<descriptor-stat, error-code> wire layout
        // (112 bytes, align 8):
        //
        //   align = max(disc=1, max(stat=8, error-code=4)) = 8
        //   max_case = max(stat=104, error-code=16) = 104
        //   size = align-up(align-up(1, 8) + 104, 8) = 112
        //
        //   +0:    result-disc (u8) + 7 pad
        //   +8..112: payload section (104 bytes — sized to stat)
        //     ok:  descriptor-stat (104 bytes) at +8..112
        //     err: error-code variant (16 bytes) at +8..24,
        //          remaining 88 bytes unused / zero
        //
        // (Note: the prior version of this validator used 120
        //  bytes which is the size before subtracting the
        //  "pad to align-8" optimization — actually a bug. The
        //  spec-correct size is 112.)
        private const int ResultDescriptorStatSize = 112;

        private static void ValidateDescriptorStatRetptr(
            int retptr,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            if (retptr < 0 || (retptr & 0x7) != 0
                || retptr + ResultDescriptorStatSize > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:filesystem/types.descriptor.stat: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 112-byte 8-aligned " +
                    "return area.");
        }

        private void WriteResultDescriptorStat(
            Span<byte> dest, FilesystemException? ex,
            DescriptorStat? stat, ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest.Clear();
            if (ex != null)
            {
                dest[0] = 1;
                WriteErrorCodeBytes(
                    dest.Slice(8, 16), ex, realloc, memory);
                return;
            }
            dest[0] = 0;
            WriteDescriptorStat(
                dest.Slice(8, 104), stat!, realloc, memory);
        }

        // ---- canon-ABI result<_, error-code> encoder -----------------

        private const int ResultErrorCodeSize = 20;

        private static void ValidateResultErrorCodeRetptr(
            int retptr, Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + ResultErrorCodeSize > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:filesystem/types: result<_, error-code> " +
                    $"retptr 0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 20-byte 4-aligned " +
                    "return area.");
        }

        private static void ValidateDescriptorTypeRetptr(
            int retptr, Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 16 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:filesystem/types: descriptor-type retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 16-byte 4-aligned " +
                    "return area.");
        }

        // Write the result<_, error-code> at the supplied 20-byte
        // dest. null exception → disc=0 (ok); on err writes
        // disc=1 + the error-code variant.
        private static void WriteErrorCodeResult(
            Span<byte> dest, FilesystemException? ex,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest.Clear();
            if (ex == null) { dest[0] = 0; return; }
            dest[0] = 1; // result-disc = err
            // error-code variant lives at +4 (16 bytes).
            WriteErrorCodeBytes(dest.Slice(4, 16), ex, realloc, memory);
        }

        // Write the 16-byte error-code variant into `dest`. Used
        // by both result<_, error-code> and the stat retptr (which
        // embeds the error-code variant at +8 due to align-8).
        private static void WriteErrorCodeBytes(
            Span<byte> dest, FilesystemException ex,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest.Clear();
            dest[0] = (byte)ex.Code; // error-code-disc (0..36)
            if (ex.Code == Filesystem.ErrorCode.Other
                && ex.OtherPayload != null)
            {
                // option<string> at +4 (12 bytes).
                dest[4] = 1; // option-disc = some
                var bytes = System.Text.Encoding.UTF8
                    .GetBytes(ex.OtherPayload);
                int strPtr = bytes.Length > 0
                    ? realloc.Allocate(1, bytes.Length) : 0;
                if (bytes.Length > 0)
                    new ReadOnlySpan<byte>(bytes)
                        .CopyTo(memory.AsSpan(strPtr, bytes.Length));
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(8), strPtr);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(12), bytes.Length);
            }
        }

        // Write the 104-byte descriptor-stat record into `dest`.
        // Field offsets per canon-ABI record layout (computed in
        // declaration order with field-align-rounding):
        //   +0   type        (descriptor-type, 16 bytes align 4)
        //   +16  link-count  (u64, align 8 — already aligned)
        //   +24  size        (u64, align 8)
        //   +32  data-access-timestamp        (option<instant>, 24 bytes align 8)
        //   +56  data-modification-timestamp  (option<instant>, 24 bytes align 8)
        //   +80  status-change-timestamp      (option<instant>, 24 bytes align 8)
        private static void WriteDescriptorStat(
            Span<byte> dest, DescriptorStat stat,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest.Clear();
            // type: descriptor-type variant at +0..16.
            dest[0] = (byte)stat.Type.Kind;
            if (stat.Type.Kind == DescriptorType.Tag.Other
                && stat.Type.OtherPayload != null)
            {
                dest[4] = 1; // option-disc some
                var bytes = System.Text.Encoding.UTF8
                    .GetBytes(stat.Type.OtherPayload);
                int strPtr = bytes.Length > 0
                    ? realloc.Allocate(1, bytes.Length) : 0;
                if (bytes.Length > 0)
                    new ReadOnlySpan<byte>(bytes)
                        .CopyTo(memory.AsSpan(strPtr, bytes.Length));
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(8), strPtr);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(12), bytes.Length);
            }
            // link-count + size: plain u64s.
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt64LittleEndian(dest.Slice(16), stat.LinkCount.Value);
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt64LittleEndian(dest.Slice(24), stat.Size.Value);
            // 3× option<instant> (24 bytes each: i32 disc + 4 pad + s64 seconds + u32 nanoseconds + 4 pad).
            WriteOptionInstant(dest.Slice(32, 24), stat.DataAccessTimestamp);
            WriteOptionInstant(dest.Slice(56, 24), stat.DataModificationTimestamp);
            WriteOptionInstant(dest.Slice(80, 24), stat.StatusChangeTimestamp);
        }

        // option<instant> wire layout (24 bytes 8-aligned):
        //   +0   option-disc (u8) + 7-byte pad (align-8 from instant)
        //   +8   instant.seconds (s64)
        //   +16  instant.nanoseconds (u32) + 4-byte tail pad
        private static void WriteOptionInstant(
            Span<byte> dest, Clocks.Instant? value)
        {
            dest.Clear();
            if (!value.HasValue) { dest[0] = 0; return; }
            dest[0] = 1; // option-disc = some
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt64LittleEndian(dest.Slice(8), value.Value.Seconds);
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt32LittleEndian(dest.Slice(16), value.Value.Nanoseconds);
        }

        // ---- wasi:http/client + handler binding bodies ---------------

        /// <summary>Invoke <c>wasi:http/client.send</c>'s body.
        /// Resolves the request handle, calls
        /// <see cref="IClient.SendAsync(IRequest, System.Threading.CancellationToken)"/>
        /// sync-blocking, writes a 32-byte 4-aligned
        /// <c>result&lt;response, error-code&gt;</c> retptr.
        /// HttpException is caught and lowered to the err
        /// variant; other exceptions propagate.</summary>
        public void InvokeClientSend(
            int requestHandle, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultResponseErrorCodeRetptr(retptr, memory);

            HttpException? caught = null;
            IResponse? response = null;
            IRequest? request = null;
            try
            {
                request = RequireRequest(requestHandle);
                response = HttpClient.SendAsync(request)
                    .GetAwaiter().GetResult();
            }
            catch (HttpException ex) { caught = ex; }

            // Resolve the request's completion future (allocated
            // by request.new). On success: write null (ok arm
            // of result<_, error-code>). On err: cancel the
            // FutureCell so guests awaiting it observe the
            // failure.
            ResolveRequestCompletionFuture(request, caught);

            WriteResultResponseErrorCode(
                memory.AsSpan(retptr, ResultResponseErrorCodeSize),
                caught, response, realloc, memory);
        }

        /// <summary>Invoke <c>wasi:http/handler.handle</c>'s body.
        /// Routes the request to the configured
        /// <see cref="IHandler"/>; throws
        /// <see cref="HttpException"/> with
        /// <see cref="HttpErrorCode.ConfigurationError"/> when
        /// no handler is configured — guests importing
        /// <c>wasi:http/handler</c> need an embedder-provided
        /// inbound handler. Writes a 32-byte
        /// <c>result&lt;response, error-code&gt;</c> retptr.</summary>
        public void InvokeHandlerHandle(
            int requestHandle, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultResponseErrorCodeRetptr(retptr, memory);

            HttpException? caught = null;
            IResponse? response = null;
            IRequest? request = null;
            try
            {
                var handler = HttpHandler
                    ?? throw new HttpException(
                        HttpErrorCode.ConfigurationError,
                        "wasi:http/handler.handle: no IHandler " +
                        "configured. Set " +
                        "WasiPreview3HostBuilder.HttpHandler.");
                request = RequireRequest(requestHandle);
                response = handler.HandleAsync(request)
                    .GetAwaiter().GetResult();
            }
            catch (HttpException ex) { caught = ex; }

            ResolveRequestCompletionFuture(request, caught);

            WriteResultResponseErrorCode(
                memory.AsSpan(retptr, ResultResponseErrorCodeSize),
                caught, response, realloc, memory);
        }

        /// <summary>Resolve the completion future associated
        /// with <paramref name="request"/> if request.new
        /// allocated one for it. On success (ex == null) writes
        /// null to the future cell — the canonical "ok arm of
        /// result&lt;_, error-code&gt;" payload. On failure
        /// cancels the cell so guests awaiting it surface the
        /// failure as TaskCanceledException.</summary>
        private void ResolveRequestCompletionFuture(
            IRequest? request, HttpException? ex)
        {
            if (request == null) return;
            if (!_requestCompletionFutures.TryGetValue(
                    request, out int futureHandle))
                return;
            _requestCompletionFutures.Remove(request);
            var dispatcher = RequireDispatcher();
            if (ex == null)
            {
                dispatcher.FutureWrite(futureHandle, null);
            }
            else
            {
                if (dispatcher.Futures.Get(futureHandle) is
                    Wacs.ComponentModel.Async.FutureCell<object?> cell)
                    cell.TrySetCanceled();
            }
        }

        // ---- result<response, error-code> encoder (Slice GG/HH) -----
        //
        // 40-byte 8-aligned retptr (HH-corrected layout):
        //
        //   +0:   result-disc (u8) + 7 pad
        //   +8..40: payload section (32 bytes — sized to the
        //           error-code variant which dominates the
        //           4-byte response handle)
        //
        //   ok:
        //     +8..12: response handle (i32)
        //     +12..40: unused, zero
        //
        //   err (http error-code variant, 32 bytes, align 8):
        //     +8:    error-code disc (u8) + 7 pad
        //     +16..40: payload section (24 bytes — sized to the
        //             dominant arm, option<field-size-payload>)
        //
        // The 8-alignment is forced by option<u64> in the body-size
        // arms (HttpRequestBodySize, HttpResponseBodySize) — u64
        // align = 8 → option<u64> align = 8 → error-code variant
        // align = 8 → result variant align = 8.
        //
        // Slice GG shipped the simple (no-payload) arms with disc
        // at the (incorrect) +4 offset; Slice HH corrects the
        // 8-alignment and adds typed-payload encoders for the
        // arms HttpException carries:
        //   DnsError(option<string>)
        //   TlsAlertReceived(option<u8>, option<string>)
        //   HttpRequestBodySize / HttpResponseBodySize (option<u64>)
        //   *HeaderSectionSize / *TrailerSectionSize (option<u32>)
        //   *HeaderSize / *TrailerSize (option<field-size-payload>
        //     or field-size-payload — encoded uniformly through
        //     the HttpException.{FieldName, FieldSize} fields)
        //   HttpResponseTransferCoding / *ContentCoding (option<string>)
        //   InternalError (option<string>)

        private const int ResultResponseErrorCodeSize = 40;

        private static void ValidateResultResponseErrorCodeRetptr(
            int retptr,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            if (retptr < 0 || (retptr & 0x7) != 0
                || retptr + ResultResponseErrorCodeSize > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:http/client.send / handler.handle: " +
                    $"retptr 0x{retptr:X8} misaligned or out of " +
                    "range (needs 40 bytes 8-aligned).");
        }

        private void WriteResultResponseErrorCode(
            Span<byte> dest, HttpException? ex, IResponse? response,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest.Clear();
            if (ex == null)
            {
                if (response == null)
                    throw new InvalidOperationException(
                        "WriteResultResponseErrorCode: ok path " +
                        "requires a non-null IResponse.");
                dest[0] = 0; // result-disc = ok
                int handle = ResponseHandles.Allocate(response);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(8), handle);
                return;
            }
            dest[0] = 1; // result-disc = err
            dest[8] = (byte)ex.Code; // error-code variant disc

            // Typed-payload arms write into +16..40 (the 24-byte
            // payload section of the error-code variant, sized
            // to the dominant arm option<field-size-payload>).
            WriteHttpErrorCodeTypedPayload(
                dest.Slice(16, 24), ex, realloc, memory);
        }

        // ---- Slice HH: typed-payload encoder ------------------------

        private static void WriteOptionString(
            Span<byte> dest, string? value,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            // option<string> wire: 12 bytes, align 4.
            //   +0:    opt-disc (u8) + 3 pad
            //   +4..8: ptr (i32; 0 when none)
            //   +8..12: len (i32; 0 when none)
            dest.Slice(0, 12).Clear();
            if (value == null) return;
            dest[0] = 1;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            int strPtr = bytes.Length > 0
                ? realloc.Allocate(1, bytes.Length) : 0;
            if (bytes.Length > 0)
                new ReadOnlySpan<byte>(bytes)
                    .CopyTo(memory.AsSpan(strPtr, bytes.Length));
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), strPtr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(8), bytes.Length);
        }

        private static void WriteOptionU8(
            Span<byte> dest, byte? value)
        {
            // option<u8> wire: 2 bytes, align 1.
            //   +0: opt-disc (u8)
            //   +1: u8 value (when some)
            dest.Slice(0, 2).Clear();
            if (value == null) return;
            dest[0] = 1;
            dest[1] = value.Value;
        }

        private static void WriteOptionU16(
            Span<byte> dest, ushort? value)
        {
            // option<u16> wire: 4 bytes, align 2.
            //   +0:    opt-disc (u8) + 1 pad
            //   +2..4: u16 value (when some, LE)
            dest.Slice(0, 4).Clear();
            if (value == null) return;
            dest[0] = 1;
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt16LittleEndian(dest.Slice(2, 2), value.Value);
        }

        private static void WriteOptionU32(
            Span<byte> dest, uint? value)
        {
            // option<u32> wire: 8 bytes, align 4.
            //   +0:    opt-disc (u8) + 3 pad
            //   +4..8: u32 value (when some, LE)
            dest.Slice(0, 8).Clear();
            if (value == null) return;
            dest[0] = 1;
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt32LittleEndian(dest.Slice(4, 4), value.Value);
        }

        private static void WriteOptionU64(
            Span<byte> dest, ulong? value)
        {
            // option<u64> wire: 16 bytes, align 8.
            //   +0:    opt-disc (u8) + 7 pad
            //   +8..16: u64 value (when some, LE)
            dest.Slice(0, 16).Clear();
            if (value == null) return;
            dest[0] = 1;
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt64LittleEndian(dest.Slice(8, 8), value.Value);
        }

        private static void WriteFieldSizePayload(
            Span<byte> dest, string? fieldName, uint? fieldSize,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            // field-size-payload record: 20 bytes, align 4.
            //   +0..12:  option<string> field-name
            //   +12..20: option<u32> field-size
            dest.Slice(0, 20).Clear();
            WriteOptionString(dest.Slice(0, 12), fieldName, realloc, memory);
            WriteOptionU32(dest.Slice(12, 8), fieldSize);
        }

        private static void WriteOptionFieldSizePayload(
            Span<byte> dest, string? fieldName, uint? fieldSize,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            // option<field-size-payload> wire: 24 bytes, align 4.
            //   +0:    opt-disc (u8) + 3 pad
            //   +4..24: field-size-payload (20 bytes)
            dest.Slice(0, 24).Clear();
            if (fieldName == null && fieldSize == null)
            {
                // none — represent absence as a literal none. The
                // HttpException with neither field set is rare
                // but possible; treat as a missing payload.
                return;
            }
            dest[0] = 1;
            WriteFieldSizePayload(
                dest.Slice(4, 20), fieldName, fieldSize, realloc, memory);
        }

        // Write the typed payload (if any) for an
        // HttpException's error-code arm into a 24-byte span
        // starting at the variant's payload offset (+16 within
        // the result retptr, = +8 within the error-code variant).
        // The span is already cleared; this method only writes
        // the payload bytes for arms that carry one.
        private static void WriteHttpErrorCodeTypedPayload(
            Span<byte> dest, HttpException ex,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            switch (ex.Code)
            {
                case HttpErrorCode.DnsError:
                    // DNS-error-payload record at +0:
                    //   +0..12:  option<string> rcode
                    //   +12..16: option<u16> info-code (the impl
                    //            doesn't store info-code today —
                    //            always none)
                    WriteOptionString(dest.Slice(0, 12),
                        ex.DnsRcode, realloc, memory);
                    WriteOptionU16(dest.Slice(12, 4), null);
                    break;

                case HttpErrorCode.TlsAlertReceived:
                    // TLS-alert-received-payload record at +0:
                    //   +0..2:  option<u8> alert-id
                    //   +2..4:  pad to align-4 for next option<string>
                    //   +4..16: option<string> alert-message
                    WriteOptionU8(dest.Slice(0, 2), ex.TlsAlertId);
                    WriteOptionString(dest.Slice(4, 12),
                        ex.TlsAlertMessage, realloc, memory);
                    break;

                case HttpErrorCode.HttpRequestBodySize:
                case HttpErrorCode.HttpResponseBodySize:
                    WriteOptionU64(dest.Slice(0, 16), ex.BodySize);
                    break;

                case HttpErrorCode.HttpRequestHeaderSectionSize:
                case HttpErrorCode.HttpRequestTrailerSectionSize:
                case HttpErrorCode.HttpResponseHeaderSectionSize:
                case HttpErrorCode.HttpResponseTrailerSectionSize:
                    WriteOptionU32(dest.Slice(0, 8), ex.FieldSize);
                    break;

                case HttpErrorCode.HttpRequestHeaderSize:
                    // option<field-size-payload>
                    WriteOptionFieldSizePayload(
                        dest.Slice(0, 24), ex.FieldName,
                        ex.FieldSize, realloc, memory);
                    break;

                case HttpErrorCode.HttpRequestTrailerSize:
                case HttpErrorCode.HttpResponseHeaderSize:
                case HttpErrorCode.HttpResponseTrailerSize:
                    // bare field-size-payload (not option-wrapped)
                    WriteFieldSizePayload(
                        dest.Slice(0, 20), ex.FieldName,
                        ex.FieldSize, realloc, memory);
                    break;

                case HttpErrorCode.HttpResponseTransferCoding:
                case HttpErrorCode.HttpResponseContentCoding:
                case HttpErrorCode.InternalError:
                    WriteOptionString(dest.Slice(0, 12),
                        ex.OtherPayload, realloc, memory);
                    break;

                // All other arms: no typed payload, span stays
                // zero from the caller's Clear().
            }
        }

        private IRequest RequireRequest(int handle)
        {
            var req = RequestHandles.Get(handle);
            if (req == null)
                throw new HttpException(
                    HttpErrorCode.InternalError,
                    $"wasi:http/types.request: handle {handle} " +
                    "is not allocated.");
            return req;
        }

        // ---- request.new / response.new (Slice II) -----------------

        /// <summary>Invoke <c>[static]request.new(headers,
        /// contents, trailers, options)</c>. Wraps the optional
        /// contents stream-handle into a
        /// <see cref="StreamBufferBackedStream"/> when the
        /// option is some, then constructs the Request impl
        /// with that Stream as its Body. Allocates the request
        /// handle and a fresh completion-future handle.
        ///
        /// <para>Retptr layout (8 bytes 4-aligned):</para>
        /// <code>
        /// +0..4: request-handle (i32)
        /// +4..8: completion-future-handle (i32)
        /// </code>
        /// </summary>
        internal void InvokeRequestNew(
            int headersHandle,
            int contentsOptDisc, int contentsStreamHandle,
            int trailersFutureHandle,
            int optsOptDisc, int optsHandle,
            int retptr)
        {
            var memory = RequireMemoryForHttp();
            ValidateRequestResponseNewRetptr(retptr, memory,
                "wasi:http/types.request.new");

            var fields = RequireFields(headersHandle);
            IRequestOptions? opts = null;
            if (optsOptDisc != 0)
                opts = RequireRequestOptions(optsHandle);

            // Slice NN: wrap the contents stream-handle (when
            // some) as a System.IO.Stream the impl can read.
            // Trailers future-handle is retained for a later
            // slice that resolves IFields trailers from the
            // future's value.
            Stream? body = ResolveStreamFromHandle(
                contentsOptDisc, contentsStreamHandle);

            var request = new Request(fields,
                body: body, trailers: null, options: opts);
            int requestHandle = RequestHandles.Allocate(request);

            // Allocate a fresh completion future and register
            // the request → future-handle mapping so
            // client.send / handler.handle can resolve it after
            // the underlying SendAsync / HandleAsync settles.
            int completionFuture = RequireDispatcher()
                .FutureNew(typeIdx: 0);
            _requestCompletionFutures[request] = completionFuture;

            var dest = memory.AsSpan(retptr, 8);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(0), requestHandle);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), completionFuture);
        }

        /// <summary>Invoke <c>[static]response.new(headers,
        /// contents, trailers)</c>. Same Body-bridging behavior
        /// as request.new — wraps the contents stream-handle
        /// into a <see cref="StreamBufferBackedStream"/> for
        /// the response impl's Body.</summary>
        internal void InvokeResponseNew(
            int headersHandle,
            int contentsOptDisc, int contentsStreamHandle,
            int trailersFutureHandle,
            int retptr)
        {
            var memory = RequireMemoryForHttp();
            ValidateRequestResponseNewRetptr(retptr, memory,
                "wasi:http/types.response.new");

            var fields = RequireFields(headersHandle);
            Stream? body = ResolveStreamFromHandle(
                contentsOptDisc, contentsStreamHandle);
            var response = new Response(fields,
                body: body, trailers: null);
            int responseHandle = ResponseHandles.Allocate(response);

            int completionFuture = RequireDispatcher()
                .FutureNew(typeIdx: 0);
            _responseCompletionFutures[response] = completionFuture;

            var dest = memory.AsSpan(retptr, 8);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(0), responseHandle);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), completionFuture);
        }

        /// <summary>Resolve a flat-lowered
        /// <c>option&lt;stream&lt;u8&gt;&gt;</c> arg into a
        /// host-side Stream — returns null on the none arm or
        /// when the handle doesn't resolve to a byte stream
        /// (the dispatcher returns null in that case).</summary>
        private Stream? ResolveStreamFromHandle(
            int optDisc, int streamHandle)
        {
            if (optDisc == 0) return null;
            var buffer = RequireDispatcher()
                .GetByteStreamBuffer(streamHandle);
            if (buffer == null) return null;
            return new StreamBufferBackedStream(buffer);
        }

        private static void ValidateRequestResponseNewRetptr(
            int retptr,
            Wacs.Core.Runtime.Types.MemoryInstance memory,
            string label)
        {
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    $"{label}: retptr 0x{retptr:X8} misaligned " +
                    "or out of range (needs 8 bytes 4-aligned).");
        }

        // ---- consume-body (Slice JJ) --------------------------------

        /// <summary>Invoke
        /// <c>[static]request.consume-body(this, res)</c>.
        /// Allocates a stream&lt;u8&gt; and a trailers future,
        /// writes the tuple at retptr. When the request has a
        /// non-null Body Stream, spawns a background pump that
        /// reads from Body and writes into the new
        /// StreamBuffer (closing it on EOF). When the pump
        /// finishes the trailers future is resolved with
        /// <c>IRequest.Trailers</c> (or null for none).</summary>
        internal void InvokeRequestConsumeBody(
            int thisHandle, int resFutureHandle, int retptr)
        {
            var memory = RequireMemoryForHttp();
            ValidateRequestResponseNewRetptr(retptr, memory,
                "wasi:http/types.request.consume-body");

            // Validate the request handle is allocated; the
            // `this` arg is consumed by the canon-ABI (owned
            // borrow), but v0 doesn't drop it here since the
            // resource-drop binding handles ownership.
            var req = RequireRequest(thisHandle);
            WriteConsumeBodyHandles(
                memory.AsSpan(retptr, 8),
                req.Body, req.Trailers);
        }

        /// <summary>Invoke
        /// <c>[static]response.consume-body(this, res)</c>.
        /// Same shape as request.consume-body — pumps the
        /// response's Body into the returned StreamBuffer when
        /// non-null and resolves the trailers future on
        /// completion.</summary>
        internal void InvokeResponseConsumeBody(
            int thisHandle, int resFutureHandle, int retptr)
        {
            var memory = RequireMemoryForHttp();
            ValidateRequestResponseNewRetptr(retptr, memory,
                "wasi:http/types.response.consume-body");

            var resp = RequireResponse(thisHandle);
            WriteConsumeBodyHandles(
                memory.AsSpan(retptr, 8),
                resp.Body, resp.Trailers);
        }

        // Allocate the body stream + trailers future, write them
        // as a tuple at offset 0 of `dest` (8 bytes). When
        // `bodySource` is non-null, spawns a background pump
        // task that reads bytes from the Stream and writes them
        // into the StreamBuffer until EOF, then completes it
        // and resolves the trailers future with the impl's
        // trailers IFields (or null for the none arm). On
        // pump exception the trailers future faults — guest
        // readers observe the error rather than a "got trailers
        // = none" success.
        private void WriteConsumeBodyHandles(
            Span<byte> dest, System.IO.Stream? bodySource,
            IFields? trailers)
        {
            var dispatcher = RequireDispatcher();
            int streamHandle = dispatcher.StreamNew(typeIdx: 0);
            int trailersFuture = dispatcher.FutureNew(typeIdx: 0);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(0), streamHandle);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), trailersFuture);

            if (bodySource != null)
            {
                var buffer = dispatcher.GetByteStreamBuffer(streamHandle);
                if (buffer != null)
                    _ = PumpBodyAndResolveTrailersAsync(
                        bodySource, buffer,
                        dispatcher, trailersFuture, trailers);
                else
                    // Stream slot vanished out from under us;
                    // close the trailers future cleanly.
                    dispatcher.FutureWrite(trailersFuture, trailers);
            }
            else
            {
                // No body — close the buffer immediately so the
                // guest reading the stream observes EOF on the
                // first read, and resolve the trailers future
                // synchronously since there's no pump to wait
                // for.
                dispatcher.GetByteStreamBuffer(streamHandle)?.Complete();
                dispatcher.FutureWrite(trailersFuture, trailers);
            }
        }

        /// <summary>Pump bytes from <paramref name="source"/>
        /// into <paramref name="dest"/> until source EOF, then
        /// Complete() the buffer and resolve the trailers
        /// future with <paramref name="trailers"/>. Runs as a
        /// fire-and-forget background task — the caller
        /// doesn't await it.
        ///
        /// <para>Exception handling: a body read failure leaves
        /// already-drained bytes visible to the guest, completes
        /// the buffer to signal EOF, and faults the trailers
        /// future via TryCancel so a guest awaiting the trailers
        /// observes the failure rather than a "none" trailers
        /// arm.</para>
        /// </summary>
        private static async System.Threading.Tasks.Task
            PumpBodyAndResolveTrailersAsync(
                System.IO.Stream source,
                Wacs.ComponentModel.Async.StreamBuffer<byte> dest,
                Wacs.ComponentModel.Async.AsyncDispatcher dispatcher,
                int trailersFuture, IFields? trailers)
        {
            bool faulted = false;
            try
            {
                byte[] buf = new byte[4096];
                while (true)
                {
                    int n = await source.ReadAsync(buf, 0, buf.Length)
                        .ConfigureAwait(false);
                    if (n <= 0) break;
                    for (int i = 0; i < n; i++)
                        await dest.WriteAsync(buf[i])
                            .ConfigureAwait(false);
                }
            }
            catch
            {
                faulted = true;
            }
            finally
            {
                dest.Complete();
                if (faulted)
                {
                    // Cancel the future without dropping the
                    // handle so a guest already awaiting it
                    // observes the cancellation; FutureDropReadable
                    // would remove the handle from the table and
                    // turn subsequent reads into "handle not
                    // allocated" instead of "future was cancelled".
                    if (dispatcher.Futures.Get(trailersFuture) is
                        Wacs.ComponentModel.Async.FutureCell<object?> cell)
                        cell.TrySetCanceled();
                }
                else
                {
                    dispatcher.FutureWrite(trailersFuture, trailers);
                }
            }
        }

        // ---- request.get-options (Slice EE) -------------------------

        /// <summary>Invoke
        /// <c>[method]request.get-options() -&gt;
        /// option&lt;request-options&gt;</c>. Writes an 8-byte
        /// 4-aligned retptr: option-disc at +0, handle at +4
        /// (zero when the request has no options).</summary>
        internal void InvokeRequestGetOptions(int self, int retptr)
        {
            var memory = RequireMemoryForHttp();
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:http/types.request.get-options: " +
                    $"retptr 0x{retptr:X8} misaligned or out of " +
                    "range (needs 8 bytes 4-aligned).");

            var dest = memory.AsSpan(retptr, 8);
            dest.Clear();
            var opts = RequireRequest(self).GetOptions();
            if (opts == null)
            {
                // option-disc = none; handle stays zero.
                return;
            }
            dest[0] = 1; // option-disc = some
            int handle = RequestOptionsHandles.Allocate(opts);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), handle);
        }

        // ---- request method + scheme (Slice CC) ---------------------

        // Tag mapping per the WIT variant order:
        //   method: get=0, head=1, post=2, put=3, delete=4,
        //           connect=5, options=6, trace=7, patch=8,
        //           other(string)=9
        //   scheme: HTTP=0, HTTPS=1, other(string)=2

        internal void InvokeRequestGetMethod(
            int self, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateMethodOrSchemeRetptr(retptr, memory,
                "wasi:http/types.request.get-method", 12);

            var method = RequireRequest(self).GetMethod();
            var dest = memory.AsSpan(retptr, 12);
            dest.Clear();
            WriteMethodVariantBytes(dest, method, realloc, memory);
        }

        internal int InvokeRequestSetMethod(
            int self, int disc, int strPtr, int strLen)
        {
            HttpMethod m = ReadMethodFromSlots(disc, strPtr, strLen);
            RequireRequest(self).SetMethod(m);
            return 0; // result = ok
        }

        public HttpMethod ReadMethodFromSlots(
            int disc, int strPtr, int strLen)
        {
            switch (disc)
            {
                case 0: return HttpMethod.Get;
                case 1: return HttpMethod.Head;
                case 2: return HttpMethod.Post;
                case 3: return HttpMethod.Put;
                case 4: return HttpMethod.Delete;
                case 5: return HttpMethod.Connect;
                case 6: return HttpMethod.Options_;
                case 7: return HttpMethod.Trace;
                case 8: return HttpMethod.Patch;
                case 9: return HttpMethod.Other(
                    ReadGuestUtf8(strPtr, strLen));
                default:
                    throw new HttpException(
                        HttpErrorCode.HttpRequestMethodInvalid,
                        $"method: invalid discriminant {disc} " +
                        "(expected 0..9).");
            }
        }

        internal void InvokeRequestGetScheme(
            int self, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateMethodOrSchemeRetptr(retptr, memory,
                "wasi:http/types.request.get-scheme", 16);

            var scheme = RequireRequest(self).GetScheme();
            var dest = memory.AsSpan(retptr, 16);
            dest.Clear();
            if (scheme == null)
            {
                // option-disc = none; payload stays zero.
                return;
            }
            dest[0] = 1; // option-disc = some
            // Inner scheme variant at +4..16 (12 bytes).
            WriteSchemeVariantBytes(
                dest.Slice(4, 12), scheme.Value, realloc, memory);
        }

        internal int InvokeRequestSetScheme(
            int self, int optDisc,
            int schemeDisc, int strPtr, int strLen)
        {
            HttpScheme? scheme;
            if (optDisc == 0)
            {
                scheme = null;
            }
            else
            {
                scheme = ReadSchemeFromSlots(
                    schemeDisc, strPtr, strLen);
            }
            RequireRequest(self).SetScheme(scheme);
            return 0;
        }

        /// <summary>Write a flat method variant into a 12-byte
        /// span: disc at +0, string-ptr / string-len at +4..12
        /// (used only when disc=9 / Other).</summary>
        private static void WriteMethodVariantBytes(
            Span<byte> dest, HttpMethod method,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            int disc = (int)method.Kind;
            dest[0] = (byte)disc;
            if (method.Kind == HttpMethod.Tag.Other)
            {
                byte[] bytes = System.Text.Encoding.UTF8
                    .GetBytes(method.OtherName ?? string.Empty);
                int strPtr = bytes.Length > 0
                    ? realloc.Allocate(1, bytes.Length) : 0;
                if (bytes.Length > 0)
                    new ReadOnlySpan<byte>(bytes)
                        .CopyTo(memory.AsSpan(strPtr, bytes.Length));
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(4), strPtr);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(8), bytes.Length);
            }
        }

        private static void WriteSchemeVariantBytes(
            Span<byte> dest, HttpScheme scheme,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest[0] = (byte)scheme.Kind;
            if (scheme.Kind == HttpScheme.Tag.Other)
            {
                byte[] bytes = System.Text.Encoding.UTF8
                    .GetBytes(scheme.OtherName ?? string.Empty);
                int strPtr = bytes.Length > 0
                    ? realloc.Allocate(1, bytes.Length) : 0;
                if (bytes.Length > 0)
                    new ReadOnlySpan<byte>(bytes)
                        .CopyTo(memory.AsSpan(strPtr, bytes.Length));
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(4), strPtr);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(8), bytes.Length);
            }
        }

        public HttpScheme ReadSchemeFromSlots(
            int disc, int strPtr, int strLen)
        {
            switch (disc)
            {
                case 0: return HttpScheme.Http;
                case 1: return HttpScheme.Https;
                case 2: return HttpScheme.Other(
                    ReadGuestUtf8(strPtr, strLen));
                default:
                    throw new HttpException(
                        HttpErrorCode.InternalError,
                        $"scheme: invalid discriminant {disc} " +
                        "(expected 0/1/2 for HTTP/HTTPS/other).");
            }
        }

        private static void ValidateMethodOrSchemeRetptr(
            int retptr,
            Wacs.Core.Runtime.Types.MemoryInstance memory,
            string label, int size)
        {
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + size > memory.Data.Length)
                throw new InvalidOperationException(
                    $"{label}: retptr 0x{retptr:X8} misaligned " +
                    $"or out of range (needs {size} bytes " +
                    "4-aligned).");
        }

        // ---- request option<string> shared dispatch (Slice BB) ------

        /// <summary>Shared body for option&lt;string&gt; getters
        /// on the request resource (path-with-query, authority).
        /// Writes a 12-byte 4-aligned option&lt;string&gt; retptr.
        ///
        /// <para>Layout (12 bytes, align 4):</para>
        /// <code>
        /// +0:    option-disc (u8) + 3 pad
        /// +4..8: string-ptr (i32; 0 when option is none)
        /// +8..12: string-len (i32; 0 when option is none)
        /// </code>
        /// </summary>
        internal void InvokeRequestGetOptionString(
            int self, int retptr, ICabiRealloc realloc,
            Func<IRequest, string?> getter)
        {
            var memory = RequireMemoryForHttp();
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 12 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:http/types.request: option<string> " +
                    $"retptr 0x{retptr:X8} misaligned or out of " +
                    "range (needs 12 bytes 4-aligned).");

            var dest = memory.AsSpan(retptr, 12);
            dest.Clear();
            string? value = getter(RequireRequest(self));
            if (value == null)
            {
                // option-disc = none; payload stays zero.
                return;
            }
            dest[0] = 1; // option-disc = some
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            int strPtr = bytes.Length > 0
                ? realloc.Allocate(1, bytes.Length) : 0;
            if (bytes.Length > 0)
                new ReadOnlySpan<byte>(bytes)
                    .CopyTo(memory.AsSpan(strPtr, bytes.Length));
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), strPtr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(8), bytes.Length);
        }

        /// <summary>Shared body for option&lt;string&gt; setters
        /// on the request resource. Decodes the 3-slot variant
        /// arg (disc + ptr + len), invokes the setter delegate,
        /// returns the result-disc (always 0 since the impl
        /// setters can't fail). The wire return is the i32
        /// discriminant of <c>result</c> (no payload).</summary>
        internal int InvokeRequestSetOptionString(
            int self, int optDisc, int strPtr, int strLen,
            Action<IRequest, string?> setter)
        {
            string? value;
            if (optDisc == 0) value = null;
            else value = ReadGuestUtf8(strPtr, strLen);
            setter(RequireRequest(self), value);
            return 0; // result disc = ok
        }

        // Helper that registers the get/set pair for an
        // option<duration>-shaped timeout property. The lowered
        // signature is (self, i32 is-some, i64 value) on the
        // setter and returns (i32 is-some, i64 value) on the
        // getter via a 16-byte retArea (option<duration>
        // flat-count = 2 = i32 disc + i64 payload, exceeds
        // MAX_FLAT_RESULTS).
        private void BindOptionalDurationTimeout(
            WasmRuntime runtime, string propertyName,
            Func<IRequestOptions, ulong?> getter,
            Action<IRequestOptions, ulong?> setter)
        {
            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    $"[method]request-options.get-{propertyName}"),
                (Action<ExecContext, int, int>)((_, self, retptr) =>
                    InvokeRequestOptionsGetTimeout(
                        self, retptr, getter)));

            runtime.BindHostFunction(
                (HttpTypesModuleName,
                    $"[method]request-options.set-{propertyName}"),
                (Action<ExecContext, int, int, long>)(
                    (_, self, isSome, value) =>
                        InvokeRequestOptionsSetTimeout(
                            self, isSome, value, setter)));
        }

        // ---- wasi:http/types binding bodies (Slice G additions) ----

        /// <summary>Invoke
        /// <c>[method]request-options.get-{connect,first-byte,between-bytes}-timeout</c>'s
        /// body. Writes the option&lt;duration&gt; at retptr:
        /// (i32 disc, i64 value), 16 bytes 8-aligned.</summary>
        public void InvokeRequestOptionsGetTimeout(
            int self, int retptr,
            Func<IRequestOptions, ulong?> getter)
        {
            var opt = RequireRequestOptions(self);
            var memory = RequireMemoryForHttp();
            if (retptr < 0 || (retptr & 0x7) != 0
                || retptr + 16 > memory.Data.Length)
                throw new RequestOptionsException(
                    RequestOptionsError.Other,
                    "request-options.get-timeout: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 16-byte 8-aligned " +
                    "return area.");
            var dest = memory.AsSpan(retptr, 16);
            var value = getter(opt);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(
                    dest.Slice(0), value.HasValue ? 1 : 0);
            // 4-byte tail-pad in the disc slot (option<u64>
            // align = 8 forces the payload to start at +8).
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt64LittleEndian(
                    dest.Slice(8), value ?? 0UL);
        }

        /// <summary>Invoke
        /// <c>[method]request-options.set-{connect,first-byte,between-bytes}-timeout</c>'s
        /// body. The option&lt;duration&gt; param flat-lowers to
        /// (i32 is-some, i64 value).</summary>
        public void InvokeRequestOptionsSetTimeout(
            int self, int isSome, long value,
            Action<IRequestOptions, ulong?> setter)
        {
            var opt = RequireRequestOptions(self);
            setter(opt, isSome != 0
                ? unchecked((ulong)value)
                : (ulong?)null);
        }

        private IRequestOptions RequireRequestOptions(int handle)
        {
            var opt = RequestOptionsHandles.Get(handle);
            if (opt == null)
                throw new RequestOptionsException(
                    RequestOptionsError.Other,
                    $"wasi:http/types.request-options: handle " +
                    $"{handle} is not allocated.");
            return opt;
        }

        private IResponse RequireResponse(int handle)
        {
            var resp = ResponseHandles.Get(handle);
            if (resp == null)
                throw new HttpException(
                    HttpErrorCode.InternalError,
                    $"wasi:http/types.response: handle {handle} " +
                    "is not allocated.");
            return resp;
        }

        // ---- wasi:http/types.fields binding bodies --------------------

        /// <summary>Invoke the
        /// <c>[resource-drop]fields</c> body — release the
        /// handle from <see cref="FieldsHandles"/>. Idempotent:
        /// dropping an absent handle is a no-op (matches the
        /// canon spec's "drop on missing handle is silent").</summary>
        public void InvokeFieldsDrop(int handle)
        {
            FieldsHandles.Drop(handle);
        }

        /// <summary>Invoke <c>[method]fields.has(name)</c> —
        /// looks up the fields instance by self-handle, reads
        /// the name string from guest memory at
        /// <paramref name="namePtr"/>, returns whether the
        /// field exists. Throws when the self handle is
        /// invalid.</summary>
        public bool InvokeFieldsHas(int self, int namePtr, int nameLen)
        {
            var fields = RequireFields(self);
            var name = ReadGuestUtf8(namePtr, nameLen);
            return fields.Has(name);
        }

        /// <summary>Invoke
        /// <c>[method]fields.append(name, value)</c> — reads
        /// both the name (UTF-8 string) and value (byte list)
        /// from guest memory and forwards to
        /// <see cref="IFields.Append"/>. Throws on invalid
        /// handle or on the err path
        /// (<see cref="HeaderException"/> from the impl).</summary>
        public void InvokeFieldsAppend(
            int self, int namePtr, int nameLen,
            int valuePtr, int valueLen)
        {
            var fields = RequireFields(self);
            var name = ReadGuestUtf8(namePtr, nameLen);
            var memory = RequireMemoryForHttp();
            var value = new byte[valueLen];
            if (valueLen > 0)
                memory.AsSpan(valuePtr, valueLen).CopyTo(value);
            fields.Append(name, value);
        }

        /// <summary>Invoke
        /// <c>[method]fields.set(name, list&lt;list&lt;u8&gt;&gt;)</c>.
        /// Reads the name + value list from guest memory,
        /// forwards to <see cref="IFields.Set"/>, writes the
        /// <c>result&lt;_, header-error&gt;</c> at retptr.</summary>
        public void InvokeFieldsSet(
            int self, int namePtr, int nameLen,
            int listPtr, int listCount,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultHeaderErrorRetptr(retptr, memory);
            HeaderException? caught = null;
            try
            {
                var fields = RequireFields(self);
                var name = ReadGuestUtf8(namePtr, nameLen);
                var values = ReadListOfByteLists(listPtr, listCount);
                fields.Set(name, values);
            }
            catch (HeaderException ex) { caught = ex; }
            WriteHeaderErrorResult(
                memory.AsSpan(retptr, ResultHeaderErrorSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke <c>[method]fields.delete(name)</c>.
        /// Forwards to <see cref="IFields.Delete"/>, writes
        /// the <c>result&lt;_, header-error&gt;</c> at retptr.</summary>
        public void InvokeFieldsDelete(
            int self, int namePtr, int nameLen,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultHeaderErrorRetptr(retptr, memory);
            HeaderException? caught = null;
            try
            {
                var fields = RequireFields(self);
                fields.Delete(ReadGuestUtf8(namePtr, nameLen));
            }
            catch (HeaderException ex) { caught = ex; }
            WriteHeaderErrorResult(
                memory.AsSpan(retptr, ResultHeaderErrorSize),
                caught, realloc, memory);
        }

        /// <summary>Invoke <c>[method]fields.clone() -&gt;
        /// own&lt;fields&gt;</c>. Returns a fresh handle bound
        /// to the cloned IFields. No err path (the WIT clone
        /// signature has no result wrapper).</summary>
        public int InvokeFieldsClone(int self)
        {
            var fields = RequireFields(self);
            var cloned = fields.Clone();
            return FieldsHandles.Allocate(cloned);
        }

        // ---- result<_, header-error> wire encoding -------------------
        //
        // Canon-ABI layout for `result<_, header-error>` (size 20,
        // align 4):
        //
        //   retptr+0: result-disc (u8) + 3-byte pad
        //   retptr+4: header-error variant (16 bytes, used when disc=1):
        //     +4: header-error-disc (u8) + 3-byte pad
        //     +8: option<string> (12 bytes — only the `other` case):
        //       +8: option-disc (u8) + 3-byte pad
        //       +12: string-ptr (i32)
        //       +16: string-len (i32)

        private const int ResultHeaderErrorSize = 20;

        private static void ValidateResultHeaderErrorRetptr(
            int retptr, Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + ResultHeaderErrorSize > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:http/types.fields: result<_, header-error> " +
                    $"retptr 0x{retptr:X8} misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). " +
                    "Caller must allocate a 20-byte 4-aligned " +
                    "return area.");
        }

        private static void WriteHeaderErrorResult(
            Span<byte> dest, HeaderException? ex,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest.Clear();
            if (ex == null)
            {
                dest[0] = 0; // result-disc = ok
                return;
            }
            dest[0] = 1; // result-disc = err
            dest[4] = (byte)ex.Code; // header-error-disc (0..4)
            if (ex.Code == HeaderError.Other && ex.OtherPayload != null)
            {
                dest[8] = 1; // option-disc = some
                var bytes = System.Text.Encoding.UTF8
                    .GetBytes(ex.OtherPayload);
                int strPtr = bytes.Length > 0
                    ? realloc.Allocate(align: 1, size: bytes.Length)
                    : 0;
                if (bytes.Length > 0)
                    new ReadOnlySpan<byte>(bytes)
                        .CopyTo(memory.AsSpan(strPtr, bytes.Length));
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(12), strPtr);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(16), bytes.Length);
            }
            // For unit-payload cases (invalid-syntax / forbidden /
            // immutable / size-exceeded), the bytes at +8..20
            // remain zero from the Clear() above — those fields
            // are payload-irrelevant since the disc selects the
            // unit case.
        }

        // Reads list<list<u8>> from guest memory at listPtr.
        // Each outer-list element is 8 bytes (i32 ptr, i32 len);
        // each inner list is a contiguous byte run at its ptr.
        private byte[][] ReadListOfByteLists(int listPtr, int listCount)
        {
            var memory = RequireMemoryForHttp();
            var result = new byte[listCount][];
            for (int i = 0; i < listCount; i++)
            {
                var entrySpan = memory.AsSpan(listPtr + i * 8, 8);
                int ptr = System.Buffers.Binary.BinaryPrimitives
                    .ReadInt32LittleEndian(entrySpan.Slice(0));
                int len = System.Buffers.Binary.BinaryPrimitives
                    .ReadInt32LittleEndian(entrySpan.Slice(4));
                var bytes = new byte[len];
                if (len > 0)
                    memory.AsSpan(ptr, len).CopyTo(bytes);
                result[i] = bytes;
            }
            return result;
        }

        private IFields RequireFields(int handle)
        {
            var fields = FieldsHandles.Get(handle);
            if (fields == null)
                throw new HeaderException(
                    HeaderError.Other,
                    $"wasi:http/types.fields: handle {handle} " +
                    "is not allocated.");
            return fields;
        }

        // ---- fields collection-returning methods (Slice AA) ---------

        /// <summary>Invoke <c>[method]fields.get(name) -&gt;
        /// list&lt;field-value&gt;</c>. The return is not wrapped
        /// in a result variant — empty list means "no header
        /// with this name", and there are no failure cases on
        /// the read path.
        ///
        /// <para>Retptr layout (8 bytes 4-aligned):</para>
        /// <code>
        /// +0: list-ptr (i32 — cabi_realloc'd region of 8 *
        ///     count bytes, 4-aligned)
        /// +4: list-count (i32)
        ///
        /// Each element (8 bytes, 4-aligned) at listPtr + i*8:
        ///   +0: value-ptr (i32)
        ///   +4: value-len (i32)
        /// </code>
        /// </summary>
        public void InvokeFieldsGet(
            int self, int namePtr, int nameLen,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:http/types.fields.get: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    "(needs 8 bytes 4-aligned).");

            var name = ReadGuestUtf8(namePtr, nameLen);
            var values = RequireFields(self).Get(name);
            WriteListOfByteLists(
                memory.AsSpan(retptr, 8), values, realloc, memory);
        }

        /// <summary>Invoke <c>[method]fields.copy-all() -&gt;
        /// list&lt;tuple&lt;field-name, field-value&gt;&gt;</c>.
        /// Returns every (name, value) pair currently in the
        /// fields collection — duplicates are preserved.
        ///
        /// <para>Retptr layout (8 bytes 4-aligned):</para>
        /// <code>
        /// +0: list-ptr (i32 — cabi_realloc'd region of 16 *
        ///     count bytes, 4-aligned)
        /// +4: list-count (i32)
        ///
        /// Each element (16 bytes, 4-aligned) at listPtr + i*16:
        ///   +0:  name-ptr (i32)
        ///   +4:  name-len (i32)
        ///   +8:  value-ptr (i32)
        ///   +12: value-len (i32)
        /// </code>
        /// </summary>
        public void InvokeFieldsCopyAll(
            int self, int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:http/types.fields.copy-all: retptr " +
                    $"0x{retptr:X8} misaligned or out of range " +
                    "(needs 8 bytes 4-aligned).");

            var entries = RequireFields(self).CopyAll();
            int count = entries.Count;
            int listPtr;
            if (count == 0)
            {
                listPtr = 0;
            }
            else
            {
                listPtr = realloc.Allocate(
                    align: 4, size: count * 16);
                var listSpan = memory.AsSpan(listPtr, count * 16);
                listSpan.Clear();
                for (int i = 0; i < count; i++)
                {
                    int off = i * 16;
                    var (name, value) = entries[i];
                    byte[] nameBytes = System.Text.Encoding.UTF8
                        .GetBytes(name ?? string.Empty);
                    int namePtr = nameBytes.Length > 0
                        ? realloc.Allocate(1, nameBytes.Length) : 0;
                    if (nameBytes.Length > 0)
                        new ReadOnlySpan<byte>(nameBytes).CopyTo(
                            memory.AsSpan(namePtr, nameBytes.Length));

                    byte[] valBytes = value ?? Array.Empty<byte>();
                    int valPtr = valBytes.Length > 0
                        ? realloc.Allocate(1, valBytes.Length) : 0;
                    if (valBytes.Length > 0)
                        new ReadOnlySpan<byte>(valBytes).CopyTo(
                            memory.AsSpan(valPtr, valBytes.Length));

                    System.Buffers.Binary.BinaryPrimitives
                        .WriteInt32LittleEndian(listSpan.Slice(off + 0), namePtr);
                    System.Buffers.Binary.BinaryPrimitives
                        .WriteInt32LittleEndian(listSpan.Slice(off + 4), nameBytes.Length);
                    System.Buffers.Binary.BinaryPrimitives
                        .WriteInt32LittleEndian(listSpan.Slice(off + 8), valPtr);
                    System.Buffers.Binary.BinaryPrimitives
                        .WriteInt32LittleEndian(listSpan.Slice(off + 12), valBytes.Length);
                }
            }

            var dest = memory.AsSpan(retptr, 8);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(0), listPtr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), count);
        }

        /// <summary>Invoke
        /// <c>[method]fields.get-and-delete(name) -&gt;
        /// result&lt;list&lt;field-value&gt;,
        /// header-error&gt;</c>. Mutating op that fails with
        /// <c>HeaderError.Immutable</c> if the collection has
        /// been frozen.
        ///
        /// <para>Retptr is 20 bytes 4-aligned — same shape as
        /// <c>result&lt;_, header-error&gt;</c> since the
        /// header-error variant (16 bytes) dominates the
        /// list pair (8 bytes). On ok, list-ptr/len at +4..12;
        /// +12..20 stay zero. On err, header-error variant at
        /// +4..20.</para>
        /// </summary>
        public void InvokeFieldsGetAndDelete(
            int self, int namePtr, int nameLen,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultHeaderErrorRetptr(retptr, memory);
            HeaderException? caught = null;
            IReadOnlyList<byte[]>? values = null;
            try
            {
                var name = ReadGuestUtf8(namePtr, nameLen);
                values = RequireFields(self).GetAndDelete(name);
            }
            catch (HeaderException ex) { caught = ex; }

            var dest = memory.AsSpan(retptr, 20);
            dest.Clear();
            if (caught != null)
            {
                dest[0] = 1; // err disc
                WriteHeaderErrorBytes(
                    dest.Slice(4, 16), caught, realloc, memory);
                return;
            }

            dest[0] = 0; // ok disc
            // Write list pair at +4..12 (same shape as
            // InvokeFieldsGet's body at offset 0, just shifted
            // by +4 for the result-disc).
            WriteListOfByteLists(
                dest.Slice(4, 8), values!, realloc, memory);
        }

        /// <summary>Invoke
        /// <c>[static]fields.from-list(entries: list&lt;tuple
        /// &lt;field-name, field-value&gt;&gt;) -&gt;
        /// result&lt;fields, header-error&gt;</c>. Reads the
        /// guest's tuple list into a CLR list, calls
        /// <c>Fields.FromList</c>, returns a fresh handle on
        /// success or the header-error variant on failure.
        ///
        /// <para>Retptr is 20 bytes 4-aligned (same shape as
        /// result&lt;_, header-error&gt;). On ok, the fresh
        /// fields handle lands at +4 (i32) and +8..20 stay
        /// zero.</para>
        /// </summary>
        public void InvokeFieldsFromList(
            int listPtr, int listLen,
            int retptr, ICabiRealloc realloc)
        {
            var memory = RequireMemoryForHttp();
            ValidateResultHeaderErrorRetptr(retptr, memory);

            // Each entry in the guest list is a tuple<list<u8>,
            // list<u8>> = 16 bytes (4 i32s) at align 4.
            var entries =
                new List<(string Name, byte[] Value)>(listLen);
            HeaderException? caught = null;
            try
            {
                for (int i = 0; i < listLen; i++)
                {
                    int off = listPtr + i * 16;
                    int namePtr = System.Buffers.Binary.BinaryPrimitives
                        .ReadInt32LittleEndian(memory.AsSpan(off, 4));
                    int nameLen = System.Buffers.Binary.BinaryPrimitives
                        .ReadInt32LittleEndian(memory.AsSpan(off + 4, 4));
                    int valPtr = System.Buffers.Binary.BinaryPrimitives
                        .ReadInt32LittleEndian(memory.AsSpan(off + 8, 4));
                    int valLen = System.Buffers.Binary.BinaryPrimitives
                        .ReadInt32LittleEndian(memory.AsSpan(off + 12, 4));

                    string name = ReadGuestUtf8(namePtr, nameLen);
                    byte[] value = valLen > 0
                        ? memory.AsSpan(valPtr, valLen).ToArray()
                        : Array.Empty<byte>();
                    entries.Add((name, value));
                }
            }
            catch (HeaderException ex) { caught = ex; }

            int handle = 0;
            if (caught == null)
            {
                try
                {
                    var fields = Fields.FromList(entries);
                    handle = FieldsHandles.Allocate(fields);
                }
                catch (HeaderException ex) { caught = ex; }
            }

            var dest = memory.AsSpan(retptr, 20);
            dest.Clear();
            if (caught != null)
            {
                dest[0] = 1;
                WriteHeaderErrorBytes(
                    dest.Slice(4, 16), caught, realloc, memory);
                return;
            }
            dest[0] = 0;
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), handle);
        }

        // Shared encoder used by fields.get + fields.get-and-delete
        // ok-path. Writes (list-ptr, list-len) at offset 0 of dest
        // (8 bytes), with the element backing region allocated
        // via cabi_realloc. Each element is 8 bytes
        // (value-ptr + value-len).
        private void WriteListOfByteLists(
            Span<byte> dest, IReadOnlyList<byte[]> values,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            int count = values.Count;
            int listPtr;
            if (count == 0)
            {
                listPtr = 0;
            }
            else
            {
                listPtr = realloc.Allocate(
                    align: 4, size: count * 8);
                var listSpan = memory.AsSpan(listPtr, count * 8);
                listSpan.Clear();
                for (int i = 0; i < count; i++)
                {
                    byte[] valBytes = values[i] ?? Array.Empty<byte>();
                    int valPtr = valBytes.Length > 0
                        ? realloc.Allocate(1, valBytes.Length) : 0;
                    if (valBytes.Length > 0)
                        new ReadOnlySpan<byte>(valBytes).CopyTo(
                            memory.AsSpan(valPtr, valBytes.Length));
                    int off = i * 8;
                    System.Buffers.Binary.BinaryPrimitives
                        .WriteInt32LittleEndian(listSpan.Slice(off), valPtr);
                    System.Buffers.Binary.BinaryPrimitives
                        .WriteInt32LittleEndian(
                            listSpan.Slice(off + 4), valBytes.Length);
                }
            }
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(0), listPtr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), count);
        }

        // Sibling of WriteHeaderErrorResult that writes only the
        // header-error variant (16 bytes) starting at dest[0], so
        // callers writing a `result<X, header-error>` retptr that
        // is NOT shaped as result<_, ...> can route the err arm
        // through here at their +4 payload offset.
        private static void WriteHeaderErrorBytes(
            Span<byte> dest, HeaderException ex,
            ICabiRealloc realloc,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            dest[0] = (byte)ex.Code;
            if (ex.Code == HeaderError.Other && ex.OtherPayload != null)
            {
                // Inner option<string> at +4..16:
                //   +4: option-disc (u8) + 3 pad
                //   +8: string-ptr (i32)
                //   +12: string-len (i32)
                dest[4] = 1;
                var bytes = System.Text.Encoding.UTF8
                    .GetBytes(ex.OtherPayload);
                int strPtr = bytes.Length > 0
                    ? realloc.Allocate(1, bytes.Length) : 0;
                if (bytes.Length > 0)
                    new ReadOnlySpan<byte>(bytes)
                        .CopyTo(memory.AsSpan(strPtr, bytes.Length));
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(8), strPtr);
                System.Buffers.Binary.BinaryPrimitives
                    .WriteInt32LittleEndian(dest.Slice(12), bytes.Length);
            }
        }

        private string ReadGuestUtf8(int ptr, int len)
        {
            var memory = RequireMemoryForHttp();
            if (len == 0) return string.Empty;
            return System.Text.Encoding.UTF8.GetString(
                memory.AsSpan(ptr, len));
        }

        private Wacs.Core.Runtime.Types.MemoryInstance RequireMemoryForHttp()
        {
            var dispatcher = RequireDispatcher();
            return dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:http binding: dispatcher.Memory must be " +
                    "set before any string- or list-marshaling " +
                    "method is invoked.");
        }

        /// <summary>
        /// Invoke <c>wasi:random/random.get-random-bytes</c>'s
        /// host-side delegate body. Allocates
        /// <paramref name="maxLen"/> bytes of guest memory via
        /// <paramref name="realloc"/>, fills it with the
        /// <see cref="IRandom"/>'s output, and writes (ptr, len)
        /// at <paramref name="retptr"/>. Public for test access.
        /// </summary>
        public void InvokeGetRandomBytes(
            IRandom source, ICabiRealloc realloc, ulong maxLen, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (realloc == null) throw new ArgumentNullException(nameof(realloc));
            var bytes = source.GetRandomBytes(maxLen);
            WriteByteList(realloc, retptr, bytes);
        }

        /// <summary>Same as
        /// <see cref="InvokeGetRandomBytes"/> for the insecure
        /// variant.</summary>
        public void InvokeGetInsecureRandomBytes(
            IInsecure source, ICabiRealloc realloc, ulong maxLen, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (realloc == null) throw new ArgumentNullException(nameof(realloc));
            var bytes = source.GetInsecureRandomBytes(maxLen);
            WriteByteList(realloc, retptr, bytes);
        }

        /// <summary>
        /// Invoke <c>wasi:random/insecure-seed.get-insecure-seed</c>'s
        /// host-side delegate body. Writes the two u64s at
        /// <paramref name="retptr"/> (16 bytes, 8-aligned).
        /// </summary>
        public void InvokeGetInsecureSeed(IInsecureSeed source, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:random/insecure-seed: dispatcher.Memory " +
                    "must be set before this import is invoked.");

            if (retptr < 0 || (retptr & 0x7) != 0
                || retptr + 16 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:random/insecure-seed: retptr " +
                    $"0x{retptr:X8} is misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). The caller " +
                    "must allocate a 16-byte 8-aligned return area.");

            var (a, b) = source.GetInsecureSeed();
            var dest = memory.AsSpan(retptr, 16);
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt64LittleEndian(dest.Slice(0), a);
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt64LittleEndian(dest.Slice(8), b);
        }

        // Shared write path for get-*-bytes: allocate guest
        // memory via cabi_realloc, copy the bytes into it,
        // write (ptr, len) into the retArea slot. Spec allows
        // implementations to return fewer bytes than requested
        // (short read) — the wire convention encodes whatever
        // the host actually produced.
        private void WriteByteList(ICabiRealloc realloc, int retptr, byte[] data)
        {
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:random/get-*-bytes: dispatcher.Memory must " +
                    "be set before this import is invoked.");
            if (retptr < 0 || (retptr & 0x3) != 0
                || retptr + 8 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:random/get-*-bytes: retptr " +
                    $"0x{retptr:X8} is misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). The caller " +
                    "must allocate an 8-byte 4-aligned return area.");

            int ptr = data.Length == 0
                ? 0
                : realloc.Allocate(align: 1, size: data.Length);
            if (data.Length > 0)
                new ReadOnlySpan<byte>(data)
                    .CopyTo(memory.AsSpan(ptr, data.Length));
            var dest = memory.AsSpan(retptr, 8);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(0), ptr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(dest.Slice(4), data.Length);
        }

        // wasi:cli/environment — canonical-ABI lowering helpers.
        //
        // The three returns share a pattern: allocate the
        // payload(s) via cabi_realloc, write payload bytes into
        // guest memory, then write the outer (ptr, len) or
        // option-discriminator at the retptr the canon lower
        // reserved. Memory must be re-fetched after each
        // cabi_realloc call because realloc can grow the
        // underlying buffer.

        /// <summary>
        /// Invoke <c>wasi:cli/environment.get-environment</c>.
        /// retArea is 8 bytes: (list-ptr i32, list-len i32).
        /// Each element occupies 16 bytes:
        /// (k-ptr, k-len, v-ptr, v-len), align 4.
        /// </summary>
        public void InvokeGetEnvironment(
            IEnvironment source, ICabiRealloc realloc, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (realloc == null) throw new ArgumentNullException(nameof(realloc));
            var pairs = source.GetEnvironment()
                ?? Array.Empty<(string, string)>();

            int count = pairs.Length;
            int arrayPtr = count == 0
                ? 0
                : realloc.Allocate(align: 4, size: count * 16);
            for (int i = 0; i < count; i++)
            {
                var (kPtr, kLen) = AllocateUtf8(realloc, pairs[i].Item1);
                var (vPtr, vLen) = AllocateUtf8(realloc, pairs[i].Item2);
                var mem = RequireDispatcherMemory();
                int slot = arrayPtr + i * 16;
                WriteI32LE(mem, slot + 0,  kPtr);
                WriteI32LE(mem, slot + 4,  kLen);
                WriteI32LE(mem, slot + 8,  vPtr);
                WriteI32LE(mem, slot + 12, vLen);
            }
            var memEnd = RequireDispatcherMemory();
            ValidateRetArea(retptr, 8, align: 4, memEnd);
            WriteI32LE(memEnd, retptr + 0, arrayPtr);
            WriteI32LE(memEnd, retptr + 4, count);
        }

        /// <summary>
        /// Invoke <c>wasi:cli/environment.get-arguments</c>.
        /// retArea is 8 bytes: (list-ptr, list-len). Each
        /// element 8 bytes: (str-ptr, str-len).
        /// </summary>
        public void InvokeGetArguments(
            IEnvironment source, ICabiRealloc realloc, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (realloc == null) throw new ArgumentNullException(nameof(realloc));
            var args = source.GetArguments() ?? Array.Empty<string>();

            int count = args.Length;
            int arrayPtr = count == 0
                ? 0
                : realloc.Allocate(align: 4, size: count * 8);
            for (int i = 0; i < count; i++)
            {
                var (ptr, len) = AllocateUtf8(realloc, args[i]);
                var mem = RequireDispatcherMemory();
                int slot = arrayPtr + i * 8;
                WriteI32LE(mem, slot + 0, ptr);
                WriteI32LE(mem, slot + 4, len);
            }
            var memEnd = RequireDispatcherMemory();
            ValidateRetArea(retptr, 8, align: 4, memEnd);
            WriteI32LE(memEnd, retptr + 0, arrayPtr);
            WriteI32LE(memEnd, retptr + 4, count);
        }

        /// <summary>
        /// Invoke <c>wasi:cli/environment.get-initial-cwd</c>.
        /// retArea is 12 bytes: disc(u8) + 3 pad + (ptr, len).
        /// None: disc=0, payload zeros. Some: disc=1 + payload.
        /// </summary>
        public void InvokeGetInitialCwd(
            IEnvironment source, ICabiRealloc realloc, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (realloc == null) throw new ArgumentNullException(nameof(realloc));
            var cwd = source.GetInitialCwd();

            if (cwd == null)
            {
                var memNone = RequireDispatcherMemory();
                ValidateRetArea(retptr, 12, align: 4, memNone);
                var span = memNone.AsSpan(retptr, 12);
                span.Clear();
                return;
            }

            var (ptr, len) = AllocateUtf8(realloc, cwd);
            var mem = RequireDispatcherMemory();
            ValidateRetArea(retptr, 12, align: 4, mem);
            var dst = mem.AsSpan(retptr, 12);
            dst[0] = 1;
            dst.Slice(1, 3).Clear();
            WriteI32LE(mem, retptr + 4, ptr);
            WriteI32LE(mem, retptr + 8, len);
        }

        // Allocate a UTF-8 buffer in guest memory and copy the
        // bytes in. Returns (ptr, byte_count). Empty string
        // returns (0, 0). Re-fetches dispatcher.Memory after
        // realloc since it can grow the underlying buffer.
        private (int ptr, int len) AllocateUtf8(
            ICabiRealloc realloc, string value)
        {
            if (string.IsNullOrEmpty(value)) return (0, 0);
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            int ptr = realloc.Allocate(align: 1, size: bytes.Length);
            var mem = RequireDispatcherMemory();
            new ReadOnlySpan<byte>(bytes).CopyTo(mem.AsSpan(ptr, bytes.Length));
            return (ptr, bytes.Length);
        }

        private Wacs.Core.Runtime.Types.MemoryInstance RequireDispatcherMemory()
        {
            var dispatcher = RequireDispatcher();
            return dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:cli/environment: dispatcher.Memory must " +
                    "be set before this import is invoked.");
        }

        private static void ValidateRetArea(
            int retptr, int size, int align,
            Wacs.Core.Runtime.Types.MemoryInstance memory)
        {
            int mask = align - 1;
            if (retptr < 0 || (retptr & mask) != 0
                || retptr + size > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:cli/environment: retptr " +
                    $"0x{retptr:X8} is misaligned (align={align}) " +
                    $"or out of range (size={size}, " +
                    $"memory={memory.Data.Length}).");
        }

        private static void WriteI32LE(
            Wacs.Core.Runtime.Types.MemoryInstance memory,
            int offset, int value)
        {
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(memory.AsSpan(offset, 4), value);
        }

        // Span readers — for unpacking canonical-ABI param
        // structs at params_ptr in async-lowered method
        // bindings. Endianness is little-endian (wasm spec).
        private static int ReadI32(System.ReadOnlySpan<byte> span, int offset) =>
            System.Buffers.Binary.BinaryPrimitives
                .ReadInt32LittleEndian(span.Slice(offset));

        private static long ReadI64(System.ReadOnlySpan<byte> span, int offset) =>
            System.Buffers.Binary.BinaryPrimitives
                .ReadInt64LittleEndian(span.Slice(offset));

        // new-timestamp variant (24 bytes, 8-aligned):
        //   disc:u8 @ +0 (case: 0=no-change, 1=now, 2=timestamp)
        //   7-byte pad
        //   seconds:s64 @ +8  (used only for case 2)
        //   nanoseconds:u32 @ +16  (used only for case 2)
        //   4-byte tail pad
        // Returns (disc, seconds, nanoseconds) — caller hands
        // the trio to ReadNewTimestampFromSlots which the
        // existing sync slot binding already uses.
        private static (int Disc, long Seconds, int Nanoseconds)
            ReadNewTimestamp(System.ReadOnlySpan<byte> span, int offset)
        {
            int disc = span[offset];
            long seconds = ReadI64(span, offset + 8);
            int nanos = ReadI32(span, offset + 16);
            return (disc, seconds, nanos);
        }

        // Write canonical-ABI `option<resource>` = None at
        // retptr. Layout: 8 bytes (disc:u8 + 3 pad + handle:u32),
        // align 4. None means disc=0; payload doesn't matter but
        // we zero it for hygiene.
        private void WriteOptionResourceNone(int retptr)
        {
            var mem = RequireDispatcherMemory();
            ValidateRetArea(retptr, 8, align: 4, mem);
            mem.AsSpan(retptr, 8).Clear();
        }

        /// <summary>
        /// Invoke <c>wasi:clocks/system-clock.now</c>'s host-side
        /// delegate body. Writes the
        /// <see cref="Wacs.WASI.Preview3.Clocks.Instant"/> at
        /// <paramref name="retptr"/> per canon-ABI layout
        /// (16 bytes, 8-aligned: s64 seconds at +0, u32 nanoseconds
        /// at +8, 4-byte tail pad). Public for test access; the
        /// runtime calls this via the bound delegate above.
        /// </summary>
        public void InvokeSystemClockNow(ISystemClock source, int retptr)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var dispatcher = RequireDispatcher();
            var memory = dispatcher.Memory
                ?? throw new InvalidOperationException(
                    "wasi:clocks/system-clock.now: dispatcher.Memory " +
                    "must be set before this import is invoked.");

            // record { seconds: s64, nanoseconds: u32 }, align 8,
            // size 16. retptr must be 8-aligned to satisfy the
            // s64 field's alignment.
            if (retptr < 0 || (retptr & 0x7) != 0
                || retptr + 16 > memory.Data.Length)
                throw new InvalidOperationException(
                    "wasi:clocks/system-clock.now: retptr " +
                    $"0x{retptr:X8} is misaligned or out of range " +
                    $"(memory size = {memory.Data.Length}). The caller " +
                    "must allocate a 16-byte 8-aligned return area.");

            var instant = source.Now();
            var dest = memory.AsSpan(retptr, 16);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt64LittleEndian(dest.Slice(0), instant.Seconds);
            System.Buffers.Binary.BinaryPrimitives
                .WriteUInt32LittleEndian(dest.Slice(8), instant.Nanoseconds);
            // Bytes 12..16 are tail padding; leave as-is (canon
            // ABI doesn't require zeroing).
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

        // Canonical-ABI packed return for an [async-lower]<func>
        // import that completed synchronously. Current
        // wit-bindgen (commit 85d10eb, used by the wasip3
        // fixtures) decodes `packed & 0xf` as the status code
        // and `packed >> 4` as the subtask handle:
        //
        //   STATUS_STARTING            = 0
        //   STATUS_STARTED             = 1
        //   STATUS_RETURNED            = 2
        //   STATUS_STARTED_CANCELLED   = 3
        //   STATUS_RETURNED_CANCELLED  = 4
        //
        // For a sync-completed call with no in-flight subtask,
        // the packed value is just `STATUS_RETURNED = 2`
        // (upper 28 bits zero → no subtask handle).
        //
        // Note: earlier wit-bindgen-rt versions (e.g. 0.41 on
        // crates.io) used `status >> 30` packing. The version
        // shipping in the wasip3 fixture builds uses `& 0xf` —
        // verified against `subtask.rs` at commit 85d10eb.
        private const int CanonAsyncStatusReturnedPacked = 2;

        /// <summary>Wire-level WASI module name for stdout.</summary>
        public const string StdoutModuleName =
            "wasi:cli/stdout@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for stderr.</summary>
        public const string StderrModuleName =
            "wasi:cli/stderr@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for stdin.</summary>
        public const string StdinModuleName =
            "wasi:cli/stdin@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the CLI exit interface.</summary>
        public const string CliExitModuleName =
            "wasi:cli/exit@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the CLI environment interface.</summary>
        public const string CliEnvironmentModuleName =
            "wasi:cli/environment@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the CLI terminal-stdin interface.</summary>
        public const string CliTerminalStdinModuleName =
            "wasi:cli/terminal-stdin@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the CLI terminal-stdout interface.</summary>
        public const string CliTerminalStdoutModuleName =
            "wasi:cli/terminal-stdout@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the CLI terminal-stderr interface.</summary>
        public const string CliTerminalStderrModuleName =
            "wasi:cli/terminal-stderr@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the monotonic clock.</summary>
        public const string MonotonicClockModuleName =
            "wasi:clocks/monotonic-clock@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the system clock.</summary>
        public const string SystemClockModuleName =
            "wasi:clocks/system-clock@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for cryptographic random.</summary>
        public const string RandomModuleName =
            "wasi:random/random@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for insecure random.</summary>
        public const string InsecureRandomModuleName =
            "wasi:random/insecure@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the insecure-seed
        /// 128-bit DoS-protection seed source.</summary>
        public const string InsecureSeedModuleName =
            "wasi:random/insecure-seed@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the
        /// <c>wasi:http/types</c> interface (covers fields,
        /// request, response, request-options resources +
        /// shared types).</summary>
        public const string HttpTypesModuleName =
            "wasi:http/types@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for
        /// <c>wasi:http/client</c> (outbound HTTP).</summary>
        public const string HttpClientModuleName =
            "wasi:http/client@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for
        /// <c>wasi:http/handler</c> (inbound HTTP — guest
        /// provides this when serving).</summary>
        public const string HttpHandlerModuleName =
            "wasi:http/handler@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the
        /// <c>wasi:filesystem/types</c> interface (descriptor
        /// resource + shared types).</summary>
        public const string FilesystemTypesModuleName =
            "wasi:filesystem/types@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the
        /// <c>wasi:filesystem/preopens</c> interface
        /// (get-directories).</summary>
        public const string FilesystemPreopensModuleName =
            "wasi:filesystem/preopens@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the
        /// <c>wasi:sockets/types</c> interface (tcp-socket +
        /// udp-socket resources + shared types).</summary>
        public const string SocketsTypesModuleName =
            "wasi:sockets/types@0.3.0-rc-2026-03-15";

        /// <summary>Wire-level WASI module name for the
        /// <c>wasi:sockets/ip-name-lookup</c> interface.</summary>
        public const string IpNameLookupModuleName =
            "wasi:sockets/ip-name-lookup@0.3.0-rc-2026-03-15";
    }

    /// <summary>Fluent builder for <see cref="WasiPreview3Host"/>.</summary>
    public sealed class WasiPreview3HostBuilder
    {
        public IStdin? Stdin { get; set; }
        public IStdout? Stdout { get; set; }
        public IStderr? Stderr { get; set; }
        public IMonotonicClock? MonotonicClock { get; set; }
        public ISystemClock? SystemClock { get; set; }
        public IRandom? Random { get; set; }
        public IInsecure? InsecureRandom { get; set; }
        public IInsecureSeed? InsecureSeed { get; set; }
        public IExit? Exit { get; set; }
        public IEnvironment? Environment { get; set; }
        public IPreopens? Preopens { get; set; }
        public IIpNameLookup? IpNameLookup { get; set; }
        public IClient? HttpClient { get; set; }
        public IHandler? HttpHandler { get; set; }
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
